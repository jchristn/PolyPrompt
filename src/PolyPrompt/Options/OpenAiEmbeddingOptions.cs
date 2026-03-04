namespace PolyPrompt.Options
{
    using PolyPrompt.Models;

    /// <summary>
    /// OpenAI-specific options for embedding requests.
    /// These map to fields in the OpenAI /v1/embeddings request body.
    /// </summary>
    public class OpenAiEmbeddingOptions : EmbeddingOptions
    {
        #region Public-Members

        /// <summary>
        /// Encoding format for the embeddings. Valid values: "float" (default), "base64".
        /// </summary>
        public string? EncodingFormat { get; set; } = null;

        /// <summary>
        /// Number of dimensions for the output embedding. Null uses model default.
        /// </summary>
        public int? Dimensions { get; set; } = null;

        #endregion
    }
}
