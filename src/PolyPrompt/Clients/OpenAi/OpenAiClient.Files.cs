namespace PolyPrompt.Clients
{
    using PolyPrompt.Helpers;
    using PolyPrompt.Models;
    using PolyPrompt.Telemetry;

    public partial class OpenAiClient
    {
        #region Files-Methods

        /// <summary>
        /// Upload a file to <c>/v1/files</c>, for example an image to reuse across requests by ID. Send it with
        /// <c>ContentPart.FileReferenceOf(ContentPartType.Image, file.Id, mediaType)</c>.
        /// </summary>
        /// <param name="data">The file bytes.</param>
        /// <param name="fileName">The file name; its extension sets the uploaded media type.</param>
        /// <param name="purpose">The purpose: <c>vision</c> (default) or <c>user_data</c>.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The uploaded file, or an unsuccessful response with the error.</returns>
        /// <exception cref="ArgumentException">Thrown when data is empty or fileName or purpose is blank.</exception>
        public Task<OpenAiFileResponse> UploadFileAsync(byte[] data, string fileName, string purpose = "vision", CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(data);
            if (data.Length == 0) throw new ArgumentException("File data is empty.", nameof(data));
            ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
            ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

            string mediaType = MediaTypes.FromFileExtension(fileName) ?? MediaTypeSniffer.Detect(data) ?? "application/octet-stream";
            List<MultipartField> fields = new List<MultipartField>
            {
                MultipartField.Text("purpose", purpose),
                MultipartField.File("file", data, Path.GetFileName(fileName), mediaType),
            };
            string url = Url("files");
            return InstrumentAsync(PolyPromptTelemetryNames.OperationFileUpload, null, null,
                () => ExecuteMultipartAsync(new OpenAiFileResponse(), "file upload", url, fields, ParseFile, token), token);
        }

        /// <summary>
        /// Delete a file from <c>/v1/files</c>.
        /// </summary>
        /// <param name="fileId">The file ID.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result; <see cref="FileDeleteResponse.Deleted"/> reports the provider's confirmation.</returns>
        /// <exception cref="ArgumentException">Thrown when fileId is blank.</exception>
        public Task<FileDeleteResponse> DeleteFileAsync(string fileId, CancellationToken token = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
            string url = Url("files/" + Uri.EscapeDataString(fileId));
            return InstrumentAsync(PolyPromptTelemetryNames.OperationFileDelete, null, null, () => ExecuteSendAsync(
                new FileDeleteResponse { Id = fileId }, "file delete", "DELETE", url,
                t => DeleteAndRecordAsync(url, null, t),
                (body, r) =>
                {
                    Dictionary<string, object>? root = TryDeserializeObject(body);
                    r.Deleted = root != null && root.ContainsKey("deleted") && string.Equals(root["deleted"]?.ToString(), "true", StringComparison.OrdinalIgnoreCase);
                }, token), token);
        }

        #endregion

        #region Private-Methods

        private void ParseFile(string body, OpenAiFileResponse response)
        {
            Dictionary<string, object>? root = TryDeserializeObject(body);
            if (root == null || !root.ContainsKey("id"))
            {
                response.Error = "Response is not an OpenAI file";
                return;
            }

            OpenAiFile file = new OpenAiFile
            {
                Id = root["id"]?.ToString() ?? string.Empty,
                FileName = root.ContainsKey("filename") ? root["filename"]?.ToString() : null,
                Purpose = root.ContainsKey("purpose") ? root["purpose"]?.ToString() : null,
            };
            if (root.ContainsKey("bytes") && long.TryParse(root["bytes"]?.ToString(), out long bytes)) file.SizeBytes = bytes;
            if (root.ContainsKey("created_at") && long.TryParse(root["created_at"]?.ToString(), out long created)) file.CreatedUtc = DateTimeOffset.FromUnixTimeSeconds(created).UtcDateTime;
            response.File = file;
        }

        #endregion
    }
}
