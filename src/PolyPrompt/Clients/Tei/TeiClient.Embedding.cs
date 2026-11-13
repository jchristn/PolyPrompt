namespace PolyPrompt.Clients
{
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using SyslogLogging;

    public partial class TeiClient
    {

        #region Protected-Methods

        /// <inheritdoc />
        protected override bool EmbeddingRequiresModel => false;

        /// <inheritdoc />
        protected override Task<EmbeddingResponse> EmbedCoreAsync(List<string> inputs, string? model, EmbeddingOptions? options, CancellationToken token)
        {
            Dictionary<string, object> body = new Dictionary<string, object> { { "inputs", inputs } };

            bool? normalize = Pick<TeiEmbeddingOptions, bool>(options, Defaults.Embedding, o => o.Normalize);
            bool? truncate = Pick<TeiEmbeddingOptions, bool>(options, Defaults.Embedding, o => o.Truncate);
            string? direction = PickRef<TeiEmbeddingOptions, string>(options, Defaults.Embedding, o => o.TruncationDirection);
            string? promptName = PickRef<TeiEmbeddingOptions, string>(options, Defaults.Embedding, o => o.PromptName);
            int? dimensions = Pick<TeiEmbeddingOptions, int>(options, Defaults.Embedding, o => o.Dimensions);

            if (normalize.HasValue) body["normalize"] = normalize.Value;
            if (truncate.HasValue) body["truncate"] = truncate.Value;
            if (direction != null) body["truncation_direction"] = direction;
            if (promptName != null) body["prompt_name"] = promptName;
            if (dimensions.HasValue) body["dimensions"] = dimensions.Value;

            EmbeddingResponse response = new EmbeddingResponse { Model = model };
            return ExecutePostAsync(response, "embed", BuildUrl("/embed"), body, ParseEmbeddings, token);
        }

        #endregion

        #region Private-Methods

        private void ParseEmbeddings(string responseBody, EmbeddingResponse response)
        {
            List<object>? vectors = TryParseJson(responseBody, out System.Text.Json.JsonElement root) && root.ValueKind == System.Text.Json.JsonValueKind.Array
                ? _Serializer.DeserializeJson<List<object>>(root.GetRawText())
                : null;

            if (vectors == null)
            {
                response.Error = "Response is not a JSON array of embeddings";
                return;
            }

            for (int i = 0; i < vectors.Count; i++)
            {
                response.Embeddings.Add(new EmbeddingResult { Index = i, Embedding = ParseFloatArray(_Serializer.SerializeJson(vectors[i], false)) });
            }
        }

        #endregion
    }
}
