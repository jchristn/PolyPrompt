namespace PolyPrompt.Models
{
    /// <summary>
    /// A single chunk from a streaming chat completion response.
    /// </summary>
    public class ChatStreamingChunk
    {
        /// <summary>
        /// The text content of this chunk. May be null or empty for metadata-only chunks.
        /// </summary>
        public string? Text { get; set; }

        /// <summary>
        /// Reasoning ("thinking") content of this chunk, kept separate from <see cref="Text"/>. Null when the
        /// chunk carries no reasoning. Sourced from the provider's reasoning channel (OpenAI reasoning_content,
        /// Ollama message.thinking, Gemini thought parts).
        /// </summary>
        public string? ReasoningText { get; set; }

        /// <summary>
        /// The model that generated this chunk.
        /// </summary>
        public string? Model { get; set; }

        /// <summary>
        /// Whether this is the final chunk in the stream.
        /// </summary>
        public bool Done { get; set; }

        /// <summary>
        /// The reason generation finished. Present on the final chunk.
        /// Common values: "stop", "length" (Ollama/OpenAI), "STOP", "MAX_TOKENS" (Gemini).
        /// </summary>
        public string? FinishReason { get; set; }

        /// <summary>
        /// Token usage and timing statistics. Typically only populated on the final chunk.
        /// Gemini provides cumulative usage on every chunk.
        /// </summary>
        public ChatStreamingUsage? Usage { get; set; }

        /// <summary>
        /// Response identifier for correlation. Present in OpenAI (id) and Gemini (responseId).
        /// </summary>
        public string? ResponseId { get; set; }

        /// <summary>
        /// UTC timestamp of when this chunk was created.
        /// </summary>
        public DateTime? CreatedUtc { get; set; }
    }
}
