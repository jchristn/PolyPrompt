<img src="https://github.com/jchristn/PolyPrompt/blob/main/assets/logo.png?raw=true" width="192" height="192">

# PolyPrompt

[![NuGet Version](https://img.shields.io/nuget/v/PolyPrompt.svg?style=flat)](https://www.nuget.org/packages/PolyPrompt/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/PolyPrompt.svg?style=flat)](https://www.nuget.org/packages/PolyPrompt/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE.md)

PolyPrompt is a lightweight, unified .NET library for chat, tool calling, text generation, embeddings, reranking, classification, decision models, and model catalogs across **OpenAI**, **OpenAI-compatible servers** (vLLM, Ollama `/v1`, LM Studio, gateways), **Azure OpenAI**, **xAI Grok**, **Anthropic Claude**, **Google Gemini**, **Google Vertex AI**, **AWS Bedrock**, **Ollama**, **Ollama Cloud**, **Cohere**, **VoyageAI**, **Hugging Face Text Embeddings Inference (TEI)**, and **TypeSafe** (Jev and the local Ollaya runtime). You construct one client per provider, and every client has the same operations.

It also drives local **agent harnesses** (**Claude Code**, **Codex**, **Gemini CLI**, **Cursor CLI**, **Mux**, **Pi**, and **OpenCode**) as shell-free child processes. See [Agent Harnesses](#agent-harnesses).

> **Upgrading from 3.x?** 4.0 has one client per provider instead of one per capability, and `OpenAiClient` now speaks the Responses API. See [MIGRATION_V3_TO_V4.md](MIGRATION_V3_TO_V4.md), which lists every rename.

## What's New in v4.0

- **One client per provider.** `new OpenAiClient(key)` gives you chat, tool chat, generation, embeddings, and models on one object, with one key, one `HttpClient`, and one `Defaults`. The 39 capability clients (`OpenAiCompletionClient`, `OpenAiEmbeddingClient`, ...) are gone.
- **Every operation on every client.** `ProviderClientBase` declares them all. A client reports what it supports in `Capabilities`; `Supports(Capability.Embed)` checks first, and an unsupported call throws `CapabilityNotSupportedException` before any request is sent. The [capability matrix](#capability-matrix) is checked against the code by tests.
- **OpenAI on the Responses API.** `OpenAiClient` uses `/v1/responses`, so encrypted reasoning carries across tool-loop turns. `OpenAiCompatibleClient` speaks Chat Completions for vLLM, Ollama `/v1`, LM Studio, and gateways. `AzureOpenAiClient` uses Azure's v1 API (no `api-version`).
- **A shared Responses engine** for OpenAI, Azure OpenAI, and xAI: `ResponsesApiClientBase` with native operations (`CreateResponseAsync`, `ContinueResponseAsync`, `GetResponseAsync`, `DeleteResponseAsync`, `ListResponseInputItemsAsync`, `CompactConversationAsync`), typed tool factories, and `ResponsesApiEventKind` for native streams.
- **Sectioned defaults.** `client.Defaults.Completion.Model`, `client.Defaults.Embedding.Model`, and so on. A provider's defaults type has a section only for the capabilities it supports. The `Model` shorthand is gone.
- **New result fields** on every response: `RawResponse` (the provider JSON), `Citations` (normalized across providers), and `AppliedReasoningEffort` (the wire value actually sent).
- **More reasoning levels:** `None`, `XHigh`, and `Max`, mapped per provider (see [Reasoning effort](#reasoning-effort)).
- **Extensible values** for fixed-value provider settings: `ServiceTier`, `ReasoningSummary`, and `ReasoningEffortValue` accept known values and any future string.
- **Fail-fast construction.** Blank, whitespace, or URL-shaped keys and non-URL endpoints throw with guidance. Passing another provider's options type throws instead of being silently ignored.
- **`ClientOptions`** carries logging, `HttpClient`, timeout, and call-detail settings for every client, including the renamed harness clients (`CodexClient`, `ClaudeCodeClient`, ...).
- **Updated default models**, for example `gpt-6.1-sol` and `claude-sonnet-5-5`; clients whose models depend on your account or server (Bedrock, Azure, Ollama Cloud, OpenAI-compatible) have no default. See [Default models](#default-models).
- **One tagged continuation envelope for every provider.** State from another provider, protocol, or endpoint is ignored instead of leaking; 3.x state is still read.
- **`llms.txt`** in the repository and the package, pointing coding agents at the sections below.

## Installation

```bash
dotnet add package PolyPrompt
```

Current version: **4.0.0**. PolyPrompt targets **.NET 8.0** and **.NET 10.0**, takes no provider SDK dependencies (SigV4 signing and Google service-account token exchange are implemented in-library), and is Native AOT and trimming compatible.

## Quick Start

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

using OpenAiClient openai = new OpenAiClient(Environment.GetEnvironmentVariable("OPENAI_API_KEY")!);
openai.Defaults.Completion.Model = "gpt-6-astra";          // the default is gpt-6.1-sol
openai.Defaults.Embedding.Model = "text-embedding-3-large";

ChatResponse reply = await openai.ChatAsync("Explain dependency injection in one paragraph.");
Console.WriteLine(reply.Text);

EmbeddingResponse vector = await openai.EmbedAsync("some text");
await foreach (ModelInformation model in openai.ListModelsAsync()) Console.WriteLine(model.Name);

using AnthropicClient claude = new AnthropicClient(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")!);
if (!claude.Supports(Capability.Embed)) Console.WriteLine("Anthropic has no embeddings; pair it with VoyageAiClient.");
```

One constructor and one call per provider:

```csharp
using PolyPrompt.Auth;
using PolyPrompt.Clients;
using PolyPrompt.Models;

string key = "your-key";

using OpenAiClient openai = new OpenAiClient(key);                                            // Responses API
using OpenAiCompatibleClient vllm = new OpenAiCompatibleClient("http://localhost:8000/v1");  // Chat Completions
vllm.Defaults.Completion.Model = "qwen3-8b";
using AzureOpenAiClient azure = new AzureOpenAiClient("https://my-resource.openai.azure.com", key);
azure.Defaults.Completion.Model = "my-gpt-deployment";
using XAiClient grok = new XAiClient(key);
using AnthropicClient claude = new AnthropicClient(key);
using GeminiClient gemini = new GeminiClient(key);
using VertexAiClient vertex = new VertexAiClient("my-project", "us-central1", new AdcCredential());
using BedrockClient bedrock = new BedrockClient("us-east-1", new EnvironmentAwsCredential("us-east-1"));
bedrock.Defaults.Completion.Model = "anthropic.claude-sonnet-5-5";
using OllamaClient ollama = new OllamaClient();                                              // http://localhost:11434
using OllamaCloudClient ollamaCloud = new OllamaCloudClient(key);
ollamaCloud.Defaults.Completion.Model = "gpt-oss:120b";
using CohereClient cohere = new CohereClient(key);
using VoyageAiClient voyage = new VoyageAiClient(key);
using TeiClient tei = new TeiClient("http://localhost:8080");
using TypeSafeClient jev = new TypeSafeClient(key);

ChatResponse chat = await claude.ChatAsync("Hello!");
EmbeddingResponse embedding = await voyage.EmbedAsync("Hello!");
RerankResponse ranked = await cohere.RerankAsync("capital of France", new List<string> { "Berlin", "Paris" });
SparseEmbeddingResponse sparse = await tei.EmbedSparseAsync("Hello!");
DecisionResponse decision = await jev.DecideAsync(new DecisionRequest { State = "Refund please", Questions = { DecisionQuestion.Binary("refund", "Does the customer want a refund?") } });
```

## Capability Matrix

Every client exposes every operation. ✅ means the provider supports it; any other call throws `CapabilityNotSupportedException` ("Anthropic does not support Embed (EmbedAsync). Check client.Supports(Capability.Embed) first, or see the capability matrix: ...") synchronously, before any network activity. `Capabilities` reports provider-level support; a particular model may still lack a feature (for example tool calling on a small Ollama model).

<!-- capability-matrix:start -->
| Client | Chat | ToolChat | Generate | Embed | SparseEmbed | Rerank | Classify | Decide | Models |
|---|---|---|---|---|---|---|---|---|---|
| `OpenAiClient` | ✅ | ✅ | ✅ | ✅ | | | | | ✅ |
| `OpenAiCompatibleClient` | ✅ | ✅ | ✅ | ✅ | | | | | ✅ |
| `AzureOpenAiClient` | ✅ | ✅ | ✅ | ✅ | | | | | ✅ |
| `XAiClient` | ✅ | ✅ | ✅ | | | | | | ✅ |
| `AnthropicClient` | ✅ | ✅ | ✅ | | | | | | ✅ |
| `GeminiClient` | ✅ | ✅ | ✅ | ✅ | | | | | ✅ |
| `VertexAiClient` | ✅ | ✅ | ✅ | ✅ | | | | | |
| `BedrockClient` | ✅ | ✅ | ✅ | ✅ | | ✅ | | | ✅ |
| `OllamaClient` | ✅ | ✅ | ✅ | ✅ | | | | | ✅ |
| `OllamaCloudClient` | ✅ | ✅ | ✅ | ✅ | | | | | ✅ |
| `CohereClient` | ✅ | ✅ | ✅ | ✅ | | ✅ | ✅ | | ✅ |
| `VoyageAiClient` | | | | ✅ | | ✅ | | | |
| `TeiClient` | | | | ✅ | ✅ | ✅ | ✅ | | ✅ |
| `TypeSafeClient` | | | | | | | | ✅ | |
<!-- capability-matrix:end -->

| Capability | Operations |
|---|---|
| `Chat` | `ChatAsync`, `ChatStreamingAsync` |
| `ToolChat` | `ToolChatAsync`, `ToolChatStreamingAsync` |
| `Generate` | `GenerateAsync`, `GenerateStreamingAsync` |
| `Embed` | `EmbedAsync(string)`, `EmbedAsync(List<string>)` |
| `SparseEmbed` | `EmbedSparseAsync(string)`, `EmbedSparseAsync(List<string>)` |
| `Rerank` | `RerankAsync(query, documents)` |
| `Classify` | `ClassifyAsync(string)`, `ClassifyAsync(List<string>)` |
| `Decide` | `DecideAsync(DecisionRequest)`, `DecideAsync(List<DecisionRequest>)` |
| `Models` | `ListModelsAsync`, `ModelExistsAsync`, `GetModelInformationAsync` |
| (always) | `ValidateConnectivityAsync` |

Notes: xAI no longer offers embedding models, so `XAiClient` has no `Embed`. Cohere recommends classifying with Embed models; `/v1/classify` is still available. Ollama Cloud supports the embeddings API, but the hosted catalog may not include embedding models. A TEI server hosts one model; which TEI operations succeed depends on its type.

## Which OpenAI Client?

| You are calling | Use | Wire API |
|---|---|---|
| OpenAI (`api.openai.com`), or any server that implements `/v1/responses` | `OpenAiClient` | Responses |
| Azure OpenAI (`{resource}.openai.azure.com`) | `AzureOpenAiClient` | Responses on the Azure v1 API |
| vLLM, Ollama `/v1`, LM Studio, OpenRouter, Gemini's OpenAI endpoint, gateways, or OpenAI itself when you want Chat Completions | `OpenAiCompatibleClient` | Chat Completions (and legacy `/v1/completions` for generation) |

If `OpenAiClient` gets a 404 or 405 from `/responses`, the response's `Error` ends with "This server may not implement the OpenAI Responses API. Use OpenAiCompatibleClient for Chat Completions servers." Passing a URL as `OpenAiClient`'s key throws `ArgumentException` with the same advice.

```csharp
using PolyPrompt.Clients;

using OpenAiCompatibleClient lmStudio = new OpenAiCompatibleClient("http://localhost:1234/v1");
lmStudio.Defaults.Completion.Model = "qwen3-8b";

// Azure's v1 Chat Completions route with the key in an api-key header.
using OpenAiCompatibleClient azureChat = new OpenAiCompatibleClient(
    "https://my-resource.openai.azure.com/openai/v1", "azure-key",
    new OpenAiCompatibleClientOptions { AuthPlacement = AuthPlacement.Header("api-key") });
azureChat.Defaults.Completion.Model = "my-deployment";
```

## Architecture

```
ClientBase                          logging, TimeoutMs, CallDetails, capture and redaction, telemetry, Dispose
├── HttpClientBase                  HTTP transport, per-request credentials, Endpoint, ApiKey, ValidateConnectivityAsync -> bool
│   └── ProviderClientBase          every model operation, Capabilities, Supports, ProviderName, Defaults
│       ├── ResponsesApiClientBase  shared Responses engine + native Responses operations
│       │   ├── OpenAiClient
│       │   ├── AzureOpenAiClient
│       │   └── XAiClient
│       ├── GenerateContentClientBase   shared Gemini generateContent engine
│       │   ├── GeminiClient
│       │   └── VertexAiClient
│       ├── OpenAiCompatibleClient, AnthropicClient, BedrockClient
│       ├── OllamaClient
│       │   └── OllamaCloudClient
│       └── CohereClient, VoyageAiClient, TeiClient, TypeSafeClient
└── CliAgentClientBase              agent harnesses: RunAsync, RunStreamingAsync, ValidateConnectivityAsync -> CliConnectivityResult
```

Rules that hold for every provider client:

1. **Capabilities are checked first.** An unsupported operation throws `CapabilityNotSupportedException` (a `NotSupportedException` with `Provider`, `Capability`, and `Operation`) synchronously. No request, call detail, span, or metric is recorded for it.
2. **Arguments are validated before any request.** Null prompts or inputs throw `ArgumentNullException`; empty lists, null elements, empty tool-chat messages, invalid decision questions, and another provider's options type throw `ArgumentException`; a call with no model throws `InvalidOperationException` naming the defaults section to set.
3. **HTTP errors do not throw.** An HTTP failure returns `Success = false` with `StatusCode` and `Error`. Streaming failures after headers use the final `State` and `StreamError`. Cancellation and `TimeoutMs` timeouts propagate as `OperationCanceledException`.
4. **Credentials are attached per request.** No client modifies `HttpClient.DefaultRequestHeaders`, so clients for different providers can share one `HttpClient`.

### Defaults and per-call options

Each client has a get-only `Defaults` of its own type (`OpenAiDefaults`, `AnthropicDefaults`, ...) with one section per supported capability: `Completion`, `Embedding`, `SparseEmbedding`, `Rerank`, `Classification`, and `Decision`. A section is typed with that provider's options (for example `OllamaCompletionOptions`), so provider settings can be defaulted too. A section the provider does not support does not compile on its own defaults type, and is null when read through the base `ClientDefaults`.

Every operation also takes its capability's options per call. For each setting, a non-null per-call value wins, then the `Defaults` section, then the provider's own default (the field is not sent).

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;
using PolyPrompt.Options;

using OllamaClient ollama = new OllamaClient("http://localhost:11434");
ollama.Defaults.Completion.Model = "gpt-oss:20b";
ollama.Defaults.Completion.MaxTokens = 512;
ollama.Defaults.Completion.ContextLength = 8192;          // Ollama-specific
ollama.Defaults.Completion.SystemPrompt = "Be concise.";
ollama.Defaults.Embedding.Model = "all-minilm";

// Max tokens 512 and context length 8192 from Defaults, temperature 0.9 from this call.
ChatResponse reply = await ollama.ChatAsync("Explain dependency injection.", new CompletionOptions { Temperature = 0.9 });

// A per-call model does not change the client's model.
ChatResponse other = await ollama.ChatAsync("Hi", new OllamaCompletionOptions { Model = "gemma3:4b", TopK = 20 });
```

**The options-type rule.** Each operation accepts the shared base options type, the client's own derived type, or a type between them (for example `ResponsesApiCompletionOptions` on `OpenAiClient`). Another provider's type throws `ArgumentException`: "XAiCompletionOptions cannot be used with OpenAiClient. Use OpenAiCompletionOptions or CompletionOptions."

**Output limit.** When nothing sets `MaxTokens`, 4096 (`ProviderClientBase.DefaultMaxTokens`) is sent, except on Ollama and Ollama Cloud, which send no limit so reasoning models can finish. Set `SendDefaultMaxTokens = false` to send no limit; Anthropic, and Anthropic models on Bedrock, require one and always send 4096.

### ClientOptions

Every constructor takes an optional `ClientOptions`:

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

using HttpClient shared = new HttpClient();
ClientOptions options = new ClientOptions
{
    HttpClient = shared,                       // never disposed by the client
    TimeoutMs = 60000,
    CallDetailCapture = CallDetailCapture.Full,
    RedactCredentials = true,
    Logging = new SyslogLogging.LoggingModule(),
};

using AnthropicClient claude = new AnthropicClient("sk-ant-...", options: options);
using GeminiClient gemini = new GeminiClient("AIza...", options: options);   // same connection pool, own credentials
```

| Client | Constructor |
|---|---|
| `OpenAiClient`, `XAiClient`, `AnthropicClient`, `GeminiClient`, `OllamaCloudClient`, `CohereClient`, `VoyageAiClient`, `TypeSafeClient` | `(string apiKey, string? endpoint = null, ClientOptions? options = null)` |
| `OpenAiCompatibleClient`, `TeiClient` | `(string endpoint, string? apiKey = null, ClientOptions? options = null)` (`OpenAiCompatibleClientOptions` adds `AuthPlacement`) |
| `OllamaClient` | `(string endpoint = "http://localhost:11434", string? apiKey = null, ClientOptions? options = null)` |
| `AzureOpenAiClient` | `(string endpoint, string apiKey, ClientOptions? options = null)` or `(string endpoint, ICredentialProvider credential, ClientOptions? options = null)` |
| `VertexAiClient` | `(string project, string region, ICredentialProvider credential, string? endpoint = null, ClientOptions? options = null)` |
| `BedrockClient` | `(string region, IAwsCredentialProvider credential, string? endpoint = null, ClientOptions? options = null)` |
| Harness clients | `(string executable = <default>, ClientOptions? options = null)` |

A required key that is null, blank, contains whitespace, or looks like a URL throws `ArgumentException`; an endpoint that is not an absolute http(s) URL throws. Endpoints are normalized per provider: OpenAI-family endpoints get `/v1` appended unless present, Ollama strips a trailing `/v1`, Azure appends `/openai/v1`, and Bedrock and Vertex derive regional hosts.

### Provider-agnostic code

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

static async Task<string?> AskAsync(ProviderClientBase client, string question)
{
    if (!client.Supports(Capability.Chat)) return null;
    ChatResponse response = await client.ChatAsync(question);
    return response.Success ? response.Text : response.Error;
}

static async Task<List<float[]>> EmbedAllAsync(ProviderClientBase client, List<string> texts)
{
    if (!client.Supports(Capability.Embed)) throw new InvalidOperationException(client.ProviderName + " cannot embed.");
    EmbeddingResponse response = await client.EmbedAsync(texts);
    return response.Embeddings.Select(e => e.Embedding).ToList();
}

using OllamaClient local = new OllamaClient();
Console.WriteLine(await AskAsync(local, "Hello"));
```

Through `ProviderClientBase`, `client.Defaults` is a `ClientDefaults` whose unsupported sections are null: `client.Defaults.Embedding?.Model`.

## Responses API (OpenAI, Azure OpenAI, and xAI)

The Responses API replaces Chat Completions at OpenAI. Each response is a list of output items (messages, reasoning, function calls, hosted tool calls); PolyPrompt projects them onto the generic results (`Text`, `Reasoning`, `ToolCalls`, `Usage`, `FinishReason`, `Citations`) and keeps the full JSON in `RawResponse`.

**Encrypted reasoning carries across turns.** Every request includes `reasoning.encrypted_content`, and each response's `ProviderState` holds its output items. `ToAssistantMessage()` keeps that state, and the next tool-chat call replays the items in order, so the model does not re-think from scratch at every step of a tool loop, even with `Store = false`.

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

**`Store`.** OpenAI's API (and its official SDKs) default `store` to true, keeping responses for at least 30 days. PolyPrompt always sends `store` explicitly: `false` unless you set `Store = true`. Stored responses can be retrieved, continued by id, listed, cancelled (background), and deleted.

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

**Native events.** `CreateResponseStreamingAsync` streams every server-sent event with its raw `Type` and a mapped `Kind` (`TextDelta`, `ReasoningSummaryDelta`, `FunctionCallArgumentsDelta`, `AnnotationAdded`, `HostedToolProgress`, `Completed`, ...). Unknown events map to `Other`.

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

**Tools.** Function tools come from `ToolChatRequest.Tools` or `FunctionTools`. Hosted and client-executed tools come from the factories: `ResponsesApiTools` (both providers: `WebSearch`, `FileSearch`, `CodeInterpreter`, `Mcp`, `ImageGeneration`, `Shell`, `ToolSearch`), `OpenAiTools` (`Computer`, `LocalShell`, `ApplyPatch`, `Custom`, `Namespace`, `ProgrammaticToolCalling`), and `XAiTools` (`XSearch`). Dated or preview variants go through `ResponsesApiHostedTool.Create(type, parameters)`. The generic `ToolCalls` contain function calls only; calls to client-executed non-function tools (`custom`, `shell`, `local_shell`, `apply_patch`, `computer`) are in `RawResponse` and the native event stream.

**Options.** `ResponsesApiCompletionOptions` holds the shared fields (`Store`, `PreviousResponseId`, `ParallelToolCalls`, `StrictTools`, `ReasoningEffortValue`, `ReasoningSummary`, `Include`, `FunctionTools`, `HostedTools`, `TextFormat`, `ToolChoice`, `ServiceTier`, `PromptCacheKey`, `User`, `SafetyIdentifier`, `AdditionalParameters`). `OpenAiCompletionOptions` adds `Metadata`, `Background`, `Conversation`, `MaxToolCalls`, `TopLogprobs`, `PromptCacheOptions`, `ContextManagement`, and `StreamOptions`; `XAiCompletionOptions` adds `MaxTurns`, `TopK`, `MinP`, `Logprobs`, `TopLogprobs`, and `Guidelines`. Unmodeled fields (`prompt`, `truncation`, `prompt_cache_retention`) go in `AdditionalParameters`, which cannot replace built-in fields.

**Status mapping.** `incomplete` maps `incomplete_details.reason` to `FinishReason` (`max_output_tokens` to `length`, `content_filter` unchanged, others such as `max_messages` pass through); `failed` and `cancelled` set `Error`; `queued` and `in_progress` are reported as the finish reason. Usage maps `input_tokens`, `output_tokens`, `total_tokens`, `cached_tokens`, `cache_write_tokens`, and `reasoning_tokens` onto `TokenUsage`.

**Azure OpenAI** sends requests to `https://{resource}.openai.azure.com/openai/v1/responses` with the deployment name as `model`, authenticating with an `api-key` header or an Entra ID bearer token (scope `https://ai.azure.com/.default`). Encrypted reasoning, compaction, stored responses, background mode, and `CancelResponseAsync` are supported; `CountInputTokensAsync` is OpenAI-only. Confirmed Azure hosted tools are code interpreter, remote MCP, image generation, and computer use; PolyPrompt does not restrict tools client-side. An `invalid_encrypted_content` error comes with a hint to check proxies (for example API Management) that rewrite request bodies.

## Provider Notes

| Client | Auth | Notes |
|---|---|---|
| `OpenAiClient` | bearer key | Responses API; `CancelResponseAsync`, `CountInputTokensAsync`. |
| `OpenAiCompatibleClient` | optional; `AuthPlacement` bearer, header, or query | Reads `reasoning_content` and `reasoning` extensions; replays Gemini thought signatures in `extra_content.google.thought_signature`; generation uses legacy `/v1/completions`. |
| `AzureOpenAiClient` | `api-key` or Entra ID | v1 API, no `api-version`; deployment names are models. |
| `XAiClient` | bearer key | Responses API; `ListLanguageModelsAsync`, `ListImageGenerationModelsAsync`, `ListVideoGenerationModelsAsync`, `GetCatalogModelInformationAsync`; at most 1000 tools; reasoning effort `low` to `xhigh`. |
| `AnthropicClient` | `x-api-key` | `AnthropicVersion`, `WorkspaceId` (identity-linked keys), `PageSize`; `max_tokens` always sent; citations on documents cannot be combined with `output_config.format`. |
| `GeminiClient` | `x-goog-api-key` | See [Gemini tool calling](#gemini-tool-calling). |
| `VertexAiClient` | OAuth from `ICredentialProvider` (ADC, service account, static token) | Same generateContent schema as Gemini; embeddings through `:predict`; connectivity uses `:countTokens`. |
| `BedrockClient` | SigV4 from `IAwsCredentialProvider` | Converse API for completion; InvokeModel for embeddings (Titan, Cohere) and rerank; control-plane model listing. No default completion model: IDs and inference profiles vary by region and account. |
| `OllamaClient` | none, or a bearer token behind a proxy | `PullModelAsync`, `DeleteModelAsync`, `LoadModelAsync`, `UnloadModelAsync`, `PullTimeout`; `KeepAlive` on completion and embedding options. |
| `OllamaCloudClient` | bearer key | Same operations as `OllamaClient` at `https://ollama.com`; no default models because the hosted catalog changes. |
| `CohereClient` | bearer key | v2 chat, embed, and rerank; v1 classify; `PageSize` for model listing. Tool choice accepts only `REQUIRED` and `NONE`. |
| `VoyageAiClient` | bearer key | Embed (`InputType`, `OutputDimension`, `OutputDtype`) and rerank; connectivity embeds a probe word. |
| `TeiClient` | none, or a bearer token | One model per server: embed, sparse embed (SPLADE), rerank (cross-encoders), classify, `/info`. |
| `TypeSafeClient` | bearer key (a local Ollaya server accepts any value) | `POST /v1/systemone`; binary questions are TypeSafe `noul` questions; `MaxConcurrency` for batches. |

### Authentication

`PolyPrompt.Auth` implements the richer credentials with no SDK: Vertex AI takes an `ICredentialProvider` (`AdcCredential` for Application Default Credentials, `ServiceAccountCredential.FromJson(...)`, or `StaticTokenCredential`), tokens cached and refreshed ahead of expiry; Bedrock signs every request with Signature Version 4 from `StaticAwsCredential(accessKey, secretKey, region, sessionToken?)` or `EnvironmentAwsCredential()`; Azure accepts either an API key string or an `ICredentialProvider` for Entra ID.

### Gemini tool calling

These rules apply to `GeminiClient` and `VertexAiClient` (the thought-signature rule also applies to `OpenAiCompatibleClient` pointed at Gemini's OpenAI-compatible endpoint):

- **Thought signatures.** Gemini 3 attaches an opaque `thoughtSignature` to each function call and rejects a follow-up that lost it. PolyPrompt captures it into `ToolCall.ThoughtSignature` and replays it; `ToAssistantMessage()` keeps it. Persist it with your conversation.
- **History without signatures.** A replayed turn with no signature (from another provider) gets Google's documented `skip_thought_signature_validator` placeholder on its first function call.
- **Tool result names.** Gemini needs the function name on every result; a missing `ToolName` is taken from the matching call earlier in the conversation, otherwise `ToolChatAsync` throws `ArgumentException` before sending.
- **Tool results** are `functionResponse` parts in a `user` turn, consecutive results merged. JSON objects are sent as is; arrays, scalars, and text are wrapped as `{"result": ...}`.
- **Tool schemas** go in `parametersJsonSchema` by default; set `GeminiCompletionOptions.ToolSchemaMode = GeminiToolSchemaMode.OpenApiSubset` for endpoints that only accept `parameters`.

## Agent Harnesses

Claude Code, Codex, Gemini CLI, Cursor CLI, Mux, Pi, and OpenCode are not model APIs. They are **agent harnesses**: local CLIs that run their own tool loop and report agent events. PolyPrompt drives them through `CliAgentClientBase`, separate from the provider clients, so they never hand you tool calls to execute.

### Prerequisites

Install each CLI and sign in **with the CLI itself**; PolyPrompt never takes an API key for a harness. The executable must be on `PATH` (or set `Executable`).

| Harness | Client | Executable | Install | Sign in |
|---|---|---|---|---|
| Claude Code | `ClaudeCodeClient` | `claude` | [Claude Code setup](https://code.claude.com/docs/en/setup) | `claude auth login` |
| Codex | `CodexClient` | `codex` | [Codex CLI](https://developers.openai.com/codex/cli) | `codex login`, or `OPENAI_API_KEY` / `CODEX_API_KEY` |
| Gemini CLI | `GeminiCliClient` | `gemini` | [Gemini CLI](https://github.com/google-gemini/gemini-cli) | run `gemini` once and sign in, or `GEMINI_API_KEY` |
| Cursor CLI | `CursorClient` | `cursor-agent` | [Cursor CLI](https://cursor.com/docs/cli/installation) | `cursor-agent login` or `CURSOR_API_KEY` |
| Mux | `MuxClient` | `mux` | `dotnet tool install -g mux.cli` | configure an endpoint; `mux probe` checks it |
| Pi | `PiClient` | `pi` | [pi.dev](https://pi.dev) | `pi` then `/login`, or a provider key |
| OpenCode | `OpenCodeClient` | `opencode` | [opencode.ai](https://opencode.ai/docs) | `opencode auth login` |

### Run a turn

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

using CodexClient codex = new CodexClient { WorkingDirectory = "/path/to/repo" };

CliConnectivityResult status = await codex.ValidateConnectivityAsync();
if (!status.Success) throw new InvalidOperationException(status.Status + ": " + status.Message);

CliAgentResult result = await codex.RunAsync(new CliAgentRequest("Summarize what this repository does."));
Console.WriteLine(result.Success ? result.Text : result.FailureKind + ": " + result.Error);

CliAgentResult next = await codex.RunAsync(new CliAgentRequest("What is the project name?") { SessionId = result.SessionId });
```

### Stream agent events

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

using ClaudeCodeClient claude = new ClaudeCodeClient { Model = "sonnet" };

await foreach (CliAgentEvent evt in claude.RunStreamingAsync(new CliAgentRequest("Find the TODO comments in src/")))
{
    switch (evt.Kind)
    {
        case CliAgentEventKind.TextDelta: Console.Write(evt.Text); break;
        case CliAgentEventKind.ToolUse: Console.WriteLine("\n[" + evt.ToolName + "] " + evt.ToolInput); break;
        case CliAgentEventKind.Completed: Console.WriteLine("\nDone. Cost: " + evt.CostUsd); break;
        case CliAgentEventKind.Failed: Console.WriteLine("\nFailed (" + evt.FailureKind + "): " + evt.Text); break;
    }
}
```

A harness stream is coarse and its chunk boundaries vary; the last event is always `Completed` or `Failed`, and cancelling kills the whole process tree. See [STREAMING.md](STREAMING.md).

### Permissions

`CliPermissionMode` controls what the harness may do. The default is **`Restricted`**, and no mode other than `Unrestricted` ever passes a bypass, yolo, or force flag.

| Harness | `Restricted` (default) | `ReadOnly` | `WorkspaceWrite` | `Unrestricted` |
|---|---|---|---|---|
| Claude Code | `--permission-mode dontAsk --tools Read,Glob,Grep --strict-mcp-config` | `--permission-mode dontAsk --disallowedTools Edit,Write,NotebookEdit` | `--permission-mode acceptEdits` | `--permission-mode bypassPermissions` |
| Codex | `-c sandbox_mode="read-only" -c approval_policy="never" --skip-git-repo-check` | same as Restricted | `-c sandbox_mode="workspace-write" -c approval_policy="never"` | `--dangerously-bypass-approvals-and-sandbox` |
| Gemini CLI | `--approval-mode default` | same as Restricted | `--approval-mode auto_edit` | `--approval-mode yolo` |
| Cursor CLI | `--mode ask --sandbox enabled` | same as Restricted | `--sandbox enabled` | `--force --sandbox disabled` |
| Mux | `--approval-policy auto --sandbox read-only --deny-tools web_search,web_retrieve` | `--approval-policy auto --sandbox read-only` | `--approval-policy auto --sandbox workspace-write --deny-tools run_process` | `--yolo --sandbox none` |
| Pi (RPC) | `--tools read,grep,find,ls --no-mcp --no-approve` | same as Restricted | adds `edit,write` | Pi defaults |
| OpenCode | `--agent build` with an inline read/glob/grep-only permission config | same as Restricted | adds `edit` | `--auto` |

### Requests and results

`CliAgentRequest` takes `Prompt` (sent on standard input), `SystemPrompt`, `History`, `SessionId`, `Model`, `WorkingDirectory`, `PermissionMode`, `TimeoutMs`, `Environment`, `ExtraArguments`, and `MaxOutputCharacters`. `CliAgentResult` carries `Success`, `Text`, `Reasoning`, `SessionId`, `Events`, `Usage`, `CostUsd`, `ExitCode`, `StandardError`, `RawOutput`, `FailureKind`, `TimedOut`, `OutputTruncated`, and `MalformedLines`. Failures do not throw; only caller cancellation does. Each process is recorded in `CallDetails` as a `ProcessCallDetail` and reported to telemetry as `exec {executable}` spans.

`src/CliAgentConsole` runs any harness (`--provider codex|claudecode|geminicli|cursor|mux|pi|opencode`), and each harness has its own console (`src/ClaudeCodeConsole`, `src/CodexConsole`, `src/GeminiCliConsole`, `src/CursorCliConsole`, `src/MuxConsole`, `src/PiConsole`, `src/OpenCodeConsole`) with `--prompt`, `--check`, `--models`, `--setup`, `--permission`, and interactive commands.

## Detailed Examples

### Chat with a system prompt and provider options

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;
using PolyPrompt.Options;

using AnthropicClient claude = new AnthropicClient(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")!);
claude.Defaults.Completion.SystemPrompt = "You are a terse senior engineer.";

ChatResponse response = await claude.ChatAsync("Review this: var x = list.Count() > 0;",
    new AnthropicCompletionOptions { MaxTokens = 300, TopK = 40 });

if (response.Success) Console.WriteLine(response.Text);
else Console.WriteLine(response.StatusCode + ": " + response.Error);
```

### Tool calling

Tool execution belongs to your code. Append `ToAssistantMessage()` and one `ChatMessage.ToolResult` per call, then call again:

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

using GeminiClient gemini = new GeminiClient(Environment.GetEnvironmentVariable("GEMINI_API_KEY")!);

ToolChatRequest request = new ToolChatRequest
{
    Messages = { ChatMessage.User("What's the weather in Seattle and Tokyo?") },
    Tools = { ToolDefinition.Function("get_weather", "Get the weather.", new Dictionary<string, object> { { "type", "object" } }) },
    ToolChoice = "auto"
};

ToolChatResponse turn = await gemini.ToolChatAsync(request);
request.Messages.Add(turn.ToAssistantMessage());   // keeps thought signatures and native state
foreach (ToolCall call in turn.ToolCalls)
    request.Messages.Add(ChatMessage.ToolResult(call.Id, call.Name, "{\"temperature\":72}"));

ToolChatResponse answer = await gemini.ToolChatAsync(request);
Console.WriteLine(answer.Text);
```

### Streaming

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

using OllamaClient ollama = new OllamaClient();
ollama.Defaults.Completion.Model = "gpt-oss:20b";

ChatStreamingResponse stream = await ollama.ChatStreamingAsync("Write a haiku about compilers.");
await foreach (ChatStreamingChunk chunk in stream.Chunks)
{
    if (chunk.ReasoningText != null) Console.Write(chunk.ReasoningText);
    if (chunk.Text != null) Console.Write(chunk.Text);
}

if (stream.State != StreamingState.Completed) Console.WriteLine("Stream ended: " + stream.StreamError);
Console.WriteLine("\nTime to first token: " + stream.TimeToFirstTokenMs + " ms");
```

Enumerate a stream completely and require `State == StreamingState.Completed` before trusting its result. [STREAMING.md](STREAMING.md) covers the lifecycle, tool-call deltas, and Responses events.

### Reasoning effort

`ReasoningEffort` is provider-neutral: a `ReasoningEffortLevel` plus optional per-provider overrides. Set it in `Defaults.Completion.ReasoningEffort` or per call; when nothing sets it, no reasoning field is sent. Each provider maps every level to its nearest supported wire value, and `AppliedReasoningEffort` reports what was sent.

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;
using PolyPrompt.Options;

using GeminiClient gemini = new GeminiClient("AIza...");
gemini.Defaults.Completion.ReasoningEffort = ReasoningEffort.Medium;

ChatResponse quick = await gemini.ChatAsync("Rate 0 to 10 as JSON.", new CompletionOptions { ReasoningEffort = ReasoningEffort.None });
ChatResponse deep = await gemini.ChatAsync("Prove it.", new CompletionOptions
{
    ReasoningEffort = new ReasoningEffort(ReasoningEffortLevel.High) { GeminiThinkingBudget = 16000 }
});
Console.WriteLine(quick.AppliedReasoningEffort + " / " + deep.AppliedReasoningEffort);   // "0" / "16000"

using OpenAiClient openai = new OpenAiClient("sk-...");
ChatResponse exact = await openai.ChatAsync("Plan the migration.", new OpenAiCompletionOptions { ReasoningEffortValue = ReasoningEffortValue.XHigh });
```

| Level | OpenAI / Azure (Responses) | xAI | OpenAI-compatible | Anthropic | Gemini / Vertex | Ollama | Cohere | Bedrock (Anthropic models) |
|---|---|---|---|---|---|---|---|---|
| `None` | `none` | omitted | omitted | omitted | `0` | `false` | `0` (disabled) | omitted |
| `Minimal` | `minimal` | `low` | `minimal` | `low`, no thinking | `0` | `false` | `0` (disabled) | omitted |
| `Low` | `low` | `low` | `low` | `low` | `1024` | `low` | `1024` | `1024` |
| `Medium` | `medium` | `medium` | `medium` | `medium` | `8192` | `medium` | `4096` | `4096` |
| `High` | `high` | `high` | `high` | `high` | `-1` (dynamic) | `high` | `16384` | `16384` |
| `XHigh` | `xhigh` | `xhigh` | `high` | `xhigh` | `-1` | `high` | `16384` | `16384` |
| `Max` | `max` | `xhigh` | `high` | `max` | `-1` | `high` | `16384` | `16384` |

OpenAI documents that not every reasoning model supports every value; PolyPrompt sends the mapped value and lets the model reject it. Overrides on the value object: `OpenAiValue` (Chat Completions), `GeminiThinkingBudget`, `OllamaThink`, `AnthropicEffort`, `CohereThinkingBudget`, and `BedrockThinkingBudget`; Responses clients use `ReasoningEffortValue`.

### Reasoning output and continuation state

Reasoning stays out of `Text`: streamed chunks carry `ReasoningText`, responses carry `Reasoning`. `ToAssistantMessage()` carries native continuation state in `ProviderState` (encrypted reasoning, thinking signatures, redacted blocks, thought signatures, tool plans). Preserve the returned message unchanged and persist conversations with `PolyPromptJsonContext.Default.ListChatMessage`.

State is a tagged envelope, `{ "protocol", "provider", "scope", "value" }`. A client replays only state with its own protocol, provider, and endpoint; anything else falls back to the plain message, so state never leaks across providers. State written by 3.x (including xAI's untagged output arrays) is still read.

### Citations

`Citations` is filled when the provider returns citations (hosted search tools, or documents with citations enabled); PolyPrompt never enables them implicitly. `TextStart` and `TextEnd` index the final `Text` in UTF-16 code units, end exclusive (Gemini's byte offsets are converted); positions inside the cited source are in `Source`; the provider's object is in `Raw`.

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

| Provider | Source of citations |
|---|---|
| OpenAI, Azure, xAI | `output_text` annotations (`url_citation`, `file_citation`, `container_file_citation`, `file_path`); xAI titles are citation numbers and are not used as `Title` |
| Anthropic | text-block `citations` (`char_location`, `page_location`, `content_block_location`, `search_result_location`, `web_search_result_location`); the span is the containing block |
| Gemini, Vertex AI | `groundingMetadata` supports joined to chunks, and `citationMetadata` sources |
| Cohere | `message.citations` (thinking-content citations excluded) |
| Bedrock | `citationsContent` blocks with document, search-result, or web locations (no span in the text) |
| OpenAI-compatible | `choices[].message.annotations` (`url_citation`) |
| Ollama, VoyageAI, TEI, TypeSafe | none; `Citations` is empty, never null |

Streaming responses fill `Citations` once the stream completes.

### Raw responses and token usage

`RawResponse` is the provider's complete response JSON (for streaming, the final response object when the provider sends one, as on the Responses API). It is returned to you only, never written to call details, logs, or telemetry. `Usage` is a `TokenUsage` with nullable `PromptTokens`, `CompletionTokens`, `TotalTokens`, `CachedPromptTokens`, `CacheCreationTokens`, and `ReasoningTokens`.

### Embeddings, reranking, classification, sparse embeddings

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;
using PolyPrompt.Options;

using CohereClient cohere = new CohereClient("co-...");

EmbeddingResponse vectors = await cohere.EmbedAsync(new List<string> { "alpha", "beta" }, new CohereEmbeddingOptions { InputType = "search_document" });
RerankResponse ranked = await cohere.RerankAsync("capital of France", new List<string> { "Berlin is in Germany.", "Paris is in France." },
    new RerankOptions { TopN = 1, ReturnDocuments = true });
ClassificationResponse labels = await cohere.ClassifyAsync("I love this product!", new CohereClassificationOptions
{
    Examples = new List<ClassificationExample> { new ClassificationExample("great", "positive"), new ClassificationExample("awful", "negative") }
});

using TeiClient tei = new TeiClient("http://localhost:8080");
SparseEmbeddingResponse sparse = await tei.EmbedSparseAsync("lexical search terms");

Console.WriteLine(vectors.Embeddings.Count + " vectors, best: " + ranked.Results[0].Document + ", label: " + labels.Classifications[0].Label);
```

Rerank results behave the same on every provider: sorted by score (ties by index), indices outside the input dropped, `TopN` applied natively or client-side, and `ReturnDocuments` attaching text from your list.

### Decision models

A decision model reads a state and answers typed questions with calibrated probabilities. Nothing is generated.

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

using TypeSafeClient jev = new TypeSafeClient("ts-...");

DecisionRequest request = new DecisionRequest
{
    State = "My order arrived broken and I want my money back today.",
    Questions =
    {
        DecisionQuestion.Choice("intent", "What does the customer want?",
            DecisionOption.Of("refund", "Wants money back"), DecisionOption.Of("billing"), DecisionOption.Of("other")),
        DecisionQuestion.Binary("urgent", "Does the customer need a response today?"),
        DecisionQuestion.Score("tone", "How upset is the customer?", "calm", "annoyed", "angry")
    }
};

DecisionResponse response = await jev.DecideAsync(request);
if (response.Success) Console.WriteLine(response.Choice("intent").Value + ", urgent p=" + response.Binary("urgent").Probability);

jev.MaxConcurrency = 8;   // the batch overload sends at most this many at once
List<DecisionResponse> batch = await jev.DecideAsync(new List<DecisionRequest> { request, request });
```

### Text generation, models, and connectivity

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

using OllamaClient ollama = new OllamaClient();

GenerationResponse generated = await ollama.GenerateAsync("Once upon a time");   // /api/generate, prompt sent unchanged

await foreach (ModelInformation model in ollama.ListModelsAsync()) Console.WriteLine(model.Name + " " + model.SizeBytes);
bool exists = await ollama.ModelExistsAsync("gemma3");                            // also matches gemma3:latest
ModelInformation? info = await ollama.GetModelInformationAsync("gemma3:4b");
await ollama.PullModelAsync("all-minilm", progress => { Console.WriteLine(progress.Status); return Task.CompletedTask; });

bool reachable = await ollama.ValidateConnectivityAsync();                        // false on HTTP error, timeout, or no server
```

### Call details, telemetry, cancellation

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

`RedactCredentials` (default true) replaces credential headers in recorded call details with `[redacted]`. Every client emits metrics and traces through the BCL `Meter` and `ActivitySource` named `PolyPrompt`, with no exporter dependency: one span per operation with GenAI attributes and the `polyprompt.capability` of the operation called, child spans per HTTP request, streaming, decision batch, and credential metrics, and `polyprompt.reasoning.effort` when a reasoning field is sent. [TELEMETRY.md](TELEMETRY.md) has the catalog.

### Native AOT

PolyPrompt is AOT and trimming compatible. Persist and load models with the source-generated `PolyPromptJsonContext`, and register your own types for tool arguments and decision states with `Serializer.AddTypeInfoResolver`. See [AOT.md](AOT.md).

## For Coding Agents

A compact, exact reference. Read the [capability matrix](#capability-matrix) and [Which OpenAI client?](#which-openai-client) too.

- **Construct one client per provider:** `new OpenAiClient(key)`, `new OpenAiCompatibleClient(endpoint, key?)`, `new AzureOpenAiClient(endpoint, key | ICredentialProvider)`, `new XAiClient(key)`, `new AnthropicClient(key)`, `new GeminiClient(key)`, `new VertexAiClient(project, region, credential)`, `new BedrockClient(region, awsCredential)`, `new OllamaClient(endpoint?)`, `new OllamaCloudClient(key)`, `new CohereClient(key)`, `new VoyageAiClient(key)`, `new TeiClient(endpoint)`, `new TypeSafeClient(key)`. Optional last arguments: `endpoint`, then `ClientOptions`. Dispose clients.
- **Operations** (all on `ProviderClientBase`): `ChatAsync(prompt, options?)`, `ChatStreamingAsync`, `ToolChatAsync(ToolChatRequest)`, `ToolChatStreamingAsync`, `GenerateAsync`, `GenerateStreamingAsync`, `EmbedAsync(string | List<string>, options?)`, `EmbedSparseAsync`, `RerankAsync(query, documents, options?)`, `ClassifyAsync`, `DecideAsync(DecisionRequest | List<DecisionRequest>)`, `ListModelsAsync`, `ModelExistsAsync`, `GetModelInformationAsync`, `ValidateConnectivityAsync`.
- **Capability rule:** call `client.Supports(Capability.X)` before an operation you are not sure of; otherwise it throws `CapabilityNotSupportedException` synchronously.
- **Models:** set `client.Defaults.Completion.Model` (or `.Embedding`, `.Rerank`, `.Classification`, `.Decision`, `.SparseEmbedding`), or pass `Model` in the per-call options. There is no `client.Model`. `OpenAiCompatibleClient`, `AzureOpenAiClient`, `OllamaCloudClient`, and Bedrock completion have no default model.
- **Options rule:** pass the base options type (`CompletionOptions`, `EmbeddingOptions`, ...) or the client's own type (`OpenAiCompletionOptions` for `OpenAiClient`/`AzureOpenAiClient`, `OpenAiCompatibleCompletionOptions` for `OpenAiCompatibleClient`, `XAiCompletionOptions`, `AnthropicCompletionOptions`, `GeminiCompletionOptions` for Gemini and Vertex, `OllamaCompletionOptions`, `CohereCompletionOptions`, ...). Another provider's type throws `ArgumentException`.
- **OpenAI choice:** OpenAI itself → `OpenAiClient` (Responses). vLLM, Ollama `/v1`, LM Studio, gateways → `OpenAiCompatibleClient`.
- **Errors:** HTTP failures return `Success = false` with `StatusCode` and `Error`; they do not throw. Streams: enumerate fully, then check `State == StreamingState.Completed`.
- **Continuation state:** after a tool-chat turn, append `response.ToAssistantMessage()` unchanged, then one `ChatMessage.ToolResult(call.Id, call.Name, result)` per call. Do not copy only text and tool calls; `ProviderState` carries reasoning and signatures.
- **Results:** `Text`, `Reasoning`, `ToolCalls`, `Usage`, `FinishReason`, `Citations` (never null), `RawResponse`, `AppliedReasoningEffort`.

## API Reference

### Common members

| Member | On | Description |
|---|---|---|
| `Capabilities`, `Supports(Capability)`, `ProviderName` | provider clients | What the client supports, and its display name |
| `Defaults` | provider clients | Sectioned defaults (`ClientDefaults` through the base type) |
| `SendDefaultMaxTokens`, `DefaultMaxTokens` | provider clients | Output-limit policy (default true, false on Ollama) |
| `MaxConcurrency` | provider clients | Decision batch concurrency (1..64, default 4) |
| `Endpoint`, `ApiKey`, `ValidateConnectivityAsync` | HTTP clients | Transport details and probe |
| `TimeoutMs`, `CallDetails`, `MaxCallDetails`, `ClearCallDetails`, `CallDetailCapture`, `RedactCredentials` | every client | Timeout and call recording |

### Default models

<!-- default-models:start -->
| Client | Section | Default model |
|---|---|---|
| `OpenAiClient` | Completion | `gpt-6.1-sol` |
| `OpenAiClient` | Embedding | `text-embedding-3-small` |
| `OpenAiCompatibleClient` | Completion | none |
| `OpenAiCompatibleClient` | Embedding | none |
| `AzureOpenAiClient` | Completion | none |
| `AzureOpenAiClient` | Embedding | none |
| `XAiClient` | Completion | `grok-4.7` |
| `AnthropicClient` | Completion | `claude-sonnet-5-5` |
| `GeminiClient` | Completion | `gemini-3.8-flash` |
| `GeminiClient` | Embedding | `gemini-embedding-001` |
| `VertexAiClient` | Completion | `gemini-3.8-flash` |
| `VertexAiClient` | Embedding | `gemini-embedding-001` |
| `BedrockClient` | Completion | none |
| `BedrockClient` | Embedding | `amazon.titan-embed-text-v2:0` |
| `BedrockClient` | Rerank | `cohere.rerank-v3-5:0` |
| `OllamaClient` | Completion | `gemma3:4b` |
| `OllamaClient` | Embedding | `all-minilm` |
| `OllamaCloudClient` | Completion | none |
| `OllamaCloudClient` | Embedding | none |
| `CohereClient` | Completion | `command-a-plus-05-2026` |
| `CohereClient` | Embedding | `embed-v4.0` |
| `CohereClient` | Rerank | `rerank-v5.0-fast` |
| `CohereClient` | Classification | none |
| `VoyageAiClient` | Embedding | `voyage-3.5` |
| `VoyageAiClient` | Rerank | `rerank-3` |
| `TeiClient` | Embedding | none |
| `TeiClient` | SparseEmbedding | none |
| `TeiClient` | Rerank | none |
| `TeiClient` | Classification | none |
| `TypeSafeClient` | Decision | `jev-latest` |
<!-- default-models:end -->

Chat defaults are each provider's balanced, generally available model. Embedding defaults are kept from 3.x wherever the model is still live, because changing one silently changes vector spaces; newer embedding models (for example `gemini-embedding-2`, `voyage-4`, `embed-v5.0-pro`) are recommendations. "none" means a call without a model throws `InvalidOperationException` naming the section to set. TEI serves whatever model the server hosts, and Cohere picks the classification model.

### Options classes

| Capability | Base | Provider types |
|---|---|---|
| Completion | `CompletionOptions` | `ResponsesApiCompletionOptions` → `OpenAiCompletionOptions`, `XAiCompletionOptions`; `OpenAiCompatibleCompletionOptions`, `AnthropicCompletionOptions`, `GeminiCompletionOptions`, `OllamaCompletionOptions`, `CohereCompletionOptions` |
| Embedding | `EmbeddingOptions` | `OpenAiEmbeddingOptions` (OpenAI, compatible, Azure), `GeminiEmbeddingOptions`, `VertexAiEmbeddingOptions`, `BedrockEmbeddingOptions`, `OllamaEmbeddingOptions`, `CohereEmbeddingOptions`, `VoyageAiEmbeddingOptions`, `TeiEmbeddingOptions` |
| Sparse embedding | `SparseEmbeddingOptions` | `TeiSparseEmbeddingOptions` |
| Rerank | `RerankOptions` | `BedrockRerankOptions`, `CohereRerankOptions`, `VoyageAiRerankOptions`, `TeiRerankOptions` |
| Classification | `ClassificationOptions` | `CohereClassificationOptions`, `TeiClassificationOptions` |
| Decision | `DecisionOptions` | |

## Console Samples

Every `*Console` shares `ConsoleApplicationBase` in `Console.Shared` and now holds one provider client. The chat-capable consoles (Ollama, Ollama Cloud, OpenAI, Anthropic, Gemini, Cohere, xAI) use one `ConsoleSession` for streaming, conversation history, reasoning display, and automatic tool execution with the Mux built-in tools (`read_file`, `write_file`, `edit_file`, `list_directory`, `glob`, `grep`, `run_process`, `web_retrieve`, task planning, and more). `OpenAIConsole` drives `OpenAiClient` (Responses) by default, and `OpenAiCompatibleClient` with `--compatible <url>`:

```bash
dotnet run --project src/OpenAIConsole -f net10.0
dotnet run --project src/OpenAIConsole -f net10.0 -- --compatible http://localhost:8000/v1
dotnet run --project src/XAiConsole -f net10.0
```

| Command | Behavior |
|---|---|
| `ch/chat`, `tc/toolchat` | Chat with automatic tools and persistent history |
| `gen`, `gens` | Generate text (non-streaming, streaming) |
| `em`, `emb`, `rr`, `cl`, `sp` | Embed, batch embed, rerank, classify, sparse embed (where supported) |
| `mod`, `val`, `settings` | List models, validate connectivity (and print `Capabilities`), show settings |
| `tools on/off`, `pwd`, `cd PATH`, `reset`, `reasoning LEVEL` | Tool, directory, history, and reasoning controls |

Automatic tools run with the console user's permissions; the loop is bounded to 12 model rounds and 64 tool calls per round. `Console.Tools` is a companion JIT process copied into each console's `tools/` directory, so the library and `AotConsole` never load Mux. `AotConsole --provider ollama|ollamacloud|openai|openai-compatible|anthropic|gemini|xai` is the native console.

## Project Structure

```
PolyPrompt/
|-- src/
|   |-- PolyPrompt/              # Core library (NuGet package)
|   |   |-- Auth/                # SigV4 signing and OAuth / AWS credential providers
|   |   |-- Clients/
|   |   |   |-- Base/            # ClientBase, HttpClientBase, ProviderClientBase, CliAgentClientBase, ClientOptions, ClientDefaults
|   |   |   |-- Responses/       # ResponsesApiClientBase and the shared Responses engine
|   |   |   `-- <Provider>/      # One folder per provider or harness
|   |   |-- Helpers/             # Serializer, PolyPromptJsonContext, continuation state, citation normalization
|   |   |-- Telemetry/           # Meter, ActivitySource, and stable names
|   |   |-- Wire/                # AWS event-stream decoder
|   |   |-- Models/              # Requests, responses, Capability, Citation, extensible values
|   |   `-- Options/             # Provider-specific options
|   |-- *Console/                # Provider and harness consoles
|   |-- Console.Shared/, Console.Tools/, AgentConsole.Shared/
|   |-- Test.Shared/             # Touchstone test descriptors and the local fixture server
|   |-- Test.Automated/, Test.Xunit/, Test.Nunit/   # Runners over Test.Shared
|   |-- Test.Console/            # Console end-to-end tests
|   |-- Test.Docs/               # Compiles every C# snippet in the docs
|   `-- Test.Aot/                # Native AOT verification
|-- postman/                     # Contract specs, mocks, and simulations (OpenAI Responses, xAI, Ollama Cloud)
`-- llms.txt                     # Index for coding agents
```

## Building and Testing

```bash
dotnet build src/PolyPrompt.sln -c Release
dotnet run --project src/Test.Automated -c Release -f net8.0
dotnet run --project src/Test.Automated -c Release -f net10.0
dotnet test src/Test.Xunit -c Release
dotnet test src/Test.Nunit -c Release
dotnet test src/Test.Docs -c Release
dotnet test src/Test.Console -c Release
src/Test.Aot/verify-aot.sh
```

The local suite runs against an in-process fixture server and a fake agent CLI (macOS or Linux) and needs no credentials. It includes contract tests that parse this README's capability matrix and default-model table and compare them to the code, and a behavior contract that calls every unsupported operation on every client.

The saved Postman simulations exercise real Mux tool rounds through local contract fixtures:

```bash
postman simulation run postman/simulations/openai-responses.sim.yaml --port 4913 --no-history
POLYPROMPT_OPENAI_RESPONSES_SIMULATION=http://127.0.0.1:4913/openai-responses dotnet test src/Test.Console -c Release
# Also: postman/simulations/xai.sim.yaml (POLYPROMPT_XAI_SIMULATION) and ollamacloud.sim.yaml (POLYPROMPT_CONSOLE_SIMULATION)
```

The live suite runs against one provider at a time; cases for unsupported capabilities are skipped:

```bash
dotnet run --project src/Test.Automated -- --openai-key sk-... --openai-model gpt-6.1-sol
dotnet run --project src/Test.Automated -- --anthropic-key sk-ant-... --anthropic-model claude-sonnet-5-5
dotnet run --project src/Test.Automated -- --ollama-endpoint http://localhost:11434 --ollama-model gpt-oss:20b --ollama-embedding-model all-minilm
dotnet run --project src/Test.Automated -- --azure-endpoint https://my.openai.azure.com --azure-key ... --azure-model my-deployment
dotnet run --project src/Test.Automated -- --bedrock-region us-east-1 --bedrock-model anthropic.claude-sonnet-5-5
```

The same settings come from `POLYPROMPT_TEST_*` environment variables (for example `POLYPROMPT_TEST_OPENAI_API_KEY` and `POLYPROMPT_TEST_OPENAI_MODEL`); run `Test.Automated --help` for the list. Harness live suites are opt-in: `POLYPROMPT_TEST_CODEX=1`, `POLYPROMPT_TEST_CLAUDE_CODE=1`, and so on.

## Upgrading from 3.x

See [MIGRATION_V3_TO_V4.md](MIGRATION_V3_TO_V4.md) for the client mapping, every rename in one table, the behavior changes, and a checklist.

## Issues and Discussions

Bugs and feature requests: https://github.com/jchristn/PolyPrompt/issues. Questions: https://github.com/jchristn/PolyPrompt/discussions.

## License

PolyPrompt is available under the [MIT License](LICENSE.md).
