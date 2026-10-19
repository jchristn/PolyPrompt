namespace PolyPrompt.Clients
{
    using System.Net.Http.Headers;
    using System.Text.Json;

    internal static class XAiProtocol
    {
        internal const string Endpoint = "https://api.x.ai";
        internal static string RequireKey(string apiKey)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
            if (apiKey.Any(char.IsWhiteSpace)) throw new ArgumentException("Bearer token must not contain whitespace.", nameof(apiKey));
            return apiKey;
        }
        internal static string Url(string endpoint, string path) => OpenAiProtocol.BuildApiUrl(endpoint, path);
        internal static void Auth(HttpRequestMessage request, string? key) => request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        internal static string? String(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        internal static JsonElement? Property(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) ? value : null;
        internal static JsonElement Parse(string json) { using JsonDocument document = JsonDocument.Parse(json); return document.RootElement.Clone(); }
    }
}
