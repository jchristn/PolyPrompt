namespace PolyPrompt.Clients
{
    using System.Net.Http.Headers;

    /// <summary>
    /// Wire details shared by the Cohere clients.
    /// </summary>
    internal static class CohereProtocol
    {
        internal const string DefaultEndpoint = "https://api.cohere.com";
        internal const string Header = "[Cohere] ";

        /// <summary>
        /// A cheap authenticated request used to validate connectivity: a one-entry page of the model list.
        /// </summary>
        internal const string ProbePath = "/v1/models?page_size=1";

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
