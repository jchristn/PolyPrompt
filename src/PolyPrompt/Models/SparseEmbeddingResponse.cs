namespace PolyPrompt.Models
{
    /// <summary>
    /// Response from a sparse embedding request.
    /// </summary>
    public class SparseEmbeddingResponse : ResponseBase
    {
        #region Public-Members

        /// <summary>
        /// Sparse embeddings, one per input, in input order. Empty when the request failed.
        /// </summary>
        public List<SparseEmbeddingResult> Embeddings { get; set; } = new List<SparseEmbeddingResult>();

        #endregion
    }
}
