# Changelog

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
