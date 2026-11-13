namespace Test.Shared
{
    using PolyPrompt.Auth;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;

    /// <summary>
    /// The capability clients a configured live provider offers. A capability the provider does not have is null, so live
    /// cases can skip it with a reason instead of expecting an exception.
    /// </summary>
    internal sealed class LiveProvider : IDisposable
    {
        #region Public-Members

        private ProviderClientBase? _Client;

        public ProviderTestConfiguration Configuration { get; }

        public ProviderClientBase? Completion { get; private set; }

        public ProviderClientBase? Embedding { get; private set; }

        public ProviderClientBase? SparseEmbedding { get; private set; }

        public ProviderClientBase? Rerank { get; private set; }

        public ProviderClientBase? Classification { get; private set; }

        public ProviderClientBase? Decision { get; private set; }

        public ProviderClientBase? Models { get; private set; }

        /// <summary>
        /// Every non-null client.
        /// </summary>
        public List<HttpClientBase> All
        {
            get
            {
                return new List<HttpClientBase?> { Completion, Embedding, SparseEmbedding, Rerank, Classification, Decision, Models }
                    .Where(client => client != null)
                    .Select(client => client!)
                    .Distinct()
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

            ProviderClientBase client;
            switch (configuration.ProviderType)
            {
                case "xai":
                    client = new XAiClient(key!, endpoint);
                    break;
                case "ollama":
                    client = new OllamaClient(endpoint, key);
                    break;
                case "openai":
                    client = new OpenAiClient(key!, endpoint);
                    break;
                case "openai-compatible":
                    client = new OpenAiCompatibleClient(endpoint, key);
                    break;
                case "azure":
                    client = new AzureOpenAiClient(endpoint, key!) { Defaults = { Completion = { Model = configuration.InferenceModel! }, Embedding = { Model = configuration.EmbeddingModel } } };
                    break;
                case "gemini":
                    client = new GeminiClient(key!, endpoint);
                    break;
                case "vertex":
                    client = new VertexAiClient(configuration.Project!, configuration.Region!, CreateVertexCredential(configuration), regionalEndpoint);
                    break;
                case "anthropic":
                    client = new AnthropicClient(key!, endpoint) { WorkspaceId = configuration.AnthropicWorkspaceId };
                    break;
                case "bedrock":
                    IAwsCredentialProvider aws = string.IsNullOrWhiteSpace(configuration.AwsAccessKeyId)
                        ? new EnvironmentAwsCredential(configuration.Region)
                        : new StaticAwsCredential(configuration.AwsAccessKeyId!, configuration.AwsSecretAccessKey!, configuration.Region!, configuration.AwsSessionToken);
                    client = new BedrockClient(configuration.Region!, aws, regionalEndpoint);
                    break;
                case "voyageai":
                    client = new VoyageAiClient(key!, endpoint);
                    break;
                case "cohere":
                    client = new CohereClient(key!, endpoint);
                    break;
                case "tei":
                    client = new TeiClient(endpoint, key);
                    break;
                case "typesafe":
                    client = new TypeSafeClient(key!, endpoint);
                    break;
                default:
                    throw new ArgumentException("Unknown provider: " + configuration.ProviderType);
            }

            // One client per provider; each capability property points at it when the provider supports the capability.
            provider._Client = client;
            if (client.Supports(Capability.Chat)) provider.Completion = client;
            if (client.Supports(Capability.Embed) && (configuration.ProviderType != "azure" || !string.IsNullOrEmpty(configuration.EmbeddingModel))) provider.Embedding = client;
            if (client.Supports(Capability.SparseEmbed)) provider.SparseEmbedding = client;
            if (client.Supports(Capability.Rerank)) provider.Rerank = client;
            if (client.Supports(Capability.Classify)) provider.Classification = client;
            if (client.Supports(Capability.Decide)) provider.Decision = client;
            if (client.Supports(Capability.Models)) provider.Models = client;

            provider.ApplyModels();
            foreach (HttpClientBase live in provider.All) live.TimeoutMs = 120000;
            return provider;
        }

        #endregion

        #region Public-Methods

        public void Dispose()
        {
            foreach (HttpClientBase client in All) client.Dispose();
        }

        #endregion

        #region Private-Methods

        private void ApplyModels()
        {
            string? inference = Configuration.InferenceModel;
            if (Completion != null && !string.IsNullOrEmpty(inference)) Completion.Defaults.Completion!.Model = inference;
            if (Decision != null && !string.IsNullOrEmpty(inference)) Decision.Defaults.Decision!.Model = inference;
            if (Embedding != null && !string.IsNullOrEmpty(Configuration.EmbeddingModel) && Configuration.ProviderType != "tei")
                Embedding.Defaults.Embedding!.Model = Configuration.EmbeddingModel;
            if (Rerank != null && !string.IsNullOrEmpty(Configuration.RerankModel)) Rerank.Defaults.Rerank!.Model = Configuration.RerankModel;
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
