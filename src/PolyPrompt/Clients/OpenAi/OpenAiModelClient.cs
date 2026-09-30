namespace PolyPrompt.Clients
{
    using System.Runtime.CompilerServices;
    using PolyPrompt.Models;
    using SyslogLogging;

    /// <summary>
    /// OpenAI model client using <c>/v1/models</c>. Works with OpenAI-compatible servers that implement it.
    /// </summary>
    public class OpenAiModelClient : ModelClientBase
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new OpenAI model client.
        /// </summary>
        /// <param name="endpoint">API endpoint URL; <c>/v1</c> is appended unless present. Default: https://api.openai.com.</param>
        /// <param name="apiKey">API key, sent as a bearer token. Default: null.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        /// <param name="httpClient">Optional HTTP client; see <see cref="ClientBase"/>. Default: null.</param>
        public OpenAiModelClient(
            string endpoint = OpenAiProtocol.DefaultEndpoint,
            string? apiKey = null,
            LoggingModule? logging = null,
            HttpClient? httpClient = null)
            : base(endpoint, apiKey, logging ?? new LoggingModule(), httpClient)
        {
            _Header = OpenAiProtocol.Header;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override Task<bool> ValidateConnectivityAsync(CancellationToken token = default)
        {
            return ProbeAsync(BuildApiUrl("models"), token);
        }

        /// <inheritdoc />
        public override async IAsyncEnumerable<ModelInformation> ListModelsAsync([EnumeratorCancellation] CancellationToken token = default)
        {
            string url = BuildApiUrl("models");
            _Logging.Debug(_Header + "GET " + StripQuery(url));

            HttpCallResult result = await GetAndRecordAsync(url, token).ConfigureAwait(false);
            if (!result.IsSuccessStatusCode)
            {
                _Logging.Warn(_Header + "list models failed with status " + result.StatusCode);
                yield break;
            }

            Dictionary<string, object>? responseObj = TryDeserializeObject(result.ResponseBody);
            List<Dictionary<string, object>>? data = responseObj == null ? null : ParseNestedList(responseObj, "data");
            if (data == null) yield break;

            foreach (Dictionary<string, object> modelObj in data)
            {
                token.ThrowIfCancellationRequested();
                yield return ParseModel(modelObj, string.Empty);
            }
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Build the URL for a models path. Azure OpenAI overrides this.
        /// </summary>
        /// <param name="path">Path, for example <c>models</c> or <c>models/{id}</c>.</param>
        /// <returns>The absolute URL.</returns>
        protected virtual string BuildApiUrl(string path)
        {
            return OpenAiProtocol.BuildApiUrl(_Endpoint, path);
        }

        /// <inheritdoc />
        protected override Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
        {
            OpenAiProtocol.ApplyAuth(request, _ApiKey);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override async Task<ModelInformation?> GetModelInformationCoreAsync(string model, CancellationToken token)
        {
            string url = BuildApiUrl("models/" + Uri.EscapeDataString(model));
            _Logging.Debug(_Header + "GET " + StripQuery(url));

            try
            {
                HttpCallResult result = await GetAndRecordAsync(url, token).ConfigureAwait(false);
                if (!result.IsSuccessStatusCode)
                {
                    _Logging.Warn(_Header + "get model failed with status " + result.StatusCode + ": " + result.ResponseBody);
                    return null;
                }

                Dictionary<string, object>? responseObj = TryDeserializeObject(result.ResponseBody);
                return responseObj == null ? null : ParseModel(responseObj, model);
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

        private static ModelInformation ParseModel(Dictionary<string, object> modelObj, string fallbackName)
        {
            ModelInformation info = new ModelInformation();
            info.Name = modelObj.ContainsKey("id") ? modelObj["id"]?.ToString() ?? fallbackName : fallbackName;
            info.OwnedBy = modelObj.ContainsKey("owned_by") ? modelObj["owned_by"]?.ToString() : null;

            long? created = TryGetLong(modelObj, "created");
            if (created.HasValue) info.CreatedUtc = DateTimeOffset.FromUnixTimeSeconds(created.Value).UtcDateTime;

            if (modelObj.ContainsKey("object")) info.Metadata["object"] = modelObj["object"]?.ToString();
            return info;
        }

        #endregion
    }
}
