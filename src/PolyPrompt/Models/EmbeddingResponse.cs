namespace PolyPrompt.Models
{
    /// <summary>
    /// Response from an embedding request containing one or more embedding vectors.
    /// </summary>
    public class EmbeddingResponse
    {
        /// <summary>
        /// Whether the request was successful.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// HTTP status code from the request.
        /// </summary>
        public int StatusCode { get; set; }

        /// <summary>
        /// Error message if the request failed.
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// The model name used for this embedding.
        /// </summary>
        public string? Model { get; set; }

        /// <summary>
        /// List of embedding vectors returned by the model.
        /// </summary>
        public List<EmbeddingResult> Embeddings { get; set; } = new List<EmbeddingResult>();

        /// <summary>
        /// Overall runtime of the request in milliseconds.
        /// </summary>
        public long OverallRuntimeMs { get; set; }
    }
}
