namespace PolyPrompt.Models
{
    /// <summary>
    /// Response from a non-streaming chat completion request.
    /// Contains the completion text, metadata, and timing information.
    /// </summary>
    public class ChatResponse : ResponseBase
    {
        /// <summary>Provider response identifier, when reported.</summary>
        public string? ResponseId { get; set; }
        /// <summary>Provider finish reason, when reported.</summary>
        public string? FinishReason { get; set; }
        /// <summary>
        /// The completion text returned by the model.
        /// </summary>
        public string? Text { get; set; }

        /// <summary>
        /// Reasoning ("thinking") content returned by the model, kept separate from <see cref="Text"/>. Null
        /// when the model produced no reasoning. Returned to the caller for display or logging.
        /// </summary>
        public string? Reasoning { get; set; }

        /// <summary>
        /// Token usage for this completion, when the provider reported it. Populated on both the streaming
        /// and non-streaming paths so cost and cache accounting is identical regardless of which was used.
        /// Null when the provider returned no usage data.
        /// </summary>
        public TokenUsage? Usage { get; set; }
    }
}
