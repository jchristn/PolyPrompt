namespace Test.Shared
{
    /// <summary>
    /// Configuration for live provider tests.
    /// </summary>
    public sealed class ProviderTestConfiguration
    {
        /// <summary>
        /// Default OpenAI-compatible endpoint used when OpenAI live tests are configured without an endpoint.
        /// </summary>
        public const string DefaultOpenAiEndpoint = "https://api.openai.com";

        /// <summary>
        /// Default Ollama endpoint used when Ollama live tests are configured without an endpoint.
        /// </summary>
        public const string DefaultOllamaEndpoint = "http://localhost:11434";

        /// <summary>
        /// Default Gemini endpoint used when Gemini live tests are configured without an endpoint.
        /// </summary>
        public const string DefaultGeminiEndpoint = "https://generativelanguage.googleapis.com";

        /// <summary>
        /// Default Anthropic endpoint used when Anthropic live tests are configured without an endpoint.
        /// </summary>
        public const string DefaultAnthropicEndpoint = "https://api.anthropic.com";

        /// <summary>
        /// Default VoyageAI endpoint used when VoyageAI live tests are configured without an endpoint.
        /// </summary>
        public const string DefaultVoyageAiEndpoint = "https://api.voyageai.com";

        /// <summary>
        /// Default Cohere endpoint used when Cohere live tests are configured without an endpoint.
        /// </summary>
        public const string DefaultCohereEndpoint = "https://api.cohere.com";

        /// <summary>
        /// Default Text Embeddings Inference endpoint used when TEI live tests are configured without an endpoint.
        /// </summary>
        public const string DefaultTeiEndpoint = "http://localhost:8080";

        /// <summary>
        /// Default TypeSafe endpoint used when TypeSafe live tests are configured without an endpoint. Point it at a local
        /// Ollaya server to test Ollaya.
        /// </summary>
        public const string DefaultTypeSafeEndpoint = "https://api.typesafe.ai";

        /// <summary>
        /// Every supported provider type.
        /// </summary>
        public static readonly IReadOnlyList<string> ProviderTypes = new List<string>
        {
            "ollama", "openai", "azure", "gemini", "vertex", "anthropic", "bedrock", "voyageai", "cohere", "tei", "typesafe"
        };

        /// <summary>
        /// Provider type for live tests: one of <see cref="ProviderTypes"/>.
        /// </summary>
        public string ProviderType { get; set; } = string.Empty;

        /// <summary>
        /// Provider endpoint for live tests. Empty for Vertex AI and Bedrock means the regional default.
        /// </summary>
        public string Endpoint { get; set; } = string.Empty;

        /// <summary>
        /// Optional provider API key. May be null when the provider does not require authentication.
        /// </summary>
        public string? ApiKey { get; set; }

        /// <summary>
        /// Optional inference (completion or decision) model override. For Azure OpenAI this is the chat deployment and is
        /// required. When null, the client default model is used.
        /// </summary>
        public string? InferenceModel { get; set; }

        /// <summary>
        /// Embedding model used by live embedding tests (for Azure OpenAI, the embedding deployment). Empty when the
        /// provider has no embeddings API or, for Azure, no embedding deployment is configured.
        /// </summary>
        public string EmbeddingModel { get; set; } = string.Empty;

        /// <summary>
        /// Optional Anthropic workspace identifier, required by identity-linked Anthropic API keys.
        /// </summary>
        public string? AnthropicWorkspaceId { get; set; }

        /// <summary>
        /// Rerank model used by the live rerank test. Empty when the provider has no rerank model setting
        /// (Text Embeddings Inference serves a single model) or no rerank API.
        /// </summary>
        public string RerankModel { get; set; } = string.Empty;

        /// <summary>
        /// Region for Vertex AI and Bedrock, for example us-central1 or us-east-1.
        /// </summary>
        public string? Region { get; set; }

        /// <summary>
        /// Google Cloud project for Vertex AI.
        /// </summary>
        public string? Project { get; set; }

        /// <summary>
        /// Path to a Google service-account key JSON file for Vertex AI. When null, Application Default Credentials are used.
        /// </summary>
        public string? CredentialsPath { get; set; }

        /// <summary>
        /// AWS access key id for Bedrock. When null, credentials are read from the standard AWS environment variables.
        /// </summary>
        public string? AwsAccessKeyId { get; set; }

        /// <summary>
        /// AWS secret access key for Bedrock.
        /// </summary>
        public string? AwsSecretAccessKey { get; set; }

        /// <summary>
        /// Optional AWS session token for Bedrock.
        /// </summary>
        public string? AwsSessionToken { get; set; }

        /// <summary>
        /// Optional Azure OpenAI api-version override.
        /// </summary>
        public string? ApiVersion { get; set; }

        /// <summary>
        /// Creates live provider configuration from generic or provider-specific POLYPROMPT_TEST_* environment variables.
        /// </summary>
        /// <returns>A configuration when environment variables identify a provider; otherwise null.</returns>
        /// <exception cref="ArgumentException">Thrown when environment variables contain an invalid provider configuration.</exception>
        public static ProviderTestConfiguration? FromEnvironment()
        {
            string? provider = Env("POLYPROMPT_TEST_PROVIDER");

            if (!string.IsNullOrWhiteSpace(provider))
            {
                ProviderTestConfiguration generic = CreateWithDefaults(
                    provider,
                    Env("POLYPROMPT_TEST_ENDPOINT"),
                    Env("POLYPROMPT_TEST_API_KEY"),
                    Env("POLYPROMPT_TEST_MODEL"),
                    Env("POLYPROMPT_TEST_EMBEDDING_MODEL"));

                string? rerankModel = Env("POLYPROMPT_TEST_RERANK_MODEL");
                if (!string.IsNullOrWhiteSpace(rerankModel)) generic.RerankModel = rerankModel;
                generic.Region = Env("POLYPROMPT_TEST_REGION") ?? generic.Region;
                generic.Project = Env("POLYPROMPT_TEST_PROJECT");
                generic.CredentialsPath = Env("POLYPROMPT_TEST_CREDENTIALS");
                generic.AwsAccessKeyId = Env("POLYPROMPT_TEST_AWS_ACCESS_KEY_ID");
                generic.AwsSecretAccessKey = Env("POLYPROMPT_TEST_AWS_SECRET_ACCESS_KEY");
                generic.AwsSessionToken = Env("POLYPROMPT_TEST_AWS_SESSION_TOKEN");
                generic.ApiVersion = Env("POLYPROMPT_TEST_API_VERSION");
                generic.Validate();
                return generic;
            }

            return FromProviderSpecificEnvironment();
        }

        /// <summary>
        /// Creates live provider configuration from a provider-specific POLYPROMPT_TEST_{PROVIDER}_* environment group.
        /// </summary>
        /// <returns>A configuration when exactly one provider-specific environment group is present; otherwise null.</returns>
        /// <exception cref="ArgumentException">Thrown when multiple provider-specific environment groups are configured.</exception>
        public static ProviderTestConfiguration? FromProviderSpecificEnvironment()
        {
            List<ProviderTestConfiguration> found = new List<ProviderTestConfiguration>();

            if (AnySet("OPENAI", "API_KEY", "ENDPOINT", "MODEL", "EMBEDDING_MODEL"))
                found.Add(CreateWithDefaults("openai", Group("OPENAI", "ENDPOINT"), Group("OPENAI", "API_KEY"), Group("OPENAI", "MODEL"), Group("OPENAI", "EMBEDDING_MODEL")));

            if (AnySet("OLLAMA", "API_KEY", "ENDPOINT", "MODEL", "EMBEDDING_MODEL"))
                found.Add(CreateWithDefaults("ollama", Group("OLLAMA", "ENDPOINT"), Group("OLLAMA", "API_KEY"), Group("OLLAMA", "MODEL"), Group("OLLAMA", "EMBEDDING_MODEL")));

            if (AnySet("GEMINI", "API_KEY", "ENDPOINT", "MODEL", "EMBEDDING_MODEL"))
                found.Add(CreateWithDefaults("gemini", Group("GEMINI", "ENDPOINT"), Group("GEMINI", "API_KEY"), Group("GEMINI", "MODEL"), Group("GEMINI", "EMBEDDING_MODEL")));

            if (AnySet("ANTHROPIC", "API_KEY", "ENDPOINT", "MODEL", "WORKSPACE_ID"))
            {
                ProviderTestConfiguration anthropic = CreateWithDefaults("anthropic", Group("ANTHROPIC", "ENDPOINT"), Group("ANTHROPIC", "API_KEY"), Group("ANTHROPIC", "MODEL"), null);
                anthropic.AnthropicWorkspaceId = Group("ANTHROPIC", "WORKSPACE_ID");
                found.Add(anthropic);
            }

            if (AnySet("VOYAGEAI", "API_KEY", "ENDPOINT", "MODEL", "EMBEDDING_MODEL", "RERANK_MODEL"))
            {
                ProviderTestConfiguration voyage = CreateWithDefaults("voyageai", Group("VOYAGEAI", "ENDPOINT"), Group("VOYAGEAI", "API_KEY"), Group("VOYAGEAI", "MODEL"), Group("VOYAGEAI", "EMBEDDING_MODEL"));
                if (Group("VOYAGEAI", "RERANK_MODEL") is string voyageRerank) voyage.RerankModel = voyageRerank;
                found.Add(voyage);
            }

            if (AnySet("COHERE", "API_KEY", "ENDPOINT", "MODEL", "EMBEDDING_MODEL", "RERANK_MODEL"))
            {
                ProviderTestConfiguration cohere = CreateWithDefaults("cohere", Group("COHERE", "ENDPOINT"), Group("COHERE", "API_KEY"), Group("COHERE", "MODEL"), Group("COHERE", "EMBEDDING_MODEL"));
                if (Group("COHERE", "RERANK_MODEL") is string cohereRerank) cohere.RerankModel = cohereRerank;
                found.Add(cohere);
            }

            if (AnySet("TEI", "API_KEY", "ENDPOINT"))
                found.Add(CreateWithDefaults("tei", Group("TEI", "ENDPOINT"), Group("TEI", "API_KEY"), null, null));

            if (AnySet("AZURE", "API_KEY", "ENDPOINT", "MODEL", "EMBEDDING_MODEL", "API_VERSION"))
            {
                ProviderTestConfiguration azure = CreateWithDefaults("azure", Group("AZURE", "ENDPOINT"), Group("AZURE", "API_KEY"), Group("AZURE", "MODEL"), Group("AZURE", "EMBEDDING_MODEL"));
                azure.ApiVersion = Group("AZURE", "API_VERSION");
                found.Add(azure);
            }

            if (AnySet("VERTEX", "PROJECT", "REGION", "CREDENTIALS", "ENDPOINT", "MODEL", "EMBEDDING_MODEL"))
            {
                ProviderTestConfiguration vertex = CreateWithDefaults("vertex", Group("VERTEX", "ENDPOINT"), null, Group("VERTEX", "MODEL"), Group("VERTEX", "EMBEDDING_MODEL"));
                vertex.Project = Group("VERTEX", "PROJECT");
                vertex.Region = Group("VERTEX", "REGION") ?? vertex.Region;
                vertex.CredentialsPath = Group("VERTEX", "CREDENTIALS");
                found.Add(vertex);
            }

            if (AnySet("BEDROCK", "ACCESS_KEY_ID", "SECRET_ACCESS_KEY", "SESSION_TOKEN", "REGION", "ENDPOINT", "MODEL", "EMBEDDING_MODEL", "RERANK_MODEL"))
            {
                ProviderTestConfiguration bedrock = CreateWithDefaults("bedrock", Group("BEDROCK", "ENDPOINT"), null, Group("BEDROCK", "MODEL"), Group("BEDROCK", "EMBEDDING_MODEL"));
                bedrock.AwsAccessKeyId = Group("BEDROCK", "ACCESS_KEY_ID");
                bedrock.AwsSecretAccessKey = Group("BEDROCK", "SECRET_ACCESS_KEY");
                bedrock.AwsSessionToken = Group("BEDROCK", "SESSION_TOKEN");
                bedrock.Region = Group("BEDROCK", "REGION") ?? bedrock.Region;
                if (Group("BEDROCK", "RERANK_MODEL") is string bedrockRerank) bedrock.RerankModel = bedrockRerank;
                found.Add(bedrock);
            }

            if (AnySet("TYPESAFE", "API_KEY", "ENDPOINT", "MODEL"))
                found.Add(CreateWithDefaults("typesafe", Group("TYPESAFE", "ENDPOINT"), Group("TYPESAFE", "API_KEY"), Group("TYPESAFE", "MODEL"), null));

            if (found.Count == 0) return null;
            if (found.Count > 1)
                throw new ArgumentException("Only one provider-specific POLYPROMPT_TEST_* environment group can be configured at a time.");

            found[0].Validate();
            return found[0];
        }

        /// <summary>
        /// Creates live provider configuration and fills in default endpoint, embedding model, rerank model, and region values.
        /// </summary>
        /// <param name="providerType">Provider type: one of <see cref="ProviderTypes"/>.</param>
        /// <param name="endpoint">Optional provider endpoint. When null or whitespace, the provider default endpoint is used.</param>
        /// <param name="apiKey">Optional provider API key.</param>
        /// <param name="inferenceModel">Optional inference model override.</param>
        /// <param name="embeddingModel">Optional embedding model override.</param>
        /// <returns>A normalized provider test configuration.</returns>
        /// <exception cref="ArgumentException">Thrown when the provider type is empty or unsupported.</exception>
        public static ProviderTestConfiguration CreateWithDefaults(
            string providerType,
            string? endpoint = null,
            string? apiKey = null,
            string? inferenceModel = null,
            string? embeddingModel = null)
        {
            string normalizedProvider = NormalizeProviderType(providerType);
            string resolvedEndpoint = string.IsNullOrWhiteSpace(endpoint)
                ? ResolveDefaultEndpoint(normalizedProvider)
                : endpoint!;

            return Create(normalizedProvider, resolvedEndpoint, apiKey, inferenceModel, embeddingModel);
        }

        /// <summary>
        /// Creates live provider configuration with an explicit endpoint and default embedding and rerank model fallbacks.
        /// </summary>
        /// <param name="providerType">Provider type: one of <see cref="ProviderTypes"/>.</param>
        /// <param name="endpoint">Provider endpoint for live tests. May be empty only for Vertex AI and Bedrock (regional default).</param>
        /// <param name="apiKey">Optional provider API key.</param>
        /// <param name="inferenceModel">Optional inference model override.</param>
        /// <param name="embeddingModel">Optional embedding model override.</param>
        /// <returns>A normalized provider test configuration.</returns>
        /// <exception cref="ArgumentException">Thrown when provider type or a required endpoint is empty, or when the provider type is unsupported.</exception>
        public static ProviderTestConfiguration Create(
            string providerType,
            string endpoint,
            string? apiKey = null,
            string? inferenceModel = null,
            string? embeddingModel = null)
        {
            if (string.IsNullOrWhiteSpace(providerType))
                throw new ArgumentException("Provider type is required.", nameof(providerType));

            string normalizedProvider = NormalizeProviderType(providerType);
            bool regional = normalizedProvider == "vertex" || normalizedProvider == "bedrock";
            if (string.IsNullOrWhiteSpace(endpoint) && !regional)
                throw new ArgumentException("Endpoint is required.", nameof(endpoint));

            return new ProviderTestConfiguration
            {
                ProviderType = normalizedProvider,
                Endpoint = endpoint ?? string.Empty,
                ApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey,
                InferenceModel = string.IsNullOrWhiteSpace(inferenceModel) ? null : inferenceModel,
                EmbeddingModel = string.IsNullOrWhiteSpace(embeddingModel) ? ResolveEmbeddingModelName(normalizedProvider) : embeddingModel!,
                RerankModel = ResolveRerankModelName(normalizedProvider),
                Region = normalizedProvider == "vertex" ? "us-central1" : normalizedProvider == "bedrock" ? "us-east-1" : null
            };
        }

        /// <summary>
        /// Resolves the default rerank model for a provider.
        /// </summary>
        /// <param name="providerType">Provider type.</param>
        /// <returns>The default rerank model, or empty when the provider has no rerank model setting.</returns>
        /// <exception cref="ArgumentException">Thrown when the provider type is unsupported.</exception>
        public static string ResolveRerankModelName(string providerType)
        {
            switch (NormalizeProviderType(providerType))
            {
                case "cohere": return "rerank-v3.5";
                case "voyageai": return "rerank-2.5";
                case "bedrock": return "cohere.rerank-v3-5:0";
                default: return string.Empty;
            }
        }

        /// <summary>
        /// Resolves the default endpoint for a provider. Empty for Vertex AI and Bedrock, whose clients derive a regional
        /// endpoint, and for Azure OpenAI, whose resource endpoint must be supplied.
        /// </summary>
        /// <param name="providerType">Provider type.</param>
        /// <returns>The default endpoint for the provider.</returns>
        /// <exception cref="ArgumentException">Thrown when the provider type is unsupported or has no default (Azure).</exception>
        public static string ResolveDefaultEndpoint(string providerType)
        {
            switch (NormalizeProviderType(providerType))
            {
                case "ollama": return DefaultOllamaEndpoint;
                case "openai": return DefaultOpenAiEndpoint;
                case "gemini": return DefaultGeminiEndpoint;
                case "anthropic": return DefaultAnthropicEndpoint;
                case "voyageai": return DefaultVoyageAiEndpoint;
                case "cohere": return DefaultCohereEndpoint;
                case "tei": return DefaultTeiEndpoint;
                case "typesafe": return DefaultTypeSafeEndpoint;
                case "vertex":
                case "bedrock":
                    return string.Empty;
                default:
                    throw new ArgumentException("Azure OpenAI live tests require the resource endpoint.", nameof(providerType));
            }
        }

        /// <summary>
        /// Resolves the default embedding model for a provider.
        /// </summary>
        /// <param name="providerType">Provider type.</param>
        /// <returns>The default embedding model, or empty when the provider has no embeddings API or needs an explicit deployment.</returns>
        /// <exception cref="ArgumentException">Thrown when the provider type is unsupported.</exception>
        public static string ResolveEmbeddingModelName(string providerType)
        {
            switch (NormalizeProviderType(providerType))
            {
                case "ollama": return "all-minilm";
                case "openai": return "text-embedding-3-small";
                case "gemini": return "gemini-embedding-001";
                case "vertex": return "text-embedding-005";
                case "bedrock": return "amazon.titan-embed-text-v2:0";
                case "voyageai": return "voyage-3.5";
                case "cohere": return "embed-v4.0";
                default: return string.Empty;
            }
        }

        /// <summary>
        /// Check the provider-specific settings that have no default.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown when a required setting is missing.</exception>
        public void Validate()
        {
            if (ProviderType == "azure" && string.IsNullOrWhiteSpace(InferenceModel))
                throw new ArgumentException("Azure OpenAI live tests require the chat deployment as the model.");
            if (ProviderType == "vertex" && string.IsNullOrWhiteSpace(Project))
                throw new ArgumentException("Vertex AI live tests require the Google Cloud project.");
            if (ProviderType == "bedrock" && string.IsNullOrWhiteSpace(AwsAccessKeyId) != string.IsNullOrWhiteSpace(AwsSecretAccessKey))
                throw new ArgumentException("Bedrock live tests require both the AWS access key id and secret access key, or neither (to use the AWS environment variables).");
        }

        /// <summary>
        /// Every environment variable <see cref="FromEnvironment"/> reads. Tests that manipulate the environment save,
        /// clear, and restore these.
        /// </summary>
        public static IReadOnlyList<string> EnvironmentVariableNames
        {
            get
            {
                List<string> names = new List<string>
                {
                    "POLYPROMPT_TEST_PROVIDER", "POLYPROMPT_TEST_ENDPOINT", "POLYPROMPT_TEST_API_KEY", "POLYPROMPT_TEST_MODEL",
                    "POLYPROMPT_TEST_EMBEDDING_MODEL", "POLYPROMPT_TEST_RERANK_MODEL", "POLYPROMPT_TEST_REGION", "POLYPROMPT_TEST_PROJECT",
                    "POLYPROMPT_TEST_CREDENTIALS", "POLYPROMPT_TEST_AWS_ACCESS_KEY_ID", "POLYPROMPT_TEST_AWS_SECRET_ACCESS_KEY",
                    "POLYPROMPT_TEST_AWS_SESSION_TOKEN", "POLYPROMPT_TEST_API_VERSION"
                };

                Dictionary<string, string[]> groups = new Dictionary<string, string[]>
                {
                    { "OPENAI", new[] { "API_KEY", "ENDPOINT", "MODEL", "EMBEDDING_MODEL" } },
                    { "OLLAMA", new[] { "API_KEY", "ENDPOINT", "MODEL", "EMBEDDING_MODEL" } },
                    { "GEMINI", new[] { "API_KEY", "ENDPOINT", "MODEL", "EMBEDDING_MODEL" } },
                    { "ANTHROPIC", new[] { "API_KEY", "ENDPOINT", "MODEL", "WORKSPACE_ID" } },
                    { "VOYAGEAI", new[] { "API_KEY", "ENDPOINT", "MODEL", "EMBEDDING_MODEL", "RERANK_MODEL" } },
                    { "COHERE", new[] { "API_KEY", "ENDPOINT", "MODEL", "EMBEDDING_MODEL", "RERANK_MODEL" } },
                    { "TEI", new[] { "API_KEY", "ENDPOINT" } },
                    { "AZURE", new[] { "API_KEY", "ENDPOINT", "MODEL", "EMBEDDING_MODEL", "API_VERSION" } },
                    { "VERTEX", new[] { "PROJECT", "REGION", "CREDENTIALS", "ENDPOINT", "MODEL", "EMBEDDING_MODEL" } },
                    { "BEDROCK", new[] { "ACCESS_KEY_ID", "SECRET_ACCESS_KEY", "SESSION_TOKEN", "REGION", "ENDPOINT", "MODEL", "EMBEDDING_MODEL", "RERANK_MODEL" } },
                    { "TYPESAFE", new[] { "API_KEY", "ENDPOINT", "MODEL" } },
                };

                foreach (KeyValuePair<string, string[]> group in groups)
                {
                    foreach (string suffix in group.Value) names.Add("POLYPROMPT_TEST_" + group.Key + "_" + suffix);
                }

                return names;
            }
        }

        private static string NormalizeProviderType(string providerType)
        {
            if (string.IsNullOrWhiteSpace(providerType))
                throw new ArgumentException("Provider type is required.", nameof(providerType));

            string normalizedProvider = providerType.Trim().ToLowerInvariant();
            if (!ProviderTypes.Contains(normalizedProvider))
                throw new ArgumentException("Provider type must be one of: " + string.Join(", ", ProviderTypes) + ".", nameof(providerType));

            return normalizedProvider;
        }

        private static string? Env(string name)
        {
            string? value = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static string? Group(string provider, string suffix)
        {
            return Env("POLYPROMPT_TEST_" + provider + "_" + suffix);
        }

        private static bool AnySet(string provider, params string[] suffixes)
        {
            return suffixes.Any(suffix => Group(provider, suffix) != null);
        }
    }
}
