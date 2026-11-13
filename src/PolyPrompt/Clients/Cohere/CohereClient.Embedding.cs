namespace PolyPrompt.Clients
{
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using SyslogLogging;

    public partial class CohereClient
    {

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task<EmbeddingResponse> EmbedCoreAsync(List<string> inputs, string? model, EmbeddingOptions? options, CancellationToken token)
        {
            string embeddingType = PickRef<CohereEmbeddingOptions, string>(options, Defaults.Embedding, o => o.EmbeddingType) ?? "float";
            string inputType = PickRef<CohereEmbeddingOptions, string>(options, Defaults.Embedding, o => o.InputType) ?? "search_document";
            int? outputDimension = Pick<CohereEmbeddingOptions, int>(options, Defaults.Embedding, o => o.OutputDimension);
            string? truncate = PickRef<CohereEmbeddingOptions, string>(options, Defaults.Embedding, o => o.Truncate);

            Dictionary<string, object> body = new Dictionary<string, object>
            {
                { "model", model! },
                { "texts", inputs },
                { "input_type", inputType },
                { "embedding_types", new List<string> { embeddingType } }
            };

            if (outputDimension.HasValue) body["output_dimension"] = outputDimension.Value;
            if (truncate != null) body["truncate"] = truncate;

            EmbeddingResponse response = new EmbeddingResponse { Model = model };
            return ExecutePostAsync(response, "embed", BuildUrl("/v2/embed"), body, (text, r) => ParseEmbeddings(text, r, embeddingType), token);
        }

        #endregion

        #region Private-Methods

        private void ParseEmbeddings(string responseBody, EmbeddingResponse response, string embeddingType)
        {
            Dictionary<string, object>? responseObj = TryDeserializeObject(responseBody);
            Dictionary<string, object>? embeddings = responseObj != null ? ParseNestedObject(responseObj, "embeddings") : null;
            if (embeddings == null || !embeddings.ContainsKey(embeddingType) || embeddings[embeddingType] == null)
            {
                response.Error = "Response missing 'embeddings." + embeddingType + "' field";
                return;
            }

            List<object>? vectors = _Serializer.DeserializeJson<List<object>>(_Serializer.SerializeJson(embeddings[embeddingType], false));
            if (vectors == null) return;

            for (int i = 0; i < vectors.Count; i++)
            {
                response.Embeddings.Add(new EmbeddingResult { Index = i, Embedding = ParseFloatArray(_Serializer.SerializeJson(vectors[i], false)) });
            }
        }

        #endregion
    }
}
