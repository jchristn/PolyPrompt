namespace PolyPrompt.Clients
{
    using System.Runtime.CompilerServices;
    using System.Text.Json;
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using SyslogLogging;

    /// <summary>xAI Grok completion client using the native Responses API for chat, tools, reasoning, and generation.</summary>
    public partial class XAiCompletionClient : CompletionClientBase
    {
        /// <summary>xAI defaults. Model: grok-4.7. Storage is disabled; encrypted continuation output is requested.</summary>
        public override XAiCompletionOptions Defaults { get; } = new XAiCompletionOptions { Model = "grok-4.7", Store = false };

        /// <summary>Initialize an xAI Responses client with required bearer authentication.</summary>
        /// <param name="apiKey">Required xAI API key.</param>
        /// <param name="endpoint">API root or versioned /v1 URL. Default: https://api.x.ai.</param>
        /// <param name="logging">Optional logging module.</param>
        /// <param name="httpClient">Optional HTTP client; see <see cref="ClientBase"/>.</param>
        public XAiCompletionClient(string apiKey, string endpoint = XAiProtocol.Endpoint, LoggingModule? logging = null, HttpClient? httpClient = null)
            : base(endpoint, XAiProtocol.RequireKey(apiKey), logging ?? new LoggingModule(), httpClient) { _Header = "[XAi] "; }

        /// <inheritdoc />
        public override Task<bool> ValidateConnectivityAsync(CancellationToken token = default) => ProbeAsync(Url("models"), token);
        /// <inheritdoc />
        protected override Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
        { XAiProtocol.Auth(request, _ApiKey); return Task.CompletedTask; }
        private string Url(string path) => XAiProtocol.Url(_Endpoint, path);

        /// <inheritdoc />
        protected override Task<ChatResponse> ChatCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token) =>
            ExecutePostAsync(new ChatResponse { Model = settings.Model }, "chat", Url("responses"), BuildBody(prompt, settings, false, true), ParseResponse, token);
        /// <inheritdoc />
        protected override Task<ToolChatResponse> ToolChatCoreAsync(ToolChatRequest request, List<ChatMessage> messages, ResolvedCompletion settings, CancellationToken token) =>
            ExecutePostAsync<ToolChatResponse>(new XAiResponse { Model = settings.Model }, "tool chat", Url("responses"), BuildBody(BuildInput(messages), settings, false, true, request), ParseResponse, token);
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

        /// <summary>Create a response from a raw Responses input string or array (including image/file input and tool outputs).</summary>
        /// <param name="input">Complete input string or array. For previous_response_id continuation, supply only new input.</param>
        /// <param name="options">Optional xAI settings.</param>
        /// <param name="token">Cancellation token.</param>
        public Task<XAiResponse> CreateResponseAsync(JsonElement input, XAiCompletionOptions? options = null, CancellationToken token = default)
        {
            ValidateInput(input);
            ResolvedCompletion settings = Resolve(options);
            return InstrumentAsync("create_response", settings.Model, null, () => ExecutePostAsync(new XAiResponse { Model = settings.Model },
                "create response", Url("responses"), BuildBody(input, settings, false, true), ParseResponse, token), token);
        }

        /// <summary>Stream every native Responses event, including hosted tools, citations, reasoning, and function-call events.</summary>
        /// <param name="input">Complete input string or array.</param>
        /// <param name="options">Optional xAI settings.</param>
        /// <param name="token">Cancellation token.</param>
        public Task<XAiStreamingResponse> CreateResponseStreamingAsync(JsonElement input, XAiCompletionOptions? options = null, CancellationToken token = default)
        {
            ValidateInput(input);
            ResolvedCompletion settings = Resolve(options);
            XAiStreamingResponse response = new() { Model = settings.Model };
            return InstrumentStreamingAsync("create_response_stream", settings.Model, null,
                () => ExecuteStreamingAsync(response, "create response", Url("responses"), BuildBody(input, settings, true, true),
                    (stream, sw) => response.Events = ReadNativeEvents(response, stream, sw), token),
                (r, scope) => r.Events = scope.WrapStream(r.Events, r, _ => true), token);
        }

        /// <summary>Retrieve a response saved with Store=true. Errors, including expired/unknown IDs, are reported on the result.</summary>
        public Task<XAiResponse> GetResponseAsync(string responseId, CancellationToken token = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(responseId);
            return InstrumentAsync("get_response", null, null, () => ExecuteGetAsync(new XAiResponse(), "get response", Url("responses/" + Uri.EscapeDataString(responseId)), ParseResponse, token), token);
        }

        /// <summary>Delete a stored response. Inspect Data.deleted for the provider's deletion result.</summary>
        public Task<XAiJsonResponse> DeleteResponseAsync(string responseId, CancellationToken token = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(responseId);
            return InstrumentAsync("delete_response", null, null, async () =>
            {
                XAiJsonResponse result = new();
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

        /// <summary>List a page of stored input items, preserving data/has_more/first_id/last_id and all item fields.</summary>
        public Task<XAiJsonResponse> ListResponseInputItemsAsync(string responseId, int limit = 20, string order = "asc", string? after = null, CancellationToken token = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(responseId);
            if (limit < 1 || limit > 100) throw new ArgumentOutOfRangeException(nameof(limit));
            if (order != "asc" && order != "desc") throw new ArgumentException("Order must be asc or desc.", nameof(order));
            string path = "responses/" + Uri.EscapeDataString(responseId) + "/input_items?limit=" + limit + "&order=" + order;
            if (!string.IsNullOrEmpty(after)) path += "&after=" + Uri.EscapeDataString(after);
            return InstrumentAsync("list_response_input", null, null, () => ExecuteGetAsync(new XAiJsonResponse(), "list response input", Url(path), ParseJsonResponse, token), token);
        }

        /// <summary>Compact a full input window. Reuse Data.output verbatim as the next response's input.</summary>
        public Task<XAiJsonResponse> CompactConversationAsync(JsonElement input, string? model = null, CancellationToken token = default)
        {
            ValidateInput(input);
            string resolvedModel = model ?? Model ?? throw new InvalidOperationException("No model configured.");
            ArgumentException.ThrowIfNullOrWhiteSpace(resolvedModel);
            return InstrumentAsync("compact_response", resolvedModel, null, () => ExecutePostAsync(new XAiJsonResponse { Model = resolvedModel }, "compact response", Url("responses/compact"),
                new Dictionary<string, object> { ["model"] = resolvedModel, ["input"] = input }, ParseJsonResponse, token), token);
        }

        private static void ValidateInput(JsonElement input)
        {
            if (input.ValueKind != JsonValueKind.String && input.ValueKind != JsonValueKind.Array)
                throw new ArgumentException("Responses input must be a JSON string or array.", nameof(input));
            if (input.ValueKind == JsonValueKind.Array && input.GetArrayLength() == 0) throw new ArgumentException("Input array must not be empty.", nameof(input));
        }
        private static void ParseJsonResponse(string json, XAiJsonResponse result)
        {
            result.Data = XAiProtocol.Parse(json);
            if (result.Data.Value.ValueKind != JsonValueKind.Object) result.Error = "Response is not a JSON object.";
            else if (XAiProtocol.Property(result.Data.Value, "error") is JsonElement error && error.ValueKind != JsonValueKind.Null) result.Error = ErrorMessage(error);
        }
    }
}
