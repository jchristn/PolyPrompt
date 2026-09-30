namespace PolyPrompt.Clients
{
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using SyslogLogging;

    /// <summary>
    /// Cohere embedding client using <c>/v2/embed</c>. Cohere requires <c>input_type</c> for v3 and later models; when
    /// <see cref="CohereEmbeddingOptions.InputType"/> is not set, <c>search_document</c> is sent.
    /// </summary>
    public class CohereEmbeddingClient : EmbeddingClientBase
    {
        #region Public-Members

        /// <summary>
        /// Client-wide default settings, including Cohere settings. Default model: embed-v4.0.
        /// </summary>
        public override CohereEmbeddingOptions Defaults { get; } = new CohereEmbeddingOptions { Model = "embed-v4.0" };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new Cohere embedding client.
        /// </summary>
        /// <param name="endpoint">Cohere API endpoint URL. Default: https://api.cohere.com.</param>
        /// <param name="apiKey">Cohere API key, sent as a bearer token. Default: null.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        /// <param name="httpClient">Optional HTTP client; see <see cref="ClientBase"/>. Default: null.</param>
        public CohereEmbeddingClient(
            string endpoint = CohereProtocol.DefaultEndpoint,
            string? apiKey = null,
            LoggingModule? logging = null,
            HttpClient? httpClient = null)
            : base(endpoint, apiKey, logging ?? new LoggingModule(), httpClient)
        {
            _Header = CohereProtocol.Header;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override Task<bool> ValidateConnectivityAsync(CancellationToken token = default)
        {
            return ProbeAsync(BuildUrl(CohereProtocol.ProbePath), token);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
        {
            CohereProtocol.ApplyAuth(request, _ApiKey);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override Task<EmbeddingResponse> EmbedCoreAsync(List<string> inputs, string? model, EmbeddingOptions? options, CancellationToken token)
        {
            string embeddingType = PickRef<CohereEmbeddingOptions, string>(options, Defaults, o => o.EmbeddingType) ?? "float";
            string inputType = PickRef<CohereEmbeddingOptions, string>(options, Defaults, o => o.InputType) ?? "search_document";
            int? outputDimension = Pick<CohereEmbeddingOptions, int>(options, Defaults, o => o.OutputDimension);
            string? truncate = PickRef<CohereEmbeddingOptions, string>(options, Defaults, o => o.Truncate);

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
