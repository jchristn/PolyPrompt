namespace PolyPrompt.Models
{
    /// <summary>
    /// A sparse embedding for a single input.
    /// </summary>
    public class SparseEmbeddingResult
    {
        #region Public-Members

        /// <summary>
        /// Zero-based position of the input in the list passed to EmbedSparseAsync.
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// Non-zero entries of the sparse vector, in the order the provider returned them.
        /// </summary>
        public List<SparseValue> Values { get; set; } = new List<SparseValue>();

        #endregion
    }
}
