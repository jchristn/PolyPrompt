# Migrating from PolyPrompt 4.x to 5.0

5.0 adds images, audio, and video input, transcription, and provider file uploads. To make media fit, it changes two things you use every day:

1. **Tool chat is part of chat.** `ToolChatAsync` and `ToolChatStreamingAsync` are gone. `ChatAsync` and `ChatStreamingAsync` take a prompt, a message, a conversation, or a `ChatRequest` with tools, and every one of them returns the same `ChatResponse` (or `ChatStreamingResponse`) with `ToolCalls`.
2. **`ChatMessage.Content` is a list of parts.** Text is a `ContentPart` like an image is. `message.Text` reads and sets the plain text, so most code changes from `Content` to `Text`.

Requests are unchanged on the wire. Every text-only request body PolyPrompt sends is byte-identical to 4.0.0, with one intended exception listed under [Wire changes](#wire-changes). Conversations you saved as JSON with 4.x still load.

## Type and method mapping

| 4.x | 5.0 |
|---|---|
| `client.ToolChatAsync(request)` | `client.ChatAsync(request)` |
| `client.ToolChatStreamingAsync(request)` | `client.ChatStreamingAsync(request)` |
| `ToolChatRequest` | `ChatRequest` (same members: `Messages`, `Tools`, `ToolChoice`, `Options`) |
| `ToolChatResponse` | `ChatResponse` (now with `ToolCalls` and `ToAssistantMessage()`) |
| `ToolChatStreamingResponse` | `ChatStreamingResponse` (now with `Text`, `ToolCalls`, `ToolCallDeltaCount`, `ToAssistantMessage()`) |
| `ToolChatStreamingChunk` | `ChatStreamingChunk` (now with `ToolCallDeltas`) |
| `Capability.ToolChat` | `Capability.ToolCalling` (same bit; required only when `Tools` is not empty) |
| `message.Content` (string) | `message.Text` (string), or `message.Content` (`List<ContentPart>`) |
| `new ChatMessage { Content = "..." }` | `new ChatMessage { Text = "..." }` |
| Telemetry operations `tool_chat`, `tool_chat_stream` | `chat`, `chat_stream` (`polyprompt.tool.definitions` tells them apart) |
| README matrix column `ToolChat` | `ToolCalling`, plus `ImageInput`, `AudioInput`, `VideoInput`, `Transcribe` |

## Chat and tools

In 4.x:

<!-- no-compile -->
```csharp
ToolChatRequest request = new ToolChatRequest { Messages = { ChatMessage.User("Weather in Paris?") }, Tools = { weather } };
ToolChatResponse turn = await client.ToolChatAsync(request);
```

In 5.0:

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

using OpenAiClient client = new OpenAiClient(Environment.GetEnvironmentVariable("OPENAI_API_KEY")!);
ToolDefinition weather = ToolDefinition.Function("get_weather", "Get the weather.", new Dictionary<string, object> { { "type", "object" } });

ChatRequest request = new ChatRequest { Messages = { ChatMessage.User("Weather in Paris?") }, Tools = { weather } };
ChatResponse turn = await client.ChatAsync(request);
while (turn.Success && turn.ToolCalls.Count > 0)
{
    request.Messages.Add(turn.ToAssistantMessage());
    foreach (ToolCall call in turn.ToolCalls) request.Messages.Add(ChatMessage.ToolResult(call.Id, call.Name, "{\"temperature\":20}"));
    turn = await client.ChatAsync(request);
}
```

A conversation without tools no longer needs a request object:

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

using AnthropicClient claude = new AnthropicClient(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")!);
List<ChatMessage> history = new List<ChatMessage> { ChatMessage.User("Hi"), ChatMessage.Assistant("Hello."), ChatMessage.User("Again, in French.") };

ChatResponse reply = await claude.ChatAsync(history, new CompletionOptions { MaxTokens = 200 });
ChatResponse single = await claude.ChatAsync(ChatMessage.User("One message."));
```

`ChatAsync(string)` still exists and still sends the same body it did in 4.x. Its response now also carries `ToolCalls` (empty), `ResponseId`, and `FinishReason` on every provider, because there is one response type.

`ChatAsync(null)` with a bare `null` literal no longer compiles, because the call is ambiguous between overloads. It threw `ArgumentNullException` in 4.x anyway.

## Message content

`ChatMessage.Content` is `List<ContentPart>`. For text-only code, use `Text`:

| 4.x | 5.0 |
|---|---|
| `string? s = message.Content;` | `string? s = message.Text;` (text parts joined with a newline; null when there are none) |
| `message.Content = "x";` | `message.Text = "x";` (replaces all content with one text part) |
| `message.Content += "!";` | `message.Text += "!";` |
| `string.IsNullOrEmpty(message.Content)` | `string.IsNullOrEmpty(message.Text)` |

The factories keep their names (`ChatMessage.System`, `User`, `Assistant`, `AssistantToolCalls`, `ToolResult`), and `User` gains media overloads:

```csharp
using PolyPrompt.Models;

ChatMessage text = ChatMessage.User("Describe the scene.");
ChatMessage withImage = ChatMessage.User("Describe the scene.", ContentPart.FromFile("frame.png"));
bool hasMedia = withImage.HasMedia;
string? plain = withImage.Text;
```

**Saved conversations.** A 4.x `ChatMessage` written as JSON (`"Content": "text"`) deserializes into one text part. 5.0 writes `"Content": [ ... ]`, which 4.x cannot read, so upgrade readers before writers if several versions share storage. `Text` and `HasMedia` are not serialized.

**Agent harnesses** read `message.Text` from `CliAgentRequest.History`. A history message with media throws `ArgumentException`, because the CLIs take text only.

## Wire changes

| Client | Change | Why |
|---|---|---|
| `OpenAiCompatibleClient` | A message-based chat with no tools no longer sends `"tool_choice": "auto"` | Some servers reject `tool_choice` without `tools`. Chats with tools send exactly what 4.x sent. |

Every other text-only request body (chat, streaming chat, tool chat, generate, and embed, on every provider) is byte-identical to 4.0.0, `Content-Type` included. The test suite checks this against bodies captured from 4.0.0 (`src/Test.Shared/Fixtures/V4Bodies`). Request bodies are now sent as `ByteArrayContent` built straight from UTF-8, with the same `application/json; charset=utf-8` header.

## Custom clients

These changes matter only if you subclass `ProviderClientBase` or `HttpClientBase`:

- `ChatCoreAsync(string prompt, ...)`, `ChatStreamingCoreAsync(string prompt, ...)`, `ToolChatCoreAsync`, and `ToolChatStreamingCoreAsync` are replaced by `ChatCoreAsync(ChatRequest request, List<ChatMessage> messages, ResolvedCompletion settings, CancellationToken token)` and `ChatStreamingCoreAsync` with the same parameters. `messages` already has the default system prompt prepended.
- `WrapToolChatChunksWithTiming` is now `WrapChunksWithTiming`; the old text-only `WrapChunksWithTiming` is gone.
- `PostAndRecordAsync(url, StringContent, string, token)` is now `PostAndRecordAsync(url, byte[] wireBody, string contentType, string? recordedBody, token)`. `PrepareRequestAsync` signs `wireBody`, and `recordedBody` (or the decoded wire body when null) is what call details show. `PostStreamingAsync` takes `(url, byte[] wireBody, string contentType, token)`. `JsonContentType` is the header PolyPrompt uses.
- Override `TranscribeCoreAsync` and set `Capability.Transcribe` to add transcription; give your defaults type a `Transcription` section.

## Telemetry

- Tool chat spans and metrics now use the operations `chat` and `chat_stream`. Filter on `polyprompt.tool.definitions > 0` for calls with tools.
- New: `transcribe` and `file_upload`, `file_get`, `file_delete`, and `file_list` operations; the `transcription` and `file` capability labels; media span attributes; `polyprompt.client.input.media` and `polyprompt.client.input.media_bytes`; and the token types `input_text`, `input_image`, `input_audio`, and `input_video`. See [TELEMETRY.md](TELEMETRY.md).

## Checklist

1. Replace `ToolChatAsync` and `ToolChatStreamingAsync` with `ChatAsync` and `ChatStreamingAsync`.
2. Rename `ToolChatRequest`, `ToolChatResponse`, `ToolChatStreamingResponse`, and `ToolChatStreamingChunk` to their `Chat` names.
3. Replace `Capability.ToolChat` with `Capability.ToolCalling`.
4. Replace `message.Content` with `message.Text` wherever you treat it as a string, including object initializers.
5. Update dashboards and alerts that filter on `tool_chat` or `tool_chat_stream`.
6. If you share stored `ChatMessage` JSON across versions, deploy 5.0 readers before 5.0 writers.
7. If you subclass a client, move to the new core hooks and `PostAndRecordAsync` signature.
8. Optional: add media with `ChatMessage.User(text, parts...)` after checking `Supports(Capability.ImageInput)`; see [MULTIMEDIA.md](MULTIMEDIA.md).
