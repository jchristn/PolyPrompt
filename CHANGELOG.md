# Changelog

## v2.7.1 (2026-09-27)

### Added

- `ChatCompletionOptions.ReasoningEffort` sets the reasoning ("thinking") effort for a single plain chat call (`ChatAsync` and `ChatStreamingAsync`), which previously could only be set on tool chat. It uses the existing `ReasoningEffort` value object and its per-provider projections: Ollama `think`, OpenAI and Azure OpenAI `reasoning_effort`, Gemini and Vertex AI `thinkingConfig.thinkingBudget`, Anthropic `output_config.effort` and `thinking`, Cohere `thinking`, and Bedrock's thinking budget. `ReasoningEffortLevel.Minimal` turns thinking off where the provider allows it, which keeps short structured calls (ratings, classifications, rewrites) fast on thinking models.
- The property defaults to null, which sends no reasoning field, so existing calls send exactly the same requests. The client-level `CompletionClientBase.ReasoningEffort` default still applies to tool chat only. The change is additive: no public member was changed or removed.

### Tests

- Added 7 hermetic cases (170 local cases in total, all green in `Test.Automated`, `Test.Xunit`, and `Test.Nunit` on net8.0 and net10.0) covering plain chat and streaming for each provider, thinking turned off at `Minimal`, the Gemini thinking budget sitting beside the sampling settings, and that no reasoning field is sent when the option is unset, even with a client-level default.

## v2.7.0 (2026-09-27)

### Added

- Added three operations to `CompletionClientBase`: `RerankAsync(query, documents, RerankOptions?, token)`, `ClassifyAsync` (single and batch), and `EmbedSparseAsync` (single and batch). They are `virtual` and throw `NotSupportedException` by default without sending a request, the same pattern as `PullModelAsync`/`DeleteModelAsync`, so the change is additive and non-breaking for existing subclasses.
- Added the models for these operations: `RerankOptions` (`Model`, `TopN`, `ReturnDocuments`), `RerankResponse` (`Results`, `ResponseId`, `TotalTokens`, `SearchUnits`), `RerankResult` (`Index`, `Score`, `Document`), `ClassificationOptions`, `ClassificationResponse`, `ClassificationResult` (top `Label`/`Score` plus every scored label), `ClassificationLabel`, `ClassificationExample`, `SparseEmbeddingOptions`, `SparseEmbeddingResponse`, `SparseEmbeddingResult`, and `SparseValue`.
- Rerank behaves identically on every provider that supports it. Results are always sorted by score (highest first, ties by index), results whose index does not map to an input document are dropped, `TopN` is sent natively where the provider supports it and applied client-side otherwise, and `ReturnDocuments` attaches document text from the caller's list without requesting it from the provider. Invalid arguments throw before any request: `ArgumentNullException` for a null query or document list, `ArgumentException` for an empty or whitespace query, an empty document list, or a null document, and `ArgumentOutOfRangeException` for `TopN` greater than the document count (the `RerankOptions.TopN` setter also rejects values below 1). HTTP errors still return `Success = false` with the status code and error text instead of throwing.
- Added **Cohere** through `CohereClient`: chat, tool chat, and streaming on the v2 Chat API (typed SSE events, with tool-call arguments reassembled across `tool-call-delta` events), text generation mapped onto single-turn v2 chat, embeddings (`/v2/embed`), reranking (`/v2/rerank`), classification (`/v1/classify`, few-shot examples or a fine-tuned model), paginated model listing and lookup (`/v1/models`), and connectivity validation. Cohere uses separate default models per operation (`Model` = `command-a-03-2025`, `EmbeddingModel` = `embed-v4.0`, `RerankModel` = `rerank-v3.5`, `ClassificationModel` = null). Reasoning maps to `thinking.token_budget`; thinking content and the tool plan are surfaced as `Reasoning` and never sent back. Usage prefers raw token counts and falls back to billed units, with `cached_tokens` as `CachedPromptTokens`. Cohere's `tool_choice` only accepts `REQUIRED`/`NONE`, so a named tool is expressed by sending only that tool with `REQUIRED`. Added `CohereChatCompletionOptions`, `CohereGenerationOptions`, `CohereEmbeddingOptions`, `CohereRerankOptions`, and `CohereClassificationOptions`.
- Added **Hugging Face Text Embeddings Inference** through `TeiClient`: dense embeddings (`/embed`), sparse embeddings (`/embed_sparse`), reranking (`/rerank`), classification (`/predict`, always sent in batch form so two inputs are never read as a text pair), model listing and lookup from `/info` (the hosted model, with `model_type` and limits in `Metadata`), and connectivity validation via `/health`. Authentication is optional (bearer, for servers started with `--api-key`). Chat, tool chat, and generation throw `NotSupportedException`. Added `TeiEmbeddingOptions`, `TeiRerankOptions`, `TeiClassificationOptions`, and `TeiSparseEmbeddingOptions`.
- Added reranking to **VoyageAI** (`/v1/rerank`, new `VoyageAiClient.RerankModel`, default `rerank-2.5`, and `VoyageAiRerankOptions`) and to **AWS Bedrock** (`InvokeModel` with `cohere.rerank-v3-5:0` by default or `amazon.rerank-v1:0`, SigV4-signed, new `BedrockClient.RerankModel` and `BedrockRerankOptions`).
- `TeiClient.ValidateConnectivityAsync` probes `/health` and `CohereClient.ValidateConnectivityAsync` probes a one-entry `/v1/models` page. Both return false on an HTTP error, an unreachable server, or a request timeout, and rethrow only caller cancellation. Cohere overrides the base implementation because the base returns true when model listing ends early on an HTTP error.
- Added the Cohere projection to `ReasoningEffort`: a clamped `CohereThinkingBudget` override (0..32768) and `ToCohereThinkingBudget()` (Minimal turns thinking off, Low 1024, Medium 4096, High 16384).
- Added the `CohereConsole` (chat, tool chat, embeddings, `rr`/`rerank`, `cl`/`classify`, generation, models) and `TeiConsole` (embed, sparse, rerank, classify, info) test harnesses to the solution, and a `rr`/`rerank` command to `VoyageAIConsole`.

### Tests

- Added 41 hermetic Touchstone cases, for 163 in total, all green across `Test.Automated selftest`, `Test.Xunit`, and `Test.Nunit` on net8.0 and net10.0. Positive cases cover request translation, headers, response parsing, usage, streaming, tool calls and follow-up turns, tool-choice mapping, reasoning projection and capture, option normalization, pagination, and model lookup for Cohere; embeddings, sparse embeddings, rerank, classification (including the batch form), `/info`, and `/health` for TEI; and rerank translation for VoyageAI and Bedrock (including the SigV4 signature and the Cohere-only `api_version`). Shared cases prove result ordering, index mapping, `TopN` handling (native and client-side), and `ReturnDocuments` on all four rerank providers.
- Negative cases prove that every provider without rerank, classify, or sparse embeddings throws `NotSupportedException` without sending a request (even when the arguments are also invalid); that invalid rerank, classify, sparse, and embed inputs throw the documented exception types before any request; that TEI's 413, 422, 424, and 429 errors and Cohere's 400, 404, and 429 errors surface as unsuccessful responses without throwing; that unmappable rerank indexes are dropped and empty results are a success; that a Cohere `ERROR` finish reason, malformed tool arguments, and a response without a message are handled; that streaming timeouts cover the response body; that connectivity validation returns false on HTTP errors, unreachable servers, and timeouts while still propagating caller cancellation; and that pre-cancelled tokens propagate from every new operation.
- Extended the live suite (`ProviderLiveSuite`) with `rerank` and `classify` cases that assert success on supporting providers and `NotSupportedException` elsewhere, plus Cohere and TEI configuration (`--cohere-*`, `--tei-*`, `--rerank-model`, and the `POLYPROMPT_TEST_COHERE_*`, `POLYPROMPT_TEST_TEI_*`, and `POLYPROMPT_TEST_RERANK_MODEL` environment variables). TEI live cases read the hosted model type from `/info` and assert success for the operations it serves and a clean HTTP 424 failure for the rest.
- Verified live against two real TEI 1.9.4 servers (`BAAI/bge-reranker-base` and `BAAI/bge-small-en-v1.5`) and against Ollama (`gemma3:4b`, `all-minilm`), all green. The Cohere, VoyageAI rerank, and Bedrock rerank paths are covered by the hermetic suite; live runs for them require provider credentials.

### Changed

- README updated for ten providers (intro, capability tables, Cohere and TEI quick starts, reranking, classification, and sparse embedding examples, options and default-model tables, feature matrix, project structure, and test instructions); package description and tags updated; package version is now 2.7.0.

## v2.6.0 (2026-09-10)

### Added

- Added **cached-prompt-token and reasoning-token accounting** to the usage surface. `ChatStreamingUsage` gains three nullable properties — `CachedPromptTokens` (prompt tokens served from the provider's prompt cache), `CacheCreationTokens` (prompt tokens written into the cache, billed at a premium), and `ReasoningTokens` (tokens billed separately for reasoning/thinking). All are `null` when a provider does not report them, so cost and cache-hit-rate accounting no longer has to be reconstructed from `PromptTokens`/`CompletionTokens` alone.
- Populated these fields from every provider's native usage payload: OpenAI/Azure OpenAI (`prompt_tokens_details.cached_tokens`, `completion_tokens_details.reasoning_tokens`), Gemini/Vertex (`cachedContentTokenCount`, `thoughtsTokenCount`), Anthropic (`cache_read_input_tokens`, `cache_creation_input_tokens`; no distinct reasoning count), Bedrock (`cacheReadInputTokens`, `cacheWriteInputTokens`; no distinct reasoning count), and Ollama (reports none, so the fields stay `null`). Azure and Vertex inherit the OpenAI and Gemini parsers unchanged.
- Exposed usage on the **non-streaming** paths for consistency: `ChatResponse` and `ToolChatResponse` gain a nullable `Usage` property (`ChatStreamingUsage`), populated by every provider's non-streaming chat and tool-chat parse. Telemetry is now identical whether a caller uses the streaming or non-streaming API.
- Cross-provider semantic (Option A, additive and non-breaking): `PromptTokens` keeps its provider-native meaning — on OpenAI/Azure/Gemini/Vertex the cached count is a **subset** of `PromptTokens`; on Anthropic/Bedrock the cache buckets are **additional** to `PromptTokens` (which counts only the uncached input). This is documented on each field's XML docs and pinned by tests.

### Tests

- Extended the hermetic Touchstone suite (green across `Test.Automated selftest`, `Test.Xunit`, and `Test.Nunit` on net8.0 and net10.0) with positive and negative assertions for the new fields on both the streaming and non-streaming paths of all providers: cache and reasoning tokens are asserted where a provider reports them, and asserted `null` where it does not (Ollama for everything, reasoning on Anthropic/Bedrock, cache-creation on OpenAI/Gemini). Anthropic and Bedrock cases pin the Option A semantic by asserting `PromptTokens` remains the uncached input count while the cache buckets carry the cached values. The local test server's mock usage payloads were enriched with the corresponding cache/reasoning fields.
- Verified live against Ollama (`gemma3:4b`) and Gemini (`gemini-2.5-flash`): usage populates identically on the streaming and non-streaming paths, with Gemini's reasoning-token count confirmed end-to-end and Ollama's cache/reasoning fields correctly `null`.

### Changed

- README updated with a usage/token-accounting section documenting the new fields and the cross-provider cached-token semantic; package version is now 2.6.0.

## v2.5.0 (2026-09-07)

### Added

- Added a per-request credential seam to `CompletionClientBase`: a new `protected virtual PrepareRequestAsync(HttpRequestMessage, byte[] body, CancellationToken)` hook invoked after the request and body are built and before send. The non-streaming `PostAndRecordAsync`/`GetAndRecordAsync` paths were rewritten to construct an explicit `HttpRequestMessage` and use `SendAsync` (previously they used the `PostAsync`/`GetAsync` convenience methods, which expose no request to mutate); `PostStreamingAsync` now calls the hook too. The change is additive and non-breaking — existing clients override nothing and their behavior is unchanged (verified by the unchanged OpenAI/Ollama/Gemini/Anthropic/VoyageAI hermetic suites). Recorded `CompletionCallDetail.RequestHeaders` now reflect the effective request headers, including per-request credentials.
- Added a hand-rolled auth layer under `PolyPrompt.Auth`, with no new package dependencies:
  - `SigV4Signer` — AWS Signature Version 4 (header-based), verified against AWS's published canonical test vector (IAM `ListUsers`), with public low-level building blocks (`CreateCanonicalRequest`, `CreateStringToSign`, `DeriveSigningKey`, `HexSha256`).
  - `AwsCredentials` plus `IAwsCredentialProvider` with `StaticAwsCredential` and `EnvironmentAwsCredential` (`AWS_ACCESS_KEY_ID`/`AWS_SECRET_ACCESS_KEY`/`AWS_SESSION_TOKEN`, region from `AWS_REGION`/`AWS_DEFAULT_REGION`).
  - `ICredentialProvider` (short-lived bearer tokens) with `StaticTokenCredential`, a caching base `CachingCredentialProvider` (refresh with a safety margin, serialized refresh), `ServiceAccountCredential` (RS256 JWT assertion → token exchange, no Google SDK), and `AdcCredential` (Application Default Credentials: `GOOGLE_APPLICATION_CREDENTIALS` key file or the GCE/Cloud Run metadata server).
- Added **Azure OpenAI** through `AzureOpenAiClient : OpenAiClient`. Routes operations under `/openai/deployments/{deployment}/{op}` with a settable `ApiVersion` (`api-version` query, default `2024-10-21`); the model **is** the deployment name. Authenticates with either an `api-key` header or an Azure AD bearer token (via `ICredentialProvider`, refreshed per request). All request/response bodies, tool-call delta assembly, reasoning (`reasoning_effort`), streaming SSE, and usage are inherited from `OpenAiClient` unchanged. `OpenAiClient.BuildApiUrl` was promoted to `protected virtual` to enable this (behavior-preserving). Added `AzureOpenAiChatCompletionOptions` and `AzureOpenAiEmbeddingOptions`.
- Added **Google Vertex AI** through `VertexAiClient : GeminiClient`. Reuses the entire Gemini `generateContent` wire (chat, tool chat, streaming, reasoning/thinking budget) and overrides only URL routing (`/v1/projects/{project}/locations/{region}/publishers/google/models/{model}:generateContent`), auth (OAuth bearer per request via `ICredentialProvider`), and embeddings (the `:predict` endpoint with an `instances` body and `predictions[].embeddings.values` response). `GeminiClient` URL construction was extracted into `protected virtual` builders (`BuildGenerateContentUrl`, `BuildListModelsUrl`, `BuildGetModelUrl`) as a behavior-preserving refactor. Model management is not exposed for Vertex and throws `NotSupportedException`; `ValidateConnectivityAsync` probes with a minimal `:predict` request. Added `VertexAiEmbeddingOptions` (`TaskType`, `Title`, `OutputDimensionality`, `AutoTruncate`).
- Added **AWS Bedrock** through `BedrockClient`, using the unified `Converse`/`ConverseStream` API so tools, reasoning, and usage normalize across model families with minimal branching. Chat, tool chat (`toolSpec`/`toolResult`/`toolUse`, including consecutive tool-result merging), streaming, generation (mapped onto single-turn Converse), and reasoning (Anthropic extended thinking via `additionalModelRequestFields.thinking.budget_tokens`) are supported. Streaming decodes the AWS binary event-stream (`application/vnd.amazon.eventstream`) via a new `EventStreamDecoder` (`PolyPrompt.Wire`) with prelude/message CRC-32 validation. Embeddings use `InvokeModel` for Amazon Titan (`inputText` → `embedding`, per-input) and Cohere (`texts`/`input_type` → `embeddings`, native batch). Model listing/lookup use the Bedrock control-plane `foundation-models` endpoint (also SigV4-signed). Every request is SigV4-signed via the credential seam. Added `BedrockEmbeddingOptions` (`InputType`, `Dimensions`, `Normalize`).
- Added the Bedrock projection to `ReasoningEffort`: a clamped `BedrockThinkingBudget` override (0..65536) and `ToBedrockThinkingBudget()` (Minimal→0/off, Low→1024, Medium→4096, High→16384), mirroring the Anthropic projection. Azure reuses `ToOpenAiWireValue()` and Vertex reuses `ToGeminiThinkingBudget()` — no other reasoning changes.

### Tests

- Added 30 hermetic Touchstone cases (all green across `Test.Automated selftest`, `Test.Xunit`, and `Test.Nunit` on net8.0 and net10.0), covering positive and negative directions for every new provider: Azure (deployment/api-version routing, api-key and AAD-bearer auth, embeddings, streaming tool chat, reasoning_effort passthrough, missing-credentials 401); Vertex (publisher-path routing, per-request OAuth bearer, `:predict` embeddings, streaming tool chat, thinking-budget mapping, model-management `NotSupportedException`); Bedrock (Converse translation, SigV4 signature shape, toolUse assembly, event-stream streaming + tool streaming, Titan and Cohere embeddings, Converse thinking, HTTP error). Added unit coverage for `SigV4Signer` (AWS canonical vector, Authorization shape, session-token signing), `EventStreamDecoder` (round-trip and CRC corruption detection), and the credential seam (per-request resolution and caching reuse). The local test server was extended to route and respond for all three providers, including byte-accurate event-stream frames via `EventStreamDecoder.EncodeMessage`.
- Live opt-in provider coverage (`ProviderLiveSuite`) for Azure/Vertex/Bedrock is a documented follow-up; the hermetic suite fully exercises each new provider's translation, auth, streaming, embeddings, and error handling without network access.

### Changed

- README updated to cover all eight providers (intro, quick starts, feature matrix, options and default-model tables, authentication section for SigV4 and OAuth/AAD credentials) and the package version and tags (`azure;vertex;bedrock;aws;gcp;sigv4`); package version is now 2.5.0.

## v2.4.1 (2026-09-01)

### Changed

- Package version bumped to 2.4.1.

## v2.4.0 (2026-08-31)

### Added

- Added VoyageAI (https://docs.voyageai.com) as a fifth provider through `VoyageAiClient`, an **embeddings-only** provider targeting `POST /v1/embeddings` with bearer authentication and a default model of `voyage-3.5`. Single embeddings delegate to the batch path, and both parse the OpenAI-shaped `data[]` response into indexed float vectors. Everything completion-shaped is intentionally unsupported and throws `NotSupportedException` without touching the network: chat, streaming chat, tool chat, streaming tool chat, generation, streaming generation, model listing, model existence, model information, pull, and delete. `ModelExistsAsync` is explicitly overridden to throw rather than inheriting the base implementation, which would have silently returned false.
- Added `VoyageAiEmbeddingOptions` with `InputType` (`query`/`document`, normalized, unrecognized reverts to null), `Truncation` (nullable bool), `OutputDimension` (clamped to the documented 256/512/1024/2048 set), and `OutputDtype` (`float`/`int8`/`uint8`/`binary`/`ubinary`, normalized).
- Added a VoyageAI-specific `ValidateConnectivityAsync` override that probes with a minimal one-word embeddings request, since VoyageAI has no model listing endpoint for the base probe to use.
- Added the `VoyageAIConsole` interactive test harness to the solution with an embeddings-focused menu (single/batch embed with input-type and output-dimension prompts, connectivity validation, settings).
- Added local Touchstone coverage: request translation (model, array-form `input`, `input_type`, `truncation`, `output_dimension`, `output_dtype`, bearer header, `/v1/embeddings` path), response parsing for single and batch, option normalization/clamping, and the connectivity probe. Negative cases prove all eleven unsupported operations throw `NotSupportedException` with nothing reaching the wire, HTTP errors surface without throwing, connectivity validation returns false on unreachable endpoints, and pre-cancelled tokens propagate `OperationCanceledException` from embeds and connectivity validation.
- Added VoyageAI live-provider support to `Test.Automated` (`--voyageai-key`, `--voyageai-endpoint`, `--voyageai-model`, `--voyageai-embedding-model`) and the `POLYPROMPT_TEST_VOYAGEAI_*` environment variables. Chat, tool-chat, generation, and required-models live cases skip with reasons for VoyageAI; model-management live cases assert the unsupported behavior; call-details and cancellation cases branch to the embeddings surface.

### Changed

- Updated README documentation, including a per-provider capability matrix now covering all five providers, the VoyageAI quick start, options and defaults tables, project structure, and test instructions; package version is now 2.4.0.

## v2.3.0 (2026-08-31)

### Added

- Added Anthropic (Claude) as a fourth provider through `AnthropicClient`, targeting the Anthropic Messages API (`POST /v1/messages`). Supported surface: chat (non-streaming and streaming over SSE), tool chat (non-streaming and streaming, including `tool_use` content blocks, split `input_json_delta` argument accumulation, and tool-result follow-up turns as user-role `tool_result` blocks), text generation mapped onto the Messages API as a single-user-turn request, model listing with `has_more`/`last_id` pagination, model lookup, model existence checks, connectivity validation, call recording, timing metrics, and token usage. Two gaps are intentional and explicit: Anthropic has no embeddings API, so both `EmbedAsync` overloads throw `NotSupportedException`, and `PullModelAsync`/`DeleteModelAsync` throw `NotSupportedException` like the other cloud providers.
- Added Anthropic authentication via the `x-api-key` and `anthropic-version` request headers (no bearer Authorization). The version value is configurable through `AnthropicClient.AnthropicVersion` (default `2023-06-01`), and identity-linked API keys are supported through `AnthropicClient.WorkspaceId`, which sends the `anthropic-workspace-id` header.
- Added the Anthropic projection to `ReasoningEffort`: levels map to `output_config.effort` (`low`/`low`/`medium`/`high`) with adaptive thinking (`thinking: {type: "adaptive", display: "summarized"}`) sent for `Low` and above and omitted for `Minimal`. A clamped `AnthropicEffort` override (`low`/`medium`/`high`/`xhigh`/`max`) wins over the level default, with `ToAnthropicEffort()` and `SendsAnthropicThinking()` projection methods.
- Added Anthropic reasoning ("thinking") capture: `thinking` content blocks and streamed `thinking_delta` events surface as `Reasoning`/`ReasoningText`, kept separate from answer text and never carried into follow-up requests.
- Added `AnthropicChatCompletionOptions` and `AnthropicGenerationOptions` with `TopK` and `StopSequences`. No embedding options class exists because embeddings are unsupported.
- Added the `AnthropicConsole` interactive test harness to the solution, mirroring the other provider consoles including `tc`/`toolchat` and streamed reasoning display.
- Added local Touchstone coverage for the Anthropic client: request translation (top-level `system`, mandatory `max_tokens`, `x-api-key`/`anthropic-version` headers, workspace header add/remove, no bearer header), streaming chat and generation, tool chat and streaming tool chat with split-argument accumulation and merged tool-result turns, tool-choice mapping (`auto`/`any`/`none`/named tool), per-request model overrides, model list pagination, reasoning-effort projection and override clamping, and reasoning capture. Negative cases prove embeddings and pull/delete throw `NotSupportedException`, HTTP errors surface without throwing, a `refusal` stop reason is a successful response with the finish reason exposed, empty thinking normalizes to null, thinking never leaks into text or follow-up messages, and `TimeoutMs` covers streaming body enumeration.
- Added Anthropic live-provider support to `Test.Automated` (`--anthropic-key`, `--anthropic-endpoint`, `--anthropic-model`, `--anthropic-workspace`) and the `POLYPROMPT_TEST_ANTHROPIC_*` environment variables; live embedding cases skip for Anthropic and pull/delete assert the unsupported behavior.

### Changed

- Updated README documentation, the provider capability matrix, default model table, and package metadata for the fourth provider; package version is now 2.3.0.

## v2.2.1 (2026-08-15)

### Changed

- Updated the `SyslogLogging` dependency from 2.0.13 to 2.2.1. No PolyPrompt API or behavior changes.

### Tests

- Updated test tooling: `Microsoft.NET.Test.Sdk` (17.14.1 to 18.9.0), `coverlet.collector` (6.0.4 to 10.0.1), `xunit.runner.visualstudio` (3.1.4 to 4.0.0), `NUnit` (4.3.2 to 4.6.1), `NUnit.Analyzers` (4.7.0 to 4.14.0), and `NUnit3TestAdapter` (5.0.0 to 6.2.0).
- Hardened the local test HTTP server against a port-binding race: `LocalOpenAiTestServer.Start()` now retries on a fresh port when a concurrent process claims the selected port between allocation and bind, fixing an intermittent `HttpListenerException` (Win32 error 32) under parallel/loaded test hosts.

## v2.2.0 (2026-08-12)

### Added

- Added reasoning ("thinking") capture returned to the caller. Streamed chunks expose `ReasoningText` and responses expose an accumulated `Reasoning`, on both chat (`ChatStreamingChunk`/`ChatStreamingResponse`/`ChatResponse`) and tool chat (`ToolChatStreamingChunk`/`ToolChatStreamingResponse`/`ToolChatResponse`), across the OpenAI-compatible, Ollama, and Gemini clients — parsed from `reasoning_content` (fallback `reasoning`), `message.thinking`, and Gemini `thought` parts respectively. Reasoning is kept separate from answer text, normalized to null when absent or empty, accumulated to match the streamed deltas, and is return-only (never carried into a follow-up request via `ToAssistantMessage`).
- Added local Touchstone coverage for reasoning capture and accumulation per provider and path, plus negative cases proving no-reasoning responses stay null, empty reasoning normalizes to null, reasoning never leaks into text, and reasoning is not resent in a follow-up message.

### Changed

- Centralized provider reasoning field-name literals as per-client constants (`reasoning_content`/`reasoning`, `thinking`, `thought`) to reduce the fragility of inlined wire-field strings.

## v2.1.0 (2026-08-12)

### Added

- Added a provider-neutral `ReasoningEffort` control for reasoning-capable models. `ToolChatRequest.ReasoningEffort` and a matching `CompletionClientBase.ReasoningEffort` instance default carry a semantic `ReasoningEffortLevel` (`Minimal`/`Low`/`Medium`/`High`) plus optional, individually clamped per-provider overrides (`OpenAiValue`, `GeminiThinkingBudget`, `OllamaThink`). The value object projects itself onto each provider — OpenAI `reasoning_effort`, Gemini `generationConfig.thinkingConfig`, and Ollama `think` — and is omitted entirely when unset, preserving existing request output.
- Added the `ReasoningEffort` value object and `ReasoningEffortLevel` enum in `PolyPrompt.Models`, with static level presets, an implicit conversion from the level, setter clamping/validation on every override, and `ToOpenAiWireValue()`/`ToGeminiThinkingBudget()`/`ToOllamaThink()` projections.
- Added local Touchstone coverage for reasoning-effort translation across OpenAI, Gemini, and Ollama tool chat (streaming and non-streaming), instance-default vs. per-request precedence, per-provider override and setter-clamping behavior, undefined-level guarding, and a backward-compatibility case proving no reasoning field is sent by default.

## v2.0.1 (2026-07-30)

### Added

- Added an optional `HttpClient` parameter to `CompletionClientBase` and the `OpenAiClient`, `OllamaClient`, and `GeminiClient` constructors. When supplied, the client uses the injected transport — letting callers configure a custom `HttpClientHandler` (for example to relax TLS certificate validation for self-signed endpoints, or to route through a proxy) — and does not dispose it; the caller retains ownership. When omitted, an internally owned client is created as before, preserving existing behavior.
- Added local Touchstone coverage verifying the injected `HttpClient` carries requests and is not disposed when the client is disposed.

## v2.0.0 (2026-07-24)

### Added

- Added streaming tool chat through `ToolChatStreamingAsync` on `CompletionClientBase`.
- Added `ToolChatStreamingResponse`, `ToolChatStreamingChunk`, and `ToolCallDelta` models for streamed assistant text, tool-call deltas, accumulated final tool calls, timing, usage, and finish metadata.
- Added OpenAI-compatible streaming tool chat over `/v1/chat/completions` with SSE `delta.tool_calls` parsing and split argument accumulation.
- Added Ollama streaming tool chat over `/api/chat` with streamed `message.tool_calls` parsing and accumulated tool-call output.
- Added Gemini streaming tool chat over `models/{model}:streamGenerateContent?alt=sse` with streamed `text`, `functionCall`, finish, response id, model version, and usage metadata parsing.
- Added local Touchstone coverage for OpenAI-compatible, Ollama, and Gemini streaming tool-call flows, multiple streamed tool calls, split argument accumulation, streamed final responses after tool results, HTTP error handling, and streaming body timeout behavior.
- Added `tc/toolchat` to the OpenAI, Ollama, and Gemini interactive console harnesses. The command uses the existing streaming toggle to exercise either `ToolChatAsync` or `ToolChatStreamingAsync` with a sample `get_weather` tool and tool-result follow-up turn.
- Added named live-provider test configuration for `Test.Automated`, including `--openai-key`, `--ollama-endpoint`, `--gemini-key`, provider-specific model arguments, and default public endpoints for OpenAI and Gemini.
- Expanded Touchstone coverage with deterministic streaming chat, streaming generation, tool-choice translation, per-request model overrides, provider-wide streaming HTTP error handling, and live provider tool-chat and streaming tool-chat cases.
- Added live provider test handling for model-level tool capability errors so non-tool Ollama models such as `gemma3:4b` validate the provider error path instead of failing the whole suite.
- Increased Ollama and OpenAI-compatible live-test token budgets so reasoning-capable models such as `gpt-oss:20b` have room to emit final content after reasoning output.
- Added OpenAI-compatible client support for endpoints that already include `/v1`, enabling Ollama OpenAI API URLs such as `http://localhost:11434/v1`.

### Changed

- `CompletionClientBase` now requires concrete clients to implement `ToolChatStreamingAsync`; this is a source-breaking change for external subclasses.
- Updated README documentation and provider capability matrix to distinguish non-streaming tool chat, streaming tool chat, and unsupported provider capabilities.

## v1.5.0 (2026-07-11)

### Added

- Added provider-normalized tool calling through `ToolChatAsync` on `CompletionClientBase`.
- Added `ToolChatRequest`, `ToolChatResponse`, `ChatMessage`, `ToolDefinition`, and `ToolCall` models.
- Added OpenAI-compatible `/v1/chat/completions` tool declarations, assistant `tool_calls`, and tool-result follow-up message support.
- Added Ollama `/api/chat` tool declarations, tool-call parsing, and tool-result follow-up message support.
- Added Gemini `functionDeclarations`, `functionCall`, `functionResponse`, `systemInstruction`, and tool choice mapping.
- Added local Touchstone coverage for tool-chat validation and OpenAI-compatible, Ollama, and Gemini tool-call flows.
- Expanded local Touchstone coverage for option clamping, guard clauses, provider chat request translation, embeddings, generation, model listing, model information, Ollama pull/delete behavior, unsupported provider operations, and HTTP error handling.

### Changed

- Updated package metadata to version `1.5.0` and expanded package tags for tool/function calling.
- Updated README documentation with the explicit `ToolChatAsync` developer flow and provider support matrix.

## v0.2.0 (2026-06-04)

### Changed

- `TimeoutMs` now preserves positive values exactly and throws for zero or negative values instead of silently clamping.
- HTTP request timeouts are enforced with per-call linked cancellation tokens instead of mutating `HttpClient.Timeout`.
- Streaming chat and generation timeouts now cover response body enumeration, not only response header retrieval.
- `ValidateConnectivityAsync` now propagates `OperationCanceledException`.
- `CallDetails` now returns detached snapshots, is recorded through a thread-safe bounded buffer, and defaults to retaining the latest 1,000 entries.
- Refactored automated tests into Touchstone package-based projects: `Test.Shared`, `Test.Automated`, `Test.Xunit`, and `Test.Nunit`.

### Added

- Added `MaxCallDetails` to configure retained call-detail capacity, including `0` to disable recording.
- Added `ClearCallDetails()` for long-lived clients.
- Added local `Test.Automated selftest` coverage for timeout behavior, streaming body cancellation, disposed non-streaming responses, call-detail retention, and cancellation propagation.
- Added xUnit and NUnit adapters over the shared Touchstone test descriptors.

### Fixed

- Non-streaming HTTP helpers now dispose upstream `HttpResponseMessage` instances after copying status, headers, and body into `CompletionHttpResult`.

## v0.1.0 (2026-03-03)

Initial release.

### Features

- **Chat Completions** — Streaming and non-streaming chat completions with system prompt support for Ollama, OpenAI, and Gemini
- **Text Generation** — Streaming and non-streaming text generation (completion-style) for Ollama and Gemini
- **Embeddings** — Single and batch embedding generation for all three providers
- **Provider-Specific Options** — Fine-grained control via `OllamaChatCompletionOptions`, `OpenAiChatCompletionOptions`, `GeminiChatCompletionOptions`, and corresponding embedding/generation option classes
- **Streaming Metrics** — Built-in timing calculations including time-to-first-token, time-to-last-token, tokens/sec, and inter-token throughput
- **Call Recording** — Every HTTP request/response is recorded in `CallDetails` with URL, method, headers, body, status code, response time, and timestamp
- **Model Management** — `ListModelsAsync` returns `IAsyncEnumerable<ModelInformation>` with normalized model metadata across all providers
- **Model Existence Check** — `ModelExistsAsync` verifies a model is available, with tag-aware matching (e.g., `gemma3` matches `gemma3:latest`)
- **Model Information** — `GetModelInformationAsync` retrieves detailed model metadata (Ollama via `POST /api/show`, OpenAI via `GET /v1/models/{id}`, Gemini via `GET /v1beta/models/{id}`)
- **Model Pulling** — `PullModelAsync` downloads models with streaming progress callbacks (Ollama only)
- **Model Deletion** — `DeleteModelAsync` removes models from the provider (Ollama only)
- **Connectivity Validation** — `ValidateConnectivityAsync` confirms provider reachability
- **Unified Client Interface** — Abstract `CompletionClientBase` provides a consistent API across all providers
- **Multi-Target** — Targets both .NET 8.0 and .NET 10.0
- **Console Test Harnesses** — Interactive CLI applications for each provider (OllamaConsole, OpenAIConsole, GeminiConsole)
- **Automated Test Suite** — Comprehensive test harness (`Test.Automated`) with per-test timing, overall PASS/FAIL summary, and CLI arguments for model selection
