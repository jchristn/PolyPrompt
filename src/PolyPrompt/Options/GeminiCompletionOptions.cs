namespace PolyPrompt.Options
{
    using PolyPrompt.Models;

    /// <summary>
    /// Gemini settings for chat, tool chat, and generation, mapped to <c>generationConfig</c> and the tool declarations of
    /// the <c>generateContent</c> request. Also used by Vertex AI. Use this type for a Gemini completion client's
    /// <c>Defaults</c> or per call; per-call values override the defaults field by field.
    /// </summary>
    public class GeminiCompletionOptions : CompletionOptions
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

        /// <summary>
        /// How tool parameter schemas are sent. Null means <see cref="GeminiToolSchemaMode.JsonSchema"/>: the schema is sent
        /// unchanged (apart from a root <c>$schema</c> keyword) in <c>parametersJsonSchema</c>, which accepts standard JSON
        /// Schema. Use <see cref="GeminiToolSchemaMode.OpenApiSubset"/> for endpoints that only accept the older
        /// <c>parameters</c> field; the schema is then reduced to the OpenAPI subset accepted there. Default: null.
        /// </summary>
        public GeminiToolSchemaMode? ToolSchemaMode { get; set; } = null;

        #endregion
    }
}
