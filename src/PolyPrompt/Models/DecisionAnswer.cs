namespace PolyPrompt.Models
{
    /// <summary>
    /// The answer to one <see cref="DecisionQuestion"/>. Use the typed subclasses (<see cref="BinaryAnswer"/>,
    /// <see cref="ChoiceAnswer"/>, <see cref="ScoreAnswer"/>) or the typed accessors on <see cref="DecisionResponse"/>.
    /// </summary>
    public abstract class DecisionAnswer
    {
        /// <summary>
        /// The id of the question this answers.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// The kind of answer.
        /// </summary>
        public abstract DecisionQuestionType Type { get; }

        /// <summary>
        /// The probability distribution the answer was drawn from, as reported by the provider: per option for a
        /// choice, per level for a score, and per outcome for a binary question. Empty when the provider reports none.
        /// </summary>
        public Dictionary<string, double> Probabilities { get; set; } = new Dictionary<string, double>(StringComparer.Ordinal);

        /// <summary>
        /// Calibrated confidence in the answer, 0 to 1, derived by the provider from the shape of the distribution
        /// (it is not simply the top probability). Null when the provider reports none.
        /// </summary>
        public double? Confidence { get; set; } = null;
    }
}
