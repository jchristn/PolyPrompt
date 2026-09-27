namespace PolyPrompt.Options
{
    using PolyPrompt.Models;

    /// <summary>
    /// Cohere-specific options for text generation requests. Cohere retired its legacy generate endpoint, so
    /// generation is sent as a single-turn /v2/chat request and these map to fields on that request body.
    /// </summary>
    public class CohereGenerationOptions : GenerationOptions
    {
        #region Private-Members

        private int? _TopK = null;
        private double? _FrequencyPenalty = null;
        private double? _PresencePenalty = null;
        private List<string>? _StopSequences = null;

        #endregion

        #region Public-Members

        /// <summary>
        /// Top-K sampling (k). Clamped to 0..500, where 0 disables top-K. Null uses the model default (0).
        /// </summary>
        public int? TopK
        {
            get { return _TopK; }
            set { _TopK = value.HasValue ? Math.Clamp(value.Value, 0, 500) : null; }
        }

        /// <summary>
        /// Random seed for deterministic sampling (seed). Null lets the provider choose.
        /// </summary>
        public int? Seed { get; set; } = null;

        /// <summary>
        /// Frequency penalty (frequency_penalty). Clamped to 0.0..1.0. Null uses the model default (0.0).
        /// </summary>
        public double? FrequencyPenalty
        {
            get { return _FrequencyPenalty; }
            set { _FrequencyPenalty = value.HasValue ? Math.Clamp(value.Value, 0.0, 1.0) : null; }
        }

        /// <summary>
        /// Presence penalty (presence_penalty). Clamped to 0.0..1.0. Null uses the model default (0.0).
        /// </summary>
        public double? PresencePenalty
        {
            get { return _PresencePenalty; }
            set { _PresencePenalty = value.HasValue ? Math.Clamp(value.Value, 0.0, 1.0) : null; }
        }

        /// <summary>
        /// Custom stop sequences (stop_sequences). Cohere accepts at most 5; only the first 5 are kept.
        /// Null or empty sends no stop sequences.
        /// </summary>
        public List<string>? StopSequences
        {
            get { return _StopSequences; }
            set { _StopSequences = value == null ? null : value.Take(5).ToList(); }
        }

        #endregion
    }
}
