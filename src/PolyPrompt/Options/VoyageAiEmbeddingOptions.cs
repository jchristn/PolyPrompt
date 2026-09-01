namespace PolyPrompt.Options
{
    using PolyPrompt.Models;

    /// <summary>
    /// VoyageAI-specific options for embedding requests.
    /// These map to fields in the VoyageAI /v1/embeddings request body.
    /// </summary>
    public class VoyageAiEmbeddingOptions : EmbeddingOptions
    {
        #region Private-Members

        private string? _InputType = null;
        private int? _OutputDimension = null;
        private string? _OutputDtype = null;

        // Accepted values per https://docs.voyageai.com. A value outside its set reverts to null so the
        // field is omitted and the provider default applies — the same normalize-or-revert idiom used by
        // the ReasoningEffort overrides.
        private static readonly HashSet<string> _InputTypes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "query", "document" };

        private static readonly HashSet<string> _OutputDtypes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "float", "int8", "uint8", "binary", "ubinary" };

        private static readonly HashSet<int> _OutputDimensions =
            new HashSet<int> { 256, 512, 1024, 2048 };

        #endregion

        #region Public-Members

        /// <summary>
        /// Input type hint (input_type). Valid values are "query" and "document"; values are normalized
        /// (trimmed, lower-cased) and an unrecognized value reverts to null. Null omits the field so
        /// embeddings are generated without a retrieval-role prefix.
        /// </summary>
        public string? InputType
        {
            get { return _InputType; }
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    _InputType = null;
                    return;
                }

                string normalized = value.Trim().ToLowerInvariant();
                _InputType = _InputTypes.Contains(normalized) ? normalized : null;
            }
        }

        /// <summary>
        /// Whether over-length inputs are truncated to the model context length (truncation). Null omits
        /// the field and uses the provider default (true). When false, over-length inputs cause an error.
        /// </summary>
        public bool? Truncation { get; set; } = null;

        /// <summary>
        /// Output embedding dimension (output_dimension). Valid values are 256, 512, 1024, and 2048 on the
        /// models that support Matryoshka embeddings; a value outside that set reverts to null. Null omits
        /// the field and uses the model default dimension.
        /// </summary>
        public int? OutputDimension
        {
            get { return _OutputDimension; }
            set
            {
                if (!value.HasValue)
                {
                    _OutputDimension = null;
                    return;
                }

                _OutputDimension = _OutputDimensions.Contains(value.Value) ? value : null;
            }
        }

        /// <summary>
        /// Output value data type (output_dtype). Valid values are "float" (default), "int8", "uint8",
        /// "binary", and "ubinary"; values are normalized (trimmed, lower-cased) and an unrecognized value
        /// reverts to null. Null omits the field. Quantized outputs are surfaced through the same float
        /// vector on <see cref="EmbeddingResult"/>.
        /// </summary>
        public string? OutputDtype
        {
            get { return _OutputDtype; }
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    _OutputDtype = null;
                    return;
                }

                string normalized = value.Trim().ToLowerInvariant();
                _OutputDtype = _OutputDtypes.Contains(normalized) ? normalized : null;
            }
        }

        #endregion
    }
}
