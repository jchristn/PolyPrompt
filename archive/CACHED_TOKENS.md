# Capturing Cached and Reasoning Tokens

An evaluation of what it would take for PolyPrompt to surface **cached prompt tokens** and **reasoning
tokens** from provider responses, and how to do it without changing how existing callers perceive the
API. The short version: it is a small, additive, non-breaking change concentrated in the streaming
usage parsers of five provider clients, plus new nullable fields on one model type. The one thing that
needs a deliberate decision is a cross-provider semantic difference in what "cached" means relative to
the prompt count — covered in the section that follows.

The provider matrix, field names, and change list below reflect the 2.5.0 source tree
(`src/PolyPrompt/PolyPrompt.csproj` → `<Version>2.5.0</Version>`).

## Why this is wanted

Downstream consumers that do cost and cache accounting need the cached-token and reasoning-token
counts that providers already return but PolyPrompt currently drops on the floor. Cache reads are
billed at a fraction of full input, cache writes at a premium, and reasoning tokens are billed as (or
alongside) output — so a consumer that can only see `PromptTokens`/`CompletionTokens` cannot compute an
accurate cost or a cache hit rate. The data is in the provider payloads today; it is simply not parsed.

## Current state

There is exactly one usage type, `ChatStreamingUsage`
(`src/PolyPrompt/Models/ChatStreamingUsage.cs`), and it exposes only:

```csharp
public int?  PromptTokens { get; set; }
public int?  CompletionTokens { get; set; }
public int?  TotalTokens { get; set; }
public long? TotalDurationNs { get; set; }      // Ollama only
public long? LoadDurationNs { get; set; }        // Ollama only
public long? PromptEvalDurationNs { get; set; }  // Ollama only
public long? EvalDurationNs { get; set; }        // Ollama only
```

Two facts about the current shape matter for this change:

Usage is parsed **only on the streaming paths**. `ChatStreamingResponse.Usage` and
`ToolChatStreamingResponse.Usage` are populated; the non-streaming `ChatResponse` and `ToolChatResponse`
have no `Usage` property at all. Every provider builds a `ChatStreamingUsage` directly from its raw
JSON — there is no provider-neutral intermediate usage type and no mapping layer. New fields added to
`ChatStreamingUsage` therefore flow through to the response automatically once a provider populates
them; the only work is in the per-provider parse and the model.

The chunk-to-response handoff is a plain "last non-null usage wins" copy in
`CompletionClientBase.WrapChunksWithTiming` / `WrapToolChatChunksWithTiming`
(`if (chunk.Usage != null) response.Usage = chunk.Usage;`). Nothing there needs to change.

## What each provider actually returns

| Provider | Cached (read) | Cached (write) | Reasoning | Source field(s) |
| --- | --- | --- | --- | --- |
| Anthropic | yes | yes | no separate count | `usage.cache_read_input_tokens`, `usage.cache_creation_input_tokens` (on `message_start`, sometimes echoed on `message_delta`) |
| OpenAI | yes | no | yes | `usage.prompt_tokens_details.cached_tokens`, `usage.completion_tokens_details.reasoning_tokens` (needs `stream_options.include_usage`) |
| Azure OpenAI | yes | no | yes | same as OpenAI (client inherits the parser) |
| Gemini | yes | no | yes | `usageMetadata.cachedContentTokenCount`, `usageMetadata.thoughtsTokenCount` |
| Vertex | yes | no | yes | same as Gemini (client inherits the parser) |
| Bedrock | yes | yes | no separate count | `usage.cacheReadInputTokens`, `usage.cacheWriteInputTokens` |
| Ollama | no | no | no count (thinking is text only) | — |

Anthropic and Bedrock have no distinct reasoning-token field: extended-thinking output is billed as
ordinary output tokens. Ollama reports neither cache nor a reasoning count — its "thinking" is streamed
as `message.thinking` text (already surfaced through `ReasoningText`), so its new fields simply stay
null. Azure and Vertex inherit their parents' parsers verbatim, so fixing OpenAI and Gemini fixes them
for free.

## The semantic that needs a decision

"Cached prompt tokens" does not mean the same thing across providers, and getting this wrong corrupts
every downstream cost calculation.

**OpenAI and Gemini report cached tokens as a subset already included in the prompt count.**
`prompt_tokens` already contains `cached_tokens`; `promptTokenCount` already contains
`cachedContentTokenCount`. Full input = `PromptTokens`, of which `CachedPromptTokens` were a cache hit.

**Anthropic and Bedrock report cache reads as a separate bucket, not included in `input_tokens`.**
Anthropic's `input_tokens` counts only the uncached portion; `cache_read_input_tokens` and
`cache_creation_input_tokens` are additional. Full input = `input_tokens + cache_read + cache_creation`.
Today PolyPrompt sets `PromptTokens = input_tokens`, so for a cached Anthropic request `PromptTokens`
is already the *uncached* count.

That leaves two ways to expose the new data:

**Option A — purely additive (recommended).** Add the new nullable fields, populate them from each
provider's native value, and leave `PromptTokens` exactly as it is computed today. Zero behavior change
for existing callers — no value they read today changes. The cost is that `PromptTokens` keeps its
provider-native meaning (includes cache on OpenAI/Gemini, excludes it on Anthropic/Bedrock), so a
consumer that wants "total input across providers" must add the cache buckets itself where the provider
splits them. Document the semantic on each field and this is unambiguous.

**Option B — normalize `PromptTokens` to full input everywhere.** For Anthropic/Bedrock, set
`PromptTokens = input_tokens + cache_read + cache_creation` so `PromptTokens` means the same thing on
every provider and `CachedPromptTokens` is always a subset of it. Cleaner for consumers, and more in the
spirit of a "provider-normalized" library — but it changes the `PromptTokens` value existing Anthropic
and Bedrock callers see whenever caching is active, which is a behavior change even though no signature
changes.

Recommendation: ship **Option A** to honor the "don't change how the API is perceived" constraint,
with the per-provider semantics spelled out in the XML docs. If a later major version wants the
normalized meaning, Option B becomes a documented, opt-in-by-version change. The field-level XML doc
should state plainly: *"On OpenAI and Gemini this is a subset of `PromptTokens`; on Anthropic and
Bedrock it is additional to `PromptTokens`."*

## Proposed model change

Add three nullable properties to `ChatStreamingUsage`. Nullable so "not reported" stays distinct from
"reported as zero," and so existing serialized output is unchanged for providers that do not populate
them.

```csharp
/// <summary>
/// Prompt tokens served from the provider's prompt cache (a cache read). Null when the provider does
/// not report cache usage. Note the cross-provider semantic: on OpenAI and Gemini this is a subset of
/// <see cref="PromptTokens"/>; on Anthropic and Bedrock it is additional to <see cref="PromptTokens"/>.
/// </summary>
public int? CachedPromptTokens { get; set; }

/// <summary>
/// Prompt tokens written into the provider's prompt cache (a cache-creation/write), billed at a
/// premium by providers that report it (Anthropic, Bedrock). Null when unreported.
/// </summary>
public int? CacheCreationTokens { get; set; }

/// <summary>
/// Reasoning/thinking tokens billed separately by the model (OpenAI reasoning models, Gemini
/// thinking). Null when the provider does not report a distinct reasoning-token count (Anthropic,
/// Bedrock, Ollama).
/// </summary>
public int? ReasoningTokens { get; set; }
```

## Change list (streaming-only, minimal footprint)

1. **`src/PolyPrompt/Models/ChatStreamingUsage.cs`** — add the three nullable properties above.

2. **`src/PolyPrompt/Clients/OpenAiClient.cs`** — in both usage blocks (`ReadOpenAiChatChunks` ~line
   1048 and `ReadOpenAiToolChatChunks` ~line 1149), after reading `prompt_tokens`/`completion_tokens`,
   read `usage.prompt_tokens_details.cached_tokens` → `CachedPromptTokens` and
   `usage.completion_tokens_details.reasoning_tokens` → `ReasoningTokens`. Azure
   (`AzureOpenAiClient : OpenAiClient`) inherits this with no further change.

3. **`src/PolyPrompt/Clients/GeminiClient.cs`** — in `ParseGeminiUsageMetadata` (line 1414) and the
   inline block in `ReadGeminiChatChunks` (~line 1246), read `usageMetadata.cachedContentTokenCount` →
   `CachedPromptTokens` and `usageMetadata.thoughtsTokenCount` → `ReasoningTokens`. Vertex
   (`VertexAiClient : GeminiClient`) inherits this. Note Gemini sends cumulative usage on every chunk,
   so the last-wins copy already yields the final totals.

4. **`src/PolyPrompt/Clients/AnthropicClient.cs`** — capture `cache_read_input_tokens` and
   `cache_creation_input_tokens` from the `message_start` usage object (the prompt-token capture sites
   at ~line 1049 / ~line 1143) and thread them into `ParseAnthropicStreamUsage` (line 1302) alongside
   the existing `promptTokens`, setting `CachedPromptTokens` and `CacheCreationTokens`. No reasoning
   field. Under Option A, leave the `PromptTokens = input_tokens` assignment untouched.

5. **`src/PolyPrompt/Clients/BedrockClient.cs`** — in `ReadMetadataUsage` (line 1153), read
   `usage.cacheReadInputTokens` → `CachedPromptTokens` and `usage.cacheWriteInputTokens` →
   `CacheCreationTokens`. No reasoning field.

6. **`src/PolyPrompt/Clients/OllamaClient.cs`** — no change; the provider reports none of these, so the
   fields remain null.

7. **`src/PolyPrompt/Clients/CompletionClientBase.cs`** — no change; usage flows through the existing
   last-non-null copy unchanged.

Each parser already guards field reads with `ContainsKey` + `TryParse`/`TryGetInt`, so the additions
follow the established pattern and are null-safe when a provider omits the nested object (for example an
OpenAI request without `stream_options.include_usage`, or a non-cached Anthropic request).

## Non-breaking assessment

Adding nullable properties to `ChatStreamingUsage` is source- and binary-compatible:

- It is a plain `public class` with public get/set auto-properties — no constructor, not a record, no
  positional/`init`-only members, no compiler-generated `Deconstruct`/`Equals`/`with` to disturb.
- No serialization attributes exist on the model (`SerializationHelper` is used only to parse inbound
  provider JSON, never to (de)serialize these public types), so there is no serializer contract or
  member ordering to break.
- New members default to `null`, so existing behavior, existing values, and existing serialized shape
  are unchanged for every current caller and every provider that does not populate them.
- No method signature or interface changes.

The only observable difference for an existing user is the presence of three new null-by-default
properties — additive and expected. Under Option A, no previously returned value changes.

## Optional: non-streaming exposure

If callers need usage on the non-streaming paths too, that is a larger but still additive change: add a
`public ChatStreamingUsage? Usage { get; set; }` to `ChatResponse` and `ToolChatResponse` (both plain
public classes, so additive/non-breaking), and populate it in each client's non-streaming parse site
(`new ChatResponse()` at OpenAiClient:61, AnthropicClient:136, GeminiClient:56, BedrockClient:92,
OllamaClient:81; `new ToolChatResponse()` at OpenAiClient:215, AnthropicClient:258, GeminiClient:170,
BedrockClient:210, OllamaClient:216). This is worth doing only if a consumer actually uses the
non-streaming API; the streaming change above covers the streaming callers that dominate real usage.

## Testing

The token assertions live in `src/Test.Shared/LocalBehaviorSuite.cs`, driven by the mock server in
`src/Test.Shared/LocalOpenAiTestServer.cs`. Extend both:

- Add the cached/reasoning fields to the mock usage payloads for each provider (OpenAI
  `prompt_tokens_details`/`completion_tokens_details`, Anthropic `cache_read_input_tokens`/
  `cache_creation_input_tokens`, Gemini `cachedContentTokenCount`/`thoughtsTokenCount`, Bedrock
  `cacheReadInputTokens`/`cacheWriteInputTokens`).
- Extend the existing per-provider streaming and tool-chat assertions (OpenAI ~line 668/967, Anthropic
  ~2296, Gemini ~1203, Bedrock ~3339, Ollama ~691) to assert the new fields — including asserting they
  stay null for providers that do not report them (Ollama, and reasoning on Anthropic/Bedrock).
- Add one assertion that pins the chosen semantic (Option A): for a cached Anthropic fixture, assert
  `PromptTokens` still equals `input_tokens` and `CachedPromptTokens` equals the cache-read value, so a
  future accidental normalization is caught.

The change touches five parsers, one model, and the test fixtures — no public surface is removed or
altered, and the behavior of every existing call is preserved.
