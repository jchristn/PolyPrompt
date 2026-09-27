namespace PolyPrompt.Models
{
    /// <summary>
    /// A candidate label and its score for one classified input.
    /// </summary>
    public class ClassificationLabel
    {
        #region Public-Members

        /// <summary>
        /// Label name.
        /// </summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>
        /// Score for the label. Higher is more likely. Cohere returns a 0..1 confidence; Text Embeddings
        /// Inference returns a normalized 0..1 score by default or raw logits when raw scores are requested.
        /// </summary>
        public double Score { get; set; }

        #endregion
    }
}
