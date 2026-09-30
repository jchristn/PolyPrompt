namespace PolyPrompt.Clients
{
    using System.Net.Http.Headers;

    /// <summary>
    /// Wire details shared by the Hugging Face Text Embeddings Inference (TEI) clients. A TEI server hosts exactly one
    /// model, whose type (embedding, sparse embedding, reranker, or classifier) is fixed when the server starts; calling an
    /// operation the hosted model does not support returns an unsuccessful response carrying TEI's error (usually HTTP 424).
    /// </summary>
    internal static class TeiProtocol
    {
        internal const string DefaultEndpoint = "http://localhost:8080";
        internal const string Header = "[TEI] ";
        internal const string HealthPath = "/health";

        /// <summary>
        /// Attach the bearer token, needed only when the server was started with <c>--api-key</c>.
        /// </summary>
        internal static void ApplyAuth(HttpRequestMessage request, string? apiKey)
        {
            if (!string.IsNullOrEmpty(apiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }
}
