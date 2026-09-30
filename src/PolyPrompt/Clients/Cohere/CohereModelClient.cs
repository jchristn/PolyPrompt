namespace PolyPrompt.Clients
{
    using System.Runtime.CompilerServices;
    using PolyPrompt.Models;
    using SyslogLogging;

    /// <summary>
    /// Cohere model client using <c>/v1/models</c>, following <c>next_page_token</c> pagination. Each model's
    /// <see cref="ModelInformation.Metadata"/> carries <c>endpoints</c> (comma-separated, for example "chat,embed"),
    /// <c>finetuned</c>, and <c>features</c> when reported, and <see cref="ModelInformation.InputTokenLimit"/> carries the
    /// context length.
    /// </summary>
    public class CohereModelClient : ModelClientBase
    {
        #region Private-Members

        private int _PageSize = 1000;

        #endregion

        #region Public-Members

        /// <summary>
        /// Page size requested from the models endpoint. Clamped to 1..1,000. Listing follows pagination, so this affects
        /// the number of requests, not the results. Default: 1,000.
        /// </summary>
        public int PageSize
        {
            get { return _PageSize; }
            set { _PageSize = Math.Clamp(value, 1, 1000); }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new Cohere model client.
        /// </summary>
        /// <param name="endpoint">Cohere API endpoint URL. Default: https://api.cohere.com.</param>
        /// <param name="apiKey">Cohere API key, sent as a bearer token. Default: null.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        /// <param name="httpClient">Optional HTTP client; see <see cref="ClientBase"/>. Default: null.</param>
        public CohereModelClient(
            string endpoint = CohereProtocol.DefaultEndpoint,
            string? apiKey = null,
            LoggingModule? logging = null,
            HttpClient? httpClient = null)
            : base(endpoint, apiKey, logging ?? new LoggingModule(), httpClient)
        {
            _Header = CohereProtocol.Header;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override Task<bool> ValidateConnectivityAsync(CancellationToken token = default)
        {
            return ProbeAsync(BuildUrl(CohereProtocol.ProbePath), token);
        }

        /// <inheritdoc />
        public override async IAsyncEnumerable<ModelInformation> ListModelsAsync([EnumeratorCancellation] CancellationToken token = default)
        {
            string? pageToken = null;
            HashSet<string> seenTokens = new HashSet<string>(StringComparer.Ordinal);

            while (true)
            {
                token.ThrowIfCancellationRequested();

                string url = BuildUrl("/v1/models?page_size=" + _PageSize);
                if (!string.IsNullOrEmpty(pageToken)) url += "&page_token=" + Uri.EscapeDataString(pageToken);
                _Logging.Debug(_Header + "GET " + url);

                HttpCallResult result = await GetAndRecordAsync(url, token).ConfigureAwait(false);
                if (!result.IsSuccessStatusCode)
                {
                    _Logging.Warn(_Header + "list models failed with status " + result.StatusCode);
                    yield break;
                }

                Dictionary<string, object>? responseObj = TryDeserializeObject(result.ResponseBody);
                List<Dictionary<string, object>>? models = responseObj == null ? null : ParseNestedList(responseObj, "models");
                if (models == null) yield break;

                foreach (Dictionary<string, object> modelObj in models)
                {
                    token.ThrowIfCancellationRequested();
                    yield return ParseModel(modelObj, string.Empty);
                }

                pageToken = responseObj!.ContainsKey("next_page_token") ? responseObj["next_page_token"]?.ToString() : null;

                // Stop when there is no next page, or defensively when a token repeats.
                if (string.IsNullOrEmpty(pageToken) || !seenTokens.Add(pageToken)) yield break;
            }
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
        {
            CohereProtocol.ApplyAuth(request, _ApiKey);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override async Task<ModelInformation?> GetModelInformationCoreAsync(string model, CancellationToken token)
        {
            string url = BuildUrl("/v1/models/" + Uri.EscapeDataString(model));
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

        private ModelInformation ParseModel(Dictionary<string, object> modelObj, string fallbackName)
        {
            ModelInformation info = new ModelInformation();
            info.Name = modelObj.ContainsKey("name") ? modelObj["name"]?.ToString() ?? fallbackName : fallbackName;
            info.OwnedBy = "cohere";
            info.InputTokenLimit = TryGetRoundedInt(modelObj, "context_length");

            string? endpoints = JoinStringArray(modelObj, "endpoints");
            if (endpoints != null) info.Metadata["endpoints"] = endpoints;

            string? features = JoinStringArray(modelObj, "features");
            if (features != null) info.Metadata["features"] = features;

            if (modelObj.ContainsKey("finetuned") && modelObj["finetuned"] != null)
                info.Metadata["finetuned"] = IsTruthy(modelObj, "finetuned") ? "true" : "false";

            return info;
        }

        private string? JoinStringArray(Dictionary<string, object> obj, string key)
        {
            if (!obj.ContainsKey(key) || obj[key] == null) return null;
            List<string>? values;
            try
            {
                values = _Serializer.DeserializeJson<List<string>>(_Serializer.SerializeJson(obj[key], false));
            }
            catch (System.Text.Json.JsonException)
            {
                return null;
            }
            return values == null || values.Count == 0 ? null : string.Join(",", values);
        }

        #endregion
    }
}
