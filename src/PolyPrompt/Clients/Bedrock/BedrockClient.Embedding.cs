namespace PolyPrompt.Clients
{
    using System.Diagnostics;
    using PolyPrompt.Auth;
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using SyslogLogging;

    public partial class BedrockClient
    {

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<EmbeddingResponse> EmbedCoreAsync(List<string> inputs, string? model, EmbeddingOptions? options, CancellationToken token)
        {
            if (model!.StartsWith("cohere.", StringComparison.OrdinalIgnoreCase))
            {
                string? inputType = PickRef<BedrockEmbeddingOptions, string>(options, Defaults.Embedding, o => o.InputType);
                Dictionary<string, object> body = new Dictionary<string, object>
                {
                    { "texts", inputs },
                    { "input_type", string.IsNullOrEmpty(inputType) ? "search_document" : inputType }
                };

                EmbeddingResponse cohereResponse = new EmbeddingResponse { Model = model };
                return await ExecutePostAsync(cohereResponse, "embed", BedrockProtocol.InvokeUrl(_Endpoint, model), body, ParseCohere, token).ConfigureAwait(false);
            }

            // Amazon Titan (and other single-input families): one InvokeModel per input.
            int? dimensions = Pick<BedrockEmbeddingOptions, int>(options, Defaults.Embedding, o => o.Dimensions);
            bool? normalize = Pick<BedrockEmbeddingOptions, bool>(options, Defaults.Embedding, o => o.Normalize);

            EmbeddingResponse response = new EmbeddingResponse { Model = model, Success = true };
            Stopwatch sw = Stopwatch.StartNew();

            for (int i = 0; i < inputs.Count; i++)
            {
                Dictionary<string, object> body = new Dictionary<string, object> { { "inputText", inputs[i] } };
                if (dimensions.HasValue) body["dimensions"] = dimensions.Value;
                if (normalize.HasValue) body["normalize"] = normalize.Value;

                int index = i;
                EmbeddingResponse single = new EmbeddingResponse { Model = model };
                await ExecutePostAsync(single, "embed", BedrockProtocol.InvokeUrl(_Endpoint, model), body, (text, r) => ParseTitan(text, r, index), token).ConfigureAwait(false);

                response.StatusCode = single.StatusCode;
                if (!single.Success)
                {
                    response.Success = false;
                    response.Error = "Input " + i + ": " + single.Error;
                    response.Embeddings.Clear();
                    break;
                }

                response.Embeddings.AddRange(single.Embeddings);
            }

            sw.Stop();
            response.OverallRuntimeMs = sw.ElapsedMilliseconds;
            return response;
        }

        #endregion

        #region Private-Methods

        private void ParseTitan(string responseBody, EmbeddingResponse response, int index)
        {
            Dictionary<string, object>? responseObj = TryDeserializeObject(responseBody);
            if (responseObj == null || !responseObj.ContainsKey("embedding"))
            {
                response.Error = "Response missing 'embedding' field";
                return;
            }

            response.Embeddings.Add(new EmbeddingResult { Index = index, Embedding = ParseFloatArray(_Serializer.SerializeJson(responseObj["embedding"], false)) });
        }

        private void ParseCohere(string responseBody, EmbeddingResponse response)
        {
            Dictionary<string, object>? responseObj = TryDeserializeObject(responseBody);
            if (responseObj == null || !responseObj.ContainsKey("embeddings"))
            {
                response.Error = "Response missing 'embeddings' field";
                return;
            }

            List<object>? vectors = _Serializer.DeserializeJson<List<object>>(_Serializer.SerializeJson(responseObj["embeddings"], false));
            if (vectors == null) return;

            for (int i = 0; i < vectors.Count; i++)
            {
                response.Embeddings.Add(new EmbeddingResult { Index = i, Embedding = ParseFloatArray(_Serializer.SerializeJson(vectors[i], false)) });
            }
        }

        #endregion
    }
}
