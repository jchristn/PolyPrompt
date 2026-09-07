namespace PolyPrompt.Clients
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using PolyPrompt.Auth;
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using SyslogLogging;

    /// <summary>
    /// Client for Google Vertex AI (Gemini models). Vertex serves the same Gemini <c>generateContent</c>
    /// content schema as <see cref="GeminiClient"/>, so all request-body building and response/stream parsing
    /// — chat, tool chat, streaming, and reasoning (thinking budget) — are inherited unchanged. Vertex
    /// differs in three ways, which is all this class overrides:
    /// <list type="bullet">
    /// <item><description><b>Routing:</b> a project/region/publisher path
    /// (<c>/v1/projects/{project}/locations/{region}/publishers/google/models/{model}:generateContent</c>).</description></item>
    /// <item><description><b>Auth:</b> an OAuth bearer token (ADC or a service account) refreshed per request,
    /// not an API key.</description></item>
    /// <item><description><b>Embeddings:</b> the <c>:predict</c> endpoint with an <c>instances</c> body and
    /// <c>predictions[].embeddings.values</c> response, rather than AI-Studio's <c>:embedContent</c>.</description></item>
    /// </list>
    /// Model management (listing/lookup) is not exposed for Vertex and throws <see cref="NotSupportedException"/>.
    /// </summary>
    public class VertexAiClient : GeminiClient
    {
        #region Private-Members

        private const string UnsupportedModelManagement =
            "Vertex AI model management is not supported by PolyPrompt; target a specific model by name.";

        private const string DefaultEmbeddingModel = "text-embedding-004";

        private readonly string _Project;
        private readonly string _Region;
        private readonly ICredentialProvider _Credential;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new Vertex AI client.
        /// </summary>
        /// <param name="project">Google Cloud project id.</param>
        /// <param name="region">Vertex region, e.g. <c>us-central1</c>. Also selects the regional endpoint host.</param>
        /// <param name="credential">Credential provider supplying OAuth bearer tokens (ADC or service account).</param>
        /// <param name="endpoint">Optional endpoint override; defaults to <c>https://{region}-aiplatform.googleapis.com</c>.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        /// <param name="httpClient">Optional HTTP client (caller-owned when supplied).</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null or empty.</exception>
        public VertexAiClient(
            string project,
            string region,
            ICredentialProvider credential,
            string? endpoint = null,
            LoggingModule? logging = null,
            HttpClient? httpClient = null)
            : base(ResolveEndpoint(endpoint, region), apiKey: null, logging, httpClient)
        {
            if (string.IsNullOrWhiteSpace(project)) throw new ArgumentNullException(nameof(project));
            if (string.IsNullOrWhiteSpace(region)) throw new ArgumentNullException(nameof(region));

            _Header = "[VertexAI] ";
            _Project = project;
            _Region = region;
            _Credential = credential ?? throw new ArgumentNullException(nameof(credential));
        }

        private static string ResolveEndpoint(string? endpoint, string region)
        {
            if (!string.IsNullOrEmpty(endpoint)) return endpoint;
            if (string.IsNullOrWhiteSpace(region)) throw new ArgumentNullException(nameof(region));
            return "https://" + region + "-aiplatform.googleapis.com";
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override async Task<EmbeddingResponse> EmbedAsync(
            string input,
            EmbeddingOptions? options = null,
            CancellationToken token = default)
        {
            return await EmbedAsync(new List<string> { input }, options, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override async Task<EmbeddingResponse> EmbedAsync(
            List<string> inputs,
            EmbeddingOptions? options = null,
            CancellationToken token = default)
        {
            EmbeddingResponse embedResponse = new EmbeddingResponse();
            string model = options?.Model ?? Model;
            embedResponse.Model = model;

            Stopwatch sw = Stopwatch.StartNew();

            string url = BuildPredictUrl(model);
            Dictionary<string, object> requestBody = BuildPredictBody(inputs, options as VertexAiEmbeddingOptions);

            string json = _Serializer.SerializeJson(requestBody, false);
            StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

            _Logging.Debug(_Header + "POST " + url);

            try
            {
                CompletionHttpResult result = await PostAndRecordAsync(url, content, json, token).ConfigureAwait(false);
                string responseBody = result.ResponseBody;

                embedResponse.StatusCode = result.StatusCode;

                if (!result.IsSuccessStatusCode)
                {
                    _Logging.Warn(_Header + "embed request failed with status " + result.StatusCode + ": " + responseBody);
                    embedResponse.Success = false;
                    embedResponse.Error = "HTTP " + result.StatusCode + ": " + responseBody;
                    return embedResponse;
                }

                ParsePredictEmbeddings(responseBody, embedResponse);
                embedResponse.Success = true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                embedResponse.Success = false;
                embedResponse.Error = ex.Message;
            }
            finally
            {
                sw.Stop();
                embedResponse.OverallRuntimeMs = sw.ElapsedMilliseconds;
            }

            return embedResponse;
        }

        /// <summary>
        /// Not supported. Vertex AI model management is not exposed by PolyPrompt. Thrown when the method is
        /// called, not when the returned sequence is enumerated.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Never returns; always throws.</returns>
        /// <exception cref="NotSupportedException">Always thrown.</exception>
        public override IAsyncEnumerable<ModelInformation> ListModelsAsync(CancellationToken token = default)
        {
            throw new NotSupportedException(UnsupportedModelManagement);
        }

        /// <summary>
        /// Not supported. Vertex AI model management is not exposed by PolyPrompt.
        /// </summary>
        /// <param name="model">The model name to search for.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Never returns; always throws.</returns>
        /// <exception cref="NotSupportedException">Always thrown.</exception>
        public override Task<bool> ModelExistsAsync(string model, CancellationToken token = default)
        {
            throw new NotSupportedException(UnsupportedModelManagement);
        }

        /// <summary>
        /// Not supported. Vertex AI model management is not exposed by PolyPrompt.
        /// </summary>
        /// <param name="model">The model name to look up.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Never returns; always throws.</returns>
        /// <exception cref="NotSupportedException">Always thrown.</exception>
        public override Task<ModelInformation?> GetModelInformationAsync(string model, CancellationToken token = default)
        {
            throw new NotSupportedException(UnsupportedModelManagement);
        }

        /// <summary>
        /// Validate connectivity by sending a minimal embeddings <c>:predict</c> request, since Vertex model
        /// listing is not exposed. The probe uses the default embedding model and a single short word.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the provider accepted the request, false otherwise.</returns>
        /// <exception cref="OperationCanceledException">Thrown when the token is cancelled.</exception>
        public override async Task<bool> ValidateConnectivityAsync(CancellationToken token = default)
        {
            try
            {
                EmbeddingOptions probeOptions = new EmbeddingOptions { Model = DefaultEmbeddingModel };
                EmbeddingResponse probe = await EmbedAsync("ping", probeOptions, token).ConfigureAwait(false);
                return probe.Success;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string BuildGenerateContentUrl(string model, bool streaming)
        {
            string method = streaming ? ":streamGenerateContent" : ":generateContent";
            string query = streaming ? "?alt=sse" : string.Empty;
            return VertexModelBase(model) + method + query;
        }

        /// <inheritdoc />
        protected override async Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
        {
            string bearer = await _Credential.GetBearerTokenAsync(token).ConfigureAwait(false);
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bearer);
        }

        #endregion

        #region Private-Methods

        private string VertexModelBase(string model)
        {
            return _Endpoint.TrimEnd('/')
                + "/v1/projects/" + _Project
                + "/locations/" + _Region
                + "/publishers/google/models/" + model;
        }

        private string BuildPredictUrl(string model)
        {
            return VertexModelBase(model) + ":predict";
        }

        private Dictionary<string, object> BuildPredictBody(List<string> inputs, VertexAiEmbeddingOptions? vertexOptions)
        {
            List<Dictionary<string, object>> instances = new List<Dictionary<string, object>>();
            foreach (string input in inputs)
            {
                Dictionary<string, object> instance = new Dictionary<string, object>
                {
                    { "content", input }
                };

                if (vertexOptions != null)
                {
                    if (!string.IsNullOrEmpty(vertexOptions.TaskType)) instance["task_type"] = vertexOptions.TaskType;
                    if (!string.IsNullOrEmpty(vertexOptions.Title)) instance["title"] = vertexOptions.Title;
                }

                instances.Add(instance);
            }

            Dictionary<string, object> requestBody = new Dictionary<string, object>
            {
                { "instances", instances }
            };

            if (vertexOptions != null && (vertexOptions.OutputDimensionality.HasValue || vertexOptions.AutoTruncate.HasValue))
            {
                Dictionary<string, object> parameters = new Dictionary<string, object>();
                if (vertexOptions.OutputDimensionality.HasValue) parameters["outputDimensionality"] = vertexOptions.OutputDimensionality.Value;
                if (vertexOptions.AutoTruncate.HasValue) parameters["autoTruncate"] = vertexOptions.AutoTruncate.Value;
                requestBody["parameters"] = parameters;
            }

            return requestBody;
        }

        private void ParsePredictEmbeddings(string responseBody, EmbeddingResponse embedResponse)
        {
            Dictionary<string, object>? responseObj = _Serializer.DeserializeJson<Dictionary<string, object>>(responseBody);
            if (responseObj == null || !responseObj.ContainsKey("predictions"))
            {
                _Logging.Warn(_Header + "embed response missing 'predictions' field");
                return;
            }

            string predictionsJson = _Serializer.SerializeJson(responseObj["predictions"], false);
            List<Dictionary<string, object>>? predictions = _Serializer.DeserializeJson<List<Dictionary<string, object>>>(predictionsJson);
            if (predictions == null) return;

            for (int i = 0; i < predictions.Count; i++)
            {
                if (!predictions[i].ContainsKey("embeddings")) continue;

                string embeddingsJson = _Serializer.SerializeJson(predictions[i]["embeddings"], false);
                Dictionary<string, object>? embeddingsObj = _Serializer.DeserializeJson<Dictionary<string, object>>(embeddingsJson);
                if (embeddingsObj == null || !embeddingsObj.ContainsKey("values")) continue;

                string valuesJson = _Serializer.SerializeJson(embeddingsObj["values"], false);
                float[] vector = ParseFloatArray(valuesJson);

                EmbeddingResult embResult = new EmbeddingResult();
                embResult.Index = i;
                embResult.Embedding = vector;
                embedResponse.Embeddings.Add(embResult);
            }
        }

        #endregion
    }
}
