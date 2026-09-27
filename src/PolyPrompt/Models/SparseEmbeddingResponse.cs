namespace PolyPrompt.Models
{
    /// <summary>
    /// Response from a sparse embedding request.
    /// </summary>
    public class SparseEmbeddingResponse
    {
        #region Public-Members

        /// <summary>
        /// Whether the request was successful.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// HTTP status code from the request.
        /// </summary>
        public int StatusCode { get; set; }

        /// <summary>
        /// Error message if the request failed; null on success.
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// The model name used for this request.
        /// </summary>
        public string? Model { get; set; }

        /// <summary>
        /// Sparse embeddings, one per input, in input order. Empty when the request failed.
        /// </summary>
        public List<SparseEmbeddingResult> Embeddings { get; set; } = new List<SparseEmbeddingResult>();

        /// <summary>
        /// Overall runtime of the request in milliseconds.
        /// </summary>
        public long OverallRuntimeMs { get; set; }

        #endregion
    }
}
