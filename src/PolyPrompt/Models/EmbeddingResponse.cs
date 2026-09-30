namespace PolyPrompt.Models
{
    /// <summary>
    /// Response from an embedding request containing one or more embedding vectors.
    /// </summary>
    public class EmbeddingResponse : ResponseBase
    {
        /// <summary>
        /// List of embedding vectors returned by the model.
        /// </summary>
        public List<EmbeddingResult> Embeddings { get; set; } = new List<EmbeddingResult>();
    }
}
