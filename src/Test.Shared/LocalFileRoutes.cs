namespace Test.Shared
{
    using System.Collections.Concurrent;
    using System.Net;
    using System.Text;

    /// <summary>
    /// Fixture routes for the Gemini File API (resumable upload, get with PROCESSING then ACTIVE, delete, paged list) and
    /// OpenAI <c>/v1/files</c> (multipart upload, delete).
    /// </summary>
    internal static class LocalFileRoutes
    {
        private sealed class Upload
        {
            public string? DisplayName;
            public string MediaType = "";
            public long Length;
        }

        private static readonly ConcurrentDictionary<string, Upload> _Uploads = new ConcurrentDictionary<string, Upload>();
        private static readonly ConcurrentDictionary<string, int> _Polls = new ConcurrentDictionary<string, int>();
        private static readonly ConcurrentDictionary<string, string> _MediaTypes = new ConcurrentDictionary<string, string>();

        public static async Task<bool> TryHandleAsync(HttpListenerContext context, string path, byte[] body)
        {
            string method = context.Request.HttpMethod;
            string query = context.Request.Url?.Query ?? "";
            string root = "http://" + (context.Request.Url?.Authority ?? "127.0.0.1");

            if (path == "/upload/v1beta/files" && method == "POST")
            {
                string command = context.Request.Headers["X-Goog-Upload-Command"] ?? "";
                if (command == "start")
                {
                    if (context.Request.Headers["X-Goog-Upload-Protocol"] != "resumable")
                    {
                        await WriteAsync(context, 400, "{\"error\":{\"message\":\"resumable protocol required\"}}").ConfigureAwait(false);
                        return true;
                    }
                    string id = Guid.NewGuid().ToString("N");
                    string text = Encoding.UTF8.GetString(body);
                    int at = text.IndexOf("\"display_name\":\"", StringComparison.Ordinal);
                    _Uploads[id] = new Upload
                    {
                        DisplayName = at < 0 ? null : text.Substring(at + 16, text.IndexOf('"', at + 16) - at - 16),
                        MediaType = context.Request.Headers["X-Goog-Upload-Header-Content-Type"] ?? "",
                        Length = long.TryParse(context.Request.Headers["X-Goog-Upload-Header-Content-Length"], out long length) ? length : -1,
                    };
                    context.Response.AddHeader("X-Goog-Upload-URL", root + "/upload/v1beta/files?upload_id=" + id);
                    context.Response.AddHeader("X-Goog-Upload-Status", "active");
                    await WriteAsync(context, 200, "{}").ConfigureAwait(false);
                    return true;
                }

                string uploadId = query.Contains("upload_id=", StringComparison.Ordinal) ? query.Substring(query.IndexOf("upload_id=", StringComparison.Ordinal) + 10) : "";
                if (command != "upload, finalize" || context.Request.Headers["X-Goog-Upload-Offset"] != "0" || !_Uploads.TryRemove(uploadId, out Upload? upload))
                {
                    await WriteAsync(context, 400, "{\"error\":{\"message\":\"bad upload step\"}}").ConfigureAwait(false);
                    return true;
                }
                if (upload.Length != body.Length)
                {
                    await WriteAsync(context, 400, "{\"error\":{\"message\":\"length mismatch\"}}").ConfigureAwait(false);
                    return true;
                }

                string name = "files/" + uploadId.Substring(0, 12);
                _MediaTypes[name] = upload.MediaType;
                await WriteAsync(context, 200, "{\"file\":" + GeminiFile(root, name, upload.DisplayName, upload.MediaType, body.Length, "PROCESSING") + "}").ConfigureAwait(false);
                return true;
            }

            if (path.StartsWith("/v1beta/files", StringComparison.Ordinal) && !path.StartsWith("/v1beta/files:", StringComparison.Ordinal))
            {
                if (path == "/v1beta/files" && method == "GET")
                {
                    bool second = query.Contains("pageToken=p2", StringComparison.Ordinal);
                    string page = second
                        ? "{\"files\":[" + GeminiFile(root, "files/c", null, "image/png", 3, "ACTIVE") + "]}"
                        : "{\"files\":[" + GeminiFile(root, "files/a", "first", "video/mp4", 1, "ACTIVE") + "," + GeminiFile(root, "files/b", null, "audio/wav", 2, "PROCESSING") + "],\"nextPageToken\":\"p2\"}";
                    await WriteAsync(context, 200, page).ConfigureAwait(false);
                    return true;
                }

                string fileName = path.Substring("/v1beta/".Length);
                if (fileName == "files/missing")
                {
                    await WriteAsync(context, 404, "{\"error\":{\"code\":404,\"message\":\"File files/missing not found.\",\"status\":\"NOT_FOUND\"}}").ConfigureAwait(false);
                    return true;
                }
                if (method == "DELETE")
                {
                    await WriteAsync(context, 200, "{}").ConfigureAwait(false);
                    return true;
                }
                if (method == "GET")
                {
                    string state = fileName switch
                    {
                        "files/failing" => "FAILED",
                        "files/never" => "PROCESSING",
                        _ => _Polls.AddOrUpdate(fileName, 1, (_, n) => n + 1) >= 2 ? "ACTIVE" : "PROCESSING",
                    };
                    string media = _MediaTypes.TryGetValue(fileName, out string? m) ? m : "video/mp4";
                    string file = GeminiFile(root, fileName, null, media, 10, state);
                    if (state == "FAILED") file = file.TrimEnd('}') + ",\"error\":{\"message\":\"unsupported codec\"}}";
                    await WriteAsync(context, 200, file).ConfigureAwait(false);
                    return true;
                }
            }

            if (path == "/v1/files" && method == "POST")
            {
                List<LocalTranscriptionRoutes.Field> fields = LocalTranscriptionRoutes.Parse(context.Request.ContentType ?? "", body);
                LocalTranscriptionRoutes.Field? file = fields.FirstOrDefault(f => f.Name == "file" && f.FileName != null);
                string? purpose = fields.FirstOrDefault(f => f.Name == "purpose")?.Text;
                if (file == null || string.IsNullOrEmpty(purpose))
                {
                    await WriteAsync(context, 400, "{\"error\":{\"message\":\"file and purpose are required\"}}").ConfigureAwait(false);
                    return true;
                }
                await WriteAsync(context, 200, "{\"id\":\"file-local1\",\"object\":\"file\",\"bytes\":" + file.Data.Length + ",\"created_at\":1763251200,\"filename\":\"" + file.FileName + "\",\"purpose\":\"" + purpose + "\"}").ConfigureAwait(false);
                return true;
            }

            if (path.StartsWith("/v1/files/", StringComparison.Ordinal) && method == "DELETE")
            {
                string id = Uri.UnescapeDataString(path.Substring("/v1/files/".Length));
                if (id == "file-missing")
                {
                    await WriteAsync(context, 404, "{\"error\":{\"message\":\"No such File object: file-missing\"}}").ConfigureAwait(false);
                    return true;
                }
                await WriteAsync(context, 200, "{\"id\":\"" + id + "\",\"object\":\"file\",\"deleted\":true}").ConfigureAwait(false);
                return true;
            }

            return false;
        }

        private static string GeminiFile(string root, string name, string? displayName, string mediaType, long size, string state)
        {
            return "{\"name\":\"" + name + "\"" + (displayName == null ? "" : ",\"displayName\":\"" + displayName + "\"")
                + ",\"mimeType\":\"" + mediaType + "\",\"sizeBytes\":\"" + size + "\",\"createTime\":\"2026-11-16T10:00:00.000000Z\""
                + ",\"expirationTime\":\"2026-11-18T10:00:00.000000Z\",\"sha256Hash\":\"aGFzaA==\",\"uri\":\"" + root + "/v1beta/" + name + "\",\"state\":\"" + state + "\"}";
        }

        private static async Task WriteAsync(HttpListenerContext context, int status, string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            context.Response.Close();
        }
    }
}
