namespace Test.Shared
{
    internal sealed class LocalGeminiRequest
    {
        public List<LocalGeminiContent>? Contents { get; set; }

        public List<LocalGeminiTool>? Tools { get; set; }

        public LocalGeminiContent? SystemInstruction { get; set; }

        public LocalGeminiGenerationConfig? GenerationConfig { get; set; }
    }
}
