# Agent Harness Support Plan

> **Status:** implemented and released in PolyPrompt 3.6.0 (P0 through P3 complete; see section 13 for the as-built record).
> **Baseline:** `main` @ `d70f310`.
> **Target:** PolyPrompt 3.6.0 (minor release; supersedes the earlier 4.0 heading). Additive for every concrete client; `Endpoint`, `ApiKey`, and the boolean `ValidateConnectivityAsync` moved from `ClientBase` to `HttpClientBase` (see section 13).
> **Related:** [README.md](README.md) provider table, [TELEMETRY.md](TELEMETRY.md), [STREAMING.md](STREAMING.md), [AOT.md](AOT.md)

## 1. Summary

PolyPrompt's provider model is "HTTP request in, capability response out". Claude Code, Codex, Gemini CLI, Cursor CLI, Mux, Pi and OpenCode are not model APIs — they are **agent harnesses**: you spawn a process, it runs its own tool loop against your filesystem, and it streams events (not tokens) back.

The recommendation is therefore not "seven more providers". It is one new **sibling capability**: `CliAgentClientBase`, sitting next to the HTTP capability bases, sharing logging, timeouts, call recording, and telemetry with `ClientBase` and nothing else. Every HTTP-shaped member stays where it is.

| Aspect | Decision |
|---|---|
| Abstract base | New `CliAgentClientBase : ClientBase`; existing clients move to `HttpClientBase : ClientBase` |
| Capability surface | `RunAsync`, `RunStreamingAsync`, `ValidateConnectivityAsync`, plus `ListModelsAsync` where the CLI exposes a model list |
| Auth | Delegated to the CLI (`claude login`, `codex login`, `cursor-agent login`, ...). No `apiKey` parameter. |
| Streaming unit | `CliAgentEvent` (text delta, reasoning delta, tool use observed, notice, completed), not token deltas |
| Build order | Codex first, end-to-end, to prove the shape; the other six are repetition |
| Package reference | No new provider SDK dependencies. Mux's `Mux.Core` stays an *option*, not a requirement (see D4). |

## 2. Where we are today

Current `README.md` provider matrix (13 providers, all HTTP, all `CompletionClientBase`-shaped where they do chat):

| Provider | Completion | Embedding | Rerank | Classification | Decision | Models | Auth |
|---|---|---|---|---|---|---|---|
| xAI Grok | `XAiCompletionClient` | `XAiEmbeddingClient` | | | | `XAiModelClient` | bearer (required) |
| OllamaCloud | `OllamaCloudCompletionClient` | `OllamaCloudEmbeddingClient` | | | | `OllamaCloudModelClient` | bearer (required) |
| Ollama | `OllamaCompletionClient` | `OllamaEmbeddingClient` | | | | `OllamaModelClient` | none / bearer |
| OpenAI (+ compatible) | `OpenAiCompletionClient` | `OpenAiEmbeddingClient` | | | | `OpenAiModelClient` | bearer |
| Azure OpenAI | `AzureOpenAiCompletionClient` | `AzureOpenAiEmbeddingClient` | | | | `AzureOpenAiModelClient` | `api-key` or Azure AD |
| Google Gemini | `GeminiCompletionClient` | `GeminiEmbeddingClient` | | | | `GeminiModelClient` | `x-goog-api-key` |
| Google Vertex AI | `VertexAiCompletionClient` | `VertexAiEmbeddingClient` | | | | | OAuth (ADC / service account) |
| Anthropic Claude | `AnthropicCompletionClient` | | | | | `AnthropicModelClient` | `x-api-key` |
| AWS Bedrock | `BedrockCompletionClient` | `BedrockEmbeddingClient` | `BedrockRerankClient` | | | `BedrockModelClient` | SigV4 |
| VoyageAI | | `VoyageAiEmbeddingClient` | `VoyageAiRerankClient` | | | | bearer |
| Cohere | `CohereCompletionClient` | `CohereEmbeddingClient` | `CohereRerankClient` | `CohereClassificationClient` | | `CohereModelClient` | bearer |
| Hugging Face TEI | | `TeiEmbeddingClient`, `TeiSparseEmbeddingClient` | `TeiRerankClient` | `TeiClassificationClient` | | `TeiModelClient` | none / bearer |
| TypeSafe (Jev, Ollaya) | | | | | `TypeSafeDecisionClient` | | bearer |

## 3. What the matrix looks like after this work

The seven new rows use their own column (`Agent run`) because they are a different capability, not a different transport for `Completion`.

| Provider | Kind | Agent run | Completion | Embedding | Rerank | Classification | Decision | Models | Auth | Transport |
|---|---|---|---|---|---|---|---|---|---|---|
| xAI Grok | API | | `XAiCompletionClient` | `XAiEmbeddingClient` | | | | `XAiModelClient` | bearer | HTTP |
| OllamaCloud | API | | `OllamaCloudCompletionClient` | `OllamaCloudEmbeddingClient` | | | | `OllamaCloudModelClient` | bearer | HTTP |
| Ollama | API | | `OllamaCompletionClient` | `OllamaEmbeddingClient` | | | | `OllamaModelClient` | none / bearer | HTTP |
| OpenAI (+ compatible) | API | | `OpenAiCompletionClient` | `OpenAiEmbeddingClient` | | | | `OpenAiModelClient` | bearer | HTTP |
| Azure OpenAI | API | | `AzureOpenAiCompletionClient` | `AzureOpenAiEmbeddingClient` | | | | `AzureOpenAiModelClient` | `api-key` or Azure AD | HTTP |
| Google Gemini | API | | `GeminiCompletionClient` | `GeminiEmbeddingClient` | | | | `GeminiModelClient` | `x-goog-api-key` | HTTP |
| Google Vertex AI | API | | `VertexAiCompletionClient` | `VertexAiEmbeddingClient` | | | | | OAuth | HTTP |
| Anthropic Claude | API | | `AnthropicCompletionClient` | | | | | `AnthropicModelClient` | `x-api-key` | HTTP |
| AWS Bedrock | API | | `BedrockCompletionClient` | `BedrockEmbeddingClient` | `BedrockRerankClient` | | | `BedrockModelClient` | SigV4 | HTTP |
| VoyageAI | API | | | `VoyageAiEmbeddingClient` | `VoyageAiRerankClient` | | | | bearer | HTTP |
| Cohere | API | | `CohereCompletionClient` | `CohereEmbeddingClient` | `CohereRerankClient` | `CohereClassificationClient` | | `CohereModelClient` | bearer | HTTP |
| Hugging Face TEI | API | | | `TeiEmbeddingClient`, `TeiSparseEmbeddingClient` | `TeiRerankClient` | `TeiClassificationClient` | | `TeiModelClient` | none / bearer | HTTP |
| TypeSafe (Jev, Ollaya) | API | | | | | | `TypeSafeDecisionClient` | | bearer | HTTP |
| **Claude Code** | **Harness** | `ClaudeCodeAgentClient` | | | | | | | CLI login | process |
| **Codex** | **Harness** | `CodexAgentClient` | | | | | | `CodexAgentClient.ListModelsAsync` | CLI login / `OPENAI_API_KEY` | process |
| **Gemini CLI** | **Harness** | `GeminiCliAgentClient` | | | | | | | Google OAuth via CLI | process |
| **Cursor CLI** | **Harness** | `CursorAgentClient` | | | | | | `CursorAgentClient.ListModelsAsync` (`agent models`) | `cursor-agent login` / `CURSOR_API_KEY` | process |
| **Mux** | **Harness** | `MuxAgentClient` | | | | | | `MuxAgentClient.ListModelsAsync` | Mux config / `mux` login | process (or in-proc `Mux.Core`, D4) |
| **Pi** | **Harness** | `PiAgentClient` | | | | | | | Pi config | process (RPC over stdio) |
| **OpenCode** | **Harness** | `OpenCodeAgentClient` | | | | | | `OpenCodeAgentClient.ListModelsAsync` (`opencode models`) | `opencode auth login` | process |

Notes that belong in the README next to that table:

* `Google Gemini` (API, `x-goog-api-key`) and `Gemini CLI` (harness, your Google account) are two different things with two different clients.
* `Anthropic Claude` (API) and `Claude Code` (harness) are likewise distinct. Do not alias them.
* No harness row has Embedding, Rerank, Classification, or Decision. The "missing capability is a compile error" rule is preserved simply by those cells being empty.

## 4. Harness invocation contracts

What each CLI actually offers on the wire. Verify every cell marked ⚠ against the installed version before writing an adapter — these flags move fast.

| Harness | Executable | Non-interactive invocation | Streaming | Session / resume | Structured control channel |
|---|---|---|---|---|---|
| **Claude Code** | `claude` | `claude -p "<prompt>" --output-format json` | `--output-format stream-json --verbose --include-partial-messages` | `--resume <session_id>`, `--continue`; `session_id` + `total_cost_usd` + usage in the JSON | `--input-format stream-json` (prompt over stdin) |
| **Codex** | `codex` | `codex exec "<prompt>" --json` (alias `codex e`); prompt `-` reads stdin; `--output-last-message -o <file>` | JSONL events on stdout | `codex exec resume <id>`, `--last` | `codex app-server --listen stdio://` |
| **Gemini CLI** | `gemini` | `gemini -p "<prompt>" --output-format json` → `{ response, stats, error? }` | `--output-format stream-json` → JSONL events `init`, `message`, `tool_use`, `tool_result`, `error`, `result` | Session/checkpoint flags vary by release — ⚠ confirm with `gemini --help` | Exit codes: `0` ok, `1` error, `42` input error, `53` turn limit |
| **Cursor CLI** | `cursor-agent` (binary also installed as `agent`) | `cursor-agent -p "<prompt>" --output-format json` | `--output-format stream-json` + `--stream-partial-output` | `--resume [chatId]`, `--continue`, `agent create-chat`, `agent ls` | `agent acp` (ACP over stdio); `agent status` for auth; `agent models` |
| **Mux** | `mux` | `mux print --output-format jsonl --output-last-message result.txt` | JSONL, including `task_plan_updated` events | `mux serve` (REST + WebSocket, OpenAPI-described); `mux mcp serve` | `Mux.Core` on NuGet (.NET 8/10) → in-process alternative |
| **Pi** | `pi` | `pi -p "<query>"`; `--mode json` for event streams | JSON event stream | RPC mode over stdin/stdout (`docs/rpc.md`); also an SDK | RPC is the cleanest integration; no shell parsing |
| **OpenCode** | `opencode` | `opencode run "<prompt>" --format json -m provider/model` | JSON event stream (`--thinking` surfaces reasoning blocks) | `-s/--session`, `--continue` | `opencode serve` + `--attach http://…`; `opencode acp` (nd-JSON over stdio) |

## 5. Why this needs a new base class

1. **Transport mismatch.** `Clients/Base/ClientBase.cs` (1,385 lines) *is* the HTTP transport: `_HttpClient`, `Endpoint`, `BuildUrl`, `ProbeAsync`, `ExecutePostAsync`, `PostStreamingAsync` (returns `StreamingHttpResult` wrapping `HttpResponseMessage`), plus `CallDetail` recording Url/Method/Status/headers/bodies. A CLI client needs `Process`, stdout line buffering, exit codes, and process-tree kill on cancel.
2. **Tool-loop mismatch.** `ToolChatAsync` deliberately *returns* tool calls and lets the caller execute them. A harness executes its own tools. Wrapping one in `ToolChatAsync` means either double-executing tools or lying about it.
3. **Capability mismatch.** `CompletionClientBase` has six abstract `...CoreAsync` members. Four are meaningless for an agent run. The project's stated rule — a missing capability is a compile error, not a runtime surprise — argues for a new base, not a bloated one.

## 6. Architecture changes, file by file

### 6.1 Split `ClientBase` (mechanical, no logic change)

* `src/PolyPrompt/Clients/Base/ClientBase.cs` keeps: logging, `TimeoutMs`, `CallDetails`, telemetry scopes, `Dispose`, and the shared validation helpers.
* New `src/PolyPrompt/Clients/Base/HttpClientBase.cs` takes: `_HttpClient`, `Endpoint`, `BuildUrl`, `ProbeAsync`, `ExecutePostAsync`, `PostStreamingAsync`, stream/status/header handling.
* Re-base every existing client (13 providers × capability bases ≈ 25 files) on `HttpClientBase`.
* Acceptance: `git diff` is rename-only apart from `: ClientBase` → `: HttpClientBase`; solution builds; existing NUnit/xUnit suites unchanged and green.

### 6.2 New `CliAgentClientBase`

`src/PolyPrompt/Clients/Base/CliAgentClientBase.cs`:

```csharp
public abstract class CliAgentClientBase : ClientBase
{
    public abstract Task<CliAgentResult> RunAsync(CliAgentRequest request, CancellationToken token = default);
    public abstract IAsyncEnumerable<CliAgentEvent> RunStreamingAsync(CliAgentRequest request, CancellationToken token = default);
    public abstract Task<CliConnectivityResult> ValidateConnectivityAsync(CancellationToken token = default);
}
```

Models under `src/PolyPrompt/Models/Cli/`:

```csharp
public sealed class CliAgentRequest
{
    public string Prompt = "";
    public string? SystemPrompt;                    // prepended, since harnesses have no system role
    public string? Model;
    public string? SessionId;                       // resume a previous turn
    public IReadOnlyList<ChatMessage>? History;     // replayed into the prompt when the CLI cannot resume
    public string? WorkingDirectory;
    public int? TimeoutMs;
    public CliPermissionMode PermissionMode = CliPermissionMode.Restricted;
    public IReadOnlyDictionary<string, string>? Environment;
    public IReadOnlyDictionary<string, string>? ExtraArguments;
    public int MaxOutputCharacters = 1_000_000;
}

public enum CliPermissionMode { Restricted, ReadOnly, WorkspaceWrite, Unrestricted }

public enum CliAgentEventKind { Started, TextDelta, ReasoningDelta, ToolUse, ToolResult, Notice, Completed, Failed }
```

`CliAgentResult` carries `Text`, `Reasoning`, `SessionId`, `ExitCode`, `StandardError`, `Events`, `Usage` (input/output/cached/reasoning tokens **as reported**), `CostUsd`, `RawOutput`, and `Succeeded`.

### 6.3 `ProcessRunner`

`src/PolyPrompt/Helpers/ProcessRunner.cs` — no cleverness, ~200 lines:

* Resolve the executable on `PATH`; distinguish *not installed* from *not logged in* (see D3).
* `ProcessStartInfo { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true }` — **never** `shell: true`.
* Line-buffered `IAsyncEnumerable<string>` over stdout (and a stderr pump kept in a bounded buffer).
* `TimeoutMs` enforcement and `Kill(entireProcessTree: true)` on cancellation.
* Output cap (`MaxOutputCharacters`) so a runaway harness cannot exhaust memory.

`Console.Shared/ProcessToolRuntime.cs` is the precedent, but it lives in a project the library cannot reference — copy the shape, not the dependency.

### 6.4 Call recording, telemetry, JSON

* `CallDetail` is HTTP-shaped. Add `ProcessCallDetail` behind the same `CallDetails` list (`Method = "EXEC"`, `RequestBody` = argv, `StatusCode` = exit code, `ResponseBody` = stdout, `Error` = stderr). Do not overload `CallDetail` with sentinel values.
* `src/PolyPrompt/Telemetry/PolyPromptTelemetry.cs`: add provider names `ClaudeCode`, `Codex`, `GeminiCli`, `CursorCli`, `Mux`, `Pi`, `OpenCode`, and a capability label for the new base in `ResolveCapability`. Without this, `tel_provider_labels` and the architecture tests fail.
* `src/PolyPrompt/Helpers/PolyPromptJsonContext.cs`: source-generated entries for every new event/result DTO, or `Test.Aot` breaks.
* `ValidateConnectivityAsync` changes meaning: probe `--version` and auth status (`agent status`, `codex login status`) instead of an HTTP 401. Exclude CLI clients from `LocalClients.All(endpoint)`; `arch_connectivity_success/failure/cancellation` assert HTTP behavior for every client.

## 7. Phases

Each phase ends in a green build, a green test run, and a reviewable increment. Do not start a phase until the previous gate passes.

### P0 — Foundation + one harness end-to-end (~2 weeks)

* [x] **P0-1** `HttpClientBase` split (§6.1). Gate: builds; existing suites green; diff is renames only.
* [x] **P0-2** `ProcessRunner` + unit tests against a shell shim (`echo`, `false`, `sleep`, malformed lines, huge output, SIGKILL-on-cancel).
* [x] **P0-3** `CliAgentClientBase` + models + `ProcessCallDetail` + telemetry labels + JSON source-gen entries (§6.2–6.4).
* [x] **P0-4** `CodexAgentClient` end-to-end: `codex exec --json`, prompt over stdin, JSONL parse, `exec resume <id>`, usage mapping, non-zero-exit mapping. Gate: `src/Test.Console` runs a live Codex turn and a recorded-JSONL replay without a live CLI.
* [x] **P0-5** Fake-CLI test fixture (§8) so P1/P2 adapters can be tested the same day they are written.
* [x] **P0-6** `README.md` "Agent Harnesses" section with the Codex row only, marked *preview*.

**P0 gate:** a developer can `dotnet add package PolyPrompt`, construct one client, and run an agent turn against Codex with a working `CallDetails` entry and telemetry. If the shape is wrong, this is the cheapest possible time to find out.

### P1 — First-party CLIs (~2 weeks)

* [x] **P1-1** `ClaudeCodeAgentClient`: `-p --output-format stream-json --verbose --include-partial-messages`, `--resume`, `total_cost_usd` + usage, `--input-format stream-json` for long prompts.
* [x] **P1-2** `GeminiCliAgentClient`: `-p --output-format json|stream-json`, map `init`/`message`/`tool_use`/`tool_result`/`error`/`result`, special-case exit codes 42 and 53. ⚠ confirm session flags first.
* [x] **P1-3** `CursorAgentClient`: `-p --output-format json|stream-json --stream-partial-output`, `--resume`/`--continue`, `agent status` for connectivity, `agent models` for `ListModelsAsync`.
* [x] **P1-4** Permission policy mapping for all three (`--permission-mode`/`--allowedTools`, `--sandbox`, `--force`/`--trust`/`--sandbox`) behind `CliPermissionMode` (D2).
* [x] **P1-5** Per-adapter test suites (§8) + README rows + one console sample per provider.

### P2 — Remaining harnesses (~2 weeks)

* [x] **P2-1** `MuxAgentClient`: `mux print --output-format jsonl`; decide in-process `Mux.Core` vs. subprocess (D4) and record the decision here. **Decided: subprocess only** (no `Mux.Core` reference); `Mux.Core` remains a possible opt-in fast path for a later release.
* [x] **P2-2** `PiAgentClient`: RPC mode over stdin/stdout (`docs/rpc.md`) — the cleanest contract of the seven; no text parsing.
* [x] **P2-3** `OpenCodeAgentClient`: `run --format json`, `--session`/`--continue`, `opencode models`.
* [x] **P2-4** Long-prompt transport: prefer stdin everywhere (`codex exec -`, Claude `--input-format stream-json`, Pi RPC) to dodge argv limits and Windows quoting (D5).

### P3 — Hardening and release (~1.5 weeks)

* [x] **P3-1** `Test.Aot`: every new client on JIT + native with matching request digests (process spawning is AOT-safe; JSONL parsing needs the source-gen entries from P0-3).
* [x] **P3-2** `Test.Shared/LocalArchitectureCases.cs`: update `_CapabilityBases` and `_ExpectedClients` — the matrix test asserts the *exact* client set.
* [x] **P3-3** Consoles: one `CliAgentConsole` with a `--provider` switch, or seven thin consoles if `AotConsole` parity is wanted.
* [x] **P3-4** Docs: README provider table (§3), `STREAMING.md` (what a stream means when the unit is a whole agent run), `TELEMETRY.md` counters for process spawn/exit, `AOT.md`, `CHANGELOG.md`, `PolyPrompt.csproj` Description/Tags.
* [x] **P3-5** Live suites, opt-in and skipped by default (`POLYPROMPT_TEST_CLAUDE_CODE=1`, ... ), because none of these can run in CI without credentials.

### Per-adapter checklist (repeat for each of the seven)

1. Argv construction in one private method, with a table test asserting the exact argv for each option combination.
2. Event/JSON mapping table (provider event → `CliAgentEventKind`) documented in a comment above the mapper.
3. Usage and cost mapping, with `null` when the provider does not report it — never estimated.
4. Error mapping: not installed / not authenticated / rate-limited / crashed / timed out, as distinct exceptions or `CliAgentResult` failures.
5. Cancellation: kill the whole process tree, surface as `OperationCanceledException`.
6. Six test cases minimum: happy path, non-zero exit, stderr-only failure, malformed JSON line, cancellation, usage parsing.

## 8. Testing strategy

There is no HTTP mock to lean on, so the fixture is a **fake CLI**: a tiny console app (or shell shim) placed first on `PATH` that emits recorded JSONL per provider, selected by an env var such as `POLYPROMPT_FAKE_CLI_SCRIPT=codex-happy`. Put it in `src/Test.Shared/FakeCli/` and reuse it across NUnit, xUnit, and the console suites.

Suites per adapter:

* happy path (text, reasoning, tool events, usage, cost, session id)
* non-zero exit with structured error on stdout
* stderr-only failure
* malformed / truncated JSON line mid-stream
* cancellation mid-stream → process tree killed, no orphan
* executable missing from `PATH` → *not installed*, distinct from *not logged in*
* oversized output → capped, not OOM
* prompt delivered via stdin (long prompt, no argv limit hit)

Architecture-level tests that will fail until updated: `LocalArchitectureCases` (client matrix), `LocalClients.All(endpoint)`, `arch_connectivity_*`, `LocalTelemetryCases` provider labels.

## 9. Decisions to settle before P0-4

| # | Question | Recommendation |
|---|---|---|
| D1 | Does an "agent run" belong in `PolyPrompt` at all, given the README's "agent runtimes are not supported" line? | Yes, but as an explicitly separate capability, and update that README line to point at it. |
| D2 | Default permission mode | `Restricted` (read-only unless the caller opts in). Never default to `--yolo`/`--force`. |
| D3 | Authentication surface | No `apiKey`. Expose `ValidateConnectivityAsync` returning *not installed* vs *not authenticated* vs *ok*, and document `claude login` etc. in the README. |
| D4 | Mux: in-process `Mux.Core` vs subprocess | Subprocess for v1, so all seven behave identically; `Mux.Core` becomes an opt-in fast path later. |
| D5 | Prompt transport | Stdin first, argv only where the CLI has no stdin form. Never `shell: true`. |
| D6 | Multi-turn | Hold `SessionId` in `ProviderState`; fall back to replaying `History` into one prompt only when the CLI cannot resume. |
| D7 | Streaming semantics | Document that a harness stream is coarse and non-deterministic in chunk boundaries; it is not token streaming, and `STREAMING.md` must say so. |
| D8 | Whether to also expose a `CompletionOnlyClientBase` shim so harnesses can drop into `ConsoleSession` | Optional stretch; do it after P1 if console parity is painful, and mark tool chat as `NotSupportedException`. |

## 10. Effort

| Piece | Estimate |
|---|---|
| `ClientBase` / `HttpClientBase` split | ~1 day, ~25 files, mechanical |
| `CliAgentClientBase` + `ProcessRunner` + models + telemetry + call recording | 4–5 days, ~800–1,000 new lines |
| Per-adapter: Claude Code | ~3 days |
| Per-adapter: Codex | ~3 days |
| Per-adapter: Gemini CLI | ~2 days |
| Per-adapter: Cursor CLI | ~2 days |
| Per-adapter: Mux | ~2 days |
| Per-adapter: Pi (RPC) | ~3 days |
| Per-adapter: OpenCode | ~3 days |
| Fake-CLI harness + 7 suites | 6–8 days |
| Docs, consoles, AOT, CHANGELOG | 3–4 days |

≈ **6–7 weeks** of focused work as one continuous effort, or one release cycle per phase. P0 alone is ~2 weeks and de-risks the whole plan.

## 11. Definition of done

* Seven clients exist, all deriving from `CliAgentClientBase`, none from `CompletionClientBase`.
* `AGENT_HARNESS_SUPPORT_PLAN.md`, README, `STREAMING.md`, `TELEMETRY.md`, `AOT.md`, and `CHANGELOG.md` all describe the same seven providers and the same permission defaults.
* README is updated with setup, prerequisites, and usage examples for the seven harness clients; `CHANGELOG.md` records the new support and the release version.
* All applicable package, assembly, and release version declarations are updated consistently for a minor version release (`major.minor.patch` → `major.(minor + 1).0`).
* All existing and new test cases pass, including `dotnet test src/Test.Nunit` and `dotnet test src/Test.Xunit`, with no fake-CLI tests skipped.
* `Test.Aot` passes on JIT and native for all seven, with request digests matching.
* No new package references in `PolyPrompt.csproj`.
* Every adapter distinguishes *not installed*, *not authenticated*, *timed out*, *crashed*, and *succeeded*.

## 12. References

* Claude Code CLI: `claude --help`, headless/print mode and `--output-format stream-json`
* Codex CLI: `codex exec --help`, `codex app-server`
* Gemini CLI: `docs/cli/headless.md` (output formats, event types, exit codes)
* Cursor CLI: `cursor-agent --help` (print mode, `--output-format`, `--resume`, `agent acp`)
* Mux: <https://usemux.ai> (`mux print`, `mux serve`, `mux mcp serve`, `Mux.Core` on NuGet)
* Pi: <https://pi.dev> (`-p`, `--mode json`, `docs/rpc.md`)
* OpenCode: `opencode run --help`, `opencode serve`, `opencode acp`

## 13. As-built record (3.6.0)

Decisions, as resolved:

| # | Resolution |
|---|---|
| D1 | Yes, as a separate capability (`CliAgentClientBase`); the README "agent runtimes are not supported" line now points to the Agent Harnesses section. |
| D2 | `CliPermissionMode.Restricted` is the default on every client and request. Only `Unrestricted` passes a bypass/yolo/force flag; per-harness mappings are in the README and each client's XML docs, and asserted by exact-argv tests. |
| D3 | No `apiKey` parameter. `ValidateConnectivityAsync` returns `CliConnectivityResult` (`Ok`, `NotInstalled`, `NotAuthenticated`, `Error`) and `AuthenticationVerified`; login status commands used: `claude auth status --json`, `codex login status`, `cursor-agent status --format json`, `mux probe`, `pi auth check`. Gemini CLI and OpenCode have no reliable login status command, so they report installation only. |
| D4 | Subprocess only; no `Mux.Core` dependency. |
| D5 | Prompt on standard input for all seven (Codex `exec -`, Claude Code `-p` with stdin, Gemini CLI headless stdin, Cursor print-mode stdin, Mux `print` stdin, Pi RPC `prompt` command, OpenCode `run` stdin). Never a shell. |
| D6 | `SessionId` is returned and stored in `ProviderState` (`sessionId`); a history message carrying it resumes the session, otherwise `History` is replayed into the prompt. |
| D7 | Documented in STREAMING.md ("Agent harness streams"). |
| D8 | Not done (optional stretch); `CliAgentConsole` gives console parity without a completion shim. |

Deviations from the original plan text:

* The split moved `Endpoint`, `ApiKey`, and the boolean `ValidateConnectivityAsync` to `HttpClientBase`; the seven HTTP capability bases (rather than ~25 client files) changed `: ClientBase` to `: HttpClientBase`. Concrete clients are unchanged.
* The base class implements `RunAsync`/`RunStreamingAsync`/`ValidateConnectivityAsync` once (validation, process lifecycle, classification, recording, telemetry); adapters implement `BuildInvocation` and `ParseLine` (repository pattern of capability bases with core hooks).
* `CliFailureKind` adds `ProviderError`, `InvalidInput`, `TurnLimitReached`, `InvalidOutput`, and `OutputLimitExceeded` to the plan's not-installed / not-authenticated / rate-limited / crashed / timed-out set. Failures are reported on the result; only caller cancellation throws. `ListModelsAsync` throws `CliAgentException`.
* Model listing: Codex (`codex debug models`), Cursor (`cursor-agent models`), Mux (`mux endpoint models`), Pi (`pi --list-models`), OpenCode (`opencode models`). Gemini CLI's model listing exists only in unreleased builds, so it was left out; Claude Code has no model list command.
* OpenCode runs with `--standalone` so its per-run permission configuration applies; Pi uses RPC mode with a `get_state` command for the session id; Gemini CLI uses `--resume` (sessions are per project directory).
* The fake CLI is a POSIX shell shim (`src/Test.Shared/FakeCli`), so fake-CLI tests and the harness part of `Test.Aot` need macOS or Linux.
* Live verification: Claude Code 2.1.312, Codex 0.163.0, Mux 1.5.0, Pi 1.4.0, and OpenCode 2.2.0 were exercised live; Gemini CLI and Cursor CLI were not installed and were implemented from official documentation and source with constructed fixtures.
