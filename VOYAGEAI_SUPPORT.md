# VOYAGEAI_SUPPORT — Plan for VoyageAI Embedding Provider Support

This document is the implementation plan for adding VoyageAI (https://docs.voyageai.com) as PolyPrompt's fifth provider, shipping as **v2.4.0**. VoyageAI is deliberately the inverse of the Anthropic effort that shipped in v2.3.0: Anthropic added a full chat/tool/generation provider with embeddings explicitly unsupported, while VoyageAI is an **embeddings-only** provider — first-class single and batch embeddings, and an explicit `NotSupportedException` for every completion-shaped operation. The library's philosophy carries straight over: a clear provider-level `NotSupportedException` beats silently faking a protocol, and the README capability matrix says "No" out loud.

Work happens on `feature/v2.4.0`, merges to `main` when the acceptance criteria pass, and ships to NuGet as 2.4.0. All code follows `c:\code\agents\requirements\CODE_STYLE.md` (namespace-first with usings inside, XML docs on public members with defaults/ranges and `<exception>` tags, `_PascalCase` private fields, backing-field validation, no `var`, no tuples, `.ConfigureAwait(false)`, `CancellationToken` on async methods, one class per file, no `Console.WriteLine` in library code).

## 1. Provider Mapping

VoyageAI exposes one endpoint PolyPrompt uses — `POST /v1/embeddings` — plus a reranker endpoint that has no PolyPrompt surface and is out of scope. There is no chat API, no completions API, no models list, and no model-info endpoint.

| PolyPrompt method | VoyageAI implementation |
|---|---|
| `EmbedAsync(string)` | Delegates to the batch overload (mirrors `OpenAiClient`) |
| `EmbedAsync(List<string>)` | `POST /v1/embeddings` with `{"model", "input", ...}`; parses `data[].index` / `data[].embedding` |
| `ValidateConnectivityAsync` | **Override**: sends a minimal one-word embeddings request and returns whether it succeeded (there is no models endpoint to probe; the probe costs ~1 token) |
| `ChatAsync` / `ChatStreamingAsync` | `NotSupportedException` |
| `ToolChatAsync` / `ToolChatStreamingAsync` | `NotSupportedException` |
| `GenerateAsync` / `GenerateStreamingAsync` | `NotSupportedException` |
| `ListModelsAsync` | `NotSupportedException` (VoyageAI has no model listing API) — thrown at call time, not on enumeration |
| `ModelExistsAsync` | **Override**: `NotSupportedException` (the base implementation would silently return false by swallowing the ListModels exception) |
| `GetModelInformationAsync` | `NotSupportedException` |
| `PullModelAsync` / `DeleteModelAsync` | Base-class `NotSupportedException` |

### Wire-level details

**Authentication.** Standard bearer auth: `Authorization: Bearer <key>`, same constructor pattern as `OpenAiClient`/`OllamaClient`.

**Defaults.** Endpoint `https://api.voyageai.com`, model `voyage-3.5` (VoyageAI's current general-purpose flagship). `_Header = "[VoyageAI] "`.

**Request body.** `model` (required), `input` (always sent as a JSON array, including the single-input path), plus the option fields below when set. The response is OpenAI-shaped (`data[]` with `index` and `embedding`), so parsing mirrors the existing OpenAI embedding parser; `usage.total_tokens` is not surfaced because `EmbeddingResponse` has no usage field for any provider.

**`VoyageAiEmbeddingOptions`** extends `EmbeddingOptions` with the documented request parameters, using the established normalize-or-revert-to-null setter idiom:

| Property | Wire field | Validation |
|---|---|---|
| `InputType` | `input_type` | Normalized (trim/lower) and clamped to `query`/`document`; unrecognized reverts to null (field omitted) |
| `Truncation` | `truncation` | `bool?`; omitted when null (provider default is true) |
| `OutputDimension` | `output_dimension` | Clamped to the documented set {256, 512, 1024, 2048}; a value outside the set reverts to null |
| `OutputDtype` | `output_dtype` | Normalized and clamped to `float`/`int8`/`uint8`/`binary`/`ubinary`; unrecognized reverts to null |

`encoding_format: "base64"` is deliberately not exposed — it would change the response shape away from parseable float arrays for no portability benefit.

## 2. Library Changes

| File | Change |
|---|---|
| `Clients/VoyageAiClient.cs` | **New.** Subclass of `CompletionClientBase` implementing the mapping above; same constructor signature as the other clients (`endpoint`, `apiKey`, `logging`, `httpClient`) with injected-`HttpClient` ownership semantics preserved. Regions included (file stays well under 500 lines, but unsupported members carry full XML docs with `<exception>` tags). |
| `Options/VoyageAiEmbeddingOptions.cs` | **New.** As specified above. |

No chat/generation options classes (dead surface), and no `ReasoningEffort` change — VoyageAI has no reasoning concept, and since tool chat itself is unsupported the projection question never arises.

## 3. VoyageAIConsole Project

`src/VoyageAIConsole/` mirrors the other consoles' structure (Inputty prompts, command loop) but with an embeddings-focused menu: `em`/`embed`, `emb`/`embedbatch` (with prompts for `InputType`, `OutputDimension`), `val`/`validate`, `settings`, `cls`, `quit`. Startup collects endpoint (default `https://api.voyageai.com`), API key, and model (default `voyage-3.5`). Chat-family commands are omitted rather than stubbed — the menu should not advertise operations the provider cannot perform. Registered in `src/PolyPrompt.sln`.

## 4. Test Plan

### 4.1 Local test infrastructure

The existing `POST /v1/embeddings` route on `LocalOpenAiTestServer` already returns an OpenAI/Voyage-compatible payload (`data[]` with two vectors), so no new route is required. `LocalEmbeddingRequest` + `LocalRequestParser` gain the Voyage request fields (`InputType`, `Truncation`, `OutputDimension`, `OutputDtype`) so recorded bodies can be asserted.

### 4.2 Local behavior cases (positive)

- `voyageai_embedding_translation` — batch embed with all options set: asserts the recorded body carries `model`, both inputs, `input_type`, `truncation`, `output_dimension`, `output_dtype`; the path is `/v1/embeddings`; the `Authorization` bearer header is present in `CallDetails`; both vectors parse with correct indexes and values. A single-input call asserts `input` is still an array and at least one vector parses.
- `voyageai_options_clamping` — `InputType` normalizes case/whitespace and reverts to null on unrecognized values; `OutputDimension` accepts documented values and reverts to null on others; `OutputDtype` normalizes and reverts; `Truncation` round-trips and clears.
- `voyageai_validate_connectivity` — the override probes via a real embeddings POST against the local server and returns true; the recorded probe path is `/v1/embeddings`.
- Extend `provider_test_configuration` — `voyageai` provider type normalizes, defaults endpoint and embedding model, env-var group selects it.

### 4.3 Local behavior cases (negative)

- `voyageai_unsupported_operations` — every completion-shaped member throws `NotSupportedException`: `ChatAsync`, `ChatStreamingAsync`, `ToolChatAsync`, `ToolChatStreamingAsync`, `GenerateAsync`, `GenerateStreamingAsync`, `ListModelsAsync`, `ModelExistsAsync`, `GetModelInformationAsync`, `PullModelAsync`, `DeleteModelAsync`; nothing reaches the wire.
- `voyageai_http_error_handling` — a non-2xx `/v1/embeddings` response surfaces `Success = false` with the status code and error text, without throwing; `ValidateConnectivityAsync` returns false against an unreachable path.
- `voyageai_cancellation` — pre-cancelled tokens propagate `OperationCanceledException` from both embed overloads and from `ValidateConnectivityAsync`.

### 4.4 Live provider suite

- `ProviderTestConfiguration`: provider `"voyageai"`, `DefaultVoyageAiEndpoint = "https://api.voyageai.com"`, default embedding model `voyage-3.5`, env vars `POLYPROMPT_TEST_VOYAGEAI_API_KEY` / `ENDPOINT` / `MODEL` / `EMBEDDING_MODEL`.
- `ProviderLiveSuite`: `embed_single` / `embed_batch` / `properties` / `call_details` / `validate_connectivity` / `pull_model` / `delete_model` / `cancellation` run. Skips with reasons: `required_models` (no model listing API), `chat`, `chat_streaming`, `tool_chat`, `tool_chat_streaming`, `generate`, `generate_streaming` (embeddings-only provider). Provider-branch adjustments: `call_details` records an embed call instead of a chat call; `list_models` / `model_exists` / `get_model_information` assert `NotSupportedException`; `cancellation` asserts `NotSupportedException` for chat/generate and cancellation for embeds; `CreateEmbeddingOptions` returns `VoyageAiEmbeddingOptions` with `InputType = "document"`.
- `Test.Automated`: `--voyageai-key`, `--voyageai-endpoint`, `--voyageai-model`, `--voyageai-embedding-model`, usage/help text, and the `--provider voyageai` generic form.
- A live run requires a VoyageAI API key; none is available in this effort, so live sign-off is the local suites plus the live-suite plumbing being in place. (The live suite has been exercised end-to-end for the other four providers.)

### 4.5 Run matrix for sign-off

```
dotnet build src/PolyPrompt.sln            # zero errors, zero warnings, both TFMs
dotnet run --project src/Test.Automated --framework net8.0 -- selftest
dotnet test src/Test.Xunit/Test.Xunit.csproj
dotnet test src/Test.Nunit/Test.Nunit.csproj
```

## 5. Documentation and Packaging

- **Version**: `<Version>` 2.3.0 → **2.4.0**; `PackageTags` gains `voyageai;voyage;rag`; `Description` gains VoyageAI.
- **CHANGELOG.md**: v2.4.0 entry covering the client, options, console, capability matrix, and test coverage, with the intentional non-support called out explicitly.
- **README.md**: intro and use cases (五 providers, embeddings for RAG), a VoyageAI quick-start, constructors note (bearer auth), provider-specific options table row and parameter list, default models row, the **Provider Feature Support matrix extended with a VoyageAI column**, project structure (VoyageAIConsole), provider-agnostic factory example, and test-run instructions with the new arguments and env vars.
- Repository requirements re-check: no new repo-level files needed; all sources under `src/`.

## 6. Execution Order and Status

1. ✅ Plan written; `feature/v2.4.0` created.
2. ✅ `VoyageAiClient` + `VoyageAiEmbeddingOptions`; solution compiles clean.
3. ✅ Test infra: `LocalEmbeddingRequest`/parser fields, behavior cases (positive + negative), configuration, live-suite branches, `Test.Automated` args.
4. ✅ `VoyageAIConsole` + solution registration.
5. ✅ Version bump, CHANGELOG, README (including the extended capability matrix).
6. ✅ Full local run matrix green (selftest, xUnit, NUnit on both TFMs; zero build warnings).
7. ✅ Merge to `main`, push to GitHub, publish NuGet 2.4.0 (package + symbols), archive this plan.

## 7. Acceptance Criteria

- `VoyageAiClient` implements every `CompletionClientBase` member; embeddings work single and batch; every unsupported member throws `NotSupportedException` with a contextual message and documented `<exception>` tag, and never touches the network.
- All existing local cases still pass; new VoyageAI cases cover the positive and negative surface in section 4.
- `dotnet build src/PolyPrompt.sln` is free of errors and warnings on both target frameworks.
- VoyageAIConsole builds and exercises embeddings interactively.
- README, CHANGELOG, and package metadata all agree on 2.4.0, and the capability matrix includes the VoyageAI column.
