namespace PolyPrompt.Models
{
    /// <summary>
    /// Settings for completion operations (chat, tool chat, and text generation), shared by every provider.
    /// The same type serves two roles: a completion client's <c>Defaults</c>, and the per-call options passed to an
    /// operation. For each setting, a non-null per-call value wins, otherwise the client default is used, otherwise
    /// the provider's own default applies (no field is sent). Provider-specific settings live on derived types such
    /// as <c>OllamaCompletionOptions</c>, which merge the same way.
    /// </summary>
    public class CompletionOptions
    {
        #region Private-Members

        private double? _Temperature = null;
        private double? _TopP = null;
        private int? _MaxTokens = null;

        #endregion

        #region Public-Members

        /// <summary>
        /// Model name. On a client's <c>Defaults</c> this is the client's model (the client's <c>Model</c> property reads
        /// and writes it); per call it overrides that model. Default: null.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Maximum number of tokens to generate. Clamped to 1..10,000,000. When no value is set anywhere, 4096 is sent.
        /// Default: null.
        /// </summary>
        public int? MaxTokens
        {
            get { return _MaxTokens; }
            set
            {
                if (value.HasValue)
                    _MaxTokens = Math.Clamp(value.Value, 1, 10_000_000);
                else
                    _MaxTokens = null;
            }
        }

        /// <summary>
        /// Sampling temperature. Clamped to 0.0..2.0. Null sends no value, so the provider default applies. Default: null.
        /// </summary>
        public double? Temperature
        {
            get { return _Temperature; }
            set
            {
                if (value.HasValue)
                    _Temperature = Math.Clamp(value.Value, 0.0, 2.0);
                else
                    _Temperature = null;
            }
        }

        /// <summary>
        /// Nucleus sampling (top-p). Clamped to 0.0..1.0. Null sends no value, so the provider default applies. Default: null.
        /// </summary>
        public double? TopP
        {
            get { return _TopP; }
            set
            {
                if (value.HasValue)
                    _TopP = Math.Clamp(value.Value, 0.0, 1.0);
                else
                    _TopP = null;
            }
        }

        /// <summary>
        /// System prompt. Sent with chat calls, and with tool chat calls whose messages contain no system message.
        /// Text generation sends its prompt unchanged and ignores this setting. Default: null.
        /// </summary>
        public string? SystemPrompt { get; set; } = null;

        /// <summary>
        /// Reasoning ("thinking") effort for reasoning-capable models, applied to chat and tool chat. Null sends no
        /// reasoning field, so the provider's own default applies. <see cref="ReasoningEffortLevel.Minimal"/> turns
        /// thinking off where the provider allows it (Ollama <c>think: false</c>, a Gemini thinking budget of 0, Cohere
        /// and Bedrock thinking disabled); other levels map to each provider's effort or budget as described on
        /// <see cref="Models.ReasoningEffort"/>. Text generation ignores this setting. Default: null.
        /// </summary>
        public ReasoningEffort? ReasoningEffort { get; set; } = null;

        #endregion
    }
}
