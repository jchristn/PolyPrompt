namespace PolyPrompt.Clients
{
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Text;
    using PolyPrompt.Auth;
    using PolyPrompt.Models;
    using PolyPrompt.Wire;
    using SyslogLogging;

    /// <summary>
    /// AWS Bedrock completion client using the unified <c>Converse</c> and <c>ConverseStream</c> APIs, which normalize
    /// messages, tools, reasoning, and usage across the model families Bedrock hosts (Anthropic, Amazon, Meta, Cohere,
    /// Mistral). Requests are signed with AWS Signature Version 4; streamed responses use the AWS binary event-stream format.
    /// Generation is sent as a single user turn.
    /// </summary>
    public class BedrockCompletionClient : CompletionClientBase
    {
        #region Private-Members

        private readonly IAwsCredentialProvider _CredentialProvider;
        private readonly string _Region;
        private readonly string _ControlPlaneEndpoint;

        #endregion

        #region Public-Members

        /// <summary>
        /// Client-wide default settings. Default model: anthropic.claude-3-5-sonnet-20240620-v1:0.
        /// </summary>
        public override CompletionOptions Defaults { get; } = new CompletionOptions { Model = "anthropic.claude-3-5-sonnet-20240620-v1:0" };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new Bedrock completion client.
        /// </summary>
        /// <param name="credentialProvider">Resolves AWS credentials to sign each request.</param>
        /// <param name="region">AWS region, for example <c>us-east-1</c>. Selects the endpoint host and the signing region.</param>
        /// <param name="endpoint">Optional endpoint override. Default: <c>https://bedrock-runtime.{region}.amazonaws.com</c>.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        /// <param name="httpClient">Optional HTTP client; see <see cref="ClientBase"/>. Default: null.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null or empty.</exception>
        public BedrockCompletionClient(
            IAwsCredentialProvider credentialProvider,
            string region,
            string? endpoint = null,
            LoggingModule? logging = null,
            HttpClient? httpClient = null)
            : base(BedrockProtocol.ResolveRuntimeEndpoint(endpoint, region), null, logging ?? new LoggingModule(), httpClient)
        {
            _CredentialProvider = credentialProvider ?? throw new ArgumentNullException(nameof(credentialProvider));
            _Region = region;
            _ControlPlaneEndpoint = BedrockProtocol.ResolveControlPlaneEndpoint(endpoint, region);
            _Header = BedrockProtocol.Header;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override Task<bool> ValidateConnectivityAsync(CancellationToken token = default)
        {
            return ProbeAsync(_ControlPlaneEndpoint.TrimEnd('/') + "/foundation-models", token);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
        {
            BedrockProtocol.Sign(request, body, _CredentialProvider, _Region);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override Task<ChatResponse> ChatCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token)
        {
            Dictionary<string, object> body = BuildBody(settings, UserMessages(prompt), SystemBlocks(settings.SystemPrompt), null, null, settings.ReasoningEffort);
            ChatResponse response = new ChatResponse { Model = settings.Model };
            return ExecutePostAsync(response, "chat", ConverseUrl(settings.Model, false), body, (text, r) =>
            {
                ConverseResult parsed = ParseConverse(text, r);
                r.Text = parsed.Text;
                r.Reasoning = NormalizeReasoning(parsed.Reasoning);
                r.Usage = parsed.Usage;
            }, token);
        }

        /// <inheritdoc />
        protected override Task<ChatStreamingResponse> ChatStreamingCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token)
        {
            Dictionary<string, object> body = BuildBody(settings, UserMessages(prompt), SystemBlocks(settings.SystemPrompt), null, null, settings.ReasoningEffort);
            ChatStreamingResponse response = new ChatStreamingResponse { Model = settings.Model };
            return ExecuteStreamingAsync(response, "chat", ConverseUrl(settings.Model, true), body,
                (stream, sw) => response.Chunks = WrapChunksWithTiming(response, ReadChatChunks(stream.Response, stream.Token), sw, stream.Token, stream),
                token);
        }

        /// <inheritdoc />
        protected override Task<ToolChatResponse> ToolChatCoreAsync(ToolChatRequest request, List<ChatMessage> messages, ResolvedCompletion settings, CancellationToken token)
        {
            Dictionary<string, object> body = BuildToolChatBody(request, messages, settings);
            ToolChatResponse response = new ToolChatResponse { Model = settings.Model };
            return ExecutePostAsync(response, "tool chat", ConverseUrl(settings.Model, false), body, (text, r) =>
            {
                ConverseResult parsed = ParseConverse(text, r);
                r.Text = parsed.Text;
                r.Reasoning = NormalizeReasoning(parsed.Reasoning);
                r.FinishReason = parsed.StopReason;
                r.Usage = parsed.Usage;
                r.ToolCalls.AddRange(parsed.ToolCalls);
            }, token);
        }

        /// <inheritdoc />
        protected override Task<ToolChatStreamingResponse> ToolChatStreamingCoreAsync(ToolChatRequest request, List<ChatMessage> messages, ResolvedCompletion settings, CancellationToken token)
        {
            Dictionary<string, object> body = BuildToolChatBody(request, messages, settings);
            ToolChatStreamingResponse response = new ToolChatStreamingResponse { Model = settings.Model };
            return ExecuteStreamingAsync(response, "tool chat", ConverseUrl(settings.Model, true), body,
                (stream, sw) => response.Chunks = WrapToolChatChunksWithTiming(response, ReadToolChatChunks(stream.Response, stream.Token), sw, stream.Token, stream),
                token);
        }

        /// <inheritdoc />
        protected override Task<GenerationResponse> GenerateCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token)
        {
            Dictionary<string, object> body = BuildBody(settings, UserMessages(prompt), null, null, null, null);
            GenerationResponse response = new GenerationResponse { Model = settings.Model };
            return ExecutePostAsync(response, "generate", ConverseUrl(settings.Model, false), body, (text, r) => r.Text = ParseConverse(text, r).Text, token);
        }

        /// <inheritdoc />
        protected override Task<GenerationStreamingResponse> GenerateStreamingCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token)
        {
            Dictionary<string, object> body = BuildBody(settings, UserMessages(prompt), null, null, null, null);
            GenerationStreamingResponse response = new GenerationStreamingResponse { Model = settings.Model };
            return ExecuteStreamingAsync(response, "generate", ConverseUrl(settings.Model, true), body,
                (stream, sw) => response.Chunks = WrapGenerationChunksWithTiming(response, ReadGenerateChunks(stream.Response, stream.Token), sw, stream.Token, stream),
                token);
        }

        #endregion

        #region Private-Methods

        private string ConverseUrl(string model, bool streaming)
        {
            return _Endpoint.TrimEnd('/') + "/model/" + model + "/" + (streaming ? "converse-stream" : "converse");
        }

        private static List<Dictionary<string, object>> UserMessages(string text)
        {
            return new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    { "role", "user" },
                    { "content", new List<object> { new Dictionary<string, object> { { "text", text } } } }
                }
            };
        }

        private static List<Dictionary<string, object>>? SystemBlocks(string? systemPrompt)
        {
            if (string.IsNullOrEmpty(systemPrompt)) return null;
            return new List<Dictionary<string, object>> { new Dictionary<string, object> { { "text", systemPrompt } } };
        }

        private Dictionary<string, object> BuildToolChatBody(ToolChatRequest request, List<ChatMessage> messages, ResolvedCompletion settings)
        {
            List<Dictionary<string, object>> system = messages
                .Where(m => string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(m.Content))
                .Select(m => new Dictionary<string, object> { { "text", m.Content! } })
                .ToList();

            List<Dictionary<string, object>>? tools = null;
            if (request.Tools != null && request.Tools.Count > 0 && !IsToolChoiceNone(request.ToolChoice))
            {
                tools = request.Tools.Select(tool => new Dictionary<string, object>
                {
                    { "toolSpec", new Dictionary<string, object>
                        {
                            { "name", tool.Name },
                            { "description", tool.Description ?? string.Empty },
                            { "inputSchema", new Dictionary<string, object> { { "json", tool.Parameters } } }
                        }
                    }
                }).ToList();
            }

            return BuildBody(settings, BuildMessages(messages), system.Count > 0 ? system : null, tools, request.ToolChoice, settings.ReasoningEffort);
        }

        private static Dictionary<string, object> BuildBody(
            ResolvedCompletion settings,
            List<Dictionary<string, object>> messages,
            List<Dictionary<string, object>>? system,
            List<Dictionary<string, object>>? tools,
            string? toolChoice,
            ReasoningEffort? reasoningEffort)
        {
            Dictionary<string, object> inferenceConfig = new Dictionary<string, object> { { "maxTokens", settings.MaxTokens } };

            bool thinkingEnabled = reasoningEffort != null
                && settings.Model.StartsWith("anthropic.", StringComparison.OrdinalIgnoreCase)
                && reasoningEffort.ToBedrockThinkingBudget() > 0;

            // Anthropic requires temperature/top_p to be unset while extended thinking is enabled.
            if (!thinkingEnabled)
            {
                if (settings.Temperature.HasValue) inferenceConfig["temperature"] = settings.Temperature.Value;
                if (settings.TopP.HasValue) inferenceConfig["topP"] = settings.TopP.Value;
            }

            Dictionary<string, object> body = new Dictionary<string, object>
            {
                { "messages", messages },
                { "inferenceConfig", inferenceConfig }
            };

            if (system != null && system.Count > 0) body["system"] = system;

            if (tools != null && tools.Count > 0)
            {
                Dictionary<string, object> toolConfig = new Dictionary<string, object> { { "tools", tools } };
                Dictionary<string, object>? choice = BuildToolChoice(toolChoice);
                if (choice != null) toolConfig["toolChoice"] = choice;
                body["toolConfig"] = toolConfig;
            }

            if (thinkingEnabled)
            {
                body["additionalModelRequestFields"] = new Dictionary<string, object>
                {
                    { "thinking", new Dictionary<string, object>
                        {
                            { "type", "enabled" },
                            { "budget_tokens", reasoningEffort!.ToBedrockThinkingBudget() }
                        }
                    }
                };
            }

            return body;
        }

        private static Dictionary<string, object>? BuildToolChoice(string? toolChoice)
        {
            if (string.IsNullOrWhiteSpace(toolChoice)) return null;
            if (string.Equals(toolChoice, "auto", StringComparison.OrdinalIgnoreCase))
                return new Dictionary<string, object> { { "auto", new Dictionary<string, object>() } };
            if (string.Equals(toolChoice, "required", StringComparison.OrdinalIgnoreCase)
                || string.Equals(toolChoice, "any", StringComparison.OrdinalIgnoreCase))
                return new Dictionary<string, object> { { "any", new Dictionary<string, object>() } };
            if (string.Equals(toolChoice, "none", StringComparison.OrdinalIgnoreCase))
                return null;

            // A specific tool name.
            return new Dictionary<string, object> { { "tool", new Dictionary<string, object> { { "name", toolChoice } } } };
        }

        private List<Dictionary<string, object>> BuildMessages(List<ChatMessage> messages)
        {
            List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();
            List<object>? pendingToolResults = null;

            foreach (ChatMessage message in messages)
            {
                if (string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase)) continue;

                bool isToolResult = string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(message.Role, "function", StringComparison.OrdinalIgnoreCase);

                if (isToolResult)
                {
                    // Consecutive tool results collapse into a single user message (parallel tool calls).
                    pendingToolResults ??= new List<object>();
                    pendingToolResults.Add(new Dictionary<string, object>
                    {
                        { "toolResult", new Dictionary<string, object>
                            {
                                { "toolUseId", ResolveToolUseId(message) },
                                { "content", new List<object> { new Dictionary<string, object> { { "text", message.Content ?? string.Empty } } } }
                            }
                        }
                    });
                    continue;
                }

                FlushToolResults(result, ref pendingToolResults);

                List<object> content = new List<object>();
                if (message.ToolCalls != null && message.ToolCalls.Count > 0)
                {
                    if (!string.IsNullOrEmpty(message.Content))
                        content.Add(new Dictionary<string, object> { { "text", message.Content } });

                    foreach (ToolCall call in message.ToolCalls)
                    {
                        content.Add(new Dictionary<string, object>
                        {
                            { "toolUse", new Dictionary<string, object>
                                {
                                    { "toolUseId", call.Id ?? call.Name },
                                    { "name", call.Name },
                                    { "input", DeserializeDictionaryOrEmpty(call.ArgumentsJson) }
                                }
                            }
                        });
                    }
                }
                else
                {
                    content.Add(new Dictionary<string, object> { { "text", message.Content ?? string.Empty } });
                }

                bool assistant = string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(message.Role, "model", StringComparison.OrdinalIgnoreCase);

                result.Add(new Dictionary<string, object>
                {
                    { "role", assistant ? "assistant" : "user" },
                    { "content", content }
                });
            }

            FlushToolResults(result, ref pendingToolResults);
            return result;
        }

        private static void FlushToolResults(List<Dictionary<string, object>> result, ref List<object>? pendingToolResults)
        {
            if (pendingToolResults != null && pendingToolResults.Count > 0)
            {
                result.Add(new Dictionary<string, object> { { "role", "user" }, { "content", pendingToolResults } });
            }

            pendingToolResults = null;
        }

        private static string ResolveToolUseId(ChatMessage message)
        {
            if (!string.IsNullOrWhiteSpace(message.ToolCallId)) return message.ToolCallId!;
            if (!string.IsNullOrWhiteSpace(message.ToolName)) return message.ToolName!;
            return "tool";
        }

        private ConverseResult ParseConverse(string responseBody, ResponseBase response)
        {
            ConverseResult parsed = new ConverseResult();

            Dictionary<string, object>? responseObj = TryDeserializeObject(responseBody);
            if (responseObj == null)
            {
                response.Error = "Response is not a JSON object";
                return parsed;
            }

            parsed.Usage = ReadUsage(responseObj);
            if (responseObj.ContainsKey("stopReason")) parsed.StopReason = responseObj["stopReason"]?.ToString();

            Dictionary<string, object>? output = ParseNestedObject(responseObj, "output");
            Dictionary<string, object>? message = output == null ? null : ParseNestedObject(output, "message");
            List<Dictionary<string, object>>? blocks = message == null ? null : ParseNestedList(message, "content");
            if (blocks == null)
            {
                response.Error = "Response missing 'output.message.content' field";
                return parsed;
            }

            StringBuilder text = new StringBuilder();
            StringBuilder reasoning = new StringBuilder();

            foreach (Dictionary<string, object> block in blocks)
            {
                if (block.ContainsKey("text"))
                {
                    text.Append(block["text"]?.ToString());
                }
                else if (block.ContainsKey("reasoningContent"))
                {
                    Dictionary<string, object>? reasoningObj = ParseNestedObject(block, "reasoningContent");
                    Dictionary<string, object>? reasoningText = reasoningObj == null ? null : ParseNestedObject(reasoningObj, "reasoningText");
                    if (reasoningText != null && reasoningText.ContainsKey("text")) reasoning.Append(reasoningText["text"]?.ToString());
                }
                else if (block.ContainsKey("toolUse"))
                {
                    Dictionary<string, object>? toolUse = ParseNestedObject(block, "toolUse");
                    if (toolUse == null || !toolUse.ContainsKey("name")) continue;

                    parsed.ToolCalls.Add(new ToolCall
                    {
                        Id = toolUse.ContainsKey("toolUseId") ? toolUse["toolUseId"]?.ToString() : null,
                        Name = toolUse["name"]?.ToString() ?? string.Empty,
                        ArgumentsJson = toolUse.ContainsKey("input") && toolUse["input"] != null ? _Serializer.SerializeJson(toolUse["input"], false) : "{}"
                    });
                }
            }

            parsed.Text = text.Length > 0 ? text.ToString() : null;
            parsed.Reasoning = reasoning.Length > 0 ? reasoning.ToString() : null;
            return parsed;
        }

        // Bedrock reports cache reads and writes as separate buckets additional to inputTokens (matching Anthropic's
        // semantic). Absent unless prompt caching was used.
        private TokenUsage? ReadUsage(Dictionary<string, object> payload)
        {
            Dictionary<string, object>? usageObj = ParseNestedObject(payload, "usage");
            if (usageObj == null) return null;

            TokenUsage usage = new TokenUsage();
            usage.PromptTokens = TryGetInt(usageObj, "inputTokens");
            usage.CompletionTokens = TryGetInt(usageObj, "outputTokens");
            usage.TotalTokens = TryGetInt(usageObj, "totalTokens");
            usage.CachedPromptTokens = TryGetInt(usageObj, "cacheReadInputTokens");
            usage.CacheCreationTokens = TryGetInt(usageObj, "cacheWriteInputTokens");
            return usage;
        }

        private async IAsyncEnumerable<(string? Type, Dictionary<string, object> Payload)> ReadEvents(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken token)
        {
            using Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            await foreach (EventStreamMessage message in EventStreamDecoder.DecodeAsync(stream, token).ConfigureAwait(false))
            {
                Dictionary<string, object>? payload = TryDeserializeObject(message.PayloadString);
                if (payload != null) yield return (message.EventType, payload);
            }
        }

        private (string? Text, string? Reasoning, ToolCallDelta? ToolDelta) ReadDelta(Dictionary<string, object> payload)
        {
            Dictionary<string, object>? delta = ParseNestedObject(payload, "delta");
            if (delta == null) return (null, null, null);

            if (delta.ContainsKey("text")) return (delta["text"]?.ToString(), null, null);

            Dictionary<string, object>? reasoning = ParseNestedObject(delta, "reasoningContent");
            if (reasoning != null) return (null, reasoning.ContainsKey("text") ? reasoning["text"]?.ToString() : null, null);

            Dictionary<string, object>? toolUse = ParseNestedObject(delta, "toolUse");
            if (toolUse != null && toolUse.ContainsKey("input"))
            {
                return (null, null, new ToolCallDelta
                {
                    Index = TryGetInt(payload, "contentBlockIndex") ?? 0,
                    ArgumentsJsonDelta = toolUse["input"]?.ToString()
                });
            }

            return (null, null, null);
        }

        private ToolCallDelta? ReadToolUseStart(Dictionary<string, object> payload)
        {
            Dictionary<string, object>? start = ParseNestedObject(payload, "start");
            Dictionary<string, object>? toolUse = start == null ? null : ParseNestedObject(start, "toolUse");
            if (toolUse == null || !toolUse.ContainsKey("name")) return null;

            return new ToolCallDelta
            {
                Index = TryGetInt(payload, "contentBlockIndex") ?? 0,
                Type = "function",
                Id = toolUse.ContainsKey("toolUseId") ? toolUse["toolUseId"]?.ToString() : null,
                Name = toolUse["name"]?.ToString()
            };
        }

        private async IAsyncEnumerable<ChatStreamingChunk> ReadChatChunks(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken token)
        {
            await foreach ((string? type, Dictionary<string, object> payload) in ReadEvents(response, token).ConfigureAwait(false))
            {
                ChatStreamingChunk chunk = new ChatStreamingChunk();

                if (type == "contentBlockDelta")
                {
                    (chunk.Text, chunk.ReasoningText, _) = ReadDelta(payload);
                }
                else if (type == "messageStop")
                {
                    chunk.FinishReason = payload.ContainsKey("stopReason") ? payload["stopReason"]?.ToString() : null;
                    chunk.Done = true;
                }
                else if (type == "metadata")
                {
                    chunk.Usage = ReadUsage(payload);
                }

                yield return chunk;
            }
        }

        private async IAsyncEnumerable<ToolChatStreamingChunk> ReadToolChatChunks(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken token)
        {
            await foreach ((string? type, Dictionary<string, object> payload) in ReadEvents(response, token).ConfigureAwait(false))
            {
                ToolChatStreamingChunk chunk = new ToolChatStreamingChunk();

                if (type == "contentBlockStart")
                {
                    ToolCallDelta? start = ReadToolUseStart(payload);
                    if (start != null) chunk.ToolCallDeltas.Add(start);
                }
                else if (type == "contentBlockDelta")
                {
                    (string? text, string? reasoning, ToolCallDelta? toolDelta) = ReadDelta(payload);
                    chunk.Text = text;
                    chunk.ReasoningText = reasoning;
                    if (toolDelta != null) chunk.ToolCallDeltas.Add(toolDelta);
                }
                else if (type == "messageStop")
                {
                    chunk.FinishReason = payload.ContainsKey("stopReason") ? payload["stopReason"]?.ToString() : null;
                    chunk.Done = true;
                }
                else if (type == "metadata")
                {
                    chunk.Usage = ReadUsage(payload);
                }

                yield return chunk;
            }
        }

        private async IAsyncEnumerable<GenerationStreamingChunk> ReadGenerateChunks(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken token)
        {
            await foreach ((string? type, Dictionary<string, object> payload) in ReadEvents(response, token).ConfigureAwait(false))
            {
                GenerationStreamingChunk chunk = new GenerationStreamingChunk();
                if (type == "contentBlockDelta") chunk.Text = ReadDelta(payload).Text;
                else if (type == "messageStop") chunk.Done = true;
                yield return chunk;
            }
        }

        private sealed class ConverseResult
        {
            public string? Text { get; set; }
            public string? Reasoning { get; set; }
            public string? StopReason { get; set; }
            public TokenUsage? Usage { get; set; }
            public List<ToolCall> ToolCalls { get; } = new List<ToolCall>();
        }

        #endregion
    }
}
