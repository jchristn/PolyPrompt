namespace PolyPrompt.Options
{
    using PolyPrompt.Models;

    /// <summary>
    /// OpenAI settings for chat, tool chat, and generation, mapped to the <c>/v1/chat/completions</c> and
    /// <c>/v1/completions</c> request bodies. Also used by Azure OpenAI. Use this type for an OpenAI completion client's
    /// <c>Defaults</c> or per call; per-call values override the defaults field by field.
    /// </summary>
    public class OpenAiCompletionOptions : CompletionOptions
    {
        #region Private-Members

        private double? _FrequencyPenalty = null;
        private double? _PresencePenalty = null;
        private int? _Seed = null;

        #endregion

        #region Public-Members

        /// <summary>
        /// Frequency penalty. Clamped to -2.0..2.0. Null uses API default (0).
        /// </summary>
        public double? FrequencyPenalty
        {
            get { return _FrequencyPenalty; }
            set
            {
                if (value.HasValue)
                    _FrequencyPenalty = Math.Clamp(value.Value, -2.0, 2.0);
                else
                    _FrequencyPenalty = null;
            }
        }

        /// <summary>
        /// Presence penalty. Clamped to -2.0..2.0. Null uses API default (0).
        /// </summary>
        public double? PresencePenalty
        {
            get { return _PresencePenalty; }
            set
            {
                if (value.HasValue)
                    _PresencePenalty = Math.Clamp(value.Value, -2.0, 2.0);
                else
                    _PresencePenalty = null;
            }
        }

        /// <summary>
        /// Random seed for reproducibility. Null uses random seed.
        /// </summary>
        public int? Seed
        {
            get { return _Seed; }
            set { _Seed = value; }
        }

        /// <summary>
        /// Echo the prompt back in the response. Text generation (<c>/v1/completions</c>) only. Null uses API default (false).
        /// </summary>
        public bool? Echo { get; set; } = null;

        /// <summary>
        /// Suffix to append after the generated text. Text generation (<c>/v1/completions</c>) only. Default: null.
        /// </summary>
        public string? Suffix { get; set; } = null;

        /// <summary>
        /// Number of log probabilities to include. Text generation (<c>/v1/completions</c>) only. Null uses API default.
        /// </summary>
        public int? Logprobs { get; set; } = null;

        #endregion
    }
}
