namespace PolyPrompt.Models
{
    /// <summary>
    /// A single chunk from a streaming text generation response.
    /// </summary>
    public class GenerationStreamingChunk
    {
        /// <summary>Reasoning summary delta, separate from generated text.</summary>
        public string? ReasoningText { get; set; }
        /// <summary>Token usage, usually reported in the final chunk.</summary>
        public TokenUsage? Usage { get; set; }
        /// <summary>Provider response identifier.</summary>
        public string? ResponseId { get; set; }
        /// <summary>Provider finish reason.</summary>
        public string? FinishReason { get; set; }
        /// <summary>
        /// The text content of this chunk.
        /// </summary>
        public string? Text { get; set; }

        /// <summary>
        /// Whether this is the final chunk in the stream.
        /// </summary>
        public bool Done { get; set; }

        /// <summary>
        /// The model that generated this chunk.
        /// </summary>
        public string? Model { get; set; }
    }
}
