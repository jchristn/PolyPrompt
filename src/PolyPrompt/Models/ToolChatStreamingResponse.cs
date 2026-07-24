namespace PolyPrompt.Models
{
    /// <summary>
    /// Top-level response from a streaming tool-capable chat completion request.
    /// Contains metadata, timing, an async enumerable of chunks, and accumulated final output.
    /// Timing and accumulated output fields are populated as chunks are consumed.
    /// </summary>
    public class ToolChatStreamingResponse
    {
        /// <summary>
        /// Assistant text accumulated from streamed chunks. May be null when the model only requested tool calls.
        /// </summary>
        public string? Text { get; set; } = null;

        /// <summary>
        /// Tool calls accumulated from streamed tool-call deltas.
        /// </summary>
        public List<ToolCall> ToolCalls { get; set; } = new List<ToolCall>();

        /// <summary>
        /// The model name used for this completion.
        /// </summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>
        /// Provider response identifier for correlation. May be null when the provider does not emit one.
        /// </summary>
        public string? ResponseId { get; set; } = null;

        /// <summary>
        /// Provider finish reason. Common values include stop, tool_calls, length, STOP, and MAX_TOKENS.
        /// </summary>
        public string? FinishReason { get; set; } = null;

        /// <summary>
        /// Whether the streaming request was initiated successfully.
        /// If false, check Error for details.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// HTTP status code from the streaming request.
        /// </summary>
        public int? StatusCode { get; set; } = null;

        /// <summary>
        /// Error message if the request failed.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// The async enumerable of streaming chunks.
        /// Enumerate this to receive text and tool-call deltas as they arrive.
        /// </summary>
        public IAsyncEnumerable<ToolChatStreamingChunk> Chunks { get; set; } = EmptyChunks();

        /// <summary>
        /// Overall runtime in milliseconds from request start to last chunk received.
        /// Updated after all chunks have been consumed.
        /// </summary>
        public long OverallRuntimeMs { get; set; }

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
        public ChatStreamingUsage? Usage { get; set; } = null;

        /// <summary>
        /// Convert this response into an assistant message that can be appended to the next ToolChatRequest.
        /// Enumerate Chunks before calling this method so streamed tool calls are fully accumulated.
        /// </summary>
        /// <returns>An assistant message containing either text or tool calls.</returns>
        public ChatMessage ToAssistantMessage()
        {
            if (ToolCalls != null && ToolCalls.Any())
            {
                return ChatMessage.AssistantToolCalls(ToolCalls);
            }

            return ChatMessage.Assistant(Text ?? string.Empty);
        }

        private static async IAsyncEnumerable<ToolChatStreamingChunk> EmptyChunks()
        {
            yield break;
        }
    }
}
