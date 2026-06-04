# Changelog

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
