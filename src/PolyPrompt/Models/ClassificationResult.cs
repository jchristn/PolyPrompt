namespace PolyPrompt.Models
{
    /// <summary>
    /// Classification output for a single input.
    /// </summary>
    public class ClassificationResult
    {
        #region Public-Members

        /// <summary>
        /// Zero-based position of the input in the list passed to ClassifyAsync.
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// The input text that was classified.
        /// </summary>
        public string Input { get; set; } = string.Empty;

        /// <summary>
        /// The highest-scoring label, or null when the provider returned no labels.
        /// </summary>
        public string? Label { get; set; } = null;

        /// <summary>
        /// Score of <see cref="Label"/>, or null when the provider returned no labels.
        /// </summary>
        public double? Score { get; set; } = null;

        /// <summary>
        /// Every label the provider scored for this input, highest score first.
        /// </summary>
        public List<ClassificationLabel> Labels { get; set; } = new List<ClassificationLabel>();

        #endregion
    }
}
