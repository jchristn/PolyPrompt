namespace PolyPrompt.Options
{
    using PolyPrompt.Models;

    /// <summary>
    /// Ollama-specific options for embedding requests.
    /// These map to fields in the Ollama /api/embed request body.
    /// </summary>
    public class OllamaEmbeddingOptions : EmbeddingOptions
    {
        #region Private-Members

        private int? _Truncate = null;
        private int? _ContextLength = null;

        #endregion

        #region Public-Members

        /// <summary>
        /// Truncate input to this many tokens. Null uses model default.
        /// </summary>
        public int? Truncate
        {
            get { return _Truncate; }
            set { _Truncate = value; }
        }

        /// <summary>
        /// Context window size (num_ctx). Clamped to 1..2,097,152. Null uses model default.
        /// </summary>
        public int? ContextLength
        {
            get { return _ContextLength; }
            set
            {
                if (value.HasValue)
                    _ContextLength = Math.Clamp(value.Value, 1, 2_097_152);
                else
                    _ContextLength = null;
            }
        }

        #endregion
    }
}
