namespace PolyPrompt.Clients
{
    using System.Text;
    using System.Text.Json;
    using PolyPrompt.Models;

    public partial class XAiCompletionClient
    {
        private static void ParseResponse<T>(string json, T result) where T : ResponseBase
        {
            try
            {
                JsonElement root = XAiProtocol.Parse(json);
                string? id = XAiProtocol.String(root, "id");
                JsonElement? output = XAiProtocol.Property(root, "output");
                string? status = XAiProtocol.String(root, "status");
                if (root.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(id) || output?.ValueKind != JsonValueKind.Array || string.IsNullOrEmpty(status))
                { result.Error = "Malformed xAI response: id, status, and output array are required."; return; }
                result.Model = XAiProtocol.String(root, "model") ?? result.Model;
                result.ProviderState = output.Value.Clone();
                if (result is XAiResponse native) { native.RawResponse = root; native.Status = status; }
                JsonElement? error = XAiProtocol.Property(root, "error");
                if (status is "failed" or "cancelled" || (error.HasValue && error.Value.ValueKind != JsonValueKind.Null))
                { result.Error = error.HasValue ? ErrorMessage(error.Value) : "xAI response " + status; return; }
                if (status is not ("completed" or "incomplete" or "in_progress")) { result.Error = "Unknown xAI response status: " + status; return; }
                StringBuilder text = new(), reasoning = new();
                List<ToolCall> calls = new();
                foreach (JsonElement item in output.Value.EnumerateArray())
                {
                    string? type = XAiProtocol.String(item, "type");
                    if (type == "message")
                    {
                        JsonElement? content = XAiProtocol.Property(item, "content");
                        if (content?.ValueKind != JsonValueKind.Array) { result.Error = "xAI message content must be an array."; return; }
                        foreach (JsonElement part in content.Value.EnumerateArray())
                        {
                            if (XAiProtocol.String(part, "type") == "output_text") text.Append(XAiProtocol.String(part, "text"));
                            else if (XAiProtocol.String(part, "type") == "refusal") text.Append(XAiProtocol.String(part, "refusal"));
                        }
                    }
                    else if (type == "reasoning")
                    {
                        JsonElement? parts = XAiProtocol.Property(item, "summary");
                        if (parts?.ValueKind != JsonValueKind.Array || parts.Value.GetArrayLength() == 0) parts = XAiProtocol.Property(item, "content");
                        if (parts?.ValueKind == JsonValueKind.Array)
                            foreach (JsonElement part in parts.Value.EnumerateArray()) reasoning.Append(XAiProtocol.String(part, "text"));
                    }
                    else if (type == "function_call")
                    {
                        if (status == "in_progress") continue; // Retrieval may include unfinished calls; keep them opaque until completion.
                        string? callId = XAiProtocol.String(item, "call_id"), name = XAiProtocol.String(item, "name"), arguments = XAiProtocol.String(item, "arguments");
                        if (string.IsNullOrWhiteSpace(callId) || string.IsNullOrWhiteSpace(name) || arguments == null)
                        { result.Error = "Malformed xAI function call: call_id, name, and arguments are required."; return; }
                        if (status == "incomplete" && XAiProtocol.String(item, "status") != "completed")
                        { result.Error = "xAI returned an incomplete function call; it must not be executed."; return; }
                        using JsonDocument args = JsonDocument.Parse(arguments);
                        if (args.RootElement.ValueKind != JsonValueKind.Object) { result.Error = "xAI function arguments must be a JSON object."; return; }
                        if (calls.Any(c => c.Id == callId)) { result.Error = "Duplicate xAI function call_id: " + callId; return; }
                        calls.Add(new ToolCall { Id = callId, Name = name, ArgumentsJson = arguments });
                    }
                    // Provider-hosted calls, annotations, encrypted outputs, compaction, and future item types
                    // stay intact in ProviderState/RawResponse and are never mistaken for local functions.
                }
                TokenUsage? usage = ParseUsage(XAiProtocol.Property(root, "usage"));
                string finish = status == "incomplete"
                    ? (XAiProtocol.String(XAiProtocol.Property(root, "incomplete_details") ?? default, "reason") == "max_output_tokens" ? "length" : "incomplete")
                    : status == "in_progress" ? status : calls.Count > 0 ? "tool_calls" : "stop";
                string? answer = text.Length > 0 ? text.ToString() : null;
                string? thought = reasoning.Length > 0 ? reasoning.ToString() : null;
                if (result is ChatResponse chat) { chat.Text = answer; chat.Reasoning = thought; chat.Usage = usage; }
                else if (result is ToolChatResponse tool) { tool.Text = answer; tool.Reasoning = thought; tool.Usage = usage; tool.ToolCalls = calls; tool.ResponseId = id; tool.FinishReason = finish; }
                else if (result is GenerationResponse generation) { generation.Text = answer; generation.Reasoning = thought; generation.Usage = usage; generation.ResponseId = id; generation.FinishReason = finish; }
                // ChatResponse already has these metadata fields, alongside the streaming result types.
                if (result is ChatResponse c) { c.ResponseId = id; c.FinishReason = finish; }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
            { result.Error = "Malformed xAI response: " + ex.Message; }
        }

        private static TokenUsage? ParseUsage(JsonElement? usage)
        {
            if (usage?.ValueKind != JsonValueKind.Object) return null;
            int? Integer(JsonElement element, string name) => XAiProtocol.Property(element, name) is JsonElement value && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) ? number : null;
            return new TokenUsage
            {
                PromptTokens = Integer(usage.Value, "input_tokens"),
                CompletionTokens = Integer(usage.Value, "output_tokens"),
                TotalTokens = Integer(usage.Value, "total_tokens"),
                CachedPromptTokens = Integer(XAiProtocol.Property(usage.Value, "input_tokens_details") ?? default, "cached_tokens"),
                ReasoningTokens = Integer(XAiProtocol.Property(usage.Value, "output_tokens_details") ?? default, "reasoning_tokens")
            };
        }
        private static string ErrorMessage(JsonElement error) => XAiProtocol.String(error, "message") ?? error.ToString();
    }
}
