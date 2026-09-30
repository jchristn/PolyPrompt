namespace PolyPrompt.Clients
{
    using System.Diagnostics;
    using PolyPrompt.Auth;
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using SyslogLogging;

    /// <summary>
    /// AWS Bedrock embedding client using <c>InvokeModel</c>. Cohere embedding models (<c>cohere.*</c>) embed a batch in one
    /// request; Amazon Titan models embed one input per request, so a batch of N inputs sends N requests. Requests are signed
    /// with AWS Signature Version 4.
    /// </summary>
    public class BedrockEmbeddingClient : EmbeddingClientBase
    {
        #region Private-Members

        private readonly IAwsCredentialProvider _CredentialProvider;
        private readonly string _Region;
        private readonly string _ControlPlaneEndpoint;

        #endregion

        #region Public-Members

        /// <summary>
        /// Client-wide default settings, including Bedrock settings. Default model: amazon.titan-embed-text-v2:0.
        /// </summary>
        public override BedrockEmbeddingOptions Defaults { get; } = new BedrockEmbeddingOptions { Model = "amazon.titan-embed-text-v2:0" };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new Bedrock embedding client.
        /// </summary>
        /// <param name="credentialProvider">Resolves AWS credentials to sign each request.</param>
        /// <param name="region">AWS region, for example <c>us-east-1</c>.</param>
        /// <param name="endpoint">Optional endpoint override. Default: <c>https://bedrock-runtime.{region}.amazonaws.com</c>.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        /// <param name="httpClient">Optional HTTP client; see <see cref="ClientBase"/>. Default: null.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null or empty.</exception>
        public BedrockEmbeddingClient(
            IAwsCredentialProvider credentialProvider,
            string region,
            string? endpoint = null,
            LoggingModule? logging = null,
            HttpClient? httpClient = null)
            : base(BedrockProtocol.ResolveRuntimeEndpoint(endpoint, region), null, logging ?? new LoggingModule(), httpClient)
        {
            _CredentialProvider = credentialProvider ?? throw new ArgumentNullException(nameof(credentialProvider));
            _Region = region;
            _ControlPlaneEndpoint = BedrockProtocol.ResolveControlPlaneEndpoint(endpoint, region);
            _Header = BedrockProtocol.Header;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override Task<bool> ValidateConnectivityAsync(CancellationToken token = default)
        {
            return ProbeAsync(_ControlPlaneEndpoint.TrimEnd('/') + "/foundation-models", token);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
        {
            BedrockProtocol.Sign(request, body, _CredentialProvider, _Region);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override async Task<EmbeddingResponse> EmbedCoreAsync(List<string> inputs, string? model, EmbeddingOptions? options, CancellationToken token)
        {
            if (model!.StartsWith("cohere.", StringComparison.OrdinalIgnoreCase))
            {
                string? inputType = PickRef<BedrockEmbeddingOptions, string>(options, Defaults, o => o.InputType);
                Dictionary<string, object> body = new Dictionary<string, object>
                {
                    { "texts", inputs },
                    { "input_type", string.IsNullOrEmpty(inputType) ? "search_document" : inputType }
                };

                EmbeddingResponse cohereResponse = new EmbeddingResponse { Model = model };
                return await ExecutePostAsync(cohereResponse, "embed", BedrockProtocol.InvokeUrl(_Endpoint, model), body, ParseCohere, token).ConfigureAwait(false);
            }

            // Amazon Titan (and other single-input families): one InvokeModel per input.
            int? dimensions = Pick<BedrockEmbeddingOptions, int>(options, Defaults, o => o.Dimensions);
            bool? normalize = Pick<BedrockEmbeddingOptions, bool>(options, Defaults, o => o.Normalize);

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
