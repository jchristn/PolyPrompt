namespace PolyPrompt.Models
{
    /// <summary>
    /// Top-level response from a streaming tool-capable chat completion request.
    /// Contains metadata, timing, an async enumerable of chunks, and accumulated final output.
    /// Timing and accumulated output fields are populated as chunks are consumed.
    /// </summary>
    public class ToolChatStreamingResponse : StreamingResponseBase
    {
        /// <summary>
        /// Assistant text accumulated from streamed chunks. May be null when the model only requested tool calls.
        /// </summary>
        public string? Text { get; set; } = null;

        /// <summary>
        /// Reasoning ("thinking") content accumulated from streamed chunks, kept separate from
        /// <see cref="Text"/>. Null when the model produced no reasoning. Populated as chunks are consumed.
        /// Returned for display or logging; native continuation state is carried separately by ToAssistantMessage().
        /// </summary>
        public string? Reasoning { get; set; } = null;

        /// <summary>
        /// Tool calls accumulated from streamed tool-call deltas.
        /// </summary>
        public List<ToolCall> ToolCalls { get; set; } = new List<ToolCall>();

        /// <summary>
        /// Provider response identifier for correlation. May be null when the provider does not emit one.
        /// </summary>
        public string? ResponseId { get; set; } = null;

        /// <summary>
        /// Provider finish reason. Common values include stop, tool_calls, length, STOP, and MAX_TOKENS.
        /// </summary>
        public string? FinishReason { get; set; } = null;

        /// <summary>
        /// The async enumerable of streaming chunks.
        /// Enumerate this to receive text and tool-call deltas as they arrive.
        /// </summary>
        public IAsyncEnumerable<ToolChatStreamingChunk> Chunks { get; set; } = EmptyChunks();

        /// <summary>
        /// Time in milliseconds from request start to the first text or tool-call delta chunk.
        /// </summary>
        public long TimeToFirstTokenMs { get; set; } = -1;

        /// <summary>
        /// Time in milliseconds from request start to the last text or tool-call delta chunk.
        /// </summary>
        public long TimeToLastTokenMs { get; set; } = -1;

        /// <summary>
        /// Number of chunks containing text or tool-call deltas.
        /// </summary>
        public int ChunkCount { get; set; }

        /// <summary>
        /// Number of streamed tool-call deltas received.
        /// </summary>
        public int ToolCallDeltaCount { get; set; }

        /// <summary>
        /// Tokens per second calculated over the overall runtime.
        /// </summary>
        public double OverallTokensPerSecond { get; set; }

        /// <summary>
        /// Tokens per second calculated between first and last streamed content arrival.
        /// </summary>
        public double InterTokenTokensPerSecond { get; set; }

        /// <summary>
        /// Final token usage from the stream.
        /// </summary>
        public TokenUsage? Usage { get; set; } = null;

        /// <summary>
        /// Convert this response into an assistant message that can be appended to the next ToolChatRequest.
        /// Enumerate Chunks and require State == StreamingState.Completed before accepting the turn or executing tools.
        /// This accessor preserves partial output for compatibility and does not enforce completion or validate arguments.
        /// </summary>
        /// <returns>An assistant message containing either text or tool calls.</returns>
        public ChatMessage ToAssistantMessage()
        {
            return new ChatMessage
            {
                Role = "assistant",
                Content = Text,
                ToolCalls = ToolCalls ?? new List<ToolCall>(),
                ProviderState = ProviderState?.Clone()
            };
        }

        private static async IAsyncEnumerable<ToolChatStreamingChunk> EmptyChunks()
        {
            yield break;
        }
    }
}
