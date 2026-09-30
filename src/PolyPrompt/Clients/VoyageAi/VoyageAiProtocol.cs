namespace PolyPrompt.Clients
{
    using System.Net.Http.Headers;

    /// <summary>
    /// Wire details shared by the VoyageAI clients.
    /// </summary>
    internal static class VoyageAiProtocol
    {
        internal const string DefaultEndpoint = "https://api.voyageai.com";
        internal const string Header = "[VoyageAI] ";

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
