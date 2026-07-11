namespace Test.Shared
{
    internal sealed class LocalGenerateRequest
    {
        public string? Model { get; set; }

        public string? Prompt { get; set; }

        public int? MaxTokens { get; set; }

        public bool? Stream { get; set; }

        public double? Temperature { get; set; }

        public double? TopP { get; set; }

        public Dictionary<string, object>? Options { get; set; }
    }
}
