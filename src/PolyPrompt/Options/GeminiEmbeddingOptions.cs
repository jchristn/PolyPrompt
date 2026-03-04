namespace PolyPrompt.Options
{
    using PolyPrompt.Models;

    /// <summary>
    /// Gemini-specific options for embedding requests.
    /// These map to fields in the Gemini embedContent request body.
    /// </summary>
    public class GeminiEmbeddingOptions : EmbeddingOptions
    {
        #region Public-Members

        /// <summary>
        /// Task type for the embedding. Examples: "RETRIEVAL_DOCUMENT", "RETRIEVAL_QUERY", "SEMANTIC_SIMILARITY", "CLASSIFICATION", "CLUSTERING".
        /// </summary>
        public string? TaskType { get; set; } = null;

        /// <summary>
        /// Title of the document (only applicable when TaskType is "RETRIEVAL_DOCUMENT").
        /// </summary>
        public string? Title { get; set; } = null;

        #endregion
    }
}
