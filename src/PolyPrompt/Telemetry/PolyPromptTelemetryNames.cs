namespace PolyPrompt.Telemetry
{
    /// <summary>
    /// Every stable telemetry name PolyPrompt emits: the meter and activity source names, the metric instrument names,
    /// the attribute (label) keys, and the bounded attribute values. These names are a public contract consumed by
    /// collectors, dashboards, and alerts; they change only in a major version. See TELEMETRY.md for the full catalog.
    /// Thread safety: constants only, safe from any thread.
    /// </summary>
    public static class PolyPromptTelemetryNames
    {
        #region Sources

        /// <summary>
        /// Name of the <see cref="System.Diagnostics.Metrics.Meter"/> that carries every PolyPrompt metric.
        /// Subscribe a collector to this name (for example Radiant's <c>settings.Sources.AddMeter("PolyPrompt")</c>).
        /// </summary>
        public const string MeterName = "PolyPrompt";

        /// <summary>
        /// Name of the <see cref="System.Diagnostics.ActivitySource"/> that carries every PolyPrompt span.
        /// Subscribe a collector to this name (for example Radiant's <c>settings.Sources.AddActivitySource("PolyPrompt")</c>).
        /// </summary>
        public const string ActivitySourceName = "PolyPrompt";

        #endregion

        #region Metrics

        /// <summary>
        /// Histogram (seconds): duration of each public client operation, from the call until the response is returned
        /// (or, for streaming operations, until the stream ends). Labels: provider, capability, operation, outcome, error.type.
        /// </summary>
        public const string OperationDuration = "polyprompt.client.operation.duration";

        /// <summary>
        /// Counter ({operation}): completed public client operations. Labels: provider, capability, operation, outcome, error.type.
        /// </summary>
        public const string Operations = "polyprompt.client.operations";

        /// <summary>
        /// Observable up-down counter ({operation}): client operations currently in progress. Labels: provider, operation.
        /// </summary>
        public const string ActiveOperations = "polyprompt.client.operation.active";

        /// <summary>
        /// Histogram (seconds): OpenTelemetry GenAI semantic-convention operation duration for model calls (chat,
        /// text completion, embeddings, rerank, classify, decide). Labels: gen_ai.operation.name, gen_ai.provider.name,
        /// gen_ai.request.model, error.type.
        /// </summary>
        public const string GenAiOperationDuration = "gen_ai.client.operation.duration";

        /// <summary>
        /// Histogram ({token}): OpenTelemetry GenAI semantic-convention token usage per call. Labels: gen_ai.operation.name,
        /// gen_ai.provider.name, gen_ai.request.model, gen_ai.token.type (input or output).
        /// </summary>
        public const string GenAiTokenUsage = "gen_ai.client.token.usage";

        /// <summary>
        /// Counter ({token}): tokens consumed, including cache and reasoning breakdowns. Labels: provider, operation,
        /// gen_ai.request.model, polyprompt.token.type.
        /// </summary>
        public const string Tokens = "polyprompt.client.tokens";

        /// <summary>
        /// Histogram (seconds): time from the start of a streaming operation to its first content chunk.
        /// Labels: provider, operation.
        /// </summary>
        public const string StreamTimeToFirstChunk = "polyprompt.client.stream.time_to_first_chunk";

        /// <summary>
        /// Counter ({chunk}): content chunks delivered by streaming operations. Labels: provider, operation.
        /// </summary>
        public const string StreamChunks = "polyprompt.client.stream.chunks";

        /// <summary>
        /// Histogram ({item}): number of inputs per request (embedding inputs, rerank documents, classification inputs,
        /// decision requests in a batch, decision questions). Labels: provider, operation.
        /// </summary>
        public const string BatchSize = "polyprompt.client.batch.size";

        /// <summary>
        /// Counter ({call}): tool calls requested by the model. Labels: provider, operation.
        /// </summary>
        public const string ToolCalls = "polyprompt.client.tool_calls";

        /// <summary>
        /// Counter ({response}): completion responses by finish reason, which exposes truncation (length / max_tokens)
        /// and content filtering. Labels: provider, operation, gen_ai.response.finish_reason.
        /// </summary>
        public const string FinishReasons = "polyprompt.client.finish_reasons";

        /// <summary>
        /// Histogram (seconds): duration of each outbound HTTP request to a provider. For streaming requests this covers
        /// the whole response body. Labels: provider, http.request.method, http.response.status_code, error.type,
        /// server.address, server.port.
        /// </summary>
        public const string HttpRequestDuration = "polyprompt.http.client.request.duration";

        /// <summary>
        /// Counter ({request}): outbound HTTP requests to providers. Labels: provider, http.request.method,
        /// http.response.status_code, error.type, server.address, server.port.
        /// </summary>
        public const string HttpRequests = "polyprompt.http.client.requests";

        /// <summary>
        /// Observable up-down counter ({request}): outbound HTTP requests currently in flight. Labels: provider,
        /// http.request.method.
        /// </summary>
        public const string HttpActiveRequests = "polyprompt.http.client.active_requests";

        /// <summary>
        /// Histogram (bytes): outbound HTTP request body size. Labels: provider, http.request.method.
        /// </summary>
        public const string HttpRequestBodySize = "polyprompt.http.client.request.body.size";

        /// <summary>
        /// Histogram (bytes): HTTP response body size for non-streaming requests. Labels: provider, http.request.method.
        /// </summary>
        public const string HttpResponseBodySize = "polyprompt.http.client.response.body.size";

        /// <summary>
        /// Histogram (seconds): time a decision batch item waited for a concurrency slot (the queued stage).
        /// Labels: provider.
        /// </summary>
        public const string DecisionQueueDuration = "polyprompt.decision.batch.queue.duration";

        /// <summary>
        /// Observable up-down counter ({request}): decision batch items holding a concurrency slot. Labels: provider.
        /// </summary>
        public const string DecisionInFlight = "polyprompt.decision.batch.in_flight";

        /// <summary>
        /// Observable up-down counter ({request}): decision batch items waiting for a concurrency slot. Labels: provider.
        /// </summary>
        public const string DecisionQueued = "polyprompt.decision.batch.queued";

        /// <summary>
        /// Counter ({question}): decision questions sent, by question type. Labels: provider, polyprompt.question.type.
        /// </summary>
        public const string DecisionQuestions = "polyprompt.decision.questions";

        /// <summary>
        /// Histogram (seconds): duration of a credential token fetch (OAuth exchange or metadata server).
        /// Labels: polyprompt.credential.source, outcome, error.type.
        /// </summary>
        public const string CredentialRefreshDuration = "polyprompt.credential.refresh.duration";

        /// <summary>
        /// Counter ({refresh}): credential token fetches. Labels: polyprompt.credential.source, outcome, error.type.
        /// </summary>
        public const string CredentialRefreshes = "polyprompt.credential.refreshes";

        /// <summary>
        /// Counter ({lookup}): credential cache lookups. Labels: polyprompt.credential.source, polyprompt.cache.result (hit or miss).
        /// </summary>
        public const string CredentialCacheLookups = "polyprompt.credential.cache.lookups";

        /// <summary>
        /// Observable up-down counter ({client}): constructed and not yet disposed clients. Labels: provider, capability.
        /// </summary>
        public const string ActiveClients = "polyprompt.clients.active";

        /// <summary>
        /// Observable gauge ({build}): always 1, carrying the library version in polyprompt.version.
        /// </summary>
        public const string BuildInfo = "polyprompt.build.info";

        #endregion

        #region Attributes

        /// <summary>
        /// Provider label (OpenTelemetry GenAI key), for example <c>openai</c>, <c>aws.bedrock</c>, or <c>gcp.vertex_ai</c>.
        /// </summary>
        public const string Provider = "gen_ai.provider.name";

        /// <summary>
        /// Capability label: completion, embedding, sparse_embedding, rerank, classification, decision, or model.
        /// </summary>
        public const string Capability = "polyprompt.capability";

        /// <summary>
        /// PolyPrompt operation label, for example <c>chat</c>, <c>chat_stream</c>, <c>embed</c>, or <c>decide_batch</c>.
        /// </summary>
        public const string Operation = "polyprompt.operation";

        /// <summary>
        /// Outcome label: success, error, timeout, cancelled, or abandoned.
        /// </summary>
        public const string Outcome = "polyprompt.outcome";

        /// <summary>
        /// Error type (OpenTelemetry key): an HTTP status code, <c>invalid_response</c>, <c>timeout</c>, or an exception type name.
        /// </summary>
        public const string ErrorType = "error.type";

        /// <summary>
        /// GenAI operation name (OpenTelemetry key): chat, text_completion, embeddings, or a PolyPrompt operation.
        /// </summary>
        public const string GenAiOperationName = "gen_ai.operation.name";

        /// <summary>
        /// Requested model (OpenTelemetry key). Model names are configured by the application, so the set is bounded.
        /// </summary>
        public const string RequestModel = "gen_ai.request.model";

        /// <summary>
        /// Model reported by the provider (OpenTelemetry key). Span attribute only.
        /// </summary>
        public const string ResponseModel = "gen_ai.response.model";

        /// <summary>
        /// Provider response id (OpenTelemetry key). Span attribute only.
        /// </summary>
        public const string ResponseId = "gen_ai.response.id";

        /// <summary>
        /// Finish reason (OpenTelemetry key).
        /// </summary>
        public const string FinishReason = "gen_ai.response.finish_reasons";

        /// <summary>
        /// Finish reason label on <see cref="FinishReasons"/>.
        /// </summary>
        public const string FinishReasonLabel = "gen_ai.response.finish_reason";

        /// <summary>
        /// Requested max tokens (OpenTelemetry key). Span attribute only.
        /// </summary>
        public const string RequestMaxTokens = "gen_ai.request.max_tokens";

        /// <summary>
        /// Requested temperature (OpenTelemetry key). Span attribute only.
        /// </summary>
        public const string RequestTemperature = "gen_ai.request.temperature";

        /// <summary>
        /// Requested top-p (OpenTelemetry key). Span attribute only.
        /// </summary>
        public const string RequestTopP = "gen_ai.request.top_p";

        /// <summary>
        /// Input tokens (OpenTelemetry key). Span attribute only.
        /// </summary>
        public const string UsageInputTokens = "gen_ai.usage.input_tokens";

        /// <summary>
        /// Output tokens (OpenTelemetry key). Span attribute only.
        /// </summary>
        public const string UsageOutputTokens = "gen_ai.usage.output_tokens";

        /// <summary>
        /// Cached input tokens. Span attribute only.
        /// </summary>
        public const string UsageCachedInputTokens = "polyprompt.usage.cached_input_tokens";

        /// <summary>
        /// Cache creation tokens. Span attribute only.
        /// </summary>
        public const string UsageCacheCreationTokens = "polyprompt.usage.cache_creation_tokens";

        /// <summary>
        /// Reasoning tokens. Span attribute only.
        /// </summary>
        public const string UsageReasoningTokens = "polyprompt.usage.reasoning_tokens";

        /// <summary>
        /// Token type label on <see cref="GenAiTokenUsage"/> (OpenTelemetry key): input or output.
        /// </summary>
        public const string GenAiTokenType = "gen_ai.token.type";

        /// <summary>
        /// Token type label on <see cref="Tokens"/>: input, output, cached_input, cache_creation, or reasoning.
        /// </summary>
        public const string TokenType = "polyprompt.token.type";

        /// <summary>
        /// Whether the operation streams its response. Span attribute only.
        /// </summary>
        public const string Streaming = "polyprompt.streaming";

        /// <summary>
        /// Input count for the operation. Span attribute only.
        /// </summary>
        public const string BatchSizeAttribute = "polyprompt.batch.size";

        /// <summary>
        /// Number of tool definitions offered to the model. Span attribute only.
        /// </summary>
        public const string ToolDefinitionCount = "polyprompt.tool.definitions";

        /// <summary>
        /// Number of tool calls returned by the model. Span attribute only.
        /// </summary>
        public const string ToolCallCount = "polyprompt.tool.calls";

        /// <summary>
        /// Number of content chunks delivered by a stream. Span attribute only.
        /// </summary>
        public const string StreamChunkCount = "polyprompt.stream.chunks";

        /// <summary>
        /// Milliseconds to the first content chunk of a stream. Span attribute only.
        /// </summary>
        public const string StreamTimeToFirstChunkMs = "polyprompt.stream.time_to_first_chunk_ms";

        /// <summary>
        /// Concurrency limit of a decision batch. Span attribute only.
        /// </summary>
        public const string MaxConcurrency = "polyprompt.decision.max_concurrency";

        /// <summary>
        /// Decision question type label: binary, choice, or score.
        /// </summary>
        public const string QuestionType = "polyprompt.question.type";

        /// <summary>
        /// Credential source label: service_account, adc, or a lower-cased custom provider type name.
        /// </summary>
        public const string CredentialSource = "polyprompt.credential.source";

        /// <summary>
        /// Cache lookup result label: hit or miss.
        /// </summary>
        public const string CacheResult = "polyprompt.cache.result";

        /// <summary>
        /// Library version label on <see cref="BuildInfo"/>.
        /// </summary>
        public const string Version = "polyprompt.version";

        /// <summary>
        /// HTTP method (OpenTelemetry key).
        /// </summary>
        public const string HttpMethod = "http.request.method";

        /// <summary>
        /// HTTP response status code (OpenTelemetry key).
        /// </summary>
        public const string HttpStatusCode = "http.response.status_code";

        /// <summary>
        /// Provider host (OpenTelemetry key). Endpoints are configured by the application, so the set is bounded.
        /// </summary>
        public const string ServerAddress = "server.address";

        /// <summary>
        /// Provider port (OpenTelemetry key).
        /// </summary>
        public const string ServerPort = "server.port";

        /// <summary>
        /// Full request URL with the query string removed (OpenTelemetry key). Span attribute only.
        /// </summary>
        public const string UrlFull = "url.full";

        #endregion

        #region Values

        /// <summary>
        /// Outcome value: the operation succeeded.
        /// </summary>
        public const string OutcomeSuccess = "success";

        /// <summary>
        /// Outcome value: the operation failed (HTTP error, transport failure, or invalid response).
        /// </summary>
        public const string OutcomeError = "error";

        /// <summary>
        /// Outcome value: the operation exceeded the client's TimeoutMs.
        /// </summary>
        public const string OutcomeTimeout = "timeout";

        /// <summary>
        /// Outcome value: the caller cancelled the operation.
        /// </summary>
        public const string OutcomeCancelled = "cancelled";

        /// <summary>
        /// Outcome value: the caller stopped enumerating a stream before it ended.
        /// </summary>
        public const string OutcomeAbandoned = "abandoned";

        /// <summary>
        /// Error type value: the provider returned a successful status with a body PolyPrompt could not use.
        /// </summary>
        public const string ErrorInvalidResponse = "invalid_response";

        /// <summary>
        /// Error type value: the request exceeded TimeoutMs.
        /// </summary>
        public const string ErrorTimeout = "timeout";

        /// <summary>
        /// Error type value: at least one item in a decision batch failed.
        /// </summary>
        public const string ErrorBatchItemFailed = "batch_item_failed";

        /// <summary>
        /// Error type value: an operation that returns a boolean reported failure without an HTTP status.
        /// </summary>
        public const string ErrorOperationFailed = "operation_failed";

        #endregion

        #region Operations

        /// <summary>
        /// Operation value: <c>ChatAsync</c>.
        /// </summary>
        public const string OperationChat = "chat";

        /// <summary>
        /// Operation value: <c>ChatStreamingAsync</c>.
        /// </summary>
        public const string OperationChatStream = "chat_stream";

        /// <summary>
        /// Operation value: <c>ToolChatAsync</c>.
        /// </summary>
        public const string OperationToolChat = "tool_chat";

        /// <summary>
        /// Operation value: <c>ToolChatStreamingAsync</c>.
        /// </summary>
        public const string OperationToolChatStream = "tool_chat_stream";

        /// <summary>
        /// Operation value: <c>GenerateAsync</c>.
        /// </summary>
        public const string OperationGenerate = "generate";

        /// <summary>
        /// Operation value: <c>GenerateStreamingAsync</c>.
        /// </summary>
        public const string OperationGenerateStream = "generate_stream";

        /// <summary>
        /// Operation value: <c>EmbedAsync</c>.
        /// </summary>
        public const string OperationEmbed = "embed";

        /// <summary>
        /// Operation value: <c>EmbedSparseAsync</c>.
        /// </summary>
        public const string OperationSparseEmbed = "sparse_embed";

        /// <summary>
        /// Operation value: <c>RerankAsync</c>.
        /// </summary>
        public const string OperationRerank = "rerank";

        /// <summary>
        /// Operation value: <c>ClassifyAsync</c>.
        /// </summary>
        public const string OperationClassify = "classify";

        /// <summary>
        /// Operation value: one decision request (<c>DecideAsync</c>, or one item of a batch).
        /// </summary>
        public const string OperationDecide = "decide";

        /// <summary>
        /// Operation value: a decision batch (<c>DecideAsync</c> with a list).
        /// </summary>
        public const string OperationDecideBatch = "decide_batch";

        /// <summary>
        /// Operation value: <c>GetModelInformationAsync</c>.
        /// </summary>
        public const string OperationModelInfo = "model_info";

        /// <summary>
        /// Operation value: <c>ModelExistsAsync</c>.
        /// </summary>
        public const string OperationModelExists = "model_exists";

        /// <summary>
        /// Operation value: Ollama <c>PullModelAsync</c>.
        /// </summary>
        public const string OperationModelPull = "model_pull";

        /// <summary>
        /// Operation value: Ollama <c>DeleteModelAsync</c>.
        /// </summary>
        public const string OperationModelDelete = "model_delete";

        /// <summary>
        /// Operation value: <c>ValidateConnectivityAsync</c>.
        /// </summary>
        public const string OperationValidateConnectivity = "validate_connectivity";

        #endregion
    }
}
