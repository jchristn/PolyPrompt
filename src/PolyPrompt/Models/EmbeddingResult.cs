namespace PolyPrompt.Models
{
    /// <summary>
    /// A single embedding vector result.
    /// </summary>
    public class EmbeddingResult
    {
        /// <summary>
        /// The index of this embedding in the request batch.
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// The embedding vector as an array of floats.
        /// </summary>
        public float[] Embedding { get; set; } = Array.Empty<float>();
    }
}
