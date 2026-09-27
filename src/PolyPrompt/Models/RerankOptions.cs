namespace PolyPrompt.Models
{
    /// <summary>
    /// Base options for rerank requests. When a value is null, the client instance default is used.
    /// </summary>
    public class RerankOptions
    {
        #region Private-Members

        private int? _TopN = null;

        #endregion

        #region Public-Members

        /// <summary>
        /// Model override for this rerank request. Null uses the client's rerank model default (for example
        /// <c>CohereClient.RerankModel</c>). Ignored by Text Embeddings Inference, which serves a single model.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Maximum number of results to return, highest score first. Null returns a score for every document.
        /// Minimum 1. It must also not exceed the number of documents passed to RerankAsync, which is checked
        /// when the request is made. Providers without a native top-N parameter (Text Embeddings Inference)
        /// score every document and the list is trimmed client-side.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when set to a value less than 1.</exception>
        public int? TopN
        {
            get { return _TopN; }
            set
            {
                if (value.HasValue && value.Value < 1)
                    throw new ArgumentOutOfRangeException(nameof(TopN), value.Value, "TopN must be at least 1.");
                _TopN = value;
            }
        }

        /// <summary>
        /// When true, each <see cref="RerankResult.Document"/> is populated with the text of the document it
        /// scores. The text is taken from the caller's input list by index, so no extra data is requested
        /// from the provider. Default: false.
        /// </summary>
        public bool ReturnDocuments { get; set; } = false;

        #endregion
    }
}
