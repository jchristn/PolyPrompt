namespace PolyPrompt.Models
{
    /// <summary>
    /// A single scored document from a rerank request.
    /// </summary>
    public class RerankResult
    {
        #region Public-Members

        /// <summary>
        /// Zero-based position of the scored document in the list passed to RerankAsync.
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// Relevance score for the document. Higher is more relevant. Scores are provider-specific and are
        /// not comparable across providers: Cohere, VoyageAI, and Bedrock return a normalized 0..1
        /// relevance, while Text Embeddings Inference returns a sigmoid-normalized 0..1 score by default or
        /// raw logits (any real number) when raw scores are requested.
        /// </summary>
        public double Score { get; set; }

        /// <summary>
        /// Text of the scored document. Populated only when <see cref="RerankOptions.ReturnDocuments"/> is
        /// true; otherwise null.
        /// </summary>
        public string? Document { get; set; } = null;

        #endregion
    }
}
