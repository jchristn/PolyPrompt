namespace PolyPrompt.Helpers
{
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using PolyPrompt.Models;

    /// <summary>
    /// Reads <see cref="ChatMessage.Content"/> from either a JSON string (the 4.x shape, read as one text part), null
    /// (no content), or an array of parts, and always writes an array. Uses source-generated metadata only, so it is
    /// safe under Native AOT and trimming.
    /// </summary>
    public sealed class ContentPartListJsonConverter : JsonConverter<List<ContentPart>>
    {
        /// <inheritdoc />
        public override bool HandleNull => true;

        /// <inheritdoc />
        public override List<ContentPart> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Null:
                    return new List<ContentPart>();
                case JsonTokenType.String:
                    string? text = reader.GetString();
                    return text == null ? new List<ContentPart>() : new List<ContentPart> { ContentPart.FromText(text) };
                case JsonTokenType.StartArray:
                    return JsonSerializer.Deserialize(ref reader, PolyPromptJsonContext.Default.ListContentPart) ?? new List<ContentPart>();
                default:
                    throw new JsonException("ChatMessage.Content must be a string, null, or an array of content parts.");
            }
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, List<ContentPart> value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value ?? new List<ContentPart>(), PolyPromptJsonContext.Default.ListContentPart);
        }
    }
}
