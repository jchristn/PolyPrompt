# PolyPrompt — Surface Model Thinking to the Caller (`THINKING.md`)

**Feature:** Capture a reasoning model's *thinking* (its reasoning / chain-of-thought output) and return it to the caller as a first-class, separate channel — on streamed chunks and on the accumulated responses, for both chat and tool-chat, streaming and non-streaming.
**Target release:** `v2.2.0` — **additive, minor** bump. No existing field changes; reasoning is simply absent (null) for responses that carry none.
**Status:** ✅ **Complete — implemented and released as `v2.2.0`.**
**Owner:** Implemented by Claude for Joel.
**Drafted:** 2026-08-12 · **Shipped:** 2026-08-12

> **Done.** All six models, all three clients (chat + tool-chat, streaming + non-streaming), and the base
> accumulation are implemented; reasoning field-name literals were centralized as per-client constants to
> reduce fragility. Tests: **56/56 pass** on net8.0 and net10.0 across xUnit and NUnit; Test.Automated 56
> passed, 0 failed. Release pack produces `PolyPrompt.2.2.0.nupkg`. The optional console-harness demo and
> the open items in §13 are the only unchecked boxes.

---

## How to use this document

Every task is a checkbox. Annotate as you go: `- [x]` done · `- [~]` in progress · `- [ ]` not started · `- [!]` blocked (add a note after an em dash). Keep the **Progress log** at the end current — it is the one place a reviewer looks for status. Do not delete finished tasks; check them off so the history stays auditable.

File paths are relative to `C:\Code\PolyPrompt`. Line references reflect the tree at drafting time; confirm before editing.

---

## 1. Why this matters

Reasoning models produce two things: the reasoning they work through, and the answer they land on. PolyPrompt returns the answer and drops the reasoning on the floor. Every provider streams reasoning on its own channel — OpenAI-compatible servers send `reasoning_content`, Ollama sends `message.thinking`, Gemini marks thought parts with `thought: true` — and none of it reaches the caller, because the parsers only pick up `content`/`text`. An application that wants to show the model's thinking, log it, or judge it has no way to get at it through PolyPrompt today.

The v2.1.0 reasoning-effort control made models think harder; it gave callers no way to see the result of that thinking. This closes the loop. A caller reads `response.Reasoning` (or the per-chunk `ReasoningText` while streaming) and gets the model's deliberation as a distinct string, cleanly separated from the answer. Nothing about the existing `Text`/`ToolCalls` surface changes.

One boundary keeps the design honest: reasoning is **return-only**. PolyPrompt surfaces it to the caller but does not fold it back into `ToAssistantMessage()` or otherwise resend it upstream — providers reject or ignore echoed reasoning and it wastes context. What the caller does with the reasoning (display it, store it, discard it) is the caller's choice.

---

## 2. Design

### 2.1 Where reasoning comes from (per provider)

| Provider | Streaming source | Non-streaming source |
|---|---|---|
| OpenAI-compatible (`OpenAiClient`) | `choices[].delta.reasoning_content` (fallback `choices[].delta.reasoning`) | `choices[].message.reasoning_content` (fallback `reasoning`) |
| Ollama (`OllamaClient`) | `message.thinking` on each streamed object | `message.thinking` |
| Gemini (`GeminiClient`) | `candidates[].content.parts[]` where `part.thought == true` | same |

Reasoning appears only when the model is a reasoning model and thinking is active (for example when a reasoning effort is set). For every other response the fields stay null, which is what makes this release additive.

### 2.2 What the caller sees (new surface)

Reasoning is exposed the same way text already is — a per-chunk delta while streaming, and an accumulated string on the response:

- **Tool chat:** `ToolChatStreamingChunk.ReasoningText`, `ToolChatStreamingResponse.Reasoning`, `ToolChatResponse.Reasoning`.
- **Chat:** `ChatStreamingChunk.ReasoningText`, `ChatStreamingResponse.Reasoning`, `ChatResponse.Reasoning`.

All are nullable. A streamed chunk that carries only reasoning has `Text == null` and a non-null `ReasoningText`; a chunk that carries only answer text is the reverse. They never overlap.

### 2.3 Rules that keep text and reasoning separate

- **Strict separation.** Reasoning content is never appended to `Text`, and answer text is never appended to `Reasoning`. A Gemini `thought` part goes to reasoning; a normal part goes to text. This holds on every path.
- **Empty is null.** An empty or whitespace-only reasoning field is normalized to null, so callers can test `Reasoning != null` rather than guarding for `""`.
- **Accumulation mirrors text.** The base timing wrappers accumulate `ReasoningText` deltas onto the response's `Reasoning` exactly as they already accumulate text, so after enumerating `Chunks` the caller finds the full reasoning on the response.
- **Return-only.** `ToAssistantMessage()` is unchanged: it carries text or tool calls, never reasoning. Reasoning is not resent to the provider.

### 2.4 Scope

Covered: chat and tool-chat, streaming and non-streaming, across all three clients. Out of scope: the legacy text-generation (`/completions`) path — reasoning models are not used through it, and it has no message envelope to carry a reasoning field. Note that exclusion in the docs rather than adding a dead field.

---

## 3. Implementation — models

Each new member is nullable, defaults to null, and carries XML documentation. One responsibility per edit; no existing member changes.

- [x] `src/PolyPrompt/Models/ToolChatStreamingChunk.cs` — add `string? ReasoningText` (per-chunk reasoning delta; null when the chunk has none).
- [x] `src/PolyPrompt/Models/ToolChatStreamingResponse.cs` — add `string? Reasoning` (accumulated reasoning; null when the model produced none).
- [x] `src/PolyPrompt/Models/ToolChatResponse.cs` — add `string? Reasoning`.
- [x] `src/PolyPrompt/Models/ChatStreamingChunk.cs` — add `string? ReasoningText`.
- [x] `src/PolyPrompt/Models/ChatStreamingResponse.cs` — add `string? Reasoning`.
- [x] `src/PolyPrompt/Models/ChatResponse.cs` — add `string? Reasoning`.
- [x] Confirm `ToAssistantMessage()` on both response types is left unchanged (reasoning is not carried into a follow-up request).

---

## 4. Implementation — OpenAI client (`src/PolyPrompt/Clients/OpenAiClient.cs`)

- [x] `ReadOpenAiToolChatChunks` — when reading `choice.delta`, read `reasoning_content` (fallback `reasoning`) into `streamChunk.ReasoningText`, normalizing empty to null. Leave the `content` handling exactly as is.
- [x] `PopulateOpenAiToolChatResponse` — read `message.reasoning_content` (fallback `reasoning`) into `toolResponse.Reasoning`.
- [x] `ReadOpenAiChatChunks` — read the same `delta.reasoning_content` into `ChatStreamingChunk.ReasoningText`.
- [x] `ChatAsync` — after extracting `message.content`, read `message.reasoning_content` into `chatResponse.Reasoning`.
- [x] Add a small private helper (for example `ReadReasoningDelta(Dictionary<string,object> delta)`) so the fallback order and empty→null rule live in one place and are reused by the chat and tool-chat readers.

---

## 5. Implementation — Ollama client (`src/PolyPrompt/Clients/OllamaClient.cs`)

- [x] Streaming tool chat reader — read `message.thinking` from each streamed object into `ToolChatStreamingChunk.ReasoningText`.
- [x] Non-streaming tool chat (`PopulateOllamaToolChatResponse` or equivalent) — read `message.thinking` into `ToolChatResponse.Reasoning`.
- [x] Streaming chat reader and `ChatAsync` — read `message.thinking` into `ChatStreamingChunk.ReasoningText` / `ChatResponse.Reasoning`.
- [x] Normalize empty/whitespace `thinking` to null.

---

## 6. Implementation — Gemini client (`src/PolyPrompt/Clients/GeminiClient.cs`)

- [x] Parts parsing (streaming and non-streaming, chat and tool chat) — split `parts[]` by the `thought` flag: a part with `thought == true` contributes to reasoning; every other part contributes to text/tool calls exactly as today. A thought part must never land in `Text`, and a normal part must never land in `Reasoning`.
- [x] Surface reasoning on `ReasoningText` (streamed) and `Reasoning` (accumulated / non-streaming), normalizing empty to null.
- [x] Confirm existing `text` and `functionCall` extraction is unchanged for non-thought parts.

---

## 7. Implementation — base accumulation (`src/PolyPrompt/Clients/CompletionClientBase.cs`)

- [x] `WrapToolChatChunksWithTiming` — alongside the existing `response.Text` accumulation, append each chunk's `ReasoningText` to `response.Reasoning` (initialize on first non-null delta so a no-reasoning stream leaves it null). Treat reasoning-only chunks as content for timing/`ChunkCount` the same way text and tool-call deltas are counted, so time-to-first-token still reflects the first thing the model emitted.
- [x] `WrapChunksWithTiming` (chat) — accumulate `ReasoningText` onto `ChatStreamingResponse.Reasoning` the same way. (Chat streaming does not accumulate `Text` today; `Reasoning` is a deliberate new convenience so a caller need not re-concatenate.)
- [x] Keep all accumulation null-safe: no reasoning ⇒ `Reasoning` stays null, not `""`.

---

## 8. Tests (positive and negative)

Tests live in `src/Test.Shared`, run through Test.Automated, Test.Xunit, and Test.Nunit, and use the local mock server (`LocalOpenAiTestServer`) on `127.0.0.1`. Drive reasoning from a request marker so cases stay deterministic.

### 8.1 Harness
- [x] Extend `LocalOpenAiTestServer` so a marker in the request makes each provider handler emit reasoning: OpenAI SSE `delta.reasoning_content` deltas, Ollama `message.thinking`, Gemini `thought: true` parts — for both the streaming and non-streaming shapes.

### 8.2 Positive cases (per provider, in `LocalBehaviorSuite`)
- [x] **Streaming tool chat surfaces reasoning.** Chunks carry `ReasoningText`; after enumeration `response.Reasoning` equals the concatenation of the deltas; `response.Text` is the answer only.
- [x] **Non-streaming tool chat surfaces reasoning.** `ToolChatResponse.Reasoning` is populated; `Text`/`ToolCalls` unchanged.
- [x] **Streaming and non-streaming chat surface reasoning.** `ChatStreamingChunk.ReasoningText` / `ChatStreamingResponse.Reasoning` and `ChatResponse.Reasoning` populated.
- [x] **OpenAI fallback field.** A server that emits `reasoning` (not `reasoning_content`) is still captured.
- [x] **Gemini split.** A response mixing a `thought` part and a normal part puts each on the correct channel.
- [x] **Accumulation equals deltas.** The accumulated `Reasoning` equals the ordered concatenation of every `ReasoningText`.

### 8.3 Negative / guardrail cases
- [x] **No reasoning ⇒ null (backward-compatibility lock).** A normal (non-reasoning) response leaves `ReasoningText` and `Reasoning` null across every path; existing suites are unaffected.
- [x] **Empty reasoning ⇒ null.** A reasoning field present but empty/whitespace normalizes to null, not `""`.
- [x] **No leakage into text.** A reasoning-only chunk has `Text == null`; the accumulated `Text` never contains reasoning, and `Reasoning` never contains answer text (assert on a mixed stream).
- [x] **Reasoning is not resent.** After a reasoning turn, `ToAssistantMessage()` produces a message with no reasoning content, and a follow-up request body contains none.
- [x] **Malformed field tolerated.** A non-string or missing reasoning field does not throw and never corrupts the answer text; the response stays successful. _(Note: the serializer boxes JSON values as `JsonElement`, so a non-string reasoning field surfaces as its JSON text via `ToString()` rather than as null — tolerated, not silently dropped. The test asserts the guaranteed property: success + unaffected answer.)_

### 8.4 Run matrix
- [x] `dotnet test src/Test.Xunit` and `src/Test.Nunit` green on **net8.0** and **net10.0**; `dotnet run --project src/Test.Automated` exits 0.
- [x] Full solution builds with **0 warnings**.

---

## 9. Documentation

### 9.1 README.md
- [x] Add a **Reasoning / Thinking** subsection near the tool-calling and reasoning-effort docs: what the new fields are, which provider channel each maps to, that reasoning is separate from `Text` and return-only, and a short sample:
  ```csharp
  ToolChatStreamingResponse stream = await client.ToolChatStreamingAsync(request);
  await foreach (ToolChatStreamingChunk chunk in stream.Chunks)
  {
      if (chunk.ReasoningText != null) Console.Write(chunk.ReasoningText); // the thinking
      if (chunk.Text != null) Console.Write(chunk.Text);                   // the answer
  }
  // After enumeration: stream.Reasoning holds the full thinking, stream.Text the full answer.
  ```
- [x] Add a **Reasoning capture** row to the provider capability matrix (OpenAI `reasoning_content`, Ollama `message.thinking`, Gemini thought parts).
- [x] State the backward-compatibility guarantee: responses without reasoning are unchanged; the fields are null.

### 9.2 CHANGELOG.md
- [x] New top entry, matching the existing dated `Added`/`Changed` format:
  ```markdown
  ## v2.2.0 (2026-08-XX)

  ### Added

  - Added reasoning ("thinking") capture: streamed chunks expose `ReasoningText` and responses expose an
    accumulated `Reasoning`, on both chat and tool-chat (streaming and non-streaming), across the OpenAI-
    compatible, Ollama, and Gemini clients — parsed from `reasoning_content`, `message.thinking`, and
    Gemini thought parts respectively. Reasoning is kept separate from answer text, normalized to null when
    absent or empty, and is return-only (never resent via `ToAssistantMessage`).
  - Added local Touchstone coverage for reasoning capture and accumulation per provider, plus negative
    cases proving no-reasoning responses stay null, reasoning never leaks into text, and reasoning is not
    carried back into follow-up requests.
  ```
- [x] XML docs on every new member (the `GenerateDocumentationFile` build ships `PolyPrompt.xml` to consumers, so these appear in downstream IntelliSense).

### 9.3 Console harnesses (optional)
- [ ] (Not done — optional, deferred) Extend the `tc`/`toolchat` command in the OpenAI/Ollama/Gemini consoles to print streamed `ReasoningText` dimmed above the answer, so the harnesses demonstrate the new channel.

---

## 10. Versioning & release

- [x] Bump `<Version>` in `src/PolyPrompt/PolyPrompt.csproj` from `2.1.0` → `2.2.0`.
- [x] Confirm the change is strictly additive (only new nullable members and new parsing; no removed or renamed public members) → **minor** bump is correct.
- [x] `dotnet pack` and verify `PolyPrompt.2.2.0.nupkg` + `.snupkg` build clean on both TFMs, and that `PolyPrompt.xml` includes the new members.
- [x] Tag and publish per the existing release flow; downstream consumers (e.g. mux) move to `2.2.0` in a separate change once published.

---

## 11. House style / quality

Follow PolyPrompt's existing conventions: namespace at top with usings inside; XML docs on all public members; backing fields only where a setter validates; no tuples; `.ConfigureAwait(false)` on awaits; `CancellationToken` on async methods; explicit types (no `var`); one type per file; specific exceptions with clear messages; nullable reference types enabled; no `Console.WriteLine` in the library. The reasoning parse mirrors the existing content parse, so it should read as a natural extension of each reader, not a new subsystem.

---

## 12. Acceptance criteria (definition of done)

- [x] `ReasoningText` (chunks) and `Reasoning` (responses) exist on chat and tool-chat, streaming and non-streaming, documented and defaulting to null.
- [x] OpenAI (`reasoning_content`/`reasoning`), Ollama (`message.thinking`), and Gemini (thought parts) all populate reasoning, streamed and non-streamed.
- [x] Reasoning is strictly separated from text, normalized to null when absent/empty, accumulated to match the deltas, and never resent via `ToAssistantMessage`.
- [x] Positive and negative tests in §8 pass on net8.0 and net10.0 across the raw, xUnit, and NUnit runners; Release builds with zero warnings.
- [x] README and CHANGELOG updated; XML docs added; version bumped to `2.2.0`; the package builds clean.
- [x] A no-reasoning response is byte-for-byte the same experience as v2.1.0 (proved by the backward-compatibility case).

---

## 13. Risks & open questions

- [x] **Field-name variance across OpenAI-compatible servers.** — _resolved (code) / open (live):_ the reader accepts both `reasoning_content` and `reasoning` and tolerates absence (deterministic tests cover both). Live verification against a hosted reasoning model and `gpt-oss` is still pending.
- [x] **Gemini thought signatures.** — _decision:_ scoped to the human-readable thought text; thought-signature metadata is ignored this release.
- [x] **Chat streaming accumulation asymmetry.** — _decision:_ shipped `ChatStreamingResponse.Reasoning` as a convenience without adding `Text` accumulation (which never existed); accepted for v2.2.0.
- [x] **Reasoning volume.** — _decision:_ returned verbatim, no truncation in the library; the caller decides what to keep.

---

## 14. Progress log

_Add dated entries as work proceeds. Newest first._

| Date | Author | Update |
|---|---|---|
| 2026-08-12 | Claude (for Joel) | **Test expansion (post-review).** Closed the full 12-cell reasoning matrix — added OpenAI non-streaming tool chat, Ollama non-streaming chat + streaming tool chat, and Gemini tool chat (streaming + non-streaming) reasoning cases — plus the OpenAI `reasoning` fallback field, malformed-field tolerance, per-provider empty→null (Ollama/Gemini), reasoning-only-chunk timing metrics, Gemini multi-part text concatenation, and an effort+capture integration case. Reasoning suite now 21 cases; **67/67 pass** on net8.0 and net10.0 across xUnit and NUnit; Test.Automated 67 passed, 0 failed. |
| 2026-08-12 | Claude (for Joel) | Implemented end to end. Six model members (`ReasoningText`/`Reasoning`) added; OpenAI (`reasoning_content`/`reasoning`), Ollama (`message.thinking`), and Gemini (`thought` parts) parse reasoning on chat + tool-chat, streaming + non-streaming; base timing wrappers accumulate reasoning for both paths. Reasoning field-name literals centralized as per-client `const` (fragility reduction requested mid-flight). 10 new Touchstone cases (positive per provider/path + negatives: absent→null, empty→null, no-leak-into-text, not-resent). Version bumped `2.1.0 → 2.2.0`; README + CHANGELOG updated. Tests: **56/56 pass** on net8.0 and net10.0 across xUnit and NUnit; Test.Automated 56 passed / 1 skipped (live) / 0 failed. Release pack builds `PolyPrompt.2.2.0.nupkg`. Not done: optional console-harness demo (§9.3); live-provider verification of field-name variance (§13). |
