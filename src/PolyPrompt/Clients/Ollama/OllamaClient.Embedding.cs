namespace PolyPrompt.Clients
{
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using SyslogLogging;

    public partial class OllamaClient
    {

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task<EmbeddingResponse> EmbedCoreAsync(List<string> inputs, string? model, EmbeddingOptions? options, CancellationToken token)
        {
            Dictionary<string, object> body = new Dictionary<string, object>
            {
                { "model", model! },
                { "input", inputs }
            };

            bool? truncate = Pick<OllamaEmbeddingOptions, bool>(options, Defaults.Embedding, o => o.Truncate);
            int? contextLength = Pick<OllamaEmbeddingOptions, int>(options, Defaults.Embedding, o => o.ContextLength);
            if (truncate.HasValue) body["truncate"] = truncate.Value;
            if (contextLength.HasValue) body["options"] = new Dictionary<string, object> { { "num_ctx", contextLength.Value } };
            TimeSpan? keepAlive = Pick<OllamaEmbeddingOptions, TimeSpan>(options, Defaults.Embedding, o => o.KeepAlive);
            if (keepAlive.HasValue) body["keep_alive"] = OllamaProtocol.KeepAliveSeconds(keepAlive.Value);

            EmbeddingResponse response = new EmbeddingResponse { Model = model };
            return ExecutePostAsync(response, "embed", BuildUrl("/api/embed"), body, ParseEmbeddings, token);
        }

        #endregion

        #region Private-Methods

        private void ParseEmbeddings(string responseBody, EmbeddingResponse response)
        {
            Dictionary<string, object>? responseObj = TryDeserializeObject(responseBody);
            if (responseObj == null || !responseObj.ContainsKey("embeddings"))
            {
                response.Error = "Response missing 'embeddings' field";
                return;
            }

            List<object>? embeddings = _Serializer.DeserializeJson<List<object>>(_Serializer.SerializeJson(responseObj["embeddings"], false));
            if (embeddings == null) return;

            for (int i = 0; i < embeddings.Count; i++)
            {
                response.Embeddings.Add(new EmbeddingResult
                {
                    Index = i,
                    Embedding = ParseFloatArray(_Serializer.SerializeJson(embeddings[i], false))
                });
            }
        }

        #endregion
    }
}
