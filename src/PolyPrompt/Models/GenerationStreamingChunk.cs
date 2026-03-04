namespace PolyPrompt.Models
{
    /// <summary>
    /// A single chunk from a streaming text generation response.
    /// </summary>
    public class GenerationStreamingChunk
    {
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
