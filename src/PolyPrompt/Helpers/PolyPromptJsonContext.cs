namespace PolyPrompt.Helpers
{
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Text.Json.Serialization;
    using PolyPrompt.Models;

    /// <summary>
    /// Source-generated JSON metadata for PolyPrompt's wire shapes and public models. It lets PolyPrompt run without
    /// reflection under Native AOT and trimming, and lets applications persist conversations and responses the same way,
    /// for example <c>JsonSerializer.Serialize(messages, PolyPromptJsonContext.Default.ListChatMessage)</c>.
    /// Settings match <see cref="Serializer"/>: null properties omitted, enums as strings, dates as UTC ISO 8601, and
    /// tolerant reads. Decision questions and answers are abstract, polymorphic types and are not included.
    /// </summary>
    [JsonSourceGenerationOptions(
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        UseStringEnumConverter = true,
        Converters = new[] { typeof(UtcDateTimeConverter) })]
    // Wire shapes: values that appear inside request and response bodies.
    [JsonSerializable(typeof(object))]
    [JsonSerializable(typeof(string))]
    [JsonSerializable(typeof(bool))]
    [JsonSerializable(typeof(int))]
    [JsonSerializable(typeof(long))]
    [JsonSerializable(typeof(float))]
    [JsonSerializable(typeof(double))]
    [JsonSerializable(typeof(decimal))]
    [JsonSerializable(typeof(DateTime))]
    [JsonSerializable(typeof(DateTimeOffset))]
    [JsonSerializable(typeof(TimeSpan))]
    [JsonSerializable(typeof(Guid))]
    [JsonSerializable(typeof(JsonElement))]
    [JsonSerializable(typeof(JsonNode))]
    [JsonSerializable(typeof(Dictionary<string, object>))]
    [JsonSerializable(typeof(Dictionary<string, string>))]
    [JsonSerializable(typeof(List<object>))]
    [JsonSerializable(typeof(List<string>))]
    [JsonSerializable(typeof(List<float>))]
    [JsonSerializable(typeof(List<double>))]
    [JsonSerializable(typeof(List<List<float>>))]
    [JsonSerializable(typeof(List<Dictionary<string, object>>))]
    [JsonSerializable(typeof(List<List<Dictionary<string, object>>>))]
    [JsonSerializable(typeof(object[]))]
    [JsonSerializable(typeof(string[]))]
    [JsonSerializable(typeof(int[]))]
    [JsonSerializable(typeof(float[]))]
    [JsonSerializable(typeof(double[]))]
    // Public models.
    [JsonSerializable(typeof(ChatMessage))]
    [JsonSerializable(typeof(List<ChatMessage>))]
    [JsonSerializable(typeof(ToolCall))]
    [JsonSerializable(typeof(List<ToolCall>))]
    [JsonSerializable(typeof(ToolDefinition))]
    [JsonSerializable(typeof(List<ToolDefinition>))]
    [JsonSerializable(typeof(ToolChatRequest))]
    [JsonSerializable(typeof(CompletionOptions))]
    [JsonSerializable(typeof(ReasoningEffort))]
    [JsonSerializable(typeof(TokenUsage))]
    [JsonSerializable(typeof(CallDetail))]
    [JsonSerializable(typeof(List<CallDetail>))]
    [JsonSerializable(typeof(ChatResponse))]
    [JsonSerializable(typeof(ToolChatResponse))]
    [JsonSerializable(typeof(GenerationResponse))]
    [JsonSerializable(typeof(EmbeddingResponse))]
    [JsonSerializable(typeof(SparseEmbeddingResponse))]
    [JsonSerializable(typeof(RerankResponse))]
    [JsonSerializable(typeof(ClassificationResponse))]
    [JsonSerializable(typeof(ModelInformation))]
    [JsonSerializable(typeof(List<ModelInformation>))]
    public partial class PolyPromptJsonContext : JsonSerializerContext
    {
    }
}
