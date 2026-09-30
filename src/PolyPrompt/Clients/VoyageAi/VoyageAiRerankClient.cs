namespace PolyPrompt.Clients
{
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using SyslogLogging;

    /// <summary>
    /// VoyageAI rerank client using <c>/v1/rerank</c>. The response reports total tokens.
    /// </summary>
    public class VoyageAiRerankClient : RerankClientBase
    {
        #region Public-Members

        /// <summary>
        /// Client-wide default settings, including VoyageAI settings. Default model: rerank-2.5.
        /// </summary>
        public override VoyageAiRerankOptions Defaults { get; } = new VoyageAiRerankOptions { Model = "rerank-2.5" };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new VoyageAI rerank client.
        /// </summary>
        /// <param name="endpoint">VoyageAI API endpoint URL. Default: https://api.voyageai.com.</param>
        /// <param name="apiKey">VoyageAI API key, sent as a bearer token. Default: null.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        /// <param name="httpClient">Optional HTTP client; see <see cref="ClientBase"/>. Default: null.</param>
        public VoyageAiRerankClient(
            string endpoint = VoyageAiProtocol.DefaultEndpoint,
            string? apiKey = null,
            LoggingModule? logging = null,
            HttpClient? httpClient = null)
            : base(endpoint, apiKey, logging ?? new LoggingModule(), httpClient)
        {
            _Header = VoyageAiProtocol.Header;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Verify connectivity and credentials by reranking a one-document request with the client's model, because
        /// VoyageAI has no model listing endpoint to probe. Returns false on an HTTP error, an unreachable server, or a
        /// timeout; rethrows only caller cancellation.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the request succeeded.</returns>
        public override async Task<bool> ValidateConnectivityAsync(CancellationToken token = default)
        {
            try
            {
                RerankResponse probe = await RerankAsync("ping", new List<string> { "ping" }, null, token).ConfigureAwait(false);
                return probe.Success;
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
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
        {
            VoyageAiProtocol.ApplyAuth(request, _ApiKey);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override Task<RerankResponse> RerankCoreAsync(string query, List<string> documents, string? model, int? topN, RerankOptions? options, CancellationToken token)
        {
            Dictionary<string, object> body = new Dictionary<string, object>
            {
                { "model", model! },
                { "query", query },
                { "documents", documents }
            };

            if (topN.HasValue) body["top_k"] = topN.Value;

            bool? truncation = Pick<VoyageAiRerankOptions, bool>(options, Defaults, o => o.Truncation);
            if (truncation.HasValue) body["truncation"] = truncation.Value;

            RerankResponse response = new RerankResponse { Model = model };
            return ExecutePostAsync(response, "rerank", BuildUrl("/v1/rerank"), body, ParseResults, token);
        }

        #endregion

        #region Private-Methods

        private void ParseResults(string responseBody, RerankResponse response)
        {
            Dictionary<string, object>? responseObj = TryDeserializeObject(responseBody);
            List<Dictionary<string, object>>? data = responseObj == null ? null : ParseNestedList(responseObj, "data");
            if (data == null)
            {
                response.Error = "Response missing 'data' field";
                return;
            }

            foreach (Dictionary<string, object> item in data)
            {
                int? index = TryGetInt(item, "index");
                double? score = TryGetDouble(item, "relevance_score");
                if (index.HasValue && score.HasValue) response.Results.Add(new RerankResult { Index = index.Value, Score = score.Value });
            }

            if (responseObj!.ContainsKey("model") && responseObj["model"] != null) response.Model = responseObj["model"]?.ToString() ?? response.Model;

            Dictionary<string, object>? usage = ParseNestedObject(responseObj, "usage");
            if (usage != null) response.TotalTokens = TryGetRoundedInt(usage, "total_tokens");
        }

        #endregion
    }
}
