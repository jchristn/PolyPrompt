namespace Test.Shared
{
    internal sealed class LocalGeminiGenerationConfig
    {
        public int? MaxOutputTokens { get; set; }

        public double? Temperature { get; set; }

        public double? TopP { get; set; }
    }
}
