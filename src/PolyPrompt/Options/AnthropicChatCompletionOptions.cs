namespace PolyPrompt.Options
{
    using PolyPrompt.Models;

    /// <summary>
    /// Anthropic-specific options for chat completion requests.
    /// These map to fields on the Anthropic Messages API request body.
    /// </summary>
    public class AnthropicChatCompletionOptions : ChatCompletionOptions
    {
        #region Private-Members

        private int? _TopK = null;
        private List<string>? _StopSequences = null;

        #endregion

        #region Public-Members

        /// <summary>
        /// Top-K sampling (top_k). Clamped to 1..1,000. Null uses the model default. Note that current
        /// Claude models (Opus 4.7 and later) reject sampling parameters; use this only with models that
        /// accept it.
        /// </summary>
        public int? TopK
        {
            get { return _TopK; }
            set
            {
                if (value.HasValue)
                    _TopK = Math.Clamp(value.Value, 1, 1000);
                else
                    _TopK = null;
            }
        }

        /// <summary>
        /// Custom stop sequences (stop_sequences). The model stops generating when any sequence is
        /// produced. Null or empty sends no stop sequences.
        /// </summary>
        public List<string>? StopSequences
        {
            get { return _StopSequences; }
            set { _StopSequences = value; }
        }

        #endregion
    }
}
