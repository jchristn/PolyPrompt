namespace PolyPrompt.Models
{
    /// <summary>
    /// One non-zero entry of a sparse embedding vector.
    /// </summary>
    public class SparseValue
    {
        #region Public-Members

        /// <summary>
        /// Dimension (vocabulary token) index of the entry.
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// Weight of the entry.
        /// </summary>
        public float Value { get; set; }

        #endregion
    }
}
