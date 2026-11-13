namespace PolyPrompt.Clients
{
    using System.Text.Json;
    using PolyPrompt.Models;
    using PolyPrompt.Options;

    /// <summary>
    /// Base class for clients that speak the Responses API (<c>/v1/responses</c>): <see cref="OpenAiClient"/>,
    /// <see cref="AzureOpenAiClient"/>, and the xAI client. Chat, tool chat, and generation (streaming and not) go
    /// through one shared engine; provider differences (allowed values, hosted tool types, provider-only fields, and
    /// message wording) come from each client. It also hosts the native Responses operations shared by every Responses
    /// provider: <see cref="CreateResponseAsync"/>, <see cref="CreateResponseStreamingAsync"/>,
    /// <see cref="ContinueResponseAsync"/>, <see cref="GetResponseAsync"/>, <see cref="DeleteResponseAsync"/>,
    /// <see cref="ListResponseInputItemsAsync"/>, and <see cref="CompactConversationAsync"/>.
    /// </summary>
    /// <remarks>
    /// Requests always send <c>store</c> (false unless <see cref="ResponsesApiCompletionOptions.Store"/> is true) and
    /// always include <c>reasoning.encrypted_content</c>, so a conversation continues statelessly: each response's
    /// <see cref="ResponseBase.ProviderState"/> carries the output items (including encrypted reasoning) and is replayed in
    /// order by <c>ChatMessage.ToAssistantMessage</c>. State from another protocol, provider, or endpoint is ignored and the
    /// plain message history is sent instead.
    /// </remarks>
    public abstract partial class ResponsesApiClientBase : ProviderClientBase
    {
        #region Private-Members

        private protected const string ResponsesProtocol = "responses";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a Responses API client.
        /// </summary>
        /// <param name="endpoint">Endpoint URL.</param>
        /// <param name="apiKey">API key, or null.</param>
        /// <param name="options">Construction settings.</param>
        private protected ResponsesApiClientBase(string endpoint, string? apiKey, ClientOptions? options)
            : base(endpoint, apiKey, options)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create a response from raw Responses input: a JSON string, or an array of input items (messages with text,
        /// image, or file content; function call outputs; replayed output items).
        /// </summary>
        /// <param name="input">Complete input string or array. For previous-response continuation, supply only new input.</param>
        /// <param name="options">Optional settings: <see cref="ResponsesApiCompletionOptions"/> or this provider's subtype.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response; failures are reported on it.</returns>
        /// <exception cref="ArgumentException">Thrown when input is not a string or non-empty array, or options are invalid.</exception>
        /// <exception cref="InvalidOperationException">Thrown when no model is configured.</exception>
        public Task<ResponsesApiResponse> CreateResponseAsync(JsonElement input, ResponsesApiCompletionOptions? options = null, CancellationToken token = default)
        {
            return CreateCoreAsync(input, options, null, token);
        }

        /// <summary>
        /// Stream every native Responses event (text, reasoning, function-call arguments, hosted-tool progress, citations,
        /// and terminal events). Enumerate <see cref="ResponsesApiStreamingResponse.Events"/> completely to populate the final
        /// response.
        /// </summary>
        /// <param name="input">Complete input string or array.</param>
        /// <param name="options">Optional settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The streaming response.</returns>
        /// <exception cref="ArgumentException">Thrown when input is not a string or non-empty array, or options are invalid.</exception>
        /// <exception cref="InvalidOperationException">Thrown when no model is configured.</exception>
        public Task<ResponsesApiStreamingResponse> CreateResponseStreamingAsync(JsonElement input, ResponsesApiCompletionOptions? options = null, CancellationToken token = default)
        {
            ValidateInput(input);
            ResolvedCompletion settings = Resolve(options);
            Dictionary<string, object> body = BuildBody(input, settings, true, true);
            bool stored = IsStored(body);
            ResponsesApiStreamingResponse response = new() { Model = settings.Model, AppliedReasoningEffort = settings.AppliedReasoningEffort };
            return InstrumentStreamingAsync("create_response_stream", settings.Model, null,
                () => ExecuteStreamingAsync(response, "create response", Url("responses"), body,
                    (stream, sw) => response.Events = ReadNativeEvents(response, stream, sw, stored), token),
                (r, scope) => r.Events = scope.WrapStream(r.Events, r, _ => true), token);
        }

        /// <summary>
        /// Continue a stored response by id: sends <paramref name="input"/> with <c>previous_response_id</c> set to
        /// <paramref name="previous"/>'s id. The previous response must have been created with
        /// <see cref="ResponsesApiCompletionOptions.Store"/> = true; otherwise this throws before sending, because the
        /// provider has nothing to continue.
        /// </summary>
        /// <param name="previous">A response created with Store = true.</param>
        /// <param name="input">New input only (string or array).</param>
        /// <param name="options">Optional settings for the new response.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The new response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when previous is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when previous was not stored or has no id.</exception>
        public Task<ResponsesApiResponse> ContinueResponseAsync(ResponsesApiResponse previous, JsonElement input, ResponsesApiCompletionOptions? options = null, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(previous);
            if (previous.Stored != true)
                throw new InvalidOperationException("This response was created with Store = false and cannot be continued by id. Set Store = true on the call that creates it, or resend the history.");
            if (string.IsNullOrWhiteSpace(previous.ResponseId))
                throw new InvalidOperationException("The previous response has no id to continue.");
            return CreateCoreAsync(input, options, previous.ResponseId, token);
        }

        /// <summary>
        /// Retrieve a stored response (created with Store = true). Errors, including expired or unknown ids, are reported on
        /// the result. Use it to poll a background response.
        /// </summary>
        /// <param name="responseId">Response id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArgumentException">Thrown when responseId is null, empty, or whitespace.</exception>
        public Task<ResponsesApiResponse> GetResponseAsync(string responseId, CancellationToken token = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(responseId);
            return InstrumentAsync("get_response", null, null, () => ExecuteGetAsync(new ResponsesApiResponse { Stored = true }, "get response", Url("responses/" + Uri.EscapeDataString(responseId)), ParseResponse, token), token);
        }

        /// <summary>
        /// Delete a stored response. Inspect <c>Data.deleted</c> for the provider's result.
        /// </summary>
        /// <param name="responseId">Response id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The provider's JSON result.</returns>
        /// <exception cref="ArgumentException">Thrown when responseId is null, empty, or whitespace.</exception>
        public Task<ResponsesApiJsonResponse> DeleteResponseAsync(string responseId, CancellationToken token = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(responseId);
            return InstrumentAsync("delete_response", null, null, async () =>
            {
                ResponsesApiJsonResponse result = new();
                try
                {
                    HttpCallResult call = await DeleteAndRecordAsync(Url("responses/" + Uri.EscapeDataString(responseId)), null, token).ConfigureAwait(false);
                    result.StatusCode = call.StatusCode;
                    if (!call.IsSuccessStatusCode) result.Error = "HTTP " + call.StatusCode + ": " + call.ResponseBody;
                    else { ParseJsonResponse(call.ResponseBody!, result); result.Success = result.Error == null; }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { result.Error = ex.Message; }
                return result;
            }, token);
        }

        /// <summary>
        /// List a page of a stored response's input items, keeping <c>data</c>, <c>has_more</c>, <c>first_id</c>,
        /// <c>last_id</c>, and every item field.
        /// </summary>
        /// <param name="responseId">Response id.</param>
        /// <param name="limit">Page size, 1..100. Default: 20.</param>
        /// <param name="order"><c>asc</c> or <c>desc</c>. Default: asc.</param>
        /// <param name="after">Item id to start after, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The provider's JSON page.</returns>
        /// <exception cref="ArgumentException">Thrown when responseId is blank or order is invalid.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when limit is outside 1..100.</exception>
        public Task<ResponsesApiJsonResponse> ListResponseInputItemsAsync(string responseId, int limit = 20, string order = "asc", string? after = null, CancellationToken token = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(responseId);
            if (limit < 1 || limit > 100) throw new ArgumentOutOfRangeException(nameof(limit));
            if (order != "asc" && order != "desc") throw new ArgumentException("Order must be asc or desc.", nameof(order));
            string path = "responses/" + Uri.EscapeDataString(responseId) + "/input_items?limit=" + limit + "&order=" + order;
            if (!string.IsNullOrEmpty(after)) path += "&after=" + Uri.EscapeDataString(after);
            return InstrumentAsync("list_response_input", null, null, () => ExecuteGetAsync(new ResponsesApiJsonResponse(), "list response input", Url(path), ParseJsonResponse, token), token);
        }

        /// <summary>
        /// Compact a conversation window (<c>POST /responses/compact</c>). Reuse <c>Data.output</c> verbatim as the next
        /// response's input. For server-side compaction on every call, use <c>OpenAiCompletionOptions.ContextManagement</c>
        /// instead.
        /// </summary>
        /// <param name="input">Complete input string or array to compact.</param>
        /// <param name="model">Model, or null for <c>Defaults.Completion.Model</c>.</param>
        /// <param name="previousResponseId">Optional stored response to compact from.</param>
        /// <param name="instructions">Optional instructions.</param>
        /// <param name="promptCacheKey">Optional prompt cache key.</param>
        /// <param name="serviceTier">Optional service tier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The provider's JSON result.</returns>
        /// <exception cref="ArgumentException">Thrown when input is not a string or non-empty array.</exception>
        /// <exception cref="InvalidOperationException">Thrown when no model is configured.</exception>
        public Task<ResponsesApiJsonResponse> CompactConversationAsync(
            JsonElement input,
            string? model = null,
            string? previousResponseId = null,
            string? instructions = null,
            string? promptCacheKey = null,
            ServiceTier? serviceTier = null,
            CancellationToken token = default)
        {
            ValidateInput(input);
            string resolvedModel = model ?? Defaults.Completion?.Model ?? throw new InvalidOperationException(NoModelMessage("Completion"));
            ArgumentException.ThrowIfNullOrWhiteSpace(resolvedModel);
            Dictionary<string, object> body = new Dictionary<string, object> { ["model"] = resolvedModel, ["input"] = input };
            if (!string.IsNullOrWhiteSpace(previousResponseId)) body["previous_response_id"] = previousResponseId;
            if (!string.IsNullOrEmpty(instructions)) body["instructions"] = instructions;
            if (!string.IsNullOrEmpty(promptCacheKey)) body["prompt_cache_key"] = promptCacheKey;
            if (serviceTier.HasValue)
            {
                ResponsesApiDialect.Check(serviceTier.Value.Value, ResponsesApiDialect.KnownServiceTiers, Dialect.ServiceTiers, Dialect.ServiceTierError);
                body["service_tier"] = serviceTier.Value.Value;
            }
            return InstrumentAsync("compact_response", resolvedModel, null, () => ExecutePostAsync(new ResponsesApiJsonResponse { Model = resolvedModel }, "compact response", Url("responses/compact"),
                body, ParseJsonResponse, token), token);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Provider data for the shared engine.
        /// </summary>
        private protected abstract ResponsesApiDialect Dialect { get; }

        /// <summary>
        /// Build the URL for a Responses-family path such as <c>responses</c> or <c>models</c>.
        /// </summary>
        /// <param name="path">Path without a leading slash.</param>
        /// <returns>The absolute URL.</returns>
        protected abstract string Url(string path);

        /// <inheritdoc />
        protected override Task<ChatResponse> ChatCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token) =>
            ExecutePostAsync(new ChatResponse { Model = settings.Model }, "chat", Url("responses"), BuildBody(prompt, settings, false, true), ParseResponse, token);

        /// <inheritdoc />
        protected override Task<ToolChatResponse> ToolChatCoreAsync(ToolChatRequest request, List<ChatMessage> messages, ResolvedCompletion settings, CancellationToken token)
        {
            Dictionary<string, object> body = BuildBody(BuildInput(messages), settings, false, true, request);
            return ExecutePostAsync<ToolChatResponse>(new ResponsesApiResponse { Model = settings.Model, Stored = IsStored(body) }, "tool chat", Url("responses"), body, ParseResponse, token);
        }

        /// <inheritdoc />
        protected override Task<GenerationResponse> GenerateCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token) =>
            ExecutePostAsync(new GenerationResponse { Model = settings.Model }, "generate", Url("responses"), BuildBody(prompt, settings, false, false), ParseResponse, token);

        /// <inheritdoc />
        protected override Task<ChatStreamingResponse> ChatStreamingCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token)
        {
            ChatStreamingResponse response = new() { Model = settings.Model };
            return ExecuteStreamingAsync(response, "chat", Url("responses"), BuildBody(prompt, settings, true, true),
                (stream, sw) => response.Chunks = WrapChunksWithTiming(response, ReadChatChunks(response, stream.Response, stream.Token), sw, stream.Token, stream), token);
        }

        /// <inheritdoc />
        protected override Task<ToolChatStreamingResponse> ToolChatStreamingCoreAsync(ToolChatRequest request, List<ChatMessage> messages, ResolvedCompletion settings, CancellationToken token)
        {
            ToolChatStreamingResponse response = new() { Model = settings.Model };
            return ExecuteStreamingAsync(response, "tool chat", Url("responses"), BuildBody(BuildInput(messages), settings, true, true, request),
                (stream, sw) => response.Chunks = WrapToolChatChunksWithTiming(response, ReadToolChunks(response, stream.Response, stream.Token), sw, stream.Token, stream), token);
        }

        /// <inheritdoc />
        protected override Task<GenerationStreamingResponse> GenerateStreamingCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token)
        {
            GenerationStreamingResponse response = new() { Model = settings.Model };
            return ExecuteStreamingAsync(response, "generate", Url("responses"), BuildBody(prompt, settings, true, false),
                (stream, sw) => response.Chunks = WrapGenerationChunksWithTiming(response, ReadGenerationChunks(response, stream.Response, stream.Token), sw, stream.Token, stream), token);
        }

        /// <summary>
        /// Validate raw Responses input: a JSON string or a non-empty array.
        /// </summary>
        /// <param name="input">Input.</param>
        /// <exception cref="ArgumentException">Thrown when input is invalid.</exception>
        protected static void ValidateInput(JsonElement input)
        {
            if (input.ValueKind != JsonValueKind.String && input.ValueKind != JsonValueKind.Array)
                throw new ArgumentException("Responses input must be a JSON string or array.", nameof(input));
            if (input.ValueKind == JsonValueKind.Array && input.GetArrayLength() == 0) throw new ArgumentException("Input array must not be empty.", nameof(input));
        }

        /// <summary>
        /// POST a body to a Responses-family path and parse a <see cref="ResponsesApiResponse"/>. For provider-only native
        /// operations such as cancellation.
        /// </summary>
        private protected Task<ResponsesApiResponse> PostResponseAsync(string operation, string path, object body, bool? stored, CancellationToken token)
        {
            return InstrumentAsync(operation, null, null, () => ExecutePostAsync(new ResponsesApiResponse { Stored = stored }, operation.Replace('_', ' '), Url(path), body, ParseResponse, token), token);
        }

        /// <summary>
        /// POST a body to a Responses-family path and keep the JSON result.
        /// </summary>
        private protected Task<ResponsesApiJsonResponse> PostJsonResultAsync(string operation, string path, object body, string? model, CancellationToken token)
        {
            return InstrumentAsync(operation, model, null, () => ExecutePostAsync(new ResponsesApiJsonResponse { Model = model }, operation.Replace('_', ' '), Url(path), body, ParseJsonResponse, token), token);
        }

        /// <summary>
        /// Build a native request body from raw input and settings, for provider-only native operations.
        /// </summary>
        private protected Dictionary<string, object> BuildNativeBody(JsonElement input, ResolvedCompletion settings)
        {
            return BuildBody(input, settings, false, true);
        }

        /// <inheritdoc />
        private protected override string? HttpErrorHint(int statusCode, string url, string? body)
        {
            if ((statusCode == 404 || statusCode == 405) && url.Contains("/responses", StringComparison.Ordinal) && Dialect.NotFoundHint != null)
                return Dialect.NotFoundHint;
            return null;
        }

        #endregion

        #region Private-Methods

        private Task<ResponsesApiResponse> CreateCoreAsync(JsonElement input, ResponsesApiCompletionOptions? options, string? previous, CancellationToken token)
        {
            ValidateInput(input);
            ResolvedCompletion settings = Resolve(options);
            Dictionary<string, object> body = BuildBody(input, settings, false, true, null, previous);
            bool stored = IsStored(body);
            return InstrumentAsync("create_response", settings.Model, null, async () =>
            {
                ResponsesApiResponse response = await ExecutePostAsync(new ResponsesApiResponse { Model = settings.Model, Stored = stored }, "create response", Url("responses"), body, ParseResponse, token).ConfigureAwait(false);
                response.AppliedReasoningEffort = settings.AppliedReasoningEffort;
                return response;
            }, token);
        }

        private static bool IsStored(Dictionary<string, object> body)
        {
            return body.TryGetValue("store", out object? store) && store is bool b && b;
        }

        #endregion
    }
}
