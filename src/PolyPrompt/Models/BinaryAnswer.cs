namespace PolyPrompt.Models
{
    /// <summary>
    /// The answer to a <see cref="BinaryQuestion"/>.
    /// </summary>
    public class BinaryAnswer : DecisionAnswer
    {
        /// <summary>
        /// Probability, 0 to 1, that the answer is yes.
        /// </summary>
        public double Probability { get; set; }

        /// <summary>
        /// True when <see cref="Probability"/> is at least 0.5.
        /// </summary>
        public bool Value => Probability >= 0.5;

        /// <inheritdoc />
        public override DecisionQuestionType Type => DecisionQuestionType.Binary;
    }
}
