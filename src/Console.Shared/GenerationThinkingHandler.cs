namespace PolyPrompt.ConsoleSupport;

using System.Net;
using System.Text;
using System.Text.Json;

/// <summary>
/// Observes reasoning returned by native generation APIs without changing PolyPrompt's public generation models.
/// The original bytes still flow unchanged into the provider client.
/// </summary>
public sealed class GenerationThinkingHandler() : DelegatingHandler(new HttpClientHandler())
{
    internal static readonly AsyncLocal<ConsoleRenderer?> Current = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        ConsoleRenderer? renderer = Current.Value;
        HttpResponseMessage response = await base.SendAsync(request, token);
        if (renderer == null || !response.IsSuccessStatusCode) return response;
        HttpContent original = response.Content;
        bool streaming = original.Headers.ContentType?.MediaType is "text/event-stream" or "application/x-ndjson" or "application/ndjson";
        if (streaming)
        {
            HttpContent wrapped = new StreamContent(new ThinkingStream(await original.ReadAsStreamAsync(token), renderer, original));
            foreach (var header in original.Headers) wrapped.Headers.TryAddWithoutValidation(header.Key, header.Value);
            response.Content = wrapped;
        }
        else Observe(await original.ReadAsStringAsync(token), renderer);
        return response;
    }

    private static void Observe(string json, ConsoleRenderer renderer)
    {
        json = json.Trim();
        if (json.StartsWith("data:", StringComparison.Ordinal)) json = json[5..].Trim();
        if (json.Length == 0 || json == "[DONE]") return;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            ObserveMessage(root, renderer);
            if (root.TryGetProperty("message", out JsonElement message)) ObserveMessage(message, renderer);
            if (root.TryGetProperty("delta", out JsonElement delta))
            {
                ObserveMessage(delta, renderer);
                if (delta.TryGetProperty("message", out message)) ObserveMessage(message, renderer);
            }
            JsonElement content;
            if (root.TryGetProperty("candidates", out JsonElement candidates) && candidates.ValueKind == JsonValueKind.Array)
                foreach (JsonElement candidate in candidates.EnumerateArray())
                    if (candidate.TryGetProperty("content", out content) && content.TryGetProperty("parts", out JsonElement parts))
                        foreach (JsonElement part in parts.EnumerateArray())
                            if (part.TryGetProperty("thought", out JsonElement thought) && thought.ValueKind == JsonValueKind.True && part.TryGetProperty("text", out JsonElement text))
                                renderer.Thinking(text.GetString());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { } // Framing and error handling remain the provider client's responsibility.
    }

    private static void ObserveMessage(JsonElement node, ConsoleRenderer renderer)
    {
        if (node.ValueKind != JsonValueKind.Object) return;
        foreach (string name in new[] { "thinking", "reasoning_content", "tool_plan" })
            if (node.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
                renderer.Thinking(value.GetString());
        if (!node.TryGetProperty("content", out JsonElement content)) return;
        if (content.ValueKind == JsonValueKind.Object) ObserveMessage(content, renderer);
        else if (content.ValueKind == JsonValueKind.Array)
            foreach (JsonElement part in content.EnumerateArray()) ObserveMessage(part, renderer);
    }

    private sealed class ThinkingStream(Stream inner, ConsoleRenderer renderer, HttpContent owner) : Stream
    {
        private readonly MemoryStream _Line = new();
        private bool _Overlong;
        private bool _Ended;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = inner.Read(buffer, offset, count);
            Inspect(buffer.AsSpan(offset, read), read == 0);
            return read;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            int read = await inner.ReadAsync(buffer, token);
            Inspect(buffer.Span[..read], read == 0);
            return read;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) => ReadAsync(buffer.AsMemory(offset, count), token).AsTask();
        private void Inspect(ReadOnlySpan<byte> bytes, bool eof)
        {
            foreach (byte value in bytes)
            {
                if (value == (byte)'\n') FlushLine();
                else if (!_Overlong)
                {
                    if (_Line.Length >= 1024 * 1024) { _Overlong = true; _Line.SetLength(0); }
                    else _Line.WriteByte(value);
                }
            }
            if (eof && !_Ended) { _Ended = true; FlushLine(); }
        }
        private void FlushLine()
        {
            if (!_Overlong && _Line.Length > 0) Observe(Encoding.UTF8.GetString(_Line.GetBuffer(), 0, (int)_Line.Length), renderer);
            _Line.SetLength(0);
            _Overlong = false;
        }
        protected override void Dispose(bool disposing) { if (disposing) { owner.Dispose(); _Line.Dispose(); } base.Dispose(disposing); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
