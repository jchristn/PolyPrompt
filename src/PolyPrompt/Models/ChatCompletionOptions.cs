namespace PolyPrompt.Models
{
    /// <summary>
    /// Base options for chat completion requests.
    /// Per-call overrides for properties that are also available on the client instance.
    /// When a value is null, the client instance default is used.
    /// </summary>
    public class ChatCompletionOptions
    {
        #region Private-Members

        private double? _Temperature = null;
        private double? _TopP = null;
        private int? _MaxTokens = null;

        #endregion

        #region Public-Members

        /// <summary>
        /// Sampling temperature override. Clamped to 0.0..2.0. Null uses client default.
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
        /// Nucleus sampling (top-p) override. Clamped to 0.0..1.0. Null uses client default.
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
        /// Max tokens override. Clamped to 1..10,000,000. Null uses client default.
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
        /// System prompt override. Null uses client default.
        /// </summary>
        public string? SystemPrompt { get; set; } = null;

        #endregion
    }
}
