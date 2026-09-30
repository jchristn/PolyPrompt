namespace PolyPrompt.Clients
{
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using SyslogLogging;

    /// <summary>
    /// Cohere rerank client using <c>/v2/rerank</c>. Relevance scores are normalized to 0..1; the response reports billed
    /// search units and input tokens.
    /// </summary>
    public class CohereRerankClient : RerankClientBase
    {
        #region Public-Members

        /// <summary>
        /// Client-wide default settings, including Cohere settings. Default model: rerank-v3.5.
        /// </summary>
        public override CohereRerankOptions Defaults { get; } = new CohereRerankOptions { Model = "rerank-v3.5" };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new Cohere rerank client.
        /// </summary>
        /// <param name="endpoint">Cohere API endpoint URL. Default: https://api.cohere.com.</param>
        /// <param name="apiKey">Cohere API key, sent as a bearer token. Default: null.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        /// <param name="httpClient">Optional HTTP client; see <see cref="ClientBase"/>. Default: null.</param>
        public CohereRerankClient(
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

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
        {
            CohereProtocol.ApplyAuth(request, _ApiKey);
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

            if (topN.HasValue) body["top_n"] = topN.Value;

            int? maxTokensPerDoc = Pick<CohereRerankOptions, int>(options, Defaults, o => o.MaxTokensPerDoc);
            if (maxTokensPerDoc.HasValue) body["max_tokens_per_doc"] = maxTokensPerDoc.Value;

            RerankResponse response = new RerankResponse { Model = model };
            return ExecutePostAsync(response, "rerank", BuildUrl("/v2/rerank"), body, ParseResults, token);
        }

        #endregion

        #region Private-Methods

        private void ParseResults(string responseBody, RerankResponse response)
        {
            Dictionary<string, object>? responseObj = TryDeserializeObject(responseBody);
            List<Dictionary<string, object>>? results = responseObj == null ? null : ParseNestedList(responseObj, "results");
            if (results == null)
            {
                response.Error = "Response missing 'results' field";
                return;
            }

            response.ResponseId = responseObj!.ContainsKey("id") ? responseObj["id"]?.ToString() : null;

            foreach (Dictionary<string, object> item in results)
            {
                int? index = TryGetInt(item, "index");
                double? score = TryGetDouble(item, "relevance_score");
                if (index.HasValue && score.HasValue) response.Results.Add(new RerankResult { Index = index.Value, Score = score.Value });
            }

            Dictionary<string, object>? meta = ParseNestedObject(responseObj, "meta");
            if (meta != null)
            {
                Dictionary<string, object>? billed = ParseNestedObject(meta, "billed_units");
                if (billed != null) response.SearchUnits = TryGetRoundedInt(billed, "search_units");

                Dictionary<string, object>? tokens = ParseNestedObject(meta, "tokens");
                if (tokens != null) response.TotalTokens = TryGetRoundedInt(tokens, "input_tokens");
            }
        }

        #endregion
    }
}
