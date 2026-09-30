namespace PolyPrompt.Clients
{
    using System.Text.Json;
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using SyslogLogging;

    /// <summary>
    /// Text Embeddings Inference rerank client using <c>/rerank</c>. Requires the server to host a reranker (cross-encoder)
    /// model. TEI has no top-N parameter, so every document is scored and the results are trimmed client-side.
    /// </summary>
    public class TeiRerankClient : RerankClientBase
    {
        #region Public-Members

        /// <summary>
        /// Client-wide default settings, including TEI settings. Default model: null (the server decides).
        /// </summary>
        public override TeiRerankOptions Defaults { get; } = new TeiRerankOptions();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new TEI rerank client.
        /// </summary>
        /// <param name="endpoint">TEI server URL. Default: http://localhost:8080.</param>
        /// <param name="apiKey">Optional bearer token, needed only when the server was started with <c>--api-key</c>. Default: null.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        /// <param name="httpClient">Optional HTTP client; see <see cref="ClientBase"/>. Default: null.</param>
        public TeiRerankClient(
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

        /// <inheritdoc />
        public override Task<bool> ValidateConnectivityAsync(CancellationToken token = default)
        {
            return ProbeAsync(BuildUrl(TeiProtocol.HealthPath), token);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override bool RequiresModel => false;

        /// <inheritdoc />
        protected override Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
        {
            TeiProtocol.ApplyAuth(request, _ApiKey);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override Task<RerankResponse> RerankCoreAsync(string query, List<string> documents, string? model, int? topN, RerankOptions? options, CancellationToken token)
        {
            Dictionary<string, object> body = new Dictionary<string, object>
            {
                { "query", query },
                { "texts", documents }
            };

            bool? rawScores = Pick<TeiRerankOptions, bool>(options, Defaults, o => o.RawScores);
            bool? truncate = Pick<TeiRerankOptions, bool>(options, Defaults, o => o.Truncate);
            string? direction = PickRef<TeiRerankOptions, string>(options, Defaults, o => o.TruncationDirection);

            if (rawScores.HasValue) body["raw_scores"] = rawScores.Value;
            if (truncate.HasValue) body["truncate"] = truncate.Value;
            if (direction != null) body["truncation_direction"] = direction;

            RerankResponse response = new RerankResponse { Model = model };
            return ExecutePostAsync(response, "rerank", BuildUrl("/rerank"), body, ParseRanks, token);
        }

        #endregion

        #region Private-Methods

        private void ParseRanks(string responseBody, RerankResponse response)
        {
            List<Dictionary<string, object>>? ranks = null;
            if (TryParseJson(responseBody, out JsonElement root) && root.ValueKind == JsonValueKind.Array)
            {
                try
                {
                    ranks = _Serializer.DeserializeJson<List<Dictionary<string, object>>>(root.GetRawText());
                }
                catch (JsonException)
                {
                    ranks = null;
                }
            }

            if (ranks == null)
            {
                response.Error = "Response is not a JSON array of ranks";
                return;
            }

            foreach (Dictionary<string, object> rank in ranks)
            {
                int? index = TryGetInt(rank, "index");
                double? score = TryGetDouble(rank, "score");
                if (index.HasValue && score.HasValue) response.Results.Add(new RerankResult { Index = index.Value, Score = score.Value });
            }
        }

        #endregion
    }
}
