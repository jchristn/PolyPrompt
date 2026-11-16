namespace Test.Shared
{
    using System.Net;
    using System.Text;

    /// <summary>
    /// The fixture transcription route (<c>*/audio/transcriptions</c>) and a minimal multipart/form-data parser that the
    /// transcription cases reuse to inspect what was sent.
    /// </summary>
    internal static class LocalTranscriptionRoutes
    {
        internal sealed class Field
        {
            public string Name = string.Empty;
            public string? FileName;
            public string? ContentType;
            public byte[] Data = Array.Empty<byte>();
            public string Text => Encoding.UTF8.GetString(Data);
        }

        public static async Task<bool> TryHandleAsync(HttpListenerContext context, string path, byte[] body)
        {
            if (!path.EndsWith("/audio/transcriptions", StringComparison.OrdinalIgnoreCase)) return false;

            List<Field> fields = Parse(context.Request.ContentType ?? string.Empty, body);
            Field? file = fields.FirstOrDefault(f => f.Name == "file" && f.FileName != null);
            string? model = fields.FirstOrDefault(f => f.Name == "model")?.Text;
            if (file == null || file.Data.Length == 0 || string.IsNullOrEmpty(model))
            {
                await WriteAsync(context, 400, "application/json", "{\"error\":{\"message\":\"file and model are required\"}}").ConfigureAwait(false);
                return true;
            }
            if (model == "error-model")
            {
                await WriteAsync(context, 500, "application/json", "{\"error\":{\"message\":\"transcription backend failed\"}}").ConfigureAwait(false);
                return true;
            }

            string format = fields.FirstOrDefault(f => f.Name == "response_format")?.Text ?? "json";
            bool words = fields.Any(f => f.Name == "timestamp_granularities[]" && f.Text == "word");
            switch (format)
            {
                case "text":
                    await WriteAsync(context, 200, "text/plain", "hello world\n").ConfigureAwait(false);
                    break;
                case "verbose_json":
                    string language = fields.FirstOrDefault(f => f.Name == "language")?.Text ?? "english";
                    string wordJson = words ? ",\"words\":[{\"word\":\"hello\",\"start\":0.0,\"end\":0.6},{\"word\":\"world\",\"start\":0.7,\"end\":1.4}]" : "";
                    await WriteAsync(context, 200, "application/json",
                        "{\"task\":\"transcribe\",\"language\":\"" + language + "\",\"duration\":2.5,\"text\":\"hello world\",\"segments\":["
                        + "{\"id\":0,\"start\":0.0,\"end\":0.6,\"text\":\" hello\",\"avg_logprob\":-0.21,\"no_speech_prob\":0.01},"
                        + "{\"id\":1,\"start\":0.7,\"end\":1.4,\"text\":\" world\",\"avg_logprob\":-0.3,\"no_speech_prob\":0.02}]" + wordJson + "}").ConfigureAwait(false);
                    break;
                default:
                    await WriteAsync(context, 200, "application/json", "{\"text\":\"hello world\",\"usage\":{\"type\":\"tokens\",\"input_tokens\":12,\"output_tokens\":3,\"total_tokens\":15}}").ConfigureAwait(false);
                    break;
            }
            return true;
        }

        /// <summary>
        /// Parse a multipart/form-data body into its fields, in order.
        /// </summary>
        public static List<Field> Parse(string contentType, byte[] body)
        {
            List<Field> fields = new List<Field>();
            int at = contentType.IndexOf("boundary=", StringComparison.OrdinalIgnoreCase);
            if (at < 0) return fields;
            string boundary = contentType.Substring(at + 9).Trim().Trim('"');
            byte[] delimiter = Encoding.ASCII.GetBytes("--" + boundary);

            int position = IndexOf(body, delimiter, 0);
            while (position >= 0)
            {
                int start = position + delimiter.Length;
                if (start + 2 <= body.Length && body[start] == '-' && body[start + 1] == '-') break;
                start += 2; // CRLF after the delimiter
                int headerEnd = IndexOf(body, Encoding.ASCII.GetBytes("\r\n\r\n"), start);
                if (headerEnd < 0) break;
                string headers = Encoding.UTF8.GetString(body, start, headerEnd - start);
                int dataStart = headerEnd + 4;
                int next = IndexOf(body, delimiter, dataStart);
                if (next < 0) break;
                int dataEnd = next - 2; // CRLF before the next delimiter

                Field field = new Field { Data = body[dataStart..dataEnd] };
                foreach (string line in headers.Split("\r\n"))
                {
                    if (line.StartsWith("Content-Disposition:", StringComparison.OrdinalIgnoreCase))
                    {
                        field.Name = Parameter(line, "name") ?? string.Empty;
                        field.FileName = Parameter(line, "filename");
                    }
                    else if (line.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase))
                    {
                        field.ContentType = line.Substring(13).Trim();
                    }
                }
                fields.Add(field);
                position = next;
            }
            return fields;
        }

        private static string? Parameter(string header, string name)
        {
            foreach (string piece in header.Split(';'))
            {
                string p = piece.Trim();
                if (p.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase)) return p.Substring(name.Length + 1).Trim('"');
            }
            return null;
        }

        private static int IndexOf(byte[] haystack, byte[] needle, int from)
        {
            for (int i = from; i <= haystack.Length - needle.Length; i++)
            {
                int j = 0;
                while (j < needle.Length && haystack[i + j] == needle[j]) j++;
                if (j == needle.Length) return i;
            }
            return -1;
        }

        private static async Task WriteAsync(HttpListenerContext context, int status, string contentType, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            context.Response.StatusCode = status;
            context.Response.ContentType = contentType;
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            context.Response.Close();
        }
    }
}
