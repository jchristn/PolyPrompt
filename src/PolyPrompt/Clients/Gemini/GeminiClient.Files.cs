namespace PolyPrompt.Clients
{
    using System.Globalization;
    using PolyPrompt.Models;
    using PolyPrompt.Telemetry;

    public partial class GeminiClient
    {
        #region Files-Methods

        /// <summary>
        /// Upload media to the Gemini File API with the resumable upload protocol, for media too large to send inline (most
        /// video). Video is processed after upload: call <see cref="WaitForFileActiveAsync"/> before using it, then send
        /// <c>ContentPart.FileReferenceOf(type, file.Uri, file.MediaType)</c>. Files expire after about 48 hours.
        /// </summary>
        /// <param name="data">The file bytes.</param>
        /// <param name="mediaType">The media type, for example <c>video/mp4</c>.</param>
        /// <param name="displayName">Optional display name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The uploaded file, or an unsuccessful response with the error.</returns>
        /// <exception cref="ArgumentException">Thrown when data is empty or the media type is blank.</exception>
        public Task<GeminiFileResponse> UploadFileAsync(byte[] data, string mediaType, string? displayName = null, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(data);
            if (data.Length == 0) throw new ArgumentException("File data is empty.", nameof(data));
            ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);

            string startUrl = _Endpoint.TrimEnd('/') + "/upload/v1beta/files";
            return InstrumentAsync(PolyPromptTelemetryNames.OperationFileUpload, null, null, () => ExecuteSendAsync(
                new GeminiFileResponse(), "file upload", "POST", startUrl,
                t => UploadAsync(startUrl, data, mediaType, displayName, t),
                (body, r) => r.File = ParseFileEnvelope(body, r), token), token);
        }

        /// <summary>
        /// Read a file's metadata and processing state.
        /// </summary>
        /// <param name="name">The resource name (<c>files/abc123</c>) or ID (<c>abc123</c>).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The file, or an unsuccessful response with the error.</returns>
        /// <exception cref="ArgumentException">Thrown when name is blank.</exception>
        public Task<GeminiFileResponse> GetFileAsync(string name, CancellationToken token = default)
        {
            string url = FileUrl(name);
            return InstrumentAsync(PolyPromptTelemetryNames.OperationFileGet, null, null, () => GetFileCoreAsync(url, token), token);
        }

        /// <summary>
        /// Poll a file until it is <see cref="GeminiFileState.Active"/>, it fails, or the timeout passes.
        /// </summary>
        /// <param name="name">The resource name or ID.</param>
        /// <param name="timeout">How long to wait in total.</param>
        /// <param name="pollInterval">Delay between polls. Default: 2 seconds.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The active file; or an unsuccessful response when processing failed, the timeout passed, or a poll failed.</returns>
        /// <exception cref="ArgumentException">Thrown when name is blank.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when timeout or pollInterval is not positive.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the token is canceled.</exception>
        public Task<GeminiFileResponse> WaitForFileActiveAsync(string name, TimeSpan timeout, TimeSpan? pollInterval = null, CancellationToken token = default)
        {
            string url = FileUrl(name);
            if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout), "timeout must be positive.");
            TimeSpan interval = pollInterval ?? TimeSpan.FromSeconds(2);
            if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(pollInterval), "pollInterval must be positive.");
            return InstrumentAsync(PolyPromptTelemetryNames.OperationFileGet, null, null, () => WaitCoreAsync(url, timeout, interval, token), token);
        }

        /// <summary>
        /// Delete a file.
        /// </summary>
        /// <param name="name">The resource name or ID.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result; <see cref="FileDeleteResponse.Deleted"/> is true on success.</returns>
        /// <exception cref="ArgumentException">Thrown when name is blank.</exception>
        public Task<FileDeleteResponse> DeleteFileAsync(string name, CancellationToken token = default)
        {
            string url = FileUrl(name);
            string resource = ResourceName(name);
            return InstrumentAsync(PolyPromptTelemetryNames.OperationFileDelete, null, null, () => ExecuteSendAsync(
                new FileDeleteResponse { Id = resource }, "file delete", "DELETE", url,
                t => DeleteAndRecordAsync(url, null, t),
                (body, r) => r.Deleted = true, token), token);
        }

        /// <summary>
        /// List one page of files.
        /// </summary>
        /// <param name="pageSize">Files per page (the API allows up to 100). Null uses the API default.</param>
        /// <param name="pageToken">The <see cref="GeminiFileListResponse.NextPageToken"/> of the previous page, or null for the first.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        public Task<GeminiFileListResponse> ListFilesAsync(int? pageSize = null, string? pageToken = null, CancellationToken token = default)
        {
            if (pageSize.HasValue && pageSize.Value <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize), "pageSize must be positive.");
            List<string> query = new List<string>();
            if (pageSize.HasValue) query.Add("pageSize=" + pageSize.Value.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(pageToken)) query.Add("pageToken=" + Uri.EscapeDataString(pageToken));
            string url = _Endpoint.TrimEnd('/') + "/v1beta/files" + (query.Count > 0 ? "?" + string.Join("&", query) : "");
            return InstrumentAsync(PolyPromptTelemetryNames.OperationFileList, null, null, () => ExecuteSendAsync(
                new GeminiFileListResponse(), "file list", "GET", url,
                t => GetAndRecordAsync(url, t),
                ParseFileList, token), token);
        }

        #endregion

        #region Private-Methods

        private async Task<HttpCallResult> UploadAsync(string startUrl, byte[] data, string mediaType, string? displayName, CancellationToken token)
        {
            Dictionary<string, object> metadata = new Dictionary<string, object>();
            if (!string.IsNullOrWhiteSpace(displayName)) metadata["display_name"] = displayName;
            byte[] startBody = _Serializer.SerializeJsonUtf8(new Dictionary<string, object> { { "file", metadata } });
            Dictionary<string, string> startHeaders = new Dictionary<string, string>
            {
                { "X-Goog-Upload-Protocol", "resumable" },
                { "X-Goog-Upload-Command", "start" },
                { "X-Goog-Upload-Header-Content-Length", data.Length.ToString(CultureInfo.InvariantCulture) },
                { "X-Goog-Upload-Header-Content-Type", mediaType },
            };

            HttpCallResult start = await PostAndRecordAsync(startUrl, startBody, JsonContentType, null, startHeaders, token).ConfigureAwait(false);
            if (!start.IsSuccessStatusCode) return start;

            string? uploadUrl = start.ResponseHeaders.FirstOrDefault(h => string.Equals(h.Key, "X-Goog-Upload-URL", StringComparison.OrdinalIgnoreCase)).Value;
            if (string.IsNullOrWhiteSpace(uploadUrl))
                return new HttpCallResult { StatusCode = 502, IsSuccessStatusCode = false, ResponseBody = "{\"error\":{\"message\":\"The upload start response had no X-Goog-Upload-URL header.\"}}" };

            Dictionary<string, string> uploadHeaders = new Dictionary<string, string>
            {
                { "X-Goog-Upload-Offset", "0" },
                { "X-Goog-Upload-Command", "upload, finalize" },
            };
            string recorded = "<binary " + data.Length + " bytes " + mediaType + ">";
            return await PostAndRecordAsync(uploadUrl, data, mediaType, recorded, uploadHeaders, token).ConfigureAwait(false);
        }

        private async Task<GeminiFileResponse> GetFileCoreAsync(string url, CancellationToken token)
        {
            return await ExecuteSendAsync(new GeminiFileResponse(), "file get", "GET", url, t => GetAndRecordAsync(url, t),
                (body, r) => r.File = ParseFile(TryDeserializeObject(body), r), token).ConfigureAwait(false);
        }

        private async Task<GeminiFileResponse> WaitCoreAsync(string url, TimeSpan timeout, TimeSpan interval, CancellationToken token)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                GeminiFileResponse response = await GetFileCoreAsync(url, token).ConfigureAwait(false);
                if (!response.Success || response.File == null) return response;
                if (response.File.State == GeminiFileState.Active) return response;
                if (response.File.State == GeminiFileState.Failed)
                {
                    response.Success = false;
                    response.Error = "Gemini could not process " + response.File.Name + ": " + (response.File.StateError ?? "no reason given") + ".";
                    return response;
                }

                TimeSpan remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    response.Success = false;
                    response.Error = response.File.Name + " is still " + response.File.State + " after " + timeout.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture) + " seconds.";
                    return response;
                }

                await Task.Delay(interval < remaining ? interval : remaining, token).ConfigureAwait(false);
            }
        }

        private GeminiFile? ParseFileEnvelope(string body, GeminiFileResponse response)
        {
            Dictionary<string, object>? root = TryDeserializeObject(body);
            return ParseFile(root == null ? null : ParseNestedObject(root, "file") ?? root, response);
        }

        private GeminiFile? ParseFile(Dictionary<string, object>? obj, ResponseBase response)
        {
            if (obj == null || !obj.ContainsKey("name"))
            {
                response.Error = "Response is not a Gemini file";
                return null;
            }

            string? S(string key) => obj.ContainsKey(key) ? obj[key]?.ToString() : null;
            GeminiFile file = new GeminiFile
            {
                Name = S("name") ?? string.Empty,
                DisplayName = S("displayName"),
                Uri = S("uri"),
                MediaType = S("mimeType"),
                Sha256Hash = S("sha256Hash"),
                State = S("state") switch
                {
                    "PROCESSING" => GeminiFileState.Processing,
                    "ACTIVE" => GeminiFileState.Active,
                    "FAILED" => GeminiFileState.Failed,
                    _ => GeminiFileState.Unspecified
                },
            };
            if (long.TryParse(S("sizeBytes"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long size)) file.SizeBytes = size;
            if (DateTime.TryParse(S("createTime"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime created)) file.CreatedUtc = created;
            if (DateTime.TryParse(S("expirationTime"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime expires)) file.ExpirationUtc = expires;
            Dictionary<string, object>? error = ParseNestedObject(obj, "error");
            if (error != null && error.ContainsKey("message")) file.StateError = error["message"]?.ToString();
            return file;
        }

        private void ParseFileList(string body, GeminiFileListResponse response)
        {
            Dictionary<string, object>? root = TryDeserializeObject(body);
            if (root == null)
            {
                response.Error = "Response is not a JSON object";
                return;
            }

            foreach (Dictionary<string, object> item in ParseNestedList(root, "files") ?? new List<Dictionary<string, object>>())
            {
                GeminiFile? file = ParseFile(item, response);
                if (file != null) response.Files.Add(file);
            }
            response.NextPageToken = root.ContainsKey("nextPageToken") ? root["nextPageToken"]?.ToString() : null;
            if (string.IsNullOrEmpty(response.NextPageToken)) response.NextPageToken = null;
        }

        private string FileUrl(string name)
        {
            return _Endpoint.TrimEnd('/') + "/v1beta/" + ResourceName(name);
        }

        private static string ResourceName(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            string trimmed = name.Trim();
            return trimmed.StartsWith("files/", StringComparison.Ordinal) ? trimmed : "files/" + trimmed;
        }

        #endregion
    }
}
