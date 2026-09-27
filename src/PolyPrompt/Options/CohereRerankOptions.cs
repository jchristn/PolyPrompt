namespace PolyPrompt.Options
{
    using PolyPrompt.Models;

    /// <summary>
    /// Cohere-specific options for rerank requests.
    /// These map to fields on the Cohere /v2/rerank request body.
    /// </summary>
    public class CohereRerankOptions : RerankOptions
    {
        #region Private-Members

        private int? _MaxTokensPerDoc = null;

        #endregion

        #region Public-Members

        /// <summary>
        /// Maximum tokens considered per document (max_tokens_per_doc); longer documents are truncated.
        /// Minimum 1; smaller values are clamped to 1. Null uses the provider default (4096).
        /// </summary>
        public int? MaxTokensPerDoc
        {
            get { return _MaxTokensPerDoc; }
            set { _MaxTokensPerDoc = value.HasValue ? Math.Max(1, value.Value) : null; }
        }

        #endregion
    }
}
