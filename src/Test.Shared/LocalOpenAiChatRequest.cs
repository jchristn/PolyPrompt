namespace Test.Shared
{
    internal sealed class LocalOpenAiChatRequest
    {
        public string? Model { get; set; }

        public List<LocalOpenAiChatMessage>? Messages { get; set; }

        public List<LocalOpenAiTool>? Tools { get; set; }

        public string? ToolChoice { get; set; }

        public bool? Stream { get; set; }

        // OpenAI reasoning_effort (top-level string).
        public string? ReasoningEffort { get; set; }

        // Ollama think (top-level). Populated as a string when a level ("low"/"medium"/"high") is sent,
        // and as a bool when true/false is sent.
        public string? Think { get; set; }

        public bool? ThinkBool { get; set; }
    }
}
