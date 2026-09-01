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

        // VoyageAI request fields.
        public string? InputType { get; set; }

        public bool? Truncation { get; set; }

        public int? OutputDimension { get; set; }

        public string? OutputDtype { get; set; }
    }
}
