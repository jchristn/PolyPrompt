namespace PolyPrompt.Clients
{
    using System.Net.Http.Headers;
    using System.Text.Json;
    using PolyPrompt.Models;
    using PolyPrompt.Options;

    /// <summary>
    /// OpenAI client for the Responses API (<c>/v1/responses</c>), OpenAI's current API for reasoning and tool use. It
    /// talks to OpenAI the service, or any server that implements <c>/v1/responses</c>. For servers that only implement
    /// Chat Completions (vLLM, Ollama <c>/v1</c>, LM Studio, gateways) use <see cref="OpenAiCompatibleClient"/>; a 404 or
    /// 405 from <c>/responses</c> says so in the error.
    /// <list type="bullet">
    /// <item><description>Supported: Chat, ToolChat, and Generate (Responses API; encrypted reasoning is carried in
    /// <see cref="ResponseBase.ProviderState"/>, so a tool loop keeps its reasoning between calls), Embed
    /// (<c>/v1/embeddings</c>), and Models (<c>/v1/models</c>), plus the native operations on
    /// <see cref="ResponsesApiClientBase"/> and the OpenAI-only <see cref="CancelResponseAsync"/> and
    /// <see cref="CountInputTokensAsync"/>.</description></item>
    /// <item><description>Not supported (throw <see cref="CapabilityNotSupportedException"/>): SparseEmbed, Rerank,
    /// Classify, Decide.</description></item>
    /// <item><description>Auth: required API key, sent as a bearer token.</description></item>
    /// <item><description>Defaults: completion <c>gpt-6.1-sol</c> with Store = false (sent explicitly; OpenAI's own default
    /// is true), embedding <c>text-embedding-3-small</c>.</description></item>
    /// </list>
    /// </summary>
    /// <seealso href="https://github.com/jchristn/Polyprompt#capability-matrix">Capability matrix</seealso>
    public class OpenAiClient : ResponsesApiClientBase
    {
        #region Private-Members

        private static readonly ResponsesApiDialect _Dialect = OpenAiResponsesFields.Dialect("OpenAI",
            "This server may not implement the OpenAI Responses API. Use OpenAiCompatibleClient for Chat Completions servers.");

        #endregion

        #region Public-Members

        /// <summary>
        /// Operations this client supports: Chat, ToolChat, Generate, Embed, and Models.
        /// </summary>
        public override Capability Capabilities => Capability.Chat | Capability.ToolChat | Capability.Generate | Capability.Embed | Capability.Models;

        /// <inheritdoc />
        public override string ProviderName => "OpenAI";

        /// <summary>
        /// Client-wide defaults: <see cref="OpenAiDefaults.Completion"/> and <see cref="OpenAiDefaults.Embedding"/>.
        /// </summary>
        public override OpenAiDefaults Defaults { get; } = new OpenAiDefaults();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize an OpenAI client.
        /// </summary>
        /// <param name="apiKey">Required OpenAI API key, sent as a bearer token.</param>
        /// <param name="endpoint">API endpoint URL; <c>/v1</c> is appended unless present. Default: https://api.openai.com.</param>
        /// <param name="options">Optional construction settings.</param>
        /// <exception cref="ArgumentNullException">Thrown when apiKey is null.</exception>
        /// <exception cref="ArgumentException">Thrown when apiKey is blank, contains whitespace, or looks like a URL (use
        /// <see cref="OpenAiCompatibleClient"/> for Chat Completions servers), or endpoint is not an absolute http(s) URL.</exception>
        public OpenAiClient(string apiKey, string? endpoint = null, ClientOptions? options = null)
            : base(ClientArguments.Endpoint(endpoint ?? OpenAiProtocol.DefaultEndpoint, nameof(endpoint)),
                ClientArguments.RequiredKey(apiKey, nameof(apiKey), "For Chat Completions servers (vLLM, Ollama /v1, LM Studio, gateways) use OpenAiCompatibleClient."),
                options)
        {
            _Header = OpenAiProtocol.Header;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Probe <c>/v1/models</c>. See <see cref="HttpClientBase.ValidateConnectivityAsync"/>.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the provider accepted the credentials.</returns>
        public override Task<bool> ValidateConnectivityAsync(CancellationToken token = default) => ProbeAsync(Url("models"), token);

        /// <summary>
        /// Cancel a background response (<c>POST /responses/{id}/cancel</c>).
        /// </summary>
        /// <param name="responseId">Response id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response after cancellation.</returns>
        /// <exception cref="ArgumentException">Thrown when responseId is null, empty, or whitespace.</exception>
        public Task<ResponsesApiResponse> CancelResponseAsync(string responseId, CancellationToken token = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(responseId);
            return PostResponseAsync("cancel_response", "responses/" + Uri.EscapeDataString(responseId) + "/cancel", new Dictionary<string, object>(), true, token);
        }

        /// <summary>
        /// Count the input tokens a request would use, without running it (<c>POST /responses/input_tokens</c>). Inspect
        /// <c>Data.input_tokens</c>.
        /// </summary>
        /// <param name="input">Input string or array.</param>
        /// <param name="options">Optional settings (model, instructions via SystemPrompt, tools, reasoning, text format).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The provider's JSON result.</returns>
        /// <exception cref="ArgumentException">Thrown when input is not a string or non-empty array.</exception>
        /// <exception cref="InvalidOperationException">Thrown when no model is configured.</exception>
        public Task<ResponsesApiJsonResponse> CountInputTokensAsync(JsonElement input, OpenAiCompletionOptions? options = null, CancellationToken token = default)
        {
            ValidateInput(input);
            ResolvedCompletion settings = Resolve(options);
            Dictionary<string, object> body = OpenAiResponsesFields.InputTokensBody(BuildNativeBody(input, settings));
            return PostJsonResultAsync("count_input_tokens", "responses/input_tokens", body, settings.Model, token);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        private protected override ResponsesApiDialect Dialect => _Dialect;

        /// <inheritdoc />
        protected override string? ModelCatalogUrl => "https://developers.openai.com/api/docs/models";

        /// <inheritdoc />
        protected override string Url(string path) => OpenAiProtocol.BuildApiUrl(_Endpoint, path);

        /// <inheritdoc />
        protected override Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _ApiKey);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        private protected override void AddProviderFields(Dictionary<string, object> body, ResolvedCompletion settings, bool stream, ResponsesApiFieldStage stage)
        {
            if (stage == ResponsesApiFieldStage.AfterSafetyIdentifier) OpenAiResponsesFields.Add(body, settings.Call, Defaults.Completion, stream);
        }

        /// <inheritdoc />
        protected override Task<EmbeddingResponse> EmbedCoreAsync(List<string> inputs, string? model, EmbeddingOptions? options, CancellationToken token)
        {
            return OpenAiEmbedAsync(Url("embeddings"), inputs, model, options, token);
        }

        /// <inheritdoc />
        protected override IAsyncEnumerable<ModelInformation> ListModelsCoreAsync(CancellationToken token)
        {
            return OpenAiListModelsAsync(Url("models"), token);
        }

        /// <inheritdoc />
        protected override Task<ModelInformation?> GetModelInformationCoreAsync(string model, CancellationToken token)
        {
            return OpenAiGetModelAsync(Url("models/" + Uri.EscapeDataString(model)), model, token);
        }

        #endregion
    }

    /// <summary>
    /// Defaults for <see cref="OpenAiClient"/>.
    /// </summary>
    public sealed class OpenAiDefaults : ClientDefaults
    {
        /// <summary>
        /// Completion defaults (Responses API), including OpenAI settings such as <see cref="OpenAiCompletionOptions.Background"/>. Default model: <c>gpt-6.1-sol</c> (OpenAI's balanced model; <c>gpt-6-astra</c> is the flagship and <c>gpt-6-luna</c> the small model). Store = false.
        /// </summary>
        public override OpenAiCompletionOptions Completion { get; } = new OpenAiCompletionOptions { Model = "gpt-6.1-sol", Store = false };

        /// <summary>
        /// Embedding defaults. Default model: <c>text-embedding-3-small</c>.
        /// </summary>
        public override OpenAiEmbeddingOptions Embedding { get; } = new OpenAiEmbeddingOptions { Model = "text-embedding-3-small" };

        /// <summary>Not supported by OpenAI; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("OpenAI does not support sparse embeddings.", true)]
        public new SparseEmbeddingOptions? SparseEmbedding => null;

        /// <summary>Not supported by OpenAI; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("OpenAI does not support reranking.", true)]
        public new RerankOptions? Rerank => null;

        /// <summary>Not supported by OpenAI; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("OpenAI does not support classification.", true)]
        public new ClassificationOptions? Classification => null;

        /// <summary>Not supported by OpenAI; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("OpenAI does not support decisions.", true)]
        public new DecisionOptions? Decision => null;

    }
}
