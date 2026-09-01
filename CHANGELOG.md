# Changelog

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
