namespace Test.Shared
{
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using Touchstone.Core;

    internal static class LocalStreamingLifecycleCases
    {
        internal static IEnumerable<TestCaseDescriptor> Create(string suite)
        {
            for (int index = 0; index < 9; index++)
                foreach (string operation in new[] { "chat", "tools", "generate" })
                {
                    int providerIndex = index;
                    string op = operation;
                    yield return new TestCaseDescriptor(suite, "stream_horizontal_" + index + "_" + op,
                        "Shared streaming lifecycle provider " + index + " " + op,
                        t => RunHorizontalAsync(providerIndex, op, t), new[] { "local", "streaming" });
                }
            string[] scenarios = { "complete", "complete-no-newline", "terminal-only", "missing-done", "empty", "whitespace",
                "truncated-first", "partial-truncated", "malformed-then-done", "error-first", "error-after-partial", "error-then-done",
                "nonobject", "missing-done-property", "done-string-true", "after-terminal", "duplicate-terminal", "http-401", "http-500",
                "cancel-after-partial", "transport-cut", "consumer-abandons", "error-secret", "error-long", "invalid-utf8" };
            foreach (bool cloud in new[] { false, true })
                foreach (string operation in new[] { "chat", "tools", "generate" })
                    foreach (string scenario in scenarios)
                        foreach (bool telemetry in new[] { false, true })
                        {
                            bool c = cloud;
                            string op = operation;
                            string fault = scenario;
                            bool tel = telemetry;
                            string id = "stream_lifecycle_" + (c ? "cloud_" : "ollama_") + op + "_" + fault + "_" + tel;
                            yield return new TestCaseDescriptor(suite, id, id, t => RunAsync(c, op, fault, tel, t), new[] { "local", "streaming" });
                        }
        }

        private static async Task RunAsync(bool cloud, string operation, string scenario, bool telemetry, CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using TelemetryCapture? capture = telemetry ? new TelemetryCapture("stream_lifecycle") : null;
            using StreamingCloudTransport handler = new StreamingCloudTransport(server.Endpoint);
            using HttpClient http = new HttpClient(handler);
            http.DefaultRequestHeaders.Add("x-stream-fault", scenario);
            using CompletionClientBase client = cloud ? new OllamaCloudCompletionClient("synthetic-cloud-token", httpClient: http) :
                new OllamaCompletionClient(server.Endpoint, "synthetic-cloud-token", httpClient: http);
            client.TimeoutMs = 3000;
            OllamaCompletionOptions options = new OllamaCompletionOptions { Model = "fixture-model" };
            using CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
            StreamingResponseBase response;
            if (operation == "chat") response = await client.ChatStreamingAsync("hello", options, token).ConfigureAwait(false);
            else if (operation == "generate") response = await client.GenerateStreamingAsync("hello", options, token).ConfigureAwait(false);
            else response = await client.ToolChatStreamingAsync(new ToolChatRequest
            {
                Messages = new List<ChatMessage> { ChatMessage.User("hello") }, Options = options,
                Tools = new List<ToolDefinition> { ToolDefinition.Function("probe", "Never executed", new Dictionary<string, object> { ["type"] = "object" }) }
            }, token).ConfigureAwait(false);
            bool httpFailure = scenario.StartsWith("http-", StringComparison.Ordinal);
            SharedAssert.Equal(httpFailure ? StreamingState.Failed : StreamingState.Open, response.State, "Initial lifecycle");
            SharedAssert.Equal(!httpFailure, response.Success, "Success reports opening");
            int count = 0;
            string text = "";
            bool cancelled = false;
            try
            {
                if (response is ChatStreamingResponse chat)
                    await foreach (ChatStreamingChunk chunk in chat.Chunks.WithCancellation(cancel.Token).ConfigureAwait(false))
                    {
                        Observe(response, ref count, ref text, chunk.Text);
                        if (scenario == "consumer-abandons") break;
                        if (scenario == "cancel-after-partial") cancel.Cancel();
                    }
                else if (response is ToolChatStreamingResponse tools)
                    await foreach (ToolChatStreamingChunk chunk in tools.Chunks.WithCancellation(cancel.Token).ConfigureAwait(false))
                    {
                        Observe(response, ref count, ref text, chunk.Text);
                        if (scenario == "consumer-abandons") break;
                        if (scenario == "cancel-after-partial") cancel.Cancel();
                    }
                else if (response is GenerationStreamingResponse generation)
                    await foreach (GenerationStreamingChunk chunk in generation.Chunks.WithCancellation(cancel.Token).ConfigureAwait(false))
                    {
                        Observe(response, ref count, ref text, chunk.Text);
                        if (scenario == "consumer-abandons") break;
                        if (scenario == "cancel-after-partial") cancel.Cancel();
                    }
            }
            catch (OperationCanceledException) { cancelled = true; }
            bool valid = scenario is "complete" or "complete-no-newline" or "terminal-only" or "after-terminal" or "duplicate-terminal";
            StreamingState expected = valid ? StreamingState.Completed : scenario == "consumer-abandons" ? StreamingState.Abandoned :
                scenario == "cancel-after-partial" ? StreamingState.Cancelled : StreamingState.Failed;
            SharedAssert.Equal(expected, response.State, "Final lifecycle " + scenario);
            SharedAssert.Equal(scenario == "cancel-after-partial", cancelled, "Only caller cancellation throws");
            if (expected == StreamingState.Failed) SharedAssert.NotEmpty(response.StreamError, "Failure diagnostic survives");
            else SharedAssert.Equal<string?>(null, response.StreamError, "Nonfailures have no diagnostic");
            if (scenario is "error-first" or "error-after-partial" or "error-then-done" or "http-401" or "http-500")
                SharedAssert.True(response.StreamError!.Contains("synthetic provider failure", StringComparison.Ordinal), "Native error retained");
            if (scenario == "error-secret")
            {
                SharedAssert.False(response.StreamError!.Contains("synthetic-cloud-token", StringComparison.Ordinal), "Bearer token redacted");
                SharedAssert.True(response.StreamError.Contains("[redacted]", StringComparison.Ordinal), "Redaction is explicit");
            }
            if (scenario == "error-long") SharedAssert.True(response.StreamError!.Length < 1150, "Provider diagnostics are bounded");
            if (valid)
            {
                SharedAssert.Equal(scenario == "terminal-only" ? 1 : 2, count, "Terminal policy excludes trailing records");
                SharedAssert.Equal(scenario == "terminal-only" ? "" : "hello", text, "Output preserved");
                TokenUsage? usage = response is ChatStreamingResponse a ? a.Usage : response is ToolChatStreamingResponse b ? b.Usage : ((GenerationStreamingResponse)response).Usage;
                SharedAssert.Equal(2, usage!.CompletionTokens!.Value, "Final usage preserved");
            }
            if (response is ToolChatStreamingResponse toolResponse && text == "hello")
            {
                SharedAssert.Equal(1, toolResponse.ToolCalls.Count, "Partial calls retained for diagnostics");
                SharedAssert.Equal(1, toolResponse.ToAssistantMessage().ToolCalls.Count, "Accessor remains compatible");
            }
            SharedAssert.Equal(1, handler.RequestCount, "No retries");
            client.Dispose();
            SharedAssert.False(handler.Disposed, "Injected HttpClient remains caller-owned");
            if (capture != null)
            {
                string name = (cloud ? "ollamacloud " : "ollama ") + (operation == "tools" ? "tool_chat_stream" : operation == "generate" ? "generate_stream" : "chat_stream");
                System.Diagnostics.Activity span = capture.Span(name);
                string outcome = expected == StreamingState.Completed ? "success" : expected == StreamingState.Abandoned ? "abandoned" : expected == StreamingState.Cancelled ? "cancelled" : "error";
                SharedAssert.Equal(outcome, span.GetTagItem("polyprompt.outcome")?.ToString(), "Telemetry follows lifecycle");
            }
        }

        private static async Task RunHorizontalAsync(int index, string operation, CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            List<CompletionClientBase> clients = LocalClients.Completion(server.Endpoint);
            try
            {
                CompletionClientBase client = clients[index];
                client.Model = "test-model";
                foreach (string mode in new[] { "complete", "abandon", "request-cancel", "consumer-cancel", "pre-request-cancel", "pre-consumer-cancel" })
                {
                    using CancellationTokenSource request = CancellationTokenSource.CreateLinkedTokenSource(token);
                    using CancellationTokenSource consumer = CancellationTokenSource.CreateLinkedTokenSource(token);
                    StreamingResponseBase response = operation == "chat" ? await client.ChatStreamingAsync("hello", token: request.Token).ConfigureAwait(false) :
                        operation == "generate" ? await client.GenerateStreamingAsync("hello", token: request.Token).ConfigureAwait(false) :
                        await client.ToolChatStreamingAsync(new ToolChatRequest { Messages = new List<ChatMessage> { ChatMessage.User("hello") } }, request.Token).ConfigureAwait(false);
                    SharedAssert.Equal(StreamingState.Open, response.State, client.GetType().Name + " opens");
                    bool cancelled = false;
                    if (mode == "pre-request-cancel") request.Cancel();
                    if (mode == "pre-consumer-cancel") consumer.Cancel();
                    try
                    {
                        await foreach (object chunk in Chunks(response, consumer.Token).ConfigureAwait(false))
                        {
                            SharedAssert.Equal(StreamingState.Streaming, response.State, "Enumeration stays provisional");
                            if (mode == "abandon") break;
                            if (mode == "request-cancel") request.Cancel();
                            if (mode == "consumer-cancel") consumer.Cancel();
                        }
                    }
                    catch (OperationCanceledException) { cancelled = true; }
                    StreamingState expected = mode == "complete" ? StreamingState.Completed : mode == "abandon" ? StreamingState.Abandoned : StreamingState.Cancelled;
                    SharedAssert.Equal(expected, response.State, client.GetType().Name + " " + operation + " " + mode + ": " + response.StreamError);
                    SharedAssert.Equal(expected == StreamingState.Cancelled, cancelled, "Cancellation propagates");
                    if (expected == StreamingState.Completed)
                        await SharedAssert.ThrowsAsync<InvalidOperationException>(async () =>
                        {
                            await foreach (object chunk in Chunks(response, token).ConfigureAwait(false)) { }
                        }, "Completed streams cannot be enumerated again.").ConfigureAwait(false);
                }
            }
            finally
            {
                foreach (CompletionClientBase client in clients) client.Dispose();
            }
        }

        private static async IAsyncEnumerable<object> Chunks(StreamingResponseBase response,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
        {
            if (response is ChatStreamingResponse chat)
                await foreach (ChatStreamingChunk chunk in chat.Chunks.WithCancellation(token).ConfigureAwait(false)) yield return chunk;
            else if (response is ToolChatStreamingResponse tools)
                await foreach (ToolChatStreamingChunk chunk in tools.Chunks.WithCancellation(token).ConfigureAwait(false)) yield return chunk;
            else if (response is GenerationStreamingResponse generation)
                await foreach (GenerationStreamingChunk chunk in generation.Chunks.WithCancellation(token).ConfigureAwait(false)) yield return chunk;
        }

        private static void Observe(StreamingResponseBase response, ref int count, ref string text, string? delta)
        {
            SharedAssert.Equal(StreamingState.Streaming, response.State, "State remains provisional inside enumeration");
            count++;
            text += delta;
        }
    }
}
