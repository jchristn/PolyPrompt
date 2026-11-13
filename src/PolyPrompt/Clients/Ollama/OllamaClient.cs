namespace PolyPrompt.Clients
{
    using PolyPrompt.Models;
    using PolyPrompt.Options;

    /// <summary>
    /// Ollama client for the native Ollama API (<c>/api/*</c>) on a local or self-hosted server.
    /// <list type="bullet">
    /// <item><description>Supported: Chat, ToolChat, and Generate (<c>/api/chat</c> and <c>/api/generate</c>, streaming as
    /// newline-delimited JSON), Embed (<c>/api/embed</c>), and Models (<c>/api/tags</c>, <c>/api/show</c>), plus the
    /// Ollama-only <see cref="PullModelAsync"/>, <see cref="DeleteModelAsync"/>, <see cref="LoadModelAsync"/>, and
    /// <see cref="UnloadModelAsync"/>.</description></item>
    /// <item><description>Not supported (throw <see cref="CapabilityNotSupportedException"/>): SparseEmbed, Rerank,
    /// Classify, Decide.</description></item>
    /// <item><description>Auth: none by default; an optional bearer token for servers behind an authenticating
    /// proxy.</description></item>
    /// <item><description>Defaults: completion <c>gemma3:4b</c>, embedding <c>all-minilm</c>; no output-token limit is sent
    /// by default (<see cref="ProviderClientBase.SendDefaultMaxTokens"/> is false).</description></item>
    /// </list>
    /// For the OpenAI-compatible <c>/v1</c> API use <see cref="OpenAiCompatibleClient"/>.
    /// </summary>
    /// <seealso href="https://github.com/jchristn/Polyprompt#capability-matrix">Capability matrix</seealso>
    public partial class OllamaClient : ProviderClientBase
    {
        #region Public-Members

        /// <summary>
        /// Operations this client supports: Chat, ToolChat, Generate, Embed, and Models.
        /// </summary>
        public override Capability Capabilities => Capability.Chat | Capability.ToolChat | Capability.Generate | Capability.Embed | Capability.Models;

        /// <inheritdoc />
        public override string ProviderName => "Ollama";

        /// <summary>
        /// Client-wide defaults: <see cref="OllamaDefaults.Completion"/> (model <c>gemma3:4b</c>) and
        /// <see cref="OllamaDefaults.Embedding"/> (model <c>all-minilm</c>).
        /// </summary>
        public override OllamaDefaults Defaults { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize an Ollama client.
        /// </summary>
        /// <param name="endpoint">Ollama server URL. A trailing <c>/v1</c> is removed. Default: http://localhost:11434.</param>
        /// <param name="apiKey">Optional bearer token, for servers behind an authenticating proxy. Default: null.</param>
        /// <param name="options">Optional construction settings.</param>
        /// <exception cref="ArgumentException">Thrown when endpoint is not an absolute http(s) URL, or apiKey contains whitespace.</exception>
        public OllamaClient(string endpoint = OllamaProtocol.DefaultEndpoint, string? apiKey = null, ClientOptions? options = null)
            : this(endpoint, apiKey, options, new OllamaDefaults("gemma3:4b", "all-minilm"))
        {
        }

        /// <summary>
        /// Initialize an Ollama-protocol client with explicit defaults (used by <see cref="OllamaCloudClient"/>).
        /// </summary>
        /// <param name="endpoint">Endpoint URL.</param>
        /// <param name="apiKey">Bearer token, or null.</param>
        /// <param name="options">Construction settings.</param>
        /// <param name="defaults">Defaults instance.</param>
        private protected OllamaClient(string endpoint, string? apiKey, ClientOptions? options, OllamaDefaults defaults)
            : base(OllamaProtocol.NormalizeEndpoint(ClientArguments.Endpoint(endpoint, nameof(endpoint))), ClientArguments.OptionalKey(apiKey, nameof(apiKey)), options)
        {
            Defaults = defaults;
            _Header = OllamaProtocol.Header;

            // Ollama's own default (num_predict unset) is no limit; a fixed 4096 cuts reasoning models off mid-answer.
            SendDefaultMaxTokens = false;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Probe <c>/api/tags</c>. See <see cref="HttpClientBase.ValidateConnectivityAsync"/>.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the server responded successfully.</returns>
        public override Task<bool> ValidateConnectivityAsync(CancellationToken token = default)
        {
            return ProbeAsync(BuildUrl(OllamaProtocol.TagsPath), token);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string? ModelCatalogUrl => "https://ollama.com/library";

        /// <inheritdoc />
        protected override Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
        {
            OllamaProtocol.ApplyAuth(request, _ApiKey);
            return Task.CompletedTask;
        }

        #endregion
    }

    /// <summary>
    /// Defaults for <see cref="OllamaClient"/> and <see cref="OllamaCloudClient"/>.
    /// </summary>
    public sealed class OllamaDefaults : ClientDefaults
    {
        /// <summary>
        /// Initialize the defaults.
        /// </summary>
        /// <param name="completionModel">Default completion model, or null.</param>
        /// <param name="embeddingModel">Default embedding model, or null.</param>
        public OllamaDefaults(string? completionModel, string? embeddingModel)
        {
            Completion = new OllamaCompletionOptions { Model = completionModel };
            Embedding = new OllamaEmbeddingOptions { Model = embeddingModel };
        }

        /// <summary>
        /// Completion defaults, including Ollama settings such as <see cref="OllamaCompletionOptions.ContextLength"/>.
        /// </summary>
        public override OllamaCompletionOptions Completion { get; }

        /// <summary>
        /// Embedding defaults.
        /// </summary>
        public override OllamaEmbeddingOptions Embedding { get; }

        /// <summary>Not supported by Ollama; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Ollama does not support sparse embeddings.", true)]
        public new SparseEmbeddingOptions? SparseEmbedding => null;

        /// <summary>Not supported by Ollama; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Ollama does not support reranking.", true)]
        public new RerankOptions? Rerank => null;

        /// <summary>Not supported by Ollama; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Ollama does not support classification.", true)]
        public new ClassificationOptions? Classification => null;

        /// <summary>Not supported by Ollama; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Ollama does not support decisions.", true)]
        public new DecisionOptions? Decision => null;
    }
}
