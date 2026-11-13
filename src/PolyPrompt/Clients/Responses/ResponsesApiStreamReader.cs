namespace PolyPrompt.Clients
{
    using System.Diagnostics;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Text.Json;
    using PolyPrompt.Models;

    // Streaming for the shared Responses API engine: SSE events, chunk projection, tool-call assembly, terminal handling.
    public abstract partial class ResponsesApiClientBase
    {
        #region Streaming

        private async IAsyncEnumerable<ResponsesApiEvent> ReadSse(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken token)
        {
            string name = Dialect.Name;
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
                        JsonElement root = ResponsesApiJson.Parse(json);
                        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException(name + " SSE data must be a JSON object.");
                        string type = ResponsesApiJson.String(root, "type") ?? eventName ?? "unknown";
                        yield return new ResponsesApiEvent { Type = type, Kind = ResponsesApiEvent.KindOf(type), Data = root };
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

        private async IAsyncEnumerable<ResponsesApiEvent> ReadEvents(ResponseBase result, HttpResponseMessage response, [EnumeratorCancellation] CancellationToken token)
        {
            string name = Dialect.Name;
            await using IAsyncEnumerator<ResponsesApiEvent> events = ReadSse(response, token).GetAsyncEnumerator(token);
            while (true)
            {
                bool more;
                try { more = await events.MoveNextAsync().ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { result.Success = false; result.Error = name + " stream failed: " + ex.Message; yield break; }
                if (!more) { result.Success = false; result.Error = name + " stream ended before a terminal response event."; yield break; }
                ResponsesApiEvent item = events.Current;
                if (item.Type == "error")
                {
                    result.Success = false;
                    result.Error = ErrorMessage(ResponsesApiJson.Property(item.Data, "error") ?? item.Data);
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
            await foreach (ResponsesApiEvent evt in ReadEvents(result, response, token).ConfigureAwait(false))
            {
                JsonElement data = evt.Data;
                ToolChatStreamingChunk chunk = new();
                if (evt.Type == "response.output_text.delta" || evt.Type == "response.refusal.delta")
                {
                    chunk.Text = ResponsesApiJson.String(data, "delta"); text.Append(chunk.Text);
                }
                else if (evt.Type is "response.reasoning_summary_text.delta" or "response.reasoning_text.delta")
                {
                    chunk.ReasoningText = ResponsesApiJson.String(data, "delta"); reasoning.Append(chunk.ReasoningText);
                }
                else if (evt.Type is "response.output_item.added" or "response.output_item.done")
                {
                    JsonElement item = ResponsesApiJson.Property(data, "item") ?? default;
                    if (ResponsesApiJson.String(item, "type") == "function_call")
                    {
                        int index = Index(data, indices);
                        string? itemId = ResponsesApiJson.String(item, "id");
                        if (itemId != null) indices[itemId] = index;
                        string? arguments = ResponsesApiJson.String(item, "arguments");
                        chunk.ToolCallDeltas.Add(new ToolCallDelta { Index = index, Id = ResponsesApiJson.String(item, "call_id"), Name = ResponsesApiJson.String(item, "name"), Type = "function", ArgumentsJson = string.IsNullOrEmpty(arguments) ? null : arguments });
                    }
                }
                else if (evt.Type is "response.function_call_arguments.delta" or "response.function_call_arguments.done")
                {
                    chunk.ToolCallDeltas.Add(new ToolCallDelta
                    {
                        Index = Index(data, indices), Type = "function",
                        ArgumentsJsonDelta = evt.Type.EndsWith(".delta", StringComparison.Ordinal) ? ResponsesApiJson.String(data, "delta") : null,
                        ArgumentsJson = evt.Type.EndsWith(".done", StringComparison.Ordinal) ? ResponsesApiJson.String(data, "arguments") : null
                    });
                }
                else if (Terminal(evt.Type))
                {
                    JsonElement final = ResponsesApiJson.Property(data, "response") ?? default;
                    ResponsesApiResponse parsed = new();
                    ParseResponse(final.ValueKind == JsonValueKind.Undefined ? "{}" : final.GetRawText(), parsed);
                    result.ProviderState = parsed.ProviderState;
                    result.RawResponse = parsed.RawResponse;
                    result.Citations = parsed.Citations;
                    result.Model = parsed.Model ?? result.Model;
                    if (parsed.Error != null)
                    {
                        result.Success = false; result.Error = parsed.Error; result.ToolCalls.Clear(); yield break;
                    }
                    chunk.Text = Suffix(parsed.Text, text.ToString());
                    chunk.ReasoningText = Suffix(parsed.Reasoning, reasoning.ToString());
                    chunk.ResponseId = parsed.ResponseId; chunk.FinishReason = parsed.FinishReason; chunk.Usage = parsed.Usage; chunk.Model = parsed.Model;
                    int index = 0;
                    JsonElement outputItems = ResponsesApiJson.Property(final, "output") ?? default;
                    if (outputItems.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement item in outputItems.EnumerateArray())
                        {
                            if (ResponsesApiJson.String(item, "type") == "function_call")
                                chunk.ToolCallDeltas.Add(new ToolCallDelta { Index = index, Type = "function", Id = ResponsesApiJson.String(item, "call_id"), Name = ResponsesApiJson.String(item, "name"), ArgumentsJson = ResponsesApiJson.String(item, "arguments") });
                            index++;
                        }
                    chunk.Done = true;
                }
                else if (evt.Type == "response.created" || evt.Type == "response.in_progress")
                {
                    JsonElement metadata = ResponsesApiJson.Property(data, "response") ?? default;
                    chunk.ResponseId = ResponsesApiJson.String(metadata, "id"); chunk.Model = ResponsesApiJson.String(metadata, "model");
                }
                if (!result.Success) { result.ToolCalls.Clear(); yield break; }
                yield return chunk;
            }
            if (!result.Success) result.ToolCalls.Clear();
        }

        private static int Index(JsonElement data, Dictionary<string, int> indices)
        {
            if (ResponsesApiJson.Property(data, "output_index") is JsonElement value && value.TryGetInt32(out int index)) return index;
            string? id = ResponsesApiJson.String(data, "item_id");
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
            finally { result.ProviderState = projected.ProviderState; result.RawResponse = projected.RawResponse; result.Citations = projected.Citations; result.Success = projected.Success; result.Error = projected.Error; result.Model = projected.Model; }
        }

        private async IAsyncEnumerable<GenerationStreamingChunk> ReadGenerationChunks(GenerationStreamingResponse result, HttpResponseMessage response, [EnumeratorCancellation] CancellationToken token)
        {
            ToolChatStreamingResponse projected = new() { Success = true, Model = result.Model };
            try
            {
                await foreach (ToolChatStreamingChunk chunk in ReadToolChunks(projected, response, token).ConfigureAwait(false))
                    yield return new GenerationStreamingChunk { Text = chunk.Text, ReasoningText = chunk.ReasoningText, Usage = chunk.Usage, ResponseId = chunk.ResponseId, FinishReason = chunk.FinishReason, Done = chunk.Done, Model = chunk.Model };
            }
            finally { result.ProviderState = projected.ProviderState; result.RawResponse = projected.RawResponse; result.Citations = projected.Citations; result.Success = projected.Success; result.Error = projected.Error; result.Model = projected.Model; }
        }

        private async IAsyncEnumerable<ResponsesApiEvent> ReadNativeEvents(ResponsesApiStreamingResponse result, StreamingHttpResult stream, Stopwatch watch, bool stored, [EnumeratorCancellation] CancellationToken token = default)
        {
            using (stream)
            using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(stream.Token, token))
            {
                try
                {
                    await foreach (ResponsesApiEvent evt in ReadEvents(result, stream.Response, linked.Token).ConfigureAwait(false))
                    {
                        if (Terminal(evt.Type))
                        {
                            ResponsesApiResponse final = new() { Stored = stored };
                            JsonElement payload = ResponsesApiJson.Property(evt.Data, "response") ?? default;
                            ParseResponse(payload.ValueKind == JsonValueKind.Undefined ? "{}" : payload.GetRawText(), final);
                            final.Success = final.Error == null;
                            final.StatusCode = result.StatusCode;
                            final.OverallRuntimeMs = watch.ElapsedMilliseconds;
                            final.AppliedReasoningEffort = result.AppliedReasoningEffort;
                            result.Response = final; result.ProviderState = final.ProviderState;
                            result.RawResponse = final.RawResponse; result.Citations = final.Citations;
                            result.Model = final.Model ?? result.Model;
                            result.Success = final.Success; result.Error = final.Error;
                        }
                        yield return evt;
                    }
                }
                finally { watch.Stop(); result.OverallRuntimeMs = watch.ElapsedMilliseconds; }
            }
        }

        #endregion
    }
}
