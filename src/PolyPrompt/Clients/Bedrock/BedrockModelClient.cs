namespace PolyPrompt.Clients
{
    using System.Runtime.CompilerServices;
    using PolyPrompt.Auth;
    using PolyPrompt.Models;
    using SyslogLogging;

    /// <summary>
    /// AWS Bedrock model client using the control-plane <c>/foundation-models</c> API. Requests are signed with AWS
    /// Signature Version 4.
    /// </summary>
    public class BedrockModelClient : ModelClientBase
    {
        #region Private-Members

        private readonly IAwsCredentialProvider _CredentialProvider;
        private readonly string _Region;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new Bedrock model client.
        /// </summary>
        /// <param name="credentialProvider">Resolves AWS credentials to sign each request.</param>
        /// <param name="region">AWS region, for example <c>us-east-1</c>.</param>
        /// <param name="endpoint">Optional endpoint override. Default: <c>https://bedrock.{region}.amazonaws.com</c> (the control plane).</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        /// <param name="httpClient">Optional HTTP client; see <see cref="ClientBase"/>. Default: null.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null or empty.</exception>
        public BedrockModelClient(
            IAwsCredentialProvider credentialProvider,
            string region,
            string? endpoint = null,
            LoggingModule? logging = null,
            HttpClient? httpClient = null)
            : base(BedrockProtocol.ResolveControlPlaneEndpoint(endpoint, region), null, logging ?? new LoggingModule(), httpClient)
        {
            _CredentialProvider = credentialProvider ?? throw new ArgumentNullException(nameof(credentialProvider));
            _Region = region;
            _Header = BedrockProtocol.Header;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override Task<bool> ValidateConnectivityAsync(CancellationToken token = default)
        {
            return ProbeAsync(BuildUrl("/foundation-models"), token);
        }

        /// <inheritdoc />
        public override async IAsyncEnumerable<ModelInformation> ListModelsAsync([EnumeratorCancellation] CancellationToken token = default)
        {
            string url = BuildUrl("/foundation-models");
            _Logging.Debug(_Header + "GET " + url);

            HttpCallResult result = await GetAndRecordAsync(url, token).ConfigureAwait(false);
            if (!result.IsSuccessStatusCode)
            {
                _Logging.Warn(_Header + "list models failed with status " + result.StatusCode);
                yield break;
            }

            Dictionary<string, object>? responseObj = TryDeserializeObject(result.ResponseBody);
            List<Dictionary<string, object>>? summaries = responseObj == null ? null : ParseNestedList(responseObj, "modelSummaries");
            if (summaries == null) yield break;

            foreach (Dictionary<string, object> summary in summaries)
            {
                token.ThrowIfCancellationRequested();
                yield return ParseModel(summary, string.Empty);
            }
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
        {
            BedrockProtocol.Sign(request, body, _CredentialProvider, _Region);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override async Task<ModelInformation?> GetModelInformationCoreAsync(string model, CancellationToken token)
        {
            string url = BuildUrl("/foundation-models/" + Uri.EscapeDataString(model));
            _Logging.Debug(_Header + "GET " + url);

            try
            {
                HttpCallResult result = await GetAndRecordAsync(url, token).ConfigureAwait(false);
                if (!result.IsSuccessStatusCode)
                {
                    _Logging.Warn(_Header + "get model failed with status " + result.StatusCode + ": " + result.ResponseBody);
                    return null;
                }

                Dictionary<string, object>? responseObj = TryDeserializeObject(result.ResponseBody);
                if (responseObj == null) return null;

                return ParseModel(ParseNestedObject(responseObj, "modelDetails") ?? responseObj, model);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "get model failed: " + ex.Message);
                return null;
            }
        }

        #endregion

        #region Private-Methods

        private static ModelInformation ParseModel(Dictionary<string, object> details, string fallbackName)
        {
            ModelInformation info = new ModelInformation();
            info.Name = details.ContainsKey("modelId") ? details["modelId"]?.ToString() ?? fallbackName : fallbackName;
            info.DisplayName = details.ContainsKey("modelName") ? details["modelName"]?.ToString() : null;
            info.OwnedBy = details.ContainsKey("providerName") ? details["providerName"]?.ToString() : null;
            return info;
        }

        #endregion
    }
}
