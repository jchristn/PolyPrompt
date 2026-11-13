namespace PolyPrompt.Clients
{
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using SyslogLogging;

    public partial class GeminiClient
    {

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task<EmbeddingResponse> EmbedSingleCoreAsync(string input, string? model, EmbeddingOptions? options, CancellationToken token)
        {
            EmbeddingResponse response = new EmbeddingResponse { Model = model };
            return ExecutePostAsync(response, "embed", GeminiProtocol.ModelUrl(_Endpoint, model!) + ":embedContent", BuildRequest(input, model!, options), ParseSingle, token);
        }

        /// <inheritdoc />
        protected override Task<EmbeddingResponse> EmbedCoreAsync(List<string> inputs, string? model, EmbeddingOptions? options, CancellationToken token)
        {
            List<Dictionary<string, object>> requests = inputs.Select(input => BuildRequest(input, model!, options)).ToList();
            Dictionary<string, object> body = new Dictionary<string, object> { { "requests", requests } };

            EmbeddingResponse response = new EmbeddingResponse { Model = model };
            return ExecutePostAsync(response, "batch embed", GeminiProtocol.ModelUrl(_Endpoint, model!) + ":batchEmbedContents", body, ParseBatch, token);
        }

        #endregion

        #region Private-Methods

        private Dictionary<string, object> BuildRequest(string input, string model, EmbeddingOptions? options)
        {
            string name = model.StartsWith("models/", StringComparison.Ordinal) ? model : "models/" + model;
            Dictionary<string, object> request = new Dictionary<string, object>
            {
                { "model", name },
                { "content", new Dictionary<string, object>
                    {
                        { "parts", new List<Dictionary<string, object>> { new Dictionary<string, object> { { "text", input } } } }
                    }
                }
            };

            string? taskType = PickRef<GeminiEmbeddingOptions, string>(options, Defaults.Embedding, o => o.TaskType);
            string? title = PickRef<GeminiEmbeddingOptions, string>(options, Defaults.Embedding, o => o.Title);
            if (!string.IsNullOrEmpty(taskType)) request["taskType"] = taskType;
            if (!string.IsNullOrEmpty(title)) request["title"] = title;
            return request;
        }

        private void ParseSingle(string responseBody, EmbeddingResponse response)
        {
            Dictionary<string, object>? responseObj = TryDeserializeObject(responseBody);
            Dictionary<string, object>? embedding = responseObj == null ? null : ParseNestedObject(responseObj, "embedding");
            if (embedding == null || !embedding.ContainsKey("values"))
            {
                response.Error = "Response missing 'embedding.values' field";
                return;
            }

            response.Embeddings.Add(new EmbeddingResult { Index = 0, Embedding = ParseFloatArray(_Serializer.SerializeJson(embedding["values"], false)) });
        }

        private void ParseBatch(string responseBody, EmbeddingResponse response)
        {
            Dictionary<string, object>? responseObj = TryDeserializeObject(responseBody);
            List<Dictionary<string, object>>? embeddings = responseObj == null ? null : ParseNestedList(responseObj, "embeddings");
            if (embeddings == null)
            {
                response.Error = "Response missing 'embeddings' field";
                return;
            }

            for (int i = 0; i < embeddings.Count; i++)
            {
                if (!embeddings[i].ContainsKey("values")) continue;
                response.Embeddings.Add(new EmbeddingResult { Index = i, Embedding = ParseFloatArray(_Serializer.SerializeJson(embeddings[i]["values"], false)) });
            }
        }

        #endregion
    }
}
