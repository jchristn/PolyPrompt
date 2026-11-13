namespace PolyPrompt.Clients
{
    using System.Runtime.CompilerServices;
    using PolyPrompt.Auth;
    using PolyPrompt.Models;
    using SyslogLogging;

    public partial class BedrockClient
    {

        #region Public-Methods

        /// <inheritdoc />
        protected override async IAsyncEnumerable<ModelInformation> ListModelsCoreAsync([EnumeratorCancellation] CancellationToken token)
        {
            string url = ControlPlaneUrl("/foundation-models");
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
        protected override async Task<ModelInformation?> GetModelInformationCoreAsync(string model, CancellationToken token)
        {
            string url = ControlPlaneUrl("/foundation-models/" + Uri.EscapeDataString(model));
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
