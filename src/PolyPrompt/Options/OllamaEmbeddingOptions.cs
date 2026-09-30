namespace PolyPrompt.Options
{
    using PolyPrompt.Models;

    /// <summary>
    /// Ollama settings for embedding requests, mapped to the Ollama <c>/api/embed</c> request body. Use this type for an
    /// Ollama embedding client's <c>Defaults</c> or per call.
    /// </summary>
    public class OllamaEmbeddingOptions : EmbeddingOptions
    {
        #region Private-Members

        private bool? _Truncate = null;
        private int? _ContextLength = null;

        #endregion

        #region Public-Members

        /// <summary>
        /// Whether Ollama truncates inputs that exceed the context length (true) or returns an error (false).
        /// Null uses the server default (true).
        /// </summary>
        public bool? Truncate
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
