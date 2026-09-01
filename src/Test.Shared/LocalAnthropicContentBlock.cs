namespace Test.Shared
{
    internal sealed class LocalAnthropicContentBlock
    {
        public string? Type { get; set; }

        public string? Text { get; set; }

        public string? Id { get; set; }

        public string? Name { get; set; }

        // Serialized tool_use input object, when present.
        public string? InputJson { get; set; }

        public string? ToolUseId { get; set; }

        // tool_result content, when present as a string.
        public string? Content { get; set; }
    }
}
