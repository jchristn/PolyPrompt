namespace PolyPrompt.Clients
{
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using SyslogLogging;

    /// <summary>
    /// Ollama embedding client using <c>/api/embed</c>, which accepts a single input or a batch.
    /// </summary>
    public class OllamaEmbeddingClient : EmbeddingClientBase
    {
        #region Public-Members

        /// <summary>
        /// Client-wide default settings, including Ollama settings. Default model: all-minilm.
        /// </summary>
        public override OllamaEmbeddingOptions Defaults { get; } = new OllamaEmbeddingOptions { Model = "all-minilm" };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new Ollama embedding client.
        /// </summary>
        /// <param name="endpoint">Ollama server endpoint URL. Default: http://localhost:11434.</param>
        /// <param name="apiKey">Optional bearer token, for Ollama servers behind an authenticating proxy. Default: null.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        /// <param name="httpClient">Optional HTTP client; see <see cref="ClientBase"/>. Default: null.</param>
        public OllamaEmbeddingClient(
            string endpoint = OllamaProtocol.DefaultEndpoint,
            string? apiKey = null,
            LoggingModule? logging = null,
            HttpClient? httpClient = null)
            : base(endpoint, apiKey, logging ?? new LoggingModule(), httpClient)
        {
            _Header = OllamaProtocol.Header;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override Task<bool> ValidateConnectivityAsync(CancellationToken token = default)
        {
            return ProbeAsync(BuildUrl(OllamaProtocol.TagsPath), token);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
        {
            OllamaProtocol.ApplyAuth(request, _ApiKey);
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

            bool? truncate = Pick<OllamaEmbeddingOptions, bool>(options, Defaults, o => o.Truncate);
            int? contextLength = Pick<OllamaEmbeddingOptions, int>(options, Defaults, o => o.ContextLength);
            if (truncate.HasValue) body["truncate"] = truncate.Value;
            if (contextLength.HasValue) body["options"] = new Dictionary<string, object> { { "num_ctx", contextLength.Value } };

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
