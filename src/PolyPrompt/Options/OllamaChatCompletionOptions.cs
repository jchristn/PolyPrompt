namespace PolyPrompt.Options
{
    using PolyPrompt.Models;

    /// <summary>
    /// Ollama-specific options for chat completion requests.
    /// These map to fields in the Ollama /api/chat request body under "options".
    /// </summary>
    public class OllamaChatCompletionOptions : ChatCompletionOptions
    {
        #region Private-Members

        private int? _ContextLength = null;
        private int? _TopK = null;
        private double? _RepeatPenalty = null;
        private int? _Seed = null;
        private double? _MinP = null;
        private int? _RepeatLastN = null;

        #endregion

        #region Public-Members

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
        /// Repeat penalty. Clamped to 0.0..10.0. Null uses model default.
        /// </summary>
        public double? RepeatPenalty
        {
            get { return _RepeatPenalty; }
            set
            {
                if (value.HasValue)
                    _RepeatPenalty = Math.Clamp(value.Value, 0.0, 10.0);
                else
                    _RepeatPenalty = null;
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
        /// Minimum probability threshold (min_p). Clamped to 0.0..1.0. Null uses model default.
        /// </summary>
        public double? MinP
        {
            get { return _MinP; }
            set
            {
                if (value.HasValue)
                    _MinP = Math.Clamp(value.Value, 0.0, 1.0);
                else
                    _MinP = null;
            }
        }

        /// <summary>
        /// Number of tokens to look back for repeat penalty (repeat_last_n). Clamped to 0..4096. Null uses model default.
        /// </summary>
        public int? RepeatLastN
        {
            get { return _RepeatLastN; }
            set
            {
                if (value.HasValue)
                    _RepeatLastN = Math.Clamp(value.Value, 0, 4096);
                else
                    _RepeatLastN = null;
            }
        }

        #endregion
    }
}
