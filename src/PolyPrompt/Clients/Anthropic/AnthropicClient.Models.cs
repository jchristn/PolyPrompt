namespace PolyPrompt.Clients
{
    using System.Globalization;
    using System.Runtime.CompilerServices;
    using PolyPrompt.Models;
    using SyslogLogging;

    public partial class AnthropicClient
    {
        #region Private-Members

        private int _PageSize = 1000;

        #endregion

        #region Public-Members

        /// <summary>
        /// Page size requested by <see cref="ProviderClientBase.ListModelsAsync"/>. Clamped to 1..1,000 (the API maximum). Listing follows pagination,
        /// so this affects the number of requests, not the results. Default: 1,000.
        /// </summary>
        public int PageSize
        {
            get { return _PageSize; }
            set { _PageSize = Math.Clamp(value, 1, 1000); }
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        protected override async IAsyncEnumerable<ModelInformation> ListModelsCoreAsync([EnumeratorCancellation] CancellationToken token)
        {
            string? afterId = null;
            bool hasMore = true;

            while (hasMore)
            {
                token.ThrowIfCancellationRequested();

                string url = BuildUrl("/v1/models?limit=" + _PageSize);
                if (!string.IsNullOrEmpty(afterId)) url += "&after_id=" + Uri.EscapeDataString(afterId);
                _Logging.Debug(_Header + "GET " + url);

                HttpCallResult result = await GetAndRecordAsync(url, token).ConfigureAwait(false);
                if (!result.IsSuccessStatusCode)
                {
                    _Logging.Warn(_Header + "list models failed with status " + result.StatusCode);
                    yield break;
                }

                Dictionary<string, object>? responseObj = TryDeserializeObject(result.ResponseBody);
                List<Dictionary<string, object>>? data = responseObj == null ? null : ParseNestedList(responseObj, "data");
                if (data == null || data.Count == 0) yield break;

                foreach (Dictionary<string, object> modelObj in data)
                {
                    token.ThrowIfCancellationRequested();
                    yield return ParseModel(modelObj, string.Empty);
                }

                hasMore = IsTruthy(responseObj!, "has_more");
                afterId = responseObj!.ContainsKey("last_id") ? responseObj["last_id"]?.ToString() : null;
                if (string.IsNullOrEmpty(afterId)) hasMore = false;
            }
        }

        #endregion

        #region Protected-Methods

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

        private static ModelInformation ParseModel(Dictionary<string, object> modelObj, string fallbackName)
        {
            ModelInformation info = new ModelInformation();
            info.Name = modelObj.ContainsKey("id") ? modelObj["id"]?.ToString() ?? fallbackName : fallbackName;
            info.DisplayName = modelObj.ContainsKey("display_name") ? modelObj["display_name"]?.ToString() : null;

            string? created = modelObj.ContainsKey("created_at") ? modelObj["created_at"]?.ToString() : null;
            if (!string.IsNullOrEmpty(created)
                && DateTime.TryParse(created, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsed))
            {
                info.CreatedUtc = parsed;
            }

            if (modelObj.ContainsKey("type")) info.Metadata["type"] = modelObj["type"]?.ToString();
            return info;
        }

        #endregion
    }
}
