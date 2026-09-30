namespace PolyPrompt.Models
{
    /// <summary>
    /// Settings for rerank operations, shared by every provider. The same type serves as a rerank client's
    /// <c>Defaults</c> and as per-call options; a non-null per-call value wins over the client default.
    /// </summary>
    public class RerankOptions
    {
        #region Private-Members

        private int? _TopN = null;

        #endregion

        #region Public-Members

        /// <summary>
        /// Model name. On a client's <c>Defaults</c> this is the client's model; per call it overrides that model.
        /// Ignored by Text Embeddings Inference, which serves a single model. Default: null.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Maximum number of results to return, highest score first. Null returns a score for every document.
        /// Minimum 1. A per-call value cannot exceed the number of documents passed to RerankAsync, which is checked
        /// when the request is made; a value from the client's <c>Defaults</c> is instead capped at the number of
        /// documents. Providers without a native top-N parameter (Text Embeddings Inference) score every document
        /// and the list is trimmed client-side. Default: null.
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
        /// from the provider. Null means false unless the client's <c>Defaults</c> sets it. Default: null.
        /// </summary>
        public bool? ReturnDocuments { get; set; } = null;

        #endregion
    }
}
