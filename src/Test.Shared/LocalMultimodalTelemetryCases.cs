namespace Test.Shared
{
    using System.Diagnostics;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using Touchstone.Core;
    using N = PolyPrompt.Telemetry.PolyPromptTelemetryNames;

    /// <summary>
    /// Media telemetry: span attributes and the media counter and bytes histogram by modality, nothing for text-only
    /// requests, the modality label's bounded values, and token usage by modality.
    /// </summary>
    internal static class LocalMultimodalTelemetryCases
    {
        public static IEnumerable<TestCaseDescriptor> Create(string suite)
        {
            (string, string, Func<CancellationToken, Task>)[] cases =
            {
                ("media_telemetry_spans_and_metrics", "Chat spans count media parts and bytes, and media metrics are labeled by modality", SpansAndMetricsAsync),
                ("media_telemetry_text_only", "Text-only chat records no media attributes or media metrics", TextOnlyAsync),
                ("media_telemetry_modality_tokens", "Reported tokens by modality are counted as input_text, input_image, input_audio, and input_video", TokensAsync),
            };
            foreach (var (id, name, run) in cases) yield return new TestCaseDescriptor(suite, id, name, run, new[] { "local", "media", "telemetry" });
        }

        private static async Task SpansAndMetricsAsync(CancellationToken token)
        {
            using TelemetryCapture capture = new TelemetryCapture("tel_media");
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiCompatibleClient client = new OpenAiCompatibleClient(server.Endpoint, LocalClients.TestKey) { Defaults = { Completion = { Model = "gpt-4o-mini" } } };
            byte[] png = LocalMultimodalCases.Png;
            byte[] wav = LocalChatModelCases.Wav;
            ChatResponse response = await client.ChatAsync(ChatMessage.User("Look and listen",
                ContentPart.Image(png, MediaTypes.ImagePng),
                ContentPart.ImageUrl("https://example.com/b.png"),
                ContentPart.Audio(wav, MediaTypes.AudioWav),
                ContentPart.VideoUrl("https://example.com/v.mp4")), null, token).ConfigureAwait(false);
            SharedAssert.True(response.Success, response.Error ?? "media chat");

            Activity span = capture.Span("openai chat");
            SharedAssert.Equal("2", span.GetTagItem(N.InputImages)?.ToString(), "Images.");
            SharedAssert.Equal("1", span.GetTagItem(N.InputAudio)?.ToString(), "Audio.");
            SharedAssert.Equal("1", span.GetTagItem(N.InputVideos)?.ToString(), "Videos.");
            SharedAssert.Equal((png.Length + wav.Length).ToString(), span.GetTagItem(N.InputMediaBytesAttribute)?.ToString(), "Inline bytes only.");
            SharedAssert.Equal(null, span.GetTagItem(N.InputFileReferences), "No file references.");

            SharedAssert.Equal(2.0, capture.Require(N.InputMedia, N.Provider, "openai", N.Operation, "chat", N.Modality, "image").Value, "Image parts counted.");
            SharedAssert.Equal(1.0, capture.Require(N.InputMedia, N.Modality, "audio").Value, "Audio parts counted.");
            SharedAssert.Equal(1.0, capture.Require(N.InputMedia, N.Modality, "video").Value, "Video parts counted.");
            SharedAssert.Equal((double)png.Length, capture.Require(N.InputMediaBytes, N.Modality, "image").Value, "Image bytes.");
            SharedAssert.Equal(0, capture.Measurements(N.InputMediaBytes, N.Modality, "video").Count, "No bytes histogram for URL-only video.");
            foreach (CapturedMeasurement m in capture.Measurements(N.InputMedia).Concat(capture.Measurements(N.InputMediaBytes)))
                SharedAssert.True(new[] { "image", "audio", "video" }.Contains(m.Tags[N.Modality]), "Modality is bounded: " + m);

            using GeminiClient gemini = new GeminiClient(LocalClients.TestKey, server.Endpoint);
            await gemini.ChatAsync(ChatMessage.User("x", ContentPart.FileReferenceOf(ContentPartType.Video, "https://generativelanguage.googleapis.com/v1beta/files/a", MediaTypes.VideoMp4)), null, token).ConfigureAwait(false);
            Activity geminiSpan = capture.Span("gcp.gemini chat");
            SharedAssert.Equal("1", geminiSpan.GetTagItem(N.InputFileReferences)?.ToString(), "File references counted.");
            SharedAssert.Equal("0", geminiSpan.GetTagItem(N.InputMediaBytesAttribute)?.ToString(), "File references carry no inline bytes.");

            using GeminiClient streaming = new GeminiClient(LocalClients.TestKey, server.Endpoint);
            ChatStreamingResponse stream = await streaming.ChatStreamingAsync(ChatMessage.User("x", ContentPart.Image(png, MediaTypes.ImagePng)), null, token).ConfigureAwait(false);
            await foreach (ChatStreamingChunk _ in stream.Chunks.WithCancellation(token).ConfigureAwait(false)) { }
            SharedAssert.Equal("1", capture.Span("gcp.gemini chat_stream").GetTagItem(N.InputImages)?.ToString(), "Streaming chat records media too.");
        }

        private static async Task TextOnlyAsync(CancellationToken token)
        {
            using TelemetryCapture capture = new TelemetryCapture("tel_media_text");
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using GeminiClient gemini = new GeminiClient(LocalClients.TestKey, server.Endpoint);
            await gemini.ChatAsync("plain", null, token).ConfigureAwait(false);
            Activity span = capture.Span("gcp.gemini chat");
            foreach (string attribute in new[] { N.InputImages, N.InputAudio, N.InputVideos, N.InputMediaBytesAttribute, N.InputFileReferences })
                SharedAssert.Equal(null, span.GetTagItem(attribute), attribute + " is not set for text-only chat.");
            SharedAssert.Equal(0, capture.Measurements(N.InputMedia).Count, "No media counter.");
            SharedAssert.Equal(0, capture.Measurements(N.InputMediaBytes).Count, "No media bytes histogram.");
        }

        private static async Task TokensAsync(CancellationToken token)
        {
            using TelemetryCapture capture = new TelemetryCapture("tel_media_tokens");
            string body = "{\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[{\"text\":\"a cat\"}]},\"finishReason\":\"STOP\"}],"
                + "\"usageMetadata\":{\"promptTokenCount\":268,\"candidatesTokenCount\":2,\"totalTokenCount\":270,"
                + "\"promptTokensDetails\":[{\"modality\":\"TEXT\",\"tokenCount\":10},{\"modality\":\"IMAGE\",\"tokenCount\":258}]}}";
            using HttpClient http = new HttpClient(LocalFakeHandler.Always(System.Net.HttpStatusCode.OK, body));
            using GeminiClient gemini = new GeminiClient(LocalClients.TestKey, "http://127.0.0.1:1", new ClientOptions { HttpClient = http });
            await gemini.ChatAsync(ChatMessage.User("x", ContentPart.Image(LocalMultimodalCases.Png, MediaTypes.ImagePng)), null, token).ConfigureAwait(false);
            SharedAssert.Equal(258.0, capture.Require(N.Tokens, N.TokenType, "input_image").Value, "Image tokens.");
            SharedAssert.Equal(10.0, capture.Require(N.Tokens, N.TokenType, "input_text").Value, "Text tokens.");
            SharedAssert.Equal(268.0, capture.Require(N.Tokens, N.TokenType, "input").Value, "Total input is unchanged.");
            SharedAssert.Equal(0, capture.Measurements(N.Tokens, N.TokenType, "input_video").Count, "Unreported modalities are not recorded.");
        }
    }
}
