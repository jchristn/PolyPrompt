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
        /// Remove a trailing <c>/v1</c> (the OpenAI-compatible API) so native <c>/api/*</c> routes resolve.
        /// </summary>
        internal static string NormalizeEndpoint(string endpoint)
        {
            string trimmed = endpoint.TrimEnd('/');
            return trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? trimmed.Substring(0, trimmed.Length - 3) : trimmed;
        }

        /// <summary>
        /// Attach the bearer token, when one is configured (Ollama itself needs none; proxies in front of it may).
        /// </summary>
        internal static void ApplyAuth(HttpRequestMessage request, string? apiKey)
        {
            if (!string.IsNullOrEmpty(apiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        /// <summary>
        /// Convert a keep-alive duration to the <c>keep_alive</c> wire value: whole seconds (a fractional second rounds up,
        /// so a short positive duration never unloads the model), 0 to unload, and -1 for any negative value (keep loaded).
        /// Ollama reads a JSON number as seconds; a number avoids its duration-string parser, which rejects "-1".
        /// </summary>
        internal static long KeepAliveSeconds(TimeSpan keepAlive)
        {
            if (keepAlive < TimeSpan.Zero) return -1;
            return (long)Math.Ceiling(keepAlive.TotalSeconds);
        }
    }
}
