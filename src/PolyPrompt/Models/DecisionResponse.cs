namespace PolyPrompt.Models
{
    /// <summary>
    /// Response to a decision request. When <see cref="ResponseBase.Success"/> is true, <see cref="Answers"/> holds one answer per
    /// question, keyed by question id.
    /// </summary>
    public class DecisionResponse : ResponseBase
    {
        #region Public-Members

        /// <summary>
        /// Answers keyed by question id.
        /// </summary>
        public Dictionary<string, DecisionAnswer> Answers { get; set; } = new Dictionary<string, DecisionAnswer>(StringComparer.Ordinal);

        /// <summary>
        /// Token usage, when the provider reports it. Decision models generate no text, so completion tokens are
        /// usually zero.
        /// </summary>
        public TokenUsage? Usage { get; set; }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Get the answer to a choice question.
        /// </summary>
        /// <param name="id">Question id.</param>
        /// <returns>The answer.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when there is no answer with this id.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the answer is not a choice answer.</exception>
        public ChoiceAnswer Choice(string id)
        {
            return Get<ChoiceAnswer>(id);
        }

        /// <summary>
        /// Get the answer to a binary (yes/no) question.
        /// </summary>
        /// <param name="id">Question id.</param>
        /// <returns>The answer.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when there is no answer with this id.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the answer is not a binary answer.</exception>
        public BinaryAnswer Binary(string id)
        {
            return Get<BinaryAnswer>(id);
        }

        /// <summary>
        /// Get the answer to a score question.
        /// </summary>
        /// <param name="id">Question id.</param>
        /// <returns>The answer.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when there is no answer with this id.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the answer is not a score answer.</exception>
        public ScoreAnswer Score(string id)
        {
            return Get<ScoreAnswer>(id);
        }

        #endregion

        #region Private-Methods

        private T Get<T>(string id) where T : DecisionAnswer
        {
            ArgumentNullException.ThrowIfNull(id);
            if (!Answers.TryGetValue(id, out DecisionAnswer? answer))
                throw new KeyNotFoundException("No answer for question '" + id + "'.");
            if (answer is not T typed)
                throw new InvalidOperationException("The answer for question '" + id + "' is a " + answer.Type + " answer, not a " + typeof(T).Name + ".");
            return typed;
        }

        #endregion
    }
}
