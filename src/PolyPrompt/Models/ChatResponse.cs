namespace PolyPrompt.Models
{
    /// <summary>
    /// Response from a chat request: assistant text, reasoning, and any tool calls.
    /// A response may contain text, tool calls, or both depending on the provider and model.
    /// </summary>
    public class ChatResponse : ResponseBase
    {
        #region Public-Members

        /// <summary>
        /// Assistant text returned by the model. May be null when the model requested tool calls.
        /// </summary>
        public string? Text { get; set; } = null;

        /// <summary>
        /// Reasoning ("thinking") content returned by the model, kept separate from <see cref="Text"/>. Null
        /// when the model produced no reasoning. Returned for display or logging. Provider-specific continuation state is preserved separately
        /// in ProviderState and carried by ToAssistantMessage().
        /// </summary>
        public string? Reasoning { get; set; } = null;

        /// <summary>
        /// Tool calls requested by the model.
        /// </summary>
        public List<ToolCall> ToolCalls { get; set; } = new List<ToolCall>();

        /// <summary>
        /// Provider response identifier for correlation. May be null when the provider does not emit one.
        /// </summary>
        public string? ResponseId { get; set; } = null;

        /// <summary>
        /// Provider finish reason. Common values include stop, tool_calls, length, STOP, and MAX_TOKENS.
        /// </summary>
        public string? FinishReason { get; set; } = null;

        /// <summary>
        /// Token usage for this completion, when the provider reported it. Populated on both the streaming
        /// and non-streaming paths so cost and cache accounting is identical regardless of which was used.
        /// Null when the provider returned no usage data.
        /// </summary>
        public TokenUsage? Usage { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Convert this response into an assistant message that can be appended to the next ChatRequest.
        /// </summary>
        /// <returns>An assistant message containing either text or tool calls.</returns>
        public ChatMessage ToAssistantMessage()
        {
            return new ChatMessage
            {
                Role = "assistant",
                Text = Text,
                ToolCalls = ToolCalls ?? new List<ToolCall>(),
                ProviderState = ProviderState?.Clone()
            };
        }

        #endregion
    }
}
