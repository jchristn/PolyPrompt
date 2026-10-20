namespace Test.Shared
{
    using System.Net;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using PolyPrompt.Auth;
    using PolyPrompt.Clients;
    using PolyPrompt.Helpers;
    using PolyPrompt.Models;
    using PolyPrompt.Wire;


    /// <summary>Real HTTP regressions for native assistant continuation state, shared with Native AOT.</summary>
    public static partial class LocalToolTurnCases
    {
        private static readonly string[] Providers = { "ollama", "openai", "anthropic", "bedrock", "gemini", "cohere" };
        private static CompletionClientBase Client(string provider, string endpoint, HttpClient? http = null) => provider switch
        {
            "ollama" => new OllamaCompletionClient(endpoint),
            "ollamacloud" => new OllamaCloudCompletionClient("fixture", httpClient: http),
            "azure" => new AzureOpenAiCompletionClient(endpoint, "fixture-model", "fixture"),
            "vertex" => new VertexAiCompletionClient("fixture-project", "us-central1", new StaticTokenCredential("fixture"), endpoint),
            "openai" => new OpenAiCompletionClient(endpoint: endpoint, apiKey: "fixture"),
            "anthropic" => new AnthropicCompletionClient(endpoint: endpoint, apiKey: "fixture"),
            "bedrock" => new BedrockCompletionClient(new StaticAwsCredential("fixture", "fixture", "us-east-1"), "us-east-1", endpoint),
            "gemini" => new GeminiCompletionClient(endpoint: endpoint, apiKey: "fixture"),
            "cohere" => new CohereCompletionClient(endpoint: endpoint, apiKey: "fixture"),
            _ => throw new ArgumentException(provider)
        };
        private static ToolChatRequest Request(string provider, bool plain = false) => new()
        {
            Messages = new() { ChatMessage.User("turn-state-fixture:" + provider + (plain ? ":plain" : "")) },
            Tools = new() { ToolDefinition.Function("read_file", "Read", new() { ["type"] = "object" }) }
        };
        public static async Task RunAsync(string provider, bool stream, CancellationToken token, List<string>? capturedRequests = null)
        {
            using var server = LocalOpenAiTestServer.Start();
            using var http = new HttpClient(new CloudTransport(server.Endpoint));
            using var client = Client(provider, server.Endpoint, http);
            provider = provider switch { "ollamacloud" => "ollama", "azure" => "openai", "vertex" => "gemini", _ => provider };
            ToolChatRequest request = Request(provider);
            for (int turn = 0; turn < 3; turn++)
            {
                ChatMessage assistant;
                if (stream)
                {
                    var response = await client.ToolChatStreamingAsync(request, token);
                    await foreach (var _ in response.Chunks.WithCancellation(token)) { }
                    SharedAssert.True(response.Success, response.Error ?? "Stream failed");
                    assistant = response.ToAssistantMessage();
                }
                else
                {
                    var response = await client.ToolChatAsync(request, token);
                    SharedAssert.True(response.Success, response.Error ?? "Request failed");
                    assistant = response.ToAssistantMessage();
                }
                if (turn < 2) SharedAssert.Equal(2, assistant.ToolCalls.Count, "Parallel calls survive");
                SharedAssert.False((assistant.Content ?? "").Contains("thought"), "Thinking stays out of visible text");
                // Test the public generated metadata, not reflection serialization or only the response property.
                string json = JsonSerializer.Serialize(assistant, PolyPromptJsonContext.Default.ChatMessage);
                assistant = JsonSerializer.Deserialize(json, PolyPromptJsonContext.Default.ChatMessage)!;
                request.Messages.Add(assistant);
                foreach (ToolCall call in assistant.ToolCalls)
                    request.Messages.Add(ChatMessage.ToolResult(call.Id, call.Name, "result"));
            }
            for (int requestIndex = 1; requestIndex <= 2; requestIndex++)
            {
                JsonObject body = JsonNode.Parse(server.RequestBodies[requestIndex])!.AsObject();
                var history = History(body, provider).Where(IsAssistant).ToArray();
                SharedAssert.Equal(requestIndex, history.Length, "Each assistant turn appears once");
                for (int turn = 0; turn < history.Length; turn++)
                {
                    string expected = Thought(turn);
                    JsonNode h = history[turn]!;
                    switch (provider)
                    {
                        case "ollama": SharedAssert.Equal(expected, h["thinking"]!.GetValue<string>(), "Raw thinking is exact"); break;
                        case "openai": SharedAssert.Equal(expected, h["reasoning_content"]!.GetValue<string>(), "Raw reasoning is exact"); break;
                        case "anthropic":
                            SharedAssert.Equal(expected, h["content"]![0]!["thinking"]!.GetValue<string>(), "Signed text exact");
                            SharedAssert.Equal("sig" + turn, h["content"]![0]!["signature"]!.GetValue<string>(), "Fragmented signature exact");
                            SharedAssert.Equal("opaque", h["content"]![1]!["data"]!.GetValue<string>(), "Redacted block retained"); break;
                        case "bedrock":
                            SharedAssert.Equal(expected, h["content"]![0]!["reasoningContent"]!["reasoningText"]!["text"]!.GetValue<string>(), "Signed text exact");
                            SharedAssert.Equal("sig" + turn, h["content"]![0]!["reasoningContent"]!["reasoningText"]!["signature"]!.GetValue<string>(), "Signature exact");
                            SharedAssert.Equal("AQIDBA==", h["content"]![1]!["reasoningContent"]!["redactedContent"]!.GetValue<string>(), "Binary redacted fragments retained"); break;
                        case "gemini":
                            SharedAssert.Equal(expected, h["parts"]![0]!["text"]!.GetValue<string>(), "Thought exact");
                            SharedAssert.Equal("text-sig", h["parts"]![1]!["thoughtSignature"]!.GetValue<string>(), "Non-function signature retained"); break;
                        case "cohere":
                            SharedAssert.Equal("plan" + turn, h["tool_plan"]!.GetValue<string>(), "Tool plan retained separately");
                            SharedAssert.Equal(expected, h["content"]![0]!["thinking"]!.GetValue<string>(), "Thinking block exact"); break;
                    }
                }
                if (provider == "ollama")
                    SharedAssert.True(History(body, provider).Where(n => n?["role"]?.GetValue<string>() == "tool").All(n => n!["tool_name"]!.GetValue<string>() == "read_file"), "Native tool result names retained");
            }
            var noToolsRequest = Request(provider);
            noToolsRequest.Messages[0].Content += ":no-tools";
            ChatMessage noToolsAssistant;
            if (stream)
            {
                var response = await client.ToolChatStreamingAsync(noToolsRequest, token);
                await foreach (var _ in response.Chunks.WithCancellation(token)) { }
                SharedAssert.True(response.Success, response.Error ?? "Reasoning-only stream failed");
                noToolsAssistant = response.ToAssistantMessage();
            }
            else
            {
                var response = await client.ToolChatAsync(noToolsRequest, token);
                SharedAssert.True(response.Success, response.Error ?? "Reasoning-only turn failed");
                noToolsAssistant = response.ToAssistantMessage();
            }
            SharedAssert.Equal(0, noToolsAssistant.ToolCalls.Count, "Reasoning without tools is supported");
            noToolsRequest.Messages.Add(noToolsAssistant);
            noToolsRequest.Messages.Add(ChatMessage.User("next"));
            SharedAssert.True((await client.ToolChatAsync(noToolsRequest, token)).Success, "Reasoning-only follow-up succeeds");
            SharedAssert.True(server.RequestBodies.Last().Contains("thought 0"), "Reasoning survives even without tool calls");
            // Plain assistant turns with no thinking or tools remain valid and acquire no stale state.
            var plain = Request(provider, true);
            ChatMessage plainAssistant;
            if (stream)
            {
                var response = await client.ToolChatStreamingAsync(plain, token);
                await foreach (var _ in response.Chunks.WithCancellation(token)) { }
                SharedAssert.True(response.Success, response.Error ?? "Plain stream failed"); plainAssistant = response.ToAssistantMessage();
            }
            else { var response = await client.ToolChatAsync(plain, token); SharedAssert.True(response.Success, response.Error ?? "Plain failed"); plainAssistant = response.ToAssistantMessage(); }
            plain.Messages.Add(plainAssistant);
            var next = await client.ToolChatAsync(plain, token);
            SharedAssert.True(next.Success, next.Error ?? "Plain follow-up failed");
            string outgoing = server.RequestBodies.Last();
            SharedAssert.False(outgoing.Contains("sig0") || outgoing.Contains("thought 0"), "State is local to its own turn");
            capturedRequests?.AddRange(server.RequestBodies);
        }
        private static async Task IsolationAsync(CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start();
            using var otherServer = LocalOpenAiTestServer.Start();
            using var openaiSource = Client("openai", server.Endpoint);
            using var openaiDestination = Client("openai", otherServer.Endpoint);
            var scopedResponse = await openaiSource.ToolChatAsync(Request("openai"), token);
            var scopedRequest = Request("openai", true);
            scopedRequest.Messages.Add(scopedResponse.ToAssistantMessage());
            foreach (var call in scopedResponse.ToolCalls) scopedRequest.Messages.Add(ChatMessage.ToolResult(call.Id, call.Name, "result"));
            SharedAssert.True((await openaiDestination.ToolChatAsync(scopedRequest, token)).Success, "Changing compatible endpoints succeeds");
            SharedAssert.False(otherServer.RequestBodies.Last().Contains("reasoning_content"), "Reasoning extensions stay at the endpoint that returned them");
            using var xai = new XAiCompletionClient(LocalXAiRoutes.Key, server.Endpoint);
            var xaiResponse = await xai.ToolChatAsync(new ToolChatRequest { Messages = new() { ChatMessage.User("hello") } }, token);
            SharedAssert.True(xaiResponse.Success, xaiResponse.Error ?? "xAI source failed");
            foreach (string source in Providers)
            {
                using var from = Client(source, server.Endpoint);
                var response = await from.ToolChatAsync(Request(source), token);
                SharedAssert.True(response.Success, response.Error ?? "Source failed");
                var xaiRequest = new ToolChatRequest { Messages = new() { ChatMessage.User("hello"), response.ToAssistantMessage() } };
                foreach (var call in response.ToolCalls) xaiRequest.Messages.Add(ChatMessage.ToolResult(call.Id, call.Name, "result"));
                var xaiFollowup = await xai.ToolChatAsync(xaiRequest, token);
                SharedAssert.True(xaiFollowup.Success, xaiFollowup.Error ?? "xAI ignores foreign envelope");
                SharedAssert.False(server.RequestBodies.Last().Contains("thought 0"), "Foreign state omitted by xAI");
                var fromXai = Request(source, true);
                fromXai.Messages.Add(xaiResponse.ToAssistantMessage());
                var nativeFollowup = await from.ToolChatAsync(fromXai, token);
                SharedAssert.True(nativeFollowup.Success, nativeFollowup.Error ?? "Native provider ignores xAI array");
                SharedAssert.False(server.RequestBodies.Last().Contains("opaque-state"), "xAI native output stays isolated");
                foreach (string destination in Providers.Where(p => p != source))
                {
                    using var to = Client(destination, server.Endpoint);
                    var request = Request(destination, true);
                    request.Messages.Add(response.ToAssistantMessage());
                    foreach (var call in response.ToolCalls) request.Messages.Add(ChatMessage.ToolResult(call.Id, call.Name, "result"));
                    var followup = await to.ToolChatAsync(request, token);
                    SharedAssert.True(followup.Success, followup.Error ?? "Cross-protocol follow-up failed");
                    SharedAssert.False(server.RequestBodies.Last().Contains("thought 0"), "Foreign reasoning omitted");
                }
            }
        }
        private sealed class CloudTransport : DelegatingHandler
        {
            private readonly string endpoint;
            internal CloudTransport(string endpoint) : base(new HttpClientHandler()) { this.endpoint = endpoint; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                SharedAssert.Equal("ollama.com", request.RequestUri!.Host, "Cloud native destination");
                SharedAssert.Equal("Bearer", request.Headers.Authorization?.Scheme, "Cloud bearer auth");
                request.RequestUri = new Uri(endpoint + request.RequestUri.PathAndQuery);
                return base.SendAsync(request, token);
            }
        }
        private static string Thought(int turn) => " thought " + turn + " \n end ";
        private static bool IsAssistant(JsonNode? n) => n?["role"]?.GetValue<string>() is "assistant" or "model";
        private static JsonArray History(JsonObject body, string provider) => body[provider == "gemini" ? "contents" : "messages"]!.AsArray();

        internal static async Task<bool> TryHandleAsync(HttpListenerContext context, string path, string raw)
        {
            if (!raw.Contains("turn-state-fixture:")) return false;
            JsonObject body = JsonNode.Parse(raw)!.AsObject();
            string provider = Providers.First(p => raw.Contains("turn-state-fixture:" + p));
            int turn = History(body, provider).Count(IsAssistant);
            bool plain = raw.Contains(":plain") || turn >= 2;
            bool noTools = raw.Contains(":no-tools");
            JsonArray content = Content(provider, turn, plain, noTools);
            bool stream = body["stream"]?.GetValue<bool>() == true || path.Contains("Stream") || path.EndsWith("converse-stream") || path.Contains("streamGenerateContent");
            string text;
            if (stream) text = Streaming(provider, content, turn, plain, noTools);
            else text = provider switch
            {
                "ollama" => new JsonObject { ["message"] = StringMessage(provider, turn, plain, noTools), ["done"] = true }.ToJsonString(),
                "openai" => new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["message"] = StringMessage(provider, turn, plain, noTools), ["finish_reason"] = "stop" }) }.ToJsonString(),
                "anthropic" => new JsonObject { ["content"] = content, ["stop_reason"] = "tool_use" }.ToJsonString(),
                "bedrock" => new JsonObject { ["output"] = new JsonObject { ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = content } }, ["stopReason"] = "tool_use" }.ToJsonString(),
                "gemini" => new JsonObject { ["candidates"] = new JsonArray(new JsonObject { ["content"] = new JsonObject { ["parts"] = content }, ["finishReason"] = "STOP" }) }.ToJsonString(),
                _ => CohereMessage(content, turn, plain, noTools).ToJsonString()
            };
            byte[] bytes = provider == "bedrock" && stream ? Convert.FromBase64String(text) : Encoding.UTF8.GetBytes(text);
            context.Response.StatusCode = 200;
            context.Response.ContentType = stream ? provider == "bedrock" ? "application/vnd.amazon.eventstream" : provider == "ollama" ? "application/x-ndjson" : "text/event-stream" : "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close(); return true;
        }
        private static JsonObject StringMessage(string provider, int turn, bool plain, bool noTools = false)
        {
            var message = new JsonObject { ["role"] = "assistant", ["content"] = "answer" };
            if (!plain)
            {
                message[provider == "ollama" ? "thinking" : "reasoning_content"] = Thought(turn);
                var calls = new JsonArray();
                for (int i = 0; i < 2; i++) calls.Add(JsonNode.Parse(provider == "ollama" ? """{"function":{"name":"read_file","arguments":{"path":"test"}}}""" : """{"id":"call","type":"function","function":{"name":"read_file","arguments":"{\"path\":\"test\"}"}}"""));
                if (provider != "ollama") for (int i = 0; i < calls.Count; i++) calls[i]!["id"] = "call" + i;
                if (!noTools) message["tool_calls"] = calls;
            }
            return message;
        }
        private static JsonArray Content(string provider, int turn, bool plain, bool noTools)
        {
            var blocks = new JsonArray();
            if (!plain) blocks.Add((JsonNode)(provider switch
            {
                "anthropic" => new JsonObject { ["type"] = "thinking", ["thinking"] = Thought(turn), ["signature"] = "sig" + turn },
                "bedrock" => new JsonObject { ["reasoningContent"] = new JsonObject { ["reasoningText"] = new JsonObject { ["text"] = Thought(turn), ["signature"] = "sig" + turn } } },
                "gemini" => new JsonObject { ["thought"] = true, ["text"] = Thought(turn) },
                _ => new JsonObject { ["type"] = "thinking", ["thinking"] = Thought(turn) }
            }));
            if (!plain && provider == "anthropic") blocks.Add(JsonNode.Parse("""{"type":"redacted_thinking","data":"opaque"}"""));
            if (!plain && provider == "bedrock") blocks.Add(JsonNode.Parse("""{"reasoningContent":{"redactedContent":"AQIDBA=="}}"""));
            var answer = new JsonObject { ["text"] = "answer" };
            if (provider is "anthropic" or "cohere") answer["type"] = "text";
            if (provider == "gemini" && !plain) answer["thoughtSignature"] = "text-sig";
            blocks.Add((JsonNode)answer);
            if (!plain && !noTools && provider is "anthropic" or "bedrock" or "gemini")
                for (int i = 0; i < 2; i++) blocks.Add((JsonNode)(provider switch
                {
                    "anthropic" => new JsonObject { ["type"] = "tool_use", ["id"] = "call" + i, ["name"] = "read_file", ["input"] = new JsonObject { ["path"] = "test" } },
                    "bedrock" => new JsonObject { ["toolUse"] = new JsonObject { ["toolUseId"] = "call" + i, ["name"] = "read_file", ["input"] = new JsonObject { ["path"] = "test" } } },
                    _ => new JsonObject { ["functionCall"] = new JsonObject { ["id"] = "call" + i, ["name"] = "read_file", ["args"] = new JsonObject { ["path"] = "test" } }, ["thoughtSignature"] = "call-sig" + i }
                }));
            return blocks;
        }
        private static JsonObject CohereMessage(JsonArray content, int turn, bool plain, bool noTools)
        {
            var message = StringMessage("openai", turn, plain, noTools); message.Remove("reasoning_content"); message["content"] = content;
            if (!plain) message["tool_plan"] = "plan" + turn;
            return new JsonObject { ["message"] = message, ["finish_reason"] = "COMPLETE" };
        }
        private static string Streaming(string provider, JsonArray content, int turn, bool plain, bool noTools)
        {
            var events = new List<(string, JsonObject)>();
            void E(string type, JsonObject payload) { if (provider != "bedrock") payload["type"] = type; events.Add((type, payload)); }
            if (provider is "ollama" or "openai")
            {
                var message = StringMessage(provider, turn, plain, noTools);
                string key = provider == "ollama" ? "thinking" : "reasoning_content";
                if (!plain)
                {
                    string thought = message[key]!.GetValue<string>(); message.Remove(key);
                    foreach (string fragment in new[] { thought[..5], thought[5..] })
                        events.Add(("", provider == "ollama" ? new JsonObject { ["message"] = new JsonObject { [key] = fragment } } : new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["delta"] = new JsonObject { [key] = fragment } }) }));
                }
                if (provider == "ollama") events.Add(("", new JsonObject { ["message"] = message, ["done"] = true }));
                else
                {
                    if (message["tool_calls"] is JsonArray calls) for (int i = 0; i < calls.Count; i++) calls[i]!["index"] = i;
                    events.Add(("", new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["delta"] = message, ["finish_reason"] = "stop" }) }));
                }
            }
            else if (provider == "gemini")
            {
                foreach (var part in content) events.Add(("", new JsonObject { ["candidates"] = new JsonArray(new JsonObject { ["content"] = new JsonObject { ["parts"] = new JsonArray(part!.DeepClone()) } }) }));
            }
            else if (provider == "cohere")
            {
                if (!plain) foreach (string piece in new[] { "pl", "an" + turn }) E("tool-plan-delta", new JsonObject { ["delta"] = new JsonObject { ["message"] = new JsonObject { ["tool_plan"] = piece } } });
                for (int i = 0; i < content.Count; i++)
                {
                    var block = content[i]!.AsObject(); string key = block["type"]!.GetValue<string>() == "text" ? "text" : "thinking";
                    string value = block[key]!.GetValue<string>();
                    foreach (string piece in new[] { value[..2], value[2..] }) E("content-delta", new JsonObject { ["index"] = i, ["delta"] = new JsonObject { ["message"] = new JsonObject { ["content"] = new JsonObject { [key] = piece } } } });
                }
                if (!plain && !noTools) for (int i = 0; i < 2; i++) E("tool-call-start", new JsonObject { ["index"] = i, ["delta"] = new JsonObject { ["message"] = new JsonObject { ["tool_calls"] = StringMessage("openai", turn, false)["tool_calls"]![i]!.DeepClone() } } });
                E("message-end", new JsonObject { ["delta"] = new JsonObject { ["finish_reason"] = "COMPLETE" } });
            }
            else for (int i = 0; i < content.Count; i++)
            {
                var block = content[i]!.AsObject();
                if (provider == "anthropic")
                {
                    var start = block.DeepClone().AsObject(); string? kind = start["type"]?.GetValue<string>();
                    if (kind is "thinking" or "text") { start[kind] = ""; if (kind == "thinking") start["signature"] = ""; }
                    if (kind == "tool_use") start["input"] = new JsonObject();
                    E("content_block_start", new JsonObject { ["index"] = i, ["content_block"] = start });
                    if (kind is "thinking" or "text")
                    {
                        string value = block[kind]!.GetValue<string>();
                        foreach (string piece in new[] { value[..2], value[2..] }) E("content_block_delta", new JsonObject { ["index"] = i, ["delta"] = new JsonObject { ["type"] = kind + "_delta", [kind] = piece } });
                        if (kind == "thinking") foreach (string piece in new[] { "si", "g" + turn }) E("content_block_delta", new JsonObject { ["index"] = i, ["delta"] = new JsonObject { ["type"] = "signature_delta", ["signature"] = piece } });
                    }
                    if (kind == "tool_use") E("content_block_delta", new JsonObject { ["index"] = i, ["delta"] = new JsonObject { ["type"] = "input_json_delta", ["partial_json"] = block["input"]!.ToJsonString() } });
                    E("content_block_stop", new JsonObject { ["index"] = i });
                }
                else
                {
                    void Delta(JsonObject d) => E("contentBlockDelta", new JsonObject { ["contentBlockIndex"] = i, ["delta"] = d });
                    if (block["toolUse"] is JsonObject tool)
                    {
                        var start = tool.DeepClone().AsObject(); start.Remove("input");
                        E("contentBlockStart", new JsonObject { ["contentBlockIndex"] = i, ["start"] = new JsonObject { ["toolUse"] = start } });
                        Delta(new JsonObject { ["toolUse"] = new JsonObject { ["input"] = tool["input"]!.ToJsonString() } });
                    }
                    else if (block["reasoningContent"]?["reasoningText"] is JsonObject r)
                    {
                        string value = r["text"]!.GetValue<string>();
                        foreach (string piece in new[] { value[..2], value[2..] }) Delta(new JsonObject { ["reasoningContent"] = new JsonObject { ["text"] = piece } });
                        foreach (string piece in new[] { "si", "g" + turn }) Delta(new JsonObject { ["reasoningContent"] = new JsonObject { ["signature"] = piece } });
                    }
                    else if (block["reasoningContent"]?["redactedContent"] != null)
                        foreach (string piece in new[] { "AQI=", "AwQ=" }) Delta(new JsonObject { ["reasoningContent"] = new JsonObject { ["redactedContent"] = piece } });
                    else Delta(block.DeepClone().AsObject());
                    E("contentBlockStop", new JsonObject { ["contentBlockIndex"] = i });
                }
            }
            if (provider == "anthropic") E("message_stop", new JsonObject());
            if (provider == "bedrock")
            {
                E("messageStop", new JsonObject { ["stopReason"] = "tool_use" });
                using var buffer = new MemoryStream();
                foreach (var (type, payload) in events)
                {
                    byte[] frame = EventStreamDecoder.EncodeMessage(new Dictionary<string, string> { [":message-type"] = "event", [":event-type"] = type, [":content-type"] = "application/json" }, Encoding.UTF8.GetBytes(payload.ToJsonString()));
                    buffer.Write(frame);
                }
                return Convert.ToBase64String(buffer.ToArray());
            }
            return string.Concat(events.Select(e => (provider == "ollama" ? "" : "data: ") + e.Item2.ToJsonString() + (provider == "ollama" ? "\n" : "\n\n")));
        }
    }
}
