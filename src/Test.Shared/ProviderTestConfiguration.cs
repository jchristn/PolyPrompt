namespace Test.Shared
{
    /// <summary>
    /// Configuration for live provider tests.
    /// </summary>
    public sealed class ProviderTestConfiguration
    {
        public string ProviderType { get; set; } = string.Empty;
        public string Endpoint { get; set; } = string.Empty;
        public string? ApiKey { get; set; }
        public string? InferenceModel { get; set; }
        public string EmbeddingModel { get; set; } = string.Empty;

        public static ProviderTestConfiguration? FromEnvironment()
        {
            string? provider = Environment.GetEnvironmentVariable("POLYPROMPT_TEST_PROVIDER");
            string? endpoint = Environment.GetEnvironmentVariable("POLYPROMPT_TEST_ENDPOINT");

            if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(endpoint))
                return null;

            return Create(
                provider,
                endpoint,
                Environment.GetEnvironmentVariable("POLYPROMPT_TEST_API_KEY"),
                Environment.GetEnvironmentVariable("POLYPROMPT_TEST_MODEL"),
                Environment.GetEnvironmentVariable("POLYPROMPT_TEST_EMBEDDING_MODEL"));
        }

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

            string normalizedProvider = providerType.Trim().ToLowerInvariant();
            if (normalizedProvider != "ollama" && normalizedProvider != "openai" && normalizedProvider != "gemini")
                throw new ArgumentException("Provider type must be ollama, openai, or gemini.", nameof(providerType));

            return new ProviderTestConfiguration
            {
                ProviderType = normalizedProvider,
                Endpoint = endpoint,
                ApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey,
                InferenceModel = string.IsNullOrWhiteSpace(inferenceModel) ? null : inferenceModel,
                EmbeddingModel = string.IsNullOrWhiteSpace(embeddingModel) ? ResolveEmbeddingModelName(normalizedProvider) : embeddingModel!
            };
        }

        public static string ResolveEmbeddingModelName(string providerType)
        {
            switch (providerType.ToLowerInvariant())
            {
                case "ollama": return "all-minilm";
                case "openai": return "text-embedding-3-small";
                case "gemini": return "text-embedding-004";
                default: throw new ArgumentException("Unknown provider type: " + providerType, nameof(providerType));
            }
        }
    }
}
