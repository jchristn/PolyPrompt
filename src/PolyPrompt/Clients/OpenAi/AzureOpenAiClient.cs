namespace PolyPrompt.Clients
{
    using System.Net.Http.Headers;
    using PolyPrompt.Auth;
    using PolyPrompt.Models;
    using PolyPrompt.Options;

    /// <summary>
    /// Azure OpenAI client for the v1 API (<c>https://{resource}.openai.azure.com/openai/v1/</c>), which needs no
    /// <c>api-version</c>. Chat, tool chat, and generation use the Responses API with the deployment name sent as
    /// <c>model</c>; request options and behavior match <see cref="OpenAiClient"/>.
    /// <list type="bullet">
    /// <item><description>Supported: Chat, ToolChat, and Generate (<c>/openai/v1/responses</c>), Embed
    /// (<c>/openai/v1/embeddings</c>), and Models (<c>/openai/v1/models</c>), plus the native operations on
    /// <see cref="ResponsesApiClientBase"/> and <see cref="CancelResponseAsync"/>.</description></item>
    /// <item><description>Not supported (throw <see cref="CapabilityNotSupportedException"/>): SparseEmbed, Rerank,
    /// Classify, Decide.</description></item>
    /// <item><description>Auth: an <c>api-key</c> header, or an Entra ID bearer token (scope
    /// <c>https://ai.azure.com/.default</c>, role Cognitive Services OpenAI User), refreshed per request.</description></item>
    /// <item><description>Defaults: none. Set <c>Defaults.Completion.Model</c> and <c>Defaults.Embedding.Model</c> to your
    /// deployment names.</description></item>
    /// </list>
    /// Confirmed hosted tools on Azure: code interpreter, remote MCP, image generation, and computer use (via the
    /// computer-use-preview model). PolyPrompt does not restrict tools client-side; the service rejects unsupported ones.
    /// For Chat Completions on Azure, use <see cref="OpenAiCompatibleClient"/> with endpoint
    /// <c>https://{resource}.openai.azure.com/openai/v1</c> and <c>AuthPlacement.Header("api-key")</c>.
    /// </summary>
    /// <seealso href="https://github.com/jchristn/Polyprompt#capability-matrix">Capability matrix</seealso>
    public class AzureOpenAiClient : ResponsesApiClientBase
    {
        #region Private-Members

        private static readonly ResponsesApiDialect _Dialect = OpenAiResponsesFields.Dialect("Azure OpenAI",
            "Check that the endpoint is an Azure OpenAI resource and that the v1 API is available for it.");

        private readonly ICredentialProvider? _Credential;
        private readonly string _ApiRoot;

        #endregion

        #region Public-Members

        /// <summary>
        /// Operations this client supports: Chat, ToolChat, Generate, Embed, and Models.
        /// </summary>
        public override Capability Capabilities => Capability.Chat | Capability.ToolChat | Capability.Generate | Capability.Embed | Capability.Models;

        /// <inheritdoc />
        public override string ProviderName => "Azure OpenAI";

        /// <summary>
        /// Client-wide defaults: <see cref="AzureOpenAiDefaults.Completion"/> and <see cref="AzureOpenAiDefaults.Embedding"/>.
        /// Set each section's <c>Model</c> to a deployment name.
        /// </summary>
        public override AzureOpenAiDefaults Defaults { get; } = new AzureOpenAiDefaults();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize an Azure OpenAI client authenticated with an API key (sent as the <c>api-key</c> header).
        /// </summary>
        /// <param name="endpoint">Resource endpoint, for example <c>https://my-resource.openai.azure.com</c>. <c>/openai/v1</c> is
        /// appended unless present; <c>https://{resource}.services.ai.azure.com</c> also works.</param>
        /// <param name="apiKey">Required Azure OpenAI API key.</param>
        /// <param name="options">Optional construction settings.</param>
        /// <exception cref="ArgumentNullException">Thrown when endpoint or apiKey is null.</exception>
        /// <exception cref="ArgumentException">Thrown when apiKey is blank, contains whitespace, or looks like a URL, or endpoint is not an absolute http(s) URL.</exception>
        public AzureOpenAiClient(string endpoint, string apiKey, ClientOptions? options = null)
            : base(ClientArguments.Endpoint(endpoint, nameof(endpoint)), ClientArguments.RequiredKey(apiKey, nameof(apiKey), null), options)
        {
            _ApiRoot = ResolveApiRoot(_Endpoint);
            _Header = "[AzureOpenAI] ";
        }

        /// <summary>
        /// Initialize an Azure OpenAI client authenticated with Entra ID: a bearer token from <paramref name="credential"/>
        /// (scope <c>https://ai.azure.com/.default</c>), refreshed per request.
        /// </summary>
        /// <param name="endpoint">Resource endpoint, for example <c>https://my-resource.openai.azure.com</c>.</param>
        /// <param name="credential">Credential provider supplying bearer tokens.</param>
        /// <param name="options">Optional construction settings.</param>
        /// <exception cref="ArgumentNullException">Thrown when endpoint or credential is null.</exception>
        /// <exception cref="ArgumentException">Thrown when endpoint is not an absolute http(s) URL.</exception>
        public AzureOpenAiClient(string endpoint, ICredentialProvider credential, ClientOptions? options = null)
            : base(ClientArguments.Endpoint(endpoint, nameof(endpoint)), null, options)
        {
            _Credential = credential ?? throw new ArgumentNullException(nameof(credential));
            _ApiRoot = ResolveApiRoot(_Endpoint);
            _Header = "[AzureOpenAI] ";
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Probe <c>/openai/v1/models</c>. See <see cref="HttpClientBase.ValidateConnectivityAsync"/>.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the resource accepted the credentials.</returns>
        public override Task<bool> ValidateConnectivityAsync(CancellationToken token = default) => ProbeAsync(Url("models"), token);

        /// <summary>
        /// Cancel a background response (<c>POST /openai/v1/responses/{id}/cancel</c>).
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

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        private protected override ResponsesApiDialect Dialect => _Dialect;

        /// <inheritdoc />
        protected override string? ModelCatalogUrl => "https://learn.microsoft.com/en-us/azure/ai-foundry/openai/concepts/models";

        /// <inheritdoc />
        protected override string Url(string path) => _ApiRoot + "/" + path.TrimStart('/');

        /// <inheritdoc />
        protected override async Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
        {
            if (_Credential != null)
            {
                string bearer = await _Credential.GetBearerTokenAsync(token).ConfigureAwait(false);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            }
            else if (!string.IsNullOrEmpty(_ApiKey))
            {
                request.Headers.Remove("api-key");
                request.Headers.TryAddWithoutValidation("api-key", _ApiKey);
            }
        }

        /// <inheritdoc />
        private protected override void AddProviderFields(Dictionary<string, object> body, ResolvedCompletion settings, bool stream, ResponsesApiFieldStage stage)
        {
            if (stage == ResponsesApiFieldStage.AfterSafetyIdentifier) OpenAiResponsesFields.Add(body, settings.Call, Defaults.Completion, stream);
        }

        /// <inheritdoc />
        private protected override string? HttpErrorHint(int statusCode, string url, string? body)
        {
            if (body != null && body.Contains("invalid_encrypted_content", StringComparison.Ordinal))
                return "Azure rejected replayed encrypted reasoning because items were altered or reordered. Check proxies between the client and Azure (for example Azure API Management), which can rewrite request bodies.";
            return base.HttpErrorHint(statusCode, url, body);
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

        #region Private-Methods

        private static string ResolveApiRoot(string endpoint)
        {
            string trimmed = endpoint.TrimEnd('/');
            if (trimmed.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase)) return trimmed;
            if (trimmed.EndsWith("/openai", StringComparison.OrdinalIgnoreCase)) return trimmed + "/v1";
            return trimmed + "/openai/v1";
        }

        #endregion
    }

    /// <summary>
    /// Defaults for <see cref="AzureOpenAiClient"/>.
    /// </summary>
    public sealed class AzureOpenAiDefaults : ClientDefaults
    {
        /// <summary>
        /// Completion defaults (Responses API). No default model: set <c>Model</c> to your deployment name. Store = false.
        /// </summary>
        public override OpenAiCompletionOptions Completion { get; } = new OpenAiCompletionOptions { Store = false };

        /// <summary>
        /// Embedding defaults. No default model: set <c>Model</c> to your embedding deployment name.
        /// </summary>
        public override OpenAiEmbeddingOptions Embedding { get; } = new OpenAiEmbeddingOptions();

        /// <summary>Not supported by Azure OpenAI; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Azure OpenAI does not support sparse embeddings.", true)]
        public new SparseEmbeddingOptions? SparseEmbedding => null;

        /// <summary>Not supported by Azure OpenAI; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Azure OpenAI does not support reranking.", true)]
        public new RerankOptions? Rerank => null;

        /// <summary>Not supported by Azure OpenAI; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Azure OpenAI does not support classification.", true)]
        public new ClassificationOptions? Classification => null;

        /// <summary>Not supported by Azure OpenAI; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Azure OpenAI does not support decisions.", true)]
        public new DecisionOptions? Decision => null;

    }
}
