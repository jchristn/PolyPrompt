namespace PolyPrompt.Options
{
    using PolyPrompt.Models;

    /// <summary>
    /// AWS Bedrock options for embedding requests. Bedrock multiplexes several embedding model families
    /// behind one API; the fields here map to whichever family the model id selects (Amazon Titan or Cohere).
    /// Fields that do not apply to the chosen family are ignored.
    /// </summary>
    public class BedrockEmbeddingOptions : EmbeddingOptions
    {
        #region Public-Members

        /// <summary>
        /// Cohere input type, e.g. <c>search_document</c>, <c>search_query</c>, <c>classification</c>,
        /// <c>clustering</c>. Applies to Cohere embedding models only.
        /// </summary>
        public string? InputType { get; set; } = null;

        /// <summary>
        /// Output embedding dimension. Applies to Amazon Titan v2 embedding models (which support 256/512/1024).
        /// Null uses the model default.
        /// </summary>
        public int? Dimensions { get; set; } = null;

        /// <summary>
        /// Whether Titan should L2-normalize the output embedding. Applies to Amazon Titan v2 models. Null
        /// omits the field and uses the model default.
        /// </summary>
        public bool? Normalize { get; set; } = null;

        #endregion
    }
}
