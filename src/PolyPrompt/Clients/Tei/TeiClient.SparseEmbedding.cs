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
        protected override bool SparseEmbeddingRequiresModel => false;

        /// <inheritdoc />
        protected override Task<SparseEmbeddingResponse> EmbedSparseCoreAsync(List<string> inputs, string? model, SparseEmbeddingOptions? options, CancellationToken token)
        {
            Dictionary<string, object> body = new Dictionary<string, object> { { "inputs", inputs } };

            bool? truncate = Pick<TeiSparseEmbeddingOptions, bool>(options, Defaults.SparseEmbedding, o => o.Truncate);
            string? direction = PickRef<TeiSparseEmbeddingOptions, string>(options, Defaults.SparseEmbedding, o => o.TruncationDirection);
            string? promptName = PickRef<TeiSparseEmbeddingOptions, string>(options, Defaults.SparseEmbedding, o => o.PromptName);

            if (truncate.HasValue) body["truncate"] = truncate.Value;
            if (direction != null) body["truncation_direction"] = direction;
            if (promptName != null) body["prompt_name"] = promptName;

            SparseEmbeddingResponse response = new SparseEmbeddingResponse { Model = model };
            return ExecutePostAsync(response, "sparse embed", BuildUrl("/embed_sparse"), body, ParseSparse, token);
        }

        #endregion

        #region Private-Methods

        private void ParseSparse(string responseBody, SparseEmbeddingResponse response)
        {
            List<List<Dictionary<string, object>>>? vectors = null;
            if (TryParseJson(responseBody, out JsonElement root) && root.ValueKind == JsonValueKind.Array)
            {
                try
                {
                    vectors = _Serializer.DeserializeJson<List<List<Dictionary<string, object>>>>(root.GetRawText());
                }
                catch (JsonException)
                {
                    vectors = null;
                }
            }

            if (vectors == null)
            {
                response.Error = "Response is not a JSON array of sparse embeddings";
                return;
            }

            for (int i = 0; i < vectors.Count; i++)
            {
                SparseEmbeddingResult sparse = new SparseEmbeddingResult { Index = i };
                foreach (Dictionary<string, object> entry in vectors[i])
                {
                    int? index = TryGetInt(entry, "index");
                    double? value = TryGetDouble(entry, "value");
                    if (index.HasValue && value.HasValue) sparse.Values.Add(new SparseValue { Index = index.Value, Value = (float)value.Value });
                }
                response.Embeddings.Add(sparse);
            }
        }

        #endregion
    }
}
