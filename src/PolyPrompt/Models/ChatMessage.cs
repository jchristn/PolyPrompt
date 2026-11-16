namespace PolyPrompt.Models
{
    using System.Text.Json.Serialization;
    using PolyPrompt.Helpers;

    /// <summary>
    /// One message in a conversation. Content is a list of parts: text, and on user messages images, audio, and video
    /// (see <see cref="ContentPart"/>). Assistant messages that only request tools have no content.
    /// </summary>
    public class ChatMessage
    {
        #region Public-Members

        /// <summary>Opaque native continuation state for this assistant turn. Preserve unchanged; incompatible protocols ignore it.</summary>
        public System.Text.Json.JsonElement? ProviderState { get; set; }


        /// <summary>
        /// Message role. Common values are system, user, assistant, and tool.
        /// </summary>
        public string Role { get; set; } = "user";

        /// <summary>
        /// The message content, in order. Empty for assistant tool-call messages. Media parts are accepted only on user
        /// messages. When deserializing, a JSON string (the 4.x shape) is read as a single text part.
        /// </summary>
        [JsonConverter(typeof(ContentPartListJsonConverter))]
        public List<ContentPart> Content
        {
            get => _Content;
            set => _Content = value ?? new List<ContentPart>();
        }

        /// <summary>
        /// The message's text: its text parts joined with a newline, or null when it has none. Setting it replaces all
        /// content with a single text part (null clears the content).
        /// </summary>
        [JsonIgnore]
        public string? Text
        {
            get
            {
                string? single = null;
                List<string>? many = null;
                foreach (ContentPart part in _Content)
                {
                    if (part == null || part.Type != ContentPartType.Text || part.Text == null) continue;
                    if (single == null && many == null) { single = part.Text; continue; }
                    many ??= new List<string> { single! };
                    many.Add(part.Text);
                }
                return many != null ? string.Join("\n", many) : single;
            }
            set
            {
                _Content = new List<ContentPart>();
                if (value != null) _Content.Add(ContentPart.FromText(value));
            }
        }

        /// <summary>
        /// True when the content holds an image, audio, or video part.
        /// </summary>
        [JsonIgnore]
        public bool HasMedia => _Content.Any(p => p != null && p.Type != ContentPartType.Text);

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

        #region Private-Members

        private List<ContentPart> _Content = new List<ContentPart>();

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
            return new ChatMessage { Role = "system", Text = content };
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
            return new ChatMessage { Role = "user", Text = content };
        }

        /// <summary>
        /// Create a user message with text followed by media parts.
        /// </summary>
        /// <param name="text">User text, sent first.</param>
        /// <param name="media">Image, audio, or video parts, sent after the text in order.</param>
        /// <returns>A user chat message.</returns>
        /// <exception cref="ArgumentNullException">Thrown when text or media is null.</exception>
        /// <exception cref="ArgumentException">Thrown when a part is null.</exception>
        public static ChatMessage User(string text, params ContentPart[] media)
        {
            ArgumentNullException.ThrowIfNull(text);
            ArgumentNullException.ThrowIfNull(media);
            ChatMessage message = new ChatMessage { Role = "user", Text = text };
            foreach (ContentPart part in media)
            {
                if (part == null) throw new ArgumentException("Media parts cannot be null.", nameof(media));
                message._Content.Add(part);
            }
            return message;
        }

        /// <summary>
        /// Create a user message from content parts, in order.
        /// </summary>
        /// <param name="content">Text and media parts.</param>
        /// <returns>A user chat message.</returns>
        /// <exception cref="ArgumentNullException">Thrown when content is null.</exception>
        /// <exception cref="ArgumentException">Thrown when a part is null.</exception>
        public static ChatMessage User(IEnumerable<ContentPart> content)
        {
            ArgumentNullException.ThrowIfNull(content);
            ChatMessage message = new ChatMessage { Role = "user" };
            foreach (ContentPart part in content)
            {
                if (part == null) throw new ArgumentException("Content parts cannot be null.", nameof(content));
                message._Content.Add(part);
            }
            return message;
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
            return new ChatMessage { Role = "assistant", Text = content };
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
                Text = content
            };
        }

        #endregion
    }
}
