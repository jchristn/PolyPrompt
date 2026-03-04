namespace PolyPrompt.Options
{
    using PolyPrompt.Models;

    /// <summary>
    /// Gemini-specific options for chat completion requests.
    /// These map to fields in the Gemini generationConfig.
    /// </summary>
    public class GeminiChatCompletionOptions : ChatCompletionOptions
    {
        #region Private-Members

        private int? _TopK = null;
        private int? _CandidateCount = null;
        private double? _PresencePenalty = null;
        private double? _FrequencyPenalty = null;

        #endregion

        #region Public-Members

        /// <summary>
        /// Top-K sampling. Clamped to 1..1,000. Null uses model default.
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
        /// Number of candidate responses to generate. Clamped to 1..8. Null uses default (1).
        /// </summary>
        public int? CandidateCount
        {
            get { return _CandidateCount; }
            set
            {
                if (value.HasValue)
                    _CandidateCount = Math.Clamp(value.Value, 1, 8);
                else
                    _CandidateCount = null;
            }
        }

        /// <summary>
        /// Presence penalty. Clamped to -2.0..2.0. Null uses model default.
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
        /// Frequency penalty. Clamped to -2.0..2.0. Null uses model default.
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

        #endregion
    }
}
