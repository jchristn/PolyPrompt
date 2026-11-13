namespace PolyPrompt.Clients
{
    using PolyPrompt.Models;
    using PolyPrompt.Options;

    /// <summary>
    /// Google Gemini client for the Gemini API (Google AI Studio, <c>generativelanguage.googleapis.com</c>).
    /// <list type="bullet">
    /// <item><description>Supported: Chat, ToolChat, and Generate (<c>generateContent</c> and
    /// <c>streamGenerateContent</c>), Embed (<c>:embedContent</c> and <c>:batchEmbedContents</c>), and Models
    /// (<c>/v1beta/models</c>, following pagination).</description></item>
    /// <item><description>Not supported (throw <see cref="CapabilityNotSupportedException"/>): SparseEmbed, Rerank,
    /// Classify, Decide.</description></item>
    /// <item><description>Auth: required API key, sent as <c>x-goog-api-key</c>.</description></item>
    /// <item><description>Defaults: completion <c>gemini-3.8-flash</c>, embedding <c>gemini-embedding-001</c>.</description></item>
    /// </list>
    /// Request building, streaming, tool calling, thought signatures, and reasoning are shared with
    /// <see cref="VertexAiClient"/> through <see cref="GenerateContentClientBase"/>.
    /// </summary>
    /// <seealso href="https://github.com/jchristn/Polyprompt#capability-matrix">Capability matrix</seealso>
    public partial class GeminiClient : GenerateContentClientBase
    {
        #region Public-Members

        /// <summary>
        /// Operations this client supports: Chat, ToolChat, Generate, Embed, and Models.
        /// </summary>
        public override Capability Capabilities => Capability.Chat | Capability.ToolChat | Capability.Generate | Capability.Embed | Capability.Models;

        /// <inheritdoc />
        public override string ProviderName => "Gemini";

        /// <summary>
        /// Client-wide defaults: <see cref="GeminiDefaults.Completion"/> and <see cref="GeminiDefaults.Embedding"/>.
        /// </summary>
        public override GeminiDefaults Defaults { get; } = new GeminiDefaults();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a Gemini client.
        /// </summary>
        /// <param name="apiKey">Required Google AI Studio API key, sent as the <c>x-goog-api-key</c> header.</param>
        /// <param name="endpoint">API endpoint URL. Default: https://generativelanguage.googleapis.com.</param>
        /// <param name="options">Optional construction settings.</param>
        /// <exception cref="ArgumentNullException">Thrown when apiKey is null.</exception>
        /// <exception cref="ArgumentException">Thrown when apiKey is blank, contains whitespace, or looks like a URL, or endpoint is not an absolute http(s) URL.</exception>
        public GeminiClient(string apiKey, string? endpoint = null, ClientOptions? options = null)
            : base(ClientArguments.Endpoint(endpoint ?? GeminiProtocol.DefaultEndpoint, nameof(endpoint)), ClientArguments.RequiredKey(apiKey, nameof(apiKey), "For Gemini's OpenAI-compatible endpoint use OpenAiCompatibleClient."), options)
        {
            _Header = GeminiProtocol.Header;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Probe <c>/v1beta/models?pageSize=1</c>. See <see cref="HttpClientBase.ValidateConnectivityAsync"/>.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the provider accepted the credentials.</returns>
        public override Task<bool> ValidateConnectivityAsync(CancellationToken token = default)
        {
            return ProbeAsync(_Endpoint.TrimEnd('/') + "/v1beta/models?pageSize=1", token);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string? ModelCatalogUrl => "https://aistudio.google.com/docs/models";

        /// <inheritdoc />
        protected override string BuildModelUrl(string model)
        {
            return GeminiProtocol.ModelUrl(_Endpoint, model);
        }

        /// <inheritdoc />
        protected override Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
        {
            GeminiProtocol.ApplyApiKey(request, _ApiKey);
            return Task.CompletedTask;
        }

        #endregion
    }

    /// <summary>
    /// Defaults for <see cref="GeminiClient"/>.
    /// </summary>
    public sealed class GeminiDefaults : ClientDefaults
    {
        /// <summary>
        /// Completion defaults, including Gemini settings such as <see cref="GeminiCompletionOptions.TopK"/> and <see cref="GeminiCompletionOptions.ToolSchemaMode"/>. Default model: <c>gemini-3.8-flash</c> (the newest stable Flash model).
        /// </summary>
        public override GeminiCompletionOptions Completion { get; } = new GeminiCompletionOptions { Model = "gemini-3.8-flash" };

        /// <summary>
        /// Embedding defaults. Default model: <c>gemini-embedding-001</c> (kept from 3.x so stored vectors stay compatible; <c>gemini-embedding-2</c> is the newer multimodal model).
        /// </summary>
        public override GeminiEmbeddingOptions Embedding { get; } = new GeminiEmbeddingOptions { Model = "gemini-embedding-001" };

        /// <summary>Not supported by Gemini; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Gemini does not support sparse embeddings.", true)]
        public new SparseEmbeddingOptions? SparseEmbedding => null;

        /// <summary>Not supported by Gemini; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Gemini does not support reranking.", true)]
        public new RerankOptions? Rerank => null;

        /// <summary>Not supported by Gemini; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Gemini does not support classification.", true)]
        public new ClassificationOptions? Classification => null;

        /// <summary>Not supported by Gemini; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Gemini does not support decisions.", true)]
        public new DecisionOptions? Decision => null;

    }
}
