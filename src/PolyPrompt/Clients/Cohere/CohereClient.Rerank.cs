namespace PolyPrompt.Clients
{
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using SyslogLogging;

    public partial class CohereClient
    {

        #region Protected-Methods

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

            int? maxTokensPerDoc = Pick<CohereRerankOptions, int>(options, Defaults.Rerank, o => o.MaxTokensPerDoc);
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
