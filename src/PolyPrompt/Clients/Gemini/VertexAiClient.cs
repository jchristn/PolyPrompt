namespace PolyPrompt.Clients
{
    using System.Net.Http.Headers;
    using PolyPrompt.Auth;
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using PolyPrompt.Telemetry;

    /// <summary>
    /// Google Vertex AI client for Gemini models and Vertex text embeddings.
    /// <list type="bullet">
    /// <item><description>Supported: Chat, ToolChat, and Generate (the same <c>generateContent</c> schema as
    /// <see cref="GeminiClient"/>, routed through the project, region, and publisher path), and Embed (<c>:predict</c>).
    /// </description></item>
    /// <item><description>Not supported (throw <see cref="CapabilityNotSupportedException"/>): SparseEmbed, Rerank,
    /// Classify, Decide, Models (Vertex has no per-project model listing for publisher models).</description></item>
    /// <item><description>Auth: an OAuth bearer token from <see cref="ICredentialProvider"/> (ADC or a service account),
    /// refreshed per request.</description></item>
    /// <item><description>Defaults: completion <c>gemini-3.8-flash</c>, embedding <c>gemini-embedding-001</c>.</description></item>
    /// </list>
    /// </summary>
    /// <seealso href="https://github.com/jchristn/Polyprompt#capability-matrix">Capability matrix</seealso>
    public partial class VertexAiClient : GenerateContentClientBase
    {
        #region Private-Members

        private readonly string _Project;
        private readonly string _Region;
        private readonly ICredentialProvider _Credential;

        #endregion

        #region Public-Members

        /// <summary>
        /// Operations this client supports: Chat, ToolChat, Generate, and Embed.
        /// </summary>
        public override Capability Capabilities => Capability.Chat | Capability.ToolChat | Capability.Generate | Capability.Embed;

        /// <inheritdoc />
        public override string ProviderName => "Vertex AI";

        /// <summary>
        /// Client-wide defaults: <see cref="VertexAiDefaults.Completion"/> and <see cref="VertexAiDefaults.Embedding"/>.
        /// </summary>
        public override VertexAiDefaults Defaults { get; } = new VertexAiDefaults();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a Vertex AI client.
        /// </summary>
        /// <param name="project">Google Cloud project id.</param>
        /// <param name="region">Vertex region, for example <c>us-central1</c>. Also selects the regional endpoint host.</param>
        /// <param name="credential">Credential provider supplying OAuth bearer tokens (ADC or a service account).</param>
        /// <param name="endpoint">Optional endpoint override. Default: <c>https://{region}-aiplatform.googleapis.com</c>.</param>
        /// <param name="options">Optional construction settings.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null or empty.</exception>
        /// <exception cref="ArgumentException">Thrown when endpoint is not an absolute http(s) URL.</exception>
        public VertexAiClient(string project, string region, ICredentialProvider credential, string? endpoint = null, ClientOptions? options = null)
            : base(ClientArguments.Endpoint(GeminiProtocol.ResolveVertexEndpoint(endpoint, region), nameof(endpoint)), null, options)
        {
            if (string.IsNullOrWhiteSpace(project)) throw new ArgumentNullException(nameof(project));
            _Project = project;
            _Region = region;
            _Credential = credential ?? throw new ArgumentNullException(nameof(credential));
            _Header = GeminiProtocol.VertexHeader;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Verify that Vertex AI is reachable and the credential is accepted, by counting the tokens of a one-word prompt
        /// with the completion model (<c>:countTokens</c>, which is not billed). Returns false on an HTTP error, an
        /// unreachable server, a timeout, or a credential failure; rethrows only caller cancellation.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the request succeeded.</returns>
        public override async Task<bool> ValidateConnectivityAsync(CancellationToken token = default)
        {
            return await InstrumentBoolAsync(PolyPromptTelemetryNames.OperationValidateConnectivity, null, async () =>
            {
                try
                {
                    Dictionary<string, object> body = new Dictionary<string, object>
                    {
                        { "contents", new List<object>
                            {
                                new Dictionary<string, object>
                                {
                                    { "role", "user" },
                                    { "parts", new List<object> { new Dictionary<string, object> { { "text", "ping" } } } }
                                }
                            }
                        }
                    };

                    HttpCallResult result = await PostJsonAsync(BuildModelUrl(Defaults.Completion.Model ?? "gemini-3.8-flash") + ":countTokens", body, token).ConfigureAwait(false);
                    return result.IsSuccessStatusCode;
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
        protected override string? ModelCatalogUrl => "https://docs.cloud.google.com/vertex-ai/generative-ai/docs/models";

        /// <inheritdoc />
        protected override string BuildModelUrl(string model)
        {
            return GeminiProtocol.VertexModelUrl(_Endpoint, _Project, _Region, model);
        }

        /// <inheritdoc />
        protected override async Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
        {
            string bearer = await _Credential.GetBearerTokenAsync(token).ConfigureAwait(false);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        #endregion
    }

    /// <summary>
    /// Defaults for <see cref="VertexAiClient"/>.
    /// </summary>
    public sealed class VertexAiDefaults : ClientDefaults
    {
        /// <summary>
        /// Completion defaults (the Gemini settings apply). Default model: <c>gemini-3.8-flash</c>.
        /// </summary>
        public override GeminiCompletionOptions Completion { get; } = new GeminiCompletionOptions { Model = "gemini-3.8-flash" };

        /// <summary>
        /// Embedding defaults. Default model: <c>gemini-embedding-001</c> (Vertex AI's current text embedding model; 3.x used <c>text-embedding-005</c>, which Vertex no longer lists).
        /// </summary>
        public override VertexAiEmbeddingOptions Embedding { get; } = new VertexAiEmbeddingOptions { Model = "gemini-embedding-001" };

        /// <summary>Not supported by Vertex AI; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Vertex AI does not support sparse embeddings.", true)]
        public new SparseEmbeddingOptions? SparseEmbedding => null;

        /// <summary>Not supported by Vertex AI; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Vertex AI does not support reranking.", true)]
        public new RerankOptions? Rerank => null;

        /// <summary>Not supported by Vertex AI; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Vertex AI does not support classification.", true)]
        public new ClassificationOptions? Classification => null;

        /// <summary>Not supported by Vertex AI; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Vertex AI does not support decisions.", true)]
        public new DecisionOptions? Decision => null;

    }
}
