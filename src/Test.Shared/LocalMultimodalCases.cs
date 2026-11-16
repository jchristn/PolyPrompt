namespace Test.Shared
{
    using System.Text.Json;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using Touchstone.Core;

    /// <summary>
    /// Media on the wire: the exact shape each provider receives for images, audio, video, URLs, file references, and
    /// clips; validation that fails before the wire; inline size limits; raw versus escaped base64; and call detail
    /// redaction that never changes the bytes sent.
    /// </summary>
    internal static class LocalMultimodalCases
    {
        public static IEnumerable<TestCaseDescriptor> Create(string suite)
        {
            (string, string, Func<CancellationToken, Task>)[] cases =
            {
                ("media_image_bytes_every_provider", "Every ImageInput client sends inline image bytes in its provider's shape, after the text, unescaped", ImageBytesAsync),
                ("media_image_url", "URL images pass through where the provider fetches URLs and throw before the wire elsewhere", ImageUrlAsync),
                ("media_image_detail", "ImageDetail maps to OpenAI-style detail", DetailAsync),
                ("media_escape_option", "EscapeMediaInJson writes escaped base64 that decodes to the same bytes", EscapeAsync),
                ("media_multiple_images_and_tools", "Several images in one message, and an image beside tools", MultipleAndToolsAsync),
                ("media_compatible_audio_video", "OpenAI-compatible sends input_audio and video_url, and honors ImageUrlStyle", CompatibleAudioVideoAsync),
                ("media_gemini_video", "Gemini and Vertex send inline video, file references, YouTube links, and videoMetadata clips", GeminiVideoAsync),
                ("media_bedrock", "Bedrock sends image and video blocks with formats, inline bytes, and S3 locations", BedrockAsync),
                ("media_streaming", "Media works on streaming chat", StreamingAsync),
                ("media_validation", "Media on non-user roles, unsupported modalities, and malformed parts fail before the wire", ValidationAsync),
                ("media_inline_limit", "Inline media over MaxInlineMediaBytes throws with the provider's large-media route", LimitAsync),
                ("media_redaction", "Recorded bodies replace large media with placeholders; the wire keeps every byte", RedactionAsync),
                ("media_usage_and_modalities", "Gemini usage by modality, Ollama and xAI model input modalities are parsed", UsageAsync),
            };
            foreach (var (id, name, run) in cases) yield return new TestCaseDescriptor(suite, id, name, run, new[] { "local", "media" });
        }

        // 16 header bytes plus 2 align FB EF BE to a 3-byte group, so the base64 contains "++++" and escaping is visible.
        internal static readonly byte[] Png = LocalChatModelCases.Png.Concat(new byte[] { 0, 0, 0xFB, 0xEF, 0xBE, 1, 2, 3 }).ToArray();
        private static readonly string PngBase64 = Convert.ToBase64String(Png);
        private static string Big(int size) => Convert.ToBase64String(BigPng(size));
        private static byte[] BigPng(int size) => LocalChatModelCases.Png.Concat(Enumerable.Range(0, size - LocalChatModelCases.Png.Length).Select(i => (byte)(i * 7))).ToArray();

        private static JsonElement LastBody(LocalOpenAiTestServer server)
        {
            using JsonDocument doc = JsonDocument.Parse(server.RequestBodies.Last());
            return doc.RootElement.Clone();
        }

        private static JsonElement At(JsonElement e, params object[] path)
        {
            foreach (object step in path)
            {
                if (step is int i)
                {
                    if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() <= i) throw new TestFailureException("No index " + i + " in " + e.GetRawText());
                    e = e[i];
                }
                else
                {
                    if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty((string)step, out JsonElement next)) throw new TestFailureException("No property '" + step + "' in " + e.GetRawText());
                    e = next;
                }
            }
            return e;
        }

        private static string S(JsonElement e, params object[] path) => At(e, path).GetString() ?? "";

        /// <summary>
        /// The image part and the leading text part for each provider's shape.
        /// </summary>
        private static (JsonElement Text, JsonElement Image) Parts(string client, JsonElement body)
        {
            switch (client)
            {
                case nameof(OpenAiClient):
                case nameof(AzureOpenAiClient):
                case nameof(XAiClient):
                    return (At(body, "input", 0, "content", 0), At(body, "input", 0, "content", 1));
                case nameof(GeminiClient):
                case nameof(VertexAiClient):
                    return (At(body, "contents", 0, "parts", 0), At(body, "contents", 0, "parts", 1));
                case nameof(OllamaClient):
                    return (At(body, "messages", 0), At(body, "messages", 0, "images"));
                default:
                    return (At(body, "messages", 0, "content", 0), At(body, "messages", 0, "content", 1));
            }
        }

        private static void AssertImage(string client, JsonElement body, string base64, string label)
        {
            (JsonElement text, JsonElement image) = Parts(client, body);
            switch (client)
            {
                case nameof(OpenAiClient):
                case nameof(AzureOpenAiClient):
                case nameof(XAiClient):
                    SharedAssert.Equal("input_text", S(text, "type"), label + " text item.");
                    SharedAssert.Equal("Describe", S(text, "text"), label + " text first.");
                    SharedAssert.Equal("input_image", S(image, "type"), label + " image item.");
                    SharedAssert.Equal("data:image/png;base64," + base64, S(image, "image_url"), label + " data URL.");
                    break;
                case nameof(OpenAiCompatibleClient):
                case nameof(CohereClient):
                    SharedAssert.Equal("text", S(text, "type"), label + " text item.");
                    SharedAssert.Equal("Describe", S(text, "text"), label + " text first.");
                    SharedAssert.Equal("image_url", S(image, "type"), label + " image item.");
                    SharedAssert.Equal("data:image/png;base64," + base64, S(image, "image_url", "url"), label + " data URL.");
                    break;
                case nameof(AnthropicClient):
                    SharedAssert.Equal("text", S(text, "type"), label + " text block.");
                    SharedAssert.Equal("image", S(image, "type"), label + " image block.");
                    SharedAssert.Equal("base64", S(image, "source", "type"), label + " base64 source.");
                    SharedAssert.Equal("image/png", S(image, "source", "media_type"), label + " media type.");
                    SharedAssert.Equal(base64, S(image, "source", "data"), label + " raw base64.");
                    break;
                case nameof(GeminiClient):
                case nameof(VertexAiClient):
                    SharedAssert.Equal("Describe", S(text, "text"), label + " text part first.");
                    SharedAssert.Equal("image/png", S(image, "inlineData", "mimeType"), label + " mimeType.");
                    SharedAssert.Equal(base64, S(image, "inlineData", "data"), label + " raw base64.");
                    break;
                case nameof(BedrockClient):
                    SharedAssert.Equal("Describe", S(text, "text"), label + " text block first.");
                    SharedAssert.Equal("png", S(image, "image", "format"), label + " format.");
                    SharedAssert.Equal(base64, S(image, "image", "source", "bytes"), label + " raw base64 bytes.");
                    break;
                case nameof(OllamaClient):
                    SharedAssert.Equal("Describe", S(text, "content"), label + " text is the content.");
                    SharedAssert.Equal(base64, S(image, 0), label + " raw base64, no data URL.");
                    break;
                default:
                    throw new TestFailureException("No image shape for " + client);
            }
        }

        private static List<ProviderClientBase> Clients(LocalOpenAiTestServer server, Capability capability)
        {
            List<ProviderClientBase> clients = LocalClients.Supporting(capability, server.Endpoint);
            clients.Add(new OllamaCloudClient("k", server.Endpoint) { Defaults = { Completion = { Model = "test-model" } } });
            return clients.Where(c => c.Supports(capability)).ToList();
        }

        private static async Task ImageBytesAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            List<ProviderClientBase> clients = LocalClients.Supporting(Capability.ImageInput, server.Endpoint);
            try
            {
                SharedAssert.True(PngBase64.Contains("++++", StringComparison.Ordinal), "The test image's base64 contains '+': " + PngBase64);
                SharedAssert.Equal(10, clients.Count, "Ten local clients support images.");
                foreach (ProviderClientBase client in clients)
                {
                    string name = client.GetType().Name;
                    ChatResponse response = await client.ChatAsync(ChatMessage.User("Describe", ContentPart.Image(Png, MediaTypes.ImagePng)), null, token).ConfigureAwait(false);
                    SharedAssert.True(response.Success, name + ": " + response.Error);
                    string raw = server.RequestBodies.Last();
                    SharedAssert.True(raw.Contains(PngBase64, StringComparison.Ordinal), name + " sends base64 unescaped.");
                    SharedAssert.False(raw.Contains("\\u002B", StringComparison.Ordinal), name + " does not escape '+' in media.");
                    AssertImage(name, LastBody(server), PngBase64, name);
                }
            }
            finally { LocalClients.DisposeAll(clients); }
        }

        private static async Task ImageUrlAsync(CancellationToken token)
        {
            const string url = "https://example.com/cat.png";
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            List<ProviderClientBase> clients = Clients(server, Capability.ImageInput);
            try
            {
                foreach (ProviderClientBase client in clients)
                {
                    string name = client.GetType().Name;
                    bool fetches = client is ResponsesApiClientBase or OpenAiCompatibleClient or AnthropicClient or CohereClient;
                    int before = server.RequestBodies.Count;
                    if (!fetches)
                    {
                        await SharedAssert.ThrowsAsync<ArgumentException>(() => client.ChatAsync(ChatMessage.User("Describe", ContentPart.ImageUrl(url)), null, token), name + " rejects URL images.").ConfigureAwait(false);
                        SharedAssert.Equal(before, server.RequestBodies.Count, name + " sends nothing for a rejected URL.");
                        continue;
                    }

                    ChatResponse response = await client.ChatAsync(ChatMessage.User("Describe", ContentPart.ImageUrl(url)), null, token).ConfigureAwait(false);
                    SharedAssert.True(response.Success, name + ": " + response.Error);
                    JsonElement image = Parts(name, LastBody(server)).Image;
                    string sent = client switch
                    {
                        ResponsesApiClientBase => S(image, "image_url"),
                        AnthropicClient => S(image, "source", "url"),
                        _ => S(image, "image_url", "url"),
                    };
                    SharedAssert.Equal(url, sent, name + " passes the URL through.");
                    if (client is AnthropicClient) SharedAssert.Equal("url", S(image, "source", "type"), name + " URL source type.");
                }
            }
            finally { LocalClients.DisposeAll(clients); }
        }

        private static async Task DetailAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiClient openai = new OpenAiClient(LocalOpenAiResponsesRoutes.Key, server.Endpoint);
            await openai.ChatAsync(ChatMessage.User("Describe", ContentPart.Image(Png, MediaTypes.ImagePng, ImageDetail.High)), null, token).ConfigureAwait(false);
            SharedAssert.Equal("high", S(LastBody(server), "input", 0, "content", 1, "detail"), "Responses detail.");

            using OpenAiCompatibleClient compatible = new OpenAiCompatibleClient(server.Endpoint, LocalClients.TestKey) { Defaults = { Completion = { Model = "gpt-4o-mini" } } };
            await compatible.ChatAsync(ChatMessage.User("Describe", ContentPart.ImageUrl("https://example.com/a.png", ImageDetail.Low)), null, token).ConfigureAwait(false);
            SharedAssert.Equal("low", S(LastBody(server), "messages", 0, "content", 1, "image_url", "detail"), "Chat Completions detail.");

            using AnthropicClient anthropic = new AnthropicClient(LocalClients.TestKey, server.Endpoint);
            await anthropic.ChatAsync(ChatMessage.User("Describe", ContentPart.Image(Png, MediaTypes.ImagePng, ImageDetail.High)), null, token).ConfigureAwait(false);
            SharedAssert.False(server.RequestBodies.Last().Contains("detail", StringComparison.Ordinal), "Anthropic ignores detail.");
        }

        private static async Task EscapeAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using GeminiClient gemini = new GeminiClient(LocalClients.TestKey, server.Endpoint, new ClientOptions { EscapeMediaInJson = true });
            SharedAssert.True(gemini.EscapeMediaInJson, "The option applies.");
            ChatResponse response = await gemini.ChatAsync(ChatMessage.User("Describe", ContentPart.Image(Png, MediaTypes.ImagePng)), null, token).ConfigureAwait(false);
            SharedAssert.True(response.Success, response.Error ?? "escaped");
            string raw = server.RequestBodies.Last();
            SharedAssert.True(raw.Contains("\\u002B", StringComparison.Ordinal), "'+' is escaped: " + raw);
            SharedAssert.Equal(PngBase64, S(LastBody(server), "contents", 0, "parts", 1, "inlineData", "data"), "Escaped JSON decodes to the same base64.");
        }

        private static async Task MultipleAndToolsAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using AnthropicClient anthropic = new AnthropicClient(LocalClients.TestKey, server.Endpoint);
            byte[] jpeg = LocalChatModelCases.Jpeg;
            await anthropic.ChatAsync(ChatMessage.User("Compare", ContentPart.Image(Png, MediaTypes.ImagePng), ContentPart.Image(jpeg, MediaTypes.ImageJpeg), ContentPart.FromText("Which is larger?")), null, token).ConfigureAwait(false);
            JsonElement content = At(LastBody(server), "messages", 0, "content");
            SharedAssert.Equal(4, content.GetArrayLength(), "Text, image, image, text.");
            SharedAssert.Equal("image/jpeg", S(content, 2, "source", "media_type"), "Second image.");
            SharedAssert.Equal("Which is larger?", S(content, 3, "text"), "Trailing text keeps its place.");

            using OpenAiClient openai = new OpenAiClient(LocalOpenAiResponsesRoutes.Key, server.Endpoint);
            ChatRequest request = new ChatRequest
            {
                Messages = { ChatMessage.User("What is in this picture? Look it up.", ContentPart.Image(Png, MediaTypes.ImagePng)) },
                Tools = { ToolDefinition.Function("lookup", "Look something up", new Dictionary<string, object> { ["type"] = "object" }) }
            };
            ChatResponse response = await openai.ChatAsync(request, token).ConfigureAwait(false);
            SharedAssert.True(response.Success, response.Error ?? "tools and image");
            JsonElement body = LastBody(server);
            SharedAssert.Equal("lookup", S(body, "tools", 0, "name"), "Tools are sent.");
            SharedAssert.Equal("input_image", S(body, "input", 0, "content", 1, "type"), "The image is sent with tools.");
        }

        private static async Task CompatibleAudioVideoAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiCompatibleClient client = new OpenAiCompatibleClient(server.Endpoint, LocalClients.TestKey) { Defaults = { Completion = { Model = "gpt-4o-mini" } } };
            byte[] wav = LocalChatModelCases.Wav;
            ChatResponse response = await client.ChatAsync(ChatMessage.User("Transcribe and describe",
                ContentPart.Audio(wav, MediaTypes.AudioWav),
                ContentPart.Audio(new byte[] { 0x49, 0x44, 0x33, 4, 0 }, MediaTypes.AudioMpeg),
                ContentPart.VideoUrl("https://example.com/v.mp4"),
                ContentPart.Video(LocalChatModelCases.Mp4, MediaTypes.VideoMp4)), null, token).ConfigureAwait(false);
            SharedAssert.True(response.Success, response.Error ?? "audio and video");
            JsonElement content = At(LastBody(server), "messages", 0, "content");
            SharedAssert.Equal("input_audio", S(content, 1, "type"), "Audio item.");
            SharedAssert.Equal(Convert.ToBase64String(wav), S(content, 1, "input_audio", "data"), "Audio is raw base64.");
            SharedAssert.Equal("wav", S(content, 1, "input_audio", "format"), "WAV format.");
            SharedAssert.Equal("mp3", S(content, 2, "input_audio", "format"), "MPEG audio is mp3.");
            SharedAssert.Equal("video_url", S(content, 3, "type"), "Video item.");
            SharedAssert.Equal("https://example.com/v.mp4", S(content, 3, "video_url", "url"), "Video URL.");
            SharedAssert.True(S(content, 4, "video_url", "url").StartsWith("data:video/mp4;base64,", StringComparison.Ordinal), "Inline video is a data URL.");

            await SharedAssert.ThrowsAsync<ArgumentException>(() => client.ChatAsync(ChatMessage.User("x", ContentPart.FileReferenceOf(ContentPartType.Image, "file-1", MediaTypes.ImagePng)), null, token), "No file references.").ConfigureAwait(false);
            await SharedAssert.ThrowsAsync<ArgumentException>(() => client.ChatAsync(ChatMessage.User("x", new ContentPart { Type = ContentPartType.Audio, Url = "https://example.com/a.wav" }), null, token), "Audio by URL is rejected.").ConfigureAwait(false);

            using OpenAiCompatibleClient remoteOnly = new OpenAiCompatibleClient(server.Endpoint, LocalClients.TestKey, new OpenAiCompatibleClientOptions { ImageUrlStyle = ImageUrlStyle.RemoteUrlOnly }) { Defaults = { Completion = { Model = "gpt-4o-mini" } } };
            SharedAssert.Equal(ImageUrlStyle.RemoteUrlOnly, remoteOnly.ImageUrlStyle, "ImageUrlStyle applies.");
            int before = server.RequestBodies.Count;
            await SharedAssert.ThrowsAsync<ArgumentException>(() => remoteOnly.ChatAsync(ChatMessage.User("x", ContentPart.Image(Png, MediaTypes.ImagePng)), null, token), "RemoteUrlOnly rejects inline images.").ConfigureAwait(false);
            SharedAssert.Equal(before, server.RequestBodies.Count, "Nothing sent.");
            ChatResponse remote = await remoteOnly.ChatAsync(ChatMessage.User("x", ContentPart.ImageUrl("https://example.com/a.png")), null, token).ConfigureAwait(false);
            SharedAssert.True(remote.Success, remote.Error ?? "remote url");
        }

        private static async Task GeminiVideoAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            List<ProviderClientBase> clients = LocalClients.All(server.Endpoint).Where(c => c is GenerateContentClientBase).ToList();
            try
            {
                foreach (ProviderClientBase client in clients)
                {
                    string name = client.GetType().Name;
                    ChatResponse response = await client.ChatAsync(ChatMessage.User("Summarize",
                        ContentPart.Video(LocalChatModelCases.Mp4, MediaTypes.VideoMp4, new VideoClip { StartOffsetMs = 1500, EndOffsetMs = 10000, FramesPerSecond = 2 }),
                        ContentPart.FileReferenceOf(ContentPartType.Video, "https://generativelanguage.googleapis.com/v1beta/files/abc", MediaTypes.VideoMp4),
                        ContentPart.VideoUrl("https://www.youtube.com/watch?v=dQw4w9WgXcQ", new VideoClip { FramesPerSecond = 0.5 }),
                        ContentPart.Audio(LocalChatModelCases.Wav, MediaTypes.AudioWav)), null, token).ConfigureAwait(false);
                    SharedAssert.True(response.Success, name + ": " + response.Error);
                    JsonElement parts = At(LastBody(server), "contents", 0, "parts");
                    SharedAssert.Equal("video/mp4", S(parts, 1, "inlineData", "mimeType"), name + " inline video.");
                    SharedAssert.Equal("1.5s", S(parts, 1, "videoMetadata", "startOffset"), name + " start offset.");
                    SharedAssert.Equal("10s", S(parts, 1, "videoMetadata", "endOffset"), name + " end offset.");
                    SharedAssert.Equal(2.0, At(parts, 1, "videoMetadata", "fps").GetDouble(), name + " fps.");
                    SharedAssert.Equal("https://generativelanguage.googleapis.com/v1beta/files/abc", S(parts, 2, "fileData", "fileUri"), name + " file reference.");
                    SharedAssert.Equal("https://www.youtube.com/watch?v=dQw4w9WgXcQ", S(parts, 3, "fileData", "fileUri"), name + " YouTube link.");
                    SharedAssert.Equal("video/mp4", S(parts, 3, "fileData", "mimeType"), name + " YouTube default mime type.");
                    SharedAssert.Equal(0.5, At(parts, 3, "videoMetadata", "fps").GetDouble(), name + " YouTube clip.");
                    SharedAssert.False(At(parts, 3, "videoMetadata").TryGetProperty("startOffset", out _), name + " only set clip fields are sent.");
                    SharedAssert.Equal("audio/wav", S(parts, 4, "inlineData", "mimeType"), name + " audio.");

                    await SharedAssert.ThrowsAsync<ArgumentException>(() => client.ChatAsync(ChatMessage.User("x", ContentPart.VideoUrl("https://vimeo.com/1")), null, token), name + " rejects non-YouTube URLs.").ConfigureAwait(false);
                }
            }
            finally { LocalClients.DisposeAll(clients); }
        }

        private static async Task BedrockAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using BedrockClient bedrock = new BedrockClient("us-east-1", LocalClients.AwsCredential(), server.Endpoint) { Defaults = { Completion = { Model = "amazon.nova-pro-v1:0" } }, S3BucketOwner = "111122223333" };
            ChatResponse response = await bedrock.ChatAsync(ChatMessage.User("Describe",
                ContentPart.Image(LocalChatModelCases.Jpeg, MediaTypes.ImageJpeg),
                ContentPart.Video(LocalChatModelCases.Mp4, MediaTypes.VideoQuicktime),
                ContentPart.FileReferenceOf(ContentPartType.Video, "s3://bucket/clip.mp4", MediaTypes.VideoMp4)), null, token).ConfigureAwait(false);
            SharedAssert.True(response.Success, response.Error ?? "bedrock");
            JsonElement content = At(LastBody(server), "messages", 0, "content");
            SharedAssert.Equal("jpeg", S(content, 1, "image", "format"), "JPEG format.");
            SharedAssert.Equal("mov", S(content, 2, "video", "format"), "QuickTime is mov.");
            SharedAssert.Equal(Convert.ToBase64String(LocalChatModelCases.Mp4), S(content, 2, "video", "source", "bytes"), "Inline video bytes.");
            SharedAssert.Equal("s3://bucket/clip.mp4", S(content, 3, "video", "source", "s3Location", "uri"), "S3 URI.");
            SharedAssert.Equal("111122223333", S(content, 3, "video", "source", "s3Location", "bucketOwner"), "Bucket owner.");

            await SharedAssert.ThrowsAsync<ArgumentException>(() => bedrock.ChatAsync(ChatMessage.User("x", ContentPart.FileReferenceOf(ContentPartType.Image, "gs://b/o.png", MediaTypes.ImagePng)), null, token), "Non-S3 references are rejected.").ConfigureAwait(false);
            await SharedAssert.ThrowsAsync<ArgumentException>(() => bedrock.ChatAsync(ChatMessage.User("x", ContentPart.Image(new byte[] { 0x42, 0x4D, 1, 2 }, "image/bmp")), null, token), "Unsupported formats are rejected.").ConfigureAwait(false);
        }

        private static async Task StreamingAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            List<ProviderClientBase> clients = LocalClients.Supporting(Capability.ImageInput, server.Endpoint);
            try
            {
                foreach (ProviderClientBase client in clients)
                {
                    string name = client.GetType().Name;
                    ChatStreamingResponse response = await client.ChatStreamingAsync(ChatMessage.User("Describe", ContentPart.Image(Png, MediaTypes.ImagePng)), null, token).ConfigureAwait(false);
                    await foreach (ChatStreamingChunk _ in response.Chunks.WithCancellation(token).ConfigureAwait(false)) { }
                    SharedAssert.True(response.Success, name + ": " + response.Error);
                    SharedAssert.Equal(StreamingState.Completed, response.State, name + " completes.");
                    AssertImage(name, LastBody(server), PngBase64, name + " streaming");
                }
            }
            finally { LocalClients.DisposeAll(clients); }
        }

        private static async Task ValidationAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            List<ProviderClientBase> clients = LocalClients.Completion(server.Endpoint);
            try
            {
                foreach (ProviderClientBase client in clients)
                {
                    string name = client.GetType().Name;
                    int before = server.RequestBodies.Count;
                    ContentPart image = ContentPart.Image(Png, MediaTypes.ImagePng);

                    if (client.Supports(Capability.ImageInput))
                    {
                        foreach (ChatMessage wrong in new[]
                        {
                            new ChatMessage { Role = "assistant", Content = { image } },
                            new ChatMessage { Role = "system", Content = { ContentPart.FromText("s"), image } },
                            new ChatMessage { Role = "tool", ToolCallId = "c", ToolName = "f", Content = { image } },
                        })
                        {
                            try
                            {
                                await client.ChatAsync(new[] { ChatMessage.User("hi"), wrong }, null, token).ConfigureAwait(false);
                                throw new TestFailureException(name + " accepted media on a " + wrong.Role + " message.");
                            }
                            catch (ArgumentException ex)
                            {
                                SharedAssert.True(ex.Message.Contains("Messages[1].Content[", StringComparison.Ordinal) && ex.Message.Contains("only on user messages", StringComparison.Ordinal), name + " names the part and the rule: " + ex.Message);
                            }
                        }

                        ContentPart broken = new ContentPart { Type = ContentPartType.Image, Data = Png, Url = "https://example.com/a.png", MediaType = MediaTypes.ImagePng };
                        await SharedAssert.ThrowsAsync<ArgumentException>(() => client.ChatAsync(ChatMessage.User("x", broken), null, token), name + " validates parts.").ConfigureAwait(false);
                        ContentPart mismatch = new ContentPart { Type = ContentPartType.Image, Data = Png, MediaType = MediaTypes.ImageJpeg };
                        await SharedAssert.ThrowsAsync<ArgumentException>(() => client.ChatAsync(ChatMessage.User("x", mismatch), null, token), name + " checks magic bytes.").ConfigureAwait(false);
                    }

                    foreach (var (capability, part) in new (Capability, ContentPart)[]
                    {
                        (Capability.ImageInput, image),
                        (Capability.AudioInput, ContentPart.Audio(LocalChatModelCases.Wav, MediaTypes.AudioWav)),
                        (Capability.VideoInput, ContentPart.Video(LocalChatModelCases.Mp4, MediaTypes.VideoMp4)),
                    })
                    {
                        if (client.Supports(capability)) continue;
                        CapabilityNotSupportedException? caught = null;
                        try { _ = client.ChatAsync(ChatMessage.User("x", part), null, token); }
                        catch (CapabilityNotSupportedException ex) { caught = ex; }
                        SharedAssert.NotNull(caught, name + " throws synchronously for unsupported " + capability + ".");
                        SharedAssert.Equal(capability, caught!.Capability, name + " names " + capability + ".");
                    }

                    SharedAssert.Equal(before, server.RequestBodies.Count, name + " sends nothing for invalid media.");
                }
            }
            finally { LocalClients.DisposeAll(clients); }
        }

        private static async Task LimitAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using GeminiClient gemini = new GeminiClient(LocalClients.TestKey, server.Endpoint, new ClientOptions { MaxInlineMediaBytes = 100 });
            SharedAssert.Equal(100L, gemini.MaxInlineMediaBytes, "The option applies.");
            using AnthropicClient anthropic = new AnthropicClient(LocalClients.TestKey, server.Endpoint);
            SharedAssert.Equal(20L * 1024 * 1024, anthropic.MaxInlineMediaBytes, "The default is 20 MB.");

            byte[] big = BigPng(200);
            try
            {
                _ = gemini.ChatAsync(ChatMessage.User("x", ContentPart.Image(big, MediaTypes.ImagePng)), null, token);
                throw new TestFailureException("Over-limit media was accepted.");
            }
            catch (ArgumentException ex)
            {
                SharedAssert.True(ex.Message.Contains("Gemini", StringComparison.Ordinal), "Names the provider: " + ex.Message);
                SharedAssert.True(ex.Message.Contains("MaxInlineMediaBytes", StringComparison.Ordinal), "Names the setting: " + ex.Message);
                SharedAssert.True(ex.Message.Contains("UploadFileAsync", StringComparison.Ordinal), "Points at the upload route: " + ex.Message);
            }
            SharedAssert.Equal(0, server.RequestBodies.Count, "Nothing sent.");

            // Data URL overhead counts: 60 bytes is 80 base64 characters, plus 22 for "data:image/png;base64,".
            using OpenAiCompatibleClient compatible = new OpenAiCompatibleClient(server.Endpoint, LocalClients.TestKey, new ClientOptions { MaxInlineMediaBytes = 90 }) { Defaults = { Completion = { Model = "m" } } };
            await SharedAssert.ThrowsAsync<ArgumentException>(() => compatible.ChatAsync(ChatMessage.User("x", ContentPart.Image(BigPng(60), MediaTypes.ImagePng)), null, token), "The data URL prefix counts.").ConfigureAwait(false);
            using GeminiClient roomy = new GeminiClient(LocalClients.TestKey, server.Endpoint, new ClientOptions { MaxInlineMediaBytes = 90 });
            ChatResponse fits = await roomy.ChatAsync(ChatMessage.User("x", ContentPart.Image(BigPng(60), MediaTypes.ImagePng)), null, token).ConfigureAwait(false);
            SharedAssert.True(fits.Success, "Raw base64 without a prefix fits: " + fits.Error);
            using GeminiClient references = new GeminiClient(LocalClients.TestKey, server.Endpoint, new ClientOptions { MaxInlineMediaBytes = 1 });
            ChatResponse referenced = await references.ChatAsync(ChatMessage.User("x", ContentPart.FileReferenceOf(ContentPartType.Video, "https://generativelanguage.googleapis.com/v1beta/files/x", MediaTypes.VideoMp4)), null, token).ConfigureAwait(false);
            SharedAssert.True(referenced.Success, "File references do not count toward the inline limit: " + referenced.Error);

            bool threw = false;
            try { gemini.MaxInlineMediaBytes = 0; } catch (ArgumentOutOfRangeException) { threw = true; }
            SharedAssert.True(threw, "The limit must be positive.");
        }

        private static async Task RedactionAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            byte[] big = BigPng(4096);
            string bigBase64 = Convert.ToBase64String(big);
            List<ProviderClientBase> clients = LocalClients.Supporting(Capability.ImageInput, server.Endpoint);
            try
            {
                foreach (ProviderClientBase client in clients)
                {
                    string name = client.GetType().Name;
                    WireCapture.Full(client);
                    ChatResponse response = await client.ChatAsync(ChatMessage.User("Describe", ContentPart.Image(big, MediaTypes.ImagePng), ContentPart.Image(Png, MediaTypes.ImagePng)), null, token).ConfigureAwait(false);
                    SharedAssert.True(response.Success, name + ": " + response.Error);
                    SharedAssert.True(server.RequestBodies.Last().Contains(bigBase64, StringComparison.Ordinal), name + " sends every byte.");
                    string recorded = client.CallDetails.Last().RequestBody ?? "";
                    SharedAssert.False(recorded.Contains(bigBase64, StringComparison.Ordinal), name + " does not record the large image.");
                    SharedAssert.True(recorded.Contains("<base64 4096 bytes image/png sha256=", StringComparison.Ordinal), name + " records a placeholder: " + recorded);
                    SharedAssert.True(recorded.Contains(PngBase64, StringComparison.Ordinal), name + " records media under the threshold in full.");
                    using (JsonDocument.Parse(recorded)) { }
                }
            }
            finally { LocalClients.DisposeAll(clients); }

            using GeminiClient full = WireCapture.Full(new GeminiClient(LocalClients.TestKey, server.Endpoint, new ClientOptions { RedactMediaInCallDetails = false }));
            await full.ChatAsync(ChatMessage.User("x", ContentPart.Image(big, MediaTypes.ImagePng)), null, token).ConfigureAwait(false);
            SharedAssert.True((full.CallDetails.Last().RequestBody ?? "").Contains(bigBase64, StringComparison.Ordinal), "Redaction off records the full body.");

            using GeminiClient lowThreshold = WireCapture.Full(new GeminiClient(LocalClients.TestKey, server.Endpoint, new ClientOptions { MediaRedactionThresholdBytes = 0 }));
            await lowThreshold.ChatAsync(ChatMessage.User("x", ContentPart.Image(Png, MediaTypes.ImagePng)), null, token).ConfigureAwait(false);
            SharedAssert.False((lowThreshold.CallDetails.Last().RequestBody ?? "").Contains(PngBase64, StringComparison.Ordinal), "A zero threshold redacts every media value.");

            using GeminiClient metadata = new GeminiClient(LocalClients.TestKey, server.Endpoint);
            await metadata.ChatAsync(ChatMessage.User("x", ContentPart.Image(big, MediaTypes.ImagePng)), null, token).ConfigureAwait(false);
            SharedAssert.Equal(null, metadata.CallDetails.Last().RequestBody, "Metadata capture records no body.");

            using GeminiClient text = WireCapture.Full(new GeminiClient(LocalClients.TestKey, server.Endpoint));
            await text.ChatAsync("plain text", null, token).ConfigureAwait(false);
            SharedAssert.Equal(server.RequestBodies.Last(), text.CallDetails.Last().RequestBody, "Text-only bodies are recorded exactly as sent.");
        }

        private static async Task UsageAsync(CancellationToken token)
        {
            string gemini = "{\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[{\"text\":\"a cat\"}]},\"finishReason\":\"STOP\"}],"
                + "\"usageMetadata\":{\"promptTokenCount\":268,\"candidatesTokenCount\":2,\"totalTokenCount\":270,"
                + "\"promptTokensDetails\":[{\"modality\":\"TEXT\",\"tokenCount\":10},{\"modality\":\"IMAGE\",\"tokenCount\":258}]}}";
            using HttpClient geminiHttp = new HttpClient(LocalFakeHandler.Always(System.Net.HttpStatusCode.OK, gemini));
            using GeminiClient geminiClient = new GeminiClient(LocalClients.TestKey, "http://127.0.0.1:1", new ClientOptions { HttpClient = geminiHttp });
            ChatResponse response = await geminiClient.ChatAsync(ChatMessage.User("x", ContentPart.Image(Png, MediaTypes.ImagePng)), null, token).ConfigureAwait(false);
            SharedAssert.True(response.Success, response.Error ?? "gemini usage");
            SharedAssert.Equal(10L, response.Usage!.InputByModality!.Text, "Text tokens.");
            SharedAssert.Equal(258L, response.Usage.InputByModality.Image, "Image tokens.");
            SharedAssert.Equal(null, response.Usage.InputByModality.Video, "Unreported modality is null.");

            string show = "{\"modelfile\":\"\",\"template\":\"\",\"capabilities\":[\"completion\",\"vision\"],\"details\":{}}";
            using HttpClient ollamaHttp = new HttpClient(LocalFakeHandler.Always(System.Net.HttpStatusCode.OK, show));
            using OllamaClient ollama = new OllamaClient("http://127.0.0.1:1", null, new ClientOptions { HttpClient = ollamaHttp });
            ModelInformation? info = await ollama.GetModelInformationAsync("llava", token).ConfigureAwait(false);
            SharedAssert.NotNull(info, "Ollama model information.");
            SharedAssert.Equal("Text,Image", string.Join(",", info!.InputModalities!), "Ollama vision capability.");

            string catalog = "{\"id\":\"grok-4.7\",\"owned_by\":\"xai\",\"input_modalities\":[\"text\",\"image\"]}";
            using HttpClient xaiHttp = new HttpClient(LocalFakeHandler.Always(System.Net.HttpStatusCode.OK, catalog));
            using XAiClient xai = new XAiClient(LocalXAiRoutes.Key, "http://127.0.0.1:1", new ClientOptions { HttpClient = xaiHttp });
            ModelInformation? model = await xai.GetModelInformationAsync("grok-4.7", token).ConfigureAwait(false);
            SharedAssert.Equal("Text,Image", string.Join(",", model!.InputModalities!), "xAI input modalities.");

            string json = JsonSerializer.Serialize(model, PolyPrompt.Helpers.PolyPromptJsonContext.Default.ModelInformation);
            SharedAssert.True(json.Contains("\"InputModalities\":[\"Text\",\"Image\"]", StringComparison.Ordinal), "Modalities serialize as names: " + json);
        }
    }
}
