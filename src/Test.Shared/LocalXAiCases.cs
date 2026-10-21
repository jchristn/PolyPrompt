namespace Test.Shared
{
    using System.Net;
    using System.Text;
    using System.Text.Json;
    using PolyPrompt.Clients;
    using PolyPrompt.Helpers;
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using Touchstone.Core;

    internal static class LocalXAiCases
    {
        private static JsonElement J(string json) { using JsonDocument d = JsonDocument.Parse(json); return d.RootElement.Clone(); }
        private static XAiCompletionClient Client(LocalOpenAiTestServer server) => new(LocalXAiRoutes.Key, server.Endpoint);
        private static ToolChatRequest Request(string prompt = "hello") => new()
        {
            Messages = new() { ChatMessage.User(prompt) },
            Tools = new() { ToolDefinition.Function("get_weather", "Weather", new() { ["type"] = "object", ["properties"] = new Dictionary<string, object> { ["city"] = new Dictionary<string, object> { ["type"] = "string" } } }) }
        };
        public static IEnumerable<TestCaseDescriptor> Create(string suite)
        {
            (string, Func<CancellationToken, Task>)[] cases = {
                ("auth_and_defaults", AuthAsync), ("chat", ChatAsync), ("chat_stream", ChatStreamAsync),
                ("generation", GenerationAsync), ("generation_stream", GenerationStreamAsync),
                ("tools_state_replay", ToolsAsync), ("tools_stream_state_replay", ToolsStreamAsync),
                ("options_merge_and_structured_output", OptionsAsync), ("multimodal_raw_input", RawAsync),
                ("native_event_stream", NativeAsync), ("hosted_tools", HostedAsync),
                ("stored_response_resources", ResourcesAsync), ("embeddings", EmbeddingsAsync),
                ("embedding_base64_and_tokens", EmbeddingTokensAsync), ("catalogs", CatalogsAsync),
                ("http_and_malformed_errors", ErrorsAsync), ("stream_errors", StreamErrorsAsync),
                ("incomplete_responses", IncompleteAsync), ("validation", ValidationAsync),
                ("sse_framing_and_final_fallback", FramingAsync), ("cancellation", CancellationAsync),
                ("serialization_metadata", SerializationAsync), ("parallel_tool_calls", ParallelAsync), ("hosted_output_and_failures", HostedOutputAsync), ("stream_transport_and_protocol_failures", ProtocolErrorsAsync)
            };
            foreach (var (name, execute) in cases) yield return new TestCaseDescriptor(suite, "xai_" + name, "xAI " + name.Replace('_', ' '), execute, new[] { "local", "xai" });
        }
        private static Task AuthAsync(CancellationToken token)
        {
            foreach (string? invalid in new string?[] { null, "", " ", "bad token", "bad\r\ntoken" })
            {
                bool threw = false; try { using XAiCompletionClient c = new(invalid!); } catch (ArgumentException) { threw = true; }
                SharedAssert.True(threw, "Completion must require a valid bearer token.");
                threw = false; try { using XAiEmbeddingClient c = new(invalid!); } catch (ArgumentException) { threw = true; }
                SharedAssert.True(threw, "Embedding must require a valid bearer token.");
                threw = false; try { using XAiModelClient c = new(invalid!); } catch (ArgumentException) { threw = true; }
                SharedAssert.True(threw, "Models must require a valid bearer token.");
            }
            using XAiCompletionClient client = new("test");
            SharedAssert.Equal("https://api.x.ai", client.Endpoint, "Default endpoint.");
            SharedAssert.Equal("grok-4.7", client.Model, "Default model.");
            SharedAssert.Equal<bool?>(false, client.Defaults.Store, "Storage is explicit.");
            return Task.CompletedTask;
        }
        private static async Task ChatAsync(CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start(); using var c = Client(server);
            ChatResponse r = await c.ChatAsync("hello", token: token);
            SharedAssert.True(r.Success, r.Error ?? "Chat failed."); SharedAssert.Equal("pong", r.Text, "Text.");
            SharedAssert.Equal("Thinking clearly.", r.Reasoning, "Summary separate."); SharedAssert.Equal("resp-test", r.ResponseId, "ID.");
            SharedAssert.Equal<int?>(4, r.Usage?.ReasoningTokens, "Reasoning tokens."); SharedAssert.Equal<int?>(3, r.Usage?.CachedPromptTokens, "Cached tokens.");
            SharedAssert.Equal("Bearer " + LocalXAiRoutes.Key, server.RequestHeaders[0]["Authorization"], "Auth.");
            SharedAssert.Equal("/v1/responses", server.RequestPaths[0], "Native endpoint.");
            SharedAssert.True(r.ProviderState?.GetRawText().Contains("opaque-state") == true, "Encrypted state preserved.");
        }
        private static async Task ChatStreamAsync(CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start(); using var c = Client(server);
            var r = await c.ChatStreamingAsync("hello", token: token); StringBuilder text = new();
            await foreach (var chunk in r.Chunks.WithCancellation(token)) text.Append(chunk.Text);
            SharedAssert.True(r.Success, r.Error ?? "Streaming failed."); SharedAssert.Equal("pong", text.ToString(), "Text not duplicated.");
            SharedAssert.Equal("Thinking clearly.", r.Reasoning, "Reasoning not duplicated."); SharedAssert.Equal<int?>(8, r.Usage?.CompletionTokens, "Usage.");
            SharedAssert.True(r.ProviderState.HasValue, "State available after enumeration.");
        }
        private static async Task GenerationAsync(CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start(); using var c = Client(server);
            c.Defaults.SystemPrompt = "must-not-send"; c.Defaults.ReasoningEffort = ReasoningEffort.High;
            var r = await c.GenerateAsync("hello", token: token);
            SharedAssert.True(r.Success, r.Error ?? "Generation failed."); SharedAssert.Equal("pong", r.Text, "Generation.");
            SharedAssert.Equal("Thinking clearly.", r.Reasoning, "Generation reason.");
            var body = LocalJson.Parse(server.RequestBodies[0]);
            SharedAssert.False(body.Has("instructions") || body.Has("reasoning"), "Generation leaves system and reasoning settings out.");
            SharedAssert.True(body.Has("max_output_tokens"), "Responses token setting.");
        }
        private static async Task GenerationStreamAsync(CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start(); using var c = Client(server);
            var r = await c.GenerateStreamingAsync("hello", token: token); StringBuilder text = new();
            await foreach (var chunk in r.Chunks.WithCancellation(token)) text.Append(chunk.Text);
            SharedAssert.True(r.Success, r.Error ?? "Generation stream failed."); SharedAssert.Equal("pong", text.ToString(), "Text.");
            SharedAssert.Equal("Thinking clearly.", r.Reasoning, "Generation reasoning."); SharedAssert.Equal<int?>(21, r.Usage?.TotalTokens, "Total usage.");
        }
        private static Task ToolsAsync(CancellationToken token) => ToolsCore(false, token);
        private static Task ToolsStreamAsync(CancellationToken token) => ToolsCore(true, token);
        private static async Task ToolsCore(bool stream, CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start(); using var c = Client(server);
            var request = Request(); ChatMessage assistant;
            if (stream)
            {
                var r = await c.ToolChatStreamingAsync(request, token);
                await foreach (var _ in r.Chunks.WithCancellation(token)) { }
                SharedAssert.True(r.Success, r.Error ?? "Tools stream failed."); SharedAssert.Equal(1, r.ToolCalls.Count, "Exactly one assembled function.");
                SharedAssert.Equal("call-1", r.ToolCalls[0].Id, "call_id is distinct from item ID.");
                SharedAssert.Equal("{\"city\":\"Seattle\"}", r.ToolCalls[0].ArgumentsJson, "Full/delta arguments don't duplicate."); assistant = r.ToAssistantMessage();
            }
            else
            {
                var r = await c.ToolChatAsync(request, token); SharedAssert.True(r.Success, r.Error ?? "Tools failed.");
                SharedAssert.Equal(1, r.ToolCalls.Count, "Function parsed."); assistant = r.ToAssistantMessage();
            }
            SharedAssert.True(assistant.ProviderState.HasValue, "ToAssistantMessage preserves opaque output.");
            string saved = JsonSerializer.Serialize(assistant, PolyPromptJsonContext.Default.ChatMessage);
            request.Messages.Add(JsonSerializer.Deserialize(saved, PolyPromptJsonContext.Default.ChatMessage)!);
            request.Messages.Add(ChatMessage.ToolResult("call-1", "get_weather", "sunny"));
            var final = await c.ToolChatAsync(request, token); SharedAssert.True(final.Success, final.Error ?? "Followup failed.");
            SharedAssert.Equal("pong", final.Text, "Followup text.");
            var body = J(server.RequestBodies.Last()); var input = body.GetProperty("input");
            SharedAssert.Equal(1, input.EnumerateArray().Count(i => i.TryGetProperty("encrypted_content", out _)), "Encrypted reasoning replayed once.");
            SharedAssert.Equal(1, input.EnumerateArray().Count(i => i.TryGetProperty("type", out var t) && t.GetString() == "function_call"), "Function replayed once, not rebuilt twice.");
            SharedAssert.True(input.GetRawText().Contains("function_call_output"), "Native result mapping.");
        }
        private static async Task OptionsAsync(CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start(); using var c = Client(server);
            c.Defaults.Store = true; c.Defaults.ParallelToolCalls = true; c.Defaults.TopK = 20; c.Defaults.ReasoningEffort = ReasoningEffort.High;
            var options = new XAiCompletionOptions
            {
                Model = "override-model", MaxTokens = 123, Store = false, ParallelToolCalls = false, TopK = 3, MinP = .1,
                ReasoningEffortOverride = "xhigh", ReasoningSummary = "detailed", Logprobs = true, TopLogprobs = 2, MaxTurns = 4,
                ServiceTier = "fast", User = "user", SafetyIdentifier = "hash", PromptCacheKey = "cache",
                TextFormat = new() { ["type"] = "json_schema", ["name"] = "answer", ["schema"] = new Dictionary<string, object> { ["type"] = "object" } },
                AdditionalParameters = new() { ["future_option"] = J("true") }
            };
            var r = await c.ChatAsync("hello", options, token); SharedAssert.True(r.Success, r.Error ?? "Options failed.");
            var b = LocalJson.Parse(server.RequestBodies[0]);
            SharedAssert.Equal<int?>(123, b.Int("max_output_tokens"), "MaxTokens maps correctly."); SharedAssert.Equal<bool?>(false, b.Bool("store"), "False overrides true.");
            SharedAssert.Equal<bool?>(false, b.Bool("parallel_tool_calls"), "False parallel override."); SharedAssert.Equal("xhigh", b.Str("reasoning.effort"), "Effort override.");
            SharedAssert.Equal<int?>(3, b.Int("top_k"), "TopK override."); SharedAssert.Equal("json_schema", b.Str("text.format.type"), "Structured output.");
            SharedAssert.Equal<bool?>(true, b.Bool("future_option"), "Extension field preserved.");
        }
        private static async Task RawAsync(CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start(); using var c = Client(server);
            JsonElement input = J("[{\"role\":\"user\",\"content\":[{\"type\":\"input_image\",\"image_url\":\"https://example.com/image.png\"},{\"type\":\"input_file\",\"file_data\":\"YQ==\",\"filename\":\"a.txt\"}]}]");
            var r = await c.CreateResponseAsync(input, new() { Store = true, PreviousResponseId = "resp-prior", SystemPrompt = "not-with-previous" }, token);
            SharedAssert.True(r.Success, r.Error ?? "Raw response failed.");
            var b = LocalJson.Parse(server.RequestBodies[0]); SharedAssert.Equal("resp-prior", b.Str("previous_response_id"), "Explicit continuation.");
            SharedAssert.False(b.Has("instructions"), "Instructions not sent with previous ID."); SharedAssert.Equal("input_image", b.Str("input.0.content.0.type"), "Rich input untouched.");
            SharedAssert.True(r.RawResponse?.GetRawText().Contains("url_citation") == true, "Citations preserved.");
            var richTools = await c.CreateResponseAsync(input, new() { FunctionTools = Request().Tools }, token);
            SharedAssert.True(richTools.Success && richTools.ToolCalls.Count == 1, "Multimodal raw input can request local functions.");
        }
        private static async Task NativeAsync(CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start(); using var c = Client(server);
            var r = await c.CreateResponseStreamingAsync(J("\"hello\""), token: token); List<string> events = new();
            await foreach (var evt in r.Events.WithCancellation(token)) events.Add(evt.Type);
            SharedAssert.Equal(StreamingState.Completed, r.State, "Native events share final lifecycle.");
            SharedAssert.True(r.Success, r.Error ?? "Native streaming failed."); SharedAssert.True(events.Contains("response.future_event"), "Future events retained.");
            SharedAssert.Equal("pong", r.Response?.Text, "Final native response parsed."); SharedAssert.True(r.Response?.RawResponse.HasValue == true, "Raw terminal response retained.");
        }
        private static async Task HostedAsync(CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start(); using var c = Client(server);
            string[] types = { "web_search", "x_search", "code_interpreter", "file_search", "mcp", "image_generation", "shell", "tool_search" };
            var r = await c.ChatAsync("hello", new XAiCompletionOptions { HostedTools = types.Select(t => XAiHostedTool.Create(t, t switch
            {
                "file_search" => new Dictionary<string, object> { ["vector_store_ids"] = new[] { "collection-test" } },
                "mcp" => new Dictionary<string, object> { ["server_label"] = "fixture", ["server_url"] = "https://example.com/mcp" },
                "shell" => new Dictionary<string, object> { ["environment"] = new Dictionary<string, object> { ["type"] = "local" } },
                _ => null
            })).ToList() }, token);
            SharedAssert.True(r.Success, r.Error ?? "Hosted configuration failed.");
            SharedAssert.Equal<int>(8, LocalJson.Parse(server.RequestBodies[0]).Count("tools"), "All hosted tool variants mapped.");
        }
        private static async Task ResourcesAsync(CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start(); using var c = Client(server);
            SharedAssert.True((await c.GetResponseAsync("resp-test", token)).Success, "Get response.");
            var input = await c.ListResponseInputItemsAsync("resp-test", 7, "desc", "item /+", token);
            SharedAssert.True(input.Success, input.Error ?? "Input listing."); SharedAssert.True(server.RequestUrls.Last().Contains("after=item%20%2F%2B"), "Cursor escaped.");
            var compact = await c.CompactConversationAsync(J("\"compact me\""), token: token);
            SharedAssert.True(compact.Success, compact.Error ?? "Compaction."); SharedAssert.True(compact.Data?.GetRawText().Contains("compact-state") == true, "Compaction opaque output.");
            var deleted = await c.DeleteResponseAsync("resp-test", token); SharedAssert.True(deleted.Success && deleted.Data?.GetProperty("deleted").GetBoolean() == true, "Delete response.");
            SharedAssert.True(c.CallDetails.Any(d => d.Method == "DELETE"), "Deletion is recorded.");
        }
        private static async Task EmbeddingsAsync(CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start(); using XAiEmbeddingClient c = new(LocalXAiRoutes.Key, server.Endpoint) { Model = "test-embedding" };
            var r = await c.EmbedAsync(new List<string> { "first", "second" }, new XAiEmbeddingOptions { Dimensions = 3, Preview = true, User = "u" }, token);
            SharedAssert.True(r.Success, r.Error ?? "Embeddings failed."); SharedAssert.Equal(2, r.Embeddings.Count, "Batch."); SharedAssert.Equal(0, r.Embeddings[0].Index, "Sorted indices.");
            SharedAssert.Equal<int?>(7, r.Usage?.PromptTokens, "Embedding usage.");
            SharedAssert.True((await c.EmbedAsync("single", token: token)).Success, "Single embedding.");
        }
        private static async Task EmbeddingTokensAsync(CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start(); using XAiEmbeddingClient c = new(LocalXAiRoutes.Key, server.Endpoint) { Model = "test-embedding" };
            var r = await c.EmbedTokensAsync(new[] { 1, 2 }, new() { EncodingFormat = "base64" }, token);
            SharedAssert.True(r.Success, r.Error ?? "Token embedding failed."); SharedAssert.Equal(3f, r.Embeddings[0].Embedding[2], "Little-endian base64 floats.");
            SharedAssert.True((await c.EmbedTokenBatchAsync(new[] { new[] { 1 }, new[] { 2 } }, token: token)).Success, "Token batch.");
        }
        private static async Task CatalogsAsync(CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start(); using XAiModelClient c = new(LocalXAiRoutes.Key, server.Endpoint);
            var catalogs = new[] { c.ListModelsAsync(token), c.ListLanguageModelsAsync(token), c.ListEmbeddingModelsAsync(token), c.ListImageGenerationModelsAsync(token), c.ListVideoGenerationModelsAsync(token) };
            foreach (var catalog in catalogs) { int n = 0; await foreach (var model in catalog) { n++; SharedAssert.True(model.Metadata.ContainsKey("capabilities"), "Detailed metadata preserved."); } SharedAssert.Equal(1, n, "Catalog count."); }
            SharedAssert.True(await c.ModelExistsAsync("test-model", token), "Model existence.");
            SharedAssert.NotNull(await c.GetModelInformationAsync("test-model", token), "General detail.");
            SharedAssert.NotNull(await c.GetCatalogModelInformationAsync("language-models", "test-model", token), "Capability detail.");
        }
        private static async Task ErrorsAsync(CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start(); using var c = Client(server);
            var r = await c.ChatAsync("xai-http-error", token: token); SharedAssert.False(r.Success, "HTTP failure must fail."); SharedAssert.Equal<int?>(429, r.StatusCode, "HTTP status.");
            SharedAssert.False((await c.ChatStreamingAsync("xai-http-error", token: token)).Success, "Stream startup failure.");
            SharedAssert.False((await c.ChatAsync("xai-malformed", token: token)).Success, "Malformed response must fail.");
        }
        private static async Task StreamErrorsAsync(CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start(); using var c = Client(server);
            foreach (string prompt in new[] { "xai-stream-error", "xai-truncated" })
            {
                var r = await c.ChatStreamingAsync(prompt, token: token); await foreach (var _ in r.Chunks.WithCancellation(token)) { }
                SharedAssert.False(r.Success, "Stream failure/truncation must not look successful."); SharedAssert.NotEmpty(r.Error, "Stream error recorded.");
            }
        }
        private static async Task IncompleteAsync(CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start(); using var c = Client(server);
            var r = await c.ChatAsync("xai-incomplete", token: token); SharedAssert.True(r.Success, r.Error ?? "Partial text is valid."); SharedAssert.Equal("length", r.FinishReason, "Token limit reason.");
            var tools = await c.ToolChatAsync(Request("xai-incomplete"), token); SharedAssert.False(tools.Success, "Partial functions are never executable.");
        }
        private static async Task ValidationAsync(CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start(); using var c = Client(server);
            await SharedAssert.ThrowsAsync<ArgumentException>(() => c.CreateResponseAsync(J("{}"), token: token), "Invalid raw input.");
            await SharedAssert.ThrowsAsync<ArgumentException>(() => c.ChatAsync("hello", new XAiCompletionOptions { ReasoningEffortOverride = "none" }, token), "Reasoning cannot be disabled.");
            await SharedAssert.ThrowsAsync<ArgumentException>(() => c.ChatAsync("hello", new XAiCompletionOptions { AdditionalParameters = new() { ["model"] = J("\"override\"") } }, token), "Reserved fields cannot be overridden.");
            await SharedAssert.ThrowsAsync<ArgumentException>(() => c.ListResponseInputItemsAsync("id", 101, token: token), "Paging range.");
            await SharedAssert.ThrowsAsync<ArgumentException>(() => c.ToolChatAsync(new() { Messages = new() { ChatMessage.ToolResult(null, "tool", "result") } }, token), "Result call ID required.");
            using XAiEmbeddingClient embedding = new(LocalXAiRoutes.Key, server.Endpoint) { Model = "test" };
            await SharedAssert.ThrowsAsync<ArgumentException>(() => embedding.EmbedAsync(Enumerable.Repeat("x", 129).ToList(), token: token), "Batch limit.");
            SharedAssert.Equal(0, server.RequestBodies.Count, "Invalid arguments send nothing.");
        }
        private static async Task FramingAsync(CancellationToken token)
        {
            string final = "{\"type\":\"response.completed\",\"response\":{\"id\":\"r\",\"status\":\"completed\",\"model\":\"m\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"fallback\"}]}]}}";
            using var handler = new LocalFakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(": comment\r\nevent: response.completed\r\ndata: " + final[..final.IndexOf(",\"response\"", StringComparison.Ordinal)] + ",\r\ndata: " + final[(final.IndexOf(",\"response\"", StringComparison.Ordinal) + 1)..] + "\r\n\r\n", Encoding.UTF8, "text/event-stream") }));
            using var http = new HttpClient(handler); using XAiCompletionClient c = new("test", httpClient: http);
            var r = await c.ChatStreamingAsync("hello", token: token); StringBuilder text = new(); await foreach (var chunk in r.Chunks.WithCancellation(token)) text.Append(chunk.Text);
            SharedAssert.True(r.Success, r.Error ?? "SSE framing."); SharedAssert.Equal("fallback", text.ToString(), "Final-only payload fills missing deltas.");
        }
        private static async Task CancellationAsync(CancellationToken token)
        {
            using var c = new XAiCompletionClient("test", "http://127.0.0.1:1"); using var canceled = new CancellationTokenSource(); canceled.Cancel();
            await SharedAssert.ThrowsAsync<OperationCanceledException>(() => c.ChatAsync("hello", token: canceled.Token), "Caller cancellation propagates.");
        }
        private static async Task ParallelAsync(CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start(); using var c = Client(server);
            var r = await c.ToolChatStreamingAsync(Request("xai-parallel"), token);
            await foreach (var _ in r.Chunks.WithCancellation(token)) { }
            SharedAssert.True(r.Success, r.Error ?? "Parallel tools."); SharedAssert.Equal(2, r.ToolCalls.Count, "Two independent tool calls.");
            SharedAssert.Equal("call-1", r.ToolCalls[0].Id, "Ordered by output index, not arrival.");
            SharedAssert.Equal("call-2", r.ToolCalls[1].Id, "Separate call identity.");
            SharedAssert.Equal("{\"city\":\"Tokyo\"}", r.ToolCalls[1].ArgumentsJson, "Arguments stay with the correct call.");
        }
        private static async Task HostedOutputAsync(CancellationToken token)
        {
            const string json = """{"id":"hosted","model":"test","status":"completed","output":[{"type":"web_search_call","id":"search","status":"completed","action":{"type":"search","query":"test"},"encrypted_content":"hosted-opaque"},{"type":"message","content":[{"type":"output_text","text":"cited answer","annotations":[{"type":"url_citation","url":"https://example.com"}]}]}]}""";
            using var handler = LocalFakeHandler.Always(HttpStatusCode.OK, json); using var http = new HttpClient(handler); using XAiCompletionClient c = new("test", httpClient: http);
            var r = await c.CreateResponseAsync(J("\"hello\""), token: token);
            SharedAssert.True(r.Success, r.Error ?? "Hosted output."); SharedAssert.Equal(0, r.ToolCalls.Count, "Hosted calls are not executed locally.");
            SharedAssert.True(r.ToAssistantMessage().ProviderState!.Value.GetRawText().Contains("hosted-opaque"), "Hosted encrypted outputs replay intact.");
            SharedAssert.True(r.RawResponse!.Value.GetRawText().Contains("url_citation"), "Citation retained.");
        }
        private static async Task ProtocolErrorsAsync(CancellationToken token)
        {
            string[] payloads = {
                "data: {not-json}\n\n",
                "data: {\"type\":\"response.completed\"}\n\n",
                "data: {\"type\":\"response.failed\",\"response\":{\"id\":\"r\",\"status\":\"failed\",\"output\":[],\"error\":{\"message\":\"provider failed\"}}}\n\n"
            };
            foreach (string payload in payloads)
            {
                using var handler = new LocalFakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "text/event-stream") }));
                using var http = new HttpClient(handler); using XAiCompletionClient c = new("test", httpClient: http);
                var r = await c.ToolChatStreamingAsync(Request(), token); await foreach (var _ in r.Chunks.WithCancellation(token)) { }
                SharedAssert.False(r.Success, "Malformed/failed streams cannot succeed."); SharedAssert.Equal(0, r.ToolCalls.Count, "No executable calls escape a failed stream.");
            }
        }

        private static Task SerializationAsync(CancellationToken token)
        {
            var config = JsonSerializer.Deserialize("[{\"type\":\"web_search\",\"parameters\":{\"allowed_domains\":[\"example.com\"]}}]", PolyPromptJsonContext.Default.ListXAiHostedTool);
            SharedAssert.Equal("web_search", config![0].Type, "Console hosted-tool JSON names round trip.");
            SharedAssert.True(config[0].Parameters.ContainsKey("allowed_domains"), "Hosted parameters round trip without reflection.");
            var response = new XAiResponse { ProviderState = J("[{\"type\":\"reasoning\",\"encrypted_content\":\"opaque\"}]") };
            string json = JsonSerializer.Serialize(response, PolyPromptJsonContext.Default.XAiResponse);
            SharedAssert.True(JsonSerializer.Deserialize(json, PolyPromptJsonContext.Default.XAiResponse)!.ToAssistantMessage().ProviderState!.Value.GetRawText().Contains("opaque"), "Native-safe state persistence.");
            return Task.CompletedTask;
        }
    }
}
