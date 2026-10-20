namespace PolyPrompt.Helpers
{
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using PolyPrompt.Models;

    // Tagged envelopes prevent a provider's native continuation fields leaking into another protocol.
    // xAI's existing, public output-array representation remains unchanged.
    internal static class ToolTurnState
    {
        internal static void Set(ResponseBase response, string protocol, object value, string? scope = null)
        {
            var state = new Dictionary<string, object> { ["protocol"] = protocol, ["value"] = value };
            if (scope != null) state["scope"] = scope.TrimEnd('/');
            using JsonDocument document = JsonDocument.Parse(new Serializer().SerializeJson(state, false));
            response.ProviderState = document.RootElement.Clone();
        }

        internal static bool TryGet(ChatMessage message, string protocol, out JsonElement value, string? scope = null)
        {
            value = default;
            return string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                && message.ProviderState is JsonElement state && state.ValueKind == JsonValueKind.Object
                && state.TryGetProperty("protocol", out JsonElement tag) && tag.ValueKind == JsonValueKind.String
                && tag.GetString() == protocol
                && (scope == null || state.TryGetProperty("scope", out JsonElement origin)
                    && origin.ValueKind == JsonValueKind.String && origin.GetString() == scope.TrimEnd('/'))
                && state.TryGetProperty("value", out value);
        }

        internal static bool IsTagged(JsonElement state) => state.ValueKind == JsonValueKind.Object
            && state.TryGetProperty("protocol", out JsonElement tag) && tag.ValueKind == JsonValueKind.String
            && tag.GetString() is "ollama" or "openai" or "anthropic" or "bedrock" or "gemini" or "cohere";

        internal static bool IsString(object? value) => value is string || value is JsonElement e && e.ValueKind == JsonValueKind.String;

        internal static JsonObject Object(object value) => JsonNode.Parse(new Serializer().SerializeJson(value, false))!.AsObject();
        internal static void Append(JsonObject target, string key, JsonNode? value)
        {
            if (value != null) target[key] = (target[key]?.GetValue<string>() ?? "") + value.GetValue<string>();
        }

        // Anthropic streams thinking signatures separately from thinking text. Keep block indices and opaque
        // redacted blocks, and assemble tool inputs without normalizing any signed text.
        internal sealed class AnthropicBlocks
        {
            private readonly SortedDictionary<int, JsonObject> blocks = new();
            private readonly Dictionary<int, string> inputs = new();
            internal void Apply(string type, Dictionary<string, object> evt)
            {
                JsonObject e = Object(evt);
                int index = e["index"]?.GetValue<int>() ?? 0;
                if (type == "content_block_start" && e["content_block"] is JsonObject block)
                    blocks[index] = block.DeepClone().AsObject();
                else if (type == "content_block_delta" && e["delta"] is JsonObject delta && blocks.TryGetValue(index, out JsonObject? b))
                {
                    string? kind = delta["type"]?.GetValue<string>();
                    if (kind == "input_json_delta") inputs[index] = inputs.GetValueOrDefault(index, "") + delta["partial_json"]?.GetValue<string>();
                    else if (kind == "text_delta") Append(b, "text", delta["text"]);
                    else if (kind == "thinking_delta") Append(b, "thinking", delta["thinking"]);
                    else if (kind == "signature_delta") Append(b, "signature", delta["signature"]);
                }
                if (type == "content_block_stop" && inputs.TryGetValue(index, out string? input))
                    blocks[index]["input"] = JsonNode.Parse(input);
            }
            internal JsonArray Content() => new(blocks.Values.Select(b => (JsonNode)b.DeepClone()).ToArray());
        }

        internal sealed class BedrockBlocks
        {
            private readonly SortedDictionary<int, JsonObject> blocks = new();
            private readonly Dictionary<int, string> inputs = new();
            private readonly Dictionary<int, List<byte>> redacted = new();
            internal void Apply(string? type, Dictionary<string, object> payload)
            {
                JsonObject e = Object(payload);
                int index = e["contentBlockIndex"]?.GetValue<int>() ?? 0;
                if (type == "contentBlockStart" && e["start"] is JsonObject start && start["toolUse"] != null)
                    blocks[index] = new JsonObject { ["toolUse"] = start["toolUse"]!.DeepClone() };
                if (type == "contentBlockDelta" && e["delta"] is JsonObject delta)
                {
                    if (!blocks.TryGetValue(index, out JsonObject? b)) blocks[index] = b = new JsonObject();
                    if (delta["text"] != null) Append(b, "text", delta["text"]);
                    if (delta["toolUse"] is JsonObject tool)
                        inputs[index] = inputs.GetValueOrDefault(index, "") + tool["input"]?.GetValue<string>();
                    if (delta["reasoningContent"] is JsonObject reasoning)
                    {
                        JsonObject r = b["reasoningContent"] as JsonObject ?? new JsonObject();
                        if (r.Parent == null) b["reasoningContent"] = r;
                        if (reasoning["redactedContent"] != null)
                        {
                            if (!redacted.TryGetValue(index, out List<byte>? bytes)) redacted[index] = bytes = new();
                            bytes.AddRange(Convert.FromBase64String(reasoning["redactedContent"]!.GetValue<string>()));
                            r["redactedContent"] = Convert.ToBase64String(bytes.ToArray());
                        }
                        else
                        {
                            JsonObject text = r["reasoningText"] as JsonObject ?? new JsonObject();
                            if (text.Parent == null) r["reasoningText"] = text;
                            Append(text, "text", reasoning["text"]);
                            Append(text, "signature", reasoning["signature"]);
                        }
                    }
                }
                if (type == "contentBlockStop" && inputs.TryGetValue(index, out string? input))
                    blocks[index]["toolUse"]!["input"] = JsonNode.Parse(input);
            }
            internal JsonArray Content() => new(blocks.Values.Select(b => (JsonNode)b.DeepClone()).ToArray());
        }
    }
}
