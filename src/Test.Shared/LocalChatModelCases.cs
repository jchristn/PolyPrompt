namespace Test.Shared
{
    using System.Text.Json;
    using PolyPrompt.Clients;
    using PolyPrompt.Helpers;
    using PolyPrompt.Models;
    using Touchstone.Core;

    /// <summary>
    /// 5.0 chat surface and content model: the four ChatAsync overloads and their streaming forms on every chat client,
    /// ChatMessage content parts and their JSON (including 4.x string content), and ContentPart validation.
    /// </summary>
    internal static class LocalChatModelCases
    {
        public static IEnumerable<TestCaseDescriptor> Create(string suite)
        {
            (string, string, Func<CancellationToken, Task>)[] cases =
            {
                ("chat_overloads", "Every chat client answers ChatAsync and ChatStreamingAsync from a prompt, a message, a conversation, and a request", OverloadsAsync),
                ("chat_overloads_same_body", "A single message and a request holding it send the same body", SameBodyAsync),
                ("chat_message_content", "ChatMessage content is a list of parts with Text, HasMedia, and factories", ContentAsync),
                ("chat_message_json", "ChatMessage JSON reads 4.x string content, null, and arrays, and round-trips media", JsonAsync),
                ("content_part_validation", "ContentPart factories and Validate reject malformed parts with the part's path", ValidationAsync),
                ("content_part_from_file", "ContentPart.FromFile infers the part type and media type from bytes, then the extension", FromFileAsync),
                ("harness_history_text_only", "Agent harnesses reject history messages that carry media before starting a process", HarnessAsync),
            };
            foreach (var (id, name, run) in cases) yield return new TestCaseDescriptor(suite, id, name, run, new[] { "local", "chat" });
        }

        internal static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' };
        internal static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 0, 16, (byte)'J', (byte)'F', (byte)'I', (byte)'F' };
        internal static readonly byte[] Wav = { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 36, 0, 0, 0, (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' };
        internal static readonly byte[] Mp4 = { 0, 0, 0, 24, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m', 0, 0, 2, 0 };

        private static async Task Drain<T>(IAsyncEnumerable<T> chunks, CancellationToken token)
        {
            await foreach (T _ in chunks.WithCancellation(token).ConfigureAwait(false)) { }
        }

        private static async Task OverloadsAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            List<ProviderClientBase> clients = LocalClients.Completion(server.Endpoint);
            try
            {
                foreach (ProviderClientBase client in clients)
                {
                    string name = client.GetType().Name;
                    List<ChatMessage> conversation = new List<ChatMessage> { ChatMessage.User("Hi"), ChatMessage.Assistant("Hello."), ChatMessage.User("Again") };
                    ChatResponse[] responses =
                    {
                        await client.ChatAsync("Hi", null, token).ConfigureAwait(false),
                        await client.ChatAsync(ChatMessage.User("Hi"), null, token).ConfigureAwait(false),
                        await client.ChatAsync(conversation, null, token).ConfigureAwait(false),
                        await client.ChatAsync(new ChatRequest { Messages = conversation }, token).ConfigureAwait(false),
                    };
                    for (int i = 0; i < responses.Length; i++)
                    {
                        SharedAssert.True(responses[i].Success, name + " overload " + i + ": " + responses[i].Error);
                        SharedAssert.NotEmpty(responses[i].Text, name + " overload " + i + " text.");
                        SharedAssert.Equal(0, responses[i].ToolCalls.Count, name + " overload " + i + " requested no tools.");
                    }

                    ChatStreamingResponse[] streams =
                    {
                        await client.ChatStreamingAsync("Hi", null, token).ConfigureAwait(false),
                        await client.ChatStreamingAsync(ChatMessage.User("Hi"), null, token).ConfigureAwait(false),
                        await client.ChatStreamingAsync(conversation, null, token).ConfigureAwait(false),
                        await client.ChatStreamingAsync(new ChatRequest { Messages = conversation }, token).ConfigureAwait(false),
                    };
                    for (int i = 0; i < streams.Length; i++)
                    {
                        await Drain(streams[i].Chunks, token).ConfigureAwait(false);
                        SharedAssert.True(streams[i].Success, name + " streaming overload " + i + ": " + streams[i].Error);
                        SharedAssert.Equal(StreamingState.Completed, streams[i].State, name + " streaming overload " + i + " completes.");
                        SharedAssert.NotEmpty(streams[i].Text, name + " streaming overload " + i + " accumulates text.");
                    }

                    await SharedAssert.ThrowsAsync<ArgumentException>(() => client.ChatAsync(new List<ChatMessage>(), null, token), name + " rejects an empty conversation.").ConfigureAwait(false);
                    await SharedAssert.ThrowsAsync<ArgumentNullException>(() => client.ChatAsync((ChatMessage)null!, null, token), name + " rejects a null message.").ConfigureAwait(false);
                }
            }
            finally { LocalClients.DisposeAll(clients); }
        }

        private static async Task SameBodyAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            List<ProviderClientBase> clients = LocalClients.Completion(server.Endpoint);
            try
            {
                foreach (ProviderClientBase client in clients)
                {
                    WireCapture.Full(client);
                    CompletionOptions options = new CompletionOptions { MaxTokens = 32, SystemPrompt = "Be brief." };
                    await client.ChatAsync(ChatMessage.User("Same"), options, token).ConfigureAwait(false);
                    await client.ChatAsync(new ChatRequest { Messages = { ChatMessage.User("Same") }, Options = options }, token).ConfigureAwait(false);
                    await client.ChatAsync(new[] { ChatMessage.User("Same") }, options, token).ConfigureAwait(false);
                    List<CallDetail> details = client.CallDetails.ToList();
                    SharedAssert.Equal(3, details.Count, client.GetType().Name + " sent three requests.");
                    SharedAssert.Equal(details[0].RequestBody, details[1].RequestBody, client.GetType().Name + ": message and request bodies match.");
                    SharedAssert.Equal(details[0].RequestBody, details[2].RequestBody, client.GetType().Name + ": message and conversation bodies match.");
                }
            }
            finally { LocalClients.DisposeAll(clients); }
        }

        private static Task ContentAsync(CancellationToken token)
        {
            ChatMessage user = ChatMessage.User("Describe", ContentPart.Image(Png, MediaTypes.ImagePng), ContentPart.FromText("then summarize"));
            SharedAssert.Equal(3, user.Content.Count, "Text first, then parts in order.");
            SharedAssert.Equal(ContentPartType.Text, user.Content[0].Type, "Leading text part.");
            SharedAssert.Equal(ContentPartType.Image, user.Content[1].Type, "Image part.");
            SharedAssert.Equal("Describe\nthen summarize", user.Text, "Text joins text parts with a newline.");
            SharedAssert.True(user.HasMedia, "HasMedia with an image.");

            ChatMessage plain = ChatMessage.User("hello");
            SharedAssert.Equal("hello", plain.Text, "Single text part.");
            SharedAssert.False(plain.HasMedia, "No media.");

            plain.Text = "changed";
            SharedAssert.Equal(1, plain.Content.Count, "Setting Text leaves one part.");
            SharedAssert.Equal("changed", plain.Content[0].Text, "Setting Text replaces the content.");
            plain.Text = null;
            SharedAssert.Equal(0, plain.Content.Count, "Setting Text to null clears the content.");
            SharedAssert.Equal(null, plain.Text, "No text parts means null Text.");

            ChatMessage calls = ChatMessage.AssistantToolCalls(new[] { new ToolCall { Id = "c", Name = "f" } });
            SharedAssert.Equal(0, calls.Content.Count, "Tool-call messages have no content.");
            SharedAssert.Equal(null, calls.Text, "Tool-call messages have no text.");

            ChatMessage fromParts = ChatMessage.User(new[] { ContentPart.Audio(Wav, MediaTypes.AudioWav) });
            SharedAssert.Equal(null, fromParts.Text, "Media-only message has no text.");
            SharedAssert.True(fromParts.HasMedia, "Media-only message has media.");

            ChatMessage set = new ChatMessage { Content = null! };
            SharedAssert.NotNull(set.Content, "A null Content becomes an empty list.");

            ChatResponse response = new ChatResponse { Text = "answer", ToolCalls = { new ToolCall { Id = "1", Name = "f" } } };
            ChatMessage assistant = response.ToAssistantMessage();
            SharedAssert.Equal("assistant", assistant.Role, "Assistant role.");
            SharedAssert.Equal("answer", assistant.Text, "Assistant text.");
            SharedAssert.Equal(1, assistant.ToolCalls.Count, "Assistant tool calls.");
            return Task.CompletedTask;
        }

        private static Task JsonAsync(CancellationToken token)
        {
            ChatMessage legacy = JsonSerializer.Deserialize("{\"Role\":\"user\",\"Content\":\"from 4.x\"}", PolyPromptJsonContext.Default.ChatMessage)!;
            SharedAssert.Equal(1, legacy.Content.Count, "4.x string content becomes one part.");
            SharedAssert.Equal("from 4.x", legacy.Text, "4.x string content is the text.");

            ChatMessage empty = JsonSerializer.Deserialize("{\"Role\":\"assistant\",\"Content\":null}", PolyPromptJsonContext.Default.ChatMessage)!;
            SharedAssert.Equal(0, empty.Content.Count, "Null content is empty.");

            ChatMessage missing = JsonSerializer.Deserialize("{\"Role\":\"assistant\"}", PolyPromptJsonContext.Default.ChatMessage)!;
            SharedAssert.Equal(0, missing.Content.Count, "Missing content is empty.");

            List<ChatMessage> legacyList = JsonSerializer.Deserialize("[{\"Role\":\"system\",\"Content\":\"s\"},{\"Role\":\"user\",\"Content\":\"u\"}]", PolyPromptJsonContext.Default.ListChatMessage)!;
            SharedAssert.Equal("u", legacyList[1].Text, "4.x conversations load.");

            ChatMessage media = ChatMessage.User("look", ContentPart.Image(Png, MediaTypes.ImagePng, ImageDetail.High), ContentPart.VideoUrl("https://www.youtube.com/watch?v=x", new VideoClip { StartOffsetMs = 1000, FramesPerSecond = 2 }));
            string json = JsonSerializer.Serialize(media, PolyPromptJsonContext.Default.ChatMessage);
            SharedAssert.True(json.Contains("\"Content\":[", StringComparison.Ordinal), "Content is written as an array: " + json);
            SharedAssert.False(json.Contains("\"Text\":\"look\",\"HasMedia\"", StringComparison.Ordinal), "Computed members are not written.");
            SharedAssert.False(json.Contains("HasMedia", StringComparison.Ordinal), "HasMedia is not written.");
            ChatMessage back = JsonSerializer.Deserialize(json, PolyPromptJsonContext.Default.ChatMessage)!;
            SharedAssert.Equal(3, back.Content.Count, "Parts round-trip.");
            SharedAssert.Equal("look", back.Text, "Text round-trips.");
            SharedAssert.Equal(ContentPartType.Image, back.Content[1].Type, "Type round-trips as a string enum.");
            SharedAssert.True(Png.SequenceEqual(back.Content[1].Data!), "Bytes round-trip.");
            SharedAssert.Equal(ImageDetail.High, back.Content[1].Detail, "Detail round-trips.");
            SharedAssert.Equal(1000L, back.Content[2].Clip!.StartOffsetMs, "Clip round-trips.");
            SharedAssert.True(json.Contains("\"Type\":\"Image\"", StringComparison.Ordinal), "Enums are strings.");

            string again = JsonSerializer.Serialize(back, PolyPromptJsonContext.Default.ChatMessage);
            SharedAssert.Equal(json, again, "Serialization is stable.");

            bool threw = false;
            try { JsonSerializer.Deserialize("{\"Content\":42}", PolyPromptJsonContext.Default.ChatMessage); }
            catch (JsonException) { threw = true; }
            SharedAssert.True(threw, "A number is not valid content.");
            return Task.CompletedTask;
        }

        private static void Rejects(Action action, string contains, string message)
        {
            try { action(); }
            catch (ArgumentException ex)
            {
                SharedAssert.True(ex.Message.Contains(contains, StringComparison.Ordinal), message + " Message: " + ex.Message);
                return;
            }
            throw new TestFailureException(message + " (no ArgumentException)");
        }

        private static Task ValidationAsync(CancellationToken token)
        {
            Rejects(() => ContentPart.Image(Array.Empty<byte>(), MediaTypes.ImagePng), "Data is empty", "Empty data.");
            Rejects(() => ContentPart.Image(Png, MediaTypes.AudioWav), "not an image type", "Audio media type on an image.");
            Rejects(() => ContentPart.Image(Png, MediaTypes.ImageJpeg), "looks like 'image/png'", "Magic bytes disagree with the media type.");
            Rejects(() => ContentPart.Image(Png, "image/png\""), "not allowed", "Quote in the media type.");
            Rejects(() => ContentPart.ImageUrl("ftp://example.com/a.png"), "http or https", "Non-http URL.");
            Rejects(() => ContentPart.ImageUrl("relative/a.png"), "http or https", "Relative URL.");
            Rejects(() => ContentPart.FileReferenceOf(ContentPartType.Video, " ", MediaTypes.VideoMp4), "blank", "Blank reference.");
            Rejects(() => ContentPart.FileReferenceOf(ContentPartType.Text, "files/abc", MediaTypes.VideoMp4), "Text part requires Text", "Text-typed file reference.");
            Rejects(() => ContentPart.Video(Mp4, MediaTypes.VideoMp4, new VideoClip { StartOffsetMs = 5000, EndOffsetMs = 1000 }), "EndOffsetMs must be greater", "Clip end before start.");

            Rejects(() => new ContentPart { Type = ContentPartType.Image, Data = Png, Url = "https://example.com/a.png", MediaType = MediaTypes.ImagePng }.Validate("Messages[0].Content[1]"),
                "Messages[0].Content[1]: exactly one of", "Two sources, with the part path.");
            Rejects(() => new ContentPart { Type = ContentPartType.Image }.Validate(), "found 0", "No source.");
            Rejects(() => new ContentPart { Type = ContentPartType.Image, Data = Png }.Validate(), "requires MediaType", "Data without a media type.");
            Rejects(() => new ContentPart { Type = ContentPartType.Audio, Data = Wav, MediaType = MediaTypes.AudioWav, Detail = ImageDetail.Low }.Validate(), "Detail applies only to Image", "Detail on audio.");
            Rejects(() => new ContentPart { Type = ContentPartType.Image, Data = Png, MediaType = MediaTypes.ImagePng, Clip = new VideoClip() }.Validate(), "Clip applies only to Video", "Clip on an image.");
            Rejects(() => new ContentPart { Type = ContentPartType.Text, Text = "x", MediaType = "text/plain" }.Validate(), "cannot set MediaType", "Media type on text.");
            Rejects(() => new ContentPart { Type = ContentPartType.Image, Text = "x" }.Validate(), "an Image part cannot set Text", "Text on an image part.");

            bool threw = false;
            try { new VideoClip { FramesPerSecond = 30 }; } catch (ArgumentOutOfRangeException) { threw = true; }
            SharedAssert.True(threw, "Frames per second above 24.");
            threw = false;
            try { new VideoClip { StartOffsetMs = -1 }; } catch (ArgumentOutOfRangeException) { threw = true; }
            SharedAssert.True(threw, "Negative offset.");

            // Accepted: declared aliases and shared containers.
            ContentPart.Image(Jpeg, "image/jpg");
            ContentPart.Audio(Wav, "audio/x-wav");
            ContentPart.Audio(Mp4, MediaTypes.AudioMp4);
            ContentPart.Video(Mp4, MediaTypes.VideoQuicktime);
            ContentPart.ImageUrl("https://example.com/a.png", ImageDetail.Auto);
            ContentPart.FileReferenceOf(ContentPartType.Video, "gs://bucket/v.mp4", MediaTypes.VideoMp4);
            new ContentPart { Type = ContentPartType.Image, Data = new byte[] { 1, 2, 3 }, MediaType = MediaTypes.ImageWebp }.Validate();
            return Task.CompletedTask;
        }

        private static async Task FromFileAsync(CancellationToken token)
        {
            string dir = Path.Combine(Path.GetTempPath(), "polyprompt-fromfile-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string png = Path.Combine(dir, "photo.bin");
                await File.WriteAllBytesAsync(png, Png, token).ConfigureAwait(false);
                ContentPart image = ContentPart.FromFile(png);
                SharedAssert.Equal(ContentPartType.Image, image.Type, "Sniffed image.");
                SharedAssert.Equal(MediaTypes.ImagePng, image.MediaType, "Sniffed PNG despite the extension.");
                SharedAssert.Equal("photo.bin", image.Name, "Name is the file name.");

                string webm = Path.Combine(dir, "clip.webm");
                await File.WriteAllBytesAsync(webm, new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 1, 2 }, token).ConfigureAwait(false);
                SharedAssert.Equal(ContentPartType.Video, ContentPart.FromFile(webm).Type, "WebM is video.");

                string m4a = Path.Combine(dir, "voice.m4a");
                await File.WriteAllBytesAsync(m4a, new byte[] { 1, 2, 3, 4 }, token).ConfigureAwait(false);
                ContentPart audio = ContentPart.FromFile(m4a);
                SharedAssert.Equal(MediaTypes.AudioMp4, audio.MediaType, "Unrecognized bytes fall back to the extension.");

                string unknown = Path.Combine(dir, "notes.txt");
                await File.WriteAllBytesAsync(unknown, new byte[] { 1, 2, 3 }, token).ConfigureAwait(false);
                Rejects(() => ContentPart.FromFile(unknown), "Cannot determine the media type", "Unknown files are rejected.");
            }
            finally { Directory.Delete(dir, true); }
        }

        private static async Task HarnessAsync(CancellationToken token)
        {
            using CodexClient codex = new CodexClient("/nonexistent/codex");
            CliAgentRequest request = new CliAgentRequest
            {
                Prompt = "continue",
                History = new List<ChatMessage> { ChatMessage.User("look", ContentPart.Image(Png, MediaTypes.ImagePng)) }
            };
            await SharedAssert.ThrowsAsync<ArgumentException>(() => codex.RunAsync(request, token), "Media history is rejected.").ConfigureAwait(false);
        }
    }
}
