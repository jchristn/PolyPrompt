namespace PolyPrompt.Clients
{
    using PolyPrompt.Auth;
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using SyslogLogging;

    /// <summary>
    /// AWS Bedrock rerank client using <c>InvokeModel</c> with a hosted rerank model. Cohere rerank models (<c>cohere.*</c>)
    /// are sent with <c>api_version: 2</c> as Bedrock requires; other rerank models (for example <c>amazon.rerank-v1:0</c>)
    /// omit it. Requests are signed with AWS Signature Version 4.
    /// </summary>
    public class BedrockRerankClient : RerankClientBase
    {
        #region Private-Members

        private readonly IAwsCredentialProvider _CredentialProvider;
        private readonly string _Region;
        private readonly string _ControlPlaneEndpoint;

        #endregion

        #region Public-Members

        /// <summary>
        /// Client-wide default settings, including Bedrock settings. Default model: cohere.rerank-v3-5:0.
        /// </summary>
        public override BedrockRerankOptions Defaults { get; } = new BedrockRerankOptions { Model = "cohere.rerank-v3-5:0" };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new Bedrock rerank client.
        /// </summary>
        /// <param name="credentialProvider">Resolves AWS credentials to sign each request.</param>
        /// <param name="region">AWS region, for example <c>us-east-1</c>.</param>
        /// <param name="endpoint">Optional endpoint override. Default: <c>https://bedrock-runtime.{region}.amazonaws.com</c>.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        /// <param name="httpClient">Optional HTTP client; see <see cref="ClientBase"/>. Default: null.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null or empty.</exception>
        public BedrockRerankClient(
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
        protected override Task<RerankResponse> RerankCoreAsync(string query, List<string> documents, string? model, int? topN, RerankOptions? options, CancellationToken token)
        {
            Dictionary<string, object> body = new Dictionary<string, object>
            {
                { "query", query },
                { "documents", documents }
            };

            if (topN.HasValue) body["top_n"] = topN.Value;
            if (model!.StartsWith("cohere.", StringComparison.OrdinalIgnoreCase)) body["api_version"] = 2;

            int? maxTokensPerDoc = Pick<BedrockRerankOptions, int>(options, Defaults, o => o.MaxTokensPerDoc);
            if (maxTokensPerDoc.HasValue) body["max_tokens_per_doc"] = maxTokensPerDoc.Value;

            RerankResponse response = new RerankResponse { Model = model };
            return ExecutePostAsync(response, "rerank", BedrockProtocol.InvokeUrl(_Endpoint, model), body, ParseResults, token);
        }

        #endregion

        #region Private-Methods

        private void ParseResults(string responseBody, RerankResponse response)
        {
            Dictionary<string, object>? responseObj = TryDeserializeObject(responseBody);
            List<Dictionary<string, object>>? results = responseObj == null ? null : ParseNestedList(responseObj, "results");
            if (results == null)
            {
                response.Error = "Response missing 'results' field";
                return;
            }

            foreach (Dictionary<string, object> item in results)
            {
                int? index = TryGetInt(item, "index");
                double? score = TryGetDouble(item, "relevance_score");
                if (index.HasValue && score.HasValue) response.Results.Add(new RerankResult { Index = index.Value, Score = score.Value });
            }
        }

        #endregion
    }
}
