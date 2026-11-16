namespace PolyPrompt.Models
{
    /// <summary>
    /// A chat request: messages, optional tools, and per-call settings. With no tools it is an ordinary multi-turn chat;
    /// tools require <see cref="Capability.ToolCalling"/>.
    /// </summary>
    public class ChatRequest
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

        #region Internal-Members

        /// <summary>
        /// True when the request came from <c>ChatAsync(string)</c>. Providers whose 4.x single-prompt body differs from
        /// their message body (the Responses API sends <c>input</c> as a string with <c>instructions</c>) keep that body.
        /// </summary>
        internal bool FromPrompt { get; set; }

        #endregion
    }
}
