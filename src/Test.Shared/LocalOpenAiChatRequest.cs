namespace Test.Shared
{
    internal sealed class LocalOpenAiChatRequest
    {
        public string? Model { get; set; }

        public List<LocalOpenAiChatMessage>? Messages { get; set; }

        public List<LocalOpenAiTool>? Tools { get; set; }

        public string? ToolChoice { get; set; }

        public bool? Stream { get; set; }
    }
}
