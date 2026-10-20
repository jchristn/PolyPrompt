# Ollama tool follow-ups discard assistant thinking

Reported by Armada integration, 2026-10-19 (America/Los_Angeles).

## Affected version and scope

Reproduced with the published **PolyPrompt 3.4.0** NuGet package, .NET 10, non-streaming `OllamaCompletionClient.ToolChatAsync`. Armada uses Ollama Cloud with DeepSeek (`deepseek-v4.1-flash`); Ollama Cloud inherits the native Ollama chat protocol. The standalone reproduction uses a local Ollama HTTP fixture, without Armada, a real provider, or credentials. Streaming and other providers were not verified by this reproduction.

## Expected behavior

The assistant message returned from an Ollama tool request includes `thinking`, `content`, and `tool_calls`. After `response.ToAssistantMessage()` and tool results are appended, the next native `/api/chat` request should retain the thinking associated with that assistant tool turn.

Ollama's documented tool loop appends the complete response message; its streaming guidance explicitly preserves thinking, content, and tool calls together:
https://docs.ollama.com/capabilities/tool-calling

Do not indiscriminately copy reasoning into user-visible content or into other providers' incompatible request formats. Preserve the provider-specific state for the appropriate assistant turn.

## Actual behavior

The first response has `Reasoning == "fixture reasoning retained"` and one tool call. The next request retains the assistant role/content/tool call, but omits `thinking`. `ToAssistantMessage()` preserves `ProviderState`, but the Ollama implementation does not populate/replay this state for thinking. `ChatMessage` has no reasoning field, and Ollama `BuildMessages` serializes only role, content, and tool calls.

The source documentation on `ToolChatResponse.Reasoning` explicitly says it is not carried into a follow-up request. That behavior conflicts with the native Ollama tool-loop contract for thinking models. Armada uses the documented helper correctly; the standalone SDK reproduction exhibits the same loss.

## Minimal reproduction

Create a .NET 10 console project referencing PolyPrompt 3.4.0. Run a local HTTP fixture on port 4921. On the first `POST /api/chat`, return HTTP 200 with:

```json
{"model":"deepseek-fixture","done":true,"done_reason":"stop","message":{"role":"assistant","content":"","thinking":"fixture reasoning retained","tool_calls":[{"function":{"name":"read_file","arguments":{"path":"test"}}}]}}
```

On a subsequent request, inspect `messages` for the assistant tool message. Require its `thinking` to equal `fixture reasoning retained`; return HTTP 400 when it is absent.

A standalone fixture can be saved as `fixture.cjs` and run with `node fixture.cjs` (no npm dependencies):

```javascript
const http = require('node:http');
http.createServer(async (req, res) => {
  const send = (status, body) => {
    res.writeHead(status, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify(body));
  };
  if (req.method !== 'POST' || req.url !== '/api/chat')
    return send(501, { error: 'Outside this fixture contract' });
  let raw = '';
  for await (const chunk of req) raw += chunk;
  const body = JSON.parse(raw);
  const assistant = (body.messages || []).find(m => m.role === 'assistant');
  if (assistant && assistant.thinking !== 'fixture reasoning retained')
    return send(400, { error: 'assistant thinking missing from tool follow-up' });
  send(200, { model: body.model, done: true, done_reason: 'stop', message:
    assistant ? { role: 'assistant', content: 'done' } : {
      role: 'assistant', content: '', thinking: 'fixture reasoning retained',
      tool_calls: [{ function: { name: 'read_file', arguments: { path: 'test' } } }]
    }
  });
}).listen(4921, '127.0.0.1');
```

The client is:

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

using var client = new OllamaCompletionClient("http://127.0.0.1:4921");
client.Model = "deepseek-fixture";
var request = new ToolChatRequest();
request.Messages.Add(ChatMessage.User("read test"));
request.Tools.Add(ToolDefinition.Function("read_file", "read",
    new Dictionary<string, object> { { "type", "object" } }));
var first = await client.ToolChatAsync(request);
Console.WriteLine($"success={first.Success}; reasoning={first.Reasoning}; calls={first.ToolCalls.Count}");
request.Messages.Add(first.ToAssistantMessage());
foreach (var call in first.ToolCalls)
    request.Messages.Add(ChatMessage.ToolResult(call.Id, call.Name, "file contents"));
var second = await client.ToolChatAsync(request);
Console.WriteLine($"success={second.Success}; error={second.Error}");
```

Use implicit usings or import `System` and `System.Collections.Generic`.

Observed with the real SDK making actual HTTP requests through a local Postman simulation (the fixture was mounted at `/ollama`, so that run used endpoint `http://127.0.0.1:4921/ollama`):

```text
first success=True reasoning=fixture reasoning retained calls=1
second success=False
HTTP 400: {"error":"assistant thinking missing from tool follow-up"}
```

The HTTP 400 is the fixture's assertion failure, not a claim that Ollama Cloud necessarily rejects this omission with 400. The observed defect is the omitted request field.

## Impact and limits of the diagnosis

Armada observed long/repetitive analysis during DeepSeek tool loops. Dropping the previous assistant reasoning may contribute, but this reproduction **does not prove** causation for latency, repeated reasoning, incorrect code, or any particular failed mission. DeepSeek successfully executed tools and produced commits in other observed runs. Separate application defects and server rollout recovery defects were also identified.

## Suggested fix and regression coverage

- Preserve and replay native Ollama assistant thinking through the public `ToAssistantMessage()` tool-loop path. Keep assistant text and tool calls intact.
- Cover local Ollama and Ollama Cloud's shared protocol without requiring cloud secrets.
- Assert the actual outgoing second/third HTTP requests, including multiple/parallel tool results and turns without thinking. Do not test only the response property or constructors.
- Verify JSON persistence / Native AOT state round trips if provider state is used.
- Inspect the streaming implementation separately; accumulate thinking fragments and verify the fully enumerated response can replay the complete assistant turn.
- Keep other providers' state formats isolated, and update the misleading follow-up documentation.

This report requests an upstream fix; no PolyPrompt implementation was modified as part of writing it.
