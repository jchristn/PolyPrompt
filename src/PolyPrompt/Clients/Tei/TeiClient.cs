namespace PolyPrompt.Clients
{
    using PolyPrompt.Models;
    using PolyPrompt.Options;

    /// <summary>
    /// Hugging Face Text Embeddings Inference (TEI) client. A TEI server hosts one model; which operations succeed depends
    /// on that model's type.
    /// <list type="bullet">
    /// <item><description>Supported: Embed (<c>/embed</c>), SparseEmbed (<c>/embed_sparse</c>, SPLADE models), Rerank
    /// (<c>/rerank</c>, cross-encoders), Classify (<c>/predict</c>), and Models (<c>/info</c>).</description></item>
    /// <item><description>Not supported (throw <see cref="CapabilityNotSupportedException"/>): Chat, ToolChat, Generate,
    /// Decide.</description></item>
    /// <item><description>Auth: none by default; an optional bearer token for protected deployments (for example Hugging
    /// Face Inference Endpoints).</description></item>
    /// <item><description>Defaults: none needed; the server's hosted model is used.</description></item>
    /// </list>
    /// </summary>
    /// <seealso href="https://github.com/jchristn/Polyprompt#capability-matrix">Capability matrix</seealso>
    public partial class TeiClient : ProviderClientBase
    {
        #region Public-Members

        /// <summary>
        /// Operations this client supports: Embed, SparseEmbed, Rerank, Classify, and Models.
        /// </summary>
        public override Capability Capabilities => Capability.Embed | Capability.SparseEmbed | Capability.Rerank | Capability.Classify | Capability.Models;

        /// <inheritdoc />
        public override string ProviderName => "TEI";

        /// <summary>
        /// Client-wide defaults: embedding, sparse embedding, rerank, and classification sections.
        /// </summary>
        public override TeiDefaults Defaults { get; } = new TeiDefaults();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a TEI client.
        /// </summary>
        /// <param name="endpoint">TEI server URL, for example http://localhost:8080.</param>
        /// <param name="apiKey">Optional bearer token. Default: null.</param>
        /// <param name="options">Optional construction settings.</param>
        /// <exception cref="ArgumentException">Thrown when endpoint is not an absolute http(s) URL, or apiKey contains whitespace or looks like a URL.</exception>
        public TeiClient(string endpoint, string? apiKey = null, ClientOptions? options = null)
            : base(ClientArguments.Endpoint(endpoint, nameof(endpoint)), ClientArguments.OptionalKey(apiKey, nameof(apiKey)), options)
        {
            _Header = TeiProtocol.Header;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Probe <c>/health</c>. See <see cref="HttpClientBase.ValidateConnectivityAsync"/>.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the server responded successfully.</returns>
        public override Task<bool> ValidateConnectivityAsync(CancellationToken token = default)
        {
            return ProbeAsync(BuildUrl(TeiProtocol.HealthPath), token);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
        {
            TeiProtocol.ApplyAuth(request, _ApiKey);
            return Task.CompletedTask;
        }

        #endregion
    }

    /// <summary>
    /// Defaults for <see cref="TeiClient"/>. TEI serves one model per server, so no section needs a model.
    /// </summary>
    public sealed class TeiDefaults : ClientDefaults
    {
        /// <summary>Not supported by TEI; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("TEI does not support completion.", true)]
        public new CompletionOptions? Completion => null;

        /// <summary>
        /// Embedding defaults. No model: the server serves the model it was started with.
        /// </summary>
        public override TeiEmbeddingOptions Embedding { get; } = new TeiEmbeddingOptions();

        /// <summary>
        /// Sparse embedding defaults (SPLADE models).
        /// </summary>
        public override TeiSparseEmbeddingOptions SparseEmbedding { get; } = new TeiSparseEmbeddingOptions();

        /// <summary>
        /// Rerank defaults (cross-encoder models).
        /// </summary>
        public override TeiRerankOptions Rerank { get; } = new TeiRerankOptions();

        /// <summary>
        /// Classification defaults (sequence-classification models).
        /// </summary>
        public override TeiClassificationOptions Classification { get; } = new TeiClassificationOptions();

        /// <summary>Not supported by TEI; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("TEI does not support decisions.", true)]
        public new DecisionOptions? Decision => null;

    }
}
