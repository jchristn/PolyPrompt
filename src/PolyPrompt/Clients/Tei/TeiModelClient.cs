namespace PolyPrompt.Clients
{
    using System.Runtime.CompilerServices;
    using PolyPrompt.Models;
    using SyslogLogging;

    /// <summary>
    /// Text Embeddings Inference model client. The server hosts a single model, described by <c>GET /info</c>; its
    /// <see cref="ModelInformation.Metadata"/> carries <c>model_type</c> ("embedding", "reranker", or "classifier"),
    /// <c>model_dtype</c>, <c>max_client_batch_size</c>, <c>max_batch_tokens</c>, <c>version</c>, and
    /// <c>served_model_name</c> when reported.
    /// </summary>
    public class TeiModelClient : ModelClientBase
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new TEI model client.
        /// </summary>
        /// <param name="endpoint">TEI server URL. Default: http://localhost:8080.</param>
        /// <param name="apiKey">Optional bearer token, needed only when the server was started with <c>--api-key</c>. Default: null.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        /// <param name="httpClient">Optional HTTP client; see <see cref="ClientBase"/>. Default: null.</param>
        public TeiModelClient(
            string endpoint = TeiProtocol.DefaultEndpoint,
            string? apiKey = null,
            LoggingModule? logging = null,
            HttpClient? httpClient = null)
            : base(endpoint, apiKey, logging ?? new LoggingModule(), httpClient)
        {
            _Header = TeiProtocol.Header;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Verify connectivity with <c>GET /health</c>, which TEI answers with 200 once the model is loaded and 503 while it
        /// is unhealthy. Returns false on an HTTP error, an unreachable server, or a timeout; rethrows only caller cancellation.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the server reports healthy.</returns>
        public override Task<bool> ValidateConnectivityAsync(CancellationToken token = default)
        {
            return ProbeAsync(BuildUrl(TeiProtocol.HealthPath), token);
        }

        /// <summary>
        /// List the single hosted model, read from <c>GET /info</c>. Yields nothing when the server is unreachable or
        /// returns an error.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Zero or one model.</returns>
        public override async IAsyncEnumerable<ModelInformation> ListModelsAsync([EnumeratorCancellation] CancellationToken token = default)
        {
            ModelInformation? info = await GetInfoAsync(token).ConfigureAwait(false);
            if (info != null) yield return info;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
        {
            TeiProtocol.ApplyAuth(request, _ApiKey);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Return the hosted model's information when <paramref name="model"/> matches its model id or served model name
        /// (case-insensitive); otherwise null.
        /// </summary>
        /// <param name="model">The model name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The hosted model's information, or null.</returns>
        protected override async Task<ModelInformation?> GetModelInformationCoreAsync(string model, CancellationToken token)
        {
            ModelInformation? info = await GetInfoAsync(token).ConfigureAwait(false);
            if (info == null) return null;

            if (string.Equals(info.Name, model, StringComparison.OrdinalIgnoreCase)) return info;
            if (info.DisplayName != null && string.Equals(info.DisplayName, model, StringComparison.OrdinalIgnoreCase)) return info;
            return null;
        }

        #endregion

        #region Private-Methods

        private async Task<ModelInformation?> GetInfoAsync(CancellationToken token)
        {
            string url = BuildUrl("/info");
            _Logging.Debug(_Header + "GET " + url);

            try
            {
                HttpCallResult result = await GetAndRecordAsync(url, token).ConfigureAwait(false);
                if (!result.IsSuccessStatusCode)
                {
                    _Logging.Warn(_Header + "info request failed with status " + result.StatusCode + ": " + result.ResponseBody);
                    return null;
                }

                Dictionary<string, object>? infoObj = TryDeserializeObject(result.ResponseBody);
                if (infoObj == null || !infoObj.ContainsKey("model_id")) return null;

                ModelInformation info = new ModelInformation();
                info.Name = infoObj["model_id"]?.ToString() ?? string.Empty;
                info.DisplayName = infoObj.ContainsKey("served_model_name") ? infoObj["served_model_name"]?.ToString() : null;
                info.OwnedBy = "text-embeddings-inference";
                info.InputTokenLimit = TryGetInt(infoObj, "max_input_length");

                Dictionary<string, object>? modelType = ParseNestedObject(infoObj, "model_type");
                if (modelType != null && modelType.Count > 0) info.Metadata["model_type"] = modelType.Keys.First();

                foreach (string key in new[] { "model_dtype", "max_client_batch_size", "max_batch_tokens", "version", "served_model_name" })
                {
                    if (infoObj.ContainsKey(key) && infoObj[key] != null) info.Metadata[key] = infoObj[key]?.ToString();
                }

                return info;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "info request failed: " + ex.Message);
                return null;
            }
        }

        #endregion
    }
}
