namespace PolyPrompt.Models
{
    /// <summary>
    /// Top-level response from a streaming chat completion request.
    /// Contains metadata, timing, and an async enumerable of streaming chunks.
    /// Timing fields are populated as chunks are consumed through the Chunks enumerable.
    /// </summary>
    public class ChatStreamingResponse
    {
        /// <summary>
        /// The model name used for this completion.
        /// </summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>
        /// Whether the streaming request was initiated successfully.
        /// If false, check Error for details.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// HTTP status code from the streaming request.
        /// </summary>
        public int? StatusCode { get; set; }

        /// <summary>
        /// Error message if the request failed.
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// The async enumerable of streaming chunks.
        /// Enumerate this to receive tokens and metadata as they arrive.
        /// Timing fields on this response are updated as chunks are consumed.
        /// </summary>
        public IAsyncEnumerable<ChatStreamingChunk> Chunks { get; set; } = EmptyChunks();

        /// <summary>
        /// Overall runtime in milliseconds from request start to last chunk received.
        /// Updated after all chunks have been consumed.
        /// </summary>
        public long OverallRuntimeMs { get; set; }

        /// <summary>
        /// Time in milliseconds from request start to the first token received.
        /// </summary>
        public long TimeToFirstTokenMs { get; set; } = -1;

        /// <summary>
        /// Time in milliseconds from request start to the last token received.
        /// </summary>
        public long TimeToLastTokenMs { get; set; } = -1;

        /// <summary>
        /// Number of text-bearing chunks received.
        /// </summary>
        public int ChunkCount { get; set; }

        /// <summary>
        /// Tokens per second calculated over the overall runtime (completion tokens / overall runtime).
        /// </summary>
        public double OverallTokensPerSecond { get; set; }

        /// <summary>
        /// Tokens per second calculated between first and last token arrival (completion tokens / inter-token duration).
        /// </summary>
        public double InterTokenTokensPerSecond { get; set; }

        /// <summary>
        /// Final token usage from the stream (typically from the last chunk).
        /// </summary>
        public ChatStreamingUsage? Usage { get; set; }

        /// <summary>
        /// The finish reason from the model (e.g. "stop", "length").
        /// </summary>
        public string? FinishReason { get; set; }

        /// <summary>
        /// Response identifier for correlation.
        /// </summary>
        public string? ResponseId { get; set; }

        private static async IAsyncEnumerable<ChatStreamingChunk> EmptyChunks()
        {
            yield break;
        }
    }
}
