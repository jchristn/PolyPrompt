namespace PolyPrompt.Options
{
    using PolyPrompt.Models;

    /// <summary>
    /// Text Embeddings Inference options for sparse embedding requests.
    /// These map to fields on the TEI /embed_sparse request body.
    /// </summary>
    public class TeiSparseEmbeddingOptions : SparseEmbeddingOptions
    {
        #region Private-Members

        private string? _TruncationDirection = null;
        private string? _PromptName = null;

        #endregion

        #region Public-Members

        /// <summary>
        /// Whether over-length inputs are truncated (truncate). Null uses the server default.
        /// </summary>
        public bool? Truncate { get; set; } = null;

        /// <summary>
        /// Which end of an over-length input is removed when truncating (truncation_direction). Valid values
        /// are "left" and "right" (case-insensitive); an unrecognized value reverts to null. Null uses the
        /// server default ("Right").
        /// </summary>
        public string? TruncationDirection
        {
            get { return _TruncationDirection; }
            set { _TruncationDirection = TeiOptionValues.NormalizeTruncationDirection(value); }
        }

        /// <summary>
        /// Name of a sentence-transformers prompt to prepend (prompt_name). Null or whitespace applies no
        /// prompt.
        /// </summary>
        public string? PromptName
        {
            get { return _PromptName; }
            set { _PromptName = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }
        }

        #endregion
    }
}
