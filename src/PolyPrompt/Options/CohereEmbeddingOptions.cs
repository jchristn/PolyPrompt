namespace PolyPrompt.Options
{
    using PolyPrompt.Models;

    /// <summary>
    /// Cohere-specific options for embedding requests.
    /// These map to fields on the Cohere /v2/embed request body.
    /// </summary>
    public class CohereEmbeddingOptions : EmbeddingOptions
    {
        #region Private-Members

        private string? _InputType = null;
        private string? _EmbeddingType = null;
        private int? _OutputDimension = null;
        private string? _Truncate = null;

        // Accepted values per https://docs.cohere.com/reference/embed. A value outside its set reverts to
        // null so the client default applies.
        private static readonly HashSet<string> _InputTypes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "search_document", "search_query", "classification", "clustering" };

        private static readonly HashSet<string> _EmbeddingTypes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "float", "int8", "uint8", "binary", "ubinary" };

        private static readonly HashSet<int> _OutputDimensions = new HashSet<int> { 256, 512, 1024, 1536 };

        private static readonly HashSet<string> _TruncateValues =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "NONE", "START", "END" };

        #endregion

        #region Public-Members

        /// <summary>
        /// Input type (input_type). Valid values are "search_document", "search_query", "classification",
        /// and "clustering"; values are normalized (trimmed, lower-cased) and an unrecognized value reverts
        /// to null. Cohere requires input_type for v3 and later embedding models, so when this is null the
        /// client sends "search_document".
        /// </summary>
        public string? InputType
        {
            get { return _InputType; }
            set { _InputType = Normalize(value, _InputTypes, false); }
        }

        /// <summary>
        /// Embedding value type to request and parse (embedding_types). Valid values are "float" (default),
        /// "int8", "uint8", "binary", and "ubinary"; values are normalized and an unrecognized value reverts
        /// to null. Null requests "float". Quantized values are surfaced through the same float vector on
        /// <see cref="EmbeddingResult"/>.
        /// </summary>
        public string? EmbeddingType
        {
            get { return _EmbeddingType; }
            set { _EmbeddingType = Normalize(value, _EmbeddingTypes, false); }
        }

        /// <summary>
        /// Output embedding dimension (output_dimension), supported by embed-v4.0 and later. Valid values are
        /// 256, 512, 1024, and 1536; a value outside that set reverts to null. Null uses the model default.
        /// </summary>
        public int? OutputDimension
        {
            get { return _OutputDimension; }
            set { _OutputDimension = value.HasValue && _OutputDimensions.Contains(value.Value) ? value : null; }
        }

        /// <summary>
        /// How over-length inputs are handled (truncate). Valid values are "NONE" (return an error), "START",
        /// and "END"; values are normalized (trimmed, upper-cased) and an unrecognized value reverts to null.
        /// Null uses the provider default ("END").
        /// </summary>
        public string? Truncate
        {
            get { return _Truncate; }
            set { _Truncate = Normalize(value, _TruncateValues, true); }
        }

        #endregion

        #region Private-Methods

        private static string? Normalize(string? value, HashSet<string> allowed, bool upper)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            string normalized = upper ? value.Trim().ToUpperInvariant() : value.Trim().ToLowerInvariant();
            return allowed.Contains(normalized) ? normalized : null;
        }

        #endregion
    }
}
