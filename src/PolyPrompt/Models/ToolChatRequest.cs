namespace PolyPrompt.Models
{
    /// <summary>
    /// Request for a message-based chat completion that may use tools. With no tools it is an ordinary multi-turn chat.
    /// </summary>
    public class ToolChatRequest
    {
        #region Public-Members

        /// <summary>
        /// Conversation messages. At least one message is required.
        /// </summary>
        public List<ChatMessage> Messages { get; set; } = new List<ChatMessage>();

        /// <summary>
        /// Tool definitions available to the model. Empty for a plain multi-turn chat.
        /// </summary>
        public List<ToolDefinition> Tools { get; set; } = new List<ToolDefinition>();

        /// <summary>
        /// Tool choice directive. Common values are auto, none, and required. Null uses provider default behavior.
        /// Default: auto.
        /// </summary>
        public string? ToolChoice { get; set; } = "auto";

        /// <summary>
        /// Per-call completion settings (model, token limit, sampling, reasoning effort, and provider-specific settings
        /// when a provider options type is used). Null uses the client's <c>Defaults</c>. Default: null.
        /// </summary>
        public CompletionOptions? Options { get; set; } = null;

        #endregion
    }
}
