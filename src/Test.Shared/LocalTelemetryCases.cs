namespace Test.Shared
{
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Globalization;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using PolyPrompt.Telemetry;
    using SyslogLogging;
    using Touchstone.Core;
    using N = PolyPrompt.Telemetry.PolyPromptTelemetryNames;

    /// <summary>
    /// Deterministic cases proving PolyPrompt emits its documented telemetry: the instrument catalog and build info, the
    /// operation and HTTP spans and metrics for every capability, streaming time-to-first-chunk and completion, decision
    /// batch queueing, credential refresh, model management, provider labels, in-flight gauges, W3C propagation, and the
    /// failure paths (HTTP errors, transport failures, invalid responses, timeouts, cancellation, abandoned streams). Also
    /// proves the no-listener path and a faulting listener never break a call. Registered into the local behavior suite.
    /// </summary>
    internal static class LocalTelemetryCases
    {
        public static IEnumerable<TestCaseDescriptor> Create(string suiteId)
        {
            return new List<TestCaseDescriptor>
            {
                Case(suiteId, "tel_catalog", "The PolyPrompt meter publishes every documented instrument and the build-info gauge reports the version", RunCatalogAsync),
                Case(suiteId, "tel_no_listener", "Every instrumented path runs normally with no telemetry listener attached", RunNoListenerAsync),
                Case(suiteId, "tel_listener_faults", "A listener that throws from its callbacks never breaks a client call", RunListenerFaultsAsync),
                Case(suiteId, "tel_tool_chat", "Tool chat emits a client span with GenAI attributes, a child HTTP span, token, tool-call, finish-reason, and HTTP metrics, and propagates traceparent", RunToolChatAsync),
                Case(suiteId, "tel_streaming", "A streaming chat keeps its span open until the stream ends and records time to first chunk, chunks, and usage", RunStreamingAsync),
                Case(suiteId, "tel_stream_abandoned", "A stream the consumer stops early ends its span with outcome abandoned", RunStreamAbandonedAsync),
                Case(suiteId, "tel_http_error", "HTTP error statuses mark the operation and HTTP spans as errors with error.type set to the status", RunHttpErrorAsync),
                Case(suiteId, "tel_transport_failure", "A transport failure records the exception type as error.type and an exception event on the HTTP span", RunTransportFailureAsync),
                Case(suiteId, "tel_timeout_cancel", "Timeouts and caller cancellation are told apart on streaming and non-streaming operations", RunTimeoutCancelAsync),
                Case(suiteId, "tel_invalid_response", "A successful status with an unusable body is recorded as invalid_response", RunInvalidResponseAsync),
                Case(suiteId, "tel_capabilities", "Embedding, sparse embedding, rerank, and classification emit spans, GenAI durations, and batch sizes", RunCapabilitiesAsync),
                Case(suiteId, "tel_decision_batch", "A decision batch emits a root span, a queued stage and decide span per item, queue wait, in-flight, and question metrics", RunDecisionBatchAsync),
                Case(suiteId, "tel_credentials", "Credential refresh emits a token span under the operation plus cache hit, miss, and failure metrics", RunCredentialsAsync),
                Case(suiteId, "tel_model_ops", "Model existence, information, pull, delete, and connectivity emit operation spans with HTTP children and failure outcomes", RunModelOpsAsync),
                Case(suiteId, "tel_provider_labels", "Every client reports its documented provider label on its spans", RunProviderLabelsAsync),
                Case(suiteId, "tel_gauges", "The active clients, active operations, and active HTTP request gauges track live work", RunGaugesAsync),
                Case(suiteId, "tel_logging_isolation", "SyslogLogging's own meter and activity source stay separate from PolyPrompt's when both are subscribed", RunLoggingIsolationAsync),
            };
        }

        private static TestCaseDescriptor Case(string suiteId, string caseId, string displayName, Func<CancellationToken, Task> executeAsync)
        {
            return new TestCaseDescriptor(suiteId, caseId, displayName, executeAsync, new[] { "local" });
        }

        #region Catalog-and-Safety

        private static Task RunCatalogAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            using (OpenAiCompletionClient warm = new OpenAiCompletionClient("http://127.0.0.1:1", "k"))
            {
            }

            using TelemetryCapture capture = new TelemetryCapture("tel_catalog");

            SharedAssert.Equal("PolyPrompt", N.MeterName, "The meter name is a public contract.");
            SharedAssert.Equal("PolyPrompt", N.ActivitySourceName, "The activity source name is a public contract.");

            List<string> documented = new List<string>
            {
                N.OperationDuration, N.Operations, N.ActiveOperations, N.GenAiOperationDuration, N.GenAiTokenUsage, N.Tokens,
                N.StreamTimeToFirstChunk, N.StreamChunks, N.BatchSize, N.ToolCalls, N.FinishReasons,
                N.HttpRequestDuration, N.HttpRequests, N.HttpActiveRequests, N.HttpRequestBodySize, N.HttpResponseBodySize,
                N.DecisionQueueDuration, N.DecisionInFlight, N.DecisionQueued, N.DecisionQuestions,
                N.CredentialRefreshDuration, N.CredentialRefreshes, N.CredentialCacheLookups, N.ActiveClients, N.BuildInfo,
            };

            List<string> published = capture.Instruments;
            SharedAssert.Equal(documented.Count, published.Count, "The meter should publish exactly the documented instruments. Published: " + string.Join(", ", published));
            foreach (string name in documented)
            {
                SharedAssert.True(published.Contains(name), "Instrument '" + name + "' should be published on the PolyPrompt meter.");
            }

            capture.Observe();
            CapturedMeasurement build = capture.Require(N.BuildInfo);
            SharedAssert.Equal(1.0, build.Value, "Build info should report 1.");
            string version = build.Tags[N.Version];
            SharedAssert.True(!string.IsNullOrEmpty(version) && version != "unknown" && !version.Contains('+', StringComparison.Ordinal), "Build info should carry the clean library version, got '" + version + "'.");
            return Task.CompletedTask;
        }

        private static async Task RunNoListenerAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiCompletionClient chat = NewOpenAi(server.Endpoint, "test-model");
            using OpenAiEmbeddingClient embed = new OpenAiEmbeddingClient(server.Endpoint, "test-key");
            embed.Model = "test-embed";
            using TypeSafeDecisionClient decide = new TypeSafeDecisionClient(server.Endpoint, "ts-key");
            using OllamaModelClient models = new OllamaModelClient(server.Endpoint, "test-key");

            SharedAssert.True((await chat.ChatAsync("ping", token: token).ConfigureAwait(false)).Success, "Chat should succeed without a listener.");
            ChatStreamingResponse stream = await chat.ChatStreamingAsync("normal stream", token: token).ConfigureAwait(false);
            await foreach (ChatStreamingChunk chunk in stream.Chunks.WithCancellation(token).ConfigureAwait(false))
            {
            }
            SharedAssert.True(stream.Success && stream.ChunkCount >= 2, "Streaming should succeed without a listener.");
            SharedAssert.True((await embed.EmbedAsync(new List<string> { "a", "b" }, token: token).ConfigureAwait(false)).Success, "Embedding should succeed without a listener.");
            List<DecisionResponse> batch = await decide.DecideAsync(new List<DecisionRequest> { Decision("ok"), Decision("ok") }, token: token).ConfigureAwait(false);
            SharedAssert.True(batch.All(r => r.Success), "A decision batch should succeed without a listener.");
            SharedAssert.True(await models.ModelExistsAsync("test-model", token).ConfigureAwait(false), "Model lookup should succeed without a listener.");
            SharedAssert.True(await models.ValidateConnectivityAsync(token).ConfigureAwait(false), "Connectivity should succeed without a listener.");
        }

        private static async Task RunListenerFaultsAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiCompletionClient client = NewOpenAi(server.Endpoint, "test-model");
            using TypeSafeDecisionClient decide = new TypeSafeDecisionClient(server.Endpoint, "ts-key");

            // Open a trace for this test and make both listeners throw only inside it, so concurrently running tests
            // are unaffected.
            using TelemetryCapture capture = new TelemetryCapture("tel_listener_faults");
            ActivityTraceId trace = capture.Root.TraceId;

            using MeterListener faulty = new MeterListener();
            faulty.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == N.MeterName) listener.EnableMeasurementEvents(instrument);
            };
            faulty.SetMeasurementEventCallback<double>((i, v, t, s) =>
            {
                if (Activity.Current?.TraceId == trace) throw new InvalidOperationException("faulty metric listener");
            });
            faulty.SetMeasurementEventCallback<long>((i, v, t, s) =>
            {
                if (Activity.Current?.TraceId == trace) throw new InvalidOperationException("faulty metric listener");
            });
            faulty.Start();

            using ActivityListener faultySpans = new ActivityListener
            {
                ShouldListenTo = source => source.Name == N.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    if (activity.TraceId == trace) throw new InvalidOperationException("faulty span listener");
                },
            };
            ActivitySource.AddActivityListener(faultySpans);

            ChatResponse chat = await client.ChatAsync("ping", token: token).ConfigureAwait(false);
            SharedAssert.True(chat.Success && chat.Text == "pong", "A faulting listener must not break a chat call.");

            ChatStreamingResponse stream = await client.ChatStreamingAsync("normal stream", token: token).ConfigureAwait(false);
            await foreach (ChatStreamingChunk chunk in stream.Chunks.WithCancellation(token).ConfigureAwait(false))
            {
            }
            SharedAssert.True(stream.Success, "A faulting listener must not break a streaming call.");

            List<DecisionResponse> batch = await decide.DecideAsync(new List<DecisionRequest> { Decision("ok"), Decision("ok") }, token: token).ConfigureAwait(false);
            SharedAssert.True(batch.All(r => r.Success), "A faulting listener must not break a decision batch.");
            SharedAssert.True(capture.Spans("openai chat").Count == 1, "Spans should still stop and reach other listeners after a faulty listener throws.");
        }

        #endregion

        #region Completion

        private static async Task RunToolChatAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            string model = UniqueModel("toolchat");
            using OpenAiCompletionClient client = NewOpenAi(server.Endpoint, model);
            using TelemetryCapture capture = new TelemetryCapture("tel_tool_chat");

            ToolChatRequest request = new ToolChatRequest();
            request.Messages.Add(ChatMessage.User("What is the weather in Seattle?"));
            request.Tools.Add(ToolDefinition.Function("get_weather", "Get weather.", new Dictionary<string, object> { { "type", "object" } }));
            request.Options = new CompletionOptions { MaxTokens = 256, Temperature = 0.2 };

            ToolChatResponse response = await client.ToolChatAsync(request, token).ConfigureAwait(false);
            SharedAssert.True(response.Success && response.ToolCalls.Count == 1, "The tool chat should succeed with one tool call.");

            Activity op = capture.Span("openai tool_chat");
            SharedAssert.Equal(ActivityKind.Client, op.Kind, "The operation span should be a client span.");
            SharedAssert.Equal(capture.Root.SpanId, op.ParentSpanId, "The operation span should nest under the caller's span.");
            SharedAssert.Equal(ActivityStatusCode.Ok, op.Status, "A successful operation span should have status Ok.");
            SharedAssert.Equal("openai", Tag(op, N.Provider), "Provider attribute.");
            SharedAssert.Equal("chat", Tag(op, N.GenAiOperationName), "GenAI operation name.");
            SharedAssert.Equal("tool_chat", Tag(op, N.Operation), "PolyPrompt operation.");
            SharedAssert.Equal(model, Tag(op, N.RequestModel), "Request model attribute.");
            SharedAssert.Equal("256", Tag(op, N.RequestMaxTokens), "Max tokens attribute.");
            SharedAssert.Equal("0.2", Tag(op, N.RequestTemperature), "Temperature attribute.");
            SharedAssert.Equal("1", Tag(op, N.ToolDefinitionCount), "Tool definition count attribute.");
            SharedAssert.Equal("1", Tag(op, N.ToolCallCount), "Tool call count attribute.");
            SharedAssert.Equal("11", Tag(op, N.UsageInputTokens), "Input tokens attribute.");
            SharedAssert.Equal("7", Tag(op, N.UsageOutputTokens), "Output tokens attribute.");
            SharedAssert.Equal("8", Tag(op, N.UsageCachedInputTokens), "Cached input tokens attribute.");
            SharedAssert.Equal("chatcmpl-tool-local", Tag(op, N.ResponseId), "Response id attribute.");
            SharedAssert.Equal("127.0.0.1", Tag(op, N.ServerAddress), "Server address attribute.");

            Activity http = capture.Span("POST");
            SharedAssert.Equal(op.SpanId, http.ParentSpanId, "The HTTP span should nest under the operation span.");
            SharedAssert.Equal(ActivityKind.Client, http.Kind, "The HTTP span should be a client span.");
            SharedAssert.Equal("200", Tag(http, N.HttpStatusCode), "HTTP status attribute.");
            SharedAssert.Equal(server.Endpoint + "/v1/chat/completions", Tag(http, N.UrlFull), "url.full attribute.");

            string? traceparent = server.RequestHeaders.Last()
                .FirstOrDefault(h => string.Equals(h.Key, "traceparent", StringComparison.OrdinalIgnoreCase)).Value;
            SharedAssert.True(traceparent != null && traceparent.Contains(capture.Root.TraceId.ToHexString(), StringComparison.Ordinal),
                "The outbound request should carry a W3C traceparent in the caller's trace, got '" + traceparent + "'.");

            string port = new Uri(server.Endpoint).Port.ToString(CultureInfo.InvariantCulture);
            capture.Require(N.OperationDuration, N.Provider, "openai", N.Capability, "completion", N.Operation, "tool_chat", N.Outcome, "success");
            capture.Require(N.Operations, N.Provider, "openai", N.Operation, "tool_chat", N.Outcome, "success");
            capture.Require(N.GenAiOperationDuration, N.GenAiOperationName, "chat", N.Provider, "openai", N.RequestModel, model);
            SharedAssert.Equal(11.0, capture.Require(N.GenAiTokenUsage, N.RequestModel, model, N.GenAiTokenType, "input").Value, "GenAI input token usage.");
            SharedAssert.Equal(7.0, capture.Require(N.GenAiTokenUsage, N.RequestModel, model, N.GenAiTokenType, "output").Value, "GenAI output token usage.");
            SharedAssert.Equal(8.0, capture.Require(N.Tokens, N.RequestModel, model, N.TokenType, "cached_input").Value, "Cached input tokens.");
            SharedAssert.Equal(4.0, capture.Require(N.Tokens, N.RequestModel, model, N.TokenType, "reasoning").Value, "Reasoning tokens.");
            capture.Require(N.ToolCalls, N.Provider, "openai", N.Operation, "tool_chat");
            capture.Require(N.FinishReasons, N.Provider, "openai", N.FinishReasonLabel, "tool_calls");
            capture.Require(N.HttpRequestDuration, N.Provider, "openai", N.HttpMethod, "POST", N.HttpStatusCode, "200", N.ServerPort, port);
            capture.Require(N.HttpRequests, N.Provider, "openai", N.HttpStatusCode, "200", N.ServerPort, port);
            SharedAssert.True(capture.Require(N.HttpRequestBodySize, N.Provider, "openai").Value > 0, "Request body size should be positive.");
            SharedAssert.True(capture.Require(N.HttpResponseBodySize, N.Provider, "openai").Value > 0, "Response body size should be positive.");
        }

        private static async Task RunStreamingAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            string model = UniqueModel("stream");
            using OpenAiCompletionClient client = NewOpenAi(server.Endpoint, model);
            using TelemetryCapture capture = new TelemetryCapture("tel_streaming");

            ChatStreamingResponse stream = await client.ChatStreamingAsync("normal stream", token: token).ConfigureAwait(false);
            SharedAssert.True(stream.Success, "The stream should start.");
            SharedAssert.Equal(0, capture.Spans("openai chat_stream").Count, "The operation span should stay open until the stream is consumed.");

            int chunks = 0;
            await foreach (ChatStreamingChunk chunk in stream.Chunks.WithCancellation(token).ConfigureAwait(false)) chunks++;

            Activity op = capture.Span("openai chat_stream");
            SharedAssert.Equal("success", Tag(op, N.Outcome), "A consumed stream should succeed.");
            SharedAssert.Equal("2", Tag(op, N.StreamChunkCount), "The span should count content chunks.");
            SharedAssert.True(Tag(op, N.StreamTimeToFirstChunkMs) != null, "The span should record time to first chunk.");
            SharedAssert.Equal("3", Tag(op, N.UsageInputTokens), "Usage from the final chunk should reach the span.");
            SharedAssert.True(op.Duration > TimeSpan.Zero, "The span should have a duration.");

            Activity http = capture.Span("POST");
            SharedAssert.Equal(op.SpanId, http.ParentSpanId, "The streaming HTTP span should nest under the operation span.");
            SharedAssert.Equal("200", Tag(http, N.HttpStatusCode), "The streaming HTTP span should record the status.");

            capture.Require(N.StreamTimeToFirstChunk, N.Provider, "openai", N.Operation, "chat_stream");
            SharedAssert.Equal(2.0, capture.Require(N.StreamChunks, N.Provider, "openai", N.Operation, "chat_stream").Value, "Chunk counter.");
            SharedAssert.Equal(2.0, capture.Require(N.GenAiTokenUsage, N.RequestModel, model, N.GenAiTokenType, "output").Value, "Streaming output tokens.");
            capture.Require(N.FinishReasons, N.Provider, "openai", N.Operation, "chat_stream", N.FinishReasonLabel, "stop");
            capture.Require(N.OperationDuration, N.Provider, "openai", N.Operation, "chat_stream", N.Outcome, "success");
        }

        private static async Task RunStreamAbandonedAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiCompletionClient client = NewOpenAi(server.Endpoint, UniqueModel("abandon"));
            using TelemetryCapture capture = new TelemetryCapture("tel_stream_abandoned");

            ChatStreamingResponse stream = await client.ChatStreamingAsync("normal stream", token: token).ConfigureAwait(false);
            await foreach (ChatStreamingChunk chunk in stream.Chunks.WithCancellation(token).ConfigureAwait(false))
            {
                break;
            }

            Activity op = capture.Span("openai chat_stream");
            SharedAssert.Equal("abandoned", Tag(op, N.Outcome), "A stream stopped early should end as abandoned.");
            capture.Require(N.OperationDuration, N.Provider, "openai", N.Operation, "chat_stream", N.Outcome, "abandoned");
        }

        #endregion

        #region Failure-Paths

        private static async Task RunHttpErrorAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiCompletionClient client = NewOpenAi(server.Endpoint + "/missing", UniqueModel("err"));
            using TelemetryCapture capture = new TelemetryCapture("tel_http_error");

            ChatResponse chat = await client.ChatAsync("fail", token: token).ConfigureAwait(false);
            SharedAssert.Equal(404, chat.StatusCode, "The chat should fail with 404.");

            Activity op = capture.Span("openai chat");
            SharedAssert.Equal(ActivityStatusCode.Error, op.Status, "A failed operation span should have status Error.");
            SharedAssert.Equal("HTTP 404", op.StatusDescription, "The status description should name the HTTP status without the body.");
            SharedAssert.Equal("404", Tag(op, N.ErrorType), "error.type should be the status code.");
            Activity http = capture.Span("POST");
            SharedAssert.Equal(ActivityStatusCode.Error, http.Status, "The HTTP span should have status Error.");

            GenerationStreamingResponse generation = await client.GenerateStreamingAsync("fail", token: token).ConfigureAwait(false);
            SharedAssert.False(generation.Success, "The streaming generation should fail to start.");
            SharedAssert.Equal("404", Tag(capture.Span("openai generate_stream"), N.ErrorType), "A failed stream start should record error.type.");

            string port = new Uri(server.Endpoint).Port.ToString(CultureInfo.InvariantCulture);
            capture.Require(N.OperationDuration, N.Provider, "openai", N.Operation, "chat", N.Outcome, "error", N.ErrorType, "404");
            capture.Require(N.OperationDuration, N.Provider, "openai", N.Operation, "generate_stream", N.Outcome, "error", N.ErrorType, "404");
            capture.Require(N.HttpRequests, N.Provider, "openai", N.HttpStatusCode, "404", N.ErrorType, "404", N.ServerPort, port);
        }

        private static async Task RunTransportFailureAsync(CancellationToken token)
        {
            using OpenAiCompletionClient client = NewOpenAi("http://127.0.0.1:1", UniqueModel("down"));
            using TelemetryCapture capture = new TelemetryCapture("tel_transport_failure");

            ChatResponse chat = await client.ChatAsync("ping", token: token).ConfigureAwait(false);
            SharedAssert.False(chat.Success, "A chat against a closed port should fail.");

            string errorType = typeof(HttpRequestException).FullName!;
            Activity op = capture.Span("openai chat");
            SharedAssert.Equal(errorType, Tag(op, N.ErrorType), "The operation should record the transport exception type.");
            Activity http = capture.Span("POST");
            SharedAssert.Equal(errorType, Tag(http, N.ErrorType), "The HTTP span should record the exception type.");
            SharedAssert.True(http.Events.Any(e => e.Name == "exception" && e.Tags.Any(t => t.Key == "exception.type" && (string?)t.Value == errorType)),
                "The HTTP span should carry an exception event.");

            capture.Require(N.OperationDuration, N.Provider, "openai", N.Operation, "chat", N.Outcome, "error", N.ErrorType, errorType);
            capture.Require(N.HttpRequests, N.Provider, "openai", N.ErrorType, errorType, N.ServerPort, "1");
        }

        private static async Task RunTimeoutCancelAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using TelemetryCapture capture = new TelemetryCapture("tel_timeout_cancel");

            // Streaming timeout: the server sends one chunk and hangs; TimeoutMs covers the whole response.
            using (OpenAiCompletionClient timed = NewOpenAi(server.Endpoint, UniqueModel("timeout")))
            {
                timed.TimeoutMs = 300;
                ChatStreamingResponse stream = await timed.ChatStreamingAsync("hang stream", token: token).ConfigureAwait(false);
                await SharedAssert.ThrowsAsync<OperationCanceledException>(async () =>
                {
                    await foreach (ChatStreamingChunk chunk in stream.Chunks.WithCancellation(token).ConfigureAwait(false))
                    {
                    }
                }, "A hung stream should time out.").ConfigureAwait(false);
            }

            Activity timedOut = capture.Span("openai chat_stream");
            SharedAssert.Equal("timeout", Tag(timedOut, N.Outcome), "A stream that exceeded TimeoutMs should end as timeout.");
            SharedAssert.Equal("timeout", Tag(timedOut, N.ErrorType), "A timed out stream should record error.type timeout.");
            SharedAssert.Equal(ActivityStatusCode.Error, timedOut.Status, "A timed out stream should have status Error.");

            // Streaming cancellation by the caller.
            using (OpenAiCompletionClient cancelled = NewOpenAi(server.Endpoint, UniqueModel("cancel")))
            using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                cancelled.TimeoutMs = 10000;
                ChatStreamingResponse stream = await cancelled.ChatStreamingAsync("hang stream", token: cts.Token).ConfigureAwait(false);
                await SharedAssert.ThrowsAsync<OperationCanceledException>(async () =>
                {
                    await foreach (ChatStreamingChunk chunk in stream.Chunks.WithCancellation(cts.Token).ConfigureAwait(false))
                    {
                        cts.Cancel();
                    }
                }, "A cancelled stream should throw.").ConfigureAwait(false);
            }

            List<Activity> streams = capture.Spans("openai chat_stream");
            SharedAssert.Equal(2, streams.Count, "Both streams should have ended their spans.");
            SharedAssert.Equal("cancelled", Tag(streams[1], N.Outcome), "A stream cancelled by the caller should end as cancelled.");
            SharedAssert.True(Tag(streams[1], N.ErrorType) == null, "Cancellation is not an error.");

            // Non-streaming timeout: the decision route waits 150 ms on "slow".
            using (TypeSafeDecisionClient slow = new TypeSafeDecisionClient(server.Endpoint, "ts-key"))
            {
                slow.TimeoutMs = 50;
                await SharedAssert.ThrowsAsync<OperationCanceledException>(
                    () => slow.DecideAsync(Decision("slow"), null, token), "A slow decision should time out.").ConfigureAwait(false);
            }
            SharedAssert.Equal("timeout", Tag(capture.Span("typesafe decide"), N.Outcome), "A non-streaming timeout should end as timeout.");

            // Non-streaming caller cancellation.
            using (OpenAiCompletionClient client = NewOpenAi(server.Endpoint, UniqueModel("precancel")))
            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                cts.Cancel();
                await SharedAssert.ThrowsAsync<OperationCanceledException>(
                    () => client.ChatAsync("ping", null, cts.Token), "A cancelled chat should throw.").ConfigureAwait(false);
            }
            SharedAssert.Equal("cancelled", Tag(capture.Span("openai chat"), N.Outcome), "A cancelled chat should end as cancelled.");

            capture.Require(N.OperationDuration, N.Provider, "openai", N.Operation, "chat_stream", N.Outcome, "timeout", N.ErrorType, "timeout");
            capture.Require(N.OperationDuration, N.Provider, "openai", N.Operation, "chat_stream", N.Outcome, "cancelled");
            capture.Require(N.OperationDuration, N.Provider, "typesafe", N.Operation, "decide", N.Outcome, "timeout");
            capture.Require(N.HttpRequests, N.Provider, "typesafe", N.ErrorType, "timeout");
        }

        private static async Task RunInvalidResponseAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using TypeSafeDecisionClient client = new TypeSafeDecisionClient(server.Endpoint, "ts-key");
            using TelemetryCapture capture = new TelemetryCapture("tel_invalid_response");

            DecisionResponse response = await client.DecideAsync(Decision("notjson"), null, token).ConfigureAwait(false);
            SharedAssert.False(response.Success, "A non-JSON body should fail.");

            Activity op = capture.Span("typesafe decide");
            SharedAssert.Equal("invalid_response", Tag(op, N.ErrorType), "A 200 with an unusable body should be invalid_response.");
            SharedAssert.Equal("200", Tag(capture.Span("POST"), N.HttpStatusCode), "The HTTP exchange itself succeeded.");
            capture.Require(N.OperationDuration, N.Provider, "typesafe", N.Operation, "decide", N.Outcome, "error", N.ErrorType, "invalid_response");
        }

        #endregion

        #region Capabilities

        private static async Task RunCapabilitiesAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            string model = UniqueModel("embed");
            using OpenAiEmbeddingClient embed = new OpenAiEmbeddingClient(server.Endpoint, "test-key");
            embed.Model = model;
            using TeiSparseEmbeddingClient sparse = new TeiSparseEmbeddingClient(server.Endpoint, "test-key");
            using TeiRerankClient rerank = new TeiRerankClient(server.Endpoint, "test-key");
            using TeiClassificationClient classify = new TeiClassificationClient(server.Endpoint, "test-key");
            using TelemetryCapture capture = new TelemetryCapture("tel_capabilities");

            SharedAssert.True((await embed.EmbedAsync(new List<string> { "a", "b" }, token: token).ConfigureAwait(false)).Success, "Embedding should succeed.");
            SharedAssert.True((await sparse.EmbedSparseAsync(new List<string> { "a", "b", "c" }, token: token).ConfigureAwait(false)).Success, "Sparse embedding should succeed.");
            SharedAssert.True((await rerank.RerankAsync("q", new List<string> { "a", "b", "c", "d" }, token: token).ConfigureAwait(false)).Success, "Rerank should succeed.");
            SharedAssert.True((await classify.ClassifyAsync("hello", token: token).ConfigureAwait(false)).Success, "Classification should succeed.");

            Activity embedSpan = capture.Span("openai embed");
            SharedAssert.Equal("embeddings", Tag(embedSpan, N.GenAiOperationName), "Embedding GenAI operation name.");
            SharedAssert.Equal("embedding", Tag(embedSpan, N.Capability), "Embedding capability.");
            SharedAssert.Equal("2", Tag(embedSpan, N.BatchSizeAttribute), "Embedding batch size attribute.");
            SharedAssert.Equal("4", Tag(capture.Span("tei rerank"), N.BatchSizeAttribute), "Rerank batch size attribute.");
            SharedAssert.Equal("sparse_embedding", Tag(capture.Span("tei sparse_embed"), N.Capability), "Sparse capability.");
            SharedAssert.Equal("classification", Tag(capture.Span("tei classify"), N.Capability), "Classification capability.");

            capture.Require(N.GenAiOperationDuration, N.GenAiOperationName, "embeddings", N.Provider, "openai", N.RequestModel, model);
            SharedAssert.True(capture.Measurements(N.BatchSize, N.Provider, "openai", N.Operation, "embed").Any(m => m.Value == 2), "Embedding batch size metric.");
            SharedAssert.True(capture.Measurements(N.BatchSize, N.Provider, "tei", N.Operation, "sparse_embed").Any(m => m.Value == 3), "Sparse batch size metric.");
            SharedAssert.True(capture.Measurements(N.BatchSize, N.Provider, "tei", N.Operation, "rerank").Any(m => m.Value == 4), "Rerank batch size metric.");
            capture.Require(N.OperationDuration, N.Provider, "tei", N.Capability, "rerank", N.Operation, "rerank", N.Outcome, "success");
            capture.Require(N.OperationDuration, N.Provider, "tei", N.Capability, "classification", N.Operation, "classify", N.Outcome, "success");
        }

        private static async Task RunDecisionBatchAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using TypeSafeDecisionClient client = new TypeSafeDecisionClient(server.Endpoint, "ts-key");
            client.MaxConcurrency = 1;
            using TelemetryCapture capture = new TelemetryCapture("tel_decision_batch");

            List<DecisionRequest> requests = new List<DecisionRequest> { Decision("slow a"), Decision("slow b"), Decision("slow c"), Decision("slow err429") };
            List<DecisionResponse> responses = await client.DecideAsync(requests, null, token).ConfigureAwait(false);
            SharedAssert.Equal(3, responses.Count(r => r.Success), "Three items should succeed and one should fail.");

            Activity batch = capture.Span("typesafe decide_batch");
            SharedAssert.Equal("error", Tag(batch, N.Outcome), "A batch with a failed item should end as error.");
            SharedAssert.Equal("batch_item_failed", Tag(batch, N.ErrorType), "The batch error type should be batch_item_failed.");
            SharedAssert.Equal("4", Tag(batch, N.BatchSizeAttribute), "The batch span should record its size.");
            SharedAssert.Equal("1", Tag(batch, N.MaxConcurrency), "The batch span should record its concurrency limit.");

            List<Activity> items = capture.Spans("typesafe decide");
            SharedAssert.Equal(4, items.Count, "Each batch item should have a decide span.");
            SharedAssert.True(items.All(i => i.ParentSpanId == batch.SpanId), "Decide spans should nest under the batch span across the background hand-off.");
            SharedAssert.Equal(1, items.Count(i => Tag(i, N.ErrorType) == "429"), "The failed item should record error.type 429.");

            List<Activity> queued = capture.Spans("stage:queued");
            SharedAssert.Equal(4, queued.Count, "Each batch item should have a queued stage span.");
            SharedAssert.True(queued.All(q => q.ParentSpanId == batch.SpanId), "Queued spans should nest under the batch span.");

            List<CapturedMeasurement> waits = capture.Measurements(N.DecisionQueueDuration, N.Provider, "typesafe");
            SharedAssert.True(waits.Count >= 4, "Each item should record its queue wait.");
            SharedAssert.True(waits.Max(w => w.Value) >= 0.1, "With one slot and 150 ms calls, some item should wait at least 100 ms.");
            SharedAssert.True(capture.Measurements(N.DecisionQuestions, N.Provider, "typesafe", N.QuestionType, "binary").Sum(m => m.Value) >= 4, "Questions should be counted by type.");
            SharedAssert.True(capture.Measurements(N.BatchSize, N.Provider, "typesafe", N.Operation, "decide_batch").Any(m => m.Value == 4), "Batch size metric.");
            capture.Require(N.OperationDuration, N.Provider, "typesafe", N.Capability, "decision", N.Operation, "decide_batch", N.Outcome, "error", N.ErrorType, "batch_item_failed");
            capture.Require(N.OperationDuration, N.Provider, "typesafe", N.Operation, "decide", N.Outcome, "error", N.ErrorType, "429");
            capture.Require(N.GenAiTokenUsage, N.GenAiOperationName, "decide", N.Provider, "typesafe", N.GenAiTokenType, "input");
        }

        #endregion

        #region Credentials-and-Models

        private static async Task RunCredentialsAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            TelemetryTestCredential credential = new TelemetryTestCredential();
            using VertexAiCompletionClient client = new VertexAiCompletionClient("test-project", "us-central1", credential, endpoint: server.Endpoint);
            client.Model = "test-model";
            client.TimeoutMs = 3000;
            using TelemetryCapture capture = new TelemetryCapture("tel_credentials");

            SharedAssert.True((await client.ChatAsync("first", token: token).ConfigureAwait(false)).Success, "The first Vertex chat should succeed.");
            SharedAssert.True((await client.ChatAsync("second", token: token).ConfigureAwait(false)).Success, "The second Vertex chat should succeed.");
            SharedAssert.Equal(1, credential.FetchCount, "The token should be fetched once and then cached.");

            string source = "telemetrytestcredential";
            Activity refresh = capture.Span(source + " token");
            List<Activity> chats = capture.Spans("gcp.vertex_ai chat");
            SharedAssert.Equal(2, chats.Count, "Both chats should have spans.");
            SharedAssert.True(chats.Any(c => c.SpanId == refresh.ParentSpanId), "The token span should nest under the operation that needed it.");
            SharedAssert.Equal(ActivityStatusCode.Ok, refresh.Status, "A successful refresh should have status Ok.");

            capture.Require(N.CredentialCacheLookups, N.CredentialSource, source, N.CacheResult, "miss");
            capture.Require(N.CredentialCacheLookups, N.CredentialSource, source, N.CacheResult, "hit");
            capture.Require(N.CredentialRefreshDuration, N.CredentialSource, source, N.Outcome, "success");
            capture.Require(N.CredentialRefreshes, N.CredentialSource, source, N.Outcome, "success");

            TelemetryTestCredential failing = new TelemetryTestCredential { Fail = true };
            using VertexAiCompletionClient broken = new VertexAiCompletionClient("test-project", "us-central1", failing, endpoint: server.Endpoint);
            broken.Model = "test-model";
            ChatResponse failed = await broken.ChatAsync("denied", token: token).ConfigureAwait(false);
            SharedAssert.False(failed.Success, "A chat whose credential fails should fail.");

            string errorType = typeof(InvalidOperationException).FullName!;
            capture.Require(N.CredentialRefreshes, N.CredentialSource, source, N.Outcome, "error", N.ErrorType, errorType);
            SharedAssert.True(capture.Spans("gcp.vertex_ai chat").Any(c => Tag(c, N.ErrorType) == errorType), "The operation should record the credential failure type.");
            SharedAssert.True(capture.Spans(source + " token").Any(s => s.Status == ActivityStatusCode.Error), "The failed refresh span should have status Error.");
        }

        private static async Task RunModelOpsAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OllamaModelClient client = new OllamaModelClient(server.Endpoint, "test-key");
            using TelemetryCapture capture = new TelemetryCapture("tel_model_ops");

            SharedAssert.True(await client.ModelExistsAsync("test-model", token).ConfigureAwait(false), "The model should exist.");
            SharedAssert.NotNull(await client.GetModelInformationAsync("test-model", token).ConfigureAwait(false), "Model information should be returned.");
            SharedAssert.True(await client.PullModelAsync("test-model", null, token).ConfigureAwait(false), "The pull should succeed.");
            SharedAssert.True(await client.DeleteModelAsync("test-model", token).ConfigureAwait(false), "The delete should succeed.");
            SharedAssert.True(await client.ValidateConnectivityAsync(token).ConfigureAwait(false), "Connectivity should succeed.");

            Activity exists = capture.Span("ollama model_exists");
            SharedAssert.True(capture.Spans("GET").Any(h => h.ParentSpanId == exists.SpanId), "The model listing request should nest under model_exists.");
            SharedAssert.Equal("success", Tag(capture.Span("ollama model_info"), N.Outcome), "model_info outcome.");
            Activity pull = capture.Span("ollama model_pull");
            SharedAssert.Equal("success", Tag(pull, N.Outcome), "model_pull outcome.");
            SharedAssert.True(capture.Spans("POST").Any(h => h.ParentSpanId == pull.SpanId && Tag(h, N.HttpStatusCode) == "200"), "The pull request should nest under model_pull.");
            SharedAssert.True(capture.Spans("DELETE").Any(h => h.ParentSpanId == capture.Span("ollama model_delete").SpanId), "The delete request should nest under model_delete.");
            SharedAssert.Equal("model", Tag(capture.Span("ollama validate_connectivity"), N.Capability), "Connectivity capability.");

            using OllamaModelClient down = new OllamaModelClient("http://127.0.0.1:1", "test-key");
            down.TimeoutMs = 3000;
            SharedAssert.False(await down.ValidateConnectivityAsync(token).ConfigureAwait(false), "Connectivity to a closed port should fail.");
            SharedAssert.False(await down.ModelExistsAsync("test-model", token).ConfigureAwait(false), "Lookup against a closed port should fail.");

            string errorType = typeof(HttpRequestException).FullName!;
            capture.Require(N.OperationDuration, N.Provider, "ollama", N.Operation, "validate_connectivity", N.Outcome, "error", N.ErrorType, errorType);
            capture.Require(N.OperationDuration, N.Provider, "ollama", N.Operation, "model_exists", N.Outcome, "error", N.ErrorType, errorType);
            capture.Require(N.OperationDuration, N.Provider, "ollama", N.Operation, "model_pull", N.Outcome, "success");
            capture.Require(N.HttpRequests, N.Provider, "ollama", N.HttpMethod, "DELETE", N.HttpStatusCode, "200");
        }

        private static async Task RunProviderLabelsAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            List<ClientBase> clients = LocalClients.All(server.Endpoint);
            try
            {
                using TelemetryCapture capture = new TelemetryCapture("tel_provider_labels");
                foreach (ClientBase client in clients)
                {
                    await client.ValidateConnectivityAsync(token).ConfigureAwait(false);
                }

                HashSet<string> providers = capture.Spans()
                    .Where(s => Tag(s, N.Operation) == "validate_connectivity")
                    .Select(s => Tag(s, N.Provider)!)
                    .ToHashSet(StringComparer.Ordinal);

                string[] expected = new[] { "openai", "azure.ai.openai", "gcp.gemini", "gcp.vertex_ai", "anthropic", "aws.bedrock", "cohere", "ollama", "tei", "voyageai", "typesafe" };
                SharedAssert.Equal(string.Join(",", expected.OrderBy(p => p, StringComparer.Ordinal)), string.Join(",", providers.OrderBy(p => p, StringComparer.Ordinal)),
                    "Connectivity spans should carry every documented provider label.");

                foreach (Activity span in capture.Spans().Where(s => Tag(s, N.Operation) == "validate_connectivity"))
                {
                    SharedAssert.Equal(Tag(span, N.Provider) + " validate_connectivity", span.DisplayName, "Span names should be '{provider} {operation}'.");
                }
            }
            finally
            {
                foreach (ClientBase client in clients) client.Dispose();
            }
        }

        private static async Task RunGaugesAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using TelemetryCapture capture = new TelemetryCapture("tel_gauges");

            long before = LatestGauge(capture, N.ActiveClients, N.Provider, "tei", N.Capability, "rerank");
            List<TeiRerankClient> created = new List<TeiRerankClient>
            {
                new TeiRerankClient(server.Endpoint, "k"), new TeiRerankClient(server.Endpoint, "k"), new TeiRerankClient(server.Endpoint, "k")
            };
            long during = LatestGauge(capture, N.ActiveClients, N.Provider, "tei", N.Capability, "rerank");
            SharedAssert.True(during >= before + 3, "Creating three clients should raise the active client gauge by three (" + before + " -> " + during + ").");
            foreach (TeiRerankClient client in created) client.Dispose();
            foreach (TeiRerankClient client in created) client.Dispose();
            long after = LatestGauge(capture, N.ActiveClients, N.Provider, "tei", N.Capability, "rerank");
            SharedAssert.True(after <= during - 3, "Disposing the clients (twice) should lower the gauge by exactly three (" + during + " -> " + after + ").");

            using OpenAiCompletionClient hanging = NewOpenAi(server.Endpoint, UniqueModel("gauge"));
            hanging.TimeoutMs = 10000;
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            ChatStreamingResponse stream = await hanging.ChatStreamingAsync("hang stream", token: cts.Token).ConfigureAwait(false);
            IAsyncEnumerator<ChatStreamingChunk> enumerator = stream.Chunks.GetAsyncEnumerator(cts.Token);
            SharedAssert.True(await enumerator.MoveNextAsync().ConfigureAwait(false), "The hanging stream should deliver its first chunk.");

            long activeOps = LatestGauge(capture, N.ActiveOperations, N.Provider, "openai", N.Operation, "chat_stream");
            long activeRequests = LatestGauge(capture, N.HttpActiveRequests, N.Provider, "openai", N.HttpMethod, "POST");
            SharedAssert.True(activeOps >= 1, "A stream in progress should count as an active operation.");
            SharedAssert.True(activeRequests >= 1, "A stream in progress should count as an active HTTP request.");

            cts.Cancel();
            try
            {
                await enumerator.MoveNextAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            await enumerator.DisposeAsync().ConfigureAwait(false);

            SharedAssert.True(LatestGauge(capture, N.ActiveOperations, N.Provider, "openai", N.Operation, "chat_stream") < activeOps, "The active operation gauge should drop when the stream ends.");
            SharedAssert.True(LatestGauge(capture, N.HttpActiveRequests, N.Provider, "openai", N.HttpMethod, "POST") < activeRequests, "The active request gauge should drop when the stream ends.");
        }

        #endregion

        #region Logging

        private static async Task RunLoggingIsolationAsync(CancellationToken token)
        {
            SharedAssert.True(SyslogLoggingTelemetry.MeterName != N.MeterName, "SyslogLogging and PolyPrompt must use different meter names.");
            SharedAssert.True(SyslogLoggingTelemetry.ActivitySourceName != N.ActivitySourceName, "SyslogLogging and PolyPrompt must use different activity source names.");

            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            logging.Settings.EnableMetrics = true;
            logging.Settings.EnableTracing = true;

            List<string> loggingInstruments = new List<string>();
            List<Activity> loggingSpans = new List<Activity>();
            using MeterListener loggingMeters = new MeterListener();
            loggingMeters.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name != SyslogLoggingTelemetry.MeterName) return;
                lock (loggingInstruments) loggingInstruments.Add(instrument.Name);
                listener.EnableMeasurementEvents(instrument);
            };
            loggingMeters.Start();
            using ActivityListener loggingSources = new ActivityListener
            {
                ShouldListenTo = source => source.Name == SyslogLoggingTelemetry.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity => { lock (loggingSpans) loggingSpans.Add(activity); },
            };
            ActivitySource.AddActivityListener(loggingSources);

            using (TelemetryCapture capture = new TelemetryCapture("tel_logging_isolation"))
            {
                using (OpenAiCompletionClient client = new OpenAiCompletionClient(server.Endpoint, "test-key", logging))
                {
                    client.Model = UniqueModel("logging");
                    client.TimeoutMs = 3000;

                    await logging.InfoAsync("tel_logging_isolation entry").ConfigureAwait(false);
                    ChatResponse chat = await client.ChatAsync("ping", token: token).ConfigureAwait(false);
                    SharedAssert.True(chat.Success && chat.Text == "pong", "A chat with an injected, subscribed logging module should succeed.");
                }

                SharedAssert.Equal(1, capture.Spans("openai chat").Count, "PolyPrompt should emit exactly one chat span.");
                SharedAssert.True(capture.Spans().All(a => a.Source.Name == N.ActivitySourceName), "Only PolyPrompt spans should be captured on the PolyPrompt source.");
                SharedAssert.True(capture.Instruments.All(name => !loggingInstruments.Contains(name)), "No SyslogLogging instrument should be published on the PolyPrompt meter.");
            }

            lock (loggingInstruments) SharedAssert.True(loggingInstruments.Count > 0, "SyslogLogging should publish its own instruments on the SyslogLogging meter.");
            lock (loggingSpans)
            {
                SharedAssert.True(loggingSpans.Count > 0, "SyslogLogging should emit its own spans on the SyslogLogging activity source.");
                SharedAssert.True(loggingSpans.All(a => a.Source.Name == SyslogLoggingTelemetry.ActivitySourceName), "SyslogLogging spans should come only from its own source.");
            }

            await logging.DisposeAsync().ConfigureAwait(false);
        }

        #endregion

        #region Helpers

        private static OpenAiCompletionClient NewOpenAi(string endpoint, string model)
        {
            OpenAiCompletionClient client = new OpenAiCompletionClient(endpoint, "test-key");
            client.Model = model;
            client.TimeoutMs = 3000;
            return client;
        }

        private static DecisionRequest Decision(string state)
        {
            return new DecisionRequest
            {
                State = state,
                Questions = { DecisionQuestion.Binary("q1", "Is this a test?") }
            };
        }

        private static string UniqueModel(string prefix)
        {
            return "tel-" + prefix + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        private static string? Tag(Activity activity, string key)
        {
            object? value = activity.GetTagItem(key);
            if (value == null) return null;
            if (value is string[] array) return string.Join(",", array);
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static long LatestGauge(TelemetryCapture capture, string instrument, params string[] tags)
        {
            capture.Observe();
            List<CapturedMeasurement> values = capture.Measurements(instrument, tags);
            return values.Count == 0 ? 0 : (long)values[values.Count - 1].Value;
        }

        #endregion
    }
}
