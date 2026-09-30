namespace PolyPrompt.Models
{
    /// <summary>
    /// Content supplied to a decision model: question instructions, an option description, a criterion, or a score level.
    /// In this version content is text. The type exists so that structured (JSON) content can be added later without
    /// changing any property or method signature; code that reads <see cref="Text"/> should expect it to be null for
    /// content that is not text once that support exists. A string converts to content implicitly, so callers can
    /// assign or pass strings directly.
    /// </summary>
    public sealed class DecisionContent
    {
        #region Public-Members

        /// <summary>
        /// The text content.
        /// </summary>
        public string? Text { get; }

        #endregion

        #region Constructors-and-Factories

        private DecisionContent(string text)
        {
            Text = text;
        }

        /// <summary>
        /// Create text content.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <returns>The content.</returns>
        /// <exception cref="ArgumentNullException">Thrown when text is null.</exception>
        public static DecisionContent FromText(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            return new DecisionContent(text);
        }

        /// <summary>
        /// Convert a string to text content. A null string converts to null.
        /// </summary>
        /// <param name="text">The text.</param>
        public static implicit operator DecisionContent?(string? text)
        {
            return text == null ? null : new DecisionContent(text);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The text content, or an empty string when there is none.
        /// </summary>
        /// <returns>The text.</returns>
        public override string ToString()
        {
            return Text ?? string.Empty;
        }

        #endregion
    }
}
