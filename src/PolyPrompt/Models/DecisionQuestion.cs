namespace PolyPrompt.Models
{
    /// <summary>
    /// A question put to a decision model about a <see cref="DecisionRequest.State"/>. Create questions with the
    /// <see cref="Binary"/>, <c>Choice</c>, and <c>Score</c> factory methods, or by constructing
    /// <see cref="BinaryQuestion"/>, <see cref="ChoiceQuestion"/>, or <see cref="ScoreQuestion"/> directly.
    /// </summary>
    public abstract class DecisionQuestion
    {
        #region Public-Members

        /// <summary>
        /// Question identifier, unique within a request. Answers are keyed by it.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// What to decide, in the caller's words. Required.
        /// </summary>
        public DecisionContent? Instructions { get; set; } = null;

        /// <summary>
        /// The kind of question.
        /// </summary>
        public abstract DecisionQuestionType Type { get; }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create a yes/no question.
        /// </summary>
        /// <param name="id">Question identifier.</param>
        /// <param name="instructions">What to decide.</param>
        /// <param name="trueCriterion">Optional description of when the answer is yes.</param>
        /// <param name="falseCriterion">Optional description of when the answer is no.</param>
        /// <returns>The question.</returns>
        public static BinaryQuestion Binary(string id, DecisionContent instructions, DecisionContent? trueCriterion = null, DecisionContent? falseCriterion = null)
        {
            return new BinaryQuestion { Id = id, Instructions = instructions, TrueCriterion = trueCriterion, FalseCriterion = falseCriterion };
        }

        /// <summary>
        /// Create a choice question from options with optional descriptions.
        /// </summary>
        /// <param name="id">Question identifier.</param>
        /// <param name="instructions">What to decide.</param>
        /// <param name="options">The options (2 to 255).</param>
        /// <returns>The question.</returns>
        public static ChoiceQuestion Choice(string id, DecisionContent instructions, params DecisionOption[] options)
        {
            return new ChoiceQuestion { Id = id, Instructions = instructions, Options = new List<DecisionOption>(options ?? Array.Empty<DecisionOption>()) };
        }

        /// <summary>
        /// Create a choice question from option values without descriptions.
        /// </summary>
        /// <param name="id">Question identifier.</param>
        /// <param name="instructions">What to decide.</param>
        /// <param name="values">The option values (2 to 255).</param>
        /// <returns>The question.</returns>
        public static ChoiceQuestion Choice(string id, DecisionContent instructions, params string[] values)
        {
            return Choice(id, instructions, (IEnumerable<string>)(values ?? Array.Empty<string>()));
        }

        /// <summary>
        /// Create a choice question from option values without descriptions.
        /// </summary>
        /// <param name="id">Question identifier.</param>
        /// <param name="instructions">What to decide.</param>
        /// <param name="values">The option values (2 to 255).</param>
        /// <returns>The question.</returns>
        public static ChoiceQuestion Choice(string id, DecisionContent instructions, IEnumerable<string> values)
        {
            List<DecisionOption> options = new List<DecisionOption>();
            foreach (string value in values ?? Enumerable.Empty<string>()) options.Add(new DecisionOption { Value = value });
            return new ChoiceQuestion { Id = id, Instructions = instructions, Options = options };
        }

        /// <summary>
        /// Create a score question from rubric levels, lowest first.
        /// </summary>
        /// <param name="id">Question identifier.</param>
        /// <param name="instructions">What to rate.</param>
        /// <param name="levels">Rubric levels, lowest first (2 to 10).</param>
        /// <returns>The question.</returns>
        public static ScoreQuestion Score(string id, DecisionContent instructions, params DecisionContent[] levels)
        {
            return new ScoreQuestion { Id = id, Instructions = instructions, Levels = new List<DecisionContent>(levels ?? Array.Empty<DecisionContent>()) };
        }

        /// <summary>
        /// Create a score question from rubric levels, lowest first.
        /// </summary>
        /// <param name="id">Question identifier.</param>
        /// <param name="instructions">What to rate.</param>
        /// <param name="levels">Rubric levels, lowest first (2 to 10).</param>
        /// <returns>The question.</returns>
        public static ScoreQuestion Score(string id, DecisionContent instructions, IEnumerable<string> levels)
        {
            List<DecisionContent> list = new List<DecisionContent>();
            foreach (string level in levels ?? Enumerable.Empty<string>()) list.Add(level!);
            return new ScoreQuestion { Id = id, Instructions = instructions, Levels = list };
        }

        #endregion
    }
}
