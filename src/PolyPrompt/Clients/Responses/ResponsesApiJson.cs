namespace PolyPrompt.Clients
{
    using System.Text.Json;

    /// <summary>
    /// JSON helpers for the Responses API engine.
    /// </summary>
    internal static class ResponsesApiJson
    {
        internal static string? String(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        internal static JsonElement? Property(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) ? value : null;
        internal static int? Int(JsonElement element, string name) => Property(element, name) is JsonElement value && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) ? number : null;
        internal static JsonElement Parse(string json) { using JsonDocument document = JsonDocument.Parse(json); return document.RootElement.Clone(); }
    }
}
