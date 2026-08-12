namespace PolyPrompt.Models
{
    /// <summary>
    /// A single chunk from a streaming tool-capable chat completion response.
    /// </summary>
    public class ToolChatStreamingChunk
    {
        /// <summary>
        /// Assistant text content of this chunk. May be null or empty for metadata-only or tool-call chunks.
        /// </summary>
        public string? Text { get; set; } = null;

        /// <summary>
        /// Reasoning ("thinking") content of this chunk, kept separate from <see cref="Text"/>. Null when the
        /// chunk carries no reasoning. Sourced from the provider's reasoning channel (OpenAI
        /// reasoning_content, Ollama message.thinking, Gemini thought parts).
        /// </summary>
        public string? ReasoningText { get; set; } = null;

        /// <summary>
        /// Tool call deltas emitted in this chunk.
        /// </summary>
        public List<ToolCallDelta> ToolCallDeltas { get; set; } = new List<ToolCallDelta>();

        /// <summary>
        /// The model that generated this chunk.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Whether this is the final chunk in the stream.
        /// </summary>
        public bool Done { get; set; }

        /// <summary>
        /// The reason generation finished. Present on the final chunk.
        /// Common values include stop, tool_calls, length, STOP, and MAX_TOKENS.
        /// </summary>
        public string? FinishReason { get; set; } = null;

        /// <summary>
        /// Token usage and timing statistics. Typically only populated on the final chunk.
        /// </summary>
        public ChatStreamingUsage? Usage { get; set; } = null;

        /// <summary>
        /// Response identifier for correlation.
        /// </summary>
        public string? ResponseId { get; set; } = null;

        /// <summary>
        /// UTC timestamp of when this chunk was created.
        /// </summary>
        public DateTime? CreatedUtc { get; set; } = null;
    }
}
