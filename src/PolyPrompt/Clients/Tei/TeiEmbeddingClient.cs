namespace PolyPrompt.Clients
{
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using SyslogLogging;

    /// <summary>
    /// Text Embeddings Inference dense embedding client using <c>/embed</c>. The server hosts a single model, so no model
    /// name is sent; <c>Model</c> is informational. A batch larger than the server's <c>max_client_batch_size</c> fails
    /// with HTTP 413.
    /// </summary>
    public class TeiEmbeddingClient : EmbeddingClientBase
    {
        #region Public-Members

        /// <summary>
        /// Client-wide default settings, including TEI settings. Default model: null (the server decides).
        /// </summary>
        public override TeiEmbeddingOptions Defaults { get; } = new TeiEmbeddingOptions();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new TEI embedding client.
        /// </summary>
        /// <param name="endpoint">TEI server URL. Default: http://localhost:8080.</param>
        /// <param name="apiKey">Optional bearer token, needed only when the server was started with <c>--api-key</c>. Default: null.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        /// <param name="httpClient">Optional HTTP client; see <see cref="ClientBase"/>. Default: null.</param>
        public TeiEmbeddingClient(
            string endpoint = TeiProtocol.DefaultEndpoint,
            string? apiKey = null,
            LoggingModule? logging = null,
            HttpClient? httpClient = null)
            : base(endpoint, apiKey, logging ?? new LoggingModule(), httpClient)
        {
            _Header = TeiProtocol.Header;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override Task<bool> ValidateConnectivityAsync(CancellationToken token = default)
        {
            return ProbeAsync(BuildUrl(TeiProtocol.HealthPath), token);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override bool RequiresModel => false;

        /// <inheritdoc />
        protected override Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
        {
            TeiProtocol.ApplyAuth(request, _ApiKey);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override Task<EmbeddingResponse> EmbedCoreAsync(List<string> inputs, string? model, EmbeddingOptions? options, CancellationToken token)
        {
            Dictionary<string, object> body = new Dictionary<string, object> { { "inputs", inputs } };

            bool? normalize = Pick<TeiEmbeddingOptions, bool>(options, Defaults, o => o.Normalize);
            bool? truncate = Pick<TeiEmbeddingOptions, bool>(options, Defaults, o => o.Truncate);
            string? direction = PickRef<TeiEmbeddingOptions, string>(options, Defaults, o => o.TruncationDirection);
            string? promptName = PickRef<TeiEmbeddingOptions, string>(options, Defaults, o => o.PromptName);
            int? dimensions = Pick<TeiEmbeddingOptions, int>(options, Defaults, o => o.Dimensions);

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
