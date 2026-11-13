namespace PolyPrompt.Clients
{
    using System.Net.Http.Headers;
    using PolyPrompt.Models;
    using PolyPrompt.Options;

    /// <summary>
    /// Client for servers that implement OpenAI's Chat Completions API: vLLM, Ollama's <c>/v1</c> API, LM Studio,
    /// OpenRouter, Gemini's OpenAI-compatible endpoint, Azure's v1 Chat Completions route, gateways, and OpenAI itself
    /// when Chat Completions is wanted. For OpenAI's current API (Responses) use <see cref="OpenAiClient"/>.
    /// <list type="bullet">
    /// <item><description>Supported: Chat and ToolChat (<c>/v1/chat/completions</c>, streaming as SSE), Generate (the
    /// legacy <c>/v1/completions</c>), Embed (<c>/v1/embeddings</c>), and Models (<c>/v1/models</c>). Reasoning extensions
    /// (<c>reasoning_content</c>, <c>reasoning</c>) are read, and Gemini tool-call thought signatures are replayed in
    /// <c>extra_content.google.thought_signature</c>.</description></item>
    /// <item><description>Not supported (throw <see cref="CapabilityNotSupportedException"/>): SparseEmbed, Rerank,
    /// Classify, Decide.</description></item>
    /// <item><description>Auth: optional API key; placement set by <see cref="OpenAiCompatibleClientOptions.AuthPlacement"/>
    /// (default <c>Authorization: Bearer</c>).</description></item>
    /// <item><description>Defaults: none. Models are server-specific: set <c>Defaults.Completion.Model</c> and
    /// <c>Defaults.Embedding.Model</c>; a call without a model throws <see cref="InvalidOperationException"/>.</description></item>
    /// </list>
    /// <c>/v1</c> is appended to the endpoint unless it already ends with it.
    /// </summary>
    /// <seealso href="https://github.com/jchristn/Polyprompt#capability-matrix">Capability matrix</seealso>
    public partial class OpenAiCompatibleClient : ProviderClientBase
    {
        #region Private-Members

        private const string ChatCompletionsProtocol = "chat_completions";
        private readonly AuthPlacement _AuthPlacement;

        #endregion

        #region Public-Members

        /// <summary>
        /// Operations this client supports: Chat, ToolChat, Generate, Embed, and Models.
        /// </summary>
        public override Capability Capabilities => Capability.Chat | Capability.ToolChat | Capability.Generate | Capability.Embed | Capability.Models;

        /// <inheritdoc />
        public override string ProviderName => "OpenAI-compatible server";

        /// <summary>
        /// Client-wide defaults: <see cref="OpenAiCompatibleDefaults.Completion"/> and
        /// <see cref="OpenAiCompatibleDefaults.Embedding"/>. Neither has a default model.
        /// </summary>
        public override OpenAiCompatibleDefaults Defaults { get; } = new OpenAiCompatibleDefaults();

        /// <summary>
        /// Where the API key is sent.
        /// </summary>
        public AuthPlacement AuthPlacement
        {
            get { return _AuthPlacement; }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a client for a Chat Completions server.
        /// </summary>
        /// <param name="endpoint">Server URL, for example http://localhost:8000/v1. <c>/v1</c> is appended unless present.</param>
        /// <param name="apiKey">Optional API key. Default: null.</param>
        /// <param name="options">Optional construction settings; pass <see cref="OpenAiCompatibleClientOptions"/> to choose
        /// where the key is placed.</param>
        /// <exception cref="ArgumentException">Thrown when endpoint is not an absolute http(s) URL, or apiKey contains whitespace or looks like a URL.</exception>
        public OpenAiCompatibleClient(string endpoint, string? apiKey = null, ClientOptions? options = null)
            : base(ClientArguments.Endpoint(endpoint, nameof(endpoint)), ClientArguments.OptionalKey(apiKey, nameof(apiKey)), options)
        {
            _AuthPlacement = (options as OpenAiCompatibleClientOptions)?.AuthPlacement ?? AuthPlacement.Bearer;
            _Header = OpenAiProtocol.Header;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Probe <c>/v1/models</c>. See <see cref="HttpClientBase.ValidateConnectivityAsync"/>.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the server responded successfully.</returns>
        public override Task<bool> ValidateConnectivityAsync(CancellationToken token = default)
        {
            return ProbeAsync(BuildApiUrl("models", null), token);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Build the URL for an operation path such as <c>chat/completions</c>.
        /// </summary>
        /// <param name="path">Operation path.</param>
        /// <param name="model">The model for this call, or null.</param>
        /// <returns>The absolute URL.</returns>
        protected virtual string BuildApiUrl(string path, string? model)
        {
            return OpenAiProtocol.BuildApiUrl(_Endpoint, path);
        }

        /// <inheritdoc />
        protected override Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
        {
            if (string.IsNullOrEmpty(_ApiKey)) return Task.CompletedTask;

            switch (_AuthPlacement.Kind)
            {
                case AuthPlacementKind.Header:
                    request.Headers.Remove(_AuthPlacement.Name);
                    request.Headers.TryAddWithoutValidation(_AuthPlacement.Name, _ApiKey);
                    break;
                case AuthPlacementKind.Query:
                    if (request.RequestUri != null)
                    {
                        string uri = request.RequestUri.ToString();
                        string separator = uri.Contains('?') ? "&" : "?";
                        request.RequestUri = new Uri(uri + separator + Uri.EscapeDataString(_AuthPlacement.Name) + "=" + Uri.EscapeDataString(_ApiKey));
                    }
                    break;
                default:
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _ApiKey);
                    break;
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override Task<EmbeddingResponse> EmbedCoreAsync(List<string> inputs, string? model, EmbeddingOptions? options, CancellationToken token)
        {
            return OpenAiEmbedAsync(BuildApiUrl("embeddings", model), inputs, model, options, token);
        }

        /// <inheritdoc />
        protected override IAsyncEnumerable<ModelInformation> ListModelsCoreAsync(CancellationToken token)
        {
            return OpenAiListModelsAsync(BuildApiUrl("models", null), token);
        }

        /// <inheritdoc />
        protected override Task<ModelInformation?> GetModelInformationCoreAsync(string model, CancellationToken token)
        {
            return OpenAiGetModelAsync(BuildApiUrl("models/" + Uri.EscapeDataString(model), null), model, token);
        }

        #endregion
    }

    /// <summary>
    /// Defaults for <see cref="OpenAiCompatibleClient"/>.
    /// </summary>
    public sealed class OpenAiCompatibleDefaults : ClientDefaults
    {
        /// <summary>
        /// Completion defaults, including Chat Completions settings such as penalties and seed. No default model: models are server-specific, so set <c>Model</c>.
        /// </summary>
        public override OpenAiCompatibleCompletionOptions Completion { get; } = new OpenAiCompatibleCompletionOptions();

        /// <summary>
        /// Embedding defaults. No default model: models are server-specific, so set <c>Model</c>.
        /// </summary>
        public override OpenAiEmbeddingOptions Embedding { get; } = new OpenAiEmbeddingOptions();

        /// <summary>Not supported by OpenAiCompatibleClient; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("OpenAiCompatibleClient does not support sparse embeddings.", true)]
        public new SparseEmbeddingOptions? SparseEmbedding => null;

        /// <summary>Not supported by OpenAiCompatibleClient; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("OpenAiCompatibleClient does not support reranking.", true)]
        public new RerankOptions? Rerank => null;

        /// <summary>Not supported by OpenAiCompatibleClient; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("OpenAiCompatibleClient does not support classification.", true)]
        public new ClassificationOptions? Classification => null;

        /// <summary>Not supported by OpenAiCompatibleClient; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("OpenAiCompatibleClient does not support decisions.", true)]
        public new DecisionOptions? Decision => null;

    }
}
