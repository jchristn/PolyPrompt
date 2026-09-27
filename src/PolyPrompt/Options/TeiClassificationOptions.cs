namespace PolyPrompt.Options
{
    using PolyPrompt.Models;

    /// <summary>
    /// Text Embeddings Inference options for classification requests.
    /// These map to fields on the TEI /predict request body.
    /// </summary>
    public class TeiClassificationOptions : ClassificationOptions
    {
        #region Private-Members

        private string? _TruncationDirection = null;

        #endregion

        #region Public-Members

        /// <summary>
        /// When true, label scores are raw logits rather than normalized probabilities (raw_scores). Null uses
        /// the server default (false).
        /// </summary>
        public bool? RawScores { get; set; } = null;

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

        #endregion
    }
}
