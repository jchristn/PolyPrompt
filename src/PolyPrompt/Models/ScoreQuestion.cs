namespace PolyPrompt.Models
{
    /// <summary>
    /// A question that rates the state along an ordered rubric. Answered by a <see cref="ScoreAnswer"/>.
    /// </summary>
    public class ScoreQuestion : DecisionQuestion
    {
        /// <summary>
        /// Minimum number of levels.
        /// </summary>
        public const int MinLevels = 2;

        /// <summary>
        /// Maximum number of levels.
        /// </summary>
        public const int MaxLevels = 10;

        /// <summary>
        /// Rubric levels, lowest first (2 to 10). Level i corresponds to score i.
        /// </summary>
        public List<DecisionContent> Levels { get; set; } = new List<DecisionContent>();

        /// <inheritdoc />
        public override DecisionQuestionType Type => DecisionQuestionType.Score;
    }
}
