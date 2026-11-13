# Provider Reference

Details for each provider client. Start with the [README](README.md); come here when you need the specifics.

- [Constructors](#constructors)
- [Provider notes](#provider-notes)
- [Authentication](#authentication)
- [Responses API (OpenAI, Azure OpenAI, xAI)](#responses-api-openai-azure-openai-xai)
- [Gemini tool calling](#gemini-tool-calling)
- [Reasoning effort](#reasoning-effort)
- [Reasoning output and continuation state](#reasoning-output-and-continuation-state)
- [Citations](#citations)
- [Raw responses and token usage](#raw-responses-and-token-usage)
- [Call details, telemetry, cancellation](#call-details-telemetry-cancellation)
- [Options classes](#options-classes)
- [Common members](#common-members)

## Constructors

Every constructor takes an optional `ClientOptions` last.

```
OpenAiClient, XAiClient, AnthropicClient, GeminiClient,
OllamaCloudClient, CohereClient, VoyageAiClient, TypeSafeClient
    (string apiKey, string? endpoint = null, ClientOptions? options = null)

OpenAiCompatibleClient, TeiClient
    (string endpoint, string? apiKey = null, ClientOptions? options = null)

OllamaClient
    (string endpoint = "http://localhost:11434", string? apiKey = null, ClientOptions? options = null)

AzureOpenAiClient
    (string endpoint, string apiKey, ClientOptions? options = null)
    (string endpoint, ICredentialProvider credential, ClientOptions? options = null)

VertexAiClient
    (string project, string region, ICredentialProvider credential, string? endpoint = null, ClientOptions? options = null)

BedrockClient
    (string region, IAwsCredentialProvider credential, string? endpoint = null, ClientOptions? options = null)

Harness clients
    (string executable = <default>, ClientOptions? options = null)
```

A required key that is null, blank, contains whitespace, or looks like a URL throws `ArgumentException`. So does an endpoint that is not an absolute http(s) URL. Endpoints are normalized per provider: OpenAI-family endpoints get `/v1` appended unless present, Ollama strips a trailing `/v1`, Azure appends `/openai/v1`, and Bedrock and Vertex derive regional hosts.

`OpenAiCompatibleClientOptions` adds `AuthPlacement`, for servers that want the key somewhere other than a bearer header:

```csharp
using PolyPrompt.Clients;

// Azure's v1 Chat Completions route with the key in an api-key header.
using OpenAiCompatibleClient azureChat = new OpenAiCompatibleClient(
    "https://my-resource.openai.azure.com/openai/v1", "azure-key",
    new OpenAiCompatibleClientOptions { AuthPlacement = AuthPlacement.Header("api-key") });
azureChat.Defaults.Completion.Model = "my-deployment";
```

## Provider notes

**`OpenAiClient`**: bearer key. Responses API; also `CancelResponseAsync` and `CountInputTokensAsync`. If it gets a 404 or 405 from `/responses`, the `Error` suggests `OpenAiCompatibleClient`.

**`OpenAiCompatibleClient`**: optional key; `AuthPlacement` can be bearer, header, or query. Reads the `reasoning_content` and `reasoning` extensions, replays Gemini thought signatures in `extra_content.google.thought_signature`, and generates with legacy `/v1/completions`.

**`AzureOpenAiClient`**: `api-key` or Entra ID. v1 API with no `api-version`; deployment names are models.

**`XAiClient`**: bearer key. Responses API; also `ListLanguageModelsAsync`, `ListImageGenerationModelsAsync`, `ListVideoGenerationModelsAsync`, and `GetCatalogModelInformationAsync`. At most 1000 tools; reasoning effort `low` to `xhigh`. xAI no longer offers embedding models.

**`AnthropicClient`**: `x-api-key`. `AnthropicVersion`, `WorkspaceId` (identity-linked keys), `PageSize`. `max_tokens` is always sent. Citations on documents cannot be combined with `output_config.format`.

**`GeminiClient`**: `x-goog-api-key`. See [Gemini tool calling](#gemini-tool-calling).

**`VertexAiClient`**: OAuth from an `ICredentialProvider` (ADC, service account, static token). Same generateContent schema as Gemini; embeddings through `:predict`; connectivity uses `:countTokens`.

**`BedrockClient`**: SigV4 from an `IAwsCredentialProvider`. Converse API for completion, InvokeModel for embeddings (Titan, Cohere) and rerank, control-plane model listing. No default completion model, because IDs and inference profiles vary by region and account.

**`OllamaClient`**: no auth, or a bearer token behind a proxy. `PullModelAsync`, `DeleteModelAsync`, `LoadModelAsync`, `UnloadModelAsync`, `PullTimeout`; `KeepAlive` on completion and embedding options.

**`OllamaCloudClient`**: bearer key. Same operations as `OllamaClient` at `https://ollama.com`. No default models, because the hosted catalog changes, and the catalog may not include embedding models.

**`CohereClient`**: bearer key. v2 chat, embed, and rerank; v1 classify; `PageSize` for model listing. Tool choice accepts only `REQUIRED` and `NONE`. Cohere recommends classifying with Embed models, but `/v1/classify` is still available.

**`VoyageAiClient`**: bearer key. Embed (`InputType`, `OutputDimension`, `OutputDtype`) and rerank; connectivity embeds a probe word.

**`TeiClient`**: no auth, or a bearer token. One model per server: embed, sparse embed (SPLADE), rerank (cross-encoders), classify, `/info`. Which operations succeed depends on the hosted model's type.

**`TypeSafeClient`**: bearer key (a local Ollaya server accepts any value). `POST /v1/systemone`; binary questions are TypeSafe `noul` questions; `MaxConcurrency` for batches.

## Authentication

`PolyPrompt.Auth` implements the richer credentials without any SDK:

- **Vertex AI** takes an `ICredentialProvider`: `AdcCredential` (Application Default Credentials), `ServiceAccountCredential.FromJson(...)`, or `StaticTokenCredential`. Tokens are cached and refreshed before they expire.
- **Bedrock** signs every request with Signature Version 4, from `StaticAwsCredential(accessKey, secretKey, region, sessionToken?)` or `EnvironmentAwsCredential()`.
- **Azure** accepts an API key string, or an `ICredentialProvider` for Entra ID.

## Responses API (OpenAI, Azure OpenAI, xAI)

The Responses API replaces Chat Completions at OpenAI. Each response is a list of output items (messages, reasoning, function calls, hosted tool calls). PolyPrompt projects them onto the generic results (`Text`, `Reasoning`, `ToolCalls`, `Usage`, `FinishReason`, `Citations`) and keeps the full JSON in `RawResponse`.

### Encrypted reasoning across turns

Every request includes `reasoning.encrypted_content`, and each response's `ProviderState` holds its output items. `ToAssistantMessage()` keeps that state and the next tool-chat call replays it, so the model does not re-think at every step of a tool loop, even with `Store = false`.

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

using OpenAiClient openai = new OpenAiClient(Environment.GetEnvironmentVariable("OPENAI_API_KEY")!);

ToolChatRequest request = new ToolChatRequest
{
    Messages = { ChatMessage.User("What is the weather in Seattle?") },
    Tools = { ToolDefinition.Function("get_weather", "Get the weather for a city.", new Dictionary<string, object>
        {
            { "type", "object" },
            { "properties", new Dictionary<string, object> { { "city", new Dictionary<string, object> { { "type", "string" } } } } },
            { "required", new List<string> { "city" } }
        }) },
    Options = new CompletionOptions { ReasoningEffort = ReasoningEffort.High }
};

ToolChatResponse turn = await openai.ToolChatAsync(request);
while (turn.Success && turn.ToolCalls.Count > 0)
{
    request.Messages.Add(turn.ToAssistantMessage());   // keeps encrypted reasoning in ProviderState
    foreach (ToolCall call in turn.ToolCalls)
        request.Messages.Add(ChatMessage.ToolResult(call.Id, call.Name, "{\"temperature\":61}"));
    turn = await openai.ToolChatAsync(request);
}

Console.WriteLine(turn.Text + " (effort sent: " + turn.AppliedReasoningEffort + ")");
```

### Stored responses

OpenAI's API (and its official SDKs) default `store` to true, keeping responses for at least 30 days. PolyPrompt always sends `store` explicitly, and sends `false` unless you set `Store = true`. Stored responses can be retrieved, continued by id, listed, cancelled (background), and deleted.

```csharp
using System.Text.Json;
using PolyPrompt.Clients;
using PolyPrompt.Models;
using PolyPrompt.Options;

using OpenAiClient openai = new OpenAiClient(Environment.GetEnvironmentVariable("OPENAI_API_KEY")!);
JsonElement input = JsonDocument.Parse("\"Draft a release note for 4.0.\"").RootElement;

ResponsesApiResponse first = await openai.CreateResponseAsync(input, new OpenAiCompletionOptions { Store = true });
JsonElement more = JsonDocument.Parse("\"Make it shorter.\"").RootElement;
ResponsesApiResponse second = await openai.ContinueResponseAsync(first, more);   // throws before sending if first was not stored

ResponsesApiResponse fetched = await openai.GetResponseAsync(first.ResponseId!);
ResponsesApiJsonResponse items = await openai.ListResponseInputItemsAsync(first.ResponseId!, limit: 20);
ResponsesApiJsonResponse tokens = await openai.CountInputTokensAsync(more);       // OpenAI only
ResponsesApiJsonResponse compacted = await openai.CompactConversationAsync(input);
await openai.DeleteResponseAsync(first.ResponseId!);

// Background responses require Store = true; poll with GetResponseAsync and stop with CancelResponseAsync.
ResponsesApiResponse queued = await openai.CreateResponseAsync(input, new OpenAiCompletionOptions { Store = true, Background = true });
await openai.CancelResponseAsync(queued.ResponseId!);
```

### Native events

`CreateResponseStreamingAsync` streams every server-sent event with its raw `Type` and a mapped `Kind` (`TextDelta`, `ReasoningSummaryDelta`, `FunctionCallArgumentsDelta`, `AnnotationAdded`, `HostedToolProgress`, `Completed`, ...). Unknown events map to `Other`.

```csharp
using System.Text.Json;
using PolyPrompt.Clients;
using PolyPrompt.Models;
using PolyPrompt.Options;

using XAiClient grok = new XAiClient(Environment.GetEnvironmentVariable("XAI_API_KEY")!);
JsonElement input = JsonDocument.Parse("\"What changed in .NET this month?\"").RootElement;
ResponsesApiStreamingResponse stream = await grok.CreateResponseStreamingAsync(input,
    new XAiCompletionOptions { HostedTools = new List<ResponsesApiHostedTool> { ResponsesApiTools.WebSearch(), XAiTools.XSearch() } });

await foreach (ResponsesApiEvent evt in stream.Events)
{
    if (evt.Kind == ResponsesApiEventKind.TextDelta) Console.Write(evt.Data.GetProperty("delta").GetString());
    if (evt.Kind == ResponsesApiEventKind.HostedToolProgress) Console.WriteLine("\n[" + evt.Type + "]");
}
Console.WriteLine("\n" + stream.Response?.Citations.Count + " citations");
```

### Tools

Function tools come from `ToolChatRequest.Tools` or `FunctionTools`. Hosted and client-executed tools come from factories:

- `ResponsesApiTools` (OpenAI and xAI): `WebSearch`, `FileSearch`, `CodeInterpreter`, `Mcp`, `ImageGeneration`, `Shell`, `ToolSearch`
- `OpenAiTools`: `Computer`, `LocalShell`, `ApplyPatch`, `Custom`, `Namespace`, `ProgrammaticToolCalling`
- `XAiTools`: `XSearch`

Dated or preview variants go through `ResponsesApiHostedTool.Create(type, parameters)`. The generic `ToolCalls` contain function calls only. Calls to client-executed non-function tools (`custom`, `shell`, `local_shell`, `apply_patch`, `computer`) are in `RawResponse` and the native event stream.

### Options

`ResponsesApiCompletionOptions` holds the shared fields: `Store`, `PreviousResponseId`, `ParallelToolCalls`, `StrictTools`, `ReasoningEffortValue`, `ReasoningSummary`, `Include`, `FunctionTools`, `HostedTools`, `TextFormat`, `ToolChoice`, `ServiceTier`, `PromptCacheKey`, `User`, `SafetyIdentifier`, `AdditionalParameters`.

- `OpenAiCompletionOptions` adds `Metadata`, `Background`, `Conversation`, `MaxToolCalls`, `TopLogprobs`, `PromptCacheOptions`, `ContextManagement`, and `StreamOptions`.
- `XAiCompletionOptions` adds `MaxTurns`, `TopK`, `MinP`, `Logprobs`, `TopLogprobs`, and `Guidelines`.

Unmodeled fields (`prompt`, `truncation`, `prompt_cache_retention`) go in `AdditionalParameters`, which cannot replace built-in fields.

### Status mapping

- `incomplete` maps `incomplete_details.reason` to `FinishReason`: `max_output_tokens` becomes `length`, `content_filter` stays, and others such as `max_messages` pass through.
- `failed` and `cancelled` set `Error`.
- `queued` and `in_progress` are reported as the finish reason.
- Usage maps `input_tokens`, `output_tokens`, `total_tokens`, `cached_tokens`, `cache_write_tokens`, and `reasoning_tokens` onto `TokenUsage`.

### Azure OpenAI

Requests go to `https://{resource}.openai.azure.com/openai/v1/responses` with the deployment name as `model`, authenticated with an `api-key` header or an Entra ID bearer token (scope `https://ai.azure.com/.default`). Encrypted reasoning, compaction, stored responses, background mode, and `CancelResponseAsync` are supported; `CountInputTokensAsync` is OpenAI-only. Confirmed Azure hosted tools are code interpreter, remote MCP, image generation, and computer use, though PolyPrompt does not restrict tools client-side. An `invalid_encrypted_content` error comes with a hint to check proxies (for example API Management) that rewrite request bodies.

## Gemini tool calling

These rules apply to `GeminiClient` and `VertexAiClient`. The thought-signature rule also applies to `OpenAiCompatibleClient` pointed at Gemini's OpenAI-compatible endpoint.

- **Thought signatures.** Gemini 3 attaches an opaque `thoughtSignature` to each function call and rejects a follow-up that lost it. PolyPrompt captures it into `ToolCall.ThoughtSignature` and replays it, and `ToAssistantMessage()` keeps it. Persist it with your conversation.
- **History without signatures.** A replayed turn with no signature (for example from another provider) gets Google's documented `skip_thought_signature_validator` placeholder on its first function call.
- **Tool result names.** Gemini needs the function name on every result. A missing `ToolName` is taken from the matching call earlier in the conversation; if there is none, `ToolChatAsync` throws `ArgumentException` before sending.
- **Tool results** are `functionResponse` parts in a `user` turn, with consecutive results merged. JSON objects are sent as is; arrays, scalars, and text are wrapped as `{"result": ...}`.
- **Tool schemas** go in `parametersJsonSchema` by default. Set `GeminiCompletionOptions.ToolSchemaMode = GeminiToolSchemaMode.OpenApiSubset` for endpoints that only accept `parameters`.

## Reasoning effort

`ReasoningEffort` is a provider-neutral `ReasoningEffortLevel` plus optional per-provider overrides. When nothing sets it, no reasoning field is sent. Each provider maps every level to its nearest supported wire value, and `AppliedReasoningEffort` reports what was sent.

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;
using PolyPrompt.Options;

using GeminiClient gemini = new GeminiClient("AIza...");
ChatResponse deep = await gemini.ChatAsync("Prove it.", new CompletionOptions
{
    ReasoningEffort = new ReasoningEffort(ReasoningEffortLevel.High) { GeminiThinkingBudget = 16000 }
});
Console.WriteLine(deep.AppliedReasoningEffort);   // "16000"

using OpenAiClient openai = new OpenAiClient("sk-...");
ChatResponse exact = await openai.ChatAsync("Plan the migration.", new OpenAiCompletionOptions { ReasoningEffortValue = ReasoningEffortValue.XHigh });
```

How each level maps, for the providers that send a reasoning level by name:

| Level | OpenAI / Azure | xAI | OpenAI-compatible | Anthropic | Ollama |
|---|---|---|---|---|---|
| `None` | `none` | omitted | omitted | omitted | `false` |
| `Minimal` | `minimal` | `low` | `minimal` | `low`, no thinking | `false` |
| `Low` | `low` | `low` | `low` | `low` | `low` |
| `Medium` | `medium` | `medium` | `medium` | `medium` | `medium` |
| `High` | `high` | `high` | `high` | `high` | `high` |
| `XHigh` | `xhigh` | `xhigh` | `high` | `xhigh` | `high` |
| `Max` | `max` | `xhigh` | `high` | `max` | `high` |

And for the providers that take a thinking-token budget:

| Level | Gemini / Vertex | Cohere | Bedrock (Anthropic models) |
|---|---|---|---|
| `None` | `0` | `0` (disabled) | omitted |
| `Minimal` | `0` | `0` (disabled) | omitted |
| `Low` | `1024` | `1024` | `1024` |
| `Medium` | `8192` | `4096` | `4096` |
| `High` | `-1` (dynamic) | `16384` | `16384` |
| `XHigh` | `-1` | `16384` | `16384` |
| `Max` | `-1` | `16384` | `16384` |

Not every OpenAI reasoning model supports every value; PolyPrompt sends the mapped value and lets the model reject it. Per-provider overrides on the value object: `OpenAiValue` (Chat Completions), `GeminiThinkingBudget`, `OllamaThink`, `AnthropicEffort`, `CohereThinkingBudget`, and `BedrockThinkingBudget`. Responses clients use `ReasoningEffortValue`.

## Reasoning output and continuation state

Reasoning stays out of `Text`: streamed chunks carry `ReasoningText`, and responses carry `Reasoning`. `ToAssistantMessage()` carries native continuation state in `ProviderState` (encrypted reasoning, thinking signatures, redacted blocks, thought signatures, tool plans). Keep the returned message unchanged, and persist conversations with `PolyPromptJsonContext.Default.ListChatMessage`.

State is a tagged envelope, `{ "protocol", "provider", "scope", "value" }`. A client replays only state with its own protocol, provider, and endpoint. Anything else falls back to the plain message, so state never leaks across providers. State written by 3.x (including xAI's untagged output arrays) is still read.

## Citations

`Citations` is filled when the provider returns citations (hosted search tools, or documents with citations enabled); PolyPrompt never turns them on for you. `TextStart` and `TextEnd` index the final `Text` in UTF-16 code units, end exclusive (Gemini's byte offsets are converted). Positions inside the cited source are in `Source`, and the provider's own object is in `Raw`. Streaming responses fill `Citations` once the stream completes.

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;
using PolyPrompt.Options;

using OpenAiClient openai = new OpenAiClient("sk-...");
ChatResponse answer = await openai.ChatAsync("What happened in .NET this week? Cite sources.",
    new OpenAiCompletionOptions { HostedTools = new List<ResponsesApiHostedTool> { ResponsesApiTools.WebSearch() } });

foreach (Citation citation in answer.Citations)
{
    string span = citation.TextStart is int start && citation.TextEnd is int end ? answer.Text!.Substring(start, end - start) : "(no span)";
    Console.WriteLine(citation.Kind + " " + citation.Url + " -> " + span);
}
```

Where citations come from:

- **OpenAI, Azure, xAI**: `output_text` annotations (`url_citation`, `file_citation`, `container_file_citation`, `file_path`). xAI titles are citation numbers and are not used as `Title`.
- **Anthropic**: text-block `citations` (`char_location`, `page_location`, `content_block_location`, `search_result_location`, `web_search_result_location`). The span is the containing block.
- **Gemini, Vertex AI**: `groundingMetadata` supports joined to chunks, and `citationMetadata` sources.
- **Cohere**: `message.citations` (thinking-content citations excluded).
- **Bedrock**: `citationsContent` blocks with document, search-result, or web locations (no span in the text).
- **OpenAI-compatible**: `choices[].message.annotations` (`url_citation`).
- **Ollama, VoyageAI, TEI, TypeSafe**: none. `Citations` is empty, never null.

## Raw responses and token usage

`RawResponse` is the provider's complete response JSON. For streaming, it is the final response object when the provider sends one, as on the Responses API. It is returned to you only, never written to call details, logs, or telemetry.

`Usage` is a `TokenUsage` with nullable `PromptTokens`, `CompletionTokens`, `TotalTokens`, `CachedPromptTokens`, `CacheCreationTokens`, and `ReasoningTokens`.

## Call details, telemetry, cancellation

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

using OpenAiClient openai = new OpenAiClient("sk-...");
openai.TimeoutMs = 30000;
openai.CallDetailCapture = CallDetailCapture.Metadata;   // default: no bodies; Full adds bodies; None records nothing

using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
try
{
    ChatResponse response = await openai.ChatAsync("Hello!", token: cts.Token);
    foreach (CallDetail detail in openai.CallDetails)
        Console.WriteLine(detail.Method + " " + detail.Url + " " + detail.StatusCode + " " + detail.ResponseTimeMs + " ms");
}
catch (OperationCanceledException)
{
    Console.WriteLine("Cancelled or timed out.");
}
```

`RedactCredentials` (default true) replaces credential headers in recorded call details with `[redacted]`.

Every client emits metrics and traces through the BCL `Meter` and `ActivitySource` named `PolyPrompt`, with no exporter dependency. You get one span per operation (with GenAI attributes and the `polyprompt.capability` called), child spans per HTTP request, streaming, decision batch, and credential metrics, and `polyprompt.reasoning.effort` when a reasoning field is sent. [TELEMETRY.md](TELEMETRY.md) has the catalog.

## Options classes

Each operation accepts the shared base options type, the client's own derived type, or a type between them (for example `ResponsesApiCompletionOptions` on `OpenAiClient`).

- **Completion**: `CompletionOptions`. Provider types: `ResponsesApiCompletionOptions` (base of `OpenAiCompletionOptions` and `XAiCompletionOptions`), `OpenAiCompatibleCompletionOptions`, `AnthropicCompletionOptions`, `GeminiCompletionOptions` (Gemini and Vertex), `OllamaCompletionOptions`, `CohereCompletionOptions`.
- **Embedding**: `EmbeddingOptions`. Provider types: `OpenAiEmbeddingOptions` (OpenAI, compatible, Azure), `GeminiEmbeddingOptions`, `VertexAiEmbeddingOptions`, `BedrockEmbeddingOptions`, `OllamaEmbeddingOptions`, `CohereEmbeddingOptions`, `VoyageAiEmbeddingOptions`, `TeiEmbeddingOptions`.
- **Sparse embedding**: `SparseEmbeddingOptions`. Provider type: `TeiSparseEmbeddingOptions`.
- **Rerank**: `RerankOptions`. Provider types: `BedrockRerankOptions`, `CohereRerankOptions`, `VoyageAiRerankOptions`, `TeiRerankOptions`.
- **Classification**: `ClassificationOptions`. Provider types: `CohereClassificationOptions`, `TeiClassificationOptions`.
- **Decision**: `DecisionOptions`.

**Output limit.** When nothing sets `MaxTokens`, 4096 (`ProviderClientBase.DefaultMaxTokens`) is sent. Ollama and Ollama Cloud are the exception: they send no limit, so reasoning models can finish. Set `SendDefaultMaxTokens = false` to send no limit. Anthropic, and Anthropic models on Bedrock, require a limit and always send 4096.

## Common members

- **Provider clients**: `Capabilities`, `Supports(Capability)`, `ProviderName`, `Defaults` (a `ClientDefaults` through the base type), `SendDefaultMaxTokens`, `DefaultMaxTokens`, `MaxConcurrency` (decision batches, 1..64, default 4).
- **HTTP clients**: `Endpoint`, `ApiKey`, `ValidateConnectivityAsync`.
- **Every client**: `TimeoutMs`, `CallDetails`, `MaxCallDetails`, `ClearCallDetails`, `CallDetailCapture`, `RedactCredentials`.

The class hierarchy:

```
ClientBase                          logging, timeout, call details, telemetry
├── HttpClientBase                  HTTP transport, credentials, Endpoint, ApiKey
│   └── ProviderClientBase          every model operation, Capabilities, Defaults
│       ├── ResponsesApiClientBase  OpenAiClient, AzureOpenAiClient, XAiClient
│       ├── GenerateContentClientBase   GeminiClient, VertexAiClient
│       ├── OllamaClient            (OllamaCloudClient derives from it)
│       └── OpenAiCompatibleClient, AnthropicClient, BedrockClient,
│           CohereClient, VoyageAiClient, TeiClient, TypeSafeClient
└── CliAgentClientBase              agent harnesses (see HARNESSES.md)
```
