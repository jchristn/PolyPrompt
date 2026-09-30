namespace PolyPrompt.Models
{
    /// <summary>
    /// The answer to a <see cref="ChoiceQuestion"/>.
    /// </summary>
    public class ChoiceAnswer : DecisionAnswer
    {
        /// <summary>
        /// The chosen option value.
        /// </summary>
        public string Value { get; set; } = string.Empty;

        /// <summary>
        /// The probability of the chosen option, taken from <see cref="DecisionAnswer.Probabilities"/>. Null when the
        /// provider reports no probability for it.
        /// </summary>
        public double? Probability { get; set; } = null;

        /// <inheritdoc />
        public override DecisionQuestionType Type => DecisionQuestionType.Choice;
    }
}
