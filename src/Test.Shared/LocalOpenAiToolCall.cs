namespace Test.Shared
{
    internal sealed class LocalOpenAiToolCall
    {
        public string? Id { get; set; }

        public string? Type { get; set; }

        public LocalOpenAiToolFunction? Function { get; set; }
    }
}
