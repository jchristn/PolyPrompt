namespace Test.Shared
{
    using System.Text.Json;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using Touchstone.Core;
    using N = PolyPrompt.Telemetry.PolyPromptTelemetryNames;

    /// <summary>
    /// Provider file uploads: the Gemini resumable upload sequence, waiting for ACTIVE with failure, timeout, and
    /// cancellation, get, delete, and paged list; OpenAI upload and delete; and uploaded files used as content parts.
    /// </summary>
    internal static class LocalFileUploadCases
    {
        public static IEnumerable<TestCaseDescriptor> Create(string suite)
        {
            (string, string, Func<CancellationToken, Task>)[] cases =
            {
                ("files_gemini_upload", "Gemini uploads with the two-step resumable protocol and returns the file", GeminiUploadAsync),
                ("files_gemini_wait", "WaitForFileActiveAsync polls to ACTIVE and reports FAILED, timeout, not found, and cancellation", GeminiWaitAsync),
                ("files_gemini_delete_list", "Gemini deletes files and lists them page by page", GeminiDeleteListAsync),
                ("files_openai", "OpenAI uploads with multipart, deletes, and sends uploaded images by file_id", OpenAiAsync),
                ("files_validation_and_telemetry", "File methods validate arguments before sending and report the file capability", ValidationAsync),
            };
            foreach (var (id, name, run) in cases) yield return new TestCaseDescriptor(suite, id, name, run, new[] { "local", "files" });
        }

        private static readonly byte[] Video = LocalChatModelCases.Mp4.Concat(Enumerable.Range(0, 500).Select(i => (byte)i)).ToArray();

        private static async Task GeminiUploadAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using GeminiClient gemini = WireCapture.Full(new GeminiClient(LocalClients.TestKey, server.Endpoint));
            GeminiFileResponse response = await gemini.UploadFileAsync(Video, MediaTypes.VideoMp4, "keyframe clip", token).ConfigureAwait(false);
            SharedAssert.True(response.Success, response.Error ?? "upload");
            GeminiFile file = response.File!;
            SharedAssert.True(file.Name.StartsWith("files/", StringComparison.Ordinal), "Resource name: " + file.Name);
            SharedAssert.Equal("keyframe clip", file.DisplayName, "Display name.");
            SharedAssert.Equal(MediaTypes.VideoMp4, file.MediaType, "Media type.");
            SharedAssert.Equal((long)Video.Length, file.SizeBytes, "Size.");
            SharedAssert.Equal(GeminiFileState.Processing, file.State, "Video starts processing.");
            SharedAssert.Equal(new DateTime(2026, 11, 18, 10, 0, 0, DateTimeKind.Utc), file.ExpirationUtc, "Expiration, UTC.");
            SharedAssert.True(file.Uri!.EndsWith("/v1beta/" + file.Name, StringComparison.Ordinal), "URI: " + file.Uri);

            List<string> paths = server.RequestPaths;
            SharedAssert.Equal("/upload/v1beta/files,/upload/v1beta/files", string.Join(",", paths), "Two upload requests.");
            Dictionary<string, string> start = server.RequestHeaders[0];
            SharedAssert.Equal("resumable", start["X-Goog-Upload-Protocol"], "Resumable protocol.");
            SharedAssert.Equal("start", start["X-Goog-Upload-Command"], "Start command.");
            SharedAssert.Equal(Video.Length.ToString(), start["X-Goog-Upload-Header-Content-Length"], "Declared length.");
            SharedAssert.Equal(MediaTypes.VideoMp4, start["X-Goog-Upload-Header-Content-Type"], "Declared type.");
            SharedAssert.Equal(LocalClients.TestKey, start["x-goog-api-key"], "API key on the start request.");
            using (JsonDocument metadata = JsonDocument.Parse(server.RequestBodies[0]))
                SharedAssert.Equal("keyframe clip", metadata.RootElement.GetProperty("file").GetProperty("display_name").GetString(), "Metadata body.");
            Dictionary<string, string> upload = server.RequestHeaders[1];
            SharedAssert.Equal("upload, finalize", upload["X-Goog-Upload-Command"], "Upload and finalize.");
            SharedAssert.Equal("0", upload["X-Goog-Upload-Offset"], "Offset 0.");
            SharedAssert.True(server.RequestUrls[1].Contains("upload_id=", StringComparison.Ordinal), "Upload goes to the returned URL.");
            SharedAssert.True(Video.SequenceEqual(server.RequestRawBodies[1]), "The bytes arrive intact.");
            SharedAssert.Equal("<binary " + Video.Length + " bytes video/mp4>", gemini.CallDetails.Last().RequestBody, "The upload is recorded as a summary.");

            GeminiFileResponse unnamed = await gemini.UploadFileAsync(LocalChatModelCases.Wav, MediaTypes.AudioWav, null, token).ConfigureAwait(false);
            SharedAssert.True(unnamed.Success, unnamed.Error ?? "unnamed");
            SharedAssert.Equal(null, unnamed.File!.DisplayName, "No display name.");
        }

        private static async Task GeminiWaitAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using GeminiClient gemini = new GeminiClient(LocalClients.TestKey, server.Endpoint);
            GeminiFile uploaded = (await gemini.UploadFileAsync(Video, MediaTypes.VideoMp4, null, token).ConfigureAwait(false)).File!;

            int before = server.RequestPaths.Count;
            GeminiFileResponse active = await gemini.WaitForFileActiveAsync(uploaded.Name, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(10), token).ConfigureAwait(false);
            SharedAssert.True(active.Success, active.Error ?? "wait");
            SharedAssert.Equal(GeminiFileState.Active, active.File!.State, "Active.");
            SharedAssert.Equal(2, server.RequestPaths.Count - before, "Polled until active (PROCESSING, then ACTIVE).");
            SharedAssert.Equal("/v1beta/" + uploaded.Name, server.RequestPaths.Last(), "Polls the file resource.");

            ChatResponse chat = await gemini.ChatAsync(ChatMessage.User("Summarize", ContentPart.FileReferenceOf(ContentPartType.Video, active.File.Uri!, active.File.MediaType!)), null, token).ConfigureAwait(false);
            SharedAssert.True(chat.Success, chat.Error ?? "chat with file");
            using (JsonDocument body = JsonDocument.Parse(server.RequestBodies.Last()))
            {
                JsonElement fileData = body.RootElement.GetProperty("contents")[0].GetProperty("parts")[1].GetProperty("fileData");
                SharedAssert.Equal(active.File.Uri, fileData.GetProperty("fileUri").GetString(), "The file URI is the reference.");
                SharedAssert.Equal(MediaTypes.VideoMp4, fileData.GetProperty("mimeType").GetString(), "Its media type.");
            }

            GeminiFileResponse failed = await gemini.WaitForFileActiveAsync("files/failing", TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(10), token).ConfigureAwait(false);
            SharedAssert.False(failed.Success, "FAILED is unsuccessful.");
            SharedAssert.Equal(GeminiFileState.Failed, failed.File!.State, "Failed state.");
            SharedAssert.True(failed.Error!.Contains("unsupported codec", StringComparison.Ordinal), "The processing error: " + failed.Error);

            GeminiFileResponse timedOut = await gemini.WaitForFileActiveAsync("never", TimeSpan.FromMilliseconds(80), TimeSpan.FromMilliseconds(20), token).ConfigureAwait(false);
            SharedAssert.False(timedOut.Success, "Timeout is unsuccessful.");
            SharedAssert.True(timedOut.Error!.Contains("still Processing", StringComparison.Ordinal), "Timeout message: " + timedOut.Error);
            SharedAssert.Equal("/v1beta/files/never", server.RequestPaths.Last(), "A bare ID becomes files/ID.");

            GeminiFileResponse missing = await gemini.GetFileAsync("files/missing", token).ConfigureAwait(false);
            SharedAssert.False(missing.Success, "Not found.");
            SharedAssert.Equal(404, missing.StatusCode, "404.");
            GeminiFileResponse missingWait = await gemini.WaitForFileActiveAsync("files/missing", TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10), token).ConfigureAwait(false);
            SharedAssert.Equal(404, missingWait.StatusCode, "Wait stops at a failed poll.");

            using CancellationTokenSource cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            await SharedAssert.ThrowsAsync<OperationCanceledException>(() => gemini.WaitForFileActiveAsync("files/never", TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(10), cancel.Token), "Cancellation stops waiting.").ConfigureAwait(false);
        }

        private static async Task GeminiDeleteListAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using GeminiClient gemini = new GeminiClient(LocalClients.TestKey, server.Endpoint);

            FileDeleteResponse deleted = await gemini.DeleteFileAsync("abc", token).ConfigureAwait(false);
            SharedAssert.True(deleted.Success && deleted.Deleted, deleted.Error ?? "delete");
            SharedAssert.Equal("files/abc", deleted.Id, "Resource name.");
            SharedAssert.Equal("/v1beta/files/abc", server.RequestPaths.Last(), "Delete path.");
            FileDeleteResponse notFound = await gemini.DeleteFileAsync("files/missing", token).ConfigureAwait(false);
            SharedAssert.False(notFound.Success || notFound.Deleted, "Deleting a missing file fails.");

            GeminiFileListResponse first = await gemini.ListFilesAsync(2, null, token).ConfigureAwait(false);
            SharedAssert.True(first.Success, first.Error ?? "list");
            SharedAssert.Equal("files/a,files/b", string.Join(",", first.Files.Select(f => f.Name)), "First page.");
            SharedAssert.Equal(GeminiFileState.Processing, first.Files[1].State, "States are parsed.");
            SharedAssert.Equal("p2", first.NextPageToken, "Next page token.");
            SharedAssert.Equal("/v1beta/files?pageSize=2", server.RequestUrls.Last(), "Page size in the query.");
            GeminiFileListResponse second = await gemini.ListFilesAsync(2, first.NextPageToken, token).ConfigureAwait(false);
            SharedAssert.Equal("files/c", string.Join(",", second.Files.Select(f => f.Name)), "Second page.");
            SharedAssert.Equal(null, second.NextPageToken, "Last page.");
            SharedAssert.True(server.RequestUrls.Last().EndsWith("pageToken=p2", StringComparison.Ordinal), "Page token in the query.");
        }

        private static async Task OpenAiAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiClient openai = WireCapture.Full(new OpenAiClient(LocalOpenAiResponsesRoutes.Key, server.Endpoint));
            byte[] png = LocalMultimodalCases.Png;
            OpenAiFileResponse uploaded = await openai.UploadFileAsync(png, "photos/cat.png", "vision", token).ConfigureAwait(false);
            SharedAssert.True(uploaded.Success, uploaded.Error ?? "upload");
            SharedAssert.Equal("file-local1", uploaded.File!.Id, "File ID.");
            SharedAssert.Equal("cat.png", uploaded.File.FileName, "The file name without directories.");
            SharedAssert.Equal("vision", uploaded.File.Purpose, "Purpose.");
            SharedAssert.Equal((long)png.Length, uploaded.File.SizeBytes, "Size.");
            SharedAssert.Equal(new DateTime(2025, 11, 16, 0, 0, 0, DateTimeKind.Utc), uploaded.File.CreatedUtc, "Created.");
            SharedAssert.Equal("/v1/files", server.RequestPaths.Last(), "Upload path.");
            List<LocalTranscriptionRoutes.Field> fields = LocalTranscriptionRoutes.Parse(server.RequestContentTypes.Last(), server.RequestRawBodies.Last());
            SharedAssert.Equal("image/png", fields.Single(f => f.Name == "file").ContentType, "Media type from the extension.");
            SharedAssert.True(png.SequenceEqual(fields.Single(f => f.Name == "file").Data), "The bytes arrive intact.");
            SharedAssert.True((openai.CallDetails.Last().RequestBody ?? "").Contains("<binary " + png.Length + " bytes image/png name=cat.png>", StringComparison.Ordinal), "Recorded as a summary.");

            ChatResponse chat = await openai.ChatAsync(ChatMessage.User("Describe", ContentPart.FileReferenceOf(ContentPartType.Image, uploaded.File.Id, MediaTypes.ImagePng)), null, token).ConfigureAwait(false);
            SharedAssert.True(chat.Success, chat.Error ?? "chat with file");
            using (JsonDocument body = JsonDocument.Parse(server.RequestBodies.Last()))
                SharedAssert.Equal("file-local1", body.RootElement.GetProperty("input")[0].GetProperty("content")[1].GetProperty("file_id").GetString(), "Responses file_id.");

            FileDeleteResponse deleted = await openai.DeleteFileAsync("file-local1", token).ConfigureAwait(false);
            SharedAssert.True(deleted.Success && deleted.Deleted, deleted.Error ?? "delete");
            SharedAssert.Equal("/v1/files/file-local1", server.RequestPaths.Last(), "Delete path.");
            FileDeleteResponse missing = await openai.DeleteFileAsync("file-missing", token).ConfigureAwait(false);
            SharedAssert.False(missing.Success, "Deleting a missing file fails.");
            SharedAssert.Equal(404, missing.StatusCode, "404.");
        }

        private static async Task ValidationAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using GeminiClient gemini = new GeminiClient(LocalClients.TestKey, server.Endpoint);
            using OpenAiClient openai = new OpenAiClient(LocalOpenAiResponsesRoutes.Key, server.Endpoint);

            void Rejects<T>(Func<Task> call, string message) where T : Exception
            {
                try { call(); }
                catch (T) { return; }
                throw new TestFailureException(message + " (expected " + typeof(T).Name + ")");
            }

            Rejects<ArgumentException>(() => gemini.UploadFileAsync(Array.Empty<byte>(), MediaTypes.VideoMp4, null, token), "Empty upload.");
            Rejects<ArgumentException>(() => gemini.UploadFileAsync(Video, " ", null, token), "Blank media type.");
            Rejects<ArgumentException>(() => gemini.GetFileAsync(" ", token), "Blank name.");
            Rejects<ArgumentOutOfRangeException>(() => gemini.WaitForFileActiveAsync("a", TimeSpan.Zero, null, token), "Zero timeout.");
            Rejects<ArgumentOutOfRangeException>(() => gemini.WaitForFileActiveAsync("a", TimeSpan.FromSeconds(1), TimeSpan.Zero, token), "Zero interval.");
            Rejects<ArgumentOutOfRangeException>(() => gemini.ListFilesAsync(0, null, token), "Zero page size.");
            Rejects<ArgumentException>(() => openai.UploadFileAsync(Video, "", "vision", token), "Blank file name.");
            Rejects<ArgumentException>(() => openai.DeleteFileAsync("", token), "Blank file ID.");
            SharedAssert.Equal(0, server.RequestPaths.Count, "Nothing was sent.");

            using TelemetryCapture capture = new TelemetryCapture("tel_files");
            await gemini.UploadFileAsync(Video, MediaTypes.VideoMp4, null, token).ConfigureAwait(false);
            System.Diagnostics.Activity span = capture.Span("gcp.gemini file_upload");
            SharedAssert.Equal("file", span.GetTagItem(N.Capability)?.ToString(), "File capability label.");
            capture.Require(N.Operations, N.Operation, "file_upload", N.Capability, "file", N.Outcome, "success");
        }
    }
}
