namespace PolyPrompt.Clients
{
    using System.Text.Json;
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using SyslogLogging;

    /// <summary>
    /// OpenAI embedding client using <c>/v1/embeddings</c>, which accepts a single input or a batch. Works with
    /// OpenAI-compatible servers. Vectors returned as float arrays or as base64 (<c>EncodingFormat = "base64"</c>) are
    /// both decoded.
    /// </summary>
    public class OpenAiEmbeddingClient : EmbeddingClientBase
    {
        #region Public-Members

        /// <summary>
        /// Client-wide default settings, including OpenAI settings. Default model: text-embedding-3-small.
        /// </summary>
        public override OpenAiEmbeddingOptions Defaults { get; } = new OpenAiEmbeddingOptions { Model = "text-embedding-3-small" };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new OpenAI embedding client.
        /// </summary>
        /// <param name="endpoint">API endpoint URL; <c>/v1</c> is appended unless present. Default: https://api.openai.com.</param>
        /// <param name="apiKey">API key, sent as a bearer token. Default: null.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        /// <param name="httpClient">Optional HTTP client; see <see cref="ClientBase"/>. Default: null.</param>
        public OpenAiEmbeddingClient(
            string endpoint = OpenAiProtocol.DefaultEndpoint,
            string? apiKey = null,
            LoggingModule? logging = null,
            HttpClient? httpClient = null)
            : base(endpoint, apiKey, logging ?? new LoggingModule(), httpClient)
        {
            _Header = OpenAiProtocol.Header;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override Task<bool> ValidateConnectivityAsync(CancellationToken token = default)
        {
            return ProbeAsync(BuildApiUrl("models", null), token);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Build the URL for an operation. Azure OpenAI overrides this to route by deployment.
        /// </summary>
        /// <param name="path">Operation path, for example <c>embeddings</c>.</param>
        /// <param name="model">The model for this call, or null for operations that are not model-scoped.</param>
        /// <returns>The absolute URL.</returns>
        protected virtual string BuildApiUrl(string path, string? model)
        {
            return OpenAiProtocol.BuildApiUrl(_Endpoint, path);
        }

        /// <inheritdoc />
        protected override Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
        {
            OpenAiProtocol.ApplyAuth(request, _ApiKey);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override Task<EmbeddingResponse> EmbedCoreAsync(List<string> inputs, string? model, EmbeddingOptions? options, CancellationToken token)
        {
            Dictionary<string, object> body = new Dictionary<string, object>
            {
                { "model", model! },
                { "input", inputs }
            };

            string? encodingFormat = PickRef<OpenAiEmbeddingOptions, string>(options, Defaults, o => o.EncodingFormat);
            int? dimensions = Pick<OpenAiEmbeddingOptions, int>(options, Defaults, o => o.Dimensions);
            if (!string.IsNullOrEmpty(encodingFormat)) body["encoding_format"] = encodingFormat;
            if (dimensions.HasValue) body["dimensions"] = dimensions.Value;

            EmbeddingResponse response = new EmbeddingResponse { Model = model };
            return ExecutePostAsync(response, "embed", BuildApiUrl("embeddings", model), body, ParseEmbeddings, token);
        }

        #endregion

        #region Private-Methods

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
                        ? DecodeBase64Floats(vector.GetString())
                        : ParseFloatArray(vector.GetRawText());
                }

                response.Embeddings.Add(result);
            }

            response.Embeddings.Sort((a, b) => a.Index.CompareTo(b.Index));
        }

        private static float[] DecodeBase64Floats(string? base64)
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

        #endregion
    }
}
