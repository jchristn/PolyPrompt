# Multimedia Support Plan (PolyPrompt 5.0)

PolyPrompt sends text. Every chat, tool-chat, and generate path ends in a provider wire message whose content is a string (or, for Gemini and Bedrock, a parts list holding only text and tool blocks). This plan adds image, audio, and video input to chat, adds a speech-to-text `Transcribe` capability, and adds provider file uploads where a provider needs them for large media. It touches the library, the tests, the consoles, and every document in the repository.

Because media changes what a message *is*, this release is **5.0.0**, a major version (approved by the owner on 2026-11-15). It uses the break to fix two things that would otherwise stay awkward forever: message content becomes a list of parts, and tool chat merges into chat.

The first consumer is Keyframe (`~/Code/Keyframe`), which needs vision models to summarize and extract metadata from images and video keyframes, and transcription to align speech with video scenes. Nothing in this plan is Keyframe-specific. The design goal is that a PolyPrompt user can attach a photo to a prompt the same way on every provider that supports it, and gets a `CapabilityNotSupportedException` before any request is sent on a provider that does not.

Status markers: `[x]` done, `[~]` in progress, `[ ]` not started.

---

## 1. Principles

1. **The API breaks; the wire does not.** Public types and signatures change (section 2). Wire bodies for text-only requests do not: `ChatAsync(string)` sends exactly what 4.0.0's `ChatAsync(string)` sent, and `ChatAsync(ChatRequest)` with tools sends exactly what 4.0.0's `ToolChatAsync` sent. Bodies captured from 4.0.0 before any code changes are the regression fixtures (section 7.3). Any intended wire difference is reviewed, listed in `MIGRATION_V4_TO_V5.md`, and given its own fixture.
2. **Keep the hierarchy.** New operations follow the existing pattern exactly: a public non-virtual method on `ProviderClientBase` that calls `RequireCapability`, resolves settings, wraps the call in `InstrumentAsync`, and dispatches to a `protected virtual ...CoreAsync` that defaults to `NotSupported`. New partials live beside the existing ones in `src/PolyPrompt/Clients/Base/`. New per-provider code lives in each provider's existing folder and `.Completion.cs` partial, plus a new `.Transcription.cs` or `.Files.cs` partial where a provider gains those features.
3. **Capabilities stay constant flags.** Input modalities become `Capability` flags so `Supports()`, the README matrix, and its contract test keep working. A specific model can still lack vision on a provider that supports it (a small Ollama model, for example), which is the same caveat the README gives for tool calling.
4. **One content model, no polymorphism.** A single sealed `ContentPart` class with a `ContentPartType` discriminator. No abstract base and no derived types, so source-generated JSON and Native AOT need nothing special.
5. **Fail before the wire.** Unsupported modalities, missing media types, unsupported formats, unsupported transcription formats, and parts over a provider's inline size are rejected synchronously with specific exceptions, as unsupported capabilities are today.
6. **Call details stay readable, signatures stay correct.** Base64 media can be megabytes. Recorded request bodies replace media with a placeholder by default, and the recorded body is always a separate value from the bytes that are signed and sent.

---

## 2. Public API

### 2.1 Content parts

New files in `src/PolyPrompt/Models/`:

**`ContentPartType.cs`** (enum): `Text`, `Image`, `Audio`, `Video`.

**`ImageDetail.cs`** (enum): `Auto`, `Low`, `High`. Mapped to OpenAI's `detail`. Ignored by providers without the concept.

**`ContentPart.cs`** (sealed class, plain auto-properties):

| Member | Type | Notes |
|---|---|---|
| `Type` | `ContentPartType` | Discriminator |
| `Text` | `string?` | Set when `Type == Text` |
| `Data` | `byte[]?` | Inline media bytes |
| `Url` | `string?` | Remote `http(s)` URL, passed to providers that fetch URLs (OpenAI, Anthropic, OpenAI-compatible) and YouTube links on Gemini and Vertex video parts. Other providers reject it with `ArgumentException` naming the provider. PolyPrompt never downloads URLs itself |
| `FileReference` | `string?` | Provider-hosted file: an OpenAI `file_id`, a Gemini File API URI, a Vertex `gs://` URI, or a Bedrock `s3://` URI |
| `MediaType` | `string?` | MIME type. Required with `Data` and `FileReference`. Optional with `Url` |
| `Detail` | `ImageDetail?` | Image only |
| `Clip` | `VideoClip?` | Video only: start and end offsets and sampling rate |
| `Name` | `string?` | Optional display or file name. Used as the multipart file name for uploads |

Validation is not done in setters, so object initializers and deserialization are order-independent. Factories validate eagerly, and an internal `Validate(string path)` runs from the resolve step on every part of every message. It enforces exactly one of `Text`, `Data`, `Url`, or `FileReference`, a `Type` consistent with the populated field, `MediaType` where required, `Detail` only on images, and `Clip` only on video. Errors name the path (`Messages[2].Content[1]`).

Static factories:

```csharp
ContentPart.FromText(string text)
ContentPart.Image(byte[] data, string mediaType, ImageDetail? detail = null)
ContentPart.ImageUrl(string url, ImageDetail? detail = null)
ContentPart.Audio(byte[] data, string mediaType)
ContentPart.Video(byte[] data, string mediaType, VideoClip? clip = null)
ContentPart.VideoUrl(string url, VideoClip? clip = null)       // YouTube on Gemini and Vertex
ContentPart.FileReferenceOf(ContentPartType type, string reference, string mediaType)
ContentPart.FromFile(string path)   // reads bytes, infers type and media type
```

**`VideoClip.cs`** (sealed class): `StartOffsetMs` (nullable, at least 0), `EndOffsetMs` (nullable, greater than start), `FramesPerSecond` (nullable, 0.1 to 24). Mapped to Gemini and Vertex `videoMetadata`. Ignored elsewhere, documented in MULTIMEDIA.md.

**`MediaTypes.cs`** (static class): string constants for common types (`image/jpeg`, `image/png`, `image/webp`, `image/gif`, `audio/wav`, `audio/mpeg`, `audio/flac`, `audio/ogg`, `audio/webm`, `audio/mp4`, `video/mp4`, `video/webm`, `video/quicktime`) and `string? FromFileExtension(string path)`.

**`Helpers/MediaTypeSniffer.cs`** (internal static class): magic-byte detection used by `ContentPart.FromFile` and by validation when `MediaType` disagrees with the bytes. It detects JPEG, PNG, GIF, WebP, WAV, MP3, FLAC, OGG, MP4 and MOV (ftyp), and WebM. A mismatch throws `ArgumentException` that names both types.

### 2.2 ChatMessage (breaking)

`ChatMessage.Content` changes from `string?` to a list of parts:

```csharp
public List<ContentPart> Content { get; set; } = new List<ContentPart>();
public string? Text { get; set; } // get: the text parts joined with "\n", null when there are none
                                  // set: replaces all content with one text part (null clears it)
public bool HasMedia { get; }     // true when Content holds a non-text part
```

Factories keep their names and gain media overloads:

```csharp
ChatMessage.System(string text)
ChatMessage.User(string text)
ChatMessage.User(string text, params ContentPart[] media)
ChatMessage.User(IEnumerable<ContentPart> content)
ChatMessage.Assistant(string text)
ChatMessage.AssistantToolCalls(IEnumerable<ToolCall> toolCalls)
ChatMessage.ToolResult(string? toolCallId, string toolName, string text)
```

`Role`, `ToolCallId`, `ToolName`, `ToolCalls`, and `ProviderState` are unchanged. `Text` and `HasMedia` are `[JsonIgnore]`. Because `Text` is settable, most 4.x code migrates by renaming `Content` to `Text` (`new ChatMessage { Role = "tool", Text = "{}" }`).

Rules in this release: media parts are valid only on `user` messages. Media on `system`, `assistant`, or tool-result messages throws `ArgumentException` from validation. The model already permits them, so media in tool results (section 9) needs no further type change.

**Stored history keeps loading.** `ChatMessage.Content` gets a `[JsonConverter]` that reads either a JSON string (4.x shape, becomes one text part) or an array of parts, and always writes an array. The converter uses `PolyPromptJsonContext` type info, so it is AOT-safe. Applications that persisted 4.x conversations deserialize them without migration code.

**Agent harnesses.** `CliAgentClientBase`, `ClaudeCodeClient`, and `MuxClient` read `request.History` and use `message.Text`. A history message with media throws `ArgumentException` (harness attachments are section 9).

### 2.3 Chat and tool chat merge (breaking)

4.0 has two nearly identical surfaces: `ChatAsync` (string in, `ChatResponse` out) and `ToolChatAsync` (`ToolChatRequest` in, `ToolChatResponse` out). The response types differ only by `ToolCalls` and `ToAssistantMessage()`. 5.0 has one.

| 4.0 | 5.0 |
|---|---|
| `ToolChatRequest` | `ChatRequest` (same members: `Messages`, `Tools` default empty, `ToolChoice`, `Options`) |
| `ToolChatResponse` | `ChatResponse` (gains `ToolCalls` and `ToAssistantMessage()`) |
| `ToolChatStreamingResponse` | `ChatStreamingResponse` (gains `Text`, `ToolCalls`, `ToolCallDeltaCount`, `ToAssistantMessage()`) |
| `ToolChatStreamingChunk` | `ChatStreamingChunk` (gains `ToolCallDeltas`) |
| `ToolChatAsync`, `ToolChatStreamingAsync` | removed; use `ChatAsync(ChatRequest)` |
| `Capability.ToolChat` | `Capability.ToolCalling` (required only when `Tools` is non-empty) |
| telemetry operations `tool_chat`, `tool_chat_stream` | `chat`, `chat_stream`; the existing tool definition count attribute distinguishes them |

Public chat methods on `ProviderClientBase` (`.Completion.cs`):

```csharp
Task<ChatResponse> ChatAsync(string prompt, CompletionOptions? options = null, CancellationToken token = default)
Task<ChatResponse> ChatAsync(ChatMessage message, CompletionOptions? options = null, CancellationToken token = default)
Task<ChatResponse> ChatAsync(IEnumerable<ChatMessage> messages, CompletionOptions? options = null, CancellationToken token = default)
Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken token = default)
// ChatStreamingAsync has the same four overloads and returns ChatStreamingResponse
```

Usage:

```csharp
ChatResponse a = await client.ChatAsync("What is the capital of France?");
ChatResponse b = await client.ChatAsync(ChatMessage.User("Describe this image", ContentPart.FromFile("photo.jpg")));
ChatResponse c = await client.ChatAsync(history, new CompletionOptions { MaxTokens = 500 });
ChatResponse d = await client.ChatAsync(new ChatRequest { Messages = history, Tools = tools });
```

All four overloads require `Capability.Chat`, plus `ToolCalling` when tools are present and the modality capability for every media part (`ImageInput`, `AudioInput`, `VideoInput`). They share one resolve step, today's `ResolveToolChat` renamed `ResolveChat`: message validation, part validation, and the `Defaults.Completion.SystemPrompt` prepend.

Protected hooks collapse from four to two:

```csharp
protected virtual Task<ChatResponse> ChatCoreAsync(ChatRequest request, List<ChatMessage> messages, ResolvedCompletion settings, CancellationToken token)
protected virtual Task<ChatStreamingResponse> ChatStreamingCoreAsync(ChatRequest request, List<ChatMessage> messages, ResolvedCompletion settings, CancellationToken token)
```

Each provider keeps one message builder (its 4.0 tool-chat `BuildMessages`), which also learns to emit media parts. The 4.0 string-only builders (`BuildChatBody(prompt, ...)` and similar) are deleted once the 4.0 fixtures prove the message builder produces the same body for a single-prompt request. Where a provider's two 4.0 paths produced different bodies for the same text, the provider keeps the 4.0 shape for that case. The divergence is recorded in the fixture and the code, not silently unified.

`GenerateAsync` (completion style, single prompt) is unchanged and does not gain media.

### 2.4 Capabilities

`src/PolyPrompt/Models/Capability.cs`: `ToolChat` is renamed `ToolCalling` (same bit), and four flags are added:

```csharp
ImageInput = 1 << 9,
AudioInput = 1 << 10,
VideoInput = 1 << 11,
Transcribe = 1 << 12,
```

The README matrix renames the `ToolChat` column and gains `ImageInput`, `AudioInput`, `VideoInput`, and `Transcribe` columns. The column headers must equal the enum names because `LocalContractCases.contract_readme_matrix` parses them with `Enum.Parse<Capability>`. Footnotes cover per-model and per-server caveats: vision on Ollama models, video on Bedrock models, and `Transcribe` on `OpenAiCompatibleClient`, which works only when the server implements `/audio/transcriptions` and otherwise surfaces the server's 404 through the normal error passthrough.

### 2.5 Transcription

New files in `src/PolyPrompt/Models/`:

- **`TranscriptionRequest.cs`**: `byte[] Audio` (required), `string? MediaType` (sniffed if null), `string? FileName`, `string? Model`, `string? Language` (ISO-639-1), `string? Prompt`, `double? Temperature`, `TranscriptionFormat Format` (default `Json`), `List<TranscriptionTimestampGranularity> TimestampGranularities` (default empty).
- **`TranscriptionFormat.cs`** (enum): `Text`, `Json`, `VerboseJson`.
- **`TranscriptionTimestampGranularity.cs`** (enum): `Segment`, `Word`.
- **`TranscriptionResponse.cs`** (`: ResponseBase`): `Text`, `Language`, `DurationSeconds`, `List<TranscriptionSegment> Segments`, `List<TranscriptionWord> Words`, `TokenUsage? Usage`.
- **`TranscriptionSegment.cs`**: `Id`, `StartSeconds`, `EndSeconds`, `Text`, `AverageLogProbability`, `NoSpeechProbability`.
- **`TranscriptionWord.cs`**: `StartSeconds`, `EndSeconds`, `Word`.
- **`TranscriptionOptions.cs`** and **`TranscriptionDefaults.cs`**: `Model` only, matching `EmbeddingOptions` and the embedding defaults section.

`ClientDefaults` gains `public virtual TranscriptionDefaults? Transcription => null;`. Providers that support transcription expose a non-null covariant section, as they do for `Embedding`.

New public method (new partial `ProviderClientBase.Transcription.cs`):

```csharp
Task<TranscriptionResponse> TranscribeAsync(TranscriptionRequest request, CancellationToken token = default)
```

The hook is `protected virtual Task<TranscriptionResponse> TranscribeCoreAsync(TranscriptionRequest request, string model, CancellationToken token)`.

**Format support depends on the model.** On OpenAI, `VerboseJson` and timestamp granularities work only with `whisper-1`; the `gpt-4o-*-transcribe` models return `json` or `text` only (*verify* the current list at implementation). `OpenAiClient` and `AzureOpenAiClient` throw `ArgumentException` before sending when the format or granularities are not supported by the resolved model, with a message that names `whisper-1`. Azure checks the deployment's model name when it is known and otherwise passes the request through. `OpenAiCompatibleClient` always passes the request through because local servers vary. The response parser tolerates missing segments and words in every case. MULTIMEDIA.md has the per-model table.

`Transcribe` means a dedicated transcription endpoint that returns segments. Providers that only transcribe through chat (Gemini, OpenAI audio-input models) do not get the flag. Callers who want that use `ChatAsync(ChatMessage.User("Transcribe this", ContentPart.Audio(...)))`.

### 2.6 Provider file uploads

Large media must be uploaded first on some providers. Gemini needs this for video over the inline limit. Uploads stay provider-specific because there is no common shape. They are public methods on the concrete clients, not on `ProviderClientBase`, which keeps the capability matrix honest.

- **`GeminiClient`** (new partial `GeminiClient.Files.cs`): `UploadFileAsync(byte[] data, string mediaType, string? displayName, CancellationToken)` using the resumable upload protocol. Also `GetFileAsync(string name, ...)`, `WaitForFileActiveAsync(string name, TimeSpan timeout, ...)` (video is processed asynchronously and must reach `ACTIVE`), `DeleteFileAsync`, and `ListFilesAsync`. Returns `GeminiFile` (`Name`, `Uri`, `MediaType`, `SizeBytes`, `State`, `ExpirationUtc`). The caller then uses `ContentPart.FileReferenceOf(ContentPartType.Video, file.Uri, file.MediaType)`.
- **`VertexAiClient`**: no upload API. It takes `gs://` URIs through `FileReference`, documented in PROVIDERS.md.
- **`BedrockClient`**: `s3://` URIs through `FileReference` for images and video on models that accept `s3Location`. Optional `bucketOwner` comes from `BedrockClient.S3BucketOwner`.
- **`OpenAiClient`**: `UploadFileAsync(byte[] data, string fileName, string purpose, ...)` for `purpose = "vision"` (or `"user_data"`), plus `DeleteFileAsync`. It reuses the multipart path built for transcription.

### 2.7 Usage detail by modality

`TokenUsage` gains an optional `ModalityTokens? InputByModality` (`Text`, `Image`, `Audio`, `Video`, all `long?`). It is filled where providers report it: Gemini `promptTokensDetails`, OpenAI `input_tokens_details`, and Bedrock where present. It is null everywhere else.

### 2.8 Model metadata

`ModelInformation` gains `List<ContentPartType>? InputModalities`. It is filled where a provider's model API reports it: Ollama `/api/show` `capabilities` (`vision`), Gemini model listing where available, and the xAI catalog. It is null elsewhere, so callers can check per model when the provider-level flag is not specific enough.

### 2.9 Options

| Option | On | Default | Purpose |
|---|---|---|---|
| `MaxInlineMediaBytes` | `ClientOptions` and `ProviderClientBase` | 20 MB (providers override `DefaultMaxInlineMediaBytes`) | Inline size check (section 3) |
| `RedactMediaInCallDetails` | `ClientOptions` | `true` | Section 4.2 |
| `MediaRedactionThresholdBytes` | `ClientOptions` | `1024` | Section 4.2 |
| `EscapeMediaInJson` | `ClientOptions` | `false` | Section 4.3 |
| `ImageUrlStyle` | `OpenAiCompatibleClientOptions` | `DataUrl` | `RemoteUrlOnly` for servers that reject data URLs |
| `S3BucketOwner` | `BedrockClient` property | null | `bucketOwner` on S3 references (there is no `BedrockCompletionOptions` type, so this is a client setting) |

---

## 3. Provider Support Matrix

The table is the intended capability set. Each cell marked *verify* must be confirmed against the provider's current API documentation at implementation time. If it cannot be confirmed, the flag ships off and the cell moves to section 9.

| Client | ImageInput | AudioInput | VideoInput | Transcribe | Wire mapping |
|---|---|---|---|---|---|
| `OpenAiClient` | Yes | No (unconfirmed on the Responses API; section 9) | No | Yes | Responses `input_image` (`image_url` data URL, remote URL, or `file_id`), `input_audio` if confirmed. Transcription `POST /audio/transcriptions` multipart |
| `AzureOpenAiClient` | Yes | No (as OpenAI) | No | Yes | Same as OpenAI through the Azure v1 surface. Transcription uses a deployment name as the model |
| `XAiClient` | Yes | No | No | No | Responses `input_image` |
| `OpenAiCompatibleClient` | Yes | Yes | Yes (*server dependent*) | Yes (*server dependent*) | Chat Completions content array: `{type:"text"}`, `{type:"image_url", image_url:{url, detail}}`, `{type:"input_audio", input_audio:{data, format}}`, `{type:"video_url", video_url:{url}}` (the vLLM convention). Transcription `POST /audio/transcriptions` serves local Whisper servers (faster-whisper, speaches, whisper.cpp server, vLLM) |
| `AnthropicClient` | Yes | No | No | No | `{type:"image", source:{type:"base64", media_type, data}}` or `source:{type:"url", url}` |
| `GeminiClient` | Yes | Yes | Yes | No | Parts `inlineData {mimeType, data}`, `fileData {mimeType, fileUri}` (File API URIs and YouTube URLs), `videoMetadata {startOffset, endOffset, fps}` |
| `VertexAiClient` | Yes | Yes | Yes | No | Same generateContent schema; `fileData` with `gs://` URIs, and YouTube URLs (confirmed: public or owned videos, one per request) |
| `BedrockClient` | Yes | No | Yes (*model dependent*) | No | Converse `image {format, source:{bytes}}`, `video {format, source:{bytes or s3Location}}`. Format derived from media type (`png`, `jpeg`, `gif`, `webp`; `mp4`, `mov`, `mkv`, `webm` and the rest) |
| `OllamaClient` | Yes | No | No | No | Message `images: [base64, ...]` (raw base64, no data URL prefix). Non-image parts and URLs throw |
| `OllamaCloudClient` | Yes | No | No | No | Inherits Ollama |
| `CohereClient` | Yes (confirmed for Command A Vision) | No | No | No | v2 chat content array with `image_url` (`url` is a data URL or https URL). `detail` is not sent because its placement is not confirmed |
| `VoyageAiClient`, `TeiClient`, `TypeSafeClient` | No | No | No | No | No chat surface |

**Inline size.** Validation sums the **encoded** size of every inline part as it will appear on the wire (base64 length, plus the data URL prefix where used), because providers limit request size, not decoded bytes. Over `MaxInlineMediaBytes`, it throws `ArgumentException` that names the provider and the limit, and recommends the provider's large-media route: `UploadFileAsync` on Gemini, `gs://` on Vertex, `s3://` on Bedrock, a URL or `file_id` on OpenAI.

### Where each provider's code changes

| Provider | Message conversion today | Change |
|---|---|---|
| OpenAI, Azure, xAI | `Clients/Responses/ResponsesApiRequestBuilder.cs : BuildInput(List<ChatMessage>)` sets `["content"] = message.Content ?? ""` | A text-only message keeps the plain string `content` (wire identity). A message with media emits a content list of `input_text`, `input_image`, and `input_audio` items. Dialect flags in `ResponsesApiDialect` say which part types each dialect accepts (xAI: text and image) |
| OpenAI-compatible | `OpenAiCompatibleClient.Completion.cs : BuildMessages` already returns `List<Dictionary<string, object>>`. Only the string-prompt `BuildChatBody` (line 109) uses `Dictionary<string, string>`, and it is deleted by the merge | Text-only messages keep string `content`. Messages with media emit the content array. `ImageUrlStyle` applies here |
| Anthropic | `AnthropicClient.Completion.cs : BuildMessages`, `BuildPromptBody` | A user message with media becomes a block list (`text` and `image` blocks), reusing the existing block-list branch for tool calls |
| Gemini, Vertex | `GenerateContentClientBase.cs : BuildParts(ChatMessage, int, Dictionary<string,string>)` | Append `inlineData`, `fileData`, and `videoMetadata` parts. Gemini already uses a parts list, so this is the smallest change |
| Bedrock | `BedrockClient.Completion.cs : BuildMessages`, `UserMessages(string)` | Append `image` and `video` content blocks. SigV4 signs the wire bytes as before (section 4.1) |
| Ollama | `OllamaClient.Completion.cs : BuildMessages`, `BuildChatBody` | Add `images` to user messages with image parts |
| Cohere | `CohereClient.Completion.cs : BuildMessages` | Content array with `image_url`, only if verified |

Each provider's `Capabilities` constant gains its new flags and the `ToolChat` to `ToolCalling` rename. Each client's XML docs `<seealso>` keeps pointing at `#capability-matrix`.

---

## 4. HTTP, Call Details, and Encoding

### 4.1 One request path: wire bytes and recorded body are separate (breaking, protected)

Today `HttpClientBase.PostAndRecordAsync(url, StringContent content, string requestBodyJson, token)` uses one string as the sent body, the bytes SigV4 signs (`PrepareRequestAsync`), the telemetry body size, and `CallDetail.RequestBody`. Redacting that string would break Bedrock signatures. 5.0 replaces it with:

```csharp
protected Task<HttpCallResult> PostAndRecordAsync(
    string url, byte[] wireBody, string contentType, string? recordedBody, CancellationToken token)
```

- `wireBody` is what is sent, what `PrepareRequestAsync` signs, and what telemetry measures. It goes out as `ByteArrayContent`.
- `recordedBody` goes into `CallDetail.RequestBody` and request logs. When it is null, the recorded body is `wireBody` decoded as UTF-8, which is exactly 4.0 behavior.
- `PostJsonAsync(url, body, token)` serializes straight to UTF-8 bytes from the serializer's buffer (no intermediate string), and produces `recordedBody` only when the body contains media and redaction is active (section 4.2).
- `PostStreamingAsync` gets the same change.

Working from the serializer's UTF-8 buffer drops two full-size copies of the payload (the JSON string and the `Encoding.UTF8.GetBytes` copy for signing). For a 20 MB video that is about 55 MB of large-object-heap allocation removed per request.

Multipart (transcription, OpenAI file upload) uses a sibling:

```csharp
protected Task<HttpCallResult> PostMultipartAndRecordAsync(string url, MultipartFormDataContent content, string recordedBodySummary, CancellationToken token)
```

It runs through `PrepareRequestAsync` like every other call (bearer, api-key, and custom auth placement for OpenAI-compatible servers) and records a JSON summary built by an internal `MultipartSummary` helper. File fields show as `"<binary 1048576 bytes audio/flac name=chunk-0001.flac>"`.

### 4.2 Media redaction in call details

Builders never put a raw base64 string into a wire body dictionary. They put an internal `MediaValue`: the encoded string (raw base64 or a `data:` URL, whichever the provider needs), the media type, the decoded byte count, and a SHA-256 prefix computed lazily. `Serializer.WriteValue` already dispatches on value type (see the `byte[]` case near `Serializer.cs:297`) and gains a `MediaValue` case with two modes:

- **Wire mode** writes the real value (section 4.3).
- **Record mode** writes `"<base64 184211 bytes image/jpeg sha256=ab12cd34>"`.

`PostJsonAsync` serializes in wire mode always, and a second time in record mode only when all of these hold: the body contains a `MediaValue`, `RedactMediaInCallDetails` is true, at least one media value is over `MediaRedactionThresholdBytes`, and `CallDetailCapture` is `Full`. The second pass is cheap because placeholders are tiny. Nothing ever parses or searches serialized JSON, and the sent bytes are never modified. With redaction off, the recorded body is the full wire body.

### 4.3 Media JSON encoding

`PolyPromptJsonContext` sets no `Encoder`, so the writer uses `JavaScriptEncoder.Default`, which writes every `+` as `+`. On base64 that inflates the payload by about 8%.

In wire mode, the `MediaValue` case writes `writer.WriteRawValue("\"" + encoded + "\"", skipInputValidation: true)`. That is safe because the base64 alphabet and the `data:<type>;base64,` prefix contain nothing JSON requires escaping, so the output is valid JSON with no inflation. `WriteRawValue` is available on net8.0 and later and is AOT-safe. MIME types come from `MediaTypes` constants or validated input, and validation rejects a media type containing `"`, `\`, or control characters.

`ClientOptions.EscapeMediaInJson` (default `false`) switches media back to the configured encoder for anyone whose proxy or gateway insists on fully escaped JSON. Text values always use the existing encoder, so text-only bodies stay identical to 4.0.0 (principle 1). The inline size check (section 3) measures the encoded size under whichever mode is active.

### 4.4 Streaming

Media does not change streaming responses. `ChatStreamingCoreAsync` reuses each provider's stream reader. The merged `ChatStreamingResponse` aggregates `Text` and `ToolCalls` the way `ToolChatStreamingResponse` does today.

---

## 5. Telemetry

The changes follow `TELEMETRY.md`'s existing conventions:

- Operation constants `OperationToolChat` and `OperationToolChatStream` are removed. Chat with or without tools reports `chat` or `chat_stream`. The existing tool definition count attribute tells them apart. This is listed in the migration guide for dashboard owners.
- New operation constant `transcribe`. `PolyPromptTelemetry.ResolveCapability` maps it to a new `transcription` capability label.
- New span attributes set in `ProviderClientBase.Completion.cs : Enrich(...)`: `polyprompt.input.images`, `polyprompt.input.audio`, `polyprompt.input.videos`, `polyprompt.input.media_bytes`, `polyprompt.input.file_references`.
- Transcription spans: `polyprompt.transcription.audio_bytes`, `polyprompt.transcription.duration_seconds` (from the response), `polyprompt.transcription.segments`.
- New counter `polyprompt.client.input.media` with low-cardinality labels `provider` and `modality`. New histogram `polyprompt.client.input.media_bytes` labeled by `modality`.
- Token usage by modality is added to `polyprompt.client.tokens` with a `modality` label only when the provider reports it, which keeps label cardinality bounded (four values).
- HTTP body size attributes measure `wireBody`, never the redacted recording.

---

## 6. Serialization and AOT

- Register `ContentPart`, `List<ContentPart>`, `ContentPartType`, `ImageDetail`, `VideoClip`, `ChatRequest`, `TranscriptionRequest`, `TranscriptionResponse`, `TranscriptionSegment`, `List<TranscriptionSegment>`, `TranscriptionWord`, `List<TranscriptionWord>`, `TranscriptionFormat`, `TranscriptionTimestampGranularity`, `ModalityTokens`, `GeminiFile`, and `List<ContentPartType>` in `PolyPromptJsonContext`. Remove `ToolChatRequest`, `ToolChatResponse`, `ToolChatStreamingResponse`, and `ToolChatStreamingChunk`.
- The `ChatMessage.Content` converter (section 2.2) reads string or array and writes array, using context type info only.
- Wire bodies stay `Dictionary<string, object>` and lists written by `Helpers/Serializer.cs`. Media goes through `MediaValue` (sections 4.2 and 4.3). Builders never rely on `byte[]` serialization in wire bodies, because providers need different encodings (raw base64 for Ollama, Gemini, Anthropic, and Bedrock; `data:` URLs for OpenAI and OpenAI-compatible).
- Responses keep being parsed with `JsonDocument` and `JsonElement`.
- `Test.Aot` digests gain multimodal request cases and a recorded-body (redacted) digest. JIT and native must produce identical bytes.
- AOT.md's list of covered types and its version section are updated.

---

## 7. Tests

All tests are Touchstone descriptors in `src/Test.Shared`, so every runner (`Test.Automated`, `Test.Xunit`, `Test.Nunit`, `Test.Aot`) picks them up.

### 7.1 Fixture server

`LocalOpenAiTestServer` and its route files gain:

- **Request DTOs** (not needed in the end: the server parses bodies loosely, and the media cases assert on the recorded raw bodies with `JsonDocument`). The original idea was DTOs in `LocalRequestParser` that accept both shapes. `LocalOpenAiChatMessage.Content` becomes a custom-parsed value that is either a string or a list of `LocalOpenAiContentPart`. `LocalGeminiPart` gains `InlineData`, `FileData`, and `VideoMetadata`. `LocalAnthropicContentBlock` gains image blocks. The Ollama chat DTO gains `Images`. The Bedrock DTO gains image and video blocks. The Responses DTO gains `input_image` and `input_audio`.
- **Transcription route** `POST /v1/audio/transcriptions` that parses multipart form data (a minimal boundary parser in the test project), records the fields, and returns `json`, `text`, or `verbose_json` with segments and words depending on `response_format`.
- **Gemini file routes**: resumable upload start (`X-Goog-Upload-Protocol: resumable`), upload and finalize, `files/{name}` GET returning `PROCESSING` and then `ACTIVE`, DELETE, and list.
- **OpenAI files route**: `POST /v1/files` multipart and `DELETE /v1/files/{id}`.

### 7.2 New case files

- **`LocalMultimodalCases.cs`**: for every client in `LocalClients.Supporting(Capability.ImageInput, ...)`, send one image by bytes and assert the exact wire shape: data URL vs raw base64, media type, part order with the leading text, and no `+` in media values. Repeat for URL images where supported, YouTube URLs on Gemini, audio and video where supported, multiple images in one message, `ImageDetail` mapping, `VideoClip` mapping (Gemini and Vertex), and a tool-bearing `ChatRequest` with an image in the user turn. One case sets `EscapeMediaInJson = true` and asserts escaped output that decodes to the same bytes.
- **`LocalMultimodalValidationCases.cs`**: media on system, assistant, or tool-result messages throws. Missing `MediaType` with `Data` throws. A `MediaType` that disagrees with the magic bytes throws. Two populated sources on one part throw, with the part path in the message. An over-limit inline size throws with the provider-specific message. Non-YouTube URLs on Gemini, and any URL on Ollama, throw. An unsupported modality throws `CapabilityNotSupportedException` with zero server requests recorded.
- **`LocalChatModelCases.cs`** (ChatMessage serialization and the chat overloads, merged into one file): a 4.x-shaped `ChatMessage` JSON (string `Content`) deserializes to one text part. Arrays round-trip. `Text` and `HasMedia` are computed correctly. Covered in `Test.Aot`.
- **`LocalTranscriptionCases.cs`**: multipart field names and values for each format and granularity, segment and word parsing, `Language` and `Prompt` passthrough, `AuthPlacement` variants on `OpenAiCompatibleClient`, Azure deployment routing, the `whisper-1`-only format validation on `OpenAiClient`, pass-through on `OpenAiCompatibleClient`, and error status passthrough.
- **`LocalFileUploadCases.cs`**: the Gemini resumable upload sequence, wait-for-active polling with timeout and cancellation, OpenAI file upload and delete, and file references flowing into parts.
- **`LocalCallDetailRedactionCases.cs`**: redacted placeholders in `CallDetail.RequestBody` for media over the threshold, full bodies when redaction is off, no second serialization under `Metadata` capture, multipart summaries, Bedrock signatures valid with redaction on (the fixture server verifies the signature over the received bytes), and proof that the sent bytes are never altered.
- **`LocalChatModelCases.cs`**, continued: all four `ChatAsync` overloads and their streaming variants on every chat provider, `ToolCalls` on `ChatResponse`, `ToAssistantMessage()` round trips across tool turns (replacing the 4.0 tool-turn tests), and `ToolCalling` required only when tools are present.
- **`LocalMultimodalTelemetryCases.cs`**: new span attributes and metrics, `chat` reported for tool-bearing calls, and the `modality` label limited to its four values.

### 7.3 Contract and regression updates

- **4.0 wire fixtures (first task of the release).** Before any code change, capture request bodies from the 4.0.0 code for every provider: `ChatAsync(string)`, `ChatStreamingAsync(string)`, `ToolChatAsync` and its streaming form with and without tools, and multi-turn tool histories. Store them in `src/Test.Shared/Fixtures/V4Bodies/`. A new `LocalWireIdentityCases.cs` replays the same inputs through the 5.0 API and compares bytes, path, and `Content-Type`. An accepted difference lives in `src/Test.Shared/Fixtures/V5Changes/` under the same name, starts with a `# reason:` line, must differ from its 4.0.0 fixture, and gets a line in the migration guide.
- **README matrix**: the renamed and new columns. `contract_readme_matrix` covers them once the columns exist.
- **Default models table**: add a `Transcription` section. Proposed defaults are `whisper-1` for `OpenAiClient` and none for Azure and OpenAI-compatible. The contract test's section list gains `Transcription`.
- **`LocalArchitectureCases.arch_capability_matrix`**: update the `operations[]` list. Remove the tool-chat methods, and add the new `ChatAsync` and `ChatStreamingAsync` overloads and `TranscribeAsync`.
- **`LocalV4Cases`**: renamed `LocalCapabilityCases`. `unsupported_capabilities_throw` gains rows for `Transcribe`, for a media part per modality on every client lacking it, and for tools on a client lacking `ToolCalling`. Update the expected checked-call count.
- Every existing case that uses `ToolChatAsync`, `ToolChatRequest`, `ToolChatResponse`, or `ChatMessage.Content` as a string is migrated. This is a mechanical sweep, about 45 files across src.

### 7.4 Live tests

`ProviderLiveSuite` gains multimodal and transcription cases gated by the existing `POLYPROMPT_TEST_*` configuration, plus `POLYPROMPT_TEST_IMAGE_MODEL`, `POLYPROMPT_TEST_TRANSCRIPTION_MODEL`, and so on. They use small committed fixtures: a 64x64 PNG with a known shape and color, a two-second spoken WAV, and a two-second MP4. Fixtures stay under 100 KB total and live in `src/Test.Shared/Fixtures/Media/`.

### 7.5 AOT, docs, and consoles

- **`Test.Aot`**: link the new case files and fixtures, and add multimodal, redacted-recording, and transcription digests to `verify-aot.sh` comparisons.
- **`Test.Docs`**: every `csharp` block in README, PROVIDERS, MULTIMEDIA, STREAMING, MIGRATION_V4_TO_V5, and DEVELOPMENT must compile. README keeps more than 10 snippets.
- **`Test.Console`**: scripted runs for the new console commands below.

---

## 8. Consoles, Postman, and Documentation

### 8.1 Consoles

`Console.Shared` moves to the merged chat API and gains two commands, available in every provider console whose client supports them:

- `/attach <path>` queues a media part, which is sent with the next prompt and shown as a chip in the prompt line.
- `/transcribe <path> [language]` calls `TranscribeAsync` and prints segments with timestamps.

`/attach` on an unsupported provider prints the capability message without sending anything.

### 8.2 Postman

The `postman/` directory holds OpenAPI specs, mocks, and simulations for three targets only: `openai-responses`, `xai`, and `ollamacloud`. This release updates those three:

- `openai-responses`: an `input_image` request example in the spec, mock, and simulation, and a new `/v1/audio/transcriptions` route (multipart) with `json` and `verbose_json` responses.
- `xai`: an `input_image` example.
- `ollamacloud`: an `images` chat example.

Mocks and simulations for other providers are not part of this release.

### 8.3 Documentation

| File | Change |
|---|---|
| `MIGRATION_V4_TO_V5.md` (new) | In the style of `MIGRATION_V3_TO_V4.md`: a type and method mapping table (section 2.3), `ChatMessage.Content` to parts with `Text` for reads, stored history compatibility, `Capability.ToolChat` to `ToolCalling`, telemetry operation names, the protected `HttpClientBase` change for custom subclasses, and any accepted wire differences from section 7.3 |
| `README.md` | Capability matrix columns and footnotes. The chat section rewritten around the four `ChatAsync` overloads, with tools as part of chat. A new **Images, audio, and video** section with one compiling example per input style (bytes, URL, file reference) and the `Supports` check. A **Transcription** section. Capability-to-methods table. **For coding agents** bullets for parts, modality capabilities, `ChatRequest`, `TranscribeAsync`, and file uploads. Default models table `Transcription` rows. A link to the migration guide |
| `MULTIMEDIA.md` (new) | The full reference: the content part model, validation rules, per-provider wire mapping, formats and size limits with links to provider docs, URL versus inline versus file reference trade-offs, YouTube on Gemini and Vertex, the Gemini upload lifecycle, Vertex and Bedrock object-storage URIs, memory guidance (prefer uploads or storage URIs above a few MB), call detail redaction, `EscapeMediaInJson`, usage by modality, the transcription format table, and per-model caveats |
| `PROVIDERS.md` | A media and transcription paragraph per provider. New options (section 2.9). The `GeminiClient` and `OpenAiClient` file methods |
| `STREAMING.md` | The merged `ChatStreamingResponse` and chunk, and a note that media does not change stream handling |
| `TELEMETRY.md` | Removed tool-chat operations, new attributes, operation names, metrics, and the `modality` label |
| `AOT.md` | Registered and removed types, the `Content` converter, and the multimodal digest coverage |
| `DEVELOPMENT.md` | A new **Adding a capability** section (missing today): enum flag, README column, `ProviderClientBase` partial and core hook, `LocalArchitectureCases` operations list, `LocalCapabilityCases` rows, `LocalClients` entry, fixture routes, docs. Plus the 4.0 wire fixtures, media fixtures, and the new live-test variables |
| `HARNESSES.md` | History messages with media throw in this release (section 9) |
| `llms.txt` | Links to MULTIMEDIA.md, the migration guide, and the new README sections, plus the key rules (parts only on user messages, modality capabilities, one `ChatAsync` for tools) |
| `CHANGELOG.md` | A new top entry `## v5.0.0 (date)` with a summary paragraph, a `### Breaking` section that points to the migration guide, and `### Added` bullets in the "**Bold title.** ..." form |

---

## 9. Out of Scope, Follow-Up Candidates

- Media inside tool results (Anthropic image blocks in `tool_result`, Gemini function responses with inline data). The 5.0 content model already allows it; only validation and the builders need to change.
- Documents (PDF) as a part type. Anthropic, Gemini, OpenAI, and Bedrock all support them, so this is a natural `ContentPartType.Document` in a 5.x minor release.
- Media outputs: image generation, text-to-speech, and video generation.
- Multimodal embeddings (Voyage multimodal, Cohere embed v4 images, Vertex multimodal embeddings) as an `EmbedMultimodal` capability.
- Agent harness attachments. `CliAgentRequest` has no attachment field. A later release could write attachments into the working directory and reference their paths in the prompt for harnesses whose CLIs can read images.
- Realtime and streaming audio input (OpenAI Realtime, Gemini Live).
- Audio input on `OpenAiClient` and `AzureOpenAiClient`. OpenAI's audio guide routes audio input through Chat Completions, and `input_audio` on the Responses API could not be confirmed in November 2026. `OpenAiCompatibleClient` pointed at OpenAI's Chat Completions accepts audio today.
- Mocks and simulations for providers beyond the three in `postman/`.
- Any provider cell marked *verify* in sections 2.5 and 3 that could not be confirmed.

---

## 10. Work Plan

Each phase ends with `dotnet build src/PolyPrompt.sln -c Release` free of warnings and the local suite green on net8.0 and net10.0.

### Phase 0: Baseline
- [x] Capture the 4.0.0 wire fixtures (section 7.3) from the unmodified tree, and commit them before any library change

### Phase 1: Breaking model and surface changes (no media on the wire yet)
- [x] `ContentPartType`, `ImageDetail`, `VideoClip`, `ContentPart` (factories and `Validate`), `MediaTypes`, `MediaTypeSniffer`
- [x] `ChatMessage.Content` as parts, `Text`, `HasMedia`, factories, the string-or-array converter
- [x] Chat and tool chat merge: `ChatRequest`, merged responses and chunks, the four `ChatAsync` and `ChatStreamingAsync` overloads, `ChatCoreAsync` and `ChatStreamingCoreAsync`, the `ToolCalling` rename, and the per-provider move onto one message builder
- [x] Agent harnesses read `Text` and reject media history
- [x] `HttpClientBase.PostAndRecordAsync(wireBody, recordedBody)`, UTF-8 serialization without the intermediate string, and the `PostStreamingAsync` equivalent
- [x] Telemetry operation merge
- [x] Migrate every existing test, console, and doc snippet to the new API
- [x] Tests: `LocalWireIdentityCases` green against Phase 0 fixtures; `LocalChatModelCases` (overloads, same-body, content, JSON, part validation, `FromFile`, harness history)

### Phase 2: Media on the wire
- [x] `MediaValue`, the serializer wire and record modes, raw media writing, `EscapeMediaInJson`
- [x] Call detail redaction and its options
- [x] Capability flags, `ModalityTokens`, `ModelInformation.InputModalities`, `PolyPromptJsonContext` registrations
- [x] Responses API builder (OpenAI, Azure, xAI) with dialect part flags
- [x] OpenAI-compatible content arrays, `ImageUrlStyle`
- [x] Anthropic image blocks
- [x] Gemini and Vertex `inlineData`, `fileData` (including YouTube), `videoMetadata`
- [x] Bedrock image and video blocks
- [x] Ollama `images`
- [x] Cohere (only if verified)
- [x] `Capabilities` constants, `MaxInlineMediaBytes` with encoded-size checks
- [x] Tests: `LocalMultimodalCases` (13 cases: shapes per provider, URLs, detail, escaping, tools, audio and video, Gemini and Bedrock specifics, streaming, validation, inline limit, redaction, usage and modalities) and capability contract rows

### Phase 3: Transcription
- [ ] Multipart helper and summary recording
- [ ] Transcription models, defaults section, `TranscribeAsync`, telemetry operation
- [ ] OpenAI, Azure, and OpenAI-compatible implementations (new `.Transcription.cs` partials) with model format validation
- [ ] Tests: `LocalTranscriptionCases`, multipart redaction cases, architecture operations list, default-models section

### Phase 4: Files
- [ ] `GeminiClient.Files.cs` with the resumable upload, polling, and deletion
- [ ] `OpenAiClient` file upload and delete
- [ ] Tests: `LocalFileUploadCases`

### Phase 5: Telemetry, AOT, consoles
- [ ] Span attributes, metrics, modality token labels
- [ ] `Test.Aot` linking and digests, `verify-aot.sh`
- [ ] Console commands and `Test.Console` scripts
- [ ] Tests: `LocalMultimodalTelemetryCases`

### Phase 6: Documentation and release preparation
- [ ] MIGRATION_V4_TO_V5, README, MULTIMEDIA, PROVIDERS, STREAMING, TELEMETRY, AOT, DEVELOPMENT, HARNESSES, llms.txt
- [ ] Postman updates for the three existing targets
- [ ] Live tests against at least one provider per modality (OpenAI or Anthropic for images, Gemini for video and audio, an OpenAI-compatible Whisper server and OpenAI `whisper-1` for transcription)
- [ ] CHANGELOG `v5.0.0` entry
- [ ] Full test matrix (Automated, Xunit, Nunit, Aot on all three OSes, Docs, Console)

---

## 11. Progress Log

- **2026-11-15**: First draft written from a structural review of the 4.0.0 source, as an additive 4.1.0. Key finding: only `ToolChatAsync` takes `ChatMessage`, and `ChatAsync` and `GenerateAsync` take a string.
- **2026-11-15**: Revised after review. The owner approved a major version, so the plan became 5.0.0. Changes: `ChatMessage.Content` becomes parts, with a converter for 4.x JSON. Tool chat merges into chat, with four `ChatAsync` overloads; `ChatAsync(string, IEnumerable<ContentPart>)` was dropped because it made `ChatAsync(x, null)` ambiguous. The wire body and recorded body are separated so redaction cannot break SigV4. Media is written raw to avoid `+` inflation, with `EscapeMediaInJson` to opt out. Validation moved out of setters. Transcription format checks are per model. YouTube URLs are allowed on Gemini and Vertex. Postman scope is limited to the three existing targets. Serialization writes UTF-8 bytes without an intermediate string. Redaction moved to the media phase. Phase 0 captures the 4.0 wire fixtures. No code changes yet.
- **2026-11-15**: Phase 0 done (`b159ef3`): 160 wire fixtures captured from 4.0.0, deterministic across two runs and both frameworks.
- **2026-11-16**: Phase 1 done. Chat and tool chat merged; `ChatMessage.Content` is parts. Wire identity: 156 of 160 bodies are byte-identical, including `Content-Type`; the 4 differences are the intended OpenAI-compatible fix (no `tool_choice` without tools), recorded in `V5Changes`. The Native AOT request digest over 222 requests equals the 4.0.0 digest. Local suite 854 passed on net8.0 and net10.0; Xunit, Nunit, Docs, Console, and AOT green. `MIGRATION_V3_TO_V4.md` is no longer compiled by `Test.Docs` because its snippets target 4.0; `MIGRATION_V4_TO_V5.md` replaces it in Phase 6.
- **2026-11-16**: Phase 2 done. Checked the *verify* cells against provider docs: OpenAI and Azure audio input ship off; Cohere vision and Vertex YouTube are confirmed; only `whisper-1` has `verbose_json` and timestamps. Media is written raw through `MediaValue`; recorded bodies use placeholders. Ten local clients send images (plus Ollama Cloud), with audio and video on OpenAI-compatible, Gemini, Vertex, and Bedrock (video). Local suite 867 passed on net8.0 and net10.0; Docs, Console, and AOT green; the AOT request digest is still the 4.0.0 digest. Deviations: `S3BucketOwner` is a `BedrockClient` property; `MaxInlineMediaBytes` lives on `ClientOptions`; no fixture-server media DTOs.
