namespace PolyPrompt.Models
{
    /// <summary>
    /// Request for a tool-capable chat completion.
    /// </summary>
    public class ToolChatRequest
    {
        #region Private-Members

        private double? _Temperature = null;
        private double? _TopP = null;
        private int? _MaxTokens = null;

        #endregion

        #region Public-Members

        /// <summary>
        /// Optional model override. Null uses the client Model property.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Conversation messages. At least one message is required.
        /// </summary>
        public List<ChatMessage> Messages { get; set; } = new List<ChatMessage>();

        /// <summary>
        /// Tool definitions available to the model.
        /// </summary>
        public List<ToolDefinition> Tools { get; set; } = new List<ToolDefinition>();

        /// <summary>
        /// Tool choice directive. Common values are auto, none, and required. Null uses provider default behavior.
        /// </summary>
        public string? ToolChoice { get; set; } = "auto";

        /// <summary>
        /// Sampling temperature override. Clamped to 0.0..2.0. Null uses the client default.
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
        /// Nucleus sampling top-p override. Clamped to 0.0..1.0. Null uses the client default.
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
        /// Max tokens override. Clamped to 1..10,000,000. Null uses the client default.
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
        /// Optional reasoning effort for reasoning-capable models. Null uses the client default, and when
        /// both are null no reasoning field is sent (the provider default is used).
        /// </summary>
        public ReasoningEffort? ReasoningEffort { get; set; } = null;

        #endregion
    }
}
