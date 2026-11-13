namespace PolyPrompt.Clients
{
    using PolyPrompt.Models;
    using PolyPrompt.Options;

    /// <summary>
    /// Anthropic (Claude) client for the Messages API.
    /// <list type="bullet">
    /// <item><description>Supported: Chat, ToolChat, and Generate (<c>/v1/messages</c>, generation sent as one user turn,
    /// streaming as typed SSE events), and Models (<c>/v1/models</c>, following pagination).</description></item>
    /// <item><description>Not supported (throw <see cref="CapabilityNotSupportedException"/>): Embed, SparseEmbed, Rerank,
    /// Classify, Decide. For embeddings with Claude, pair it with <see cref="VoyageAiClient"/>.</description></item>
    /// <item><description>Auth: required API key, sent as <c>x-api-key</c>, plus <c>anthropic-version</c> and an optional
    /// <c>anthropic-workspace-id</c>.</description></item>
    /// <item><description>Defaults: completion <c>claude-sonnet-5-5</c>. <c>max_tokens</c> is always sent (the API requires
    /// it).</description></item>
    /// </list>
    /// Tool results are sent as user-role <c>tool_result</c> blocks, and consecutive results merge into one user turn.
    /// Citations on documents or search results cannot be combined with structured output (<c>output_config.format</c>);
    /// the API returns 400.
    /// </summary>
    /// <seealso href="https://github.com/jchristn/Polyprompt#capability-matrix">Capability matrix</seealso>
    public partial class AnthropicClient : ProviderClientBase
    {
        #region Private-Members

        private string _AnthropicVersion = AnthropicProtocol.DefaultVersion;
        private string? _WorkspaceId = null;

        #endregion

        #region Public-Members

        /// <summary>
        /// Operations this client supports: Chat, ToolChat, Generate, and Models.
        /// </summary>
        public override Capability Capabilities => Capability.Chat | Capability.ToolChat | Capability.Generate | Capability.Models;

        /// <inheritdoc />
        public override string ProviderName => "Anthropic";

        /// <summary>
        /// Client-wide defaults: <see cref="AnthropicDefaults.Completion"/>.
        /// </summary>
        public override AnthropicDefaults Defaults { get; } = new AnthropicDefaults();

        /// <summary>
        /// Value sent in the <c>anthropic-version</c> header. Default: 2023-06-01.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null, empty, or whitespace.</exception>
        public string AnthropicVersion
        {
            get { return _AnthropicVersion; }
            set { _AnthropicVersion = AnthropicProtocol.ValidateVersion(value, nameof(AnthropicVersion)); }
        }

        /// <summary>
        /// Workspace identifier sent in the <c>anthropic-workspace-id</c> header. Identity-linked API keys reject requests
        /// without it; standard workspace keys do not need it. Null, empty, or whitespace sends no header. Default: null.
        /// </summary>
        public string? WorkspaceId
        {
            get { return _WorkspaceId; }
            set { _WorkspaceId = AnthropicProtocol.NormalizeWorkspace(value); }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize an Anthropic client.
        /// </summary>
        /// <param name="apiKey">Required Anthropic API key, sent as the <c>x-api-key</c> header.</param>
        /// <param name="endpoint">API endpoint URL. Default: https://api.anthropic.com.</param>
        /// <param name="options">Optional construction settings.</param>
        /// <exception cref="ArgumentNullException">Thrown when apiKey is null.</exception>
        /// <exception cref="ArgumentException">Thrown when apiKey is blank, contains whitespace, or looks like a URL, or endpoint is not an absolute http(s) URL.</exception>
        public AnthropicClient(string apiKey, string? endpoint = null, ClientOptions? options = null)
            : base(ClientArguments.Endpoint(endpoint ?? AnthropicProtocol.DefaultEndpoint, nameof(endpoint)), ClientArguments.RequiredKey(apiKey, nameof(apiKey), null), options)
        {
            _Header = AnthropicProtocol.Header;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Probe <c>/v1/models?limit=1</c>. See <see cref="HttpClientBase.ValidateConnectivityAsync"/>.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the provider accepted the credentials.</returns>
        public override Task<bool> ValidateConnectivityAsync(CancellationToken token = default)
        {
            return ProbeAsync(BuildUrl("/v1/models?limit=1"), token);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string? ModelCatalogUrl => "https://platform.claude.com/docs/en/about-claude/models/overview";

        /// <inheritdoc />
        protected override Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
        {
            AnthropicProtocol.ApplyHeaders(request, _ApiKey, _AnthropicVersion, _WorkspaceId);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        private protected override List<Citation>? ExtractCitations(System.Text.Json.JsonElement root)
        {
            return Helpers.CitationParser.Anthropic(Helpers.CitationParser.Prop(root, "content") ?? default);
        }

        #endregion
    }

    /// <summary>
    /// Defaults for <see cref="AnthropicClient"/>.
    /// </summary>
    public sealed class AnthropicDefaults : ClientDefaults
    {
        /// <summary>
        /// Completion defaults, including Anthropic settings such as <see cref="AnthropicCompletionOptions.TopK"/>. Default model: <c>claude-sonnet-5-5</c> (Anthropic's balanced model; <c>claude-opus-5-5</c> and <c>claude-haiku-5-5</c> are the flagship and small alternatives).
        /// </summary>
        public override AnthropicCompletionOptions Completion { get; } = new AnthropicCompletionOptions { Model = "claude-sonnet-5-5" };

        /// <summary>Not supported by Anthropic; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Anthropic does not support embeddings.", true)]
        public new EmbeddingOptions? Embedding => null;

        /// <summary>Not supported by Anthropic; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Anthropic does not support sparse embeddings.", true)]
        public new SparseEmbeddingOptions? SparseEmbedding => null;

        /// <summary>Not supported by Anthropic; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Anthropic does not support reranking.", true)]
        public new RerankOptions? Rerank => null;

        /// <summary>Not supported by Anthropic; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Anthropic does not support classification.", true)]
        public new ClassificationOptions? Classification => null;

        /// <summary>Not supported by Anthropic; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Anthropic does not support decisions.", true)]
        public new DecisionOptions? Decision => null;

    }
}
