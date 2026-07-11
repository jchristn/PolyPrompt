namespace Test.Shared
{
    internal sealed class LocalOpenAiChatMessage
    {
        public string? Role { get; set; }

        public string? Content { get; set; }

        public string? ToolCallId { get; set; }

        public List<LocalOpenAiToolCall>? ToolCalls { get; set; }
    }
}
