namespace PolyPrompt.Clients
{
    using System.Text.Json;
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using SyslogLogging;

    /// <summary>
    /// Text Embeddings Inference sparse embedding client using <c>/embed_sparse</c>. Requires the server to host a
    /// SPLADE-style model.
    /// </summary>
    public class TeiSparseEmbeddingClient : SparseEmbeddingClientBase
    {
        #region Public-Members

        /// <summary>
        /// Client-wide default settings, including TEI settings. Default model: null (the server decides).
        /// </summary>
        public override TeiSparseEmbeddingOptions Defaults { get; } = new TeiSparseEmbeddingOptions();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new TEI sparse embedding client.
        /// </summary>
        /// <param name="endpoint">TEI server URL. Default: http://localhost:8080.</param>
        /// <param name="apiKey">Optional bearer token, needed only when the server was started with <c>--api-key</c>. Default: null.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        /// <param name="httpClient">Optional HTTP client; see <see cref="ClientBase"/>. Default: null.</param>
        public TeiSparseEmbeddingClient(
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
        protected override Task<SparseEmbeddingResponse> EmbedSparseCoreAsync(List<string> inputs, string? model, SparseEmbeddingOptions? options, CancellationToken token)
        {
            Dictionary<string, object> body = new Dictionary<string, object> { { "inputs", inputs } };

            bool? truncate = Pick<TeiSparseEmbeddingOptions, bool>(options, Defaults, o => o.Truncate);
            string? direction = PickRef<TeiSparseEmbeddingOptions, string>(options, Defaults, o => o.TruncationDirection);
            string? promptName = PickRef<TeiSparseEmbeddingOptions, string>(options, Defaults, o => o.PromptName);

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
