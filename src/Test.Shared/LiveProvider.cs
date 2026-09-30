namespace Test.Shared
{
    using PolyPrompt.Auth;
    using PolyPrompt.Clients;

    /// <summary>
    /// The capability clients a configured live provider offers. A capability the provider does not have is null, so live
    /// cases can skip it with a reason instead of expecting an exception.
    /// </summary>
    internal sealed class LiveProvider : IDisposable
    {
        #region Public-Members

        public ProviderTestConfiguration Configuration { get; }

        public CompletionClientBase? Completion { get; private set; }

        public EmbeddingClientBase? Embedding { get; private set; }

        public SparseEmbeddingClientBase? SparseEmbedding { get; private set; }

        public RerankClientBase? Rerank { get; private set; }

        public ClassificationClientBase? Classification { get; private set; }

        public DecisionClientBase? Decision { get; private set; }

        public ModelClientBase? Models { get; private set; }

        /// <summary>
        /// Every non-null client.
        /// </summary>
        public List<ClientBase> All
        {
            get
            {
                return new List<ClientBase?> { Completion, Embedding, SparseEmbedding, Rerank, Classification, Decision, Models }
                    .Where(client => client != null)
                    .Select(client => client!)
                    .ToList();
            }
        }

        #endregion

        #region Constructors-and-Factories

        private LiveProvider(ProviderTestConfiguration configuration)
        {
            Configuration = configuration;
        }

        /// <summary>
        /// Create the clients for a configured provider.
        /// </summary>
        /// <param name="configuration">Live configuration.</param>
        /// <param name="endpointOverride">Optional endpoint to use instead of the configured one (for unreachable-endpoint checks).</param>
        /// <returns>The provider's clients.</returns>
        public static LiveProvider Create(ProviderTestConfiguration configuration, string? endpointOverride = null)
        {
            LiveProvider provider = new LiveProvider(configuration);
            string endpoint = endpointOverride ?? configuration.Endpoint;
            string? regionalEndpoint = string.IsNullOrEmpty(endpoint) ? null : endpoint;
            string? key = configuration.ApiKey;

            switch (configuration.ProviderType)
            {
                case "ollama":
                    provider.Completion = new OllamaCompletionClient(endpoint, key);
                    provider.Embedding = new OllamaEmbeddingClient(endpoint, key);
                    provider.Models = new OllamaModelClient(endpoint, key);
                    break;

                case "openai":
                    provider.Completion = new OpenAiCompletionClient(endpoint, key);
                    provider.Embedding = new OpenAiEmbeddingClient(endpoint, key);
                    provider.Models = new OpenAiModelClient(endpoint, key);
                    break;

                case "azure":
                    provider.Completion = new AzureOpenAiCompletionClient(endpoint, configuration.InferenceModel!, key!, configuration.ApiVersion);
                    if (!string.IsNullOrEmpty(configuration.EmbeddingModel))
                        provider.Embedding = new AzureOpenAiEmbeddingClient(endpoint, configuration.EmbeddingModel, key!, configuration.ApiVersion);
                    provider.Models = new AzureOpenAiModelClient(endpoint, key!, configuration.ApiVersion);
                    break;

                case "gemini":
                    provider.Completion = new GeminiCompletionClient(endpoint, key);
                    provider.Embedding = new GeminiEmbeddingClient(endpoint, key);
                    provider.Models = new GeminiModelClient(endpoint, key);
                    break;

                case "vertex":
                    ICredentialProvider credential = CreateVertexCredential(configuration);
                    provider.Completion = new VertexAiCompletionClient(configuration.Project!, configuration.Region!, credential, regionalEndpoint);
                    provider.Embedding = new VertexAiEmbeddingClient(configuration.Project!, configuration.Region!, credential, regionalEndpoint);
                    break;

                case "anthropic":
                    provider.Completion = new AnthropicCompletionClient(endpoint, key) { WorkspaceId = configuration.AnthropicWorkspaceId };
                    provider.Models = new AnthropicModelClient(endpoint, key) { WorkspaceId = configuration.AnthropicWorkspaceId };
                    break;

                case "bedrock":
                    IAwsCredentialProvider aws = string.IsNullOrWhiteSpace(configuration.AwsAccessKeyId)
                        ? new EnvironmentAwsCredential(configuration.Region)
                        : new StaticAwsCredential(configuration.AwsAccessKeyId!, configuration.AwsSecretAccessKey!, configuration.Region!, configuration.AwsSessionToken);
                    provider.Completion = new BedrockCompletionClient(aws, configuration.Region!, regionalEndpoint);
                    provider.Embedding = new BedrockEmbeddingClient(aws, configuration.Region!, regionalEndpoint);
                    provider.Rerank = new BedrockRerankClient(aws, configuration.Region!, regionalEndpoint);
                    provider.Models = new BedrockModelClient(aws, configuration.Region!, regionalEndpoint);
                    break;

                case "voyageai":
                    provider.Embedding = new VoyageAiEmbeddingClient(endpoint, key);
                    provider.Rerank = new VoyageAiRerankClient(endpoint, key);
                    break;

                case "cohere":
                    provider.Completion = new CohereCompletionClient(endpoint, key);
                    provider.Embedding = new CohereEmbeddingClient(endpoint, key);
                    provider.Rerank = new CohereRerankClient(endpoint, key);
                    provider.Classification = new CohereClassificationClient(endpoint, key);
                    provider.Models = new CohereModelClient(endpoint, key);
                    break;

                case "tei":
                    provider.Embedding = new TeiEmbeddingClient(endpoint, key);
                    provider.SparseEmbedding = new TeiSparseEmbeddingClient(endpoint, key);
                    provider.Rerank = new TeiRerankClient(endpoint, key);
                    provider.Classification = new TeiClassificationClient(endpoint, key);
                    provider.Models = new TeiModelClient(endpoint, key);
                    break;

                case "typesafe":
                    provider.Decision = new TypeSafeDecisionClient(endpoint, key);
                    break;

                default:
                    throw new ArgumentException("Unknown provider: " + configuration.ProviderType);
            }

            provider.ApplyModels();
            foreach (ClientBase client in provider.All) client.TimeoutMs = 120000;
            return provider;
        }

        #endregion

        #region Public-Methods

        public void Dispose()
        {
            foreach (ClientBase client in All) client.Dispose();
        }

        #endregion

        #region Private-Methods

        private void ApplyModels()
        {
            string? inference = Configuration.InferenceModel;
            if (Completion != null && !string.IsNullOrEmpty(inference)) Completion.Model = inference;
            if (Decision != null && !string.IsNullOrEmpty(inference)) Decision.Model = inference;
            if (Embedding != null && !string.IsNullOrEmpty(Configuration.EmbeddingModel) && Configuration.ProviderType != "tei")
                Embedding.Model = Configuration.EmbeddingModel;
            if (Rerank != null && !string.IsNullOrEmpty(Configuration.RerankModel)) Rerank.Model = Configuration.RerankModel;
        }

        private static ICredentialProvider CreateVertexCredential(ProviderTestConfiguration configuration)
        {
            if (!string.IsNullOrWhiteSpace(configuration.CredentialsPath))
                return ServiceAccountCredential.FromJson(File.ReadAllText(configuration.CredentialsPath));
            return new AdcCredential();
        }

        #endregion
    }
}
