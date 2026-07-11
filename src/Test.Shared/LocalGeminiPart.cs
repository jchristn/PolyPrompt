namespace Test.Shared
{
    internal sealed class LocalGeminiPart
    {
        public string? Text { get; set; }

        public LocalGeminiFunctionCall? FunctionCall { get; set; }

        public LocalGeminiFunctionResponse? FunctionResponse { get; set; }
    }
}
