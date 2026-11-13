# PolyPrompt 4.0 Plan

> **Status:** implemented in 4.0.0 (2026-11-12). See section 16 for the as-built record.
> **Baseline:** `main` @ `03b221a` (3.7.0).
> **Target:** PolyPrompt **4.0.0** (major; breaking).
> **Scope:** the whole product: library API, OpenAI Responses support, documentation (README, XML docs, STREAMING, TELEMETRY, AOT, migration guide, CHANGELOG), samples, Postman fixtures, tests, packaging, and release.
> **Origin:** this plan started as the OpenAI Responses fix. Responses support is still the trigger for 4.0 and is covered in full in section 6.

---

## 1. Why 4.0

Two problems, one release.

**1. OpenAI is stuck on Chat Completions.** `OpenAiCompletionClient` only speaks `/v1/chat/completions`. OpenAI's current reasoning and codex-tuned models are served only (or best) through the Responses API (`/v1/responses`). Chat Completions also discards the model's reasoning after every call, so in a tool loop the model re-thinks from scratch at each step. PolyPrompt already has a complete Responses implementation, but it is written as part of the xAI client.

**2. One client per capability is the wrong unit.** 3.0 split every provider into one client per capability (`OpenAiCompletionClient`, `OpenAiEmbeddingClient`, `OpenAiModelClient`, ...). Using one provider for chat, embeddings, and model listing means three objects, three copies of the key, endpoint, `HttpClient`, and logging setup, and three `Defaults` objects. Other SDKs, and developers, think in providers: "the OpenAI client." Most code against PolyPrompt is now written by coding agents that read the documentation, so an accurate capability matrix and XML docs are a better guard than splitting the type system.

4.0 goes back to **one client per provider** (`OpenAiClient`, `AnthropicClient`, ...), with every operation on every client. Unsupported operations throw a specific, documented exception. Tests keep the documentation and the behavior in lockstep. And `OpenAiClient` speaks the Responses API.

## 2. Design principles

1. **One client per provider.** `new {Provider}Client(...)` is the only thing a developer constructs. Every model-API client exposes the same full set of operations.
2. **The docs are the contract, and tests enforce them.** A capability matrix in the README, `Capabilities` on every client, and per-client XML docs say what works. Tests fail the build if any of the three disagree with each other or with actual behavior (section 9.2).
3. **Unsupported means a loud, specific exception.** `CapabilityNotSupportedException` names the provider and capability and points to the matrix. `client.Supports(...)` lets code check first.
4. **One wire protocol per client, so no mode switches.** `OpenAiClient` speaks Responses. `OpenAiCompatibleClient` speaks Chat Completions for vLLM, Ollama `/v1`, LM Studio, gateways, and the like. No option is ever valid in one mode and ignored or rejected in another.
5. **No casts.** Every documented feature is reachable from the declared return type.
6. **Provider-agnostic code is first-class.** One base type (`ProviderClientBase`) declares every operation, so code written against it runs on any provider and can branch on `Supports`.
7. **Mistakes fail early with guidance.** Construction validates arguments (for example, a URL passed as an API key). Passing another provider's options type throws. A 404 on `/responses` suggests `OpenAiCompatibleClient`.
8. **Extensible values over closed enums.** Provider settings with a fixed set of values use string-backed value types, so new provider values work without a PolyPrompt release.
9. **Shared protocol code lives once.** The Responses engine is shared by `OpenAiClient` and `XAiClient`. Nothing under `Clients/OpenAi/` or the shared layer references an `XAi*` type.

## 3. The developer experience

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

using var openai = new OpenAiClient(Environment.GetEnvironmentVariable("OPENAI_API_KEY")!);
openai.Defaults.Completion.Model = "gpt-6-astra";   // the default is gpt-6.1-sol (5.3)
openai.Defaults.Embedding.Model = "text-embedding-3-large";

ToolChatResponse reply = await openai.ToolChatAsync(request, messages);   // Responses API, reasoning carried in ProviderState
EmbeddingResponse vec   = await openai.EmbedAsync("some text");
await foreach (ModelInformation m in openai.ListModelsAsync()) Console.WriteLine(m.Name);

using var local = new OpenAiCompatibleClient("http://localhost:8000/v1");  // vLLM, LM Studio, gateways
using var claude = new AnthropicClient(anthropicKey);
if (!claude.Supports(Capability.Embed)) { /* fall back */ }
await claude.EmbedAsync("x");   // throws CapabilityNotSupportedException: "Anthropic does not support Embed. See the capability matrix: ..."

// Provider-agnostic
static Task<ToolChatResponse> Ask(ProviderClientBase client, ToolChatRequest r, List<ChatMessage> m) => client.ToolChatAsync(r, m);
```

## 4. Library design

### 4.1 Type hierarchy

```
ClientBase                          logging, TimeoutMs, CallDetails, capture/redaction, telemetry, disposal (unchanged role)
├── HttpClientBase                  HTTP transport, per-request credentials, Endpoint, ApiKey (unchanged role)
│   └── ProviderClientBase          NEW: every model-API operation, Capabilities, Supports, Defaults
│       ├── ResponsesApiClientBase  NEW: shared Responses engine + native Responses operations
│       │   ├── OpenAiClient
│       │   └── XAiClient
│       ├── OpenAiCompatibleClient
│       ├── AzureOpenAiClient       (base chosen per 6.6)
│       ├── AnthropicClient, GeminiClient, VertexAiClient, BedrockClient
│       ├── OllamaClient, OllamaCloudClient
│       └── CohereClient, VoyageAiClient, TeiClient, TypeSafeClient
└── CliAgentClientBase              agent harnesses (unchanged role; clients renamed, 4.9)
```

Removed: `CompletionClientBase`, `EmbeddingClientBase`, `SparseEmbeddingClientBase`, `RerankClientBase`, `ClassificationClientBase`, `DecisionClientBase`, `ModelClientBase`, and all 39 capability clients. Their logic moves into the provider clients. The protected helpers that concrete clients rely on (`ExecutePostAsync`, `ExecuteStreamingAsync`, the `Wrap*WithTiming` helpers, `Pick`/`PickRef`, `ResolvedCompletion`, `RequiresMaxTokens`, `SendDefaultMaxTokens`) move to `ProviderClientBase` and keep their behavior.

### 4.2 The operation surface on `ProviderClientBase`

All operations are `virtual`. The base implementation throws `CapabilityNotSupportedException`, and each provider overrides what it supports.

| Capability flag | Operations |
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

Signatures, request types, and response types are the 3.x ones, so code inside a call site changes only in which object it calls. Provider-specific operations stay on the provider client and are not on the base:
- the Responses native operations (6.5);
- Ollama `LoadModelAsync`/`UnloadModelAsync` and model pull;
- xAI `CompactConversationAsync`.

`ValidateConnectivityAsync` probes the provider's cheapest authenticated route, as each capability client does today. Where 3.x clients for one provider probed different routes, use the models route.

### 4.3 Capabilities

- `[Flags] enum Capability { None = 0, Chat, ToolChat, Generate, Embed, SparseEmbed, Rerank, Classify, Decide, Models }`.
- `ProviderClientBase.Capabilities` is an abstract get-only property. Each client returns a constant, so the value is AOT safe and needs no reflection.
- `Supports(Capability c)` returns true only when every requested flag is supported.
- `Capabilities` reports provider-level support. Model-level limits (a model without tool support, for example) are not represented and are documented as such.
- **Streaming has no separate flag (verified).** Audit of 3.7.0, 2026-11-12:
  - `CompletionClientBase` declares all six completion forms (chat, tool chat, and generate, each streaming and non-streaming) as `abstract`. All seven completion implementations (OpenAI, xAI, Anthropic, Gemini/Vertex, Bedrock, Ollama/Ollama Cloud, Cohere) implement every form.
  - Every streaming form uses the provider's real streaming transport through `ExecuteStreamingAsync`: SSE, Gemini's `:streamGenerateContent?alt=sse`, Bedrock `converse-stream`, or Ollama NDJSON. None synthesizes a stream from a non-streaming call.
  - No PolyPrompt client throws `NotSupportedException` today.
  - Embed, sparse embed, rerank, classify, decide, and models have no streaming form in PolyPrompt or in any provider's API, so there is nothing to flag.
  - Rule for 4.0: if a future provider supports an operation but not its streaming form, add a `Streaming` capability flag then. Never fake a stream.

### 4.4 `CapabilityNotSupportedException`

```csharp
public sealed class CapabilityNotSupportedException : NotSupportedException
{
    public string Provider { get; }          // "Anthropic"
    public Capability Capability { get; }    // Capability.Embed
    public string Operation { get; }         // "EmbedAsync"
}
```

- **Message:** `"Anthropic does not support Embed (EmbedAsync). Check client.Supports(Capability.Embed) first, or see the capability matrix: https://github.com/jchristn/Polyprompt#capability-matrix"`.
- **Thrown synchronously** from the call, before any network activity, and never wrapped in a response's `Error` field.
- **Telemetry:** no span or metric is recorded for an unsupported call, because nothing was attempted. Call details record nothing either.

### 4.5 Defaults and per-call options

- **Grouped defaults.** Each client has a get-only `Defaults` property of a provider-specific defaults type with one section per supported capability:
  ```csharp
  public sealed class OpenAiDefaults : ClientDefaults
  {
      public override OpenAiCompletionOptions Completion { get; } = new() { Model = "gpt-6.1-sol" };      // see 5.3
      public override OpenAiEmbeddingOptions Embedding { get; } = new() { Model = "text-embedding-3-small" };
  }
  ```
- **Unsupported sections don't exist at compile time.** `ClientDefaults` declares `virtual` nullable sections (`CompletionOptions? Completion`, `EmbeddingOptions? Embedding`, `RerankOptions? Rerank`, `ClassificationOptions? Classification`, `DecisionOptions? Decision`, `SparseEmbeddingOptions? SparseEmbedding`) returning null. A provider's defaults type overrides only the sections it supports, with covariant, non-null return types. So `claude.Defaults.Embedding` doesn't exist on `AnthropicDefaults`, yet agnostic code can still read `client.Defaults.Embedding` through `ClientDefaults` and get null.
- **The Model shorthand goes away.** In 3.x, `client.Model` meant `Defaults.Model`. With one client per provider it is ambiguous (chat model or embedding model?), so there is no top-level `Model`. Set `Defaults.Completion.Model`, `Defaults.Embedding.Model`, and so on.
- **Per-call options keep the 3.x rule.** Each operation still takes its capability's options (`CompletionOptions?`, `EmbeddingOptions?`, ...), and a non-null per-call value wins, then `Defaults`, then the provider default.
- **Wrong options type throws.** Each operation accepts the base options type or the client's own derived type. Passing another provider's derived type (for example `XAiCompletionOptions` to `OpenAiClient`) throws `ArgumentException`: *"XAiCompletionOptions cannot be used with OpenAiClient. Use OpenAiCompletionOptions or CompletionOptions."* In 3.x, provider-specific fields of the wrong provider were silently ignored.

### 4.6 Options classes

Options stay per capability and per provider, because their fields differ per operation. Changes from 3.x:

| Options class | 4.0 |
|---|---|
| `ResponsesApiCompletionOptions` | **New** base for Responses clients; the shared Responses fields (6.3) |
| `OpenAiCompletionOptions` | Now `: ResponsesApiCompletionOptions`; the shared Responses fields plus the verified OpenAI-only fields `Metadata`, `Background`, `Conversation`, `MaxToolCalls`, `TopLogprobs`, `PromptCacheOptions`, `ContextManagement`, `StreamOptions` (6.3) |
| `XAiCompletionOptions` | Now `: ResponsesApiCompletionOptions`; keeps its xAI-only fields (`TopK`, `MinP`, `MaxTurns`, `Guidelines`, `Logprobs`, `TopLogprobs`). The shared field names are unchanged, so most xAI option code still compiles. |
| `OpenAiCompatibleCompletionOptions` | **New name** for the 3.x `OpenAiCompletionOptions` (`FrequencyPenalty`, `PresencePenalty`, `Seed`, `Echo`, `Suffix`, `Logprobs`) |
| `OpenAiEmbeddingOptions` | Unchanged; used by `OpenAiClient`, `OpenAiCompatibleClient`, and `AzureOpenAiClient` |
| All other options classes | Unchanged names and fields |

**Extensible values.** Provider settings that are strings with a fixed set of values become `readonly struct` string-backed values, with static known values, implicit conversion from `string`, and a `JsonConverter`. For example:
- `ReasoningSummary.Auto`, `ReasoningSummary.Concise`, `ReasoningSummary.Detailed` (the full OpenAI set; xAI accepts the same three);
- `ServiceTier.Auto`, `ServiceTier.Default`, `ServiceTier.Flex`, `ServiceTier.Scale`, `ServiceTier.Priority`, `ServiceTier.Fast` (the union: OpenAI accepts all six; xAI accepts `auto`, `priority`, `fast`);
- `new ServiceTier("future-tier")` passes through.

OpenAI's response reports `priority` when `fast` was requested, so PolyPrompt never treats the returned tier as an echo of the requested one.

Each provider validates the values it knows and passes unknown values through. This covers `ReasoningSummary`, `ServiceTier`, and `ReasoningEffortOverride`'s replacement (4.7), plus any other fixed-value string setting found in the P0 audit. Gemini's `GeminiToolSchemaMode` is a PolyPrompt behavior switch, not a wire value, so it stays an `enum`.

### 4.7 Reasoning effort

- `ReasoningEffortLevel` is `Minimal, Low, Medium, High` in 3.7.0. 4.0 adds `None`, `XHigh`, and `Max`, so it becomes `None, Minimal, Low, Medium, High, XHigh, Max`. OpenAI's Responses API accepts exactly these seven values (verified, section 6.4).
- Each provider maps every level to its nearest supported wire value, using a table documented in the README. The Responses rows are verified:

  | Level | OpenAI | xAI | Other providers |
  |---|---|---|---|
  | `None` | `none` | omit `reasoning` | omit or disable reasoning, per each provider's existing 3.x mapping |
  | `Minimal` | `minimal` | `low` (3.x behavior) | existing 3.x mapping |
  | `Low` / `Medium` / `High` | same name | same name | existing 3.x mapping |
  | `XHigh` | `xhigh` | `xhigh` | nearest: `High`'s mapping |
  | `Max` | `max` | `xhigh` | nearest: `High`'s mapping |

  OpenAI documents that "not all reasoning models support every value". PolyPrompt sends the mapped value and lets the model reject an unsupported one; it does not keep a per-model table.
- The provider-specific `ReasoningEffortOverride` string (xAI today) is replaced by a `ReasoningEffortValue` extensible value on `ResponsesApiCompletionOptions` for callers who need an exact wire value.
- Every response reports the wire value actually sent in `AppliedReasoningEffort` (4.8) and in telemetry (`polyprompt.reasoning.effort`), so a downgrade is visible.

### 4.8 Results

The generic result types (`ChatResponse`, `ToolChatResponse`, `GenerationResponse`, the streaming responses, `EmbeddingResponse`, ...) are unchanged in shape, with these additions to `ResponseBase`:

| New member | Meaning |
|---|---|
| `JsonElement? RawResponse` | The provider's complete response JSON. For streaming calls, the final response object when the provider sends one (Responses `response.completed`), otherwise null. Returned to the caller only; never written to call details, logs, or telemetry, so 3.7's privacy defaults are unchanged. |
| `List<Citation> Citations` | Neutral citations (4.8.1), filled in where the provider returns them. Empty otherwise, never null. |
| `string? AppliedReasoningEffort` | The reasoning effort wire value sent, after mapping (4.7). Null when no reasoning field was sent. |

Responses-specific status folds into the existing fields:
- **`incomplete`** maps to `FinishReason` from `incomplete_details.reason` (verified values): `max_output_tokens` → `length`, `content_filter` → `content_filter`. `max_messages` and `steered` pass through as raw strings.
- **`failed` and `cancelled`** map to `Error`.

Generic callers need no Responses-only type.

**Usage.** The Responses usage object (verified) maps onto the existing `TokenUsage`:
- `input_tokens` → `PromptTokens`
- `output_tokens` → `CompletionTokens`
- `total_tokens` → `TotalTokens`
- `input_tokens_details.cached_tokens` → `CachedPromptTokens`
- `input_tokens_details.cache_write_tokens` → `CacheCreationTokens` (the field already exists for Anthropic)
- `output_tokens_details.reasoning_tokens` → `ReasoningTokens`

#### 4.8.1 Citations

Verified against each provider's official API reference on 2026-11-12 (sources in section 14). PolyPrompt normalizes all of them into one type:

```csharp
public sealed class Citation
{
    public CitationKind Kind { get; init; }        // Url, Document, File, SearchResult
    public string? Url { get; init; }
    public string? Title { get; init; }
    public string? CitedText { get; init; }        // the quoted source text, when the provider returns it
    public int? TextStart { get; init; }           // span in the response's Text, UTF-16 code units, end exclusive
    public int? TextEnd { get; init; }
    public CitationSourceLocation? Source { get; init; }  // where in the source: document/file id or index, char, page, or block range
    public JsonElement Raw { get; init; }          // the provider's original citation object
}
```

**Normalization rules:**
- `TextStart`/`TextEnd` always index the response's final `Text` (all text parts concatenated) in .NET `string` units: UTF-16 code units, end exclusive. Provider offsets are converted:
  - Gemini reports **bytes**, converted to UTF-16 indices;
  - offsets relative to one content part are shifted by that part's position in `Text`;
  - an inclusive end is made exclusive.
- When a provider gives no span in the response text, `TextStart`/`TextEnd` are null.
- Positions inside the **source** (Anthropic's char, page, and block locations; Bedrock's document locations) go in `Source`, never in `TextStart`/`TextEnd`.

**Per provider:**

| Provider | Where in the response | Fields mapped | Offsets | Streaming |
|---|---|---|---|---|
| OpenAI Responses (`OpenAiClient`, `AzureOpenAiClient`) | `output[].content[]` (`output_text`) `.annotations[]` | `url_citation` {`url`, `title`, `start_index`, `end_index`} → Url; `file_citation` {`file_id`, `filename`, `index`} → File; `container_file_citation` {`container_id`, `file_id`, `filename`, `start_index`, `end_index`} → File; `file_path` {`file_id`, `index`} → File | characters in the part | `response.output_text.annotation.added` |
| xAI Responses | same `annotations[]` path, `url_citation` {`url`, `title`, `start_index`, `end_index`} | → Url. `title` is the citation number (for example `"1"`), not a page title, so it is not used as `Title`. | characters, end exclusive; the span covers the inline `[[N]](url)` markdown | final response only (stream events unverified) |
| Anthropic | `content[]` text block `.citations[]` | `char_location`, `page_location`, `content_block_location` → Document with `Source` (document index and title, char, page, or block range); `search_result_location` → SearchResult; `web_search_result_location` {`url`, `title`, `cited_text`} → Url. `cited_text` → `CitedText`. The span is the containing text block's range in `Text`. | source ranges: chars 0-indexed, pages 1-indexed, ends exclusive | `content_block_delta` with `citations_delta` |
| Gemini / Vertex AI | `candidates[].groundingMetadata`: `groundingSupports[].segment` {`startIndex`, `endIndex`, `text`} joined to `groundingChunks[groundingChunkIndices[i]].web` {`uri`, `title`}; also `candidates[].citationMetadata.citationSources[]` {`startIndex`, `endIndex`, `uri`, `license`} | → Url (one citation per support/chunk pair, and one per citation source) | **bytes**, end exclusive | final chunk |
| Cohere v2 | `message.citations[]` {`start`, `end`, `text`, `sources[]`, `type`} | `sources[]` of type `document` → Document, `tool` → `Source` with tool id. `text` → `CitedText`. `THINKING_CONTENT` citations are dropped from `Citations` (they index reasoning, not `Text`) but stay in `RawResponse`. | characters in the message text | `citation-start`/`citation-end` |
| Bedrock Converse | `output.message.content[].citationsContent` {`content[]`, `citations[]`}; each citation {`title`, `source`, `sourceContent[]`, `location`} | → Document (or Url for `web` locations), with `Source` from `location` (`documentChar`, `documentPage`, `documentChunk`, `searchResultLocation`, `web`) | source ranges | `contentBlockDelta.delta.citation` |
| OpenAI-compatible Chat Completions | `choices[].message.annotations[]` {`type:"url_citation"`, `url_citation`:{`url`, `title`, `start_index`, `end_index`}} (nested, unlike Responses) | → Url | characters; `end_index` documented as "the last character" | final response only |
| Ollama, Ollama Cloud, VoyageAI, TEI, TypeSafe | none in the API | | | |

**Scope rules:**
- Generic streaming responses populate `Citations` once, on the final response, for every provider. Per-chunk citation events stay available through the native event stream (`ResponsesApiEvent`) and `RawResponse`; they are not added to the generic chunk types.
- Citations only appear when the provider produces them: hosted search tools (OpenAI and xAI `web_search`, xAI `x_search`, Anthropic web search, Gemini Google Search grounding), or documents with citations enabled (Anthropic `citations.enabled`, Bedrock document citations, Cohere documents). PolyPrompt does not enable them implicitly.
- Anthropic: citations on documents or search results cannot be combined with `output_config.format` (structured output); the API returns 400. Document this.
- Anthropic: `web_search_result_location.encrypted_index` must be passed back on later turns. It already round-trips through the 4.0 continuation state, because PolyPrompt replays Anthropic content blocks verbatim.

**Residual unknowns, settled in P0 from a recorded live payload** (each becomes a fixture-backed case):
- Whether "characters" means UTF-16 code units or code points for OpenAI, xAI, Anthropic, and Cohere. Verify with a payload containing non-BMP characters (emoji), and convert code points if needed.
- Whether OpenAI Chat Completions' `end_index` is inclusive.
- Whether xAI's raw Responses JSON has a top-level `citations` array (the SDK exposes one).
- Bedrock's inner location field names.
- Whether Gemini's newer `url_citation` annotation shape (shown in the Google Search guide) appears in `generateContent` responses. If it does, parse both shapes.

**Continuation state.** Every provider writes one tagged envelope, `{ "protocol": "<protocol>", "provider": "<provider>", "scope": "<endpoint>", "value": ... }`:
- Each client replays only state tagged with its own protocol, provider, and scope. Anything else falls back to plain message history, so cross-provider leaks are impossible by construction.
- **Backward compatibility:** 4.0 still reads every 3.x shape (xAI's untagged output array and the 3.x tagged envelopes) but writes only the 4.0 envelope. Persisted 3.x conversations (including Mux sessions) keep working.
- The special-case tag list in `ToolTurnState.IsTagged` is removed.

### 4.9 Constructors and `ClientOptions`

```csharp
public sealed class ClientOptions
{
    public LoggingModule? Logging { get; init; }
    public HttpClient? HttpClient { get; init; }                 // not disposed by the client when supplied
    public int? TimeoutMs { get; init; }
    public CallDetailCapture? CallDetailCapture { get; init; }
    public bool? RedactCredentials { get; init; }
}
```

| Client | Constructor |
|---|---|
| Keyed providers (`OpenAiClient`, `XAiClient`, `AnthropicClient`, `GeminiClient`, `OllamaCloudClient`, `CohereClient`, `VoyageAiClient`, `TypeSafeClient`) | `(string apiKey, string? endpoint = null, ClientOptions? options = null)` |
| `OpenAiCompatibleClient`, `TeiClient` | `(string endpoint, string? apiKey = null, ClientOptions? options = null)` |
| `OllamaClient` | `(string endpoint = "http://localhost:11434", string? apiKey = null, ClientOptions? options = null)` |
| `AzureOpenAiClient` | `(string endpoint, string apiKey, ClientOptions? options = null)` (sent as `api-key`), plus the existing Azure AD credential overload (sent as `Authorization: Bearer`, token scope `https://ai.azure.com/.default`). There is no `apiVersion` parameter, because the v1 API needs none (6.6). |
| `VertexAiClient` | `(string project, string region, ICredentialProvider credential, string? endpoint = null, ClientOptions? options = null)` |
| `BedrockClient` | `(string region, IAwsCredentialProvider credential, string? endpoint = null, ClientOptions? options = null)` |

- **Fail-fast validation.** A required `apiKey` that is blank, contains whitespace, or looks like a URL throws `ArgumentException`. For a URL, the message suggests the right client (for `OpenAiClient`: *"apiKey looks like a URL. For Chat Completions servers (vLLM, Ollama /v1, LM Studio, gateways) use OpenAiCompatibleClient."*). An `endpoint` that is not an absolute http(s) URL throws.
- **Key placement for `OpenAiCompatibleClient`.** Gateways and Azure's v1 Chat Completions route expect the key in different places. `OpenAiCompatibleClientOptions : ClientOptions` adds `AuthPlacement`:
  - `Bearer` (the default): `Authorization: Bearer <key>`;
  - `Header(name)`: for example `api-key` for Azure;
  - `Query(name)`: a query-string parameter.

  This replaces the hand-rolled placement that callers such as Mux implement today. Native clients keep their fixed schemes.
- **Endpoint normalization** keeps the 3.x rules per provider (for example, `/v1` appended or tolerated for OpenAI-family endpoints, a trailing `/v1` stripped for Ollama native).
- **One `HttpClient` per client** is shared by all of its operations. It is owned and disposed by the client unless supplied through `ClientOptions.HttpClient`.

### 4.10 Agent harness clients

They are renamed for consistency, stay on `CliAgentClientBase` (they are not model APIs and do not get the model operations), and are otherwise unchanged:

| 3.x | 4.0 |
|---|---|
| `ClaudeCodeAgentClient` | `ClaudeCodeClient` |
| `CodexAgentClient` | `CodexClient` |
| `GeminiCliAgentClient` | `GeminiCliClient` |
| `CursorAgentClient` | `CursorClient` |
| `MuxAgentClient` | `MuxClient` |
| `PiAgentClient` | `PiClient` |
| `OpenCodeAgentClient` | `OpenCodeClient` |

Their constructors adopt `ClientOptions` for logging (`(string executable = Default, ClientOptions? options = null)`).

### 4.11 Telemetry and call details

- Metric and span names, provider labels, and operation names are unchanged.
- The `capability` attribute now comes from the operation called, not the client type, so it has the same values as in 3.x.
- One client now produces call details for every operation, all on that client's `CallDetails`.
- New attribute: `polyprompt.reasoning.effort` (the applied value).
- Unsupported-capability calls record nothing (4.4).

## 5. Provider mapping and capability matrix

### 5.1 3.x to 4.0 client mapping

| Provider | 3.x clients | 4.0 client |
|---|---|---|
| OpenAI | `OpenAiCompletionClient` (against api.openai.com), `OpenAiEmbeddingClient`, `OpenAiModelClient` | `OpenAiClient` |
| OpenAI-compatible servers | `OpenAiCompletionClient` (against any other server), `OpenAiEmbeddingClient`, `OpenAiModelClient` | `OpenAiCompatibleClient` |
| Azure OpenAI | `AzureOpenAiCompletionClient`, `AzureOpenAiEmbeddingClient`, `AzureOpenAiModelClient` | `AzureOpenAiClient` |
| xAI | `XAiCompletionClient`, `XAiEmbeddingClient`, `XAiModelClient` | `XAiClient` |
| Anthropic | `AnthropicCompletionClient`, `AnthropicModelClient` | `AnthropicClient` |
| Google Gemini | `GeminiCompletionClient`, `GeminiEmbeddingClient`, `GeminiModelClient` | `GeminiClient` |
| Google Vertex AI | `VertexAiCompletionClient`, `VertexAiEmbeddingClient` | `VertexAiClient` |
| AWS Bedrock | `BedrockCompletionClient`, `BedrockEmbeddingClient`, `BedrockRerankClient`, `BedrockModelClient` | `BedrockClient` |
| Ollama | `OllamaCompletionClient`, `OllamaEmbeddingClient`, `OllamaModelClient` | `OllamaClient` |
| Ollama Cloud | `OllamaCloudCompletionClient`, `OllamaCloudEmbeddingClient`, `OllamaCloudModelClient` | `OllamaCloudClient` |
| Cohere | `CohereCompletionClient`, `CohereEmbeddingClient`, `CohereRerankClient`, `CohereClassificationClient`, `CohereModelClient` | `CohereClient` |
| VoyageAI | `VoyageAiEmbeddingClient`, `VoyageAiRerankClient` | `VoyageAiClient` |
| Hugging Face TEI | `TeiEmbeddingClient`, `TeiSparseEmbeddingClient`, `TeiRerankClient`, `TeiClassificationClient`, `TeiModelClient` | `TeiClient` |
| TypeSafe (Jev, Ollaya) | `TypeSafeDecisionClient` | `TypeSafeClient` |

### 5.2 Capability matrix (initial; P0 audit confirms each cell)

| Client | Chat | ToolChat | Generate | Embed | SparseEmbed | Rerank | Classify | Decide | Models |
|---|---|---|---|---|---|---|---|---|---|
| `OpenAiClient` | ✅ | ✅ | ✅ | ✅ | | | | | ✅ |
| `OpenAiCompatibleClient` | ✅ | ✅ | ✅ | ✅ | | | | | ✅ |
| `AzureOpenAiClient` | ✅ | ✅ | ✅ | ✅ | | | | | ✅ |
| `XAiClient` | ✅ | ✅ | ✅ | ✅ | | | | | ✅ |
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

This table is copied into the README (section 8.1), where a test checks it against `Capabilities` (section 9.2).

Cells still to confirm in P0, each against the provider's current API before the flag is set:
- **`XAiClient` Embed:** 3.7.0 calls `/v1/embeddings`, but xAI's current models page lists no embedding model, and its embeddings guide returned 404 on 2026-11-12. If the endpoint is gone, clear the flag and remove the code. If it exists, keep it with no default model (5.3).
- **`CohereClient` Classify:** 3.7.0 calls `/v1/classify`, but Cohere's models page now says to classify with Embed models. Confirm that `/v1/classify` still works. If it does, keep the flag and document the recommendation. If not, clear it.
- **`AzureOpenAiClient` Embed and Models:** the v1 Responses and Chat Completions routes are verified, but `/openai/v1/embeddings` and `/openai/v1/models` were not shown in the pages read (6.6).
- **`OllamaCloudClient` Embed:** the hosted catalog decides which models exist. The flag stays set because the API supports it, and the README notes that embedding models may not be in the cloud catalog.

### 5.3 Default models

**Policy:**
1. **Chat models:** a default is the provider's recommended **balanced, generally available** model. Where a provider names both a flagship and a balanced model, pick the balanced one, because a default runs exactly when the caller didn't choose, and it shouldn't be the most expensive option. Never use preview models.
2. **Embedding models:** keep the 3.x defaults wherever the model is still live, even when a newer one exists. Changing an embedding default silently changes vector dimensions and spaces, which breaks stored indexes for anyone relying on the default. Newer embedding models are documented as recommendations, not made the default.
3. **No default when no single model is right**, for example when the catalog is per account, per region, or per server. Calling without a model throws `InvalidOperationException` before any request: *"Set client.Defaults.Completion.Model (or options.Model). See <provider model list URL>."*
4. **The README default-models table is checked by a contract test** (9.2) against the code, like the capability matrix.

**Defaults (verified against provider model pages on 2026-11-12; sources in section 14):**

| Client | Section | 3.7.0 default | 4.0 default | Reason |
|---|---|---|---|---|
| `OpenAiClient` | Completion | `gpt-4o-mini` (Chat Completions) | `gpt-6.1-sol` | OpenAI: "Choose GPT-6.1 Sol to balance intelligence and cost." Its flagship (`gpt-6-astra`) and small model (`gpt-6-luna`) are documented as alternatives. |
| `OpenAiClient` | Embedding | `text-embedding-3-small` | `text-embedding-3-small` | still current; unchanged (policy 2) |
| `OpenAiCompatibleClient` | Completion | `gpt-4o-mini` (inherited) | none | server-specific (policy 3) |
| `OpenAiCompatibleClient` | Embedding | `text-embedding-3-small` (inherited) | none | server-specific |
| `AzureOpenAiClient` | all | the deployment | none; the deployment name is required | as 3.x |
| `XAiClient` | Completion | `grok-4.7` | `grok-4.7` | xAI's recommended chat and code model |
| `XAiClient` | Embedding | none | none | see the 5.2 note |
| `AnthropicClient` | Completion | `claude-opus-4-8` | `claude-sonnet-5-5` | Anthropic's balanced model. `claude-opus-5-5` (flagship) and `claude-haiku-5-5` (small) are documented as alternatives. |
| `GeminiClient` | Completion | `gemini-2.5-flash` | `gemini-3.8-flash` ⚠ | the newest stable Flash. Google's models page labels both 3.6 and 3.8 Flash "Stable" and "previous-generation", and recommends "Gemini 4 Hydrogen" for new projects without publishing a Flash-tier ID for it. Re-check in P0, and switch to a Gemini 4 Flash-tier ID if one is GA by then. |
| `GeminiClient` | Embedding | `gemini-embedding-001` | `gemini-embedding-001` | still live; unchanged (policy 2). `gemini-embedding-2` (multimodal) is documented as the recommendation. |
| `VertexAiClient` | Completion | `gemini-2.5-flash` (inherited) | `gemini-3.8-flash` ⚠ | Gemini 3.8 Flash is GA on Vertex; confirm the exact ID string in P0 |
| `VertexAiClient` | Embedding | `text-embedding-005` | `text-embedding-005` | unchanged (policy 2), if still live. Vertex's current recommendation is `gemini-embedding-001`. If `text-embedding-005` is retired, switch and call it out in the migration guide. |
| `BedrockClient` | Completion | `anthropic.claude-3-5-sonnet-20240620-v1:0` | none | Model and inference-profile IDs vary by region and account (for example `anthropic.claude-sonnet-5-5` versus a regional profile), and the 3.x default is a retired generation (policy 3) |
| `BedrockClient` | Embedding | `amazon.titan-embed-text-v2:0` | `amazon.titan-embed-text-v2:0` | still listed; unchanged |
| `BedrockClient` | Rerank | `cohere.rerank-v3-5:0` | `cohere.rerank-v3-5:0` | still listed; `amazon.rerank-v1:0` is no longer in Bedrock's catalog |
| `OllamaClient` | Completion | `gemma3:4b` | `gemma3:4b` | a small local model; unchanged |
| `OllamaClient` | Embedding | `all-minilm` | `all-minilm` | unchanged (policy 2) |
| `OllamaCloudClient` | Completion / Embedding | `gemma3:4b` / `all-minilm` (inherited from local, usually wrong for the cloud) | none | the hosted catalog changes (for example `gpt-oss:120b`, `glm-5.3`, `kimi-k3`, `deepseek-v4.1-flash`); policy 3. Fixes a 3.x bug where the cloud client inherited local defaults. |
| `CohereClient` | Completion | `command-a-03-2025` | `command-a-plus-05-2026` ⚠ | Cohere's newest chat model; confirm it is GA in P0, otherwise keep `command-a-03-2025` (still live) |
| `CohereClient` | Embedding | `embed-v4.0` | `embed-v4.0` | still live; unchanged (policy 2). `embed-v5.0-pro`/`-fast` documented. |
| `CohereClient` | Rerank | `rerank-v3.5` | `rerank-v5.0-fast` | current generation, the cheaper of the two v5 models; rerank defaults don't affect stored data |
| `CohereClient` | Classification | none | none | Cohere picks; unchanged |
| `VoyageAiClient` | Embedding | `voyage-3.5` | `voyage-3.5` | unchanged (policy 2); `voyage-4` documented |
| `VoyageAiClient` | Rerank | `rerank-2.5` | `rerank-3` | `rerank-2.5` is now legacy |
| `TeiClient` | all | the hosted model | the hosted model | unchanged |
| `TypeSafeClient` | Decision | `jev-latest` | `jev-latest` | unchanged |

Every changed default is listed under Breaking in the CHANGELOG and in the migration guide.

## 6. OpenAI Responses support

### 6.1 The two OpenAI clients

- **`OpenAiClient`** talks to OpenAI the service, or any server that implements `/v1/responses`. Chat, tool chat, and generate all use Responses. Embeddings and models use `/v1/embeddings` and `/v1/models`, as in 3.x.
- **`OpenAiCompatibleClient`** talks to servers that implement OpenAI's older Chat Completions API (vLLM, Ollama `/v1`, LM Studio, OpenRouter, Gemini's OpenAI endpoint, gateways). It is the 3.x `OpenAiCompletionClient` behavior, including legacy `/v1/completions` generation, the reasoning extensions, and Gemini thought-signature replay. Callers who want Chat Completions against OpenAI itself point it at `https://api.openai.com/v1`.

**Recovering from the wrong choice:** when `OpenAiClient` gets a 404 or 405 on `/responses`, the response's `Error` includes *"This server may not implement the OpenAI Responses API. Use OpenAiCompatibleClient for Chat Completions servers."*

### 6.2 `ResponsesApiClientBase` and the shared engine

The Responses implementation moves out of the 3.x xAI client (`XAiCompletionClient.cs`, `.Requests.cs`, `.Parsing.cs`, `.Streaming.cs`) into `Clients/Responses/`:

| New type | From | Purpose |
|---|---|---|
| `ResponsesApiClientBase` (public, abstract) | xAI client shell | Overrides chat, tool chat, and generate (streaming and not) through the engine; hosts native operations (6.5) |
| `ResponsesApiDialect` (internal) | new | Per-provider data: display name, log header, telemetry provider, auth, effort mapping and allowed values, service tiers, hosted tool types, provider-only body fields hook, max tools, error-message wording |
| `ResponsesApiRequestBuilder` (internal) | `XAiCompletionClient.Requests.cs` | Body, input projection, reserved fields |
| `ResponsesApiParser` (internal) | `XAiCompletionClient.Parsing.cs` | Non-streaming parse, usage, errors, citations |
| `ResponsesApiStreamReader` (internal) | `XAiCompletionClient.Streaming.cs` | SSE events, chunk projection, tool-call assembly, terminal handling |
| `ResponsesApiJson` (internal) | `XAiProtocol` helpers | JSON helpers |

**Rules for the shared layer:**
- **No xAI dependency:** no code under `Clients/Responses/` or `Clients/OpenAi/` references an `XAi*` type, member, or option. Enforced by tests (9.2).
- **No vendor names in shared code:** vendor names in errors and logs come from the dialect.
- **Provider-only fields come from the subclass:** fields like xAI's `max_turns`, `top_k`, `min_p`, and `guidelines` are added through a dialect hook supplied by `XAiClient`, so the engine never names them.

### 6.3 Shared Responses options (`ResponsesApiCompletionOptions`)

The fields: `Store`, `PreviousResponseId`, `ParallelToolCalls`, `StrictTools`, `ReasoningEffortValue` (4.7), `ReasoningSummary`, `Include`, `FunctionTools`, `HostedTools`, `TextFormat`, `ToolChoice`, `ServiceTier`, `PromptCacheKey`, `User`, `SafetyIdentifier`, `AdditionalParameters`.

**`Store`.** OpenAI's API defaults `store` to `true`, keeping responses for at least 30 days (verified). PolyPrompt always sends `store` explicitly, `false` unless the caller sets `Store = true`, so its behavior never depends on the server default. The README says this next to `Store`, because developers coming from OpenAI's own SDKs will expect `true`. Stateless continuity comes from `include` always containing `reasoning.encrypted_content`, which OpenAI documents for exactly this case (`store:false` or zero data retention).

**Request fields PolyPrompt sends, and what it doesn't model.** The verified OpenAI request body has many more fields than the shared set.
- **Modeled** (typed on `OpenAiCompletionOptions`, section 6.4): `Metadata`, `Background`, `Conversation`, `MaxToolCalls`, `TopLogprobs`, `PromptCacheOptions`, `ContextManagement`, `StreamOptions`.
- **Not modeled**, sent through `AdditionalParameters` when needed:
  - `truncation` and `prompt_cache_retention`, both deprecated; `prompt_cache_options.ttl` replaces `prompt_cache_retention`;
  - `prompt` (stored prompt templates);
  - `image`, `moderation`, `access_programs`.

### 6.4 Dialect differences (verified)

OpenAI values were checked against OpenAI's official OpenAPI spec (`openai/openai-openapi`, `main` branch, fetched 2026-11-12). xAI values are PolyPrompt 3.7.0's existing, tested behavior. Record each source and date in a code comment next to the dialect data.

| Concern | xAI | OpenAI |
|---|---|---|
| `reasoning.effort` | low, medium, high, xhigh | none, minimal, low, medium, high, xhigh, max (model dependent; default medium) |
| `reasoning.summary` | auto, concise, detailed | auto, concise, detailed |
| `service_tier` | auto, priority, fast | auto (default), default, flex, scale, priority, fast |
| Hosted and client-executed tool types | web_search, x_search, code_interpreter, file_search, mcp, image_generation, shell, tool_search | function, file_search, computer, computer_use_preview, web_search (plus dated and `_preview` variants), mcp, code_interpreter, programmatic_tool_calling, image_generation, local_shell, shell, custom, namespace, tool_search, apply_patch |
| Provider-only body fields | max_turns, top_k, min_p, guidelines, logprobs, top_logprobs | metadata, background, conversation, max_tool_calls, top_logprobs, prompt_cache_options, context_management, stream_options (deprecated fields excluded; see 6.3) |
| `include` values | as 3.7.0, plus `reasoning.encrypted_content` | file_search_call.results, web_search_call.results, web_search_call.action.sources, message.input_image.image_url, computer_call_output.output.image_url, code_interpreter_call.outputs, reasoning.encrypted_content, message.output_text.logprobs |
| Tool limit | 1000 | none client-side |
| Auth | bearer, key required | bearer, key required |

**Tool calls the generic API returns.**
- Generic `ToolCalls` contain **function** calls only, as in 3.x.
- These are output item types the caller must execute: `custom` (freeform input), `shell`, `local_shell`, `apply_patch`, and `computer`.
- Hosted tool calls (`web_search`, `file_search`, `code_interpreter`, `mcp`, `image_generation`) run on the provider and need no caller action.
- Both kinds appear in `RawResponse` and the native event stream. Projecting client-executed non-function tools into the generic tool-call model is out of scope for 4.0; record it as a follow-up.

**Background mode.**
- `Background = true` requires `Store = true`. PolyPrompt throws `ArgumentException` locally when `Store` isn't set.
- The response then has status `queued` or `in_progress`. Poll it with `GetResponseAsync` and stop it with `CancelResponseAsync` (6.5).

### 6.5 Native Responses operations

These live on `ResponsesApiClientBase`, so both clients get them with identical signatures and neutral types:

- `Task<ResponsesApiResponse> CreateResponseAsync(JsonElement input, ResponsesApiCompletionOptions? options = null, CancellationToken token = default)`
- `Task<ResponsesApiStreamingResponse> CreateResponseStreamingAsync(JsonElement input, ...)`
- `Task<ResponsesApiResponse> ContinueResponseAsync(ResponsesApiResponse previous, JsonElement input, ResponsesApiCompletionOptions? options = null, ...)`: throws `InvalidOperationException` before sending when `previous` was not stored (*"This response was created with Store = false and cannot be continued by id. Set Store = true on the call that creates it, or resend the history."*)
- `Task<ResponsesApiResponse> GetResponseAsync(string responseId, ...)`
- `Task<ResponsesApiJsonResponse> DeleteResponseAsync(string responseId, ...)`
- `Task<ResponsesApiJsonResponse> ListResponseInputItemsAsync(string responseId, int limit = 20, string order = "asc", string? after = null, ...)`
- `Task<ResponsesApiJsonResponse> CompactConversationAsync(JsonElement input, string? model = null, ...)`: `POST /responses/compact`, verified on OpenAI and Azure and already implemented for xAI, so it lives on the base. OpenAI's request also accepts `previous_response_id`, `instructions`, `prompt_cache_key`, and `service_tier`; expose those as optional parameters. The response's `output` is reused verbatim as the next input, as with xAI today. Server-side compaction is set with `OpenAiCompletionOptions.ContextManagement` (`[{ type: "compaction", compact_threshold }]`) instead.

**OpenAI-only native operations** (on `OpenAiClient` and, where noted, `AzureOpenAiClient`; not on the base, because xAI support is unverified):
- `Task<ResponsesApiResponse> CancelResponseAsync(string responseId, ...)`: `POST /responses/{id}/cancel`, for background responses (OpenAI verified; Azure P0).
- `Task<ResponsesApiJsonResponse> CountInputTokensAsync(JsonElement input, OpenAiCompletionOptions? options = null, ...)`: `POST /responses/input_tokens` (OpenAI verified; Azure P0).

If P0 confirms xAI supports either operation, move it to the base.

The neutral types:

| Type | Members |
|---|---|
| `ResponsesApiResponse : ToolChatResponse` | `Status`, `Stored` |
| `ResponsesApiJsonResponse : ResponseBase` | `Data` |
| `ResponsesApiEvent` | `Kind` (`ResponsesApiEventKind`), `Type` (raw string), `Data` |
| `ResponsesApiEventKind` | Mapped from the verified SSE event names: `Created`, `Queued`, `InProgress`, `OutputItemAdded`, `OutputItemDone`, `ContentPartAdded`, `ContentPartDone`, `TextDelta`, `TextDone`, `AnnotationAdded`, `RefusalDelta`, `RefusalDone`, `ReasoningSummaryPartAdded`, `ReasoningSummaryPartDone`, `ReasoningSummaryDelta`, `ReasoningSummaryDone`, `ReasoningDelta`, `ReasoningDone`, `FunctionCallArgumentsDelta`, `FunctionCallArgumentsDone`, `CustomToolInputDelta`, `CustomToolInputDone`, `HostedToolProgress` (all `response.web_search_call.*`, `file_search_call.*`, `code_interpreter_call*`, `image_generation_call.*` including `partial_image`, `mcp_call*`, `mcp_list_tools.*`, `shell_call_*` events), `Compacting` (`response.compaction.compacting`), `Completed`, `Incomplete`, `Failed`, `Error`, `Other`. `Type` always carries the raw name. Names in the spec that belong to the Realtime/WebSocket API (`response.create`, `response.done`, `response.audio.*`, `response.steer*`, `response.inject*`) are not expected over SSE and map to `Other` if seen. |
| `ResponsesApiStreamingResponse : StreamingResponseBase` | `IAsyncEnumerable<ResponsesApiEvent> Events`, `ResponsesApiResponse? Response` |
| `ResponsesApiHostedTool` | `Type`, `Parameters`, `Create(type, parameters)` |

**Removed:** `XAiResponse`, `XAiJsonResponse`, `XAiResponseEvent`, `XAiStreamingResponse`, `XAiHostedTool`. They are not kept as obsolete aliases, because the methods no longer return them, so aliases could not keep old code compiling.

**Typed tool factories:**
Each factory sits on the class matching where the tool type is verified:
- `ResponsesApiTools`: types both providers accept: `WebSearch()`, `FileSearch(...)`, `CodeInterpreter()`, `Mcp(...)`, `ImageGeneration()`, `Shell()`, `ToolSearch()`.
- `XAiTools`: `XSearch(...)`.
- `OpenAiTools`: `Computer(...)`, `LocalShell()`, `ApplyPatch()`, `Custom(name, description, format?)`, `Namespace(...)`, `ProgrammaticToolCalling(...)`.

Dated variants (`web_search_2025_08_26`, `web_search_preview`, `computer_use_preview`) go through `Create(type, parameters)`.

`ResponsesApiHostedTool.Create(type, parameters)` stays as the escape hatch.

### 6.6 Azure OpenAI

**Verified on Microsoft Learn on 2026-11-12** (sources in section 14). The design is settled: **`AzureOpenAiClient : ResponsesApiClientBase`**.

**Routing and auth:**
- Requests go to `POST https://{resource}.openai.azure.com/openai/v1/responses`; `https://{resource}.services.ai.azure.com/openai/v1/` is also accepted. No `api-version` is needed: "`api-version` is no longer a required parameter with the v1 GA API".
- The **deployment name goes in the body as `model`**, not in the path.
- Preview features are enabled with per-feature headers or `alpha` paths. PolyPrompt passes them through `ClientOptions`/`AdditionalParameters` and doesn't model them.
- **Auth:** `api-key: <key>`, or `Authorization: Bearer <token>` for Entra ID (scope `https://ai.azure.com/.default`, role Cognitive Services OpenAI User). Microsoft recommends Entra ID.

**Feature support:**
- **Encrypted reasoning:** `store=false` with `include=["reasoning.encrypted_content"]` is supported. Azure returns an `invalid_encrypted_content` error when items are altered or reordered, which also happens behind Azure API Management. Surface that error text with a hint to check proxies.
- **Native operations:** `/responses/compact` and server-side `context_management` compaction (with `store=false`), `GET`/`DELETE /responses/{id}`, and `/input_items` are supported. Stored data is kept for 30 days by default. Background mode requires `store=true`.
- **Hosted tools:**
  - confirmed: `code_interpreter` (with `container: {type: "auto"}`), `mcp` (remote servers; no mTLS or service tags), `image_generation` (multi-turn editing with streaming is unsupported), and computer use through the `computer-use-preview` model;
  - not confirmed: `web_search` and `file_search`, which weren't on the page read.

  PolyPrompt does not restrict tools client-side for Azure. The README lists the confirmed set, and the service rejects the rest.

**Chat Completions on Azure.** `POST /openai/v1/chat/completions`, with `model` set to the deployment name, is verified. Microsoft positions it mainly for models from other providers. Callers who need it use `OpenAiCompatibleClient` with `endpoint = https://{resource}.openai.azure.com/openai/v1` and `AuthPlacement = Header("api-key")` (4.9).

**Removed in 4.0:** the 3.x deployment-in-path routing (`/openai/deployments/{deployment}/...?api-version=...`) and the `apiVersion` constructor parameter. Microsoft calls v1 "required to access the latest features". This is a breaking change, documented in the migration guide.

**Still to verify in P0:**
- `/openai/v1/embeddings` and `/openai/v1/models` (expected in Azure's v1 OpenAPI spec at `Azure/azure-rest-api-specs`, `specification/ai/data-plane/OpenAI.v1/`). If embeddings exist only on deployment routes, `AzureOpenAiClient.EmbedAsync` keeps the 3.x deployment route for embeddings alone, documented.
- Azure support for `/responses/{id}/cancel` and `/responses/input_tokens`.

## 7. Samples and fixtures

- **Consoles:** update every console to the 4.0 clients (`AnthropicConsole`, `CohereConsole`, `GeminiConsole`, `OllamaConsole`, `OllamaCloudConsole`, `OpenAIConsole`, `TeiConsole`, `TypeSafeConsole`, `VoyageAIConsole`, `XAiConsole`, `AotConsole`, `CliAgentConsole`, and the seven harness consoles), plus `Console.Shared`, `Console.Tools`, and `AgentConsole.Shared`.
  - Consoles that used several capability clients now use one client.
  - `OpenAIConsole` gains `--compatible <url>` to drive `OpenAiCompatibleClient`. Its default uses `OpenAiClient` (Responses), with persistent history, tool calls, and reasoning display.
  - `XAiConsole` uses the neutral `ResponsesApi*` types.
- **Postman:** add `postman/openai-responses.openapi.json`, a mock in `postman/mocks/openai-responses/`, and a simulation `postman/simulations/openai-responses.sim.yaml`, mirroring the xAI fixtures. `Test.Console` gains a `POLYPROMPT_OPENAI_RESPONSES_SIMULATION` path like the xAI one. Update `postman/` READMEs if present.

## 8. Documentation

Every public type and member is documented, and the docs are written for both humans and coding agents.

### 8.1 README.md (rewrite)

New section order:

1. **What it does** (rewritten around `{Provider}Client`)
2. **Installation**
3. **Quick start**: one example per provider, constructor plus one call, all using `{Provider}Client`
4. **Capability matrix** (anchor `#capability-matrix`): the section 5.2 table in a fixed, machine-checked format, linked from every `CapabilityNotSupportedException` message
5. **Which OpenAI client?**: `OpenAiClient` (Responses: OpenAI, and servers that implement `/responses`) versus `OpenAiCompatibleClient` (Chat Completions: vLLM, Ollama `/v1`, LM Studio, gateways), with the 404 hint
6. **Architecture**: the 4.1 hierarchy, `Capabilities`/`Supports`, `CapabilityNotSupportedException`, `Defaults` sections, per-call options, the wrong-options-type rule, `ClientOptions`
7. **Responses API (OpenAI and xAI)**: what it is, compared with Chat Completions; encrypted reasoning replay through `ProviderState`; `Store` and `ContinueResponseAsync`; native operations; event kinds; hosted tool factories
8. **Provider sections**, one per client: constructor, defaults, provider-specific options and operations, auth, quirks, and console sample
9. **Agent harnesses** (renamed clients)
10. **Detailed examples** (updated): system prompts, provider options, tool calling, streaming tool calling, Gemini tool notes, reasoning effort with the per-provider mapping table, reasoning output, token usage, `RawResponse`, citations, streaming, embeddings, reranking, classification, sparse embeddings, decisions, generation, models, connectivity, call details, telemetry, AOT, cancellation, custom `HttpClient`, provider-agnostic code with `ProviderClientBase` and `Supports`
11. **For coding agents**: a compact reference block with the constructor pattern per provider, the operation list, the capability rule (`Supports` before calling; `CapabilityNotSupportedException` otherwise), the options-type rule, the OpenAI client choice, and continuation state. Keep it short and exact; agents read it as context.
12. **API reference**: constructors, common members, options, default models, feature notes
13. **Upgrading from 3.x**: link to `MIGRATION_V3_TO_V4.md`

Update the version badge to 4.0.0. Replace the "Upgrading from 2.x" banner with an "Upgrading from 3.x" banner.

### 8.2 `MIGRATION_V3_TO_V4.md` (new)

Contents:
- The new shape, and why
- Client mapping (5.1)
- **The OpenAI decision:** were you calling OpenAI, or a compatible server?
- `Defaults` → `Defaults.<Section>`, and the removed `Model` shorthand
- Options renames (4.6)
- Removed `XAi*` types → `ResponsesApi*`
- String settings → extensible values
- `ReasoningEffortOverride` → `ReasoningEffortValue`
- Harness renames
- Constructors and `ClientOptions`
- Behavior changes:
  - OpenAI uses Responses;
  - the wrong options type throws;
  - unsupported capabilities throw `CapabilityNotSupportedException`;
  - state written in the 4.0 format (3.x state still read);
  - new result fields;
  - changed default models (every changed row of 5.3, with the 3.x value), including clients that now have no default and throw until a model is set (`OpenAiCompatibleClient`, `BedrockClient` completion, `OllamaCloudClient`);
  - `AzureOpenAiClient` moves to the v1 API: no `api-version`, the deployment name sent as `model`, and the deployment-in-path routes removed;
  - `Store` is sent explicitly as `false` unless set, whereas OpenAI's own SDKs default to `true`;
  - the new `Max`, `XHigh`, and `None` effort levels and their per-provider mapping.
- Provider-agnostic code: `CompletionClientBase` and friends → `ProviderClientBase` plus `Supports`
- Custom subclasses: how a 3.x derived capability client becomes a derived provider client
- Checklist

Every rename is listed in one table with the exact 3.x and 4.0 names, so an agent can apply it mechanically.

### 8.3 XML documentation

- **Every public type and member is documented.** Missing XML docs (CS1591) are an error in `PolyPrompt.csproj` for Release builds.
- **Each provider client's class summary** states:
  - the provider and wire API;
  - its supported capabilities (matching `Capabilities`);
  - its unsupported capabilities, which throw;
  - its constructor's auth requirements;
  - its defaults;
  - a `<seealso>` to the README matrix.
- **Each override** documents provider-specific behavior: limits, quirks, which options fields apply.
- **Each base operation** on `ProviderClientBase` documents `<exception cref="CapabilityNotSupportedException">` *"when `Supports(Capability.X)` is false for this client."*
- **Options fields** document which client or clients honor them.
- The package ships the XML doc file, so IntelliSense and agents reading the package see all of it.

### 8.4 Other docs

- **STREAMING.md:** rewrite examples against `{Provider}Client`; Responses streaming and `ResponsesApiEventKind`; tagged continuation state.
- **TELEMETRY.md:** capability attribute derived from the operation; the new `polyprompt.reasoning.effort` attribute; unsupported calls record nothing.
- **AOT.md:** new serializable types (`ResponsesApi*`, `Citation`, the extensible values and their converters, the 4.0 state envelope); covariant `Defaults` sections; `Capabilities` as constants.
- **`AGENT_HARNESS_SUPPORT_PLAN.md`** and **`MIGRATION_V2_TO_V3.md`:** add a one-line note at the top that the names are superseded in 4.0, linking to `MIGRATION_V3_TO_V4.md`. Don't rewrite the history.
- **`llms.txt`** (new, repo root, also packed into the NuGet package): a short index pointing agents to the README sections "For coding agents", "Capability matrix", and "Which OpenAI client?", and to `MIGRATION_V3_TO_V4.md`.
- **This file:** at release, set Status to implemented and add an as-built section with every deviation and every section 14.2 outcome.

### 8.5 CHANGELOG.md

A `## v4.0.0 (YYYY-MM-DD)` entry in the existing format:
- **Breaking:** every item in 8.2's behavior and rename lists, each with its one-line fix.
- **Added:**
  - the provider clients, `ProviderClientBase`, `Capability`/`Supports`, `CapabilityNotSupportedException`, and `ClientOptions`;
  - Responses support on `OpenAiClient`, `ResponsesApiClientBase`, the native operations, and `ContinueResponseAsync`;
  - `RawResponse`, `Citations`, and `AppliedReasoningEffort`;
  - the extensible values and the `None`/`XHigh` effort levels;
  - the typed tool factories, `llms.txt`, and the OpenAI Responses Postman fixtures.
- **Changed:** state format (backward-compatible read), telemetry capability attribute source.
- **Removed:** the capability base classes and clients, `XAi*` result types, `ReasoningEffortOverride`, the `Model` shorthand.
- **Validation:** case counts per runner and framework, AOT check count, matrix and contract test results, docs snippet compilation, whether the live OpenAI Responses run happened.

## 9. Tests

### 9.1 Migrate the existing suite

All 805 existing local cases (as of 3.7.0) move to the 4.0 clients and keep their assertions. That covers Test.Shared, run by Test.Automated, Test.Xunit, and Test.Nunit on net8.0 and net10.0.

- The cases for `OpenAiCompletionClient` against the local OpenAI fixture become `OpenAiCompatibleClient` cases. They are the Chat Completions regression suite and must pass unchanged in substance.
- The xAI cases move to `XAiClient` and the neutral types. Their wire bodies and parsed results must stay identical, which is the regression gate for the engine extraction.
- `Test.Aot` checks are migrated, and the JIT and native request digests must match on both frameworks. The xAI request digests must equal the 3.7.0 digests.
- `Test.Console` (console and fake-CLI end-to-end tests) is migrated to the new client and console names.

### 9.2 Contract tests (new; the tests that make "docs are the contract" true)

1. **Matrix test.** Parse the README capability matrix (fixed format, 8.1) and assert each row equals the client's `Capabilities`. Fails on any drift, including a client missing from the README.
2. **Behavior contract.** For every client and every capability:
   - **unsupported:** each operation throws `CapabilityNotSupportedException` synchronously, with the right `Provider`, `Capability`, and `Operation`, and no HTTP request is made (the local server records zero requests);
   - **supported:** the operation is overridden, checked by reflection, and a local-fixture call succeeds.
3. **Defaults sections.** Each provider's defaults type exposes exactly the sections for its supported capabilities, and `ClientDefaults` returns null for the others.
3a. **Default-models table.** Parse the README default-models table (5.3) and assert each cell equals the code's default model, with "none" meaning null. For every client with no default, a call without a model throws `InvalidOperationException` naming `Defaults.<Section>.Model` before any request is made.
4. **XML doc check.** Each provider client's class summary mentions every supported capability. This reads the generated XML doc file.
5. **Dependency rule.** A reflection check finds no `XAi*` types in the signatures of `OpenAiClient`, `OpenAiCompatibleClient`, `ResponsesApiClientBase`, or any `ResponsesApi*` type. A source check (`grep -rnE '\bXAi\w*' src/PolyPrompt/Clients/OpenAi src/PolyPrompt/Clients/Responses src/PolyPrompt/Models/ResponsesApi*.cs` finds nothing) catches references in method bodies.
6. **Docs snippets compile.** A new `Test.Docs` project extracts every C# block in README.md, STREAMING.md, and MIGRATION_V3_TO_V4.md (except blocks tagged `<!-- no-compile -->`) and compiles them, so the examples agents copy are known to build. It runs in the normal test pass.

### 9.3 New feature cases

- **OpenAI Responses** (`LocalOpenAiResponsesCases.cs`, against a new `LocalOpenAiResponsesRoutes` fixture keyed by its own bearer key, sharing no helpers with the xAI fixture):
  - chat, tool chat, and generate, streaming and not: text, reasoning summaries, parallel tool calls, argument deltas across events, finish reasons (including `incomplete` mapping), usage with cached and reasoning tokens;
  - the request body (model, input, instructions, `max_output_tokens`, `store:false`, the `include` field, effort mapping including `None`/`Minimal`/`XHigh`, `tool_choice` mapping, strict tools, hosted tool factories, `text.format`, `AdditionalParameters` reserved-field rejection, no xAI-only fields);
  - errors ("OpenAI" wording, `error` events, `response.failed`, HTTP errors, malformed JSON, mid-stream cancellation);
  - the 404/405 hint;
  - state replay with encrypted reasoning, in order;
  - native operations, `ContinueResponseAsync` including the not-stored rejection, and `ResponsesApiEventKind` mapping for every event type.
- **Cross-provider state isolation:** OpenAI state is ignored by xAI, xAI state by OpenAI, and both by `OpenAiCompatibleClient`. Scope mismatch falls back to plain history. Every 3.x state shape is read correctly, and only the 4.0 envelope is written.
- **Constructors:** blank, whitespace, or URL-shaped keys throw with the right message (including the `OpenAiCompatibleClient` hint); a bad endpoint throws; endpoint normalization per provider; `ClientOptions` fields applied; injected `HttpClient` not disposed, owned one disposed.
- **Options:** the wrong provider's options type throws for every operation; base options are accepted; per-call fields override `Defaults` sections.
- **Extensible values:** known values serialize, unknown values pass through, and invalid known-set values throw per dialect.
- **Result additions:** `RawResponse` populated (non-streaming, and streaming where the provider sends a final object) and absent from call details at every capture level; `AppliedReasoningEffort` and the telemetry attribute for every mapping, including `None`, `XHigh`, and `Max`.
- **Citations** (one fixture per row of the 4.8.1 table, each built from the documented response shape, plus recorded live payloads once P0 captures them):
  - every mapped field and `CitationKind`;
  - offset normalization into `Text` in UTF-16 units: Gemini byte offsets, part-relative offsets shifted for multi-part text, and an inclusive end converted;
  - fixtures containing non-ASCII and non-BMP text (accents, CJK, emoji), so byte versus character versus code-point errors fail the test;
  - source ranges kept in `Source` and never leaked into `TextStart`/`TextEnd`;
  - Cohere `THINKING_CONTENT` citations excluded;
  - xAI `title` not used as `Title`;
  - Anthropic `citations_delta` and Cohere `citation-start` assembled on the final streaming response;
  - OpenAI `response.output_text.annotation.added` surfaced as `AnnotationAdded`;
  - providers with no citations returning an empty list, never null;
  - `Raw` preserved.
- **Responses wire values:** each verified value in 6.4 (every effort, summary, service tier, and include value) serializes as documented. `fast` requested with `priority` returned is not treated as a mismatch. `Background` without `Store` throws locally. `incomplete_details.reason` maps to `FinishReason` for all four documented reasons. `cache_write_tokens` maps to `CacheCreationTokens`.
- **Azure:**
  - requests go to `/openai/v1/responses` with the deployment as `model` and no `api-version`;
  - `api-key` and Entra bearer auth each work;
  - `invalid_encrypted_content` carries the proxy hint;
  - `OpenAiCompatibleClient` with `AuthPlacement = Header("api-key")` reaches `/openai/v1/chat/completions`;
  - embeddings and models routes per the P0 outcome (6.6).
- **Telemetry:** the capability attribute per operation on a single client; no telemetry for unsupported calls.
- **AOT:** Responses tool chat (streaming and not), a native create, the state envelope, the extensible values, `Citation`, and `CapabilityNotSupportedException` (no reflection).

### 9.4 Live and simulation (not release gates; record in the CHANGELOG whether they ran)

- Postman simulation for OpenAI Responses through `Test.Console`, like the xAI one.
- Live suites with the `POLYPROMPT_TEST_*` environment groups, extended with `POLYPROMPT_TEST_OPENAI_*` for one multi-turn tool conversation with reasoning on a real OpenAI reasoning model.

### 9.5 Commands

```bash
dotnet build src/PolyPrompt.sln -c Release
dotnet run --project src/Test.Automated -c Release -f net8.0
dotnet run --project src/Test.Automated -c Release -f net10.0
dotnet test src/Test.Xunit -c Release
dotnet test src/Test.Nunit -c Release
dotnet test src/Test.Docs -c Release
# Test.Aot and Test.Console per AOT.md and README instructions, on both frameworks
```

Every case passes with none skipped. The CHANGELOG lists counts per runner and framework.

## 10. Work plan

Each phase ends with a green build and a commit on `main` (or a `v4` branch merged at release). Tests are migrated and added within each phase, not saved for the end.

| Phase | Work | Exit gate |
|---|---|---|
| **P0 Audit and foundation** | Close the remaining verification items in section 14.2 and record each outcome in the as-built section: capture live payloads for citations, settle the three ⚠ default models, confirm the four 5.2 matrix cells, and check the Azure embeddings, models, cancel, and input-token routes. Audit fixed-value string settings and probe routes. (The streaming audit and the OpenAI and Azure wire details are already done: sections 4.3, 6.4, 6.6.) Add `ProviderClientBase`, `Capability`, `Supports`, `CapabilityNotSupportedException`, `ClientDefaults`, `ClientOptions`, the extensible value types, the new `ReasoningEffortLevel` values, the `ResponseBase` additions, and the 4.0 state envelope with its backward-compatible reader. Add the contract-test scaffolding (9.2). | Foundation compiles; audit results recorded in this file |
| **P1 Provider consolidation** | One provider at a time, merge its capability clients into `{Provider}Client`, with defaults sections, constructor, capabilities, overrides, and XML docs, migrating that provider's cases. Order: Ollama, Ollama Cloud, Anthropic, Gemini, Vertex AI, Bedrock, Cohere, VoyageAI, TEI, TypeSafe, OpenAI-compatible (from 3.x OpenAI), Azure. Delete the capability bases and clients when the last provider moves. | All migrated cases pass; contract tests pass for the moved providers |
| **P2 Responses** | Extract the engine into `ResponsesApiClientBase`; move xAI onto it as `XAiClient` with the neutral types; build `OpenAiClient` on it with the OpenAI dialect; native operations, `ContinueResponseAsync`, tool factories, event kinds; Azure decision (6.6). | xAI wire bodies, results, and AOT digests identical to 3.7.0; new OpenAI Responses cases pass; dependency rule passes |
| **P3 Results and reasoning** | `RawResponse`, `Citations`, and `AppliedReasoningEffort` across all providers; effort mapping tables; telemetry attribute. | Result-addition cases pass for every provider |
| **P4 Harnesses** | Rename the seven harness clients and adopt `ClientOptions`. | Harness cases and Test.Console pass |
| **P5 Samples and fixtures** | All consoles updated; `OpenAIConsole --compatible`; OpenAI Responses Postman fixtures and simulation. | Every console builds; Test.Console passes |
| **P6 Docs** | README rewrite, `MIGRATION_V3_TO_V4.md`, XML doc completion (CS1591 as error), STREAMING, TELEMETRY, AOT, supersession notes, `llms.txt`, CHANGELOG. | Matrix test, XML doc check, and Test.Docs pass |
| **P7 Release** | Section 11. | Published and pushed |

## 11. Release

1. **Version.** Set `<Version>4.0.0</Version>` in `src/PolyPrompt/PolyPrompt.csproj`, and `PackageReleaseNotes` if present, pointing to the CHANGELOG and migration guide. Update the README badge and any sample version strings. Pack `llms.txt` and the XML docs file into the package. `grep -rn "3\.7\.0"` should find only historical CHANGELOG entries and this file's baseline line.
2. **Final validation.** On a clean checkout of the release commit, run everything in 9.5, Test.Aot and Test.Console on both frameworks, and the dependency source check. Optionally run the 9.4 simulation and live checks and record the outcome in the CHANGELOG.
3. **Pack** the library with its symbol package (the csproj already sets `IncludeSymbols` and `SymbolPackageFormat=snupkg`):
   ```bash
   rm -rf artifacts/nuget
   dotnet pack src/PolyPrompt/PolyPrompt.csproj -c Release -o artifacts/nuget
   ls artifacts/nuget   # PolyPrompt.4.0.0.nupkg and PolyPrompt.4.0.0.snupkg
   ```
   Inspect the `.nupkg` and confirm it contains `lib/net8.0` and `lib/net10.0` with XML docs, the README, the icon, and `llms.txt`.
4. **Publish to nuget.org.** Pushing the `.nupkg` uploads the matching `.snupkg`. The maintainer supplies the key at release time through an environment variable. Never write it to a file, commit it, or leave it in shell history.
   ```bash
   dotnet nuget push artifacts/nuget/PolyPrompt.4.0.0.nupkg \
     --api-key "$NUGET_API_KEY" --source https://api.nuget.org/v3/index.json --skip-duplicate
   ```
   nuget.org versions cannot be replaced, so push only after step 2 passes. Confirm that the package and the symbols appear on nuget.org (symbol indexing can lag).
5. **Commit and push to main.** The phase commits land first; the release commit carries the version, CHANGELOG, and as-built notes:
   ```bash
   git add -A
   git commit -m "Release 4.0.0: one client per provider, OpenAI Responses API, docs-as-contract"
   git push origin main
   ```
   `artifacts/` must not be committed (add it to `.gitignore` if it isn't ignored already).

## 12. Mux follow-up (separate repository, after 4.0.0 is on nuget.org)

- Bump `PolyPrompt` to 4.0.0 in `src/Mux.Core/Mux.Core.csproj`.
- `LlmClient.CreateClient` (and `EndpointModelLister`) build one provider client per endpoint and call completions and model listing on it:
  - `openai` → `OpenAiClient` (Responses);
  - `vllm`, `openai-compatible` → `OpenAiCompatibleClient`, keeping Mux's auth placement handling;
  - `azure-openai` → `AzureOpenAiClient`;
  - `xai` → `XAiClient`;
  - `anthropic` → `AnthropicClient`;
  - `gemini` → `GeminiClient`;
  - `vertex` → `VertexAiClient`;
  - `bedrock` → `BedrockClient`;
  - `ollama` → `OllamaClient`;
  - `ollama-cloud` → `OllamaCloudClient`.
- `EndpointModelLister` can then list models for every adapter whose client `Supports(Capability.Models)`, including Anthropic and Gemini.
- Mux release note: users whose `openai` endpoint points at a non-OpenAI gateway must switch the adapter to `openai-compatible`.
- Mux's `ProviderState` handling (`LlmClient`, `AgentLoop`, `SessionMergePolicy`) is provider-agnostic and already persists the state. Because 4.0 reads 3.x state, existing Mux sessions resume.
- Surface `AppliedReasoningEffort` in Mux's usage telemetry.

## 13. Settled decisions

| # | Decision |
|---|---|
| S1 | One client per provider, with every operation; unsupported operations throw `CapabilityNotSupportedException`. Compile-time capability checking from 3.0 is given up in exchange for one object per provider, kept honest by the matrix and contract tests. |
| S2 | Two OpenAI clients split by service and wire protocol (`OpenAiClient` for Responses, `OpenAiCompatibleClient` for Chat Completions), with no mode switch. |
| S3 | `Store` defaults to false. `ContinueResponseAsync` catches the not-stored mistake before sending. |
| S4 | `XAi*` result types are removed without aliases, and the migration table covers them. |
| S5 | Fixed-value string settings become extensible string-backed values, not closed enums. |
| S6 | No `Model` shorthand; models are set per `Defaults` section. |
| S7 | Harness clients are renamed but stay on their own base, without the model operations. |

## 14. Verification record

### 14.1 Verified on 2026-11-12

| Topic | Result | Source |
|---|---|---|
| Streaming support (4.3) | All six completion forms implemented with real streaming by all seven completion clients; no streaming forms exist for other capabilities | PolyPrompt 3.7.0 source: `CompletionClientBase` abstract members; each client's `*StreamingCoreAsync` uses `ExecuteStreamingAsync` (Gemini `:streamGenerateContent?alt=sse`, Bedrock `converse-stream`) |
| OpenAI `reasoning.effort`, `reasoning.summary`, `service_tier`, tool types, request fields, `store` default, `include` values, SSE event names, `incomplete_details.reason`, annotations, usage (4.7, 4.8, 6.3, 6.4, 6.5) | As recorded in those sections | OpenAI OpenAPI spec, https://github.com/openai/openai-openapi (`main`, `openapi.json`). The older `manual_spec` branch (April 2025) is stale and must not be used. |
| OpenAI `/responses/compact`, `GET`/`DELETE /responses/{id}`, `/input_items`, `/cancel`, `/responses/input_tokens` | All exist | same spec |
| Azure Responses on the v1 API: routing, auth, encrypted reasoning, compact, stored-response operations, hosted tools, v1 Chat Completions (6.6) | As recorded in 6.6 | https://learn.microsoft.com/en-us/azure/ai-foundry/openai/api-version-lifecycle and https://learn.microsoft.com/en-us/azure/ai-foundry/openai/how-to/responses |
| Citation formats (4.8.1) | As recorded in the 4.8.1 table | OpenAI spec above; https://docs.x.ai/developers/tools/citations; https://platform.claude.com/docs/en/build-with-claude/citations, `/agents-and-tools/tool-use/web-search-tool`, `/build-with-claude/search-results`; https://aistudio.google.com/api-reference/generate-content and `/docs/google-search`; https://docs.cohere.com/reference/chat and `/chat-stream`; https://docs.aws.amazon.com/bedrock/latest/APIReference/API_runtime_CitationsContentBlock.html (with `API_runtime_Citation`, `API_runtime_CitationLocation`, `API_runtime_ContentBlockDelta`); https://docs.ollama.com/api/chat; https://developers.openai.com/api/reference/resources/chat/subresources/completions/methods/create |
| Default models (5.3) | As recorded in the 5.3 table | https://developers.openai.com/api/docs/models; https://platform.claude.com/docs/en/about-claude/models/overview; https://aistudio.google.com/docs/models; https://docs.cloud.google.com/vertex-ai/generative-ai/docs/models; https://docs.x.ai/docs/models; https://docs.cohere.com/docs/models; https://docs.voyageai.com/docs/embeddings and `/docs/reranker`; https://docs.aws.amazon.com/bedrock/latest/userguide/model-cards.html; https://ollama.com/api/tags |

The default-model IDs above were read from provider model pages and API specs, not from live API calls. P0 makes one live call per changed default before release.

### 14.2 Still to verify in P0 (record each outcome in the as-built section)

| Item | Section | If confirmed | If not |
|---|---|---|---|
| Citation offset units (UTF-16 vs code points) for OpenAI, xAI, Anthropic, Cohere | 4.8.1 | convert as needed | |
| OpenAI Chat Completions `end_index` inclusive? | 4.8.1 | convert to exclusive | keep as is |
| xAI raw top-level `citations` array; xAI Responses annotation stream events | 4.8.1 | parse it | annotations only |
| Bedrock citation location inner fields | 4.8.1 | map them | keep `Raw` only for unknown members |
| Gemini `url_citation` annotation shape in `generateContent` | 4.8.1 | parse both shapes | grounding metadata only |
| xAI `/v1/embeddings` still exists | 5.2 | keep, no default model | clear the flag, remove the code |
| Cohere `/v1/classify` still works | 5.2 | keep, document the Embed recommendation | clear the flag, remove the code |
| Azure `/openai/v1/embeddings`, `/openai/v1/models`, `/cancel`, `/responses/input_tokens` | 6.6 | v1 routes | deployment route for embeddings only; omit unsupported operations |
| Gemini default (`gemini-3.8-flash` vs a GA Gemini 4 Flash-tier ID); Vertex ID string | 5.3 | as chosen | keep `gemini-3.8-flash` |
| Cohere `command-a-plus-05-2026` is GA | 5.3 | use it | keep `command-a-03-2025` |
| `text-embedding-005` still live on Vertex | 5.3 | keep | switch to `gemini-embedding-001`, call it out as breaking |
| xAI support for `/responses/{id}/cancel` and `/responses/input_tokens` | 6.5 | move to the base | OpenAI and Azure only |
| Probe routes per provider | 4.2 | models route | cheapest authenticated route |

## 15. Definition of done

- Every provider is a single `{Provider}Client` on `ProviderClientBase`. No capability client or capability base class remains.
- `OpenAiClient` speaks the Responses API. `OpenAiCompatibleClient` passes the full 3.x Chat Completions regression suite.
- The xAI wire behavior and AOT digests match 3.7.0, and no OpenAI or shared Responses code references an `XAi*` type.
- The capability matrix, `Capabilities`, XML docs, and actual behavior agree, proven by the contract tests (9.2).
- Every README, STREAMING, and migration-guide snippet compiles (Test.Docs).
- CS1591 is clean; every public member is documented.
- README, `MIGRATION_V3_TO_V4.md`, STREAMING.md, TELEMETRY.md, AOT.md, `llms.txt`, CHANGELOG, samples, and Postman fixtures are updated.
- All local, AOT, docs, and console tests pass on net8.0 and net10.0 with none skipped.
- The version is 4.0.0, and `PolyPrompt.4.0.0.nupkg` and `.snupkg` are on nuget.org.
- All work is committed and pushed to `main`.

## 16. As-built record (4.0.0)

### 16.1 Section 14.2 outcomes

No provider credentials were available during the release, so no live payloads were captured and no live calls were made. Each item was settled from the providers' official documentation and API specifications on 2026-11-12, and the plan's fallback was taken where a fact could not be confirmed.

| Item | Outcome |
|---|---|
| Citation offset units (OpenAI, xAI, Anthropic, Cohere) | Not confirmed from a live payload. Treated as UTF-16 code units (.NET string indices); Gemini byte offsets are converted. Covered by fixtures with accented, CJK, and emoji text. |
| OpenAI Chat Completions `end_index` inclusive? | Not confirmed (the reference says "the index of the last character"). Kept as reported. |
| xAI top-level `citations` array; xAI annotation stream events | No top-level array in the documented response; annotations only. |
| Bedrock citation location fields | `documentChar`/`documentPage`/`documentChunk` {`documentIndex`, `start`, `end`}, `searchResultLocation` {`searchResultIndex`, `start`, `end`}, `web` {`url`, `domain`}; all mapped. |
| Gemini `url_citation` shape in `generateContent` | Not confirmed; grounding metadata and `citationMetadata` only. |
| xAI `/v1/embeddings` | Gone: docs.x.ai lists no embedding models and the API reference has no `/v1/embeddings`. Flag cleared, code removed. |
| Cohere `/v1/classify` | Still documented, no endpoint deprecation (some fields deprecated). Kept; Embed recommendation documented. |
| Azure `/openai/v1/embeddings`, `/openai/v1/models`, `/cancel`, `/responses/input_tokens` | Embeddings, models, and cancel are in the Azure OpenAI v1 GA spec (`azure-rest-api-specs`); `input_tokens` is not. v1 routes used; `CancelResponseAsync` on Azure; `CountInputTokensAsync` OpenAI only. |
| Gemini default; Vertex ID | No GA Gemini 4 Flash-tier model; `gemini-3.8-flash` (Stable) kept for both. |
| Cohere `command-a-plus-05-2026` | Listed as Live and newest; used. |
| Vertex `text-embedding-005` | No longer listed on Vertex's model page; default switched to `gemini-embedding-001` and called out as breaking. |
| xAI `/cancel` and `/input_tokens` | Not documented (background is marked unsupported); OpenAI (and Azure cancel) only. |
| Probe routes | Models route where one exists (OpenAI family, xAI, Anthropic, Gemini, Cohere, Bedrock control plane, Ollama `/api/tags`); TEI `/health`; Vertex `:countTokens`; VoyageAI embeds a probe word; TypeSafe tries `/v1/models` then a minimal decision. |
| Default-model live check (14.1 note) | Not performed (no credentials). |
| Fixed-value string audit | `ServiceTier`, `ReasoningSummary`, and `ReasoningEffortValue` became extensible values. Other fixed-set strings (Cohere `InputType`/`EmbeddingType`/`Truncate`, VoyageAI `InputType`/`OutputDtype`, TEI `TruncationDirection`) remain strings, already normalized or validated by their options; deferred. |

### 16.2 Deviations from the plan

- **Public operations are not virtual.** `ProviderClientBase`'s public operations validate, check the capability, merge options, and record telemetry, then call protected virtual `...CoreAsync` members whose base implementations throw `CapabilityNotSupportedException`. Providers override the core members; the behavior contract test checks that exactly the supported capabilities' core members are overridden.
- **Engine layout.** The Responses engine is the partial class `ResponsesApiClientBase` split across `ResponsesApiRequestBuilder.cs`, `ResponsesApiParser.cs`, and `ResponsesApiStreamReader.cs`, plus the internal `ResponsesApiDialect` and `ResponsesApiJson`, instead of separate internal builder, parser, and reader types. OpenAI-only fields live in the internal `OpenAiResponsesFields`, shared by `OpenAiClient` and `AzureOpenAiClient`.
- **`ResponsesApiClientBase` cannot be subclassed outside the library** (its dialect member is `private protected`); derive from `OpenAiClient`, `AzureOpenAiClient`, or `XAiClient`.
- **`GenerateContentClientBase`** is a new public abstract base shared by `GeminiClient` and `VertexAiClient` (the plan placed both directly under `ProviderClientBase`). `OllamaCloudClient` derives from `OllamaClient`.
- **`ProviderName`** is a new public property, used in exception messages.
- **Unsupported defaults sections** are hidden on each provider defaults type with `[Obsolete(error: true)]` and `[EditorBrowsable(Never)]` `new` properties, so they are compile errors there and null through `ClientDefaults`.
- **No Azure `deployment` constructor parameter** (as planned); the deployment is `Defaults.Completion.Model`.
- **xAI `Embed`** removed per the P0 outcome; `ListEmbeddingModelsAsync` stays on `XAiClient` as a catalog listing.
- **Telemetry:** `validate_connectivity` reports capability `connectivity` (a new value) because one client spans capabilities; the active-clients gauge reports `provider`.
- **Responses `content_filter`** maps to `content_filter` for xAI too (3.x reported `incomplete`).
- **Streaming citations** for Cohere, Bedrock, and Chat Completions accumulate on the streaming response object as events arrive; they are complete once the stream completes.
- **xAI digest gate.** The overall `Test.Aot` request digest changes because the client set changed (one client per provider). The xAI gate was checked directly instead: 13 representative xAI requests built with 3.7.0 and 4.0 are byte-identical.
- **Live and simulation runs (9.4).** The Postman simulations were run against the mock servers with Node directly, not the Postman CLI. Live suites were not run.

### 16.3 Validation

See the CHANGELOG v4.0.0 Validation section for counts per runner and framework.

