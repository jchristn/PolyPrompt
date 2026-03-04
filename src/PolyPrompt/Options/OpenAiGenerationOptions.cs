namespace PolyPrompt.Options
{
    using PolyPrompt.Models;

    /// <summary>
    /// OpenAI-specific options for text generation requests.
    /// These map to fields in the OpenAI /v1/completions request body.
    /// </summary>
    public class OpenAiGenerationOptions : GenerationOptions
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
        /// Echo the prompt back in the response. Null uses API default (false).
        /// </summary>
        public bool? Echo { get; set; } = null;

        /// <summary>
        /// Suffix to append after the generated text.
        /// </summary>
        public string? Suffix { get; set; } = null;

        /// <summary>
        /// Include log probabilities. Null uses API default.
        /// </summary>
        public int? Logprobs { get; set; } = null;

        #endregion
    }
}
