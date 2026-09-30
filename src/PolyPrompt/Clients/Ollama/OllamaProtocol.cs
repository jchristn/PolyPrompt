namespace PolyPrompt.Clients
{
    using System.Net.Http.Headers;

    /// <summary>
    /// Wire details shared by the Ollama clients.
    /// </summary>
    internal static class OllamaProtocol
    {
        internal const string DefaultEndpoint = "http://localhost:11434";
        internal const string Header = "[Ollama] ";
        internal const string TagsPath = "/api/tags";

        /// <summary>
        /// Attach the bearer token, when one is configured (Ollama itself needs none; proxies in front of it may).
        /// </summary>
        internal static void ApplyAuth(HttpRequestMessage request, string? apiKey)
        {
            if (!string.IsNullOrEmpty(apiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }
}
