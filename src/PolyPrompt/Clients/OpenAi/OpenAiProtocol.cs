namespace PolyPrompt.Clients
{
    using System.Net.Http.Headers;
    
    /// <summary>
    /// Wire details shared by the OpenAI clients.
    /// </summary>
    internal static class OpenAiProtocol
    {
        internal const string DefaultEndpoint = "https://api.openai.com";
        internal const string Header = "[OpenAI] ";

        /// <summary>
        /// Build the URL for an operation path such as <c>chat/completions</c>. The <c>/v1</c> prefix is added unless the
        /// endpoint already ends with it, so both <c>https://api.openai.com</c> and OpenAI-compatible base URLs such as
        /// <c>http://localhost:11434/v1</c> work.
        /// </summary>
        internal static string BuildApiUrl(string endpoint, string path)
        {
            string trimmed = endpoint.TrimEnd('/');
            string normalizedPath = path.TrimStart('/');

            if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                return trimmed + "/" + normalizedPath;

            return trimmed + "/v1/" + normalizedPath;
        }

        /// <summary>
        /// Attach the bearer token, when one is configured.
        /// </summary>
        internal static void ApplyAuth(HttpRequestMessage request, string? apiKey)
        {
            if (!string.IsNullOrEmpty(apiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }
}
