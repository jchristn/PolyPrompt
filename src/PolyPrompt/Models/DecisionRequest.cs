namespace PolyPrompt.Models
{
    /// <summary>
    /// A decision request: the state to decide about and the questions to answer about it.
    /// </summary>
    public class DecisionRequest
    {
        /// <summary>
        /// The state to decide about: a string, or any object that serializes to JSON (for example a dictionary or a
        /// record describing a ticket, a transaction, or a conversation). Required.
        /// </summary>
        public object? State { get; set; } = null;

        /// <summary>
        /// The questions to answer, each with a unique id. At least one is required.
        /// </summary>
        public List<DecisionQuestion> Questions { get; set; } = new List<DecisionQuestion>();
    }
}
