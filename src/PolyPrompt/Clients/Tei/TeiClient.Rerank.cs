namespace PolyPrompt.Clients
{
    using System.Text.Json;
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using SyslogLogging;

    public partial class TeiClient
    {

        #region Protected-Methods

        /// <inheritdoc />
        protected override bool RerankRequiresModel => false;

        /// <inheritdoc />
        protected override Task<RerankResponse> RerankCoreAsync(string query, List<string> documents, string? model, int? topN, RerankOptions? options, CancellationToken token)
        {
            Dictionary<string, object> body = new Dictionary<string, object>
            {
                { "query", query },
                { "texts", documents }
            };

            bool? rawScores = Pick<TeiRerankOptions, bool>(options, Defaults.Rerank, o => o.RawScores);
            bool? truncate = Pick<TeiRerankOptions, bool>(options, Defaults.Rerank, o => o.Truncate);
            string? direction = PickRef<TeiRerankOptions, string>(options, Defaults.Rerank, o => o.TruncationDirection);

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
