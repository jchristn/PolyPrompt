namespace PolyPrompt.Options
{
    using PolyPrompt.Models;

    /// <summary>
    /// Bedrock-specific options for rerank requests sent through InvokeModel.
    /// </summary>
    public class BedrockRerankOptions : RerankOptions
    {
        #region Private-Members

        private int? _MaxTokensPerDoc = null;

        #endregion

        #region Public-Members

        /// <summary>
        /// Maximum tokens considered per document (max_tokens_per_doc), honored by Cohere rerank models.
        /// Minimum 1; smaller values are clamped to 1. Null uses the model default.
        /// </summary>
        public int? MaxTokensPerDoc
        {
            get { return _MaxTokensPerDoc; }
            set { _MaxTokensPerDoc = value.HasValue ? Math.Max(1, value.Value) : null; }
        }

        #endregion
    }
}
