# Streaming lifecycle in PolyPrompt 3.5

`ChatStreamingResponse`, `ToolChatStreamingResponse`, `GenerationStreamingResponse`, and native `XAiStreamingResponse` derive from `StreamingResponseBase` and implement `IStreamingResponse`. Their lifecycle works across providers and without telemetry listeners.

## State contract

| State | Meaning |
|---|---|
| `NotStarted` | Default for a newly constructed response; no HTTP stream has opened. |
| `Open` | HTTP headers were accepted; enumeration has not started. |
| `Streaming` | Enumeration is active. Output and tool calls are provisional. |
| `Completed` | Enumeration finished normally and a provider completion signal was observed. |
| `Failed` | HTTP, transport, provider, protocol, timeout, or cleanup failure. See `StreamError`. |
| `Cancelled` | The request or enumeration token was cancelled by the caller. |
| `Abandoned` | The consumer disposed an active enumeration before it finished, including after receiving a terminal chunk. |

The response is a single-consumer, single-enumeration object and is not thread safe. Enumerating a consumed successful stream again throws `InvalidOperationException`. A never-enumerated response remains `Open`: no final result exists, and its HTTP response remains owned by the pending enumeration. Always begin and consume the stream, or begin it with a cancelled token and handle cancellation to release its resources. Disposing an enumerator before its first `MoveNextAsync` does not execute an async iterator's cleanup.

`Success` retains its existing opening semantics. Some provider readers already update it for later failures; do not rely on its final value. Use `State == StreamingState.Completed` as the common completion check. Failures populate `StreamError` and `Error`, including failures after partial output. HTTP failures start in `Failed` and retain the HTTP status and diagnostic.

Caller cancellation records `Cancelled` and rethrows `OperationCanceledException`. A request timeout records `Failed`, populates the diagnostic, and preserves the existing `OperationCanceledException` behavior. Other enumeration-time transport and protocol failures become `Failed` rather than escaping enumeration. Resource cleanup runs before enumeration returns or propagates cancellation. Injected `HttpClient` instances remain caller-owned.

## Accepting a tool turn

```csharp
ToolChatStreamingResponse response = await client.ToolChatStreamingAsync(request, token);
await foreach (ToolChatStreamingChunk chunk in response.Chunks.WithCancellation(token))
{
    // Display partial text or thinking here. Do not execute deltas.
}

if (response.State != StreamingState.Completed)
{
    Console.WriteLine(response.StreamError ?? $"Stream ended as {response.State}.");
    return;
}

// Validate the finish reason, advertised tool names, argument schemas, and permissions.
// Only then execute accepted calls and append their results.
request.Messages.Add(response.ToAssistantMessage());
```

Partial text, tool calls, and `ToAssistantMessage()` remain accessible for compatibility and diagnostics. The accessor does not gate execution. A completed stream can still have a token-limit finish reason, invalid tool arguments, or an answer the application should reject. Completion establishes stream integrity, not task success or permission to execute a tool. No automatic inference retries are introduced.

The same state check applies to chat and generation. For native xAI, enumerate `Events`; `response.completed` or an accepted `response.incomplete` terminal event can finish the stream, while provider failure/cancellation is a failure. Inspect the native response status and finish reason separately.

## Protocol validation

Provider readers translate their completion signals into the shared contract: Ollama's boolean `done`, OpenAI-compatible completion chunks/sentinel, Gemini/Vertex finish reasons, Anthropic/Cohere terminal events, Bedrock message-stop events, and xAI native response events. Ordinary EOF without a completion signal fails.

JSON streaming readers reject malformed and non-object records and recognize top-level native errors before mapping output. Bedrock exception frames fail the stream. This is targeted stream-integrity validation, not exhaustive validation of every provider field or future event shape.

For Ollama and Ollama Cloud specifically:

- Every nonblank NDJSON record must be a JSON object with a boolean `done`, unless it is a native error record.
- Invalid UTF-8, malformed records, provider errors, and EOF without completion fail the turn. A later terminal record cannot erase an earlier failure.
- The first valid `done:true` record ends the native reader. Later records are ignored and cannot append content or tools.
- Empty bodies fail. Terminal-only records and final records without a trailing newline are valid.
- Native error diagnostics redact the configured API key, normalize line breaks, and retain at most 1,024 characters of provider text. Raw malformed records are not included. Treat provider diagnostics as potentially sensitive application data.
- Successful tool aggregation, native thinking continuation, finish reason, and token usage remain available. Generation now also retains final Ollama usage and finish reason.

Telemetry records successful streaming completion only when the final state is `Completed`. Caller cancellation, request timeout, and deliberate abandonment retain their existing distinct outcomes. It does not emit provider diagnostic text as metric labels.

## Verification

The shared local suite includes 300 Ollama/Ollama Cloud fault cases: 25 scenarios across both client types, all three streaming operations, and telemetry capture on/off. Another 27 descriptors check all nine generic completion client variants for completion, early disposal, request/enumeration cancellation before and during enumeration, and repeat-enumeration rejection. Native xAI event coverage checks the shared lifecycle, and console regressions reject provisional calls before execution.

Run deterministic checks with the existing shared Touchstone runners:

```sh
dotnet run --project src/Test.Automated -c Release -f net8.0 -- selftest
dotnet run --project src/Test.Automated -c Release -f net10.0 -- selftest
dotnet test src/Test.Nunit -c Release
dotnet test src/Test.Xunit -c Release
bash src/Test.Aot/verify-aot.sh osx-arm64 net8.0 net10.0
```

The saved `postman/simulations/ollamacloud.sim.yaml` includes `x-stream-fault` fixtures for missing completion, native error followed by completion, and corruption followed by completion. They use synthetic data and no production credentials.

The opt-in live Cloud suite reads credentials from the environment, never command-line arguments. Set `POLYPROMPT_TEST_OLLAMACLOUD_API_KEY` securely in the process environment; optionally set `POLYPROMPT_TEST_OLLAMACLOUD_MODEL` (default `gpt-oss:20b`). Then run:

```sh
dotnet run --project src/Test.Automated -c Release -f net8.0 -- ollamacloud-streaming
dotnet run --project src/Test.Automated -c Release -f net10.0 -- ollamacloud-streaming
```

The live suite checks chat, generation, a tool name and integer argument, a synthetic tool result, and the subsequent native continuation. It does not perform filesystem or external tool actions. Passing these checks establishes compatibility with the selected hosted model, not every Cloud model or historical Armada mission.
