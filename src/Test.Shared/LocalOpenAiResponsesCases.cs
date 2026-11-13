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

    /// <summary>
    /// OpenAiClient (Responses API) against the OpenAI Responses fixture, which shares no helpers with the xAI fixture.
    /// </summary>
    internal static class LocalOpenAiResponsesCases
    {
        private static JsonElement J(string json) { using JsonDocument d = JsonDocument.Parse(json); return d.RootElement.Clone(); }
        private static OpenAiClient Client(LocalOpenAiTestServer server) => new(LocalOpenAiResponsesRoutes.Key, server.Endpoint) { Defaults = { Completion = { Model = "test-model" } } };
        private static ToolChatRequest Request(string prompt = "weather?") => new()
        {
            Messages = new() { ChatMessage.User(prompt) },
            Tools = new() { ToolDefinition.Function("get_weather", "Weather", new() { ["type"] = "object", ["properties"] = new Dictionary<string, object> { ["city"] = new Dictionary<string, object> { ["type"] = "string" } } }) }
        };
        private static JsonElement Body(LocalOpenAiTestServer server, int index = -1) => J(index < 0 ? server.RequestBodies.Last() : server.RequestBodies[index]);

        public static IEnumerable<TestCaseDescriptor> Create(string suite)
        {
            (string, Func<CancellationToken, Task>)[] cases = {
                ("defaults_and_constructor", DefaultsAsync), ("chat", ChatAsync), ("chat_stream", ChatStreamAsync),
                ("generation", GenerationAsync), ("generation_stream", GenerationStreamAsync),
                ("tool_chat_and_encrypted_replay", ToolsAsync), ("tool_chat_stream_parallel_deltas", ToolsStreamAsync),
                ("request_body", RequestBodyAsync), ("reasoning_effort_mapping", EffortAsync), ("tool_choice_and_factories", ToolChoiceAsync),
                ("reserved_fields_and_validation", ValidationAsync), ("incomplete_and_failed", IncompleteAsync), ("usage", UsageAsync),
                ("http_errors_and_hint", ErrorsAsync), ("stream_errors_and_truncation", StreamErrorsAsync), ("cancellation", CancellationAsync),
                ("native_operations", NativeAsync), ("continue_response", ContinueAsync), ("event_kinds", EventKindsAsync),
                ("citations", CitationsAsync), ("citations_stream", CitationsStreamAsync), ("raw_response", RawResponseAsync),
                ("embeddings_and_models", EmbeddingsAsync), ("background", BackgroundAsync),
            };
            foreach (var (name, execute) in cases) yield return new TestCaseDescriptor(suite, "openai_responses_" + name, "OpenAI Responses " + name.Replace('_', ' '), execute, new[] { "local", "openai" });
        }

        private static Task DefaultsAsync(CancellationToken token)
        {
            using OpenAiClient client = new("sk-test");
            SharedAssert.Equal("https://api.openai.com", client.Endpoint, "Default endpoint.");
            SharedAssert.Equal("gpt-6.1-sol", client.Defaults.Completion.Model, "Default completion model.");
            SharedAssert.Equal("text-embedding-3-small", client.Defaults.Embedding.Model, "Default embedding model.");
            SharedAssert.Equal<bool?>(false, client.Defaults.Completion.Store, "Store defaults to false.");
            SharedAssert.Equal(Capability.Chat | Capability.ToolChat | Capability.Generate | Capability.Embed | Capability.Models, client.Capabilities, "Capabilities.");
            ArgumentException? url = null;
            try { using OpenAiClient bad = new("http://localhost:8000/v1"); } catch (ArgumentException ex) { url = ex; }
            SharedAssert.NotNull(url, "A URL-shaped key throws.");
            SharedAssert.True(url!.Message.Contains("OpenAiCompatibleClient", StringComparison.Ordinal), "The URL hint names OpenAiCompatibleClient: " + url.Message);
            foreach (string? invalid in new string?[] { null, "", " ", "bad key" })
            {
                bool threw = false; try { using OpenAiClient c = new(invalid!); } catch (ArgumentException) { threw = true; }
                SharedAssert.True(threw, "Invalid key '" + invalid + "' throws.");
            }
            bool badEndpoint = false; try { using OpenAiClient c = new("k", "not a url"); } catch (ArgumentException) { badEndpoint = true; }
            SharedAssert.True(badEndpoint, "A non-URL endpoint throws.");
            return Task.CompletedTask;
        }

        private static async Task ChatAsync(CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start(); using var c = Client(server);
            ChatResponse r = await c.ChatAsync("hello", token: token);
            SharedAssert.True(r.Success, r.Error ?? "Chat failed."); SharedAssert.Equal("pong", r.Text, "Text.");
            SharedAssert.Equal("Planning the answer.", r.Reasoning, "Reasoning summary.");
            SharedAssert.Equal("oai-resp", r.ResponseId, "Response id."); SharedAssert.Equal("stop", r.FinishReason, "Finish reason.");
            SharedAssert.True(server.RequestPaths.Last() == "/v1/responses", "Chat uses /v1/responses: " + server.RequestPaths.Last());
            SharedAssert.Equal("Bearer " + LocalOpenAiResponsesRoutes.Key, server.RequestHeaders.Last()["Authorization"], "Bearer auth.");
        }

        private static async Task ChatStreamAsync(CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start(); using var c = Client(server);
            var r = await c.ChatStreamingAsync("hello", token: token);
            StringBuilder text = new(), reasoning = new();
            await foreach (var chunk in r.Chunks.WithCancellation(token)) { text.Append(chunk.Text); reasoning.Append(chunk.ReasoningText); }
            SharedAssert.True(r.Success, r.Error ?? "Stream failed."); SharedAssert.Equal("pong", text.ToString(), "Streamed text.");
            SharedAssert.Equal("Planning the answer.", reasoning.ToString(), "Streamed reasoning.");
            SharedAssert.Equal(StreamingState.Completed, r.State, "Completed.");
            SharedAssert.NotNull(r.RawResponse, "Final response object kept.");
        }

        private static async Task GenerationAsync(CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start(); using var c = Client(server);
            GenerationResponse r = await c.GenerateAsync("hello", new CompletionOptions { ReasoningEffort = ReasoningEffort.High }, token);
            SharedAssert.True(r.Success, r.Error ?? "Generation failed."); SharedAssert.Equal("pong", r.Text, "Text.");
            JsonElement body = Body(server);
            SharedAssert.False(body.TryGetProperty("reasoning", out _), "Generation sends no reasoning.");
            SharedAssert.True(r.AppliedReasoningEffort == null, "No applied effort for generation.");
        }

        private static async Task GenerationStreamAsync(CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start(); using var c = Client(server);
            var r = await c.GenerateStreamingAsync("hello", token: token); StringBuilder text = new();
            await foreach (var chunk in r.Chunks.WithCancellation(token)) text.Append(chunk.Text);
            SharedAssert.True(r.Success, r.Error ?? "Generation stream failed."); SharedAssert.Equal("pong", text.ToString(), "Text.");
        }

        private static async Task ToolsAsync(CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start(); using var c = Client(server);
            var request = Request();
            ToolChatResponse first = await c.ToolChatAsync(request, token);
            SharedAssert.True(first.Success, first.Error ?? "Tool chat failed.");
            SharedAssert.Equal("tool_calls", first.FinishReason, "Tool finish."); SharedAssert.Equal(1, first.ToolCalls.Count, "One call.");
            SharedAssert.Equal("call-oai-1", first.ToolCalls[0].Id, "Call id."); SharedAssert.Equal("get_weather", first.ToolCalls[0].Name, "Name.");
            SharedAssert.True(first is ResponsesApiResponse native && native.Stored == false, "Store=false recorded.");
            JsonElement state = first.ProviderState!.Value;
            SharedAssert.Equal("responses", state.GetProperty("protocol").GetString(), "4.0 envelope protocol.");
            SharedAssert.Equal("openai", state.GetProperty("provider").GetString(), "4.0 envelope provider.");
            SharedAssert.Equal(server.Endpoint.TrimEnd('/'), state.GetProperty("scope").GetString(), "4.0 envelope scope.");
            request.Messages.Add(first.ToAssistantMessage());
            request.Messages.Add(ChatMessage.ToolResult("call-oai-1", "get_weather", "sunny"));
            ToolChatResponse second = await c.ToolChatAsync(request, token);
            SharedAssert.True(second.Success, second.Error ?? "Follow-up failed."); SharedAssert.Equal("pong", second.Text, "Answer after tool.");
            JsonElement input = Body(server).GetProperty("input");
            List<string> types = input.EnumerateArray().Select(i => i.TryGetProperty("type", out JsonElement t) ? t.GetString()! : "message").ToList();
            SharedAssert.Equal("message,reasoning,function_call,function_call_output", string.Join(",", types), "Encrypted reasoning replayed in order before the call and its output.");
            SharedAssert.Equal("oai-encrypted-state", input[1].GetProperty("encrypted_content").GetString(), "Encrypted reasoning replayed verbatim.");
        }

        private static async Task ToolsStreamAsync(CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start(); using var c = Client(server);
            var r = await c.ToolChatStreamingAsync(Request("oai-parallel"), token);
            int deltas = 0; await foreach (var chunk in r.Chunks.WithCancellation(token)) deltas += chunk.ToolCallDeltas.Count(d => d.ArgumentsJsonDelta != null);
            SharedAssert.True(r.Success, r.Error ?? "Stream failed."); SharedAssert.Equal(2, r.ToolCalls.Count, "Two parallel calls.");
            SharedAssert.Equal("call-oai-1", r.ToolCalls[0].Id, "Ordered by output index."); SharedAssert.Equal("{\"city\":\"Tokyo\"}", r.ToolCalls[1].ArgumentsJson, "Arguments stay with their call.");
            SharedAssert.True(deltas >= 4, "Argument deltas arrive across events.");
            SharedAssert.Equal("responses", r.ProviderState!.Value.GetProperty("protocol").GetString(), "Streamed state uses the 4.0 envelope.");
        }

        private static async Task RequestBodyAsync(CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start(); using var c = Client(server);
            c.Defaults.Completion.SystemPrompt = "be brief";
            await c.ChatAsync("hello", new OpenAiCompletionOptions
            {
                MaxTokens = 64, Temperature = 0.2, StrictTools = true, FunctionTools = Request().Tools, Include = new() { "web_search_call.action.sources" },
                ServiceTier = ServiceTier.Flex, ReasoningSummary = ReasoningSummary.Detailed, TextFormat = new() { ["type"] = "json_object" },
                Metadata = new() { ["k"] = "v" }, MaxToolCalls = 3, TopLogprobs = 2, Conversation = "conv_1", PromptCacheKey = "pk", SafetyIdentifier = "sid",
                ContextManagement = J("[{\"type\":\"compaction\",\"compact_threshold\":1000}]"), PromptCacheOptions = J("{\"ttl\":\"24h\"}"),
            }, token);
            JsonElement b = Body(server);
            SharedAssert.Equal("test-model", b.GetProperty("model").GetString(), "model");
            SharedAssert.Equal("hello", b.GetProperty("input").GetString(), "input");
            SharedAssert.Equal("be brief", b.GetProperty("instructions").GetString(), "instructions");
            SharedAssert.Equal(64, b.GetProperty("max_output_tokens").GetInt32(), "max_output_tokens");
            SharedAssert.Equal(JsonValueKind.False, b.GetProperty("store").ValueKind, "store is always sent, false by default");
            SharedAssert.Equal("web_search_call.action.sources,reasoning.encrypted_content", string.Join(",", b.GetProperty("include").EnumerateArray().Select(e => e.GetString())), "include adds encrypted reasoning");
            SharedAssert.Equal("flex", b.GetProperty("service_tier").GetString(), "service tier");
            SharedAssert.Equal("detailed", b.GetProperty("reasoning").GetProperty("summary").GetString(), "summary");
            SharedAssert.True(b.GetProperty("tools")[0].GetProperty("strict").GetBoolean(), "strict tools");
            SharedAssert.Equal("json_object", b.GetProperty("text").GetProperty("format").GetProperty("type").GetString(), "text.format");
            SharedAssert.Equal("v", b.GetProperty("metadata").GetProperty("k").GetString(), "metadata");
            SharedAssert.Equal(3, b.GetProperty("max_tool_calls").GetInt32(), "max_tool_calls");
            SharedAssert.Equal(2, b.GetProperty("top_logprobs").GetInt32(), "top_logprobs");
            SharedAssert.Equal("conv_1", b.GetProperty("conversation").GetString(), "conversation");
            SharedAssert.Equal(1000, b.GetProperty("context_management")[0].GetProperty("compact_threshold").GetInt32(), "context_management");
            SharedAssert.Equal("24h", b.GetProperty("prompt_cache_options").GetProperty("ttl").GetString(), "prompt_cache_options");
            foreach (string xai in new[] { "max_turns", "top_k", "min_p", "guidelines", "logprobs" })
                SharedAssert.False(b.TryGetProperty(xai, out _), "No xAI-only field " + xai + ".");
            await c.ChatAsync("hello", new OpenAiCompletionOptions { Store = true }, token);
            SharedAssert.Equal(JsonValueKind.True, Body(server).GetProperty("store").ValueKind, "Store=true is sent.");
        }

        private static async Task EffortAsync(CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start(); using var c = Client(server);
            var expected = new (ReasoningEffortLevel Level, string Wire)[] { (ReasoningEffortLevel.None, "none"), (ReasoningEffortLevel.Minimal, "minimal"), (ReasoningEffortLevel.Low, "low"),
                (ReasoningEffortLevel.Medium, "medium"), (ReasoningEffortLevel.High, "high"), (ReasoningEffortLevel.XHigh, "xhigh"), (ReasoningEffortLevel.Max, "max") };
            foreach (var (level, wire) in expected)
            {
                ChatResponse r = await c.ChatAsync("hello", new CompletionOptions { ReasoningEffort = level }, token);
                SharedAssert.Equal(wire, Body(server).GetProperty("reasoning").GetProperty("effort").GetString(), level + " maps to " + wire + ".");
                SharedAssert.Equal(wire, r.AppliedReasoningEffort, level + " is reported as applied.");
            }
            await c.ChatAsync("hello", new OpenAiCompletionOptions { ReasoningEffortValue = new ReasoningEffortValue("future-effort"), ReasoningEffort = ReasoningEffort.Low }, token);
            SharedAssert.Equal("future-effort", Body(server).GetProperty("reasoning").GetProperty("effort").GetString(), "Unknown values pass through and override the level.");
            ChatResponse none = await c.ChatAsync("hello", token: token);
            SharedAssert.False(Body(server).TryGetProperty("reasoning", out _), "No effort, no reasoning field.");
            SharedAssert.True(none.AppliedReasoningEffort == null, "No applied effort.");
        }

        private static async Task ToolChoiceAsync(CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start(); using var c = Client(server);
            foreach (var (choice, expected) in new[] { ("auto", "\"auto\""), ("none", "\"none\""), ("required", "\"required\""), ("any", "\"required\""), ("get_weather", "{\"type\":\"function\",\"name\":\"get_weather\"}") })
            {
                var request = Request(); request.ToolChoice = choice;
                await c.ToolChatAsync(request, token);
                SharedAssert.Equal(expected, Body(server).GetProperty("tool_choice").GetRawText(), "tool_choice " + choice);
            }
            var hosted = new OpenAiCompletionOptions { HostedTools = new() { ResponsesApiTools.WebSearch(), ResponsesApiTools.CodeInterpreter(), OpenAiTools.Custom("grep", "Search"), OpenAiTools.ApplyPatch(), ResponsesApiHostedTool.Create("web_search_preview") } };
            await c.ChatAsync("hello", hosted, token);
            JsonElement tools = Body(server).GetProperty("tools");
            SharedAssert.Equal("web_search,code_interpreter,custom,apply_patch,web_search_preview", string.Join(",", tools.EnumerateArray().Select(t => t.GetProperty("type").GetString())), "Hosted tool types.");
            SharedAssert.Equal("auto", tools[1].GetProperty("container").GetProperty("type").GetString(), "Code interpreter auto container.");
        }

        private static async Task ValidationAsync(CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start(); using var c = Client(server);
            await SharedAssert.ThrowsAsync<ArgumentException>(() => c.ChatAsync("hello", new OpenAiCompletionOptions { AdditionalParameters = new() { ["model"] = J("\"x\"") } }, token), "Reserved field.");
            await SharedAssert.ThrowsAsync<ArgumentException>(() => c.ChatAsync("hello", new OpenAiCompletionOptions { AdditionalParameters = new() { ["metadata"] = J("{}") } }, token), "OpenAI reserved field.");
            await SharedAssert.ThrowsAsync<ArgumentException>(() => c.ChatAsync("hello", new XAiCompletionOptions(), token), "Another provider's options type throws.");
            await SharedAssert.ThrowsAsync<ArgumentException>(() => c.ChatAsync("hello", new OpenAiCompatibleCompletionOptions(), token), "Chat Completions options are another provider's type.");
            await SharedAssert.ThrowsAsync<ArgumentOutOfRangeException>(() => c.ChatAsync("hello", new OpenAiCompletionOptions { MaxToolCalls = 0 }, token), "MaxToolCalls must be positive.");
            await SharedAssert.ThrowsAsync<ArgumentException>(() => c.CreateResponseAsync(J("{}"), token: token), "Raw input must be a string or array.");
            SharedAssert.Equal(0, server.RequestBodies.Count, "Invalid calls send nothing.");
            await c.ChatAsync("hello", new AdditionalOk().Options, token);
            SharedAssert.Equal("disabled", Body(server).GetProperty("truncation").GetString(), "Unmodeled fields pass through AdditionalParameters.");
            ChatResponse baseOptions = await c.ChatAsync("hello", new CompletionOptions { Temperature = 0.5 }, token);
            SharedAssert.True(baseOptions.Success, "Base options are accepted.");
            ChatResponse shared = await c.ChatAsync("hello", new ResponsesApiCompletionOptions { ParallelToolCalls = false }, token);
            SharedAssert.True(shared.Success, "Shared Responses options are accepted.");
        }

        private sealed class AdditionalOk
        {
            public OpenAiCompletionOptions Options { get; } = new() { AdditionalParameters = new() { ["truncation"] = J("\"disabled\"") } };
        }

        private static async Task IncompleteAsync(CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start(); using var c = Client(server);
            SharedAssert.Equal("length", (await c.ChatAsync("oai-incomplete", token: token)).FinishReason, "max_output_tokens maps to length.");
            SharedAssert.Equal("content_filter", (await c.ChatAsync("oai-incomplete-filter", token: token)).FinishReason, "content_filter maps to content_filter.");
            SharedAssert.Equal("max_messages", (await c.ChatAsync("oai-incomplete-messages", token: token)).FinishReason, "Other reasons pass through.");
            ChatResponse failed = await c.ChatAsync("oai-failed", token: token);
            SharedAssert.False(failed.Success, "failed maps to an error."); SharedAssert.Equal("The model failed.", failed.Error, "Error message.");
        }

        private static async Task UsageAsync(CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start(); using var c = Client(server);
            ChatResponse r = await c.ChatAsync("hello", token: token);
            SharedAssert.Equal<int?>(20, r.Usage?.PromptTokens, "input_tokens"); SharedAssert.Equal<int?>(9, r.Usage?.CompletionTokens, "output_tokens");
            SharedAssert.Equal<int?>(29, r.Usage?.TotalTokens, "total_tokens"); SharedAssert.Equal<int?>(5, r.Usage?.CachedPromptTokens, "cached_tokens");
            SharedAssert.Equal<int?>(2, r.Usage?.CacheCreationTokens, "cache_write_tokens"); SharedAssert.Equal<int?>(6, r.Usage?.ReasoningTokens, "reasoning_tokens");
        }

        private static async Task ErrorsAsync(CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start(); using var c = Client(server);
            ChatResponse limited = await c.ChatAsync("oai-http-error", token: token);
            SharedAssert.False(limited.Success, "HTTP error."); SharedAssert.Equal<int?>(429, limited.StatusCode, "Status.");
            ChatResponse missing = await c.ChatAsync("oai-not-found", token: token);
            SharedAssert.True(missing.Error!.Contains("Use OpenAiCompatibleClient for Chat Completions servers.", StringComparison.Ordinal), "404 hint: " + missing.Error);
            var stream = await c.ChatStreamingAsync("oai-not-found", token: token);
            SharedAssert.True(stream.Error!.Contains("OpenAiCompatibleClient", StringComparison.Ordinal), "Streaming 404 hint.");
            ChatResponse malformed = await c.ChatAsync("oai-malformed", token: token);
            SharedAssert.True(malformed.Error!.StartsWith("Malformed OpenAI response", StringComparison.Ordinal), "OpenAI wording: " + malformed.Error);
        }

        private static async Task StreamErrorsAsync(CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start(); using var c = Client(server);
            foreach (string prompt in new[] { "oai-stream-error", "oai-truncated" })
            {
                var r = await c.ChatStreamingAsync(prompt, token: token); await foreach (var _ in r.Chunks.WithCancellation(token)) { }
                SharedAssert.False(r.Success, prompt + " must not succeed."); SharedAssert.NotEmpty(r.Error, "Error recorded.");
            }
            var truncated = await c.ChatStreamingAsync("oai-truncated", token: token); await foreach (var _ in truncated.Chunks.WithCancellation(token)) { }
            SharedAssert.True(truncated.Error!.StartsWith("OpenAI stream ended", StringComparison.Ordinal), "OpenAI wording: " + truncated.Error);
            var failed = await c.ToolChatStreamingAsync(Request("oai-failed"), token); await foreach (var _ in failed.Chunks.WithCancellation(token)) { }
            SharedAssert.False(failed.Success, "response.failed fails the stream."); SharedAssert.Equal(0, failed.ToolCalls.Count, "No calls escape.");
        }

        private static async Task CancellationAsync(CancellationToken token)
        {
            using var c = new OpenAiClient("k", "http://127.0.0.1:1"); using var canceled = new CancellationTokenSource(); canceled.Cancel();
            await SharedAssert.ThrowsAsync<OperationCanceledException>(() => c.ChatAsync("hello", token: canceled.Token), "Caller cancellation propagates.");
            using var server = LocalOpenAiTestServer.Start(); using var live = Client(server);
            using var mid = CancellationTokenSource.CreateLinkedTokenSource(token);
            var r = await live.ChatStreamingAsync("hello", token: mid.Token);
            bool threw = false;
            try { await foreach (var _ in r.Chunks.WithCancellation(mid.Token)) mid.Cancel(); } catch (OperationCanceledException) { threw = true; }
            SharedAssert.True(threw, "Mid-stream cancellation propagates.");
        }

        private static async Task NativeAsync(CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start(); using var c = Client(server);
            ResponsesApiResponse created = await c.CreateResponseAsync(J("[{\"role\":\"user\",\"content\":[{\"type\":\"input_text\",\"text\":\"hi\"}]}]"), token: token);
            SharedAssert.True(created.Success, created.Error ?? "Create."); SharedAssert.Equal("completed", created.Status, "Status.");
            SharedAssert.True((await c.GetResponseAsync("oai-resp", token)).Stored == true, "Get.");
            SharedAssert.True((await c.ListResponseInputItemsAsync("oai-resp", 5, "desc", "x", token)).Success, "Input items.");
            SharedAssert.True(server.RequestUrls.Last().Contains("limit=5&order=desc&after=x", StringComparison.Ordinal), "Paging parameters.");
            SharedAssert.True((await c.DeleteResponseAsync("oai-resp", token)).Data!.Value.GetProperty("deleted").GetBoolean(), "Delete.");
            SharedAssert.Equal("cancelled", (await c.CancelResponseAsync("oai-resp", token)).Status, "Cancel.");
            var count = await c.CountInputTokensAsync(J("\"hi\""), token: token);
            SharedAssert.Equal(42, count.Data!.Value.GetProperty("input_tokens").GetInt32(), "Input tokens.");
            JsonElement countBody = Body(server);
            SharedAssert.False(countBody.TryGetProperty("stream", out _) || countBody.TryGetProperty("store", out _) || countBody.TryGetProperty("include", out _), "Input token body keeps only accepted fields.");
            var compact = await c.CompactConversationAsync(J("\"long\""), previousResponseId: "prev", instructions: "keep it", promptCacheKey: "pk", serviceTier: ServiceTier.Flex, token: token);
            SharedAssert.True(compact.Success, compact.Error ?? "Compact.");
            JsonElement cb = Body(server);
            SharedAssert.Equal("prev", cb.GetProperty("previous_response_id").GetString(), "Compact previous id."); SharedAssert.Equal("flex", cb.GetProperty("service_tier").GetString(), "Compact tier.");
        }

        private static async Task ContinueAsync(CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start(); using var c = Client(server);
            ResponsesApiResponse unstored = await c.CreateResponseAsync(J("\"hi\""), token: token);
            InvalidOperationException? ex = null;
            try { await c.ContinueResponseAsync(unstored, J("\"more\""), token: token); } catch (InvalidOperationException e) { ex = e; }
            SharedAssert.NotNull(ex, "Continuing an unstored response throws.");
            SharedAssert.True(ex!.Message.Contains("Store = false", StringComparison.Ordinal), "Guidance names Store.");
            int sent = server.RequestBodies.Count;
            ResponsesApiResponse stored = await c.CreateResponseAsync(J("\"hi\""), new OpenAiCompletionOptions { Store = true }, token);
            SharedAssert.True(stored.Stored == true, "Stored recorded.");
            ResponsesApiResponse next = await c.ContinueResponseAsync(stored, J("\"more\""), token: token);
            SharedAssert.True(next.Success, next.Error ?? "Continue.");
            SharedAssert.Equal("oai-resp", Body(server).GetProperty("previous_response_id").GetString(), "previous_response_id sent.");
            SharedAssert.Equal(sent + 2, server.RequestBodies.Count, "The rejected continuation sent nothing.");
        }

        private static async Task EventKindsAsync(CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start(); using var c = Client(server);
            var r = await c.CreateResponseStreamingAsync(J("\"oai-citations\""), token: token);
            List<ResponsesApiEvent> events = new(); await foreach (var e in r.Events.WithCancellation(token)) events.Add(e);
            SharedAssert.True(r.Success, r.Error ?? "Native stream."); SharedAssert.NotNull(r.Response, "Final response.");
            var kinds = events.Select(e => e.Kind).ToHashSet();
            foreach (ResponsesApiEventKind kind in new[] { ResponsesApiEventKind.Created, ResponsesApiEventKind.InProgress, ResponsesApiEventKind.OutputItemAdded, ResponsesApiEventKind.OutputItemDone,
                ResponsesApiEventKind.ContentPartAdded, ResponsesApiEventKind.ContentPartDone, ResponsesApiEventKind.TextDelta, ResponsesApiEventKind.TextDone, ResponsesApiEventKind.AnnotationAdded,
                ResponsesApiEventKind.ReasoningSummaryPartAdded, ResponsesApiEventKind.ReasoningSummaryPartDone, ResponsesApiEventKind.ReasoningSummaryDelta, ResponsesApiEventKind.ReasoningSummaryDone,
                ResponsesApiEventKind.HostedToolProgress, ResponsesApiEventKind.Compacting, ResponsesApiEventKind.Completed, ResponsesApiEventKind.Other })
                SharedAssert.True(kinds.Contains(kind), "Event kind " + kind + " seen.");
            SharedAssert.Equal("response.future_event", events.First(e => e.Kind == ResponsesApiEventKind.Other).Type, "Raw name kept for unknown events.");
            var table = new Dictionary<string, ResponsesApiEventKind>
            {
                ["response.queued"] = ResponsesApiEventKind.Queued, ["response.refusal.delta"] = ResponsesApiEventKind.RefusalDelta, ["response.refusal.done"] = ResponsesApiEventKind.RefusalDone,
                ["response.reasoning_text.delta"] = ResponsesApiEventKind.ReasoningDelta, ["response.reasoning_text.done"] = ResponsesApiEventKind.ReasoningDone,
                ["response.function_call_arguments.delta"] = ResponsesApiEventKind.FunctionCallArgumentsDelta, ["response.function_call_arguments.done"] = ResponsesApiEventKind.FunctionCallArgumentsDone,
                ["response.custom_tool_call_input.delta"] = ResponsesApiEventKind.CustomToolInputDelta, ["response.custom_tool_call_input.done"] = ResponsesApiEventKind.CustomToolInputDone,
                ["response.file_search_call.searching"] = ResponsesApiEventKind.HostedToolProgress, ["response.code_interpreter_call_code.delta"] = ResponsesApiEventKind.HostedToolProgress,
                ["response.image_generation_call.partial_image"] = ResponsesApiEventKind.HostedToolProgress, ["response.mcp_call_arguments.delta"] = ResponsesApiEventKind.HostedToolProgress,
                ["response.mcp_list_tools.completed"] = ResponsesApiEventKind.HostedToolProgress, ["response.shell_call_command.delta"] = ResponsesApiEventKind.HostedToolProgress,
                ["response.incomplete"] = ResponsesApiEventKind.Incomplete, ["response.failed"] = ResponsesApiEventKind.Failed, ["error"] = ResponsesApiEventKind.Error,
                ["response.done"] = ResponsesApiEventKind.Other, ["response.audio.delta"] = ResponsesApiEventKind.Other,
            };
            foreach (var pair in table) SharedAssert.Equal(pair.Value, ResponsesApiEvent.KindOf(pair.Key), pair.Key);
        }

        private static async Task CitationsAsync(CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start(); using var c = Client(server);
            ChatResponse r = await c.ChatAsync("oai-citations", token: token);
            SharedAssert.True(r.Success, r.Error ?? "Citations."); SharedAssert.Equal(LocalOpenAiResponsesRoutes.CitedText + " More.", r.Text, "Text concatenates parts.");
            SharedAssert.Equal(3, r.Citations.Count, "Three annotations.");
            Citation url = r.Citations[0];
            SharedAssert.Equal(CitationKind.Url, url.Kind, "url_citation kind."); SharedAssert.Equal("https://example.com/source", url.Url, "Url."); SharedAssert.Equal("Example Source", url.Title, "Title.");
            SharedAssert.Equal("see source", r.Text!.Substring(url.TextStart!.Value, url.TextEnd!.Value - url.TextStart.Value), "UTF-16 span over non-BMP text.");
            Citation container = r.Citations[1];
            SharedAssert.Equal(CitationKind.File, container.Kind, "container_file_citation kind.");
            SharedAssert.Equal("More", r.Text.Substring(container.TextStart!.Value, container.TextEnd!.Value - container.TextStart.Value), "Part-relative offsets are shifted.");
            SharedAssert.Equal("file-1", container.Source!.DocumentId, "File id in Source.");
            SharedAssert.True(r.Citations[2].TextStart == null, "file_citation has no span.");
            SharedAssert.Equal("file_citation", r.Citations[2].Raw.GetProperty("type").GetString(), "Raw kept.");
            ChatResponse none = await c.ChatAsync("hello", token: token);
            SharedAssert.NotNull(none.Citations, "Never null."); SharedAssert.Equal(0, none.Citations.Count, "Empty without annotations.");
        }

        private static async Task CitationsStreamAsync(CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start(); using var c = Client(server);
            var r = await c.ChatStreamingAsync("oai-citations", token: token); await foreach (var _ in r.Chunks.WithCancellation(token)) { }
            SharedAssert.True(r.Success, r.Error ?? "Stream."); SharedAssert.Equal(3, r.Citations.Count, "Citations on the final streaming response.");
        }

        private static async Task RawResponseAsync(CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start(); using var c = Client(server);
            foreach (CallDetailCapture capture in new[] { CallDetailCapture.None, CallDetailCapture.Metadata, CallDetailCapture.Full })
            {
                c.ClearCallDetails(); c.CallDetailCapture = capture;
                ChatResponse r = await c.ChatAsync("hello", token: token);
                SharedAssert.Equal("oai-resp", r.RawResponse!.Value.GetProperty("id").GetString(), "RawResponse is the provider JSON.");
                if (capture == CallDetailCapture.Full) SharedAssert.True(c.CallDetails.Last().ResponseBody != null, "Full capture keeps the wire body, not a RawResponse copy.");
                if (capture == CallDetailCapture.Metadata) SharedAssert.True(c.CallDetails.Last().ResponseBody == null, "Metadata capture never includes RawResponse.");
                if (capture == CallDetailCapture.None) SharedAssert.Equal(0, c.CallDetails.Count, "None records nothing.");
            }
        }

        private static async Task EmbeddingsAsync(CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start(); using var c = Client(server);
            EmbeddingResponse e = await c.EmbedAsync(new List<string> { "a", "b" }, token: token);
            SharedAssert.True(e.Success, e.Error ?? "Embeddings."); SharedAssert.Equal(0, e.Embeddings[0].Index, "Sorted.");
            SharedAssert.Equal("/v1/embeddings", server.RequestPaths.Last(), "Embeddings route.");
            int n = 0; await foreach (var _ in c.ListModelsAsync(token)) n++;
            SharedAssert.Equal(1, n, "Models listed."); SharedAssert.True(await c.ModelExistsAsync("test-model", token), "Model exists.");
            SharedAssert.True(await c.ValidateConnectivityAsync(token), "Connectivity probes /v1/models.");
            await SharedAssert.ThrowsAsync<CapabilityNotSupportedException>(() => c.RerankAsync("q", new List<string> { "d" }, token: token), "Rerank is unsupported.");
        }

        private static async Task BackgroundAsync(CancellationToken token)
        {
            using var server = LocalOpenAiTestServer.Start(); using var c = Client(server);
            await SharedAssert.ThrowsAsync<ArgumentException>(() => c.CreateResponseAsync(J("\"hi\""), new OpenAiCompletionOptions { Background = true }, token), "Background requires Store.");
            ResponsesApiResponse queued = await c.CreateResponseAsync(J("\"hi\""), new OpenAiCompletionOptions { Background = true, Store = true }, token);
            SharedAssert.True(queued.Success, queued.Error ?? "Background create."); SharedAssert.Equal("queued", queued.Status, "Queued status.");
            SharedAssert.Equal("queued", queued.FinishReason, "Queued finish reason.");
        }
    }
}
