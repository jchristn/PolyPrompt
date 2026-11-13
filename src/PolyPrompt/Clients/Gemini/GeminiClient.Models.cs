namespace PolyPrompt.Clients
{
    using System.Runtime.CompilerServices;
    using PolyPrompt.Models;
    using SyslogLogging;

    public partial class GeminiClient
    {

        #region Public-Methods

        /// <inheritdoc />
        protected override async IAsyncEnumerable<ModelInformation> ListModelsCoreAsync([EnumeratorCancellation] CancellationToken token)
        {
            string? pageToken = null;
            do
            {
                string url = _Endpoint.TrimEnd('/') + "/v1beta/models?pageSize=1000"
                    + (string.IsNullOrEmpty(pageToken) ? string.Empty : "&pageToken=" + Uri.EscapeDataString(pageToken));
                _Logging.Debug(_Header + "GET " + StripQuery(url));

                HttpCallResult result = await GetAndRecordAsync(url, token).ConfigureAwait(false);
                if (!result.IsSuccessStatusCode)
                {
                    _Logging.Warn(_Header + "list models failed with status " + result.StatusCode);
                    yield break;
                }

                Dictionary<string, object>? responseObj = TryDeserializeObject(result.ResponseBody);
                if (responseObj == null) yield break;

                List<Dictionary<string, object>>? models = ParseNestedList(responseObj, "models");
                if (models != null)
                {
                    foreach (Dictionary<string, object> modelObj in models)
                    {
                        token.ThrowIfCancellationRequested();
                        yield return ParseModel(modelObj, string.Empty);
                    }
                }

                pageToken = responseObj.ContainsKey("nextPageToken") ? responseObj["nextPageToken"]?.ToString() : null;
            }
            while (!string.IsNullOrEmpty(pageToken));
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ModelInformation?> GetModelInformationCoreAsync(string model, CancellationToken token)
        {
            string url = GeminiProtocol.ModelUrl(_Endpoint, model);
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

            string rawName = modelObj.ContainsKey("name") ? modelObj["name"]?.ToString() ?? fallbackName : fallbackName;
            info.Name = rawName.StartsWith("models/", StringComparison.Ordinal) ? rawName.Substring(7) : rawName;
            info.DisplayName = modelObj.ContainsKey("displayName") ? modelObj["displayName"]?.ToString() : null;
            info.Description = modelObj.ContainsKey("description") ? modelObj["description"]?.ToString() : null;
            info.InputTokenLimit = TryGetInt(modelObj, "inputTokenLimit");
            info.OutputTokenLimit = TryGetInt(modelObj, "outputTokenLimit");

            if (modelObj.ContainsKey("supportedGenerationMethods"))
                info.Metadata["supportedGenerationMethods"] = _Serializer.SerializeJson(modelObj["supportedGenerationMethods"], false);

            return info;
        }

        #endregion
    }
}
