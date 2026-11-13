namespace PolyPrompt.Clients
{
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using PolyPrompt.Telemetry;
    using SyslogLogging;

    public partial class VoyageAiClient
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

            if (topN.HasValue) body["top_k"] = topN.Value;

            bool? truncation = Pick<VoyageAiRerankOptions, bool>(options, Defaults.Rerank, o => o.Truncation);
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
