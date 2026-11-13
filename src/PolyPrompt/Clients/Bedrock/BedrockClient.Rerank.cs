namespace PolyPrompt.Clients
{
    using PolyPrompt.Auth;
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using SyslogLogging;

    public partial class BedrockClient
    {

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task<RerankResponse> RerankCoreAsync(string query, List<string> documents, string? model, int? topN, RerankOptions? options, CancellationToken token)
        {
            Dictionary<string, object> body = new Dictionary<string, object>
            {
                { "query", query },
                { "documents", documents }
            };

            if (topN.HasValue) body["top_n"] = topN.Value;
            if (model!.StartsWith("cohere.", StringComparison.OrdinalIgnoreCase)) body["api_version"] = 2;

            int? maxTokensPerDoc = Pick<BedrockRerankOptions, int>(options, Defaults.Rerank, o => o.MaxTokensPerDoc);
            if (maxTokensPerDoc.HasValue) body["max_tokens_per_doc"] = maxTokensPerDoc.Value;

            RerankResponse response = new RerankResponse { Model = model };
            return ExecutePostAsync(response, "rerank", BedrockProtocol.InvokeUrl(_Endpoint, model), body, ParseResults, token);
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

            foreach (Dictionary<string, object> item in results)
            {
                int? index = TryGetInt(item, "index");
                double? score = TryGetDouble(item, "relevance_score");
                if (index.HasValue && score.HasValue) response.Results.Add(new RerankResult { Index = index.Value, Score = score.Value });
            }
        }

        #endregion
    }
}
