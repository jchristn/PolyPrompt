namespace PolyPrompt.Clients
{
    using System.Runtime.CompilerServices;
    using System.Text.Json;
    using PolyPrompt.Models;
    using PolyPrompt.Options;

    public abstract partial class ProviderClientBase
    {
        #region OpenAi-Wire

        // OpenAI-style /embeddings and /models, shared by OpenAiClient, OpenAiCompatibleClient, and AzureOpenAiClient.

        /// <summary>
        /// POST an OpenAI-style <c>/embeddings</c> request.
        /// </summary>
        private protected Task<EmbeddingResponse> OpenAiEmbedAsync(string url, List<string> inputs, string? model, EmbeddingOptions? options, CancellationToken token)
        {
            Dictionary<string, object> body = new Dictionary<string, object>
            {
                { "model", model! },
                { "input", inputs }
            };

            string? encodingFormat = PickRef<OpenAiEmbeddingOptions, string>(options, Defaults.Embedding, o => o.EncodingFormat);
            int? dimensions = Pick<OpenAiEmbeddingOptions, int>(options, Defaults.Embedding, o => o.Dimensions);
            if (!string.IsNullOrEmpty(encodingFormat)) body["encoding_format"] = encodingFormat;
            if (dimensions.HasValue) body["dimensions"] = dimensions.Value;

            EmbeddingResponse response = new EmbeddingResponse { Model = model };
            return ExecutePostAsync(response, "embed", url, body, ParseEmbeddings, token);
        }

        /// <summary>
        /// GET an OpenAI-style <c>/models</c> list.
        /// </summary>
        private protected async IAsyncEnumerable<ModelInformation> OpenAiListModelsAsync(string url, [EnumeratorCancellation] CancellationToken token)
        {
            _Logging.Debug(_Header + "GET " + StripQuery(url));

            HttpCallResult result = await GetAndRecordAsync(url, token).ConfigureAwait(false);
            if (!result.IsSuccessStatusCode)
            {
                _Logging.Warn(_Header + "list models failed with status " + result.StatusCode);
                yield break;
            }

            Dictionary<string, object>? responseObj = TryDeserializeObject(result.ResponseBody);
            List<Dictionary<string, object>>? data = responseObj == null ? null : ParseNestedList(responseObj, "data");
            if (data == null) yield break;

            foreach (Dictionary<string, object> modelObj in data)
            {
                token.ThrowIfCancellationRequested();
                yield return ParseModel(modelObj, string.Empty);
            }
        }

        /// <summary>
        /// GET an OpenAI-style <c>/models/{id}</c>.
        /// </summary>
        private protected async Task<ModelInformation?> OpenAiGetModelAsync(string url, string model, CancellationToken token)
        {
            _Logging.Debug(_Header + "GET " + StripQuery(url));

            try
            {
                HttpCallResult result = await GetAndRecordAsync(url, token).ConfigureAwait(false);
                if (!result.IsSuccessStatusCode)
                {
                    _Logging.Warn(_Header + "get model failed with status " + result.StatusCode + ": " + result.ResponseBody);
                    return null;
                }

                Dictionary<string, object>? responseObj = TryDeserializeObject(result.ResponseBody);
                return responseObj == null ? null : ParseModel(responseObj, model);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "get model failed: " + ex.Message);
                return null;
            }
        }

        private void ParseEmbeddings(string responseBody, EmbeddingResponse response)
        {
            Dictionary<string, object>? responseObj = TryDeserializeObject(responseBody);
            if (responseObj == null || !responseObj.ContainsKey("data"))
            {
                response.Error = "Response missing 'data' field";
                return;
            }

            List<Dictionary<string, object>>? data = ParseNestedList(responseObj, "data");
            if (data == null) return;

            for (int i = 0; i < data.Count; i++)
            {
                Dictionary<string, object> item = data[i];
                EmbeddingResult result = new EmbeddingResult();
                result.Index = TryGetInt(item, "index") ?? i;

                if (item.ContainsKey("embedding") && item["embedding"] is JsonElement vector)
                {
                    result.Embedding = vector.ValueKind == JsonValueKind.String
                        ? OpenAiDecodeBase64Floats(vector.GetString())
                        : ParseFloatArray(vector.GetRawText());
                }

                response.Embeddings.Add(result);
            }

            response.Embeddings.Sort((a, b) => a.Index.CompareTo(b.Index));
        }

        private static float[] OpenAiDecodeBase64Floats(string? base64)
        {
            if (string.IsNullOrEmpty(base64)) return Array.Empty<float>();
            byte[] bytes = Convert.FromBase64String(base64);
            float[] values = new float[bytes.Length / sizeof(float)];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = BitConverter.ToSingle(bytes, i * sizeof(float));
            }
            return values;
        }

        private static ModelInformation ParseModel(Dictionary<string, object> modelObj, string fallbackName)
        {
            ModelInformation info = new ModelInformation();
            info.Name = modelObj.ContainsKey("id") ? modelObj["id"]?.ToString() ?? fallbackName : fallbackName;
            info.OwnedBy = modelObj.ContainsKey("owned_by") ? modelObj["owned_by"]?.ToString() : null;

            long? created = TryGetLong(modelObj, "created");
            if (created.HasValue) info.CreatedUtc = DateTimeOffset.FromUnixTimeSeconds(created.Value).UtcDateTime;

            if (modelObj.ContainsKey("object")) info.Metadata["object"] = modelObj["object"]?.ToString();
            return info;
        }

        #endregion
    }
}
