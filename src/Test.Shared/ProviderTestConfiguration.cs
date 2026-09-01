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
        /// Provider type for live tests. Valid values are openai, ollama, gemini, and anthropic.
        /// </summary>
        public string ProviderType { get; set; } = string.Empty;

        /// <summary>
        /// Provider endpoint for live tests.
        /// </summary>
        public string Endpoint { get; set; } = string.Empty;

        /// <summary>
        /// Optional provider API key. May be null when the provider does not require authentication.
        /// </summary>
        public string? ApiKey { get; set; }

        /// <summary>
        /// Optional inference model override. When null, the provider client default model is used.
        /// </summary>
        public string? InferenceModel { get; set; }

        /// <summary>
        /// Embedding model used by live embedding tests. Empty for providers with no embeddings API
        /// (Anthropic), in which case embedding assertions are skipped.
        /// </summary>
        public string EmbeddingModel { get; set; } = string.Empty;

        /// <summary>
        /// Optional Anthropic workspace identifier, required by identity-linked Anthropic API keys.
        /// Ignored by other providers.
        /// </summary>
        public string? AnthropicWorkspaceId { get; set; }

        /// <summary>
        /// Creates live provider configuration from generic or provider-specific POLYPROMPT_TEST_* environment variables.
        /// </summary>
        /// <returns>A configuration when environment variables identify a provider; otherwise null.</returns>
        /// <exception cref="ArgumentException">Thrown when environment variables contain an invalid provider configuration.</exception>
        public static ProviderTestConfiguration? FromEnvironment()
        {
            string? provider = Environment.GetEnvironmentVariable("POLYPROMPT_TEST_PROVIDER");
            string? endpoint = Environment.GetEnvironmentVariable("POLYPROMPT_TEST_ENDPOINT");

            if (!string.IsNullOrWhiteSpace(provider))
            {
                return CreateWithDefaults(
                    provider,
                    endpoint,
                    Environment.GetEnvironmentVariable("POLYPROMPT_TEST_API_KEY"),
                    Environment.GetEnvironmentVariable("POLYPROMPT_TEST_MODEL"),
                    Environment.GetEnvironmentVariable("POLYPROMPT_TEST_EMBEDDING_MODEL"));
            }

            ProviderTestConfiguration? providerSpecific = FromProviderSpecificEnvironment();
            if (providerSpecific != null)
            {
                return providerSpecific;
            }

            return null;
        }

        /// <summary>
        /// Creates live provider configuration from provider-specific POLYPROMPT_TEST_OPENAI_*, POLYPROMPT_TEST_OLLAMA_*, or POLYPROMPT_TEST_GEMINI_* environment variables.
        /// </summary>
        /// <returns>A configuration when exactly one provider-specific environment group is present; otherwise null.</returns>
        /// <exception cref="ArgumentException">Thrown when multiple provider-specific environment groups are configured.</exception>
        public static ProviderTestConfiguration? FromProviderSpecificEnvironment()
        {
            string? openAiKey = Environment.GetEnvironmentVariable("POLYPROMPT_TEST_OPENAI_API_KEY");
            string? openAiEndpoint = Environment.GetEnvironmentVariable("POLYPROMPT_TEST_OPENAI_ENDPOINT");
            string? openAiModel = Environment.GetEnvironmentVariable("POLYPROMPT_TEST_OPENAI_MODEL");
            string? openAiEmbeddingModel = Environment.GetEnvironmentVariable("POLYPROMPT_TEST_OPENAI_EMBEDDING_MODEL");
            bool hasOpenAi = !string.IsNullOrWhiteSpace(openAiKey)
                || !string.IsNullOrWhiteSpace(openAiEndpoint)
                || !string.IsNullOrWhiteSpace(openAiModel)
                || !string.IsNullOrWhiteSpace(openAiEmbeddingModel);

            string? ollamaKey = Environment.GetEnvironmentVariable("POLYPROMPT_TEST_OLLAMA_API_KEY");
            string? ollamaEndpoint = Environment.GetEnvironmentVariable("POLYPROMPT_TEST_OLLAMA_ENDPOINT");
            string? ollamaModel = Environment.GetEnvironmentVariable("POLYPROMPT_TEST_OLLAMA_MODEL");
            string? ollamaEmbeddingModel = Environment.GetEnvironmentVariable("POLYPROMPT_TEST_OLLAMA_EMBEDDING_MODEL");
            bool hasOllama = !string.IsNullOrWhiteSpace(ollamaKey)
                || !string.IsNullOrWhiteSpace(ollamaEndpoint)
                || !string.IsNullOrWhiteSpace(ollamaModel)
                || !string.IsNullOrWhiteSpace(ollamaEmbeddingModel);

            string? geminiKey = Environment.GetEnvironmentVariable("POLYPROMPT_TEST_GEMINI_API_KEY");
            string? geminiEndpoint = Environment.GetEnvironmentVariable("POLYPROMPT_TEST_GEMINI_ENDPOINT");
            string? geminiModel = Environment.GetEnvironmentVariable("POLYPROMPT_TEST_GEMINI_MODEL");
            string? geminiEmbeddingModel = Environment.GetEnvironmentVariable("POLYPROMPT_TEST_GEMINI_EMBEDDING_MODEL");
            bool hasGemini = !string.IsNullOrWhiteSpace(geminiKey)
                || !string.IsNullOrWhiteSpace(geminiEndpoint)
                || !string.IsNullOrWhiteSpace(geminiModel)
                || !string.IsNullOrWhiteSpace(geminiEmbeddingModel);

            string? anthropicKey = Environment.GetEnvironmentVariable("POLYPROMPT_TEST_ANTHROPIC_API_KEY");
            string? anthropicEndpoint = Environment.GetEnvironmentVariable("POLYPROMPT_TEST_ANTHROPIC_ENDPOINT");
            string? anthropicModel = Environment.GetEnvironmentVariable("POLYPROMPT_TEST_ANTHROPIC_MODEL");
            bool hasAnthropic = !string.IsNullOrWhiteSpace(anthropicKey)
                || !string.IsNullOrWhiteSpace(anthropicEndpoint)
                || !string.IsNullOrWhiteSpace(anthropicModel);

            int providerCount = 0;
            if (hasOpenAi) providerCount++;
            if (hasOllama) providerCount++;
            if (hasGemini) providerCount++;
            if (hasAnthropic) providerCount++;

            if (providerCount == 0)
                return null;

            if (providerCount > 1)
                throw new ArgumentException("Only one provider-specific POLYPROMPT_TEST_* environment group can be configured at a time.");

            if (hasOpenAi)
            {
                return CreateWithDefaults("openai", openAiEndpoint, openAiKey, openAiModel, openAiEmbeddingModel);
            }

            if (hasOllama)
            {
                return CreateWithDefaults("ollama", ollamaEndpoint, ollamaKey, ollamaModel, ollamaEmbeddingModel);
            }

            if (hasAnthropic)
            {
                ProviderTestConfiguration anthropic = CreateWithDefaults("anthropic", anthropicEndpoint, anthropicKey, anthropicModel, null);
                anthropic.AnthropicWorkspaceId = Environment.GetEnvironmentVariable("POLYPROMPT_TEST_ANTHROPIC_WORKSPACE_ID");
                return anthropic;
            }

            return CreateWithDefaults("gemini", geminiEndpoint, geminiKey, geminiModel, geminiEmbeddingModel);
        }

        /// <summary>
        /// Creates live provider configuration and fills in default endpoint and embedding model values.
        /// </summary>
        /// <param name="providerType">Provider type. Valid values are openai, ollama, and gemini.</param>
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
        /// Creates live provider configuration with a required endpoint and default embedding model fallback.
        /// </summary>
        /// <param name="providerType">Provider type. Valid values are openai, ollama, and gemini.</param>
        /// <param name="endpoint">Provider endpoint for live tests.</param>
        /// <param name="apiKey">Optional provider API key.</param>
        /// <param name="inferenceModel">Optional inference model override.</param>
        /// <param name="embeddingModel">Optional embedding model override.</param>
        /// <returns>A normalized provider test configuration.</returns>
        /// <exception cref="ArgumentException">Thrown when provider type or endpoint is empty, or when the provider type is unsupported.</exception>
        public static ProviderTestConfiguration Create(
            string providerType,
            string endpoint,
            string? apiKey = null,
            string? inferenceModel = null,
            string? embeddingModel = null)
        {
            if (string.IsNullOrWhiteSpace(providerType))
                throw new ArgumentException("Provider type is required.", nameof(providerType));
            if (string.IsNullOrWhiteSpace(endpoint))
                throw new ArgumentException("Endpoint is required.", nameof(endpoint));

            string normalizedProvider = NormalizeProviderType(providerType);

            return new ProviderTestConfiguration
            {
                ProviderType = normalizedProvider,
                Endpoint = endpoint,
                ApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey,
                InferenceModel = string.IsNullOrWhiteSpace(inferenceModel) ? null : inferenceModel,
                EmbeddingModel = string.IsNullOrWhiteSpace(embeddingModel) ? ResolveEmbeddingModelName(normalizedProvider) : embeddingModel!
            };
        }

        /// <summary>
        /// Resolves the default endpoint for a provider.
        /// </summary>
        /// <param name="providerType">Provider type. Valid values are openai, ollama, and gemini.</param>
        /// <returns>The default endpoint for the provider.</returns>
        /// <exception cref="ArgumentException">Thrown when the provider type is unsupported.</exception>
        public static string ResolveDefaultEndpoint(string providerType)
        {
            switch (providerType.ToLowerInvariant())
            {
                case "ollama": return DefaultOllamaEndpoint;
                case "openai": return DefaultOpenAiEndpoint;
                case "gemini": return DefaultGeminiEndpoint;
                case "anthropic": return DefaultAnthropicEndpoint;
                default: throw new ArgumentException("Unknown provider type: " + providerType, nameof(providerType));
            }
        }

        /// <summary>
        /// Resolves the default embedding model for a provider.
        /// </summary>
        /// <param name="providerType">Provider type. Valid values are openai, ollama, and gemini.</param>
        /// <returns>The default embedding model for the provider.</returns>
        /// <exception cref="ArgumentException">Thrown when the provider type is unsupported.</exception>
        public static string ResolveEmbeddingModelName(string providerType)
        {
            switch (providerType.ToLowerInvariant())
            {
                case "ollama": return "all-minilm";
                case "openai": return "text-embedding-3-small";
                case "gemini": return "text-embedding-004";
                case "anthropic": return string.Empty;
                default: throw new ArgumentException("Unknown provider type: " + providerType, nameof(providerType));
            }
        }

        private static string NormalizeProviderType(string providerType)
        {
            if (string.IsNullOrWhiteSpace(providerType))
                throw new ArgumentException("Provider type is required.", nameof(providerType));

            string normalizedProvider = providerType.Trim().ToLowerInvariant();
            if (normalizedProvider != "ollama" && normalizedProvider != "openai" && normalizedProvider != "gemini" && normalizedProvider != "anthropic")
                throw new ArgumentException("Provider type must be ollama, openai, gemini, or anthropic.", nameof(providerType));

            return normalizedProvider;
        }
    }
}
