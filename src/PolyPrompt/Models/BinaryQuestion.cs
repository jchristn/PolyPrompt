namespace PolyPrompt.Models
{
    /// <summary>
    /// A yes/no question. Answered by a <see cref="BinaryAnswer"/>.
    /// </summary>
    public class BinaryQuestion : DecisionQuestion
    {
        /// <summary>
        /// Optional description of when the answer is yes. Default: null.
        /// </summary>
        public DecisionContent? TrueCriterion { get; set; } = null;

        /// <summary>
        /// Optional description of when the answer is no. Default: null.
        /// </summary>
        public DecisionContent? FalseCriterion { get; set; } = null;

        /// <inheritdoc />
        public override DecisionQuestionType Type => DecisionQuestionType.Binary;
    }
}
