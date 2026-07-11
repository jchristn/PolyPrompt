namespace Test.Shared
{
    internal sealed class LocalEmbeddingRequest
    {
        public string? Model { get; set; }

        public List<string>? Input { get; set; }

        public int? Dimensions { get; set; }

        public string? EncodingFormat { get; set; }

        public int? Truncate { get; set; }

        public Dictionary<string, object>? Options { get; set; }
    }
}
