# PolyPrompt — Reasoning Effort Support (`EFFORT.md`)

**Feature:** Add a provider-neutral *reasoning effort* control to the tool-chat request path (and, in an optional second phase, the plain chat path).
**Target release:** `v2.1.0` — **additive, minor** version bump (no breaking changes).
**Status:** ☐ Not started ☐ In progress ☐ Complete
**Owner:** _(assign)_
**Drafted:** 2026-08-12 · **Release date:** _TBD_

---

## How to use this document

This is a working checklist. Each task is a checkbox. Annotate inline as you go:

- `- [x]` done · `- [~]` in progress · `- [ ]` not started · `- [!]` blocked (add a note)
- Append notes after a task with `— <your note>` (e.g. `— done, but Gemini budget mapping needs a live re-test`).
- Keep the **Progress log** at the bottom current; it is the single place a reviewer looks for status.

Do not delete tasks when they are done — check them off so the history stays auditable.

---

## 1. Why this matters

### 1.1 Product motivation
The modern coding-agent UX (Claude Code's `/effort`, Codex's `/model` → effort selection) lets a user trade latency/cost against depth of reasoning **per request**, without changing the model. Reasoning-capable models expose exactly this knob:

- **OpenAI** reasoning models (o-series, gpt-5) accept `reasoning_effort: "minimal" | "low" | "medium" | "high"` on `/v1/chat/completions`.
- **Google Gemini** 2.5 models accept `generationConfig.thinkingConfig` (a thinking-token budget).
- **Ollama** reasoning models (gpt-oss, deepseek-r1) accept a `think` control on `/api/chat`.

PolyPrompt is the provider-normalization layer that downstream agents (e.g. **Mux**) build on. Today PolyPrompt's `ToolChatRequest` exposes only `Model`, `Messages`, `Tools`, `ToolChoice`, `Temperature`, `TopP`, and `MaxTokens`. There is **no field for reasoning effort and no passthrough/extra-body mechanism**, so a consumer literally cannot put this parameter on the wire. Any agent wanting an effort selector is blocked at the PolyPrompt boundary.

### 1.2 Why solve it in PolyPrompt (not in the consumer)
Reasoning effort is the **least portable** parameter in the API surface — its field name, shape, and value space differ per provider. That per-provider translation is precisely PolyPrompt's job; it already normalizes temperature, tool declarations, tool-choice, and message roles across OpenAI/Ollama/Gemini. Pushing effort translation into each consumer would duplicate provider knowledge outside the library and invite drift. Adding one neutral field here lets every consumer opt in with a single enum and get correct per-provider wire output for free.

### 1.3 Why this is safe and additive
- The new field is **nullable** and defaults to `null`. When `null`, **no new key is written to any request body** — byte-for-byte identical output to today. Existing behavior is preserved, so this is a minor (not major) release.
- The value space is a closed **enum**, so invalid values are impossible at the type level (unlike free-form doubles that need clamping).
- Providers that have no reasoning concept simply omit the field; a caller setting effort against a non-reasoning model is a no-op, not an error.

---

## 2. Design

### 2.1 Public surface (new)

`ReasoningEffort` is a **value object**, not a bare enum. It carries a semantic `Level` (the enum, which supplies sensible per-provider defaults) plus optional, individually **tunable** per-provider override properties, and exposes **instance methods that project the object into each provider's wire value**. The projection uses an explicit override when the caller set one, and otherwise derives from `Level`. This keeps all provider-mapping knowledge in one cohesive, testable type instead of scattered across the three adapters.

Ergonomics for the common case are preserved by static presets and an implicit conversion from the level enum, so "just give me high" stays a single token.

```csharp
namespace PolyPrompt.Models
{
    /// <summary>
    /// Semantic reasoning effort level. Anchors a <see cref="ReasoningEffort"/> and supplies the
    /// per-provider defaults each level implies. Callers may override any individual provider value.
    /// </summary>
    public enum ReasoningEffortLevel
    {
        /// <summary>Minimal reasoning; disables extended thinking on providers that can toggle it off.</summary>
        Minimal,
        /// <summary>Low reasoning effort.</summary>
        Low,
        /// <summary>Medium reasoning effort.</summary>
        Medium,
        /// <summary>High reasoning effort.</summary>
        High
    }

    /// <summary>
    /// Provider-neutral reasoning effort. A <see cref="Level"/> supplies defaults; the per-provider
    /// properties override them individually; the projection methods return the value each provider
    /// expects. Null on a request means "do not send" — the provider default is used.
    /// </summary>
    public class ReasoningEffort
    {
        #region Presets

        /// <summary>A <see cref="ReasoningEffortLevel.Minimal"/> effort with default provider values.</summary>
        public static ReasoningEffort Minimal => new ReasoningEffort(ReasoningEffortLevel.Minimal);
        /// <summary>A <see cref="ReasoningEffortLevel.Low"/> effort with default provider values.</summary>
        public static ReasoningEffort Low => new ReasoningEffort(ReasoningEffortLevel.Low);
        /// <summary>A <see cref="ReasoningEffortLevel.Medium"/> effort with default provider values.</summary>
        public static ReasoningEffort Medium => new ReasoningEffort(ReasoningEffortLevel.Medium);
        /// <summary>A <see cref="ReasoningEffortLevel.High"/> effort with default provider values.</summary>
        public static ReasoningEffort High => new ReasoningEffort(ReasoningEffortLevel.High);

        #endregion

        #region Constructors

        /// <summary>Create a reasoning effort defaulting to <see cref="ReasoningEffortLevel.Medium"/>.</summary>
        public ReasoningEffort() { }

        /// <summary>Create a reasoning effort at the given level.</summary>
        public ReasoningEffort(ReasoningEffortLevel level) { _Level = level; }

        /// <summary>Implicitly build a default <see cref="ReasoningEffort"/> from a level.</summary>
        public static implicit operator ReasoningEffort(ReasoningEffortLevel level) => new ReasoningEffort(level);

        #endregion

        #region Public-Members

        private ReasoningEffortLevel _Level = ReasoningEffortLevel.Medium;
        private string? _OpenAiValue = null;
        private int? _GeminiThinkingBudget = null;
        private string? _OllamaThink = null;

        // Accepted override tokens. A value outside its set is rejected (reverts to null) so the projection
        // falls back to the Level-derived default — the same "silently clamp to a valid value" idiom the
        // existing Temperature/TopP setters use, rather than throwing.
        private static readonly HashSet<string> _OpenAiValues =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "minimal", "low", "medium", "high" };
        private static readonly HashSet<string> _OllamaValues =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "low", "medium", "high", "true", "false" };

        private const int GeminiThinkingBudgetFloor = -1;      // -1 = dynamic budget, 0 = off
        private const int GeminiThinkingBudgetCeiling = 32768; // generous upper bound across 2.5 models

        /// <summary>The semantic effort level. Drives every default that is not explicitly overridden.</summary>
        public ReasoningEffortLevel Level
        {
            get => _Level;
            set => _Level = value;
        }

        /// <summary>
        /// OpenAI reasoning_effort override. Null derives from <see cref="Level"/>. Set values are
        /// normalized (trimmed, lower-cased) and clamped to the accepted set
        /// ("minimal"/"low"/"medium"/"high"); an unrecognized value reverts to null.
        /// </summary>
        public string? OpenAiValue
        {
            get => _OpenAiValue;
            set => _OpenAiValue = NormalizeToken(value, _OpenAiValues);
        }

        /// <summary>
        /// Gemini thinking-token budget override (thinkingConfig.thinkingBudget). Null derives from
        /// <see cref="Level"/>. -1 selects the model's dynamic budget, 0 disables thinking, positive is an
        /// explicit token budget. Clamped to -1..32768.
        /// </summary>
        public int? GeminiThinkingBudget
        {
            get => _GeminiThinkingBudget;
            set => _GeminiThinkingBudget = value.HasValue
                ? Math.Clamp(value.Value, GeminiThinkingBudgetFloor, GeminiThinkingBudgetCeiling)
                : null;
        }

        /// <summary>
        /// Ollama think override. Null derives from <see cref="Level"/>. Set values are normalized
        /// (trimmed, lower-cased) and clamped to the accepted set ("low"/"medium"/"high"/"true"/"false");
        /// an unrecognized value reverts to null. "true"/"false" are emitted as JSON booleans.
        /// </summary>
        public string? OllamaThink
        {
            get => _OllamaThink;
            set => _OllamaThink = NormalizeToken(value, _OllamaValues);
        }

        #endregion

        #region Projections

        /// <summary>Returns the OpenAI reasoning_effort wire value (override if set, else derived from Level).</summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown for an undefined <see cref="Level"/>.</exception>
        public string ToOpenAiWireValue()
        {
            if (OpenAiValue != null) return OpenAiValue;
            switch (_Level)
            {
                case ReasoningEffortLevel.Minimal: return "minimal";
                case ReasoningEffortLevel.Low:     return "low";
                case ReasoningEffortLevel.Medium:  return "medium";
                case ReasoningEffortLevel.High:    return "high";
                default: throw new ArgumentOutOfRangeException(nameof(Level), _Level, "Unknown reasoning effort level.");
            }
        }

        /// <summary>Returns the Gemini thinkingConfig.thinkingBudget (override if set, else derived from Level).</summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown for an undefined <see cref="Level"/>.</exception>
        public int ToGeminiThinkingBudget()
        {
            if (_GeminiThinkingBudget.HasValue) return _GeminiThinkingBudget.Value;
            switch (_Level)
            {
                case ReasoningEffortLevel.Minimal: return 0;      // thinking off
                case ReasoningEffortLevel.Low:     return 1024;
                case ReasoningEffortLevel.Medium:  return 8192;
                case ReasoningEffortLevel.High:    return -1;     // dynamic budget
                default: throw new ArgumentOutOfRangeException(nameof(Level), _Level, "Unknown reasoning effort level.");
            }
        }

        /// <summary>Returns the Ollama think value as a bool or string (override if set, else derived from Level).</summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown for an undefined <see cref="Level"/>.</exception>
        public object ToOllamaThink()
        {
            if (_OllamaThink != null)
            {
                // Already normalized by the setter to one of low/medium/high/true/false.
                if (string.Equals(_OllamaThink, "true", StringComparison.Ordinal)) return true;
                if (string.Equals(_OllamaThink, "false", StringComparison.Ordinal)) return false;
                return _OllamaThink;
            }
            switch (_Level)
            {
                case ReasoningEffortLevel.Minimal: return false;  // disable thinking
                case ReasoningEffortLevel.Low:     return "low";
                case ReasoningEffortLevel.Medium:  return "medium";
                case ReasoningEffortLevel.High:    return "high";
                default: throw new ArgumentOutOfRangeException(nameof(Level), _Level, "Unknown reasoning effort level.");
            }
        }

        #endregion

        #region Private-Methods

        /// <summary>Trim + lower-case a candidate override and clamp it to the accepted set, else null.</summary>
        private static string? NormalizeToken(string? value, HashSet<string> allowed)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            string normalized = value.Trim().ToLowerInvariant();
            return allowed.Contains(normalized) ? normalized : null;
        }

        #endregion
    }
}
```

- `ToolChatRequest.ReasoningEffort` — `ReasoningEffort?`, default `null`.
- `CompletionClientBase.ReasoningEffort` — `ReasoningEffort?`, default `null` (instance-level default, matching the existing `Temperature`/`TopP` pattern).

**Usage — common case and tuned case:**

```csharp
// Common: preset (or implicit from the level enum) — one token.
request.ReasoningEffort = ReasoningEffort.High;
request.ReasoningEffort = ReasoningEffortLevel.High;   // implicit conversion

// Tuned: keep the semantic level, override just one provider's parameter.
request.ReasoningEffort = new ReasoningEffort(ReasoningEffortLevel.High) { GeminiThinkingBudget = 16000 };
```

### 2.2 Resolution
`CompletionClientBase.ResolveToolChatRequest` gains one `out ReasoningEffort? reasoningEffort` parameter resolved as `request.ReasoningEffort ?? _ReasoningEffort` (same precedence rule as the other fields). Each adapter's `ToolChatAsync` / `ToolChatStreamingAsync` threads that value into its `BuildToolChatRequestBody`, which — when the value is non-null — calls the appropriate projection method and emits the provider field. When null, nothing is written.

### 2.3 Per-provider projection (level defaults, all overridable)

The table shows the value each `Level` **defaults** to. Any cell is individually overridable via the matching property (`OpenAiValue`, `GeminiThinkingBudget`, `OllamaThink`) without changing the semantic level.

| `Level` | `ToOpenAiWireValue()` → `reasoning_effort` | `ToGeminiThinkingBudget()` → `thinkingConfig.thinkingBudget` | `ToOllamaThink()` → `think` |
|---|---|---|---|
| `Minimal` | `"minimal"` | `0` (off) | `false` |
| `Low` | `"low"` | `1024` | `"low"` |
| `Medium` | `"medium"` | `8192` | `"medium"` |
| `High` | `"high"` | `-1` (dynamic / model-decides) | `"high"` |
| _request field null_ | *(omitted)* | *(omitted)* | *(omitted)* |

Notes and rationale:
- **OpenAI** — direct 1:1 mapping to the documented enum; the primary, highest-confidence mapping and the exact analog of Codex/Claude Code effort. `OpenAiValue` overrides the level default and is normalized + clamped to `{minimal, low, medium, high}` (unrecognized ⇒ reverts to null ⇒ level default).
- **Gemini** — Gemini exposes a *thinking-token budget*, not an effort enum, so each level defaults to a representative budget. `Minimal → 0` turns thinking off (2.5 Flash); `High → -1` selects the dynamic budget. Callers tune with `GeminiThinkingBudget`, which is clamped to `-1..32768`; the defaults are the only values a future adjustment touches, and they live on the value object (not buried in the adapter).
- **Ollama** — newer reasoning models (gpt-oss) accept a string level; the boolean form (`false`) disables thinking for `Minimal`. The most model-variable provider; document as best-effort and model-dependent. Callers tune with `OllamaThink`, normalized + clamped to `{low, medium, high, true, false}`.

**All three override properties clamp/validate in their setters** (mirroring the existing `Temperature`/`TopP`/`MaxTokens` clamp-in-setter idiom): the numeric budget clamps to its range, and the string overrides normalize case/whitespace and reject out-of-set values by reverting to null. Each projection switches on `Level` and **throws `ArgumentOutOfRangeException` for an undefined level** (defensive; unreachable through the public API but covered by a negative test).

### 2.4 Optional Phase 2 — plain chat path (parity, not required for Mux)
The `ChatAsync`/`ChatStreamingAsync` path takes provider-specific `*ChatCompletionOptions`. For full symmetry, add `ReasoningEffort` to `OpenAiChatCompletionOptions` (and, if desired, the Gemini/Ollama options) and emit it in `BuildChatRequestBody`. This is **out of scope for the core deliverable** (Mux and other agents use the tool-chat path) but included here so the release can optionally ship parity. Gate it behind its own checklist section; it does not change the version target.

---

## 3. Implementation — exact changes

> File paths are relative to `C:\Code\PolyPrompt`. Follow existing house style (Allman braces, explicit types, `#region` grouping, XML doc on all public members, `NoWarn` nullable set already configured).

### 3.1 New model: `ReasoningEffortLevel` enum + `ReasoningEffort` value object
- [ ] Create `src/PolyPrompt/Models/ReasoningEffortLevel.cs` with the enum from §2.1.
- [ ] Create `src/PolyPrompt/Models/ReasoningEffort.cs` with the class from §2.1 — presets, constructors, implicit operator from the level, the three tunable override properties (`OpenAiValue`, `GeminiThinkingBudget` with the `-1` floor clamp, `OllamaThink`), and the three projection methods (`ToOpenAiWireValue`, `ToGeminiThinkingBudget`, `ToOllamaThink`).
- [ ] Follow house style: `#region` grouping, XML doc on every public member, clamp-in-setter for `GeminiThinkingBudget` (mirrors the existing `Temperature`/`TopP` setters).
- [ ] No separate extension/helper class is needed — the projections live on the value object, so the adapters call them directly and there are **no per-adapter mapping helpers** (this is the key structural win over the enum-only design).

### 3.2 `ToolChatRequest` — new property
File: `src/PolyPrompt/Models/ToolChatRequest.cs`
- [ ] Add:

```csharp
/// <summary>
/// Optional reasoning effort for reasoning-capable models. Null uses the client default,
/// and when both are null no reasoning field is sent (provider default is used).
/// </summary>
public ReasoningEffort? ReasoningEffort { get; set; } = null;
```
- [ ] No clamping on the request property itself; validation/derivation lives inside the `ReasoningEffort` value object.

### 3.3 `CompletionClientBase` — instance default + resolution
File: `src/PolyPrompt/Clients/CompletionClientBase.cs`
- [ ] Add a private backing field `private ReasoningEffort? _ReasoningEffort = null;` and a public `ReasoningEffort? ReasoningEffort { get; set; }` property (XML-documented, mirrors `Temperature`).
- [ ] Extend `ResolveToolChatRequest` with a new trailing `out ReasoningEffort? reasoningEffort` parameter:

```csharp
protected void ResolveToolChatRequest(
    ToolChatRequest request,
    out string model,
    out int maxTokens,
    out double? temperature,
    out double? topP,
    out ReasoningEffort? reasoningEffort)   // NEW
{
    ArgumentNullException.ThrowIfNull(request);
    if (request.Messages == null || request.Messages.Count == 0)
        throw new ArgumentException("Tool chat requests require at least one message.", nameof(request));

    model = request.Model ?? _Model;
    maxTokens = request.MaxTokens ?? _MaxTokens;
    temperature = request.Temperature ?? _Temperature;
    topP = request.TopP ?? _TopP;
    reasoningEffort = request.ReasoningEffort ?? _ReasoningEffort;   // NEW
}
```

> `ResolveToolChatRequest` is `protected`, so changing its signature is **not** a public-API break; all callers are the three adapters in this repo (see §3.4–3.6). External subclasses that called it would need updating, but it is not part of the public contract.

### 3.4 OpenAI adapter
File: `src/PolyPrompt/Clients/OpenAiClient.cs`
- [ ] In `ToolChatAsync` (~line 204) and `ToolChatStreamingAsync` (~line 260), update the `ResolveToolChatRequest(...)` call to capture `out ReasoningEffort? reasoningEffort` and pass it to `BuildToolChatRequestBody`.
- [ ] Update `BuildToolChatRequestBody` (~line 697) to accept `ReasoningEffort? reasoningEffort` and emit the field after `tool_choice`:

```csharp
if (!string.IsNullOrWhiteSpace(request.ToolChoice))
{
    requestBody["tool_choice"] = request.ToolChoice;
}

if (reasoningEffort != null)                                      // NEW
{
    requestBody["reasoning_effort"] = reasoningEffort.ToOpenAiWireValue();
}
```
- [ ] Confirm the streaming and non-streaming paths both route through the updated builder (they do — both call `BuildToolChatRequestBody`).

### 3.5 Gemini adapter
File: `src/PolyPrompt/Clients/GeminiClient.cs`
- [ ] Thread `reasoningEffort` from both tool-chat entry points (lines ~174 and ~228 call `BuildToolChatRequestBody`) into the builder.
- [ ] Update `BuildToolChatRequestBody` (~line 789) to accept `ReasoningEffort? reasoningEffort` and add a `thinkingConfig` to `generationConfig`:

```csharp
if (topP.HasValue) generationConfig["topP"] = topP.Value;

if (reasoningEffort != null)                                      // NEW
{
    generationConfig["thinkingConfig"] = new Dictionary<string, object>
    {
        { "thinkingBudget", reasoningEffort.ToGeminiThinkingBudget() }
    };
}
```
- [ ] No adapter-local mapping helper — the budget defaults and clamping live on the `ReasoningEffort` value object (§2.1), and callers can override per request via `GeminiThinkingBudget`.

### 3.6 Ollama adapter
File: `src/PolyPrompt/Clients/OllamaClient.cs`
- [ ] Thread `reasoningEffort` from both tool-chat entry points (lines ~218 and ~269) into the builder.
- [ ] Update `BuildToolChatRequestBody` (~line 879) to accept `ReasoningEffort? reasoningEffort` and set the top-level `think` field on the request body (a sibling of `messages`/`options`, **not** inside `options`):

```csharp
Dictionary<string, object> requestBody = new Dictionary<string, object>
{
    { "model", model },
    { "messages", BuildOllamaMessages(request.Messages) },
    { "stream", stream },
    { "options", modelOptions }
};

if (reasoningEffort != null)                                      // NEW
{
    requestBody["think"] = reasoningEffort.ToOllamaThink();
}
```
- [ ] No adapter-local mapping helper — the `think` defaults live on the `ReasoningEffort` value object (§2.1); callers override per request via `OllamaThink`.

### 3.7 Optional Phase 2 — plain chat path (only if shipping parity)
- [ ] `src/PolyPrompt/Options/OpenAiChatCompletionOptions.cs`: add `ReasoningEffort? ReasoningEffort` (reuse the same value object).
- [ ] `OpenAiClient.BuildChatRequestBody` (~line 659): emit `reasoning_effort` when the option is set.
- [ ] (If desired) repeat for Gemini/Ollama chat options + their `BuildChatRequestBody` equivalents.
- [ ] Mirror the tool-path tests for the chat path.

---

## 4. Test plan

Tests live in the deterministic Touchstone suite `src/Test.Shared/LocalBehaviorSuite.cs`, which spins up `LocalOpenAiTestServer`, issues requests, and asserts against **recorded request bodies** (`server.RequestBodies`). xUnit (`Test.Xunit`) and NUnit (`Test.Nunit`) adapters run the same descriptors, and `Test.Automated` runs them standalone. New cases are registered in `LocalBehaviorSuite.Create()` via the existing `Case(...)` helper.

### 4.1 Test harness prerequisites (fixtures)
- [ ] Extend `src/Test.Shared/LocalOpenAiChatRequest.cs` with:
  - `public string? ReasoningEffort { get; set; }` (maps from `reasoning_effort`)
  - `public object? Think { get; set; }` (Ollama; maps from `think`)
  (The SerializationHelper deserializer already resolves `tool_choice` → `ToolChoice`, so snake_case → PascalCase mapping works without extra attributes; verify `reasoning_effort` → `ReasoningEffort` during the first test run.)
- [ ] Extend `src/Test.Shared/LocalGeminiGenerationConfig.cs` with a `ThinkingConfig` shape exposing `ThinkingBudget` (int) so Gemini assertions can read it. Add a `LocalGeminiThinkingConfig` type if the existing config is strongly typed.
- [ ] Confirm `LocalOpenAiTestServer` already records every POST body (it does — `_RequestBodies`) and returns tool-call/`pong` fixtures reused by existing cases. No server changes expected; only add a fixture if a new endpoint/model is needed (it is not).

### 4.2 Positive cases
Register and implement:

- [ ] **`reasoning_effort_openai_toolchat`** — For each of `Minimal/Low/Medium/High`, send a weather tool request with `request.ReasoningEffort = X` through `ToolChatAsync`; assert the recorded OpenAI body's `reasoning_effort` equals the expected wire string (`minimal/low/medium/high`).
- [ ] **`reasoning_effort_openai_toolchat_streaming`** — Same, through `ToolChatStreamingAsync`; assert the streamed request body also carries `reasoning_effort` (proves both entry points thread the value).
- [ ] **`reasoning_effort_instance_default`** — Set `client.ReasoningEffort = High`, leave `request.ReasoningEffort = null`; assert the recorded body has `reasoning_effort: "high"` (instance default applied).
- [ ] **`reasoning_effort_request_overrides_instance`** — `client.ReasoningEffort = Low`, `request.ReasoningEffort = High`; assert the body sends `"high"` (request wins).
- [ ] **`reasoning_effort_gemini_toolchat`** — Gemini client, `ReasoningEffort = High`; assert `generationConfig.thinkingConfig.thinkingBudget == -1`; also assert `Minimal → 0`.
- [ ] **`reasoning_effort_ollama_toolchat`** — Ollama client, `ReasoningEffort = Medium`; assert top-level `think == "medium"`; also assert `Minimal → think:false`.
- [ ] **`reasoning_effort_projection_defaults`** — Pure unit assertions on the value object: for each `Level`, `ToOpenAiWireValue()`, `ToGeminiThinkingBudget()`, and `ToOllamaThink()` return the §2.3 defaults.
- [ ] **`reasoning_effort_presets_and_implicit`** — `ReasoningEffort.High.Level == High`; the implicit conversion `ReasoningEffort e = ReasoningEffortLevel.Low;` yields `Level == Low` with all overrides null.
- [ ] **`reasoning_effort_overrides_win`** — `new ReasoningEffort(High) { GeminiThinkingBudget = 16000 }` sends `thinkingBudget == 16000` (not `-1`) to Gemini while OpenAI still sends `"high"`; `OllamaThink = "true"`/`"false"` emit JSON booleans (not strings). Assert against recorded bodies where a client is involved, and directly on the projections for the pure cases.
- [ ] **`reasoning_effort_override_clamping`** — Setter-level clamping on all three override properties:
  - `GeminiThinkingBudget = -5 → -1`, `= 999999 → 32768`, `= null → ` level default.
  - `OpenAiValue = " HIGH " → "high"` (trim + lower-case); `= "banana" → null` (unrecognized reverts, projection falls back to `Level`).
  - `OllamaThink = "MEDIUM" → "medium"`; `= "maybe" → null`.

### 4.3 Negative / guardrail cases
- [ ] **`reasoning_effort_absent_by_default`** — Send a normal weather tool request with `ReasoningEffort` unset against **all three** providers; assert the recorded body contains **no** `reasoning_effort` (OpenAI), **no** `thinkingConfig` (Gemini), and **no** `think` (Ollama). This is the backward-compatibility lock: default output is unchanged.
- [ ] **`reasoning_effort_undefined_level_throws`** — Set `new ReasoningEffort { Level = (ReasoningEffortLevel)999 }` and assert each projection (`ToOpenAiWireValue`/`ToGeminiThinkingBudget`/`ToOllamaThink`) throws `ArgumentOutOfRangeException` (use the `SharedAssert.ThrowsAsync` pattern already in the suite). Overrides bypass the switch, so this exercises the derive path.
- [ ] **`reasoning_effort_non_reasoning_model_is_noop`** — Set effort against the local server's default model and assert the request still **succeeds** (HTTP 200, `pong`/tool-call fixture) — i.e. sending effort never breaks a normal turn even when the target has no reasoning concept.
- [ ] **`reasoning_effort_empty_messages_still_validates`** — With `ReasoningEffort` set but `Messages` empty, assert `ToolChatAsync` still throws `ArgumentException` from `ResolveToolChatRequest` (new field does not bypass existing validation).

### 4.4 Run matrix
- [ ] `dotnet test` green on **net8.0** and **net10.0**.
- [ ] `Test.Automated` selftest green.
- [ ] xUnit and NUnit adapters green (they enumerate the same descriptors).
- [ ] (Optional) Live provider smoke test via `Test.Automated` with real keys (`--openai-key`, `--gemini-key`, `--ollama-endpoint`) against a reasoning-capable model (e.g. a gpt-5 / o-series model, Gemini 2.5, `gpt-oss:20b`) to confirm the field is accepted end-to-end. Record results in the progress log; do not gate the release on live tests.

---

## 5. Documentation

### 5.1 `CHANGELOG.md`
- [ ] Add a new top entry (keep the existing dated, `Added`/`Changed`/`Fixed` format):

```markdown
## v2.1.0 (2026-08-XX)

### Added

- Added a provider-neutral `ReasoningEffort` control for reasoning-capable models. `ToolChatRequest.ReasoningEffort` and a matching `CompletionClientBase.ReasoningEffort` instance default carry a semantic `ReasoningEffortLevel` (`Minimal`/`Low`/`Medium`/`High`) plus optional, individually clamped per-provider overrides (`OpenAiValue`, `GeminiThinkingBudget`, `OllamaThink`). The value object projects itself onto each provider — OpenAI `reasoning_effort`, Gemini `generationConfig.thinkingConfig`, and Ollama `think` — and is omitted entirely when unset, preserving existing request output.
- Added `ReasoningEffort` value object and `ReasoningEffortLevel` enum in `PolyPrompt.Models`, with static level presets, an implicit conversion from the level, setter clamping/validation on every override, and `ToOpenAiWireValue()`/`ToGeminiThinkingBudget()`/`ToOllamaThink()` projections.
- Added local Touchstone coverage for reasoning-effort translation across OpenAI, Gemini, and Ollama tool chat (streaming and non-streaming), instance-default vs. per-request precedence, per-provider override + clamping behavior, undefined-level guarding, and a backward-compatibility case proving no reasoning field is sent by default.
```

### 5.2 `README.md`
- [ ] Add a **Reasoning effort** subsection near the tool-chat / options documentation, with a short code sample:

```csharp
ToolChatRequest request = new ToolChatRequest { ReasoningEffort = ReasoningEffort.High };
request.Messages.Add(ChatMessage.User("Refactor this function and explain the tradeoffs."));
ToolChatResponse response = await client.ToolChatAsync(request);
```
- [ ] Show a tuned example too (`new ReasoningEffort(ReasoningEffortLevel.High) { GeminiThinkingBudget = 16000 }`) so consumers see the override path, and note the setter clamping.
- [ ] Add a mapping table (copy §2.3) so consumers know exactly what goes on the wire per provider, and that every default is individually overridable.
- [ ] Update the provider **capability matrix** to note reasoning-effort support (OpenAI: native enum; Gemini: budget-mapped; Ollama: model-dependent `think`).
- [ ] State the backward-compatibility guarantee: unset ⇒ unchanged request body.

### 5.3 API XML docs
- [ ] XML doc comments on `ReasoningEffortLevel`, the `ReasoningEffort` value object (presets, `Level`, the three override properties with their clamp ranges, and the three projection methods), and both `ReasoningEffort` properties on `ToolChatRequest`/`CompletionClientBase` (the `GenerateDocumentationFile` build already ships `PolyPrompt.xml` to consumers, so these surface in IntelliSense downstream — e.g. in Mux).

### 5.4 Console harnesses (optional, nice-to-have)
- [ ] Extend the `tc`/`toolchat` command in `OpenAIConsole` / `GeminiConsole` / `OllamaConsole` to accept an optional effort argument, so the interactive harnesses can exercise the new field manually.

---

## 6. Versioning & release

- [ ] Bump `<Version>` in `src/PolyPrompt/PolyPrompt.csproj` from `2.0.1` → `2.1.0`.
- [ ] Confirm the change is strictly additive (no removed/renamed public members; `ResolveToolChatRequest` is `protected`, not public) → **minor** bump is correct per SemVer.
- [ ] `dotnet pack` (the project has `GeneratePackageOnBuild`) and verify `PolyPrompt.2.1.0.nupkg` + `.snupkg` build clean on both TFMs.
- [ ] Sanity-check the packaged `PolyPrompt.xml` includes the new members.
- [ ] Tag and push per existing release flow (see prior `Release PolyPrompt vX` commits); update any downstream consumer (Mux) to `2.1.0` in a separate change once published.

---

## 7. Acceptance criteria (definition of done)

- [ ] `ToolChatRequest.ReasoningEffort` and `CompletionClientBase.ReasoningEffort` exist, documented, defaulting to `null`.
- [ ] OpenAI, Gemini, and Ollama tool chat (streaming + non-streaming) emit the correct provider field when set and **nothing** when unset.
- [ ] Request value overrides instance default; instance default applies when the request is unset.
- [ ] Per-provider overrides win over level defaults, and all three override setters clamp/validate their inputs (`GeminiThinkingBudget` to `-1..32768`; `OpenAiValue`/`OllamaThink` normalized and out-of-set values reverted to null).
- [ ] All positive and negative tests in §4 pass on net8.0 and net10.0, across the raw, xUnit, and NUnit runners.
- [ ] README, CHANGELOG, and XML docs updated; version bumped to `2.1.0`; package builds clean.
- [ ] Backward compatibility proven by the `reasoning_effort_absent_by_default` test (default request bodies are byte-for-byte unchanged).

---

## 8. Risks & open questions

- [ ] **Gemini budget numbers** (`Low=1024`, `Medium=8192`, `High=-1`) and the `32768` clamp ceiling are heuristic and model-range-dependent; confirm against a live Gemini 2.5 model and tune the defaults/ceiling on the value object if needed (callers can already override per request via `GeminiThinkingBudget`). — _note:_ ______
- [ ] **Ollama `think` shape** varies by model/version (boolean vs. string level). Confirm target models (gpt-oss, deepseek-r1) accept the string form; the boolean `false` path for `Minimal` is the safe universal, and callers can override via `OllamaThink`. — _note:_ ______
- [ ] **Serializer key mapping** — verify SerializationHelper serializes the dictionary key `reasoning_effort`/`think`/`thinkingConfig` verbatim (dictionary keys are literal, so this should hold) and that the test fixture deserializes `reasoning_effort` → `ReasoningEffort`. — _note:_ ______
- [ ] **Scope of Phase 2 (plain chat path)** — decide before release whether `v2.1.0` ships chat-path parity or defers it to `v2.2.0`. Either way the version stays minor. — _decision:_ ______

---

## 9. Progress log

_Add dated entries as work proceeds. Newest first._

| Date | Author | Update |
|---|---|---|
| 2026-08-12 | Claude (for Joel) | Implemented in full: `ReasoningEffortLevel` enum + `ReasoningEffort` value object (presets, implicit conversion, clamping override setters, three projections); `ToolChatRequest.ReasoningEffort` + `CompletionClientBase.ReasoningEffort` instance default + `ResolveToolChatRequest` out-param; OpenAI/Gemini/Ollama adapter emission. Test fixtures + `LocalRequestParser` wiring and 12 new Touchstone cases added. Version bumped `2.0.1 → 2.1.0`; CHANGELOG + README updated. Tests: **46/46 pass** on net8.0 and net10.0 across xUnit, NUnit, and the Test.Automated runner; Release pack produces `PolyPrompt.2.1.0.nupkg`. Phase 2 (plain chat path) intentionally deferred. |
