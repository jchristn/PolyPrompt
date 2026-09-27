namespace PolyPrompt.Options
{
    using PolyPrompt.Models;

    /// <summary>
    /// Text Embeddings Inference options for embedding requests.
    /// These map to fields on the TEI /embed request body.
    /// </summary>
    public class TeiEmbeddingOptions : EmbeddingOptions
    {
        #region Private-Members

        private string? _TruncationDirection = null;
        private string? _PromptName = null;
        private int? _Dimensions = null;

        #endregion

        #region Public-Members

        /// <summary>
        /// Whether vectors are L2-normalized (normalize). Null uses the server default (true).
        /// </summary>
        public bool? Normalize { get; set; } = null;

        /// <summary>
        /// Whether over-length inputs are truncated to the model's maximum input length (truncate). Null uses
        /// the server default, which depends on the server's --auto-truncate setting. When false, over-length
        /// inputs return a 413 or 422 error.
        /// </summary>
        public bool? Truncate { get; set; } = null;

        /// <summary>
        /// Which end of an over-length input is removed when truncating (truncation_direction). Valid values
        /// are "left" and "right" (case-insensitive, sent as "Left"/"Right"); an unrecognized value reverts to
        /// null. Null uses the server default ("Right").
        /// </summary>
        public string? TruncationDirection
        {
            get { return _TruncationDirection; }
            set { _TruncationDirection = TeiOptionValues.NormalizeTruncationDirection(value); }
        }

        /// <summary>
        /// Name of a sentence-transformers prompt to prepend (prompt_name), for example "query". Must be a key
        /// in the model's configured prompts. Null or whitespace applies no prompt.
        /// </summary>
        public string? PromptName
        {
            get { return _PromptName; }
            set { _PromptName = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }
        }

        /// <summary>
        /// Output dimension for Matryoshka-capable models (dimensions). Minimum 1; a value below 1 reverts to
        /// null. Null returns the model's native dimension.
        /// </summary>
        public int? Dimensions
        {
            get { return _Dimensions; }
            set { _Dimensions = value.HasValue && value.Value >= 1 ? value : null; }
        }

        #endregion
    }
}
