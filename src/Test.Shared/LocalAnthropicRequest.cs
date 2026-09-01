namespace Test.Shared
{
    internal sealed class LocalAnthropicRequest
    {
        public string? Model { get; set; }

        public int? MaxTokens { get; set; }

        public bool? Stream { get; set; }

        public string? System { get; set; }

        public double? Temperature { get; set; }

        public double? TopP { get; set; }

        public int? TopK { get; set; }

        public List<string>? StopSequences { get; set; }

        public List<LocalAnthropicMessage>? Messages { get; set; }

        public List<LocalAnthropicTool>? Tools { get; set; }

        public LocalAnthropicToolChoice? ToolChoice { get; set; }

        public LocalAnthropicThinking? Thinking { get; set; }

        public LocalAnthropicOutputConfig? OutputConfig { get; set; }
    }
}
