namespace PolyPrompt.Models
{
    /// <summary>
    /// One option of a <see cref="ChoiceQuestion"/>.
    /// </summary>
    public class DecisionOption
    {
        #region Public-Members

        /// <summary>
        /// The option value, returned as <see cref="ChoiceAnswer.Value"/> when chosen. Must be non-empty and unique
        /// within its question.
        /// </summary>
        public string Value { get; set; } = string.Empty;

        /// <summary>
        /// Optional description that tells the model what the option means. Default: null.
        /// </summary>
        public DecisionContent? Description { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create an option.
        /// </summary>
        /// <param name="value">The option value.</param>
        /// <param name="description">Optional description.</param>
        /// <returns>The option.</returns>
        /// <exception cref="ArgumentNullException">Thrown when value is null.</exception>
        public static DecisionOption Of(string value, DecisionContent? description = null)
        {
            ArgumentNullException.ThrowIfNull(value);
            return new DecisionOption { Value = value, Description = description };
        }

        #endregion
    }
}
