# PolyPrompt Bug Fix Report: Gemini Tool Calling

This report describes four defects in PolyPrompt 2.7.1 that break tool calling against Google Gemini, both through the native Gemini client and through Gemini's OpenAI-compatible endpoint. They were found while building Aegon, which runs agents through PolyPrompt directly (its Provider API mode) and through Mux 1.0.1, which embeds PolyPrompt 2.7.1. Aegon currently works around the first three in its own code; Mux has no workarounds, so Mux cannot complete a multi-turn tool loop with any Gemini 3 model.

Each section gives the symptom, the exact code location, the cause, the fix, and how to verify it. The defects are listed in priority order. Bugs 1 and 2 together are what block Gemini 3 tool loops today.

## Summary

| # | Defect | Severity | Affected clients | Where |
|---|---|---|---|---|
| 1 | Gemini 3 thought signatures are dropped from tool calls, so replaying a tool call fails with HTTP 400 | Blocker | `GeminiClient`, `VertexAiClient` (inherits), `OpenAiClient` when pointed at Gemini's OpenAI-compatible endpoint | `GeminiClient.ParseGeminiToolCall`, `ParseGeminiToolCallDelta`, `BuildGeminiParts`; `OpenAiClient.ParseOpenAiToolCall`, `BuildOpenAiToolCalls`, streaming delta parsing; `ToolCall`, `ToolCallDelta`, `ToolCallAssembly` models |
| 2 | Tool results are sent with role `function` instead of `user` | Blocker (on current Gemini models) | `GeminiClient`, `VertexAiClient` | `GeminiClient.NormalizeGeminiRole` |
| 3 | A tool result that is a JSON array (or scalar) throws `JsonException` while building the request | High | `GeminiClient`, `VertexAiClient` | `GeminiClient.DeserializeDictionaryOrResult` (and `DeserializeDictionaryOrEmpty` for arguments) |
| 4 | Tool parameter schemas are passed verbatim into Gemini's strict `parameters` field, so any keyword outside Gemini's OpenAPI subset fails the whole request | High | `GeminiClient`, `VertexAiClient` | `GeminiClient.BuildGeminiTools` |

A fifth, smaller issue (tool call ids) is listed at the end because it touches the same code and is worth fixing in the same pass.

## Bug 1: Gemini 3 thought signatures are dropped

### Symptom

Any tool loop with a Gemini 3 model (observed with `gemini-3.5-flash` and `gemini-3.5-flash-lite`) fails on the second model call, the one that carries the first tool result. Gemini returns:

```
HTTP 400 INVALID_ARGUMENT
Function call is missing a thought_signature in functionCall parts. This is required for tools to work correctly,
and missing thought_signature may lead to degraded model performance. Additional data, function call
`aegon.brief_update`, position 2. Please refer to https://ai.google.dev/gemini-api/docs/thought-signatures for more details.
```

The same error comes back through both paths:

- the native Gemini API (`generateContent` / `streamGenerateContent`) via `GeminiClient`
- Gemini's OpenAI-compatible endpoint (`https://generativelanguage.googleapis.com/v1beta/openai/`) via `OpenAiClient`

Reproduced on 2026-09-30 with Mux 1.0.1 (PolyPrompt 2.7.1) calling an MCP tool: the first tool call executes, then the follow-up request is rejected.

### Cause

Gemini 3 attaches an opaque `thoughtSignature` to each function call it emits, and requires that signature to be sent back, unchanged, when the conversation history containing that function call is replayed. PolyPrompt never captures the signature and so can never send it back.

**Native API response shape.** The signature is a sibling of `functionCall` inside the part, not inside `functionCall`:

```json
{ "candidates": [ { "content": { "role": "model", "parts": [
  { "functionCall": { "name": "get_weather", "args": { "city": "Paris" } }, "thoughtSignature": "Cr4DAYm..." }
] } } ] }
```

- `GeminiClient.ParseGeminiToolCall` (non-streaming, around line 1093) receives only `part["functionCall"]`, so `part["thoughtSignature"]` is never read.
- `GeminiClient.ParseGeminiToolCallDelta` (streaming, around line 1384) reads `part` but ignores `thoughtSignature`.
- `ToolCall`, `ToolCallDelta`, and `ToolCallAssembly` have no field to carry it.
- `GeminiClient.BuildGeminiParts` (around line 941) replays an assistant tool call as `{ "functionCall": { "name", "args" } }` with no `thoughtSignature` alongside it.

**OpenAI-compatible endpoint shape.** Gemini puts the signature in a vendor extension on each tool call:

```json
{ "tool_calls": [ {
  "id": "call_1", "type": "function",
  "function": { "name": "get_weather", "arguments": "{\"city\":\"Paris\"}" },
  "extra_content": { "google": { "thought_signature": "Cr4DAYm..." } }
} ] }
```

- `OpenAiClient.ParseOpenAiToolCall` (around line 912) and the streaming tool-call delta parser (around line 1156) ignore `extra_content`.
- `OpenAiClient.BuildOpenAiToolCalls` (around line 820) emits only `id`, `type`, and `function`.

### Fix

1. **Model.** Add a nullable property to `ToolCall`, for example `public string? ThoughtSignature { get; set; } = null;` with XML docs explaining it is an opaque provider token that must be round-tripped unchanged. Add the same property to `ToolCallDelta`, and make `ToolCallAssembly.Apply` carry it into the assembled `ToolCall` (first non-empty value wins). If you prefer something provider-neutral, a `Dictionary<string, string>? ProviderMetadata` works too, but a named property is easier for callers such as Mux to persist.
2. **Native parse.** In the non-streaming path, pass the whole part to the parser (or read `part["thoughtSignature"]` before calling it) and set `ToolCall.ThoughtSignature`. Do the same in `ParseGeminiToolCallDelta`. Gemini may also put a signature on a text part or on the first of several parallel function calls only. Capture it wherever it appears on a function-call part, and do not invent one for calls that lack it.
3. **Native replay.** In `BuildGeminiParts`, when a `ToolCall` has a signature, emit it as a sibling:
   ```json
   { "functionCall": { "name": "...", "args": { } }, "thoughtSignature": "<value>" }
   ```
4. **Replayed history without a signature.** History that did not come from Gemini (another provider, an older PolyPrompt, a hand-built transcript) has no signature. Google documents a sentinel for exactly this case: set `"thoughtSignature": "skip_thought_signature_validator"` on those function-call parts. Only apply it when the target model is Gemini and the part has no real signature. Aegon uses this sentinel today and it works on `gemini-3.5-flash`.
5. **OpenAI-compatible parse and replay.** In `ParseOpenAiToolCall` and the streaming delta parser, read `extra_content.google.thought_signature` into `ToolCall.ThoughtSignature`. In `BuildOpenAiToolCalls`, when a signature is present, add `"extra_content": { "google": { "thought_signature": "<value>" } }` to that tool call item. Emitting `extra_content` only when a signature exists keeps requests to real OpenAI unchanged.
6. **Documentation.** Note in the README and CHANGELOG that callers who persist conversations (Mux does) must store and restore `ToolCall.ThoughtSignature`, or Gemini 3 tool loops will still fail on the next turn.

### Downstream note for Mux

Mux keeps its own conversation history between model calls and across resumed sessions. After this fix, Mux must persist `ToolCall.ThoughtSignature` wherever it stores tool calls, and restore it when rebuilding the `ChatMessage` list, or the signature will be lost after the first round trip. Then rebuild and release Mux against the fixed PolyPrompt.

### Verification

- Unit: parse a captured native response containing `thoughtSignature` and assert the `ToolCall` carries it; build a request from that `ToolCall` and assert the part contains `functionCall` and `thoughtSignature` as siblings. Repeat for the streaming parser, and for the OpenAI-compatible shape with `extra_content`.
- Unit: a `ToolCall` without a signature, sent to Gemini, gets the `skip_thought_signature_validator` sentinel; the same `ToolCall` sent to OpenAI gets no `extra_content`.
- Live: a two-step tool loop (model calls a tool, receives the result, answers) against `gemini-3.5-flash`, both native and through the OpenAI-compatible endpoint, streaming and non-streaming. All four must complete with no HTTP 400.

## Bug 2: Tool results are sent with role `function`

### Symptom

On current Gemini models, the request that carries a tool result is rejected on the second tool turn. Aegon's Phase 0 spike reproduced this with the native client; it is the reason Aegon rewrites every outgoing Gemini request body.

### Cause

`GeminiClient.NormalizeGeminiRole` (around line 1136) maps `tool` to `function`:

```csharp
if (string.Equals(role, "tool", StringComparison.OrdinalIgnoreCase)) return "function";
```

Gemini's `contents` array accepts only the roles `user` and `model`. A `functionResponse` part belongs in a `user` turn.

### Fix

Map `tool` (and `function`) to `user`. `BuildGeminiParts` already emits the `functionResponse` part for those messages, so only the role changes. Consecutive tool results for parallel calls may be sent as several `functionResponse` parts in one `user` content, which Gemini prefers; merging them is optional but recommended.

### Verification

- Unit: a history of user, model (with a `functionCall`), and tool messages produces `contents` roles `user`, `model`, `user`, and the last content holds the `functionResponse`.
- Live: the same two-step tool loop as bug 1 succeeds. Bugs 1 and 2 must both be fixed for Gemini 3.

## Bug 3: Array or scalar tool results throw

### Symptom

When a tool returns a JSON array (for example `[]` or `[{"id":1}]`), building the next Gemini request throws before anything is sent:

```
System.Text.Json.JsonException: The JSON value could not be converted to
System.Collections.Generic.Dictionary`2[System.String,System.Object]. Path: $ | LineNumber: 0 | BytePositionInLine: 1.
   at SerializationHelper.Serializer.DeserializeJson[T](String json)
   at PolyPrompt.Clients.GeminiClient.DeserializeDictionaryOrResult(String json)
```

Many MCP servers return arrays for list tools, so this is common in practice. Aegon hit it with a tool that returned a list of saved writing styles.

### Cause

`DeserializeDictionaryOrResult` (around line 1116) assumes that `DeserializeJson<Dictionary<string, object>>` returns null for content that is not an object. For a JSON array, a JSON scalar (`42`, `true`, `"text"`), and possibly for text that is not JSON at all, it throws instead. The same assumption exists in `DeserializeDictionaryOrEmpty` (around line 1108), which parses tool-call arguments.

### Fix

Classify the content before deserializing, for example with `JsonDocument`:

- a JSON object: use it as the `response` dictionary, as today
- a JSON array or scalar: wrap it as `{ "result": <parsed value> }`, keeping the structure rather than stringifying it
- empty or not JSON: wrap it as `{ "result": "<raw text>" }`

For arguments, a non-object value should become an empty object, or be wrapped, but never throw. Catch `JsonException` around every parse of tool content.

### Verification

Unit tests for tool results `{}`, `{"a":1}`, `[]`, `[1,2]`, `42`, `"x"`, `plain text`, and the empty string: none may throw, and each must produce a `functionResponse.response` that is a JSON object. A live tool loop where the tool returns an array must complete.

## Bug 4: Tool schemas are not sanitized for Gemini's `parameters` field

### Symptom

Mux's default tool set fails every Gemini request:

```
HTTP 400 INVALID_ARGUMENT
Invalid JSON payload received. Unknown name "mux_runtime_context" at
'tools[0].function_declarations[10].parameters': Cannot find field.
```

One tool with one unexpected keyword rejects the whole request, so no tool (and no plain answer) works.

### Cause

`GeminiClient.BuildGeminiTools` (around line 978) copies `ToolDefinition.Parameters` verbatim into `functionDeclarations[].parameters`. That field is Gemini's `Schema` object, a strict subset of OpenAPI 3.0: unknown keys are rejected, not ignored. The same failure occurs for common JSON Schema keywords that Gemini's `parameters` does not accept, such as `$schema`, `$id`, `$defs` or `definitions`, `additionalProperties`, `const`, `examples`, and vendor extensions, depending on the model and API version.

The offending key in this case comes from Mux's schema. That is a Mux-side bug too, but PolyPrompt should not let one tool's schema take down every request.

### Fix

Choose one of these:

1. **Preferred:** send the schema through `functionDeclarations[].parametersJsonSchema` instead of `parameters`. Gemini accepts standard JSON Schema there. Keep a fallback to `parameters` if you need to support older API versions, and test which keywords `parametersJsonSchema` still rejects.
2. **Or:** sanitize the schema recursively before sending it as `parameters`. Keep only the keys Gemini's `Schema` accepts: `type`, `format`, `title`, `description`, `nullable`, `enum`, `default`, `example`, `properties`, `required`, `items`, `minItems`, `maxItems`, `minProperties`, `maxProperties`, `minLength`, `maxLength`, `pattern`, `minimum`, `maximum`, `anyOf`, `propertyOrdering`. Also translate `"type": ["string", "null"]` into `"type": "string", "nullable": true`, and drop the rest.

Whichever you choose, log (at debug level) the keys that were removed, so schema problems stay visible.

### Verification

- Unit: a schema containing `additionalProperties`, `$schema`, `const`, a type array, and an unknown vendor key produces a request Gemini accepts.
- Live: a request with twenty or more tool definitions of mixed quality, including one with `mux_runtime_context`, succeeds against `gemini-3.5-flash`.

## Also fix: Gemini function call ids

`ParseGeminiToolCall` (non-streaming) always sets `Id = "gemini-call-" + index` and ignores the `id` that Gemini 3 returns in `functionCall.id`. The streaming parser already prefers the real id. When a real id exists, use it in both paths. When replaying a tool result, include it as `functionResponse.id` so Gemini can match results to parallel calls. Low risk, same code.

## Suggested implementation order

1. Bug 3 (smallest and self-contained).
2. Bug 2 (a one-line role change plus tests).
3. Bug 1 (model change plus parse and replay in both clients, native and OpenAI-compatible, streaming and non-streaming).
4. Bug 4 (schema handling).
5. Function call ids.

Add regression tests to the existing suites in `src/Test.Shared` (run by `Test.Automated`, `Test.Xunit`, and `Test.Nunit`), plus live checks gated on `GEMINI_API_KEY`. `GeminiConsole` is a convenient place for a manual two-step tool loop. Update `CHANGELOG.md`, and bump the version only as the maintainer decides.

## Acceptance criteria

- A multi-turn tool loop with `gemini-3.5-flash` completes through `GeminiClient` (streaming and non-streaming) and through `OpenAiClient` pointed at Gemini's OpenAI-compatible endpoint.
- `VertexAiClient` passes the same loop, since it inherits the Gemini request building.
- Tool results that are arrays, scalars, or plain text never throw.
- A tool list containing a schema with unsupported keywords does not fail the request.
- Requests to OpenAI, Anthropic, Ollama, and the other providers are byte-for-byte unchanged when no thought signature is present.
- After a Mux rebuild on the fixed PolyPrompt, with Mux persisting `ThoughtSignature`, Mux completes an MCP tool loop with a Gemini 3 model.

## Workarounds to remove afterwards (in Aegon, not PolyPrompt)

Once a fixed PolyPrompt is released, Aegon can drop:

- `RequestRewriter.FixGeminiToolTurns` (rewrites role `function` to `user` and inserts the `skip_thought_signature_validator` sentinel)
- `ProviderApiHarness.ToolResultText` (wraps array tool results in an object)
