namespace PolyPrompt.Clients
{
    internal static class OllamaCloudProtocol
    {
        internal const string Endpoint = "https://ollama.com:443";

        internal static string RequireToken(string apiKey)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
            if (apiKey.Any(char.IsWhiteSpace)) throw new ArgumentException("Bearer token must not contain whitespace.", nameof(apiKey));
            return apiKey;
        }
    }
}
