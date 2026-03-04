namespace PolyPrompt.Models
{
    /// <summary>
    /// Token usage and timing statistics from a chat completion.
    /// </summary>
    public class ChatStreamingUsage
    {
        /// <summary>
        /// Number of tokens in the prompt.
        /// </summary>
        public int? PromptTokens { get; set; }

        /// <summary>
        /// Number of tokens generated in the completion.
        /// </summary>
        public int? CompletionTokens { get; set; }

        /// <summary>
        /// Total tokens (prompt + completion).
        /// </summary>
        public int? TotalTokens { get; set; }

        /// <summary>
        /// Total request duration in nanoseconds (Ollama only).
        /// </summary>
        public long? TotalDurationNs { get; set; }

        /// <summary>
        /// Model load duration in nanoseconds (Ollama only).
        /// </summary>
        public long? LoadDurationNs { get; set; }

        /// <summary>
        /// Prompt evaluation duration in nanoseconds (Ollama only).
        /// </summary>
        public long? PromptEvalDurationNs { get; set; }

        /// <summary>
        /// Token generation duration in nanoseconds (Ollama only).
        /// </summary>
        public long? EvalDurationNs { get; set; }
    }
}
