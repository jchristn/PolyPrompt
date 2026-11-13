namespace PolyPrompt.Clients
{
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using PolyPrompt.Telemetry;

    /// <summary>
    /// VoyageAI client.
    /// <list type="bullet">
    /// <item><description>Supported: Embed (<c>/v1/embeddings</c>) and Rerank (<c>/v1/rerank</c>).</description></item>
    /// <item><description>Not supported (throw <see cref="CapabilityNotSupportedException"/>): Chat, ToolChat, Generate,
    /// SparseEmbed, Classify, Decide, Models.</description></item>
    /// <item><description>Auth: required API key, sent as a bearer token.</description></item>
    /// <item><description>Defaults: embedding <c>voyage-3.5</c>, rerank <c>rerank-3</c>.</description></item>
    /// </list>
    /// </summary>
    /// <seealso href="https://github.com/jchristn/Polyprompt#capability-matrix">Capability matrix</seealso>
    public partial class VoyageAiClient : ProviderClientBase
    {
        #region Public-Members

        /// <summary>
        /// Operations this client supports: Embed and Rerank.
        /// </summary>
        public override Capability Capabilities => Capability.Embed | Capability.Rerank;

        /// <inheritdoc />
        public override string ProviderName => "VoyageAI";

        /// <summary>
        /// Client-wide defaults: <see cref="VoyageAiDefaults.Embedding"/> and <see cref="VoyageAiDefaults.Rerank"/>.
        /// </summary>
        public override VoyageAiDefaults Defaults { get; } = new VoyageAiDefaults();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a VoyageAI client.
        /// </summary>
        /// <param name="apiKey">Required VoyageAI API key, sent as a bearer token.</param>
        /// <param name="endpoint">API endpoint URL. Default: https://api.voyageai.com.</param>
        /// <param name="options">Optional construction settings.</param>
        /// <exception cref="ArgumentNullException">Thrown when apiKey is null.</exception>
        /// <exception cref="ArgumentException">Thrown when apiKey is blank, contains whitespace, or looks like a URL, or endpoint is not an absolute http(s) URL.</exception>
        public VoyageAiClient(string apiKey, string? endpoint = null, ClientOptions? options = null)
            : base(ClientArguments.Endpoint(endpoint ?? VoyageAiProtocol.DefaultEndpoint, nameof(endpoint)), ClientArguments.RequiredKey(apiKey, nameof(apiKey), null), options)
        {
            _Header = VoyageAiProtocol.Header;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Verify connectivity and credentials by embedding a one-word input with the embedding model (VoyageAI has no
        /// models route). Returns false on an HTTP error, an unreachable server, or a timeout; rethrows only caller
        /// cancellation.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the request succeeded.</returns>
        public override async Task<bool> ValidateConnectivityAsync(CancellationToken token = default)
        {
            return await InstrumentBoolAsync(PolyPromptTelemetryNames.OperationValidateConnectivity, null, async () =>
            {
                try
                {
                    EmbeddingResponse probe = await EmbedAsync("ping", null, token).ConfigureAwait(false);
                    return probe.Success;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _Logging.Debug(_Header + "connectivity probe failed: " + ex.Message);
                    return false;
                }
            }, token).ConfigureAwait(false);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string? ModelCatalogUrl => "https://docs.voyageai.com/docs/embeddings";

        /// <inheritdoc />
        protected override Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
        {
            VoyageAiProtocol.ApplyAuth(request, _ApiKey);
            return Task.CompletedTask;
        }

        #endregion
    }

    /// <summary>
    /// Defaults for <see cref="VoyageAiClient"/>.
    /// </summary>
    public sealed class VoyageAiDefaults : ClientDefaults
    {
        /// <summary>Not supported by VoyageAI; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("VoyageAI does not support completion.", true)]
        public new CompletionOptions? Completion => null;

        /// <summary>
        /// Embedding defaults. Default model: <c>voyage-3.5</c> (kept from 3.x so stored vectors stay compatible; <c>voyage-4</c> is the current recommendation).
        /// </summary>
        public override VoyageAiEmbeddingOptions Embedding { get; } = new VoyageAiEmbeddingOptions { Model = "voyage-3.5" };

        /// <summary>Not supported by VoyageAI; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("VoyageAI does not support sparse embeddings.", true)]
        public new SparseEmbeddingOptions? SparseEmbedding => null;

        /// <summary>
        /// Rerank defaults. Default model: <c>rerank-3</c>.
        /// </summary>
        public override VoyageAiRerankOptions Rerank { get; } = new VoyageAiRerankOptions { Model = "rerank-3" };

        /// <summary>Not supported by VoyageAI; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("VoyageAI does not support classification.", true)]
        public new ClassificationOptions? Classification => null;

        /// <summary>Not supported by VoyageAI; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("VoyageAI does not support decisions.", true)]
        public new DecisionOptions? Decision => null;

    }
}
