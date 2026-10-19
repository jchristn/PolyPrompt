namespace PolyPrompt.Clients
{
    using System.Diagnostics;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Text.Json;
    using PolyPrompt.Models;

    public partial class XAiCompletionClient
    {
        private static async IAsyncEnumerable<XAiResponseEvent> ReadSse(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken token)
        {
            using Stream input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using StreamReader reader = new(input);
            StringBuilder data = new();
            string? eventName = null;
            while (true)
            {
                string? line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                if (line == null || line.Length == 0)
                {
                    if (data.Length > 0)
                    {
                        string json = data.ToString();
                        if (json.Trim() == "[DONE]") yield break;
                        JsonElement root = XAiProtocol.Parse(json);
                        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("xAI SSE data must be a JSON object.");
                        string type = XAiProtocol.String(root, "type") ?? eventName ?? "unknown";
                        yield return new XAiResponseEvent { Type = type, Data = root };
                    }
                    data.Clear(); eventName = null;
                    if (line == null) yield break;
                    continue;
                }
                if (line.StartsWith(':')) continue;
                int separator = line.IndexOf(':');
                string field = separator >= 0 ? line[..separator] : line;
                string value = separator >= 0 ? line[(separator + 1)..] : "";
                if (value.StartsWith(' ')) value = value[1..];
                if (field == "event") eventName = value;
                else if (field == "data")
                {
                    if (data.Length > 0) data.Append('\n');
                    data.Append(value);
                }
            }
        }

        private static bool Terminal(string type) => type is "response.completed" or "response.incomplete" or "response.failed" or "response.cancelled";
        private static async IAsyncEnumerable<XAiResponseEvent> ReadEvents(ResponseBase result, HttpResponseMessage response, [EnumeratorCancellation] CancellationToken token)
        {
            await using IAsyncEnumerator<XAiResponseEvent> events = ReadSse(response, token).GetAsyncEnumerator(token);
            while (true)
            {
                bool more;
                try { more = await events.MoveNextAsync().ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { result.Success = false; result.Error = "xAI stream failed: " + ex.Message; yield break; }
                if (!more) { result.Success = false; result.Error = "xAI stream ended before a terminal response event."; yield break; }
                XAiResponseEvent item = events.Current;
                if (item.Type == "error")
                {
                    result.Success = false;
                    result.Error = ErrorMessage(XAiProtocol.Property(item.Data, "error") ?? item.Data);
                    yield return item;
                    yield break;
                }
                yield return item;
                if (Terminal(item.Type)) yield break;
            }
        }

        private async IAsyncEnumerable<ToolChatStreamingChunk> ReadToolChunks(ToolChatStreamingResponse result, HttpResponseMessage response, [EnumeratorCancellation] CancellationToken token)
        {
            StringBuilder text = new(), reasoning = new();
            Dictionary<string, int> indices = new(StringComparer.Ordinal);
            await foreach (XAiResponseEvent evt in ReadEvents(result, response, token).ConfigureAwait(false))
            {
                JsonElement data = evt.Data;
                ToolChatStreamingChunk chunk = new();
                if (evt.Type == "response.output_text.delta" || evt.Type == "response.refusal.delta")
                {
                    chunk.Text = XAiProtocol.String(data, "delta"); text.Append(chunk.Text);
                }
                else if (evt.Type is "response.reasoning_summary_text.delta" or "response.reasoning_text.delta")
                {
                    chunk.ReasoningText = XAiProtocol.String(data, "delta"); reasoning.Append(chunk.ReasoningText);
                }
                else if (evt.Type is "response.output_item.added" or "response.output_item.done")
                {
                    JsonElement item = XAiProtocol.Property(data, "item") ?? default;
                    if (XAiProtocol.String(item, "type") == "function_call")
                    {
                        int index = Index(data, indices);
                        string? itemId = XAiProtocol.String(item, "id");
                        if (itemId != null) indices[itemId] = index;
                        string? arguments = XAiProtocol.String(item, "arguments");
                        chunk.ToolCallDeltas.Add(new ToolCallDelta { Index = index, Id = XAiProtocol.String(item, "call_id"), Name = XAiProtocol.String(item, "name"), Type = "function", ArgumentsJson = string.IsNullOrEmpty(arguments) ? null : arguments });
                    }
                }
                else if (evt.Type is "response.function_call_arguments.delta" or "response.function_call_arguments.done")
                {
                    chunk.ToolCallDeltas.Add(new ToolCallDelta
                    {
                        Index = Index(data, indices), Type = "function",
                        ArgumentsJsonDelta = evt.Type.EndsWith(".delta", StringComparison.Ordinal) ? XAiProtocol.String(data, "delta") : null,
                        ArgumentsJson = evt.Type.EndsWith(".done", StringComparison.Ordinal) ? XAiProtocol.String(data, "arguments") : null
                    });
                }
                else if (Terminal(evt.Type))
                {
                    JsonElement final = XAiProtocol.Property(data, "response") ?? default;
                    XAiResponse parsed = new();
                    ParseResponse(final.ValueKind == JsonValueKind.Undefined ? "{}" : final.GetRawText(), parsed);
                    result.ProviderState = parsed.ProviderState;
                    result.Model = parsed.Model ?? result.Model;
                    if (parsed.Error != null)
                    {
                        result.Success = false; result.Error = parsed.Error; result.ToolCalls.Clear(); yield break;
                    }
                    chunk.Text = Suffix(parsed.Text, text.ToString());
                    chunk.ReasoningText = Suffix(parsed.Reasoning, reasoning.ToString());
                    chunk.ResponseId = parsed.ResponseId; chunk.FinishReason = parsed.FinishReason; chunk.Usage = parsed.Usage; chunk.Model = parsed.Model;
                    int index = 0;
                    foreach (JsonElement item in parsed.ProviderState!.Value.EnumerateArray())
                    {
                        if (XAiProtocol.String(item, "type") == "function_call")
                            chunk.ToolCallDeltas.Add(new ToolCallDelta { Index = index, Type = "function", Id = XAiProtocol.String(item, "call_id"), Name = XAiProtocol.String(item, "name"), ArgumentsJson = XAiProtocol.String(item, "arguments") });
                        index++;
                    }
                    chunk.Done = true;
                }
                else if (evt.Type == "response.created" || evt.Type == "response.in_progress")
                {
                    JsonElement metadata = XAiProtocol.Property(data, "response") ?? default;
                    chunk.ResponseId = XAiProtocol.String(metadata, "id"); chunk.Model = XAiProtocol.String(metadata, "model");
                }
                if (!result.Success) { result.ToolCalls.Clear(); yield break; }
                yield return chunk;
            }
            if (!result.Success) result.ToolCalls.Clear();
        }

        private static int Index(JsonElement data, Dictionary<string, int> indices)
        {
            if (XAiProtocol.Property(data, "output_index") is JsonElement value && value.TryGetInt32(out int index)) return index;
            string? id = XAiProtocol.String(data, "item_id");
            if (id != null && indices.TryGetValue(id, out index)) return index;
            return 0;
        }
        private static string? Suffix(string? complete, string emitted) => complete != null && complete.StartsWith(emitted, StringComparison.Ordinal) ? complete[emitted.Length..] : null;

        private async IAsyncEnumerable<ChatStreamingChunk> ReadChatChunks(ChatStreamingResponse result, HttpResponseMessage response, [EnumeratorCancellation] CancellationToken token)
        {
            ToolChatStreamingResponse projected = new() { Success = true, Model = result.Model };
            try
            {
                await foreach (ToolChatStreamingChunk chunk in ReadToolChunks(projected, response, token).ConfigureAwait(false))
                    yield return new ChatStreamingChunk { Text = chunk.Text, ReasoningText = chunk.ReasoningText, Usage = chunk.Usage, ResponseId = chunk.ResponseId, FinishReason = chunk.FinishReason, Done = chunk.Done, Model = chunk.Model };
            }
            finally { result.ProviderState = projected.ProviderState; result.Success = projected.Success; result.Error = projected.Error; result.Model = projected.Model; }
        }
        private async IAsyncEnumerable<GenerationStreamingChunk> ReadGenerationChunks(GenerationStreamingResponse result, HttpResponseMessage response, [EnumeratorCancellation] CancellationToken token)
        {
            ToolChatStreamingResponse projected = new() { Success = true, Model = result.Model };
            try
            {
                await foreach (ToolChatStreamingChunk chunk in ReadToolChunks(projected, response, token).ConfigureAwait(false))
                    yield return new GenerationStreamingChunk { Text = chunk.Text, ReasoningText = chunk.ReasoningText, Usage = chunk.Usage, ResponseId = chunk.ResponseId, FinishReason = chunk.FinishReason, Done = chunk.Done, Model = chunk.Model };
            }
            finally { result.ProviderState = projected.ProviderState; result.Success = projected.Success; result.Error = projected.Error; result.Model = projected.Model; }
        }
        private async IAsyncEnumerable<XAiResponseEvent> ReadNativeEvents(XAiStreamingResponse result, StreamingHttpResult stream, Stopwatch watch, [EnumeratorCancellation] CancellationToken token = default)
        {
            using (stream)
            using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(stream.Token, token))
            {
                try
                {
                    await foreach (XAiResponseEvent evt in ReadEvents(result, stream.Response, linked.Token).ConfigureAwait(false))
                    {
                        if (Terminal(evt.Type))
                        {
                            XAiResponse final = new();
                            JsonElement payload = XAiProtocol.Property(evt.Data, "response") ?? default;
                            ParseResponse(payload.ValueKind == JsonValueKind.Undefined ? "{}" : payload.GetRawText(), final);
                            final.Success = final.Error == null;
                            final.StatusCode = result.StatusCode;
                            final.OverallRuntimeMs = watch.ElapsedMilliseconds;
                            result.Response = final; result.ProviderState = final.ProviderState;
                            result.Model = final.Model ?? result.Model;
                            result.Success = final.Success; result.Error = final.Error;
                        }
                        yield return evt;
                    }
                }
                finally { watch.Stop(); result.OverallRuntimeMs = watch.ElapsedMilliseconds; }
            }
        }
    }
}
