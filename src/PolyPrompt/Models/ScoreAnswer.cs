namespace PolyPrompt.Models
{
    /// <summary>
    /// The answer to a <see cref="ScoreQuestion"/>.
    /// </summary>
    public class ScoreAnswer : DecisionAnswer
    {
        /// <summary>
        /// The score on the rubric's scale, where level i of <see cref="ScoreQuestion.Levels"/> is i. May be fractional
        /// (an expected value between levels).
        /// </summary>
        public double Value { get; set; }

        /// <summary>
        /// The index of the rubric level nearest to <see cref="Value"/> (rounded, then clamped to the question's levels).
        /// </summary>
        public int Level { get; set; }

        /// <summary>
        /// The text of the level at <see cref="Level"/>, from the question that was asked.
        /// </summary>
        public string? LevelText { get; set; } = null;

        /// <summary>
        /// The provider's legend mapping score values to level descriptions, when it reports one.
        /// </summary>
        public Dictionary<string, string> Legend { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <inheritdoc />
        public override DecisionQuestionType Type => DecisionQuestionType.Score;
    }
}
