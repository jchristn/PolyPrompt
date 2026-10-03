namespace PolyPrompt.Clients
{
    using System.Net.Http.Headers;
    using PolyPrompt.Auth;
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using PolyPrompt.Telemetry;
    using SyslogLogging;

    /// <summary>
    /// Google Vertex AI embedding client using the <c>:predict</c> endpoint, which takes an <c>instances</c> array and returns
    /// <c>predictions[].embeddings.values</c>. Authenticates with an OAuth bearer token refreshed per request.
    /// </summary>
    public class VertexAiEmbeddingClient : EmbeddingClientBase
    {
        #region Private-Members

        private readonly string _Project;
        private readonly string _Region;
        private readonly ICredentialProvider _Credential;

        #endregion

        #region Public-Members

        /// <summary>
        /// Client-wide default settings, including Vertex settings. Default model: text-embedding-005.
        /// </summary>
        public override VertexAiEmbeddingOptions Defaults { get; } = new VertexAiEmbeddingOptions { Model = "text-embedding-005" };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new Vertex AI embedding client.
        /// </summary>
        /// <param name="project">Google Cloud project id.</param>
        /// <param name="region">Vertex region, for example <c>us-central1</c>. Also selects the regional endpoint host.</param>
        /// <param name="credential">Credential provider supplying OAuth bearer tokens (ADC or a service account).</param>
        /// <param name="endpoint">Optional endpoint override. Default: <c>https://{region}-aiplatform.googleapis.com</c>.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        /// <param name="httpClient">Optional HTTP client; see <see cref="ClientBase"/>. Default: null.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null or empty.</exception>
        public VertexAiEmbeddingClient(
            string project,
            string region,
            ICredentialProvider credential,
            string? endpoint = null,
            LoggingModule? logging = null,
            HttpClient? httpClient = null)
            : base(GeminiProtocol.ResolveVertexEndpoint(endpoint, region), null, logging ?? new LoggingModule(), httpClient)
        {
            if (string.IsNullOrWhiteSpace(project)) throw new ArgumentNullException(nameof(project));
            _Project = project;
            _Region = region;
            _Credential = credential ?? throw new ArgumentNullException(nameof(credential));
            _Header = GeminiProtocol.VertexHeader;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Verify that Vertex AI is reachable and the credential is accepted, by embedding a one-word input with the
        /// client's model. Returns false on an HTTP error, an unreachable server, a timeout, or a credential failure;
        /// rethrows only caller cancellation.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the request succeeded.</returns>
        public override async Task<bool> ValidateConnectivityAsync(CancellationToken token = default)
        {
            return await InstrumentBoolAsync(PolyPromptTelemetryNames.OperationValidateConnectivity, null, async () =>
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
            }, token).ConfigureAwait(false);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
        {
            string bearer = await _Credential.GetBearerTokenAsync(token).ConfigureAwait(false);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        /// <inheritdoc />
        protected override Task<EmbeddingResponse> EmbedCoreAsync(List<string> inputs, string? model, EmbeddingOptions? options, CancellationToken token)
        {
            string? taskType = PickRef<VertexAiEmbeddingOptions, string>(options, Defaults, o => o.TaskType);
            string? title = PickRef<VertexAiEmbeddingOptions, string>(options, Defaults, o => o.Title);
            int? outputDimensionality = Pick<VertexAiEmbeddingOptions, int>(options, Defaults, o => o.OutputDimensionality);
            bool? autoTruncate = Pick<VertexAiEmbeddingOptions, bool>(options, Defaults, o => o.AutoTruncate);

            List<Dictionary<string, object>> instances = new List<Dictionary<string, object>>();
            foreach (string input in inputs)
            {
                Dictionary<string, object> instance = new Dictionary<string, object> { { "content", input } };
                if (!string.IsNullOrEmpty(taskType)) instance["task_type"] = taskType;
                if (!string.IsNullOrEmpty(title)) instance["title"] = title;
                instances.Add(instance);
            }

            Dictionary<string, object> body = new Dictionary<string, object> { { "instances", instances } };

            if (outputDimensionality.HasValue || autoTruncate.HasValue)
            {
                Dictionary<string, object> parameters = new Dictionary<string, object>();
                if (outputDimensionality.HasValue) parameters["outputDimensionality"] = outputDimensionality.Value;
                if (autoTruncate.HasValue) parameters["autoTruncate"] = autoTruncate.Value;
                body["parameters"] = parameters;
            }

            string url = GeminiProtocol.VertexModelUrl(_Endpoint, _Project, _Region, model!) + ":predict";
            EmbeddingResponse response = new EmbeddingResponse { Model = model };
            return ExecutePostAsync(response, "embed", url, body, ParsePredictions, token);
        }

        #endregion

        #region Private-Methods

        private void ParsePredictions(string responseBody, EmbeddingResponse response)
        {
            Dictionary<string, object>? responseObj = TryDeserializeObject(responseBody);
            List<Dictionary<string, object>>? predictions = responseObj == null ? null : ParseNestedList(responseObj, "predictions");
            if (predictions == null)
            {
                response.Error = "Response missing 'predictions' field";
                return;
            }

            for (int i = 0; i < predictions.Count; i++)
            {
                Dictionary<string, object>? embeddings = ParseNestedObject(predictions[i], "embeddings");
                if (embeddings == null || !embeddings.ContainsKey("values")) continue;

                response.Embeddings.Add(new EmbeddingResult { Index = i, Embedding = ParseFloatArray(_Serializer.SerializeJson(embeddings["values"], false)) });
            }
        }

        #endregion
    }
}
