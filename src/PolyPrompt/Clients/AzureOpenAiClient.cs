namespace PolyPrompt.Clients
{
    using System;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using PolyPrompt.Auth;
    using SyslogLogging;

    /// <summary>
    /// Client for Azure OpenAI. Azure OpenAI is wire-compatible with OpenAI for request and response bodies,
    /// tool-call delta assembly, reasoning (<c>reasoning_effort</c>), streaming SSE, and usage — all of which
    /// are inherited unchanged from <see cref="OpenAiClient"/>. It differs in three ways, which is all this
    /// class overrides:
    /// <list type="bullet">
    /// <item><description><b>Routing:</b> operations live under
    /// <c>/openai/deployments/{deployment}/{op}</c> with a required <c>api-version</c> query parameter, and
    /// the model is the <b>deployment name</b> (set via <see cref="CompletionClientBase.Model"/>).</description></item>
    /// <item><description><b>Auth:</b> either an <c>api-key</c> header or an Azure AD bearer token (supplied
    /// through an <see cref="ICredentialProvider"/> and refreshed per request).</description></item>
    /// <item><description><b>Api version:</b> a settable <see cref="ApiVersion"/> with a current GA default.</description></item>
    /// </list>
    /// </summary>
    public class AzureOpenAiClient : OpenAiClient
    {
        #region Private-Members

        private string _ApiVersion = DefaultApiVersion;
        private readonly ICredentialProvider? _Credential;

        #endregion

        #region Public-Members

        /// <summary>A current generally-available Azure OpenAI API version, used when none is specified.</summary>
        public const string DefaultApiVersion = "2024-10-21";

        /// <summary>
        /// The Azure OpenAI <c>api-version</c> query value sent with every request. These values drift over
        /// time, so it is user-settable; defaults to <see cref="DefaultApiVersion"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or empty.</exception>
        public string ApiVersion
        {
            get { return _ApiVersion; }
            set
            {
                if (string.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(ApiVersion));
                _ApiVersion = value;
            }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize an Azure OpenAI client authenticated with an <c>api-key</c>.
        /// </summary>
        /// <param name="endpoint">Azure resource endpoint, e.g. <c>https://my-resource.openai.azure.com</c>.</param>
        /// <param name="deployment">Deployment name; becomes <see cref="CompletionClientBase.Model"/> and routes the URL.</param>
        /// <param name="apiKey">Azure OpenAI API key, sent as the <c>api-key</c> header.</param>
        /// <param name="apiVersion">API version; defaults to <see cref="DefaultApiVersion"/>.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        /// <param name="httpClient">Optional HTTP client (caller-owned when supplied).</param>
        /// <exception cref="ArgumentNullException">Thrown when endpoint or deployment is null or empty.</exception>
        public AzureOpenAiClient(
            string endpoint,
            string deployment,
            string apiKey,
            string? apiVersion = null,
            LoggingModule? logging = null,
            HttpClient? httpClient = null)
            : base(endpoint, apiKey, logging, httpClient)
        {
            if (string.IsNullOrWhiteSpace(deployment)) throw new ArgumentNullException(nameof(deployment));

            _Header = "[AzureOpenAI] ";
            Model = deployment;
            if (!string.IsNullOrEmpty(apiVersion)) _ApiVersion = apiVersion;

            // The base ctor installs an "Authorization: Bearer {key}" header; Azure OpenAI uses "api-key"
            // instead. Swap it so both the header name and scheme are correct.
            if (_HttpClient.DefaultRequestHeaders.Contains("Authorization"))
                _HttpClient.DefaultRequestHeaders.Remove("Authorization");
            if (!string.IsNullOrEmpty(apiKey))
                _HttpClient.DefaultRequestHeaders.Add("api-key", apiKey);
        }

        /// <summary>
        /// Initialize an Azure OpenAI client authenticated with an Azure AD bearer token, refreshed per
        /// request through the supplied credential provider.
        /// </summary>
        /// <param name="endpoint">Azure resource endpoint, e.g. <c>https://my-resource.openai.azure.com</c>.</param>
        /// <param name="deployment">Deployment name; becomes <see cref="CompletionClientBase.Model"/> and routes the URL.</param>
        /// <param name="credential">Azure AD credential provider supplying bearer tokens.</param>
        /// <param name="apiVersion">API version; defaults to <see cref="DefaultApiVersion"/>.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        /// <param name="httpClient">Optional HTTP client (caller-owned when supplied).</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null or empty.</exception>
        public AzureOpenAiClient(
            string endpoint,
            string deployment,
            ICredentialProvider credential,
            string? apiVersion = null,
            LoggingModule? logging = null,
            HttpClient? httpClient = null)
            : base(endpoint, apiKey: null, logging, httpClient)
        {
            if (string.IsNullOrWhiteSpace(deployment)) throw new ArgumentNullException(nameof(deployment));

            _Header = "[AzureOpenAI] ";
            Model = deployment;
            if (!string.IsNullOrEmpty(apiVersion)) _ApiVersion = apiVersion;
            _Credential = credential ?? throw new ArgumentNullException(nameof(credential));
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string BuildApiUrl(string path)
        {
            string endpoint = _Endpoint.TrimEnd('/');
            string normalizedPath = path.TrimStart('/');

            // Model management is resource-scoped; every inference operation is deployment-scoped.
            string resourcePath;
            if (normalizedPath.Equals("models", StringComparison.OrdinalIgnoreCase)
                || normalizedPath.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
            {
                resourcePath = endpoint + "/openai/" + normalizedPath;
            }
            else
            {
                resourcePath = endpoint + "/openai/deployments/" + Uri.EscapeDataString(Model) + "/" + normalizedPath;
            }

            return resourcePath + "?api-version=" + Uri.EscapeDataString(_ApiVersion);
        }

        /// <inheritdoc />
        protected override async Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
        {
            // API-key auth lives in a static default header; only AAD needs a fresh per-request token.
            if (_Credential == null) return;

            string bearer = await _Credential.GetBearerTokenAsync(token).ConfigureAwait(false);
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bearer);
        }

        #endregion
    }
}
