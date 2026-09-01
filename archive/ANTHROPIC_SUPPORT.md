# ANTHROPIC_SUPPORT — Plan for Anthropic (Claude) Provider Support

This document is the implementation plan for adding Anthropic's Claude API as PolyPrompt's fourth provider, shipping as **v2.3.0**. It follows the same shape as prior feature efforts (see `archive/THINKING.md` for the v2.2.0 reasoning-capture plan): decide the provider mapping first, then implement the client, then extend the local Touchstone suites, the live suite, the console harnesses, and the full documentation surface. All work happens on a `feature/v2.3.0` branch and merges to `main` when the acceptance criteria at the bottom pass.

Decisions already made (confirmed with the maintainer):

- `GenerateAsync` / `GenerateStreamingAsync` are **mapped onto the Messages API** as a single-user-turn request, the same way `GeminiClient` maps generation onto `generateContent`. Anthropic has no legacy completions endpoint, and the library already has precedent for this mapping.
- Default model is **`claude-opus-4-8`**.
- `ReasoningEffort` projects onto **adaptive thinking + `output_config.effort`** (the only shape accepted by current Claude models; `budget_tokens` returns a 400 on Opus 4.7+, Sonnet 5, and Fable 5, so it is not supported).
- Work happens on **`feature/v2.3.0`**, merged to `main` and deleted afterward, matching the `feature/v2.0.0` pattern.

## 1. Provider Mapping

Anthropic exposes one inference endpoint — `POST /v1/messages` — plus `GET /v1/models` and `GET /v1/models/{id}`. Everything PolyPrompt supports for Anthropic routes through those three endpoints. There is no embeddings API and no legacy completions API, so the capability matrix has real "No" rows, and the library's existing philosophy applies: an explicit `NotSupportedException` beats silently faking a protocol.

| PolyPrompt method | Anthropic implementation |
|---|---|
| `ChatAsync` | `POST /v1/messages`, non-streaming |
| `ChatStreamingAsync` | `POST /v1/messages` with `"stream": true` (SSE) |
| `ToolChatAsync` | `POST /v1/messages` with `tools` / `tool_choice`; parses `tool_use` content blocks |
| `ToolChatStreamingAsync` | `POST /v1/messages` streaming; parses `content_block_start` (tool_use) + `input_json_delta` |
| `GenerateAsync` / `GenerateStreamingAsync` | `POST /v1/messages` with a single user message built from the prompt (mirrors the Gemini mapping) |
| `EmbedAsync` (single and batch) | **`NotSupportedException`** — Anthropic has no embeddings endpoint |
| `ListModelsAsync` | `GET /v1/models`, following `has_more` / `last_id` pagination until exhausted |
| `GetModelInformationAsync` | `GET /v1/models/{id}`; 404 returns `null` (matches OpenAI/Gemini behavior) |
| `ModelExistsAsync` | Base-class implementation over `ListModelsAsync` (no override needed) |
| `PullModelAsync` / `DeleteModelAsync` | Base-class `NotSupportedException` (same as OpenAI and Gemini) |
| `ValidateConnectivityAsync` | Base-class implementation (probes via `ListModelsAsync`); verify it works against `/v1/models`, override only if the base probe path does not fit |

### Wire-level details the client must get right

**Authentication and headers.** Anthropic does not use `Authorization: Bearer`. The constructor must set two default request headers instead: `x-api-key: <key>` and `anthropic-version: 2023-06-01`. The version string must be a public member with a backing private default (`_AnthropicVersion = "2023-06-01"`) per the no-hardcoded-constants rule, so callers can pin a different API version. A negative test asserts that no `Authorization` header is emitted.

**`max_tokens` is required.** Unlike the other three providers, every `/v1/messages` request must carry `max_tokens`. The client always sends the effective `MaxTokens` (request/options override, else the client property, default 4096).

**System prompts are top-level.** Anthropic takes `system` as a request field, not a message role. `SystemPrompt` (and any `ChatMessage.System` entries in a `ToolChatRequest`) must be lifted out of the message list into the `system` field. Messages must alternate user/assistant from Anthropic's perspective; consecutive same-role PolyPrompt messages are sent as-is (the API tolerates consecutive same-role by combining them — verify in live testing, and normalize client-side only if it rejects them).

**Tool calling shapes.**

- Declarations: `tools: [{name, description, input_schema}]` — note `input_schema`, not OpenAI's nested `function` object. `ToolDefinition.Function(...)` translates directly.
- Model tool calls arrive as `tool_use` content blocks (`id`, `name`, `input` object). Map to `ToolCall` with `Arguments` re-serialized as JSON text, consistent with how the other clients normalize arguments.
- Tool results: `ChatMessage.ToolResult(id, name, json)` must translate to a **user** message whose content is `[{type: "tool_result", tool_use_id, content}]`. An assistant message carrying prior tool calls (from `ToAssistantMessage()`) translates to an assistant message with `tool_use` content blocks whose `input` is the parsed arguments object.
- `ToolChoice` mapping: `"auto"` → `{"type":"auto"}`, `"none"` → `{"type":"none"}`, `"required"` → `{"type":"any"}`, a specific tool name → `{"type":"tool","name":...}`. Follow whatever normalization `Test.Shared`'s existing `tool_choice_translation` case establishes for the other providers.

**Streaming event mapping.** The SSE stream is event-typed, not a bare delta array:

| SSE event | PolyPrompt handling |
|---|---|
| `message_start` | Capture response id, model, `usage.input_tokens` |
| `content_block_start` (`text`) | No chunk emitted; marks text accumulation |
| `content_block_start` (`thinking`) | Marks reasoning accumulation |
| `content_block_start` (`tool_use`) | Emit `ToolCallDelta` with index, id, name |
| `content_block_delta` (`text_delta`) | Chunk `Text` |
| `content_block_delta` (`thinking_delta`) | Chunk `ReasoningText` |
| `content_block_delta` (`input_json_delta`) | `ToolCallDelta` argument fragment, accumulated like OpenAI split arguments |
| `message_delta` | `stop_reason` → finish metadata; `usage.output_tokens` → usage |
| `message_stop` | End of stream |

**Finish reasons and usage.** Map `end_turn`, `max_tokens`, `stop_sequence`, `tool_use`, and `refusal` into the response finish metadata the same way the other clients surface provider finish reasons (pass through the provider string; do not invent an enum). `usage.input_tokens` / `usage.output_tokens` populate the prompt/completion/total usage fields, with total computed as the sum. A `refusal` stop reason is a *successful* HTTP response with possibly empty content — the response must not be marked failed; the finish reason carries the signal. This gets its own test case.

**Reasoning capture.** Claude returns reasoning as `thinking` content blocks (non-streaming) and `thinking_delta` events (streaming). These map directly onto the v2.2.0 surface: `Reasoning` on responses, `ReasoningText` on chunks, normalized to null when absent or empty, never leaked into `Text`, never carried back by `ToAssistantMessage()`. On current models the API omits thinking text unless the request asks for it, so the `ReasoningEffort` projection (below) sends `display: "summarized"` — otherwise thinking blocks stream with empty text and capture would silently return null. One subtlety unique to Anthropic: multi-turn tool flows on the same model expect thinking blocks to be echoed back verbatim. PolyPrompt's contract is that reasoning is return-only, and dropping thinking blocks is tolerated by the API for the follow-up patterns PolyPrompt supports (tool results in a user message); the live tool-chat test must prove the follow-up turn succeeds without echoing thinking.

### ReasoningEffort projection

`ReasoningEffort` gains a fourth provider column. Per the confirmed decision, the projection targets adaptive thinking plus `output_config.effort`:

| `ReasoningEffortLevel` | Anthropic projection |
|---|---|
| `Minimal` | `output_config: {effort: "low"}` — no `thinking` field |
| `Low` | `thinking: {type: "adaptive", display: "summarized"}` + `effort: "low"` |
| `Medium` | `thinking: {type: "adaptive", display: "summarized"}` + `effort: "medium"` |
| `High` | `thinking: {type: "adaptive", display: "summarized"}` + `effort: "high"` |
| *unset* | *(both fields omitted; request body unchanged)* |

`Minimal` deliberately omits the `thinking` field rather than sending `{type: "disabled"}`, because an explicit disable is rejected on some current models while omission is accepted everywhere.

Changes to `ReasoningEffort`:

- New override property `AnthropicEffort` (`string?`), clamped to `low`/`medium`/`high`/`xhigh`/`max` with the established setter pattern: an unrecognized value reverts to null and falls back to the level default. (`xhigh`/`max` are valid Anthropic effort values with no level preset — reachable only via the override, same spirit as `GeminiThinkingBudget` accepting values outside the presets.)
- New projection method `ToAnthropicEffort()` returning the wire string, plus a helper the client uses to decide whether to emit the `thinking` field. Follow the existing `ToOpenAiWireValue()` / `ToGeminiThinkingBudget()` / `ToOllamaThink()` conventions, including throwing on an undefined level.
- XML documentation on the new members states defaults, allowed values, and clamping behavior, per CODE_STYLE.

## 2. Library Changes

New and modified files in `src/PolyPrompt`, one class per file throughout:

| File | Change |
|---|---|
| `Clients/AnthropicClient.cs` | **New.** Subclass of `CompletionClientBase` implementing the mapping above. Default endpoint `https://api.anthropic.com`, default model `claude-opus-4-8`, `_Header = "[Anthropic] "`. Same constructor signature as the other clients (`endpoint`, `apiKey`, `logging`, `httpClient`), with the injected-`HttpClient` ownership semantics preserved. |
| `Models/ReasoningEffort.cs` | Add `AnthropicEffort` override + `ToAnthropicEffort()` projection as specified above. |
| `Options/AnthropicChatCompletionOptions.cs` | **New.** Extends `ChatCompletionOptions` with Anthropic-specific parameters: `TopK` (int?, clamped ≥ 0), `StopSequences` (`List<string>?`). Keep the surface small; more knobs can follow demand. |
| `Options/AnthropicGenerationOptions.cs` | **New.** Extends `GenerationOptions` with the same Anthropic-specific parameters, since generation rides the Messages API. |

No `AnthropicEmbeddingOptions` is added — `EmbedAsync` throws, so an options class would be dead surface. The capability matrix and XML docs on `EmbedAsync`'s `<exception>` tags make the omission explicit.

Code style requirements that apply to every new file (from `c:\code\agents\requirements\CODE_STYLE.md`): namespace-first with usings inside the namespace block (Microsoft/system first, alphabetical); XML docs on all public members with defaults/min/max and `<exception>` tags; no docs on private members; `_PascalCase` private fields; explicit getter/setter backing fields where validation applies; no `var`; no tuples; `.ConfigureAwait(false)` on awaits; `CancellationToken` on every async method with cancellation checked at appropriate points; guard clauses at method start; specific exception types with contextual messages; no `Console.WriteLine` in library code; regions (`Public-Members`, `Private-Members`, `Constructors-and-Factories`, `Public-Methods`, `Private-Methods`) for files over 500 lines — `AnthropicClient.cs` will exceed that, so include them.

## 3. AnthropicConsole Project

Add `src/AnthropicConsole/` mirroring `GeminiConsole` exactly: an `AnthropicConsole.csproj` (same target frameworks and `GetSomeInput` dependency as the other consoles) and a `Program.cs` with the same interactive command set — chat, generate, embeddings (which will surface the `NotSupportedException` cleanly), model commands, `tc`/`toolchat` with the sample `get_weather` tool and tool-result follow-up turn, and the streaming toggle. Startup prompts collect endpoint (default `https://api.anthropic.com`), API key, model (default `claude-opus-4-8`), max tokens, and timeout. Register the project in `src/PolyPrompt.sln` alongside the other consoles.

## 4. Test Plan

Testing follows the two-tier structure already in place: local Touchstone behavior cases in `Test.Shared/LocalBehaviorSuite.cs` running against `LocalOpenAiTestServer` (no network, deterministic), and live provider cases in `ProviderLiveSuite.cs` gated on configuration. The xUnit and NUnit adapters pick both up automatically from `PolyPromptSuites`, so no adapter changes are needed beyond what the shared descriptors expose.

### 4.1 Local test server

`LocalOpenAiTestServer` (which despite its name already serves OpenAI, Ollama, and Gemini routes) gains Anthropic routes:

- `POST /v1/messages` — non-streaming and streaming (branch on `"stream": true` in the body). Responses are canned Anthropic-shaped JSON / SSE, parameterized enough to exercise text, thinking blocks, tool_use blocks, split `input_json_delta` fragments, `message_delta` stop reasons, and usage.
- `GET /v1/models` and `GET /v1/models/{id}` already exist for OpenAI. Disambiguate by the presence of the `anthropic-version` request header: when present, return Anthropic-shaped payloads (`{"data":[{"type":"model","id":...,"display_name":...}],"has_more":false,...}`); otherwise keep the OpenAI shape. Include a two-page pagination fixture (`has_more: true` + `last_id`) so the client's pagination loop is actually exercised.

New request-parsing models in `Test.Shared`, one class per file, following the `LocalOpenAi*`/`LocalGemini*` naming: `LocalAnthropicRequest`, `LocalAnthropicMessage`, `LocalAnthropicContentBlock`, `LocalAnthropicTool`, `LocalAnthropicToolChoice`, `LocalAnthropicThinkingConfig`. `LocalRequestParser` gains the corresponding parse helper.

### 4.2 Local behavior cases (positive)

New `LocalBehaviorSuite` cases, IDs following the existing convention:

- `anthropic_chat_streaming` — SSE flow: text deltas accumulate, timing metrics populate, usage captured from `message_start` + `message_delta`.
- `anthropic_generation_streaming` — generation maps onto a single-user-turn Messages request; assert the request body contains the prompt as a user message and no `system` field unless set.
- `anthropic_tool_chat` — declarations translate to `tools[].input_schema`, `tool_use` block parses into `ToolCall` with JSON arguments, tool-result follow-up serializes as a user-role `tool_result` block, final turn returns text.
- `anthropic_tool_chat_streaming` — `content_block_start`(tool_use) + split `input_json_delta` fragments accumulate into a complete `ToolCall`; multiple concurrent tool calls by index.
- `anthropic_chat_request_translation` — folded into or alongside `provider_chat_request_translation`: `system` lifted to top level, `max_tokens` always present, model comes from `Model`/per-request override, `x-api-key` and `anthropic-version` headers present.
- `anthropic_models` — list (with pagination), exists (tag-free exact matching), and `GetModelInformationAsync` metadata mapping into `ModelInformation` (`display_name` → `DisplayName`, `created_at` → `ModifiedUtc` or metadata).
- `reasoning_effort_anthropic_toolchat` / `reasoning_effort_anthropic_toolchat_streaming` — each level's projected `thinking` + `output_config.effort` fields appear in the request body; `Minimal` omits `thinking`; unset omits both.
- `reasoning_effort_anthropic_override` — `AnthropicEffort` override wins over the level default; invalid override value reverts to null; extend `reasoning_effort_projection_defaults`, `reasoning_effort_overrides_win`, and `reasoning_effort_override_clamping` to cover the new provider column.
- `reasoning_anthropic_chat` / `reasoning_anthropic_chat_streaming` / `reasoning_anthropic_toolchat` / `reasoning_anthropic_toolchat_streaming` — thinking blocks and `thinking_delta` events surface as `Reasoning`/`ReasoningText`, kept out of `Text`.
- `anthropic_tool_choice_translation` — extend the existing `tool_choice_translation` case with the auto/none/any/tool mappings.
- Extend `request_model_overrides`, `injected_http_client`, and `post_and_record_disposes_response`-adjacent coverage to include the Anthropic client where those cases iterate providers.

### 4.3 Local behavior cases (negative)

- `anthropic_embedding_not_supported` — both `EmbedAsync` overloads throw `NotSupportedException` with a meaningful message; extend `unsupported_provider_model_management` to include Anthropic pull/delete.
- `anthropic_http_error_handling` — non-2xx on `/v1/messages` (non-streaming and streaming startup) surfaces `Success = false` / error text without throwing, matching provider-wide behavior; extend `streaming_http_error_handling`.
- `anthropic_refusal_stop_reason` — a `stop_reason: "refusal"` response with empty content is a successful response with the finish reason surfaced and `Text` null/empty; nothing throws.
- `anthropic_streaming_body_timeout` — `TimeoutMs` covers body enumeration on the Anthropic SSE path (extend `streaming_body_timeout` / `tool_chat_streaming_body_timeout` provider loops).
- Reasoning negatives, mirroring v2.2.0: no-thinking responses leave `Reasoning` null (`reasoning_absent_by_default` extended), empty thinking normalizes to null, reasoning never appears in `Text`, and `ToAssistantMessage()` on a reasoning-bearing Anthropic response carries no thinking content (`reasoning_not_resent` extended).
- `anthropic_no_bearer_header` — assert the request carries `x-api-key` and no `Authorization` header (can live inside the request-translation case).
- `anthropic_missing_model_information` — `GetModelInformationAsync` on a 404 model returns null rather than throwing.
- Cancellation: pre-cancelled token propagates `OperationCanceledException` on the Anthropic paths (extend the existing cancellation cases' provider loops).

### 4.4 Live provider suite

- `ProviderTestConfiguration`: add `DefaultAnthropicEndpoint = "https://api.anthropic.com"`, provider type `"anthropic"`, environment variables `POLYPROMPT_TEST_ANTHROPIC_API_KEY` / `POLYPROMPT_TEST_ANTHROPIC_MODEL`, and default model resolution (`claude-opus-4-8`). No embedding model constant — the embedding cases skip.
- `Test.Automated` argument parsing: `--anthropic-key`, `--anthropic-model`, `--anthropic-endpoint`, plus the generic `--provider anthropic` and positional forms.
- `ProviderLiveSuite` skips: `embed_single` / `embed_batch` skip for Anthropic with reason "Anthropic does not provide an embeddings API." `generate` / `generate_streaming` run (mapped). `pull_model` / `delete_model` already assert the correct `NotSupportedException` behavior per provider — extend the expectation to Anthropic. `tool_chat` / `tool_chat_streaming`, `list_models`, `model_exists`, `get_model_information`, `validate_connectivity`, `call_details`, `cancellation`, and `properties` all run unchanged.

### 4.5 Run matrix for sign-off

```
dotnet build src/PolyPrompt.sln                          # zero errors, zero new warnings, both TFMs
dotnet run --project src/Test.Automated --framework net8.0 -- selftest
dotnet test src/Test.Xunit/Test.Xunit.csproj
dotnet test src/Test.Nunit/Test.Nunit.csproj
dotnet run --project src/Test.Automated -- --anthropic-key sk-ant-... --anthropic-model claude-opus-4-8
```

The live run requires a real key and is the maintainer's call; local suites must pass unconditionally.

## 5. Documentation and Packaging

**Version.** Bump `src/PolyPrompt/PolyPrompt.csproj` `<Version>` from 2.2.1 to **2.3.0** and extend `PackageTags` with `anthropic;claude`.

**CHANGELOG.md.** Add a `v2.3.0` entry in the established voice: Added (AnthropicClient and the full mapped surface, AnthropicConsole, `AnthropicEffort` projection, options classes, local + live test coverage with the negative cases called out), Changed (README capability matrix and defaults, version metadata). Explicitly note the two intentional gaps (embeddings, pull/delete) so the entry reads as a complete account.

**README.md.** Every provider-enumerating section gains Anthropic: the opening description and "What It Does", Quick Start (a `AnthropicClient` example), the constructors note, the reasoning-effort projection table (new Anthropic column) and reasoning-source table (`thinking` blocks), streaming protocol-shape notes (`/v1/messages` SSE event stream), Default Models (`claude-opus-4-8`, embedding column "—"), Provider-Specific Options table (`AnthropicChatCompletionOptions` / `AnthropicGenerationOptions`, no embedding options), Provider Feature Support matrix (Embeddings: "No — `EmbedAsync` throws `NotSupportedException`"; Pull/Delete: "No"; Text Generation: "Yes, via the Messages API"), Project Structure (AnthropicConsole), provider-agnostic factory example, and Running the Automated Tests (`--anthropic-key` forms and env vars).

**Accuracy review.** The user asked for a review of README and CHANGELOG for accuracy and completeness as part of this effort; one defect is already known: the README says "Current documented package version: **2.0.1**" while the package is 2.2.1 — after this effort it must read 2.3.0. During the doc pass, re-verify every README claim against the code (client property table, method table, tool-calling model table, test invocation examples) and every CHANGELOG entry's tense/format consistency. Repository requirements (`REPOSITORY_REQUIREMENTS.md`) are already satisfied for this repo shape — `.gitignore`, `README.md`, `CHANGELOG.md`, `LICENSE.md` (MIT), all sources under `src/` — and the Docker/REST/MCP items do not apply to a NuGet library with no service surface. No new repository files are required beyond this plan document.

## 6. Execution Order

1. Create `feature/v2.3.0` from `main`.
2. `ReasoningEffort` changes (`AnthropicEffort`, `ToAnthropicEffort()`), with the projection unit cases extended in the same commit.
3. `AnthropicClient` + options classes; compile clean on net8.0 and net10.0.
4. Local test server routes + `LocalAnthropic*` request models + `LocalRequestParser` support.
5. Local behavior cases (positive, then negative), keeping `selftest`, xUnit, and NUnit green after each group.
6. `ProviderTestConfiguration` / `Test.Automated` args / `ProviderLiveSuite` skips and expectations.
7. `AnthropicConsole` project + solution registration.
8. Version bump, CHANGELOG entry, README update, and the accuracy review pass.
9. Full run matrix (section 4.5), live run against the real API with a maintainer-supplied key.
10. Merge `feature/v2.3.0` to `main`; delete the branch locally and on GitHub.

## 7. Acceptance Criteria

- `AnthropicClient` implements every `CompletionClientBase` member with the mappings in section 1; unsupported operations throw `NotSupportedException` with contextual messages and documented `<exception>` tags.
- All existing local cases still pass; new Anthropic cases cover every implemented path plus the negative cases in 4.3.
- `dotnet build src/PolyPrompt.sln` is free of errors and introduces no new warnings on either target framework.
- AnthropicConsole builds, runs, and exercises chat, streaming, tool chat, and model commands interactively.
- README, CHANGELOG, and package metadata all say 2.3.0 and agree with the code, including the corrected "current documented package version" line.
- Live suite passes against `https://api.anthropic.com` with a real key (embedding cases skipped, pull/delete asserting `NotSupportedException`).
- Code style spot-check against `c:\code\agents\requirements\CODE_STYLE.md` on every new/modified file.
