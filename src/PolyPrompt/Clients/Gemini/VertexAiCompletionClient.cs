namespace PolyPrompt.Clients
{
    using System.Net.Http.Headers;
    using PolyPrompt.Auth;
    using PolyPrompt.Models;
    using PolyPrompt.Telemetry;
    using SyslogLogging;

    /// <summary>
    /// Google Vertex AI completion client for Gemini models. Vertex serves the same <c>generateContent</c> schema as
    /// <see cref="GeminiCompletionClient"/>, so request building, response and stream parsing, tool calling, and reasoning
    /// are inherited. Vertex differs in routing (a project, region, and publisher path) and authentication (an OAuth bearer
    /// token from ADC or a service account, refreshed per request).
    /// </summary>
    public class VertexAiCompletionClient : GeminiCompletionClient
    {
        #region Private-Members

        private readonly string _Project;
        private readonly string _Region;
        private readonly ICredentialProvider _Credential;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new Vertex AI completion client.
        /// </summary>
        /// <param name="project">Google Cloud project id.</param>
        /// <param name="region">Vertex region, for example <c>us-central1</c>. Also selects the regional endpoint host.</param>
        /// <param name="credential">Credential provider supplying OAuth bearer tokens (ADC or a service account).</param>
        /// <param name="endpoint">Optional endpoint override. Default: <c>https://{region}-aiplatform.googleapis.com</c>.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        /// <param name="httpClient">Optional HTTP client; see <see cref="ClientBase"/>. Default: null.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null or empty.</exception>
        public VertexAiCompletionClient(
            string project,
            string region,
            ICredentialProvider credential,
            string? endpoint = null,
            LoggingModule? logging = null,
            HttpClient? httpClient = null)
            : base(GeminiProtocol.ResolveVertexEndpoint(endpoint, region), apiKey: null, logging, httpClient)
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
        /// with the client's model (<c>:countTokens</c>, which is not billed). Returns false on an HTTP error, an
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

                    HttpCallResult result = await PostJsonAsync(BuildModelUrl(Model ?? "gemini-2.5-flash") + ":countTokens", body, token).ConfigureAwait(false);
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
}
