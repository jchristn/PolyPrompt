namespace PolyPrompt.Options
{
    using PolyPrompt.Models;

    /// <summary>
    /// Google Vertex AI options for embedding requests. Vertex embeddings use the <c>:predict</c> endpoint,
    /// whose instance and parameter shapes differ from AI-Studio Gemini embeddings — hence a dedicated
    /// options type.
    /// </summary>
    public class VertexAiEmbeddingOptions : EmbeddingOptions
    {
        #region Public-Members

        /// <summary>
        /// Embedding task type, e.g. <c>RETRIEVAL_DOCUMENT</c>, <c>RETRIEVAL_QUERY</c>,
        /// <c>SEMANTIC_SIMILARITY</c>, <c>CLASSIFICATION</c>, <c>CLUSTERING</c>. Null uses the model default.
        /// </summary>
        public string? TaskType { get; set; } = null;

        /// <summary>Optional document title, used with the <c>RETRIEVAL_DOCUMENT</c> task type.</summary>
        public string? Title { get; set; } = null;

        /// <summary>Number of dimensions to truncate the output embedding to. Null uses the model default.</summary>
        public int? OutputDimensionality { get; set; } = null;

        /// <summary>
        /// Whether inputs longer than the model's limit are silently truncated (true) or rejected (false).
        /// Null omits the field and uses the model default.
        /// </summary>
        public bool? AutoTruncate { get; set; } = null;

        #endregion
    }
}
