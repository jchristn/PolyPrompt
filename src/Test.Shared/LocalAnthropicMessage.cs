namespace Test.Shared
{
    internal sealed class LocalAnthropicMessage
    {
        public string? Role { get; set; }

        // Populated when the message content is a plain string.
        public string? Text { get; set; }

        // Populated when the message content is a list of content blocks.
        public List<LocalAnthropicContentBlock>? Blocks { get; set; }
    }
}
