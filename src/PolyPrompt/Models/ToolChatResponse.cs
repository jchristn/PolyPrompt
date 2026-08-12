namespace PolyPrompt.Models
{
    /// <summary>
    /// Response from a tool-capable chat completion request.
    /// A response may contain text, tool calls, or both depending on the provider and model.
    /// </summary>
    public class ToolChatResponse
    {
        #region Public-Members

        /// <summary>
        /// Assistant text returned by the model. May be null when the model requested tool calls.
        /// </summary>
        public string? Text { get; set; } = null;

        /// <summary>
        /// Reasoning ("thinking") content returned by the model, kept separate from <see cref="Text"/>. Null
        /// when the model produced no reasoning. Returned to the caller for display or logging; it is not
        /// carried into a follow-up request.
        /// </summary>
        public string? Reasoning { get; set; } = null;

        /// <summary>
        /// Tool calls requested by the model.
        /// </summary>
        public List<ToolCall> ToolCalls { get; set; } = new List<ToolCall>();

        /// <summary>
        /// The model name used for this completion.
        /// </summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>
        /// Provider response identifier for correlation. May be null when the provider does not emit one.
        /// </summary>
        public string? ResponseId { get; set; } = null;

        /// <summary>
        /// Provider finish reason. Common values include stop, tool_calls, length, STOP, and MAX_TOKENS.
        /// </summary>
        public string? FinishReason { get; set; } = null;

        /// <summary>
        /// Whether the request succeeded.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// HTTP status code from the request.
        /// </summary>
        public int? StatusCode { get; set; } = null;

        /// <summary>
        /// Error message when Success is false.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Overall runtime of the request in milliseconds.
        /// </summary>
        public long OverallRuntimeMs { get; set; }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Convert this response into an assistant message that can be appended to the next ToolChatRequest.
        /// </summary>
        /// <returns>An assistant message containing either text or tool calls.</returns>
        public ChatMessage ToAssistantMessage()
        {
            if (ToolCalls != null && ToolCalls.Any())
            {
                return ChatMessage.AssistantToolCalls(ToolCalls);
            }

            return ChatMessage.Assistant(Text ?? string.Empty);
        }

        #endregion
    }
}
