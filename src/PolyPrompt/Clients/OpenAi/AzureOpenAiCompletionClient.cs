namespace PolyPrompt.Clients
{
    using PolyPrompt.Auth;
    using SyslogLogging;

    /// <summary>
    /// Azure OpenAI completion client. Request and response bodies, streaming, tool calls, and reasoning are the same as
    /// <see cref="OpenAiCompletionClient"/>. Azure differs in routing (operations live under
    /// <c>/openai/deployments/{deployment}/...</c> with an <c>api-version</c> query parameter, and the model is the
    /// deployment name) and in authentication (an <c>api-key</c> header, or an Azure AD bearer token refreshed per request).
    /// </summary>
    public class AzureOpenAiCompletionClient : OpenAiCompletionClient
    {
        #region Private-Members

        private string _ApiVersion = AzureOpenAiDefaults.ApiVersion;
        private readonly ICredentialProvider? _Credential;

        #endregion

        #region Public-Members

        /// <summary>
        /// The Azure OpenAI <c>api-version</c> query value sent with every request. Default: <see cref="AzureOpenAiDefaults.ApiVersion"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null, empty, or whitespace.</exception>
        public string ApiVersion
        {
            get { return _ApiVersion; }
            set { _ApiVersion = AzureOpenAiProtocol.ValidateApiVersion(value, nameof(ApiVersion)); }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize an Azure OpenAI completion client authenticated with an <c>api-key</c>.
        /// </summary>
        /// <param name="endpoint">Azure resource endpoint, for example <c>https://my-resource.openai.azure.com</c>.</param>
        /// <param name="deployment">Deployment name; becomes <see cref="CompletionClientBase.Model"/>. A per-call model selects another deployment.</param>
        /// <param name="apiKey">Azure OpenAI API key, sent as the <c>api-key</c> header.</param>
        /// <param name="apiVersion">API version. Default: <see cref="AzureOpenAiDefaults.ApiVersion"/>.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        /// <param name="httpClient">Optional HTTP client; see <see cref="ClientBase"/>. Default: null.</param>
        /// <exception cref="ArgumentNullException">Thrown when endpoint or deployment is null or empty.</exception>
        public AzureOpenAiCompletionClient(
            string endpoint,
            string deployment,
            string apiKey,
            string? apiVersion = null,
            LoggingModule? logging = null,
            HttpClient? httpClient = null)
            : base(endpoint, apiKey, logging, httpClient)
        {
            if (string.IsNullOrWhiteSpace(deployment)) throw new ArgumentNullException(nameof(deployment));
            _Header = AzureOpenAiProtocol.Header;
            Model = deployment;
            if (!string.IsNullOrEmpty(apiVersion)) _ApiVersion = apiVersion;
        }

        /// <summary>
        /// Initialize an Azure OpenAI completion client authenticated with an Azure AD bearer token, refreshed per request.
        /// </summary>
        /// <param name="endpoint">Azure resource endpoint, for example <c>https://my-resource.openai.azure.com</c>.</param>
        /// <param name="deployment">Deployment name; becomes <see cref="CompletionClientBase.Model"/>.</param>
        /// <param name="credential">Azure AD credential provider supplying bearer tokens.</param>
        /// <param name="apiVersion">API version. Default: <see cref="AzureOpenAiDefaults.ApiVersion"/>.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        /// <param name="httpClient">Optional HTTP client; see <see cref="ClientBase"/>. Default: null.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null or empty.</exception>
        public AzureOpenAiCompletionClient(
            string endpoint,
            string deployment,
            ICredentialProvider credential,
            string? apiVersion = null,
            LoggingModule? logging = null,
            HttpClient? httpClient = null)
            : base(endpoint, apiKey: null, logging, httpClient)
        {
            if (string.IsNullOrWhiteSpace(deployment)) throw new ArgumentNullException(nameof(deployment));
            _Credential = credential ?? throw new ArgumentNullException(nameof(credential));
            _Header = AzureOpenAiProtocol.Header;
            Model = deployment;
            if (!string.IsNullOrEmpty(apiVersion)) _ApiVersion = apiVersion;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string BuildApiUrl(string path, string? model)
        {
            return AzureOpenAiProtocol.BuildApiUrl(_Endpoint, path, model, _ApiVersion);
        }

        /// <inheritdoc />
        protected override Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
        {
            return AzureOpenAiProtocol.ApplyAuthAsync(request, _ApiKey, _Credential, token);
        }

        #endregion
    }
}
