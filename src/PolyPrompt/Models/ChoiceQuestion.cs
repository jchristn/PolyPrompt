namespace PolyPrompt.Models
{
    /// <summary>
    /// A question that picks one option from a defined set. Answered by a <see cref="ChoiceAnswer"/>.
    /// </summary>
    public class ChoiceQuestion : DecisionQuestion
    {
        /// <summary>
        /// Minimum number of options.
        /// </summary>
        public const int MinOptions = 2;

        /// <summary>
        /// Maximum number of options.
        /// </summary>
        public const int MaxOptions = 255;

        /// <summary>
        /// The options (2 to 255), each with a unique, non-empty value.
        /// </summary>
        public List<DecisionOption> Options { get; set; } = new List<DecisionOption>();

        /// <inheritdoc />
        public override DecisionQuestionType Type => DecisionQuestionType.Choice;
    }
}
