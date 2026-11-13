namespace PolyPrompt.Clients
{
    using PolyPrompt.Models;
    using PolyPrompt.Options;

    /// <summary>
    /// Cohere client.
    /// <list type="bullet">
    /// <item><description>Supported: Chat, ToolChat, and Generate (<c>/v2/chat</c>, streaming as SSE), Embed
    /// (<c>/v2/embed</c>), Rerank (<c>/v2/rerank</c>), Classify (<c>/v1/classify</c>), and Models
    /// (<c>/v1/models</c>).</description></item>
    /// <item><description>Not supported (throw <see cref="CapabilityNotSupportedException"/>): SparseEmbed,
    /// Decide.</description></item>
    /// <item><description>Auth: required API key, sent as a bearer token.</description></item>
    /// <item><description>Defaults: completion <c>command-a-plus-05-2026</c>, embedding <c>embed-v4.0</c>, rerank
    /// <c>rerank-v5.0-fast</c>, classification chosen by Cohere.</description></item>
    /// </list>
    /// </summary>
    /// <seealso href="https://github.com/jchristn/Polyprompt#capability-matrix">Capability matrix</seealso>
    public partial class CohereClient : ProviderClientBase
    {
        #region Public-Members

        /// <summary>
        /// Operations this client supports: Chat, ToolChat, Generate, Embed, Rerank, Classify, and Models.
        /// </summary>
        public override Capability Capabilities => Capability.Chat | Capability.ToolChat | Capability.Generate | Capability.Embed | Capability.Rerank | Capability.Classify | Capability.Models;

        /// <inheritdoc />
        public override string ProviderName => "Cohere";

        /// <summary>
        /// Client-wide defaults: completion, embedding, rerank, and classification sections.
        /// </summary>
        public override CohereDefaults Defaults { get; } = new CohereDefaults();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a Cohere client.
        /// </summary>
        /// <param name="apiKey">Required Cohere API key, sent as a bearer token.</param>
        /// <param name="endpoint">API endpoint URL. Default: https://api.cohere.com.</param>
        /// <param name="options">Optional construction settings.</param>
        /// <exception cref="ArgumentNullException">Thrown when apiKey is null.</exception>
        /// <exception cref="ArgumentException">Thrown when apiKey is blank, contains whitespace, or looks like a URL, or endpoint is not an absolute http(s) URL.</exception>
        public CohereClient(string apiKey, string? endpoint = null, ClientOptions? options = null)
            : base(ClientArguments.Endpoint(endpoint ?? CohereProtocol.DefaultEndpoint, nameof(endpoint)), ClientArguments.RequiredKey(apiKey, nameof(apiKey), null), options)
        {
            _Header = CohereProtocol.Header;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Probe <c>/v1/models?page_size=1</c>. See <see cref="HttpClientBase.ValidateConnectivityAsync"/>.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the provider accepted the credentials.</returns>
        public override Task<bool> ValidateConnectivityAsync(CancellationToken token = default)
        {
            return ProbeAsync(BuildUrl(CohereProtocol.ProbePath), token);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string? ModelCatalogUrl => "https://docs.cohere.com/docs/models";

        /// <inheritdoc />
        protected override Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
        {
            CohereProtocol.ApplyAuth(request, _ApiKey);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        private protected override List<Citation>? ExtractCitations(System.Text.Json.JsonElement root)
        {
            return Helpers.CitationParser.Cohere(root);
        }

        #endregion
    }

    /// <summary>
    /// Defaults for <see cref="CohereClient"/>.
    /// </summary>
    public sealed class CohereDefaults : ClientDefaults
    {
        /// <summary>
        /// Completion defaults, including Cohere settings such as <see cref="CohereCompletionOptions.TopK"/>. Default model: <c>command-a-plus-05-2026</c>.
        /// </summary>
        public override CohereCompletionOptions Completion { get; } = new CohereCompletionOptions { Model = "command-a-plus-05-2026" };

        /// <summary>
        /// Embedding defaults. Default model: <c>embed-v4.0</c> (kept from 3.x; <c>embed-v5.0-pro</c> and <c>embed-v5.0-fast</c> are newer).
        /// </summary>
        public override CohereEmbeddingOptions Embedding { get; } = new CohereEmbeddingOptions { Model = "embed-v4.0" };

        /// <summary>Not supported by Cohere; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Cohere does not support sparse embeddings.", true)]
        public new SparseEmbeddingOptions? SparseEmbedding => null;

        /// <summary>
        /// Rerank defaults. Default model: <c>rerank-v5.0-fast</c>.
        /// </summary>
        public override CohereRerankOptions Rerank { get; } = new CohereRerankOptions { Model = "rerank-v5.0-fast" };

        /// <summary>
        /// Classification defaults. No default model: Cohere picks one. Cohere now recommends classifying with Embed models; <c>/v1/classify</c> remains available.
        /// </summary>
        public override CohereClassificationOptions Classification { get; } = new CohereClassificationOptions();

        /// <summary>Not supported by Cohere; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Cohere does not support decisions.", true)]
        public new DecisionOptions? Decision => null;

    }
}
