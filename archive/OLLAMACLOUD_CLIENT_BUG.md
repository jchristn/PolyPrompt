# Ollama / Ollama Cloud streaming integrity and error-reporting defects

Investigation date: 2026-10-20 America/Los_Angeles (2026-10-21 UTC). Status: reproduced; no SDK fix applied by this investigation.

## Executive finding

**Both `OllamaCompletionClient` and `OllamaCloudCompletionClient` in PolyPrompt 3.4.1 silently accept incomplete or corrupt native NDJSON streams and discard native in-stream provider errors.** The problem affects `ChatStreamingAsync`, `ToolChatStreamingAsync`, and `GenerateStreamingAsync`. Cloud inherits the regular Ollama implementation; this is not isolated to the Cloud wrapper.

After fully consuming affected streams, callers can receive partial text or tool calls with `Success == true`, `Error == null`, and no exception. Some invalid streams even contain a later `Done == true`, so checking for a terminal chunk alone does not solve the problem. Operation telemetry marks these streams successful.

Severity assessment: **high for agent/tool automation**, because callers can mistake an interrupted or provider-rejected turn for completed work. This investigation demonstrated retained, serializable tool calls; it did **not** execute those tools or prove that any hosted mission executed a partial call. For display-only text applications the effect is usually silent truncation or a blank response.

There is an important public-contract distinction: `ResponseBase.Success` explicitly means the stream **opened** successfully for streaming responses. Initial `Success == true` is therefore expected. The defect is the absence of a reliable final integrity/error result after enumeration, particularly the swallowed native error and malformed record. This report does not propose treating provisional open success as completed inference.

## Versions, environment, and boundaries

- Published package: PolyPrompt **3.4.1**, assembly `PolyPrompt, Version=3.4.1.0`.
- Source reviewed and independently exercised: commit `1aa1b958e8ce128057a3fce53c6074fef9470513`, source version 3.4.1.
- SDK: .NET 10.0.401. Executed runtimes: .NET 8.0.31 and .NET 10.0.12 on the investigator's macOS machine.
- Postman CLI: 1.75.0. Real HTTP traffic ran through a local Postman simulation; deliberately broken Content-Length responses used a separate raw Python HTTP server.
- Four matrix executions: NuGet/net10, NuGet/net8, checkout/net10, and NuGet/net10 with an ActivityListener. Each executed 132 streaming cases and 12 nonstreaming controls: **576 total client invocations**. This is repeated evidence over 144 distinct combinations, not 576 distinct scenarios.
- Every case issued exactly one HTTP request. No inference retries were introduced by the harness. No production key, model, or external provider was used.
- Regular Ollama used a normal loopback endpoint without a bearer token. Cloud used its real fixed URL and a synthetic token; an injected delegating handler recorded the original request, then redirected transport to loopback. It rejected every other external hostname.
- This proves client handling of controlled native protocol responses. It does not measure Cloud availability, authenticate against Cloud, assert that every hosted model supports generation/tools, or establish how frequently real Ollama servers emit these failures.
- Nonstreaming tests here cover only valid success and HTTP 500 controls for all three operations and both providers. They are not a general nonstreaming correctness certification. Embeddings, model management/pull progress, and unrelated providers were not exercised.

## Protocol expectations

Ollama documents native streaming as newline-delimited JSON, with a terminal record containing `done: true`. A terminal record without a trailing newline is still readable JSON and passed the control test. A valid terminal-only response with no content also passed; an empty HTTP body is a different case. See [Ollama streaming](https://docs.ollama.com/api/streaming), [chat](https://docs.ollama.com/api/chat), and [generate](https://docs.ollama.com/api/generate).

Ollama also explicitly documents that streaming failures can arrive as an `error` object after streaming starts, without changing the already-sent HTTP status. A 200 status therefore cannot establish inference success. See [Ollama error handling](https://docs.ollama.com/api/errors).

Expected library behavior: expose provider errors and malformed/incomplete completion distinctly from a valid finished turn; preserve partial output only with an unmistakably incomplete/error state; retain correct caller cancellation and resource cleanup. The exact API design may preserve open-success semantics and introduce a separate final status.

## Reproduction results

The following outcomes reproduced for **both clients and all three streaming methods**. Published net8 and source net10 results exactly matched published net10 on provider, operation, scenario, final Success/Error, chunk count, Done, text, tool counts, exception type, HTTP status, and request count. A separate telemetry-enabled run confirmed the telemetry column.

In the table, Done means at least one emitted chunk had `Done == true`. Unless specified, HTTP status was 200 and final `Error` was null. Tools counts refer only to ToolChat; plain chat/generation do not expose tool calls.

| Scenario | Chunks | Done | Final Success | Exception | Tool calls / assistant-message calls | Operation telemetry |
|---|---:|:---:|:---:|---|---:|---|
| `complete` | 2 | true | true | none | 1 / 1 | success / Ok |
| `complete-no-newline` | 2 | true | true | none | 1 / 1 | success / Ok |
| `terminal-only` | 1 | true | true | none | 0 / 0 | success / Ok |
| `missing-done` | 1 | false | true | none | 1 / 1 | success / Ok |
| `empty` | 0 | false | true | none | 0 / 0 | success / Ok |
| `whitespace` | 0 | false | true | none | 0 / 0 | success / Ok |
| `truncated-first` | 0 | false | true | none | 0 / 0 | success / Ok |
| `partial-truncated` | 1 | false | true | none | 1 / 1 | success / Ok |
| `malformed-then-done` | 2 | true | true | none | 1 / 1 | success / Ok |
| `error-first` | 1 | false | true | none | 0 / 0 | success / Ok |
| `error-after-partial` | 2 | false | true | none | 1 / 1 | success / Ok |
| `error-then-done` | 3 | true | true | none | 1 / 1 | success / Ok |
| `nonobject` | 0 | false | true | none | 0 / 0 | success / Ok |
| `missing-done-property` | 1 | false | true | none | 1 / 1 | success / Ok |
| `done-string-true` | 1 | true | true | none | 1 / 1 | success / Ok |
| `after-terminal` | 3 | true | true | none | 1 / 1 | success / Ok |
| `duplicate-terminal` | 3 | true | true | none | 1 / 1 | success / Ok |
| `http-401` | 0 | false | false | none | 0 / 0 | error / Error |
| `http-500` | 0 | false | false | none | 0 / 0 | error / Error |
| `cancel-after-partial` | 1 | false | true | TaskCanceledException | 1 / 1 | cancelled / Unset |
| `transport-cut` | 1 | false | true | HttpIOException | 1 / 1 | error / Error |
| `consumer-abandons` | 1 | false | true | none | 1 / 1 | abandoned / Unset |

### Confirmed core defects

1. **EOF without terminal completion is accepted.** `missing-done`, empty body, whitespace-only body, and a content record without a `done` property finish enumeration without a failure indication. Partial text is `hello` where present. Missing-terminal tool responses retain one call, including in `ToAssistantMessage()`.
2. **Malformed NDJSON is silently dropped.** A truncated first JSON record produces zero chunks and no error. A partial record followed by malformed JSON produces partial output and no error. A malformed intermediate record followed by a valid terminal record still looks completed: Done true, no error. This last case defeats a caller-side Done-only check.
3. **Native provider errors are discarded.** `{"error":"synthetic provider failure"}` under HTTP 200 becomes an empty chunk instead of an error. This happens before any output and after partial output. Even an error followed by a terminal chunk produces Done true and success telemetry; the original diagnostic is lost.
4. **Observability reports invalid completion as success.** All ordinary-EOF cases above produce an operation span with `polyprompt.outcome=success` and Activity status Ok. This was observed with a real ActivityListener, not inferred solely from source. Transport exceptions remain error spans; cancellation and deliberate abandonment remain distinct.

### Related behavior and contract inconsistencies

- **Tool calls remain externally usable on incomplete turns.** The tool fixture puts a complete syntactic `probe_tool` call in the partial chunk and no call in the terminal chunk. For missing-terminal, malformed-tail, error-after-partial, cancellation, transport-failure, and deliberate-abandonment cases, both `ToolCalls.Count` and `ToAssistantMessage().ToolCalls.Count` remain one. This proves there is no final-completion gate on that accessor; it does not prove malformed tool arguments or actual tool execution. Streaming deltas may legitimately be exposed early, but consumers need a separate trustworthy execution eligibility state.
- **Post-terminal records are consumed.** A record after `done:true` appends `UNEXPECTED_AFTER_DONE`, giving `helloUNEXPECTED_AFTER_DONE`. A second terminal is also emitted. Define and enforce the terminal-boundary policy; this is additional protocol hardening rather than the central native-error defect.
- **Non-object JSON is ignored.** An array, null, and a JSON string produce no chunks or error. String `"true"` is accepted as a terminal value. Strict shape/boolean validation should be considered; permissive shape handling is separate from the documented native-error requirement.
- **Abrupt transport failure is surfaced, but only as an exception.** A short Content-Length response emits partial output then throws `System.Net.Http.HttpIOException`. Response Success remains true and Error remains null. This is not the silent-EOF case: a caller that awaits enumeration and catches exceptions can detect it. However, `ResponseBase`'s class summary promises transport/malformed-response errors through Success/Error and reserves exceptions for invalid arguments/cancellation. Align either the implementation or the documented streaming exception contract.
- **Cancellation is surfaced correctly.** A token cancelled after a partial record produces TaskCanceledException and cancelled telemetry. Success remains open-success. Do not turn caller cancellation into an ordinary provider error or call it silently swallowed.
- **Consumer abandonment is deliberate.** Breaking after the first chunk produces abandoned telemetry, no exception, no Done. This is a control for caller behavior, not proof of a provider failure. Partial calls remain present, so callers must not execute them merely because the stream opened.
- **HTTP failures are handled correctly in these controls.** 401 and 500 return Success false, no chunks, and Error including the HTTP status and synthetic body. All twelve nonstreaming controls also behaved as expected: valid 200 succeeds, HTTP 500 fails.

## Source diagnosis

References below are anchored to the reviewed commit; line numbers can change after fixes.

| Location | Mechanism / relevance |
|---|---|
| `src/PolyPrompt/Clients/OllamaCloud/OllamaCloudCompletionClient.cs:9` | Sealed Cloud wrapper derives from `OllamaCompletionClient`, adds fixed endpoint/token requirements, and overrides none of the streaming readers. The regular Ollama fix should cover both. |
| `src/PolyPrompt/Clients/Ollama/OllamaCompletionClient.cs:79–118` | All three streaming entry points use ExecuteStreamingAsync and the corresponding inherited native reader. |
| Same file, `ReadLines`, line 407 | Reads lines until EOF, skips whitespace, calls TryDeserializeObject, and silently skips null parse results. There is no malformed-line error, native-error detection, or terminal-completion validation. |
| Same file, `ReadChatChunks` line 423, `ReadToolChatChunks` line 450, `ReadGenerateChunks` line 523 | Maps data and Done but never checks native `error`, requires a terminal record, or terminates on Done. An error object therefore becomes an empty normal chunk. |
| `src/PolyPrompt/Clients/Base/ClientBase.cs:370`, ExecuteStreamingAsync | Marks Success true after successful headers and attaches lazy enumeration. Its setup catch cannot handle failures raised later by MoveNextAsync. Open success itself agrees with the property documentation. |
| Same file, `TryDeserializeObject` line 991 | Forgiving JSON parsing returns null for malformed or non-object input. Appropriate for optional probing in some contexts, but silently losing native protocol records is unsafe here. |
| Same file, `IsTruthy` line 913 | Accepts non-boolean truthy representations; explains the string-true result. |
| `src/PolyPrompt/Clients/Base/CompletionClientBase.cs:269`, `:342`, `:445` | Timing/aggregation wrappers do not validate terminal completion. Tool aggregation refreshes usable calls before the terminal event. |
| `src/PolyPrompt/Models/ToolChatStreamingResponse.cs:83` | ToAssistantMessage copies aggregate calls/state without a final-completion check. Tool execution remains the caller's responsibility. |
| `src/PolyPrompt/Telemetry/OperationScope.cs:216`, `:291` | Ordinary EOF invokes Complete(response), which selects success from response.Success. It cannot distinguish malformed/native-error/missing-terminal bodies because lower layers never report those conditions. |
| `src/PolyPrompt/Models/ResponseBase.cs:3–19` | Class-level no-transport-exception statement conflicts with observed enumeration-time transport exception; Success property separately documents open-success semantics. |

Existing `src/Test.Shared/LocalToolTurnCases.cs` fixtures cover useful normal native tool turns, including terminal events (around lines 210 and 282). Those tests do not substitute for the fault matrix here. This is a targeted coverage observation, not a claim that every existing test was audited or the entire test suite was run.

## Recommended fix

### Required behavior

1. Put native NDJSON validation in a shared Ollama stream layer used by chat, tool chat, and generation. Handle top-level `error` before mapping payload chunks, preserve a sanitized provider message, and stop the turn as failed.
2. Treat malformed nonblank JSON and invalid required record shapes as protocol failures rather than silently skipping them. Do not permit a later valid terminal record to erase an earlier failure.
3. Track whether a valid terminal event was reached. Ordinary EOF without one must become an explicit incomplete-stream result, including zero-record bodies. Preserve valid terminal-only completion and a final record with no newline.
4. Define a clear final response state: for example Open/Streaming/Completed/Failed/Cancelled/Abandoned, or equivalent fields. Preserve the documented initial open-success meaning if compatibility requires it. Expose a trustworthy final completion/failure result and use it in telemetry. If mutating Success/Error after enumeration is chosen, document exactly when fields become final and retain distinct cancellation semantics.
5. Treat accumulated tool calls as provisional until the turn is validly completed. Expose an explicit completion gate or reject conversion for failed/incomplete turns. Keep partial content available for diagnostics/display without presenting it as executable continuation. Decide how this affects existing users before changing ToAssistantMessage behavior.
6. Align enumeration-time network failures with the public error contract. Either translate them to the documented final state/Error, or explicitly document the exceptions callers must handle; telemetry and completion eligibility must agree either way.
7. Ensure all error, cancellation, early-break, and successful paths dispose the response/stream and end telemetry exactly once. Maintain resource ownership rules for injected HttpClient.

### Additional hardening

- Decide whether to stop immediately at the first terminal record or reject trailing protocol data. Do not append extra content after completion.
- Validate `done` as a protocol boolean if strictness is intended, with a documented compatibility policy.
- Bound record/body diagnostic sizes and redact secrets. Preserve useful provider text without dumping tokens, prompts, or credentials into Error/telemetry.
- Keep usage/finish reason, thinking/provider continuation, and valid tools intact for successful streams, including empty final content. A terminal event must not discard earlier assembled tool calls.
- Do not automatically replay a partially emitted tool turn as a hidden retry. Any retry policy needs explicit caller control and duplicate-side-effect protection.

### Suggested regression acceptance matrix

Run the 22 vectors below against both actual client types and all three stream methods on both target frameworks. Assert final state, Error, exception policy, chunk contents, tool eligibility, usage/finish metadata, disposal, and telemetry—not just initial Success. Include native errors before/after content and before a later terminal, corruption before/after content and before a later terminal, empty/whitespace EOF, non-object records, missing terminal, valid terminal-only/no-newline, trailing/duplicate terminal, HTTP failures, token cancellation, broken framing, and caller early break.

Add follow-up cases not exercised here: malformed tool arguments and delta assembly across records; invalid UTF-8; multiple network reads per JSON line and many lines per read; large/bounded records; error diagnostic redaction; failure before headers; cancellation before request; consumer token versus request token; successful finish reasons including length; optional native thinking/state; caller-injected HttpClient ownership; Activity/Meter on and off; disposal exactly once. These are proposed tests, not additional reproduced defects.

## Armada impact and attribution

This is a PolyPrompt native Ollama reader issue, inherited by Ollama Cloud, rather than evidence of a DeepSeek reasoning/capability defect. Armada must also avoid using open-success as completed inference and should surface provider/protocol failures with remediation in mission details. That caller responsibility does not recover an error object the SDK has already discarded.

The most recent Wilson acceptance run's terminal failure was separately a judged application compatibility defect: an explicitly configured legacy Ollama runner chat path was rejected during configuration validation. Do not attribute that Judge rejection, all earlier reworks, or every Worker/Judge delay to this SDK defect. The controlled tests in this report establish the client failure mechanism; they do not quantify its contribution to historical hosted failures or establish why any particular Cloud response ended early.

## Temporary consumer mitigation

Await the entire enumerable, catch cancellation and transport exceptions separately, track a valid terminal chunk, and never execute/serialize provisional tools as an accepted turn merely because Success is true. Show incomplete-stream/provider diagnostics rather than reporting successful completion. Do not blindly retry a possibly side-effecting turn.

**A Done check is necessary but insufficient with 3.4.1:** malformed-middle and native-error-then-terminal fixtures still have Done true. A complete workaround needs a strict native NDJSON adapter (or raw-protocol validation before the lossy SDK reader), or an upstream fix. No caller-only check of the current public response recovers discarded native error details.

## Reproduction package and commands

The following files form a self-contained harness. Save each fenced block at the named relative path in a temporary directory. No real key is required. The dummy tool is never executed. The redirect handler prevents external provider calls. Ports 4957, 4958, and the mock's internal 4500 must be available.

Start the two fixtures in separate terminals, then run the client matrix:

```sh
postman simulation run ./simulation.sim.yaml --port 4957 --output ndjson > simulation.ndjson 2> simulation.err
```

```sh
python3 ./raw_fixture.py > raw-fixture.log 2>&1
```

```sh
dotnet run --project ./Probe.csproj -f net10.0 -- http://127.0.0.1:4957/ollama nuget-net10.json
dotnet run --project ./Probe.csproj -f net8.0 -- http://127.0.0.1:4957/ollama nuget-net8.json
PROBE_TELEMETRY=1 dotnet run --project ./Probe.csproj -f net10.0 -- http://127.0.0.1:4957/ollama nuget-net10-telemetry.json
# Optional source confirmation; adjust the path to your checkout:
dotnet run --project ./Probe.csproj -f net10.0 -p:ProbeSource=/Users/joelchristner/Code/Polyprompt/src/PolyPrompt/PolyPrompt.csproj -- http://127.0.0.1:4957/ollama source-net10.json
```

Stop both fixture processes when finished. Installing Postman/dotnet or restoring dependencies may require network access, but the inference requests are all loopback. The per-case token is a bounded harness guard (250 ms for cancellation, 2 seconds otherwise), not a demonstration of SDK timeout enforcement. HttpClient.Timeout is not used as proof of a stream idle timeout. Broken framing is tested directly on port 4958 because an HTTP proxy can normalize framing or keep an upstream failure open.

### `Probe.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFrameworks>net8.0;net10.0</TargetFrameworks><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup Condition="'$(ProbeSource)' == ''"><PackageReference Include="PolyPrompt" Version="3.4.1" /></ItemGroup>
  <ItemGroup Condition="'$(ProbeSource)' != ''"><ProjectReference Include="$(ProbeSource)" /></ItemGroup>
</Project>
```

### `Program.cs`

```csharp
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using PolyPrompt.Clients;
using PolyPrompt.Models;
using PolyPrompt.Options;

List<object> telemetry = new();
string currentCase = "";
using ActivityListener listener = new() {
 ShouldListenTo = source => Environment.GetEnvironmentVariable("PROBE_TELEMETRY") == "1" && source.Name == "PolyPrompt",
 Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
 ActivityStopped = activity => telemetry.Add(new { Case=currentCase, Name=activity.OperationName, Status=activity.Status.ToString(), Tags=activity.TagObjects.ToDictionary(t=>t.Key,t=>t.Value) })
};
ActivitySource.AddActivityListener(listener);
string baseUrl = args.Length > 0 ? args[0] : "http://127.0.0.1:4957/ollama";
string output = args.Length > 1 ? args[1] : "results.json";
string[] scenarios = { "complete", "complete-no-newline", "terminal-only", "missing-done", "empty", "whitespace", "truncated-first", "partial-truncated", "malformed-then-done", "error-first", "error-after-partial", "error-then-done", "nonobject", "missing-done-property", "done-string-true", "after-terminal", "duplicate-terminal", "http-401", "http-500", "cancel-after-partial", "transport-cut", "consumer-abandons" };
List<ProbeResult> rows = new();
foreach (string provider in new[] { "Ollama", "OllamaCloud" })
foreach (string operation in new[] { "chat", "tools", "generate" })
foreach (string scenario in scenarios)
{
    currentCase = $"{provider}/{operation}/{scenario}";
    string caseBase = scenario == "transport-cut" ? "http://127.0.0.1:4958" : baseUrl;
    using RedirectHandler handler = new(caseBase);
    using HttpClient http = new(handler) { Timeout = TimeSpan.FromSeconds(4) };
    http.DefaultRequestHeaders.Add("x-repro-scenario", scenario == "consumer-abandons" ? "complete" : scenario);
    using CompletionClientBase client = provider == "OllamaCloud"
        ? new OllamaCloudCompletionClient("synthetic-cloud-token", null, http)
        : new OllamaCompletionClient(caseBase, null, null, http);
    using CancellationTokenSource cancel = new();
    cancel.CancelAfter(scenario == "cancel-after-partial" ? 250 : 2000);
    ProbeResult row = new() { Provider = provider, Operation = operation, Scenario = scenario };
    ResponseBase? response = null;
    ToolChatStreamingResponse? toolResponse = null;
    try
    {
        OllamaCompletionOptions options = new() { Model = "fixture-model" };
        if (operation == "chat")
        {
            ChatStreamingResponse stream = await client.ChatStreamingAsync("hello", options, cancel.Token);
            response = stream; row.SuccessBefore = response.Success;
            await foreach (ChatStreamingChunk chunk in stream.Chunks.WithCancellation(cancel.Token))
            { row.Chunks++; row.Done |= chunk.Done; row.Text += chunk.Text; if (scenario == "consumer-abandons") break; }
        }
        else if (operation == "tools")
        {
            toolResponse = await client.ToolChatStreamingAsync(new ToolChatRequest
            {
                Messages = new() { ChatMessage.User("hello") }, Options = options,
                Tools = new() { ToolDefinition.Function("probe_tool", "Synthetic tool; never executed", new() { ["type"] = "object", ["properties"] = new Dictionary<string, object> { ["value"] = new { type = "string" } } }) }
            }, cancel.Token);
            response = toolResponse; row.SuccessBefore = response.Success;
            await foreach (ToolChatStreamingChunk chunk in toolResponse.Chunks.WithCancellation(cancel.Token))
            { row.Chunks++; row.Done |= chunk.Done; row.Text += chunk.Text; row.ToolDeltas += chunk.ToolCallDeltas.Count; if (scenario == "consumer-abandons") break; }
        }
        else
        {
            GenerationStreamingResponse stream = await client.GenerateStreamingAsync("hello", options, cancel.Token);
            response = stream; row.SuccessBefore = response.Success;
            await foreach (GenerationStreamingChunk chunk in stream.Chunks.WithCancellation(cancel.Token))
            { row.Chunks++; row.Done |= chunk.Done; row.Text += chunk.Text; if (scenario == "consumer-abandons") break; }
        }
    }
    catch (Exception ex) { row.Exception = ex.GetType().FullName; row.ExceptionMessage = ex.Message; }
    row.SuccessAfter = response?.Success; row.Error = response?.Error; row.StatusCode = response?.StatusCode;
    if (toolResponse != null) { row.ToolCalls = toolResponse.ToolCalls.Count; row.AssistantToolCalls = toolResponse.ToAssistantMessage().ToolCalls.Count; }
    row.RequestCount = handler.RequestCount; row.OriginalUrl = handler.OriginalUrl; row.TransportUrl = handler.TransportUrl;
    row.BearerPresent = handler.BearerPresent; row.SentModel = handler.SentModel; row.SentStream = handler.SentStream;
    rows.Add(row);
}
// Independent non-streaming controls for all operations/providers.
foreach (string provider in new[] { "Ollama", "OllamaCloud" })
foreach (string operation in new[] { "chat", "tools", "generate" })
foreach (string scenario in new[] { "complete", "http-500" })
{
    currentCase = $"{provider}/{operation}-nonstream/{scenario}";
    using RedirectHandler handler = new(baseUrl);
    using HttpClient http = new(handler) { Timeout = TimeSpan.FromSeconds(4) };
    http.DefaultRequestHeaders.Add("x-repro-scenario", scenario);
    using CompletionClientBase client = provider == "OllamaCloud" ? new OllamaCloudCompletionClient("synthetic-cloud-token", null, http) : new OllamaCompletionClient(baseUrl, null, null, http);
    OllamaCompletionOptions options = new() { Model = "fixture-model" };
    ResponseBase response = operation switch {
        "chat" => await client.ChatAsync("hello", options),
        "generate" => await client.GenerateAsync("hello", options),
        _ => await client.ToolChatAsync(new ToolChatRequest { Messages = new() { ChatMessage.User("hello") }, Options = options })
    };
    rows.Add(new() { Provider=provider, Operation=operation+"-nonstream", Scenario=scenario, SuccessAfter=response.Success, Error=response.Error, StatusCode=response.StatusCode, RequestCount=handler.RequestCount, OriginalUrl=handler.OriginalUrl, TransportUrl=handler.TransportUrl, BearerPresent=handler.BearerPresent, SentModel=handler.SentModel, SentStream=handler.SentStream });
}
File.WriteAllText(output, JsonSerializer.Serialize(new { Framework=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, Assembly=typeof(OllamaCompletionClient).Assembly.FullName, Cases=rows, Telemetry=telemetry }, new JsonSerializerOptions { WriteIndented=true }));
Console.WriteLine($"Saved {rows.Count} cases to {output}");

sealed class RedirectHandler(string endpoint) : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect=false })
{
    public int RequestCount; public string? OriginalUrl, TransportUrl, SentModel; public bool BearerPresent; public bool? SentStream;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        RequestCount++; OriginalUrl=request.RequestUri!.ToString(); BearerPresent=request.Headers.Authorization?.Scheme=="Bearer";
        if (request.Content != null) { using JsonDocument doc=JsonDocument.Parse(await request.Content.ReadAsStringAsync(token)); SentModel=doc.RootElement.GetProperty("model").GetString(); SentStream=doc.RootElement.GetProperty("stream").GetBoolean(); }
        if (request.RequestUri.Host=="ollama.com") request.RequestUri=new Uri(endpoint.TrimEnd('/')+request.RequestUri.AbsolutePath);
        if (request.RequestUri.Host is not ("127.0.0.1" or "localhost")) throw new InvalidOperationException("Reproduction forbids external network calls");
        TransportUrl=request.RequestUri.ToString(); return await base.SendAsync(request,token);
    }
}
sealed class ProbeResult
{
    public string Provider {get;set;}=""; public string Operation {get;set;}=""; public string Scenario {get;set;}="";
    public bool? SuccessBefore {get;set;} public bool? SuccessAfter {get;set;} public string? Error {get;set;} public int? StatusCode {get;set;}
    public int Chunks {get;set;} public bool Done {get;set;} public string Text {get;set;}=""; public int ToolDeltas {get;set;} public int ToolCalls {get;set;} public int AssistantToolCalls {get;set;}
    public string? Exception {get;set;} public string? ExceptionMessage {get;set;} public int RequestCount {get;set;}
    public string? OriginalUrl {get;set;} public string? TransportUrl {get;set;} public bool BearerPresent {get;set;} public string? SentModel {get;set;} public bool? SentStream {get;set;}
}
```

### `simulation.sim.yaml`

```yaml
simulation: ollama-stream-repro
routing: path
mocks:
  - routeKey: ollama
    path: ./mock/config.yaml
```

### `mock/config.yaml`

```yaml
version: 1
name: Ollama stream fault reproduction
slug: ollama-stream-repro
interceptRequests: true
protocol: http
port: 4500
scenarios:
  - name: default
    path: ./default.js
    default: true
```

### `mock/default.js`

```javascript
const http = require('http');
const server = http.createServer((req, res) => {
  const path = new URL(req.url, 'http://localhost').pathname;
  if (req.method !== 'POST' || !['/api/chat','/api/generate'].includes(path)) {
    res.writeHead(501); return res.end('outside reproduction contract');
  }
  let data = '';
  req.on('data', c => data += c);
  req.on('end', () => {
    let request;
    try { request = JSON.parse(data); } catch { res.writeHead(400); return res.end(); }
    const scenario = req.headers['x-repro-scenario'] || 'complete';
    const tool = !!request.tools?.length;
    const gen = path === '/api/generate';
    function record(done, content='hello') {
      return JSON.stringify({model:request.model,
        ...(gen ? {response:content} : {message:{role:'assistant',content,
          ...(tool && done !== true ? {tool_calls:[{function:{name:'probe_tool',arguments:{value:'captured'}}}]} : {})}}),
        done, ...(done === true ? {done_reason:'stop',eval_count:1,prompt_eval_count:1} : {})});
    }
    const part = record(false), done = record(true, '');
    const malformed = gen ? '{"response":"lost","done":fa' : '{"message":{"content":"lost"},"done":fa';
    const error = JSON.stringify({error:'synthetic provider failure'});
    const fixtures = {
      complete:part+'\n'+done+'\n', 'complete-no-newline':part+'\n'+done,
      'terminal-only':done+'\n', 'missing-done':part+'\n', empty:'', whitespace:' \n\r\n',
      'truncated-first':malformed, 'partial-truncated':part+'\n'+malformed,
      'malformed-then-done':part+'\n'+malformed+'\n'+done+'\n',
      'error-first':error+'\n', 'error-after-partial':part+'\n'+error+'\n',
      'error-then-done':part+'\n'+error+'\n'+done+'\n',
      'nonobject': '[]\nnull\n"wrong"\n',
      'missing-done-property':record(false).replace(',"done":false','')+'\n',
      'done-string-true':record('true')+'\n',
      'after-terminal':part+'\n'+done+'\n'+record(false,'UNEXPECTED_AFTER_DONE')+'\n',
      'duplicate-terminal':part+'\n'+done+'\n'+done+'\n'
    };
    if (scenario === 'http-401' || scenario === 'http-500') {
      res.writeHead(Number(scenario.slice(5)),{'Content-Type':'application/json'}); return res.end(error);
    }
    if (!request.stream) {
      res.writeHead(200,{'Content-Type':'application/json'}); return res.end(record(true,'nonstream-control'));
    }
    if (scenario === 'cancel-after-partial' || scenario === 'transport-cut') {
      res.writeHead(200,{'Content-Type':'application/x-ndjson',
        ...(scenario==='transport-cut'?{'Content-Length': Buffer.byteLength(part)+4096}:{})});
      res.write(part+'\n');
      if (scenario==='transport-cut') setTimeout(()=>res.destroy(),100);
      return;
    }
    if (!(scenario in fixtures)) {res.writeHead(501); return res.end('unknown scenario');}
    res.writeHead(200,{'Content-Type':'application/x-ndjson'});
    res.end(fixtures[scenario]);
  });
});
server.listen(process.env.PORT || 4500);
```

### `raw_fixture.py`

```python
import http.server,json
class H(http.server.BaseHTTPRequestHandler):
 def do_POST(self):
  data=json.loads(self.rfile.read(int(self.headers.get('Content-Length','0'))))
  obj={'model':data['model'],'done':False}
  if self.path.endswith('/generate'):obj['response']='hello'
  else:obj['message']={'role':'assistant','content':'hello',**({'tool_calls':[{'function':{'name':'probe_tool','arguments':{'value':'captured'}}}]} if data.get('tools') else {})}
  body=(json.dumps(obj)+'\n').encode();self.send_response(200);self.send_header('Content-Type','application/x-ndjson');self.send_header('Content-Length',str(len(body)+4096));self.end_headers();self.wfile.write(body);self.wfile.flush();self.close_connection=True
 def log_message(self,fmt,*args):print(fmt%args,flush=True)
http.server.ThreadingHTTPServer(('127.0.0.1',4958),H).serve_forever()
```

## Evidence inventory and integrity

Original local artifacts: `/tmp/polyprompt-ollama-stream-report-20261021`. This temporary directory is supplemental; the runnable sources and representative exact results are preserved in this report. Result files contain only synthetic request data. No production tokens are included.

| Artifact | SHA-256 |
|---|---|
| `Probe.csproj` | `d606a0fe6bff185277e3fe60f2756a82482e3b20445d2e79ce9db629975e91b8` |
| `Program.cs` | `ef9be9e2ce3b9b66e0400374c2a227802eab63b411e5ceb185f1671b2cb6d62a` |
| `mock/default.js` | `852895d6a3758d926127953ac4f77f5176dde07b2f0c9d672be1350a4f1962b1` |
| `raw_fixture.py` | `bf02a14ac6671d934d4deaa9219be4671757592b7871491049e00f87cdf228cd` |
| `nuget-net10.json` | `7dd9354a4b928fa2ac9ea7311d0a5a22d71cbdddd42cd1d6e3e21375d3f89abc` |
| `nuget-net8.json` | `61809a4351d1e285a9c38c2590c8e7032e82a764f373dc2b08e810e764651be0` |
| `source-net10.json` | `7dd9354a4b928fa2ac9ea7311d0a5a22d71cbdddd42cd1d6e3e21375d3f89abc` |
| `nuget-net10-telemetry.json` | `e05e0555361542a1704bb9697b45046731060923a237db8425c369100cb77753` |

### Exact representative tool-response records

```json
[
  {
    "Provider": "OllamaCloud",
    "Operation": "tools",
    "Scenario": "missing-done",
    "SuccessBefore": true,
    "SuccessAfter": true,
    "Error": null,
    "StatusCode": 200,
    "Chunks": 1,
    "Done": false,
    "Text": "hello",
    "ToolDeltas": 1,
    "ToolCalls": 1,
    "AssistantToolCalls": 1,
    "Exception": null,
    "ExceptionMessage": null,
    "RequestCount": 1,
    "OriginalUrl": "https://ollama.com/api/chat",
    "TransportUrl": "http://127.0.0.1:4957/ollama/api/chat",
    "BearerPresent": true,
    "SentModel": "fixture-model",
    "SentStream": true
  },
  {
    "Provider": "OllamaCloud",
    "Operation": "tools",
    "Scenario": "malformed-then-done",
    "SuccessBefore": true,
    "SuccessAfter": true,
    "Error": null,
    "StatusCode": 200,
    "Chunks": 2,
    "Done": true,
    "Text": "hello",
    "ToolDeltas": 1,
    "ToolCalls": 1,
    "AssistantToolCalls": 1,
    "Exception": null,
    "ExceptionMessage": null,
    "RequestCount": 1,
    "OriginalUrl": "https://ollama.com/api/chat",
    "TransportUrl": "http://127.0.0.1:4957/ollama/api/chat",
    "BearerPresent": true,
    "SentModel": "fixture-model",
    "SentStream": true
  },
  {
    "Provider": "OllamaCloud",
    "Operation": "tools",
    "Scenario": "error-then-done",
    "SuccessBefore": true,
    "SuccessAfter": true,
    "Error": null,
    "StatusCode": 200,
    "Chunks": 3,
    "Done": true,
    "Text": "hello",
    "ToolDeltas": 1,
    "ToolCalls": 1,
    "AssistantToolCalls": 1,
    "Exception": null,
    "ExceptionMessage": null,
    "RequestCount": 1,
    "OriginalUrl": "https://ollama.com/api/chat",
    "TransportUrl": "http://127.0.0.1:4957/ollama/api/chat",
    "BearerPresent": true,
    "SentModel": "fixture-model",
    "SentStream": true
  },
  {
    "Provider": "OllamaCloud",
    "Operation": "tools",
    "Scenario": "transport-cut",
    "SuccessBefore": true,
    "SuccessAfter": true,
    "Error": null,
    "StatusCode": 200,
    "Chunks": 1,
    "Done": false,
    "Text": "hello",
    "ToolDeltas": 1,
    "ToolCalls": 1,
    "AssistantToolCalls": 1,
    "Exception": "System.Net.Http.HttpIOException",
    "ExceptionMessage": "The response ended prematurely, with at least 4096 additional bytes expected. (ResponseEnded)",
    "RequestCount": 1,
    "OriginalUrl": "https://ollama.com/api/chat",
    "TransportUrl": "http://127.0.0.1:4958/api/chat",
    "BearerPresent": true,
    "SentModel": "fixture-model",
    "SentStream": true
  }
]
```

### Verification limits

No production inference was requested, no hosted token was exercised, and no source fix or permanent test was added in this report-only change. Only the controlled matrix and source inspection described above establish these findings. A repaired package still needs the regression matrix and an Armada end-to-end acceptance run before claiming the production problem resolved.
