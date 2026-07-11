namespace PolyPrompt.Models
{
    /// <summary>
    /// Message used by tool-capable chat requests.
    /// Content may be null for assistant messages that contain tool calls.
    /// </summary>
    public class ChatMessage
    {
        #region Public-Members

        /// <summary>
        /// Message role. Common values are system, user, assistant, and tool.
        /// </summary>
        public string Role { get; set; } = "user";

        /// <summary>
        /// Text content for the message. May be null for assistant tool-call messages.
        /// </summary>
        public string? Content { get; set; } = null;

        /// <summary>
        /// Tool call identifier for tool-result messages. May be null for providers that do not use tool call IDs.
        /// </summary>
        public string? ToolCallId { get; set; } = null;

        /// <summary>
        /// Tool/function name for tool-result messages. Required by Gemini and ignored by providers that only need ToolCallId.
        /// </summary>
        public string? ToolName { get; set; } = null;

        /// <summary>
        /// Tool calls associated with an assistant message. Empty for normal text messages.
        /// </summary>
        public List<ToolCall> ToolCalls { get; set; } = new List<ToolCall>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create a system message.
        /// </summary>
        /// <param name="content">System instructions.</param>
        /// <returns>A system chat message.</returns>
        /// <exception cref="ArgumentNullException">Thrown when content is null.</exception>
        public static ChatMessage System(string content)
        {
            ArgumentNullException.ThrowIfNull(content);
            return new ChatMessage { Role = "system", Content = content };
        }

        /// <summary>
        /// Create a user message.
        /// </summary>
        /// <param name="content">User text.</param>
        /// <returns>A user chat message.</returns>
        /// <exception cref="ArgumentNullException">Thrown when content is null.</exception>
        public static ChatMessage User(string content)
        {
            ArgumentNullException.ThrowIfNull(content);
            return new ChatMessage { Role = "user", Content = content };
        }

        /// <summary>
        /// Create an assistant text message.
        /// </summary>
        /// <param name="content">Assistant text.</param>
        /// <returns>An assistant chat message.</returns>
        /// <exception cref="ArgumentNullException">Thrown when content is null.</exception>
        public static ChatMessage Assistant(string content)
        {
            ArgumentNullException.ThrowIfNull(content);
            return new ChatMessage { Role = "assistant", Content = content };
        }

        /// <summary>
        /// Create an assistant message containing tool calls.
        /// </summary>
        /// <param name="toolCalls">Tool calls requested by the assistant.</param>
        /// <returns>An assistant chat message containing tool calls.</returns>
        /// <exception cref="ArgumentNullException">Thrown when toolCalls is null.</exception>
        public static ChatMessage AssistantToolCalls(IEnumerable<ToolCall> toolCalls)
        {
            ArgumentNullException.ThrowIfNull(toolCalls);

            ChatMessage message = new ChatMessage { Role = "assistant" };
            message.ToolCalls.AddRange(toolCalls);
            return message;
        }

        /// <summary>
        /// Create a tool result message.
        /// </summary>
        /// <param name="toolCallId">Tool call identifier returned by the model. May be null for providers that do not emit one.</param>
        /// <param name="toolName">Tool/function name. Required by Gemini function-response messages.</param>
        /// <param name="content">Serialized tool result content.</param>
        /// <returns>A tool result chat message.</returns>
        /// <exception cref="ArgumentNullException">Thrown when toolName or content is null.</exception>
        public static ChatMessage ToolResult(string? toolCallId, string toolName, string content)
        {
            ArgumentNullException.ThrowIfNull(toolName);
            ArgumentNullException.ThrowIfNull(content);

            return new ChatMessage
            {
                Role = "tool",
                ToolCallId = toolCallId,
                ToolName = toolName,
                Content = content
            };
        }

        #endregion
    }
}
