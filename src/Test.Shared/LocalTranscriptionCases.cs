namespace Test.Shared
{
    using System.Text;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using Touchstone.Core;
    using N = PolyPrompt.Telemetry.PolyPromptTelemetryNames;

    /// <summary>
    /// TranscribeAsync: the multipart fields sent for each format and option, response parsing for text, json, and
    /// verbose_json with segments and words, per-model format checks, auth placement, Azure routing, call detail
    /// summaries, errors, and telemetry.
    /// </summary>
    internal static class LocalTranscriptionCases
    {
        public static IEnumerable<TestCaseDescriptor> Create(string suite)
        {
            (string, string, Func<CancellationToken, Task>)[] cases =
            {
                ("transcribe_fields", "TranscribeAsync sends the file, model, format, language, prompt, temperature, and granularities as multipart fields", FieldsAsync),
                ("transcribe_formats", "Text, json, and verbose_json responses parse into text, usage, segments, and words", FormatsAsync),
                ("transcribe_validation", "Empty audio, unknown or non-audio types, granularities without VerboseJson, and missing models fail before the wire", ValidationAsync),
                ("transcribe_model_formats", "OpenAI and Azure reject formats gpt-4o transcription models lack; OpenAI-compatible passes them through", ModelFormatsAsync),
                ("transcribe_routing_and_auth", "OpenAI, Azure (deployment as model), and OpenAI-compatible auth placements reach the transcription route", RoutingAsync),
                ("transcribe_call_details_and_errors", "Recorded bodies summarize files without bytes; HTTP errors surface as unsuccessful responses", DetailsAndErrorsAsync),
                ("transcribe_telemetry", "Transcription spans carry the operation, audio bytes, duration, and segments", TelemetryAsync),
            };
            foreach (var (id, name, run) in cases) yield return new TestCaseDescriptor(suite, id, name, run, new[] { "local", "transcription" });
        }

        private static readonly byte[] Flac = Encoding.ASCII.GetBytes("fLaC").Concat(Enumerable.Range(0, 300).Select(i => (byte)i)).ToArray();

        private static OpenAiCompatibleClient Compatible(LocalOpenAiTestServer server, ClientOptions? options = null) =>
            new OpenAiCompatibleClient(server.Endpoint, LocalClients.TestKey, options) { Defaults = { Transcription = { Model = "Systran/faster-whisper-small" } } };

        private static List<LocalTranscriptionRoutes.Field> LastFields(LocalOpenAiTestServer server) =>
            LocalTranscriptionRoutes.Parse(server.RequestContentTypes.Last(), server.RequestRawBodies.Last());

        private static string Value(List<LocalTranscriptionRoutes.Field> fields, string name) =>
            fields.FirstOrDefault(f => f.Name == name)?.Text ?? throw new TestFailureException("No field '" + name + "'.");

        private static async Task FieldsAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiCompatibleClient client = Compatible(server);

            TranscriptionResponse plain = await client.TranscribeAsync(new TranscriptionRequest { Audio = Flac }, null, token).ConfigureAwait(false);
            SharedAssert.True(plain.Success, plain.Error ?? "plain");
            SharedAssert.True(server.RequestContentTypes.Last().StartsWith("multipart/form-data; boundary=", StringComparison.Ordinal), "Multipart content type: " + server.RequestContentTypes.Last());
            List<LocalTranscriptionRoutes.Field> fields = LastFields(server);
            LocalTranscriptionRoutes.Field file = fields.Single(f => f.Name == "file");
            SharedAssert.True(Flac.SequenceEqual(file.Data), "The audio bytes arrive intact.");
            SharedAssert.Equal("audio.flac", file.FileName, "File name from the detected media type.");
            SharedAssert.Equal("audio/flac", file.ContentType, "File media type, detected from the bytes.");
            SharedAssert.Equal("Systran/faster-whisper-small", Value(fields, "model"), "Model from defaults.");
            SharedAssert.Equal("json", Value(fields, "response_format"), "Json is the default format.");
            SharedAssert.Equal("model,file,response_format", string.Join(",", fields.Select(f => f.Name).OrderBy(n => n == "model" ? 0 : n == "file" ? 1 : 2)), "Only the set fields are sent.");

            await client.TranscribeAsync(new TranscriptionRequest
            {
                Audio = Flac,
                MediaType = "audio/x-flac",
                FileName = "chunk-0001.flac",
                Language = "de",
                Prompt = "Keyframe, Polyprompt",
                Temperature = 0.2,
                Format = TranscriptionFormat.VerboseJson,
                TimestampGranularities = { TranscriptionTimestampGranularity.Segment, TranscriptionTimestampGranularity.Word, TranscriptionTimestampGranularity.Word },
            }, new TranscriptionOptions { Model = "large-v3" }, token).ConfigureAwait(false);
            fields = LastFields(server);
            SharedAssert.Equal("chunk-0001.flac", fields.Single(f => f.Name == "file").FileName, "Explicit file name.");
            SharedAssert.Equal("audio/x-flac", fields.Single(f => f.Name == "file").ContentType, "Explicit media type.");
            SharedAssert.Equal("large-v3", Value(fields, "model"), "Per-call model.");
            SharedAssert.Equal("verbose_json", Value(fields, "response_format"), "Verbose format.");
            SharedAssert.Equal("de", Value(fields, "language"), "Language.");
            SharedAssert.Equal("Keyframe, Polyprompt", Value(fields, "prompt"), "Prompt.");
            SharedAssert.Equal("0.2", Value(fields, "temperature"), "Temperature, invariant culture.");
            SharedAssert.Equal("segment,word", string.Join(",", fields.Where(f => f.Name == "timestamp_granularities[]").Select(f => f.Text)), "Granularities, deduplicated.");
        }

        private static async Task FormatsAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiCompatibleClient client = Compatible(server);

            TranscriptionResponse json = await client.TranscribeAsync(new TranscriptionRequest { Audio = Flac }, null, token).ConfigureAwait(false);
            SharedAssert.Equal("hello world", json.Text, "Json text.");
            SharedAssert.Equal(15, json.Usage!.TotalTokens, "Token usage.");
            SharedAssert.Equal(0, json.Segments.Count, "No segments for json.");
            SharedAssert.NotNull(json.RawResponse, "Json responses keep RawResponse.");

            TranscriptionResponse text = await client.TranscribeAsync(new TranscriptionRequest { Audio = Flac, Format = TranscriptionFormat.Text }, null, token).ConfigureAwait(false);
            SharedAssert.True(text.Success, text.Error ?? "text");
            SharedAssert.Equal("hello world", text.Text, "Text format, trailing newline trimmed.");

            TranscriptionResponse verbose = await client.TranscribeAsync(new TranscriptionRequest { Audio = Flac, Format = TranscriptionFormat.VerboseJson, TimestampGranularities = { TranscriptionTimestampGranularity.Word } }, null, token).ConfigureAwait(false);
            SharedAssert.Equal("english", verbose.Language, "Language.");
            SharedAssert.Equal(2.5, verbose.DurationSeconds, "Duration.");
            SharedAssert.Equal(2, verbose.Segments.Count, "Segments.");
            SharedAssert.Equal(0.7, verbose.Segments[1].StartSeconds, "Segment start.");
            SharedAssert.Equal(1.4, verbose.Segments[1].EndSeconds, "Segment end.");
            SharedAssert.Equal(" world", verbose.Segments[1].Text, "Segment text.");
            SharedAssert.Equal(-0.3, verbose.Segments[1].AverageLogProbability, "Average log probability.");
            SharedAssert.Equal(0.02, verbose.Segments[1].NoSpeechProbability, "No-speech probability.");
            SharedAssert.Equal(2, verbose.Words.Count, "Words.");
            SharedAssert.Equal("world", verbose.Words[1].Word, "Word.");

            TranscriptionResponse segmentsOnly = await client.TranscribeAsync(new TranscriptionRequest { Audio = Flac, Format = TranscriptionFormat.VerboseJson }, null, token).ConfigureAwait(false);
            SharedAssert.Equal(0, segmentsOnly.Words.Count, "No words unless requested and returned.");
        }

        private static async Task ValidationAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiCompatibleClient client = Compatible(server);
            using OpenAiCompatibleClient noModel = new OpenAiCompatibleClient(server.Endpoint, LocalClients.TestKey);

            void Rejects<T>(Func<Task> call, string message) where T : Exception
            {
                try { call(); }
                catch (T) { return; }
                throw new TestFailureException(message + " (expected " + typeof(T).Name + " synchronously)");
            }

            Rejects<ArgumentNullException>(() => client.TranscribeAsync(null!, null, token), "Null request.");
            Rejects<ArgumentException>(() => client.TranscribeAsync(new TranscriptionRequest(), null, token), "Empty audio.");
            Rejects<ArgumentException>(() => client.TranscribeAsync(new TranscriptionRequest { Audio = new byte[] { 1, 2, 3 } }, null, token), "Undetectable media type.");
            Rejects<ArgumentException>(() => client.TranscribeAsync(new TranscriptionRequest { Audio = Flac, MediaType = MediaTypes.ImagePng }, null, token), "Non-audio media type.");
            Rejects<ArgumentException>(() => client.TranscribeAsync(new TranscriptionRequest { Audio = Flac, TimestampGranularities = { TranscriptionTimestampGranularity.Word } }, null, token), "Granularities need VerboseJson.");
            Rejects<InvalidOperationException>(() => noModel.TranscribeAsync(new TranscriptionRequest { Audio = Flac }, null, token), "No model.");
            bool threw = false;
            try { new TranscriptionRequest { Temperature = 1.5 }; } catch (ArgumentOutOfRangeException) { threw = true; }
            SharedAssert.True(threw, "Temperature range.");
            SharedAssert.Equal(0, server.RequestBodies.Count, "Nothing was sent.");

            TranscriptionResponse video = await client.TranscribeAsync(new TranscriptionRequest { Audio = LocalChatModelCases.Mp4 }, null, token).ConfigureAwait(false);
            SharedAssert.True(video.Success, "MP4 containers are accepted: " + video.Error);
        }

        private static async Task ModelFormatsAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiClient openai = new OpenAiClient(LocalOpenAiResponsesRoutes.Key, server.Endpoint);
            SharedAssert.Equal("whisper-1", openai.Defaults.Transcription.Model, "OpenAI default transcription model.");

            TranscriptionResponse whisper = await openai.TranscribeAsync(new TranscriptionRequest { Audio = Flac, Format = TranscriptionFormat.VerboseJson, TimestampGranularities = { TranscriptionTimestampGranularity.Word } }, null, token).ConfigureAwait(false);
            SharedAssert.True(whisper.Success, "whisper-1 accepts verbose_json: " + whisper.Error);

            foreach (string model in new[] { "gpt-4o-transcribe", "gpt-4o-mini-transcribe" })
            {
                TranscriptionOptions options = new TranscriptionOptions { Model = model };
                TranscriptionResponse ok = await openai.TranscribeAsync(new TranscriptionRequest { Audio = Flac }, options, token).ConfigureAwait(false);
                SharedAssert.True(ok.Success, model + " accepts json: " + ok.Error);
                try
                {
                    _ = openai.TranscribeAsync(new TranscriptionRequest { Audio = Flac, Format = TranscriptionFormat.VerboseJson }, options, token);
                    throw new TestFailureException(model + " accepted verbose_json.");
                }
                catch (ArgumentException ex) { SharedAssert.True(ex.Message.Contains("whisper-1", StringComparison.Ordinal), "The error names whisper-1: " + ex.Message); }
                await SharedAssert.ThrowsAsync<ArgumentException>(() => openai.TranscribeAsync(new TranscriptionRequest { Audio = Flac, Format = TranscriptionFormat.Text }, options, token), model + " rejects text.").ConfigureAwait(false);
            }

            TranscriptionOptions diarize = new TranscriptionOptions { Model = "gpt-4o-transcribe-diarize" };
            TranscriptionResponse diarized = await openai.TranscribeAsync(new TranscriptionRequest { Audio = Flac, Format = TranscriptionFormat.Text }, diarize, token).ConfigureAwait(false);
            SharedAssert.True(diarized.Success, "The diarizing model accepts text: " + diarized.Error);
            await SharedAssert.ThrowsAsync<ArgumentException>(() => openai.TranscribeAsync(new TranscriptionRequest { Audio = Flac, Format = TranscriptionFormat.VerboseJson }, diarize, token), "The diarizing model rejects verbose_json.").ConfigureAwait(false);

            using AzureOpenAiClient azure = new AzureOpenAiClient(server.Endpoint, LocalClients.TestKey);
            await SharedAssert.ThrowsAsync<ArgumentException>(() => azure.TranscribeAsync(new TranscriptionRequest { Audio = Flac, Format = TranscriptionFormat.VerboseJson }, new TranscriptionOptions { Model = "gpt-4o-transcribe" }, token), "Azure applies the same rule to a gpt-4o deployment name.").ConfigureAwait(false);

            using OpenAiCompatibleClient compatible = Compatible(server);
            TranscriptionResponse passed = await compatible.TranscribeAsync(new TranscriptionRequest { Audio = Flac, Format = TranscriptionFormat.VerboseJson }, new TranscriptionOptions { Model = "gpt-4o-transcribe" }, token).ConfigureAwait(false);
            SharedAssert.True(passed.Success, "OpenAI-compatible servers decide for themselves: " + passed.Error);
        }

        private static async Task RoutingAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();

            using OpenAiClient openai = new OpenAiClient(LocalOpenAiResponsesRoutes.Key, server.Endpoint);
            await openai.TranscribeAsync(new TranscriptionRequest { Audio = Flac }, null, token).ConfigureAwait(false);
            SharedAssert.Equal("/v1/audio/transcriptions", server.RequestPaths.Last(), "OpenAI route.");
            SharedAssert.Equal("Bearer " + LocalOpenAiResponsesRoutes.Key, server.RequestHeaders.Last()["Authorization"], "OpenAI bearer.");

            using AzureOpenAiClient azure = new AzureOpenAiClient(server.Endpoint, LocalClients.TestKey) { Defaults = { Transcription = { Model = "my-whisper" } } };
            TranscriptionResponse azureResponse = await azure.TranscribeAsync(new TranscriptionRequest { Audio = Flac }, null, token).ConfigureAwait(false);
            SharedAssert.True(azureResponse.Success, azureResponse.Error ?? "azure");
            SharedAssert.Equal("/openai/v1/audio/transcriptions", server.RequestPaths.Last(), "Azure v1 route.");
            SharedAssert.Equal(LocalClients.TestKey, server.RequestHeaders.Last()["api-key"], "Azure api-key header.");
            SharedAssert.Equal("my-whisper", Value(LastFields(server), "model"), "The deployment name is the model.");
            using AzureOpenAiClient noDeployment = new AzureOpenAiClient(server.Endpoint, LocalClients.TestKey);
            await SharedAssert.ThrowsAsync<InvalidOperationException>(() => noDeployment.TranscribeAsync(new TranscriptionRequest { Audio = Flac }, null, token), "Azure has no default deployment.").ConfigureAwait(false);

            using OpenAiCompatibleClient header = Compatible(server, new OpenAiCompatibleClientOptions { AuthPlacement = AuthPlacement.Header("x-api-key") });
            await header.TranscribeAsync(new TranscriptionRequest { Audio = Flac }, null, token).ConfigureAwait(false);
            SharedAssert.Equal(LocalClients.TestKey, server.RequestHeaders.Last()["x-api-key"], "Custom header placement.");

            using OpenAiCompatibleClient query = Compatible(server, new OpenAiCompatibleClientOptions { AuthPlacement = AuthPlacement.Query("key") });
            await query.TranscribeAsync(new TranscriptionRequest { Audio = Flac }, null, token).ConfigureAwait(false);
            SharedAssert.True(server.RequestUrls.Last().Contains("key=" + LocalClients.TestKey, StringComparison.Ordinal), "Query placement: " + server.RequestUrls.Last());
        }

        private static async Task DetailsAndErrorsAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiCompatibleClient client = WireCapture.Full(Compatible(server));
            await client.TranscribeAsync(new TranscriptionRequest { Audio = Flac, FileName = "chunk-0001.flac", Language = "en" }, null, token).ConfigureAwait(false);
            string recorded = client.CallDetails.Last().RequestBody ?? "";
            SharedAssert.True(recorded.Contains("\"file\":\"<binary " + Flac.Length + " bytes audio/flac name=chunk-0001.flac>\"", StringComparison.Ordinal), "File summary: " + recorded);
            SharedAssert.True(recorded.Contains("\"language\":\"en\"", StringComparison.Ordinal), "Text fields are recorded: " + recorded);
            SharedAssert.False(recorded.Contains("fLaC", StringComparison.Ordinal), "No audio bytes are recorded.");

            using OpenAiCompatibleClient metadata = Compatible(server);
            await metadata.TranscribeAsync(new TranscriptionRequest { Audio = Flac }, null, token).ConfigureAwait(false);
            SharedAssert.Equal(null, metadata.CallDetails.Last().RequestBody, "Metadata capture records no body.");

            TranscriptionResponse failed = await client.TranscribeAsync(new TranscriptionRequest { Audio = Flac }, new TranscriptionOptions { Model = "error-model" }, token).ConfigureAwait(false);
            SharedAssert.False(failed.Success, "A 500 is unsuccessful.");
            SharedAssert.Equal(500, failed.StatusCode, "Status code.");
            SharedAssert.True((failed.Error ?? "").Contains("transcription backend failed", StringComparison.Ordinal), "Provider error message: " + failed.Error);
        }

        private static async Task TelemetryAsync(CancellationToken token)
        {
            using TelemetryCapture capture = new TelemetryCapture("tel_transcribe");
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiCompatibleClient client = Compatible(server);
            await client.TranscribeAsync(new TranscriptionRequest { Audio = Flac, Format = TranscriptionFormat.VerboseJson }, null, token).ConfigureAwait(false);
            System.Diagnostics.Activity span = capture.Span("openai transcribe");
            SharedAssert.Equal("transcribe", span.GetTagItem(N.Operation)?.ToString(), "Operation.");
            SharedAssert.Equal(Flac.Length.ToString(), span.GetTagItem(N.TranscriptionAudioBytes)?.ToString(), "Audio bytes.");
            SharedAssert.Equal("2.5", Convert.ToString(span.GetTagItem(N.TranscriptionDurationSeconds), System.Globalization.CultureInfo.InvariantCulture), "Duration.");
            SharedAssert.Equal("2", span.GetTagItem(N.TranscriptionSegments)?.ToString(), "Segments.");
            capture.Require(N.Operations, N.Operation, "transcribe", N.Capability, "transcription", N.Outcome, "success");
        }
    }
}
