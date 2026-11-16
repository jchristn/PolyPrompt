# Images, audio, video, and transcription

This is the full reference for media in PolyPrompt 5.0: the content-part model, validation, what each provider receives, size limits, provider file uploads, transcription, and how media appears in call details and telemetry. The [README](README.md#images-audio-and-video) has the short version.

## Content parts

A `ChatMessage` holds a list of `ContentPart`s in `Content`. Each part is one of:

| Source | Factory | Notes |
|---|---|---|
| Text | `ContentPart.FromText(text)` | `ChatMessage.User(text, ...)` puts the text first for you |
| Inline bytes | `ContentPart.Image(bytes, mediaType, detail?)`, `Audio(bytes, mediaType)`, `Video(bytes, mediaType, clip?)` | Requires a media type |
| A file on disk | `ContentPart.FromFile(path)` | Reads the bytes and detects the type from them, falling back to the extension |
| A URL the provider fetches | `ContentPart.ImageUrl(url, detail?)`, `VideoUrl(url, clip?)` | PolyPrompt never downloads URLs |
| A provider-hosted file | `ContentPart.FileReferenceOf(type, reference, mediaType)` | An OpenAI `file_id`, a Gemini File API URI, a Vertex `gs://` URI, or a Bedrock `s3://` URI |

`MediaTypes` has constants for common types (`ImagePng`, `ImageJpeg`, `ImageWebp`, `ImageGif`, `AudioWav`, `AudioMpeg`, `AudioFlac`, `AudioOgg`, `AudioWebm`, `AudioMp4`, `VideoMp4`, `VideoWebm`, `VideoQuicktime`) and `FromFileExtension(path)`.

`ImageDetail` (`Auto`, `Low`, `High`) is sent where the provider has the concept (OpenAI, Azure, xAI, and OpenAI-compatible servers). `VideoClip` (`StartOffsetMs`, `EndOffsetMs`, `FramesPerSecond` from 0.1 to 24) is sent as Gemini and Vertex AI `videoMetadata`.

```csharp
using PolyPrompt.Models;

ChatMessage message = ChatMessage.User("Compare these.",
    ContentPart.FromFile("before.png"),
    ContentPart.Image(File.ReadAllBytes("after.jpg"), MediaTypes.ImageJpeg, ImageDetail.Low),
    ContentPart.FromText("Answer in one sentence."));

string? text = message.Text;     // "Compare these.\nAnswer in one sentence."
bool hasMedia = message.HasMedia; // true
```

Every chat overload accepts media: `ChatAsync(ChatMessage)`, `ChatAsync(IEnumerable<ChatMessage>)`, and `ChatAsync(ChatRequest)` (with or without tools), and their streaming forms. `GenerateAsync` stays text only.

## Validation

All of these are checked before anything is sent:

| Problem | Exception |
|---|---|
| The client lacks `ImageInput`, `AudioInput`, or `VideoInput` for a part | `CapabilityNotSupportedException` (synchronous) |
| Media on a `system`, `assistant`, or tool-result message | `ArgumentException` naming `Messages[i].Content[j]` |
| Zero or several of `Text`, `Data`, `Url`, `FileReference` set | `ArgumentException` |
| `Data` or `FileReference` without `MediaType`; a media type that does not match the part type | `ArgumentException` |
| Bytes whose leading bytes contradict the media type (a PNG declared as JPEG) | `ArgumentException` naming both types |
| A URL that is not absolute `http` or `https` | `ArgumentException` |
| `Detail` on a non-image, `Clip` on a non-video, or a clip that ends before it starts | `ArgumentException` |
| A URL or file reference the provider cannot take (see below) | `ArgumentException` naming the provider |
| Inline media over `MaxInlineMediaBytes` | `ArgumentException` naming the provider's large-media route |

Magic-byte detection covers JPEG, PNG, GIF, WebP, WAV, MP3, FLAC, Ogg, MP4/MOV, and WebM. Shared containers (WebM, Ogg, MP4) match either an audio or a video declaration.

## What each provider receives

| Client | Image | Audio | Video | URLs | File references | Wire shape |
|---|---|---|---|---|---|---|
| `OpenAiClient`, `AzureOpenAiClient` | ✅ | | | Images | `file_id` (images) | Responses `input_text` and `input_image` (`image_url` as a data URL or remote URL, or `file_id`; `detail`) |
| `XAiClient` | ✅ | | | Images | | Responses `input_image` |
| `OpenAiCompatibleClient` | ✅ | ✅ | ✅ | Images and video | | Chat Completions `text`, `image_url` (`url`, `detail`), `input_audio` (`data`, `format`), `video_url` (vLLM). Support depends on the server and model |
| `AnthropicClient` | ✅ | | | Images | | `image` blocks with a `base64` or `url` source |
| `GeminiClient` | ✅ | ✅ | ✅ | YouTube video links | File API URIs | `inlineData`, `fileData` (`fileUri`, `mimeType`), `videoMetadata` |
| `VertexAiClient` | ✅ | ✅ | ✅ | YouTube video links | `gs://` URIs | The same generateContent parts |
| `BedrockClient` | ✅ | | ✅ (model dependent) | | `s3://` URIs | Converse `image` and `video` blocks with `format` and a `bytes` or `s3Location` source |
| `OllamaClient`, `OllamaCloudClient` | ✅ | | | | | `images` on the message (raw base64); the text is `content` |
| `CohereClient` | ✅ | | | Images | | v2 `image_url` (`url` as a data URL or remote URL) |

Text-only messages keep their 4.x shape on every provider (a plain string where the provider used one).

**Per-model caveats.** The flags are per provider. A specific model can still lack a modality: text-only Ollama models, Bedrock models other than Amazon Nova for video, and Cohere models other than Command A Vision. On Ollama, `GetModelInformationAsync` reports `InputModalities` from `/api/show`, and the xAI catalog reports `input_modalities`.

**Formats.** Bedrock accepts images as `png`, `jpeg`, `gif`, or `webp` and video as `mkv`, `mov`, `mp4`, `webm`, `flv`, `mpeg`, `mpg`, `wmv`, or `three_gp`; others throw before sending. OpenAI-compatible `input_audio` sends the format from the media type (`wav`, `mp3`, `flac`, `m4a`, ...). OpenAI's own Chat Completions accepts `wav` and `mp3`.

**OpenAI and Azure audio.** OpenAI documents audio input on Chat Completions, not on the Responses API, so `OpenAiClient` and `AzureOpenAiClient` do not claim `AudioInput`. To send audio to OpenAI's audio models, use `OpenAiCompatibleClient("https://api.openai.com/v1", key)`.

**YouTube.** Gemini and Vertex AI take one public (or owned) YouTube link per request, sent as `fileData` with `video/mp4` unless you set a media type.

## Size limits

`MaxInlineMediaBytes` caps the inline media one request may carry, counted as it is encoded on the wire: base64, plus the data URL prefix where the provider uses one (OpenAI, Azure, xAI, OpenAI-compatible, Cohere). URLs and file references do not count. The default is 20 MB. Set it per client, or through `ClientOptions`:

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

using GeminiClient gemini = new GeminiClient(Environment.GetEnvironmentVariable("GEMINI_API_KEY")!, options: new ClientOptions { MaxInlineMediaBytes = 50L * 1024 * 1024 });
gemini.MaxInlineMediaBytes = 10L * 1024 * 1024;
```

Inline media is copied a few times on the way out (the base64 string and the UTF-8 body). For anything over a few megabytes, prefer a provider file: a Gemini upload, a `gs://` object on Vertex, an `s3://` object on Bedrock, or a URL or `file_id` on OpenAI.

## Provider files

### Gemini File API

Video usually needs an upload. Uploads use the resumable protocol and are processed asynchronously, so wait for `Active`:

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

using GeminiClient gemini = new GeminiClient(Environment.GetEnvironmentVariable("GEMINI_API_KEY")!);

GeminiFileResponse uploaded = await gemini.UploadFileAsync(File.ReadAllBytes("scene.mp4"), MediaTypes.VideoMp4, "scene 12");
if (!uploaded.Success) throw new InvalidOperationException(uploaded.Error);

GeminiFileResponse ready = await gemini.WaitForFileActiveAsync(uploaded.File!.Name, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(2));
if (!ready.Success) throw new InvalidOperationException(ready.Error); // FAILED, timed out, or a failed poll

ChatResponse summary = await gemini.ChatAsync(ChatMessage.User("Describe each shot.",
    ContentPart.FileReferenceOf(ContentPartType.Video, ready.File!.Uri!, ready.File.MediaType!)));

await gemini.DeleteFileAsync(ready.File.Name);
```

`GetFileAsync(name)` reads one file, and `ListFilesAsync(pageSize, pageToken)` returns a page with `NextPageToken`. Names may be given as `files/abc` or `abc`. Files expire about 48 hours after upload (`ExpirationUtc`). Like every operation, these return `Success = false` with `StatusCode` and `Error` on HTTP failures instead of throwing.

### OpenAI files

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

using OpenAiClient openai = new OpenAiClient(Environment.GetEnvironmentVariable("OPENAI_API_KEY")!);

OpenAiFileResponse file = await openai.UploadFileAsync(File.ReadAllBytes("diagram.png"), "diagram.png", "vision");
ChatResponse answer = await openai.ChatAsync(ChatMessage.User("Explain this diagram.",
    ContentPart.FileReferenceOf(ContentPartType.Image, file.File!.Id, MediaTypes.ImagePng)));
FileDeleteResponse deleted = await openai.DeleteFileAsync(file.File.Id);
```

### Vertex AI and Bedrock object storage

Vertex AI has no upload API; put the object in Cloud Storage and send `ContentPart.FileReferenceOf(type, "gs://bucket/object", mediaType)`. Bedrock takes `s3://bucket/key` the same way; set `BedrockClient.S3BucketOwner` when the bucket belongs to another account.

## Transcription

`TranscribeAsync` calls a dedicated speech-to-text endpoint and returns text, and optionally timed segments and words:

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

using OpenAiCompatibleClient whisper = new OpenAiCompatibleClient("http://localhost:8000/v1");
whisper.Defaults.Transcription.Model = "Systran/faster-whisper-large-v3";

TranscriptionResponse transcript = await whisper.TranscribeAsync(new TranscriptionRequest
{
    Audio = File.ReadAllBytes("chunk-0001.flac"),
    Prompt = "Keyframe, PolyPrompt",
    Format = TranscriptionFormat.VerboseJson,
    TimestampGranularities = { TranscriptionTimestampGranularity.Segment, TranscriptionTimestampGranularity.Word },
});

Console.WriteLine(transcript.Language + ", " + transcript.DurationSeconds + " s");
foreach (TranscriptionWord word in transcript.Words) Console.WriteLine(word.StartSeconds + " " + word.Word);
```

| Client | Endpoint | Default model | Formats |
|---|---|---|---|
| `OpenAiClient` | `/v1/audio/transcriptions` | `whisper-1` | `whisper-1`: all. `gpt-4o-transcribe` and `gpt-4o-mini-transcribe`: `Json` only. `gpt-4o-transcribe-diarize`: `Json` and `Text`. Other combinations throw before sending. |
| `AzureOpenAiClient` | `/openai/v1/audio/transcriptions` | none (set your deployment name) | As OpenAI, by deployment name when it names the model |
| `OpenAiCompatibleClient` | `/v1/audio/transcriptions` | none | Whatever the server supports. A server without the route returns its 404 as `Success = false` |

`MediaType` is detected from the bytes when null, and `FileName` defaults to `audio.<extension>`. `TimestampGranularities` requires `VerboseJson`. Segments and words are empty when the server omits them. Gemini and OpenAI audio-input chat models can also transcribe through chat (`ChatAsync(ChatMessage.User("Transcribe this.", ContentPart.Audio(...)))`), but without timestamps; those clients do not claim `Transcribe`.

## Call details, logs, and escaping

**Redaction.** When call details record bodies (`CallDetailCapture.Full`), inline media over `MediaRedactionThresholdBytes` (default 1024 decoded bytes) is recorded as `<base64 184211 bytes image/jpeg sha256=ab12cd34>`. Smaller media is recorded in full. The bytes sent, and the bytes Bedrock signs, are never changed. Turn it off with `RedactMediaInCallDetails = false`. Multipart bodies (transcription and OpenAI uploads) are recorded as a JSON summary of their fields with files shown as `<binary N bytes type name=file>`, and Gemini uploads as `<binary N bytes type>`.

**Escaping.** JSON writers usually write `+` as `+`, which makes base64 about 8% larger. PolyPrompt writes base64 and data URLs unescaped, which is valid JSON because they contain no character JSON requires escaping. Text is escaped as before. Set `EscapeMediaInJson = true` if a proxy insists on escaped JSON; the size check then counts the escaped length.

```csharp
using PolyPrompt.Clients;

ClientOptions options = new ClientOptions { RedactMediaInCallDetails = true, MediaRedactionThresholdBytes = 4096, EscapeMediaInJson = false };
using AnthropicClient claude = new AnthropicClient(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")!, options: options);
```

## Usage and telemetry

`TokenUsage.InputByModality` (`Text`, `Image`, `Audio`, `Video`) is filled where providers report it: Gemini and Vertex AI `promptTokensDetails`, and Chat Completions `prompt_tokens_details.audio_tokens` and `image_tokens`. It is null elsewhere.

Chat spans carry `polyprompt.input.images`, `polyprompt.input.audio`, `polyprompt.input.videos`, `polyprompt.input.media_bytes`, and `polyprompt.input.file_references`. The metrics `polyprompt.client.input.media` (parts) and `polyprompt.client.input.media_bytes` are labeled by `polyprompt.modality`. Transcription spans carry audio bytes, duration, and segment count. See [TELEMETRY.md](TELEMETRY.md).

## Not in this release

Media inside tool results, documents (PDF) as a part type, image or speech output, multimodal embeddings, media in agent harness history, realtime audio, and audio input on the Responses API.
