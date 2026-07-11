namespace Test.Shared
{
    internal sealed class LocalOpenAiToolFunction
    {
        public string? Name { get; set; }

        public string? Description { get; set; }

        public Dictionary<string, object>? Parameters { get; set; }

        public string? ArgumentsJson { get; set; }
    }
}
