namespace PolyPrompt.Clients
{
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using SyslogLogging;

    /// <summary>
    /// VoyageAI embedding client using <c>/v1/embeddings</c>.
    /// </summary>
    public class VoyageAiEmbeddingClient : EmbeddingClientBase
    {
        #region Public-Members

        /// <summary>
        /// Client-wide default settings, including VoyageAI settings. Default model: voyage-3.5.
        /// </summary>
        public override VoyageAiEmbeddingOptions Defaults { get; } = new VoyageAiEmbeddingOptions { Model = "voyage-3.5" };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new VoyageAI embedding client.
        /// </summary>
        /// <param name="endpoint">VoyageAI API endpoint URL. Default: https://api.voyageai.com.</param>
        /// <param name="apiKey">VoyageAI API key, sent as a bearer token. Default: null.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        /// <param name="httpClient">Optional HTTP client; see <see cref="ClientBase"/>. Default: null.</param>
        public VoyageAiEmbeddingClient(
            string endpoint = VoyageAiProtocol.DefaultEndpoint,
            string? apiKey = null,
            LoggingModule? logging = null,
            HttpClient? httpClient = null)
            : base(endpoint, apiKey, logging ?? new LoggingModule(), httpClient)
        {
            _Header = VoyageAiProtocol.Header;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Verify connectivity and credentials by embedding a one-word input with the client's model, because VoyageAI
        /// has no model listing endpoint to probe. Returns false on an HTTP error, an unreachable server, or a timeout;
        /// rethrows only caller cancellation.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the request succeeded.</returns>
        public override async Task<bool> ValidateConnectivityAsync(CancellationToken token = default)
        {
            try
            {
                EmbeddingResponse probe = await EmbedAsync("ping", null, token).ConfigureAwait(false);
                return probe.Success;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _Logging.Debug(_Header + "connectivity probe failed: " + ex.Message);
                return false;
            }
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
        {
            VoyageAiProtocol.ApplyAuth(request, _ApiKey);
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

            string? inputType = PickRef<VoyageAiEmbeddingOptions, string>(options, Defaults, o => o.InputType);
            bool? truncation = Pick<VoyageAiEmbeddingOptions, bool>(options, Defaults, o => o.Truncation);
            int? outputDimension = Pick<VoyageAiEmbeddingOptions, int>(options, Defaults, o => o.OutputDimension);
            string? outputDtype = PickRef<VoyageAiEmbeddingOptions, string>(options, Defaults, o => o.OutputDtype);

            if (!string.IsNullOrEmpty(inputType)) body["input_type"] = inputType;
            if (truncation.HasValue) body["truncation"] = truncation.Value;
            if (outputDimension.HasValue) body["output_dimension"] = outputDimension.Value;
            if (!string.IsNullOrEmpty(outputDtype)) body["output_dtype"] = outputDtype;

            EmbeddingResponse response = new EmbeddingResponse { Model = model };
            return ExecutePostAsync(response, "embed", BuildUrl("/v1/embeddings"), body, ParseEmbeddings, token);
        }

        #endregion

        #region Private-Methods

        private void ParseEmbeddings(string responseBody, EmbeddingResponse response)
        {
            Dictionary<string, object>? responseObj = TryDeserializeObject(responseBody);
            List<Dictionary<string, object>>? data = responseObj == null ? null : ParseNestedList(responseObj, "data");
            if (data == null)
            {
                response.Error = "Response missing 'data' field";
                return;
            }

            for (int i = 0; i < data.Count; i++)
            {
                EmbeddingResult result = new EmbeddingResult { Index = TryGetInt(data[i], "index") ?? i };
                if (data[i].ContainsKey("embedding")) result.Embedding = ParseFloatArray(_Serializer.SerializeJson(data[i]["embedding"], false));
                response.Embeddings.Add(result);
            }

            response.Embeddings.Sort((a, b) => a.Index.CompareTo(b.Index));
        }

        #endregion
    }
}
