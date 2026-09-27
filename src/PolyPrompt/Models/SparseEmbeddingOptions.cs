namespace PolyPrompt.Models
{
    /// <summary>
    /// Base options for sparse embedding requests. When a value is null, the client instance default is used.
    /// </summary>
    public class SparseEmbeddingOptions
    {
        #region Public-Members

        /// <summary>
        /// Model override for this request. Null uses the client default. Ignored by Text Embeddings
        /// Inference, which serves a single model.
        /// </summary>
        public string? Model { get; set; } = null;

        #endregion
    }
}
