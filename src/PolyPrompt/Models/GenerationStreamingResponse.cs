namespace PolyPrompt.Models
{
    /// <summary>
    /// Top-level response from a streaming text generation request.
    /// Contains metadata, timing, and an async enumerable of streaming chunks.
    /// Timing fields are populated as chunks are consumed through the Chunks enumerable.
    /// </summary>
    public class GenerationStreamingResponse : ResponseBase
    {
        /// <summary>
        /// The async enumerable of streaming chunks.
        /// Enumerate this to receive tokens as they arrive.
        /// </summary>
        public IAsyncEnumerable<GenerationStreamingChunk> Chunks { get; set; } = EmptyChunks();

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
        /// Tokens per second calculated over the overall runtime.
        /// </summary>
        public double OverallTokensPerSecond { get; set; }

        /// <summary>
        /// Tokens per second calculated between first and last token arrival.
        /// </summary>
        public double InterTokenTokensPerSecond { get; set; }

        private static async IAsyncEnumerable<GenerationStreamingChunk> EmptyChunks()
        {
            yield break;
        }
    }
}
