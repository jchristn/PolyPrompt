<img src="https://github.com/jchristn/PolyPrompt/blob/main/assets/logo.png?raw=true" width="192" height="192">

# PolyPrompt

[![NuGet Version](https://img.shields.io/nuget/v/PolyPrompt.svg?style=flat)](https://www.nuget.org/packages/PolyPrompt/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/PolyPrompt.svg?style=flat)](https://www.nuget.org/packages/PolyPrompt/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE.md)

PolyPrompt is a lightweight, unified .NET library for chat completions, tool calling, text generation, embeddings, and model management across **Ollama**, **OpenAI**, **Google Gemini**, **Anthropic Claude**, and **VoyageAI** APIs. Write your LLM integration code once and swap providers without changing your application logic.

## What It Does

PolyPrompt provides a single, consistent API surface for interacting with multiple LLM providers. Instead of learning five different SDKs with different conventions, response formats, and streaming patterns, you use one set of methods that work identically across all supported providers. Not every provider offers every capability — VoyageAI is embeddings-only, and Anthropic has no embeddings API — so the [Provider Feature Support](#provider-feature-support) matrix is explicit about what each provider can do, and unsupported operations throw a clear `NotSupportedException` rather than faking a protocol.

- **Chat Completions** - Streaming and non-streaming conversational AI with system prompts
- **Tool Calling** - Provider-normalized function declarations, model tool calls, streaming tool-call deltas, and tool-result follow-up messages
- **Text Generation** - Streaming and non-streaming text generation (completion-style)
- **Embeddings** - Single and batch embedding vector generation for semantic search and RAG
- **Model Management** - List models, check existence, get model details, pull, and delete
- **Connectivity Validation** - Verify provider reachability before running workloads
- **Timing & Usage Metrics** - Built-in performance tracking including time-to-first-token, tokens/sec, and overall throughput, plus provider-reported token usage (prompt/completion/total) on responses when the provider returns it
- **Call Recording** - Every HTTP call is recorded with full request/response details for debugging and auditing
- **Provider-Specific Options** - Fine-tune each provider's unique parameters without losing portability

## Use Cases

PolyPrompt is a good fit when you need to:

- **Build provider-agnostic applications** - Let users choose their preferred LLM provider (local Ollama, cloud OpenAI, Google Gemini, Anthropic Claude, or VoyageAI for embeddings) without rewriting integration code
- **Add tool-backed workflows** - Let models request application functions while your code stays in charge of tool execution
- **Compare providers side-by-side** - Benchmark the same prompts across Ollama, OpenAI, Gemini, and Anthropic to evaluate quality, latency, and cost
- **Prototype rapidly** - Get a chat completion, embedding, or text generation working in a few lines of code without studying provider-specific SDKs
- **Build RAG pipelines** - Generate embeddings for document chunks using Ollama, OpenAI, Gemini, or purpose-built VoyageAI embedding models (with retrieval-role `input_type` hints and Matryoshka output dimensions), then query with semantic search
- **Create AI-powered CLI tools** - The simple API makes it easy to add LLM capabilities to command-line applications
- **Manage local model infrastructure** - Pull, list, inspect, and delete Ollama models programmatically
- **Monitor LLM performance** - Use built-in timing metrics and call recording to track latency, throughput, and errors in production
- **Build multi-model workflows** - Use different providers for different tasks (e.g., Ollama for embeddings, OpenAI for chat) through the same interface

## When Not to Use It

PolyPrompt may not be the right choice if you need:

- **Advanced multimodal or lifecycle APIs** - Vision/image inputs, structured outputs, fine-tuning APIs, batch APIs, and provider-specific agent runtimes are not currently supported
- **Automatic agent execution** - PolyPrompt returns requested tool calls, but your application executes tools and appends tool results
- **Conversation storage** - PolyPrompt sends the messages you provide; it does not persist conversation history or manage context windows
- **Token counting or cost estimation** - While some providers return token usage in responses, PolyPrompt does not provide pre-request token counting
- **Official SDK parity** - If you need every feature of a specific provider's API, use their official SDK instead

## Installation

```bash
dotnet add package PolyPrompt
```

Current documented package version: **2.4.1**.

PolyPrompt targets both **.NET 8.0** and **.NET 10.0**.

## Quick Start

### Ollama

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

using OllamaClient client = new OllamaClient("http://localhost:11434");
client.Model = "gemma3:4b";

ChatResponse response = await client.ChatAsync("What is the capital of France?");
Console.WriteLine(response.Text);
```

### OpenAI

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

using OpenAiClient client = new OpenAiClient("https://api.openai.com", "sk-your-api-key");
client.Model = "gpt-4o";

ChatResponse response = await client.ChatAsync("What is the capital of France?");
Console.WriteLine(response.Text);
```

OpenAI-compatible endpoints may be supplied either as the API root or as a versioned `/v1` base URL. For example, an Ollama instance exposing the OpenAI API can be used as:

```csharp
using PolyPrompt.Clients;

using OpenAiClient client = new OpenAiClient("http://localhost:11434/v1");
client.Model = "gpt-oss:20b";
```

### Gemini

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

using GeminiClient client = new GeminiClient(
    "https://generativelanguage.googleapis.com",
    "your-api-key");
client.Model = "gemini-2.5-flash";

ChatResponse response = await client.ChatAsync("What is the capital of France?");
Console.WriteLine(response.Text);
```

### Anthropic

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

using AnthropicClient client = new AnthropicClient(
    "https://api.anthropic.com",
    "sk-ant-your-api-key");
client.Model = "claude-opus-4-8";

ChatResponse response = await client.ChatAsync("What is the capital of France?");
Console.WriteLine(response.Text);
```

Anthropic authenticates with the `x-api-key` and `anthropic-version` headers rather than bearer authorization; both are set automatically. The version value is configurable via `client.AnthropicVersion` (default `2023-06-01`). Identity-linked API keys additionally require a workspace:

```csharp
client.WorkspaceId = "wrkspc_your-workspace-id"; // sends the anthropic-workspace-id header
```

### VoyageAI (embeddings only)

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;
using PolyPrompt.Options;

using VoyageAiClient client = new VoyageAiClient(
    "https://api.voyageai.com",
    "pa-your-api-key");
client.Model = "voyage-3.5";

VoyageAiEmbeddingOptions options = new VoyageAiEmbeddingOptions();
options.InputType = "document";      // or "query" at retrieval time
options.OutputDimension = 1024;      // Matryoshka dimensions: 256, 512, 1024, 2048

EmbeddingResponse response = await client.EmbedAsync("The quick brown fox.", options);
if (response.Success && response.Embeddings.Count > 0)
{
    Console.WriteLine("Dimensions: " + response.Embeddings[0].Embedding.Length);
}
```

VoyageAI is an embeddings-only provider: chat, tool calling, generation, and model management throw `NotSupportedException`, and `ValidateConnectivityAsync` probes with a minimal embeddings request because VoyageAI has no model listing endpoint.

## Detailed Examples

### Chat with System Prompt

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

using OllamaClient client = new OllamaClient("http://localhost:11434");
client.Model = "gemma3:4b";
client.SystemPrompt = "You are a helpful assistant that responds in haiku format.";
client.Temperature = 0.7;
client.MaxTokens = 256;

ChatResponse response = await client.ChatAsync("Tell me about the ocean.");
if (response.Success)
{
    Console.WriteLine(response.Text);
    Console.WriteLine("Runtime: " + response.OverallRuntimeMs + " ms");
}
else
{
    Console.WriteLine("Error: " + response.Error);
}
```

### Chat with Provider-Specific Options

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;
using PolyPrompt.Options;

using OllamaClient client = new OllamaClient("http://localhost:11434");
client.Model = "gemma3:4b";

OllamaChatCompletionOptions options = new OllamaChatCompletionOptions();
options.Temperature = 0.5;
options.TopP = 0.9;
options.MaxTokens = 512;
options.TopK = 40;
options.RepeatPenalty = 1.1;
options.Seed = 42;
options.SystemPrompt = "You are a concise technical writer.";

ChatResponse response = await client.ChatAsync("Explain dependency injection.", options);
Console.WriteLine(response.Text);
```

### Tool Calling

Tool calling is explicit. Use `ToolChatAsync` or `ToolChatStreamingAsync` when a model may request application functions, then execute those functions in your code and send the result back as another message. PolyPrompt normalizes the provider protocol; it does not run your tools for you.

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

using OpenAiClient client = new OpenAiClient("https://api.openai.com", "sk-your-api-key");
client.Model = "gpt-4o-mini";

ToolChatRequest request = new ToolChatRequest();
request.Messages.Add(ChatMessage.System("Answer with practical weather guidance."));
request.Messages.Add(ChatMessage.User("What is the weather in Seattle, and should I bring a jacket?"));
request.Tools.Add(ToolDefinition.Function(
    "get_weather",
    "Get current weather for a city.",
    new Dictionary<string, object>
    {
        { "type", "object" },
        { "properties", new Dictionary<string, object>
            {
                { "city", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "City name." }
                    }
                },
                { "unit", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "enum", new List<string> { "fahrenheit", "celsius" } }
                    }
                }
            }
        },
        { "required", new List<string> { "city" } }
    }));

ToolChatResponse first = await client.ToolChatAsync(request);

if (first.ToolCalls.Count > 0)
{
    request.Messages.Add(first.ToAssistantMessage());
}

foreach (ToolCall call in first.ToolCalls)
{
    if (call.Name == "get_weather")
    {
        string weatherJson = "{\"temperature\":72,\"conditions\":\"clear\"}";
        request.Messages.Add(ChatMessage.ToolResult(call.Id, call.Name, weatherJson));
    }
}

request.Tools.Clear();
request.ToolChoice = "none";

ToolChatResponse final = await client.ToolChatAsync(request);
Console.WriteLine(final.Text);
```

### Streaming Tool Calling

`ToolChatStreamingAsync` streams assistant text and tool-call deltas while accumulating final `Text` and `ToolCalls` on the response as you enumerate `Chunks`. OpenAI-compatible, Ollama, Gemini, and Anthropic clients support it.

```csharp
ToolChatStreamingResponse stream = await client.ToolChatStreamingAsync(request);

await foreach (ToolChatStreamingChunk chunk in stream.Chunks)
{
    if (!string.IsNullOrEmpty(chunk.Text))
    {
        Console.Write(chunk.Text);
    }
}

if (stream.ToolCalls.Count > 0)
{
    request.Messages.Add(stream.ToAssistantMessage());

    foreach (ToolCall call in stream.ToolCalls)
    {
        string resultJson = "{\"temperature\":72,\"conditions\":\"clear\"}";
        request.Messages.Add(ChatMessage.ToolResult(call.Id, call.Name, resultJson));
    }
}
```

Provider protocol shapes differ:

- **OpenAI-compatible** uses `/v1/chat/completions` SSE chunks and parses `delta.tool_calls` argument fragments.
- **Ollama** uses `/api/chat` newline-delimited JSON chunks and parses streamed `message.tool_calls`.
- **Gemini** uses `models/{model}:streamGenerateContent?alt=sse` with the same `GenerateContentRequest` body shape as `ToolChatAsync`: `contents`, optional `systemInstruction`, `tools.functionDeclarations`, and `toolConfig`. It parses streamed `GenerateContentResponse` chunks from `candidates[].content.parts[]`, including `text`, complete `functionCall` objects, `finishReason`, `responseId`, `modelVersion`, and `usageMetadata`.
- **Anthropic** uses `/v1/messages` with `"stream": true` and parses the event-typed SSE stream: `message_start` (id, model, input tokens), `content_block_start` for `text`, `thinking`, and `tool_use` blocks, `content_block_delta` carrying `text_delta`, `thinking_delta`, and `input_json_delta` fragments, and `message_delta` (stop reason, output tokens). Tool declarations use `tools[].input_schema`, and tool results are sent back as user-role `tool_result` content blocks; consecutive tool results merge into a single user turn so parallel tool calls resolve together.

### Reasoning Effort

Reasoning-capable models can trade latency and cost against depth of reasoning. `ReasoningEffort` is a provider-neutral value object: a semantic `ReasoningEffortLevel` (`Minimal`, `Low`, `Medium`, `High`) supplies per-provider defaults, and PolyPrompt projects it onto whatever each provider expects. Set it on the `ToolChatRequest` (or as a client-wide default via `client.ReasoningEffort`); the request value wins over the client default. When neither is set, no reasoning field is sent and the request body is unchanged.

```csharp
// Common case: a preset (or the level enum, via implicit conversion).
ToolChatRequest request = new ToolChatRequest { ReasoningEffort = ReasoningEffort.High };
request.Messages.Add(ChatMessage.User("Refactor this function and explain the tradeoffs."));
ToolChatResponse response = await client.ToolChatAsync(request);

// Tuned case: keep the semantic level, override just one provider's parameter.
request.ReasoningEffort = new ReasoningEffort(ReasoningEffortLevel.High) { GeminiThinkingBudget = 16000 };
```

Each level's default projection per provider (every value is individually overridable, and each override setter clamps/validates its input):

| `ReasoningEffortLevel` | OpenAI `reasoning_effort` | Gemini `thinkingConfig.thinkingBudget` | Ollama `think` | Anthropic `output_config.effort` + `thinking` |
|---|---|---|---|---|
| `Minimal` | `"minimal"` | `0` (off) | `false` | `"low"`, no thinking field |
| `Low` | `"low"` | `1024` | `"low"` | `"low"` + adaptive thinking |
| `Medium` | `"medium"` | `8192` | `"medium"` | `"medium"` + adaptive thinking |
| `High` | `"high"` | `-1` (dynamic) | `"high"` | `"high"` + adaptive thinking |
| _unset_ | *(omitted)* | *(omitted)* | *(omitted)* | *(omitted)* |

For Anthropic, `Low` and above send `thinking: {"type": "adaptive", "display": "summarized"}` alongside the effort so current Claude models think adaptively and return readable thinking summaries; `Minimal` omits the thinking field entirely (an explicit disable is rejected by some current Claude models, while omission is accepted everywhere).

Overrides live on the value object: `OpenAiValue` (clamped to `minimal`/`low`/`medium`/`high`), `GeminiThinkingBudget` (clamped to `-1..32768`), `OllamaThink` (clamped to `low`/`medium`/`high`/`true`/`false`), and `AnthropicEffort` (clamped to `low`/`medium`/`high`/`xhigh`/`max` — `xhigh` and `max` have no level preset and are reachable only through the override). An unrecognized string override reverts to null and falls back to the level default. Ollama support is model-dependent (for example `gpt-oss`); providers with no reasoning concept simply ignore an omitted field.

### Reasoning / Thinking Output

Where effort controls how hard a model thinks, this returns the thinking itself. A reasoning model emits its deliberation on a separate channel — OpenAI `reasoning_content`, Ollama `message.thinking`, Gemini `thought` parts, Anthropic `thinking` content blocks — and PolyPrompt surfaces it distinct from the answer text. Streamed chunks carry a `ReasoningText` delta; responses carry an accumulated `Reasoning`. Both are null when the model produced no reasoning, so responses without it are unchanged.

```csharp
ToolChatStreamingResponse stream = await client.ToolChatStreamingAsync(request);
await foreach (ToolChatStreamingChunk chunk in stream.Chunks)
{
    if (chunk.ReasoningText != null) Console.Write(chunk.ReasoningText); // the thinking
    if (chunk.Text != null) Console.Write(chunk.Text);                   // the answer
}
// After enumeration: stream.Reasoning holds the full thinking, stream.Text the full answer.
```

`Reasoning` is available on `ChatResponse`, `ChatStreamingResponse`, `ToolChatResponse`, and `ToolChatStreamingResponse`; `ReasoningText` is on `ChatStreamingChunk` and `ToolChatStreamingChunk`. Reasoning is kept out of `Text`, normalized to null when empty, and is return-only: `ToAssistantMessage()` never carries it into a follow-up request, since providers do not want their own reasoning echoed back.

| Provider | Reasoning source |
|---|---|
| OpenAI-compatible | `reasoning_content` (fallback `reasoning`) |
| Ollama | `message.thinking` |
| Gemini | `content.parts[]` with `thought: true` |
| Anthropic | `thinking` content blocks and streamed `thinking_delta` events |

### Streaming Chat

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

using OpenAiClient client = new OpenAiClient("https://api.openai.com", "sk-your-api-key");
client.Model = "gpt-4o";

ChatStreamingResponse stream = await client.ChatStreamingAsync("Write a short story about a robot.");

await foreach (ChatStreamingChunk chunk in stream.Chunks)
{
    if (!string.IsNullOrEmpty(chunk.Text))
    {
        Console.Write(chunk.Text);
    }
}

Console.WriteLine();
Console.WriteLine("Time to first token: " + stream.TimeToFirstTokenMs + " ms");
Console.WriteLine("Tokens/sec: " + stream.OverallTokensPerSecond.ToString("F1"));
Console.WriteLine("Total chunks: " + stream.ChunkCount);
```

### Single Embedding

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

using OllamaClient client = new OllamaClient("http://localhost:11434");

OllamaEmbeddingOptions options = new OllamaEmbeddingOptions();
options.Model = "all-minilm";

EmbeddingResponse response = await client.EmbedAsync("The quick brown fox jumps over the lazy dog.", options);
if (response.Success && response.Embeddings.Count > 0)
{
    float[] vector = response.Embeddings[0].Embedding;
    Console.WriteLine("Dimensions: " + vector.Length);
    Console.WriteLine("First 5 values: " + string.Join(", ", vector.Take(5)));
}
```

### Batch Embeddings

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

using OpenAiClient client = new OpenAiClient("https://api.openai.com", "sk-your-api-key");

OpenAiEmbeddingOptions options = new OpenAiEmbeddingOptions();
options.Model = "text-embedding-3-small";
options.Dimensions = 256;

List<string> documents = new List<string>
{
    "Machine learning is a subset of artificial intelligence.",
    "Neural networks are inspired by biological neurons.",
    "Deep learning uses multiple layers of neural networks."
};

EmbeddingResponse response = await client.EmbedAsync(documents, options);
if (response.Success)
{
    for (int i = 0; i < response.Embeddings.Count; i++)
    {
        Console.WriteLine("Document " + i + ": " + response.Embeddings[i].Embedding.Length + " dimensions");
    }
}
```

### Text Generation (Non-Streaming)

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

using OllamaClient client = new OllamaClient("http://localhost:11434");
client.Model = "gemma3:4b";

GenerationResponse response = await client.GenerateAsync("Once upon a time, in a land far away,");
Console.WriteLine(response.Text);
Console.WriteLine("Runtime: " + response.OverallRuntimeMs + " ms");
```

### Text Generation (Streaming)

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

using GeminiClient client = new GeminiClient(
    "https://generativelanguage.googleapis.com",
    "your-api-key");
client.Model = "gemini-2.5-flash";

GenerationStreamingResponse stream = await client.GenerateStreamingAsync("Write a limerick about coding.");

await foreach (GenerationStreamingChunk chunk in stream.Chunks)
{
    if (!string.IsNullOrEmpty(chunk.Text))
    {
        Console.Write(chunk.Text);
    }
}

Console.WriteLine();
Console.WriteLine("Time to first token: " + stream.TimeToFirstTokenMs + " ms");
Console.WriteLine("Tokens/sec: " + stream.OverallTokensPerSecond.ToString("F1"));
```

### List Available Models

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

using OllamaClient client = new OllamaClient("http://localhost:11434");

await foreach (ModelInformation model in client.ListModelsAsync())
{
    Console.WriteLine(model.Name
        + (model.DisplayName != null ? " (" + model.DisplayName + ")" : "")
        + (model.SizeBytes.HasValue ? " [" + (model.SizeBytes.Value / 1_000_000_000.0).ToString("F1") + " GB]" : ""));
}
```

### Check If a Model Exists

```csharp
using PolyPrompt.Clients;

using OllamaClient client = new OllamaClient("http://localhost:11434");

bool exists = await client.ModelExistsAsync("gemma3:4b");
Console.WriteLine("gemma3:4b exists: " + exists);

// Also matches without tags: "gemma3" matches "gemma3:latest"
bool existsNoTag = await client.ModelExistsAsync("gemma3");
Console.WriteLine("gemma3 exists: " + existsNoTag);
```

### Get Model Details

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

using OllamaClient client = new OllamaClient("http://localhost:11434");

ModelInformation? info = await client.GetModelInformationAsync("gemma3:4b");
if (info != null)
{
    Console.WriteLine("Name: " + info.Name);
    Console.WriteLine("Modified: " + info.ModifiedUtc);

    foreach (KeyValuePair<string, string?> kv in info.Metadata)
    {
        Console.WriteLine("  " + kv.Key + ": " + kv.Value);
    }
}
```

### Pull a Model (Ollama)

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

using OllamaClient client = new OllamaClient("http://localhost:11434");

bool success = await client.PullModelAsync("gemma3:4b", async (ModelPullProgress progress) =>
{
    if (progress.PercentComplete.HasValue)
    {
        Console.Write("\r" + progress.Status + " " + progress.PercentComplete.Value.ToString("F1") + "%");
    }
    else
    {
        Console.WriteLine(progress.Status);
    }
});

Console.WriteLine();
Console.WriteLine(success ? "Pull succeeded." : "Pull failed.");
```

### Delete a Model (Ollama)

```csharp
using PolyPrompt.Clients;

using OllamaClient client = new OllamaClient("http://localhost:11434");

bool deleted = await client.DeleteModelAsync("gemma3:4b");
Console.WriteLine(deleted ? "Model deleted." : "Delete failed.");
```

### Validate Connectivity

```csharp
using PolyPrompt.Clients;

using GeminiClient client = new GeminiClient(
    "https://generativelanguage.googleapis.com",
    "your-api-key");

bool reachable = await client.ValidateConnectivityAsync();
Console.WriteLine(reachable ? "Connected." : "Cannot reach provider.");
```

### Inspect Call Details

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

using OllamaClient client = new OllamaClient("http://localhost:11434");
client.Model = "gemma3:4b";

ChatResponse response = await client.ChatAsync("Hello!");

foreach (CompletionCallDetail detail in client.CallDetails)
{
    Console.WriteLine(detail.Method + " " + detail.Url);
    Console.WriteLine("  Status: " + detail.StatusCode);
    Console.WriteLine("  Time: " + detail.ResponseTimeMs + " ms");
    Console.WriteLine("  Success: " + detail.Success);
}

// CallDetails returns a detached snapshot. Use MaxCallDetails to bound retention
// and ClearCallDetails to release retained diagnostics on long-lived clients.
client.MaxCallDetails = 100;
client.ClearCallDetails();
```

### Using CancellationToken

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

using OllamaClient client = new OllamaClient("http://localhost:11434");
client.Model = "gemma3:4b";
client.TimeoutMs = 10000;

using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

try
{
    ChatResponse response = await client.ChatAsync("Write a very long essay.", token: cts.Token);
    Console.WriteLine(response.Text);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Request was cancelled.");
}
```

`TimeoutMs` is enforced with per-call cancellation tokens and is honored for both
non-streaming requests and streaming response bodies. Values must be greater than
zero and are not silently clamped.

### Custom HttpClient (custom transport, TLS, or proxy)

Every client constructor accepts an optional `HttpClient`. When you supply one, PolyPrompt
uses it for all requests and does not dispose it — you retain ownership. This lets you
configure the transport, for example to trust a self-signed certificate on an internal
endpoint, or to route requests through a proxy. When omitted, the client creates and owns
its own `HttpClient` as before.

```csharp
using System.Net.Http;
using PolyPrompt.Clients;
using PolyPrompt.Models;

// Example: relax TLS certificate validation for a trusted internal endpoint.
HttpClientHandler handler = new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
};
using HttpClient httpClient = new HttpClient(handler);

using OpenAiClient client = new OpenAiClient(
    "https://internal-llm.example.corp/v1",
    apiKey: "sk-your-api-key",
    logging: null,
    httpClient: httpClient);
client.Model = "gpt-oss:20b";

ChatResponse response = await client.ChatAsync("Hello!");
Console.WriteLine(response.Text);
```

The client sets the injected `HttpClient`'s `Timeout` to infinite so per-request timeouts can
be governed by `TimeoutMs`. If you share one `HttpClient` across multiple clients, give it an
infinite timeout yourself, since its timeout can no longer be changed once it has sent a request.

### Provider-Agnostic Code

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

CompletionClientBase CreateClient(string provider, string endpoint, string? apiKey)
{
    switch (provider)
    {
        case "ollama":
            return new OllamaClient(endpoint, apiKey);
        case "openai":
            return new OpenAiClient(endpoint, apiKey);
        case "gemini":
            return new GeminiClient(endpoint, apiKey);
        case "anthropic":
            return new AnthropicClient(endpoint, apiKey);
        case "voyageai":
            return new VoyageAiClient(endpoint, apiKey); // embeddings only
        default:
            throw new ArgumentException("Unknown provider: " + provider);
    }
}

// Same code works regardless of provider
using CompletionClientBase client = CreateClient("ollama", "http://localhost:11434", null);
client.Model = "gemma3:4b";

ChatResponse chat = await client.ChatAsync("Hello!");
Console.WriteLine(chat.Text);

await foreach (ModelInformation model in client.ListModelsAsync())
{
    Console.WriteLine("  " + model.Name);
}
```

## API Reference

### Constructors

Each provider client (`OllamaClient`, `OpenAiClient`, `GeminiClient`, `AnthropicClient`, `VoyageAiClient`) has a constructor with the same optional parameters, all with provider-appropriate defaults:

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `endpoint` | `string` | provider default | API endpoint URL |
| `apiKey` | `string?` | `null` | API key; when non-empty an `Authorization: Bearer` header is added (Anthropic instead sends `x-api-key` plus `anthropic-version`, and Gemini passes the key as a query parameter) |
| `logging` | `LoggingModule?` | `null` | Logging module; a new instance is created when omitted |
| `httpClient` | `HttpClient?` | `null` | Transport to use. When supplied, the caller owns and disposes it (see [Custom HttpClient](#custom-httpclient-custom-transport-tls-or-proxy)); when omitted, the client creates and owns its own |

### Client Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Endpoint` | `string` | varies | API endpoint URL (read-only) |
| `ApiKey` | `string?` | `null` | API key (read-only) |
| `Model` | `string` | varies | Model name for requests |
| `MaxTokens` | `int` | `4096` | Maximum tokens to generate (1 to 10,000,000) |
| `TimeoutMs` | `int` | `120000` | HTTP timeout in milliseconds; must be greater than zero |
| `Temperature` | `double?` | `null` | Sampling temperature (0.0 to 2.0) |
| `TopP` | `double?` | `null` | Nucleus sampling threshold (0.0 to 1.0) |
| `ReasoningEffort` | `ReasoningEffort?` | `null` | Default reasoning effort for tool chat; a request value overrides it |
| `SystemPrompt` | `string?` | `null` | System prompt for chat completions |
| `CallDetails` | `List<CompletionCallDetail>` | empty | Detached snapshot of recorded HTTP call details |
| `MaxCallDetails` | `int` | `1000` | Maximum retained call details; set to 0 to disable recording |

`AnthropicClient` adds three provider-specific properties: `AnthropicVersion` (the `anthropic-version` header value, default `2023-06-01`), `WorkspaceId` (the `anthropic-workspace-id` header, default null; required for identity-linked API keys), and `ModelsPageLimit` (models list page size, 1..1,000, default 1,000).

### Client Methods

| Method | Description |
|--------|-------------|
| `ChatAsync` | Non-streaming chat completion |
| `ChatStreamingAsync` | Streaming chat completion with timing metrics |
| `ToolChatAsync` | Tool-capable chat completion that returns assistant text and requested tool calls |
| `ToolChatStreamingAsync` | Streaming tool-capable chat completion that returns text chunks, tool-call deltas, and accumulated final tool calls |
| `EmbedAsync(string)` | Generate embedding for a single text |
| `EmbedAsync(List<string>)` | Generate embeddings for a batch of texts |
| `GenerateAsync` | Non-streaming text generation |
| `GenerateStreamingAsync` | Streaming text generation with timing metrics |
| `ListModelsAsync` | List available models (returns `IAsyncEnumerable<ModelInformation>`) |
| `ModelExistsAsync` | Check if a specific model exists |
| `GetModelInformationAsync` | Get detailed information about a model |
| `PullModelAsync` | Pull/download a model with progress callbacks (Ollama only) |
| `DeleteModelAsync` | Delete a model (Ollama only) |
| `ValidateConnectivityAsync` | Verify the provider is reachable |
| `ClearCallDetails` | Clear retained HTTP call details |

### Tool Calling Models

`ToolChatAsync` and `ToolChatStreamingAsync` use a message-based request because tool calling is inherently multi-step. A model can return tool calls instead of final text, and the caller decides how to execute those tools.

| Type | Purpose |
|------|---------|
| `ToolChatRequest` | Contains messages, tool definitions, tool choice, and generation overrides |
| `ReasoningEffort` | Provider-neutral reasoning effort: a `ReasoningEffortLevel` plus clamped per-provider overrides and projection methods |
| `ChatMessage` | Represents system, user, assistant, and tool-result messages |
| `ToolDefinition` | Declares a callable function with a JSON Schema parameter object |
| `ToolCall` | Represents a model-requested tool name and JSON arguments |
| `ToolCallDelta` | Represents a streamed update to a tool call ID, name, type, or argument JSON |
| `ToolChatResponse` | Contains assistant text, tool calls, status, timing, and finish metadata |
| `ToolChatStreamingChunk` | Contains streamed assistant text, tool-call deltas, finish metadata, and usage |
| `ToolChatStreamingResponse` | Contains streamed chunks plus accumulated assistant text, final tool calls, status, timing, and finish metadata |

### Provider-Specific Options

Each provider exposes option classes that extend the base options with provider-specific parameters:

| Provider | Chat Options | Embedding Options | Generation Options |
|----------|-------------|-------------------|-------------------|
| **Ollama** | `OllamaChatCompletionOptions` | `OllamaEmbeddingOptions` | `OllamaGenerationOptions` |
| **OpenAI** | `OpenAiChatCompletionOptions` | `OpenAiEmbeddingOptions` | `OpenAiGenerationOptions` |
| **Gemini** | `GeminiChatCompletionOptions` | `GeminiEmbeddingOptions` | `GeminiGenerationOptions` |
| **Anthropic** | `AnthropicChatCompletionOptions` | — (embeddings unsupported) | `AnthropicGenerationOptions` |
| **VoyageAI** | — (chat unsupported) | `VoyageAiEmbeddingOptions` | — (generation unsupported) |

**Ollama-specific parameters:** `ContextLength`, `TopK`, `RepeatPenalty`, `Seed`, `MinP`, `RepeatLastN`

**OpenAI-specific parameters:** `FrequencyPenalty`, `PresencePenalty`, `Seed`, `Dimensions`, `EncodingFormat`, `Echo`, `Suffix`, `Logprobs`

**Gemini-specific parameters:** `TopK`, `CandidateCount`, `PresencePenalty`, `FrequencyPenalty`, `TaskType`, `Title`

**Anthropic-specific parameters:** `TopK`, `StopSequences`. Note that current Claude models (Opus 4.7 and later) reject sampling parameters (`temperature`, `top_p`, `top_k`) with a 400; leave them unset for those models.

**VoyageAI-specific parameters:** `InputType` (`query`/`document` retrieval-role hint), `Truncation`, `OutputDimension` (256/512/1024/2048 on Matryoshka-capable models), `OutputDtype` (`float`/`int8`/`uint8`/`binary`/`ubinary`)

### Default Models

| Provider | Default Inference Model | Suggested Embedding Model |
|----------|------------------------|--------------------------|
| Ollama | `gemma3:4b` | `all-minilm` |
| OpenAI | `gpt-4o-mini` | `text-embedding-3-small` |
| Gemini | `gemini-2.5-flash` | `gemini-embedding-001` |
| Anthropic | `claude-opus-4-8` | — (no embeddings API) |
| VoyageAI | — (embeddings only) | `voyage-3.5` |

### Provider Feature Support

| Feature | Ollama | OpenAI | Gemini | Anthropic | VoyageAI |
|---------|--------|--------|--------|-----------|----------|
| Chat (non-streaming) | Yes | Yes | Yes | Yes | No |
| Chat (streaming) | Yes | Yes | Yes | Yes | No |
| Tool Chat (non-streaming) | Yes, when the selected model supports tools | Yes | Yes | Yes | No |
| Tool Chat (streaming) | Yes, when the selected model supports tools | Yes | Yes | Yes | No |
| Reasoning Effort | Model-dependent, via `think` | Native `reasoning_effort` | Via `thinkingConfig` budget | Via adaptive `thinking` + `output_config.effort` | No |
| Reasoning Capture | Via `message.thinking` | Via `reasoning_content` | Via `thought` parts | Via `thinking` blocks | No |
| Text Generation (non-streaming) | Yes | Legacy completions API only | Yes | Yes, via the Messages API | No |
| Text Generation (streaming) | Yes | Legacy completions API only | Yes | Yes, via the Messages API | No |
| Embeddings (single) | Yes | Yes | Yes | No | Yes |
| Embeddings (batch) | Yes | Yes | Yes | No | Yes |
| List Models | Yes | Yes | Yes | Yes, with pagination | No |
| Model Exists | Yes | Yes | Yes | Yes | No |
| Get Model Info | Yes | Yes | Yes | Yes | No |
| Pull Model | Yes | No | No | No | No |
| Delete Model | Yes | No | No | No | No |
| Validate Connectivity | Yes | Yes | Yes | Yes | Yes, via a minimal embeddings request |

Every "No" is enforced with a provider-level `NotSupportedException` carrying a message that names the missing capability — `PullModelAsync`/`DeleteModelAsync` on the cloud providers, `EmbedAsync` on Anthropic, and everything completion-shaped (chat, tool chat, generation, model management) on VoyageAI.

Unsupported entries are intentionally explicit. PolyPrompt prefers a clear provider-level `NotSupportedException` over silently falling back to a different protocol shape. One VoyageAI-specific note: `ListModelsAsync` throws at call time (VoyageAI has no model listing endpoint), and `ValidateConnectivityAsync` therefore probes with a minimal one-word embeddings request instead.

Ollama tool calling is model-dependent. For example, `gemma3:4b` is a valid Ollama chat, streaming chat, and generation model, but Ollama reports that it does not support tools. Use a tool-capable model such as `gpt-oss:20b` when you want the live suite to exercise actual Ollama tool-call and streaming tool-call paths.

## Project Structure

```
PolyPrompt/
|-- src/
|   |-- PolyPrompt/              # Core library (NuGet package)
|   |   |-- Clients/             # CompletionClientBase, OllamaClient, OpenAiClient, GeminiClient
|   |   |-- Models/              # Request/response data models
|   |   `-- Options/             # Provider-specific option classes
|   |-- OllamaConsole/           # Interactive Ollama test harness, including tc/toolchat
|   |-- OpenAIConsole/           # Interactive OpenAI test harness, including tc/toolchat
|   |-- GeminiConsole/           # Interactive Gemini test harness, including tc/toolchat
|   |-- AnthropicConsole/        # Interactive Anthropic test harness, including tc/toolchat
|   |-- VoyageAIConsole/         # Interactive VoyageAI embeddings test harness
|   |-- Test.Shared/             # Shared Touchstone test descriptors
|   |-- Test.Automated/          # Touchstone console runner
|   |-- Test.Xunit/              # xUnit adapter over Test.Shared
|   `-- Test.Nunit/              # NUnit adapter over Test.Shared
`-- assets/
    `-- logo.png
```

## Building from Source

```bash
dotnet restore src/PolyPrompt.sln
dotnet build src/PolyPrompt.sln
```

## Running the Automated Tests

```bash
# Local self-tests for request translation, timeout, cancellation, response disposal, CallDetails, chat, streaming chat, tool chat, streaming tool chat, generation, embeddings, and model management
dotnet run --project src/Test.Automated --framework net8.0 -- selftest

# Local self-tests through xUnit and NUnit
dotnet test src/Test.Xunit/Test.Xunit.csproj
dotnet test src/Test.Nunit/Test.Nunit.csproj

# Live provider tests through the Touchstone console runner. OpenAI, Gemini, and Anthropic default to their public API endpoints.
dotnet run --project src/Test.Automated -- --openai-key sk-your-key --openai-model gpt-4o-mini
dotnet run --project src/Test.Automated -- --ollama-endpoint http://localhost:11434 --ollama-model gpt-oss:20b --ollama-embedding-model all-minilm
dotnet run --project src/Test.Automated -- --gemini-key your-key --gemini-model gemini-2.5-flash
dotnet run --project src/Test.Automated -- --anthropic-key sk-ant-your-key --anthropic-model claude-opus-4-8

# Identity-linked Anthropic API keys also require a workspace ID.
dotnet run --project src/Test.Automated -- --anthropic-key sk-ant-your-key --anthropic-workspace wrkspc_your-id

# Anthropic has no embeddings API; the live embedding cases are skipped for it.

# VoyageAI is embeddings-only; chat, tool-chat, generation, and model-listing live cases
# are skipped, and model-management cases assert the unsupported behavior.
dotnet run --project src/Test.Automated -- --voyageai-key pa-your-key --voyageai-embedding-model voyage-3.5

# Ollama can also be validated through its OpenAI-compatible /v1 API.
dotnet run --project src/Test.Automated -- --openai-endpoint http://localhost:11434/v1 --openai-model gpt-oss:20b --openai-embedding-model all-minilm

# Live tool-chat cases verify successful tool use when the configured model supports tools,
# and verify the provider's unsupported-model error when it does not.

# Generic named form and positional form are also supported (provider: ollama | openai | gemini | anthropic | voyageai)
dotnet run --project src/Test.Automated -- --provider ollama --endpoint http://localhost:11434 --model gpt-oss:20b --embedding-model all-minilm
dotnet run --project src/Test.Automated -- ollama http://localhost:11434 "" gpt-oss:20b all-minilm

# Live provider tests can also be enabled for xUnit and NUnit with environment variables
set POLYPROMPT_TEST_PROVIDER=ollama
set POLYPROMPT_TEST_ENDPOINT=http://localhost:11434
set POLYPROMPT_TEST_MODEL=gpt-oss:20b
set POLYPROMPT_TEST_EMBEDDING_MODEL=all-minilm
dotnet test src/Test.Xunit/Test.Xunit.csproj
dotnet test src/Test.Nunit/Test.Nunit.csproj

# Provider-specific environment variables can be used instead of POLYPROMPT_TEST_PROVIDER
# (POLYPROMPT_TEST_OPENAI_*, POLYPROMPT_TEST_OLLAMA_*, POLYPROMPT_TEST_GEMINI_*, POLYPROMPT_TEST_ANTHROPIC_*, POLYPROMPT_TEST_VOYAGEAI_*)
set POLYPROMPT_TEST_OPENAI_API_KEY=sk-your-key
set POLYPROMPT_TEST_OPENAI_MODEL=gpt-4o-mini
dotnet test src/Test.Xunit/Test.Xunit.csproj
```

## Issues and Discussions

Have a bug to report or a feature to request? Please open an issue on GitHub:

https://github.com/jchristn/PolyPrompt/issues

Want to ask a question or start a conversation? Use GitHub Discussions:

https://github.com/jchristn/PolyPrompt/discussions

## License

PolyPrompt is available under the [MIT License](LICENSE.md). See the `LICENSE.md` file for full details.
