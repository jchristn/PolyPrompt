namespace Test.Shared
{
    internal sealed class LocalGeminiGenerationConfig
    {
        public int? MaxOutputTokens { get; set; }

        public double? Temperature { get; set; }

        public double? TopP { get; set; }

        // Flattened from generationConfig.thinkingConfig.thinkingBudget for convenient assertions.
        public int? ThinkingBudget { get; set; }
    }
}
