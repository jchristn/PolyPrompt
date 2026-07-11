<img src="https://github.com/jchristn/PolyPrompt/blob/main/assets/logo.png?raw=true" width="192" height="192">

# PolyPrompt

[![NuGet Version](https://img.shields.io/nuget/v/PolyPrompt.svg?style=flat)](https://www.nuget.org/packages/PolyPrompt/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/PolyPrompt.svg?style=flat)](https://www.nuget.org/packages/PolyPrompt/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE.md)

PolyPrompt is a lightweight, unified .NET library for chat completions, tool calling, text generation, embeddings, and model management across **Ollama**, **OpenAI**, and **Google Gemini** APIs. Write your LLM integration code once and swap providers without changing your application logic.

## What It Does

PolyPrompt provides a single, consistent API surface for interacting with multiple LLM providers. Instead of learning three different SDKs with different conventions, response formats, and streaming patterns, you use one set of methods that work identically across all supported providers.

- **Chat Completions** - Streaming and non-streaming conversational AI with system prompts
- **Tool Calling** - Provider-normalized function declarations, model tool calls, and tool-result follow-up messages
- **Text Generation** - Streaming and non-streaming text generation (completion-style)
- **Embeddings** - Single and batch embedding vector generation for semantic search and RAG
- **Model Management** - List models, check existence, get model details, pull, and delete
- **Connectivity Validation** - Verify provider reachability before running workloads
- **Timing Metrics** - Built-in performance tracking including time-to-first-token, tokens/sec, and overall throughput
- **Call Recording** - Every HTTP call is recorded with full request/response details for debugging and auditing
- **Provider-Specific Options** - Fine-tune each provider's unique parameters without losing portability

## Use Cases

PolyPrompt is a good fit when you need to:

- **Build provider-agnostic applications** - Let users choose their preferred LLM provider (local Ollama, cloud OpenAI, or Google Gemini) without rewriting integration code
- **Add tool-backed workflows** - Let models request application functions while your code stays in charge of tool execution
- **Compare providers side-by-side** - Benchmark the same prompts across Ollama, OpenAI, and Gemini to evaluate quality, latency, and cost
- **Prototype rapidly** - Get a chat completion, embedding, or text generation working in a few lines of code without studying provider-specific SDKs
- **Build RAG pipelines** - Generate embeddings for document chunks using any provider's embedding models, then query with semantic search
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

Current documented package version: **1.5.0**.

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

Tool calling is explicit. Use `ToolChatAsync` when a model may request application functions, then execute those functions in your code and send the result back as another message. PolyPrompt normalizes the provider protocol; it does not run your tools for you.

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
| `SystemPrompt` | `string?` | `null` | System prompt for chat completions |
| `CallDetails` | `List<CompletionCallDetail>` | empty | Detached snapshot of recorded HTTP call details |
| `MaxCallDetails` | `int` | `1000` | Maximum retained call details; set to 0 to disable recording |

### Client Methods

| Method | Description |
|--------|-------------|
| `ChatAsync` | Non-streaming chat completion |
| `ChatStreamingAsync` | Streaming chat completion with timing metrics |
| `ToolChatAsync` | Tool-capable chat completion that returns assistant text and requested tool calls |
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

`ToolChatAsync` uses a message-based request because tool calling is inherently multi-step. A model can return tool calls instead of final text, and the caller decides how to execute those tools.

| Type | Purpose |
|------|---------|
| `ToolChatRequest` | Contains messages, tool definitions, tool choice, and generation overrides |
| `ChatMessage` | Represents system, user, assistant, and tool-result messages |
| `ToolDefinition` | Declares a callable function with a JSON Schema parameter object |
| `ToolCall` | Represents a model-requested tool name and JSON arguments |
| `ToolChatResponse` | Contains assistant text, tool calls, status, timing, and finish metadata |

### Provider-Specific Options

Each provider exposes option classes that extend the base options with provider-specific parameters:

| Provider | Chat Options | Embedding Options | Generation Options |
|----------|-------------|-------------------|-------------------|
| **Ollama** | `OllamaChatCompletionOptions` | `OllamaEmbeddingOptions` | `OllamaGenerationOptions` |
| **OpenAI** | `OpenAiChatCompletionOptions` | `OpenAiEmbeddingOptions` | `OpenAiGenerationOptions` |
| **Gemini** | `GeminiChatCompletionOptions` | `GeminiEmbeddingOptions` | `GeminiGenerationOptions` |

**Ollama-specific parameters:** `ContextLength`, `TopK`, `RepeatPenalty`, `Seed`, `MinP`, `RepeatLastN`

**OpenAI-specific parameters:** `FrequencyPenalty`, `PresencePenalty`, `Seed`, `Dimensions`, `EncodingFormat`, `Echo`, `Suffix`, `Logprobs`

**Gemini-specific parameters:** `TopK`, `CandidateCount`, `PresencePenalty`, `FrequencyPenalty`, `TaskType`, `Title`

### Default Models

| Provider | Default Inference Model | Suggested Embedding Model |
|----------|------------------------|--------------------------|
| Ollama | `gemma3:4b` | `all-minilm` |
| OpenAI | `gpt-4o-mini` | `text-embedding-3-small` |
| Gemini | `gemini-2.5-flash` | `gemini-embedding-001` |

### Provider Feature Support

| Feature | Ollama | OpenAI | Gemini |
|---------|--------|--------|--------|
| Chat (non-streaming) | Yes | Yes | Yes |
| Chat (streaming) | Yes | Yes | Yes |
| Tool Calling | Yes | Yes | Yes |
| Text Generation | Yes | Legacy only | Yes |
| Embeddings (single) | Yes | Yes | Yes |
| Embeddings (batch) | Yes | Yes | Yes |
| List Models | Yes | Yes | Yes |
| Model Exists | Yes | Yes | Yes |
| Get Model Info | Yes | Yes | Yes |
| Pull Model | Yes | No | No |
| Delete Model | Yes | No | No |
| Validate Connectivity | Yes | Yes | Yes |

## Project Structure

```
PolyPrompt/
â”œâ”€â”€ src/
â”‚   â”œâ”€â”€ PolyPrompt/              # Core library (NuGet package)
â”‚   â”‚   â”œâ”€â”€ Clients/             # CompletionClientBase, OllamaClient, OpenAiClient, GeminiClient
â”‚   â”‚   â”œâ”€â”€ Models/              # Request/response data models
â”‚   â”‚   â””â”€â”€ Options/             # Provider-specific option classes
â”‚   â”œâ”€â”€ OllamaConsole/           # Interactive Ollama test harness
â”‚   â”œâ”€â”€ OpenAIConsole/           # Interactive OpenAI test harness
â”‚   â”œâ”€â”€ GeminiConsole/           # Interactive Gemini test harness
â”‚   â”œâ”€â”€ Test.Shared/             # Shared Touchstone test descriptors
â”‚   â”œâ”€â”€ Test.Automated/          # Touchstone console runner
â”‚   â”œâ”€â”€ Test.Xunit/              # xUnit adapter over Test.Shared
â”‚   â””â”€â”€ Test.Nunit/              # NUnit adapter over Test.Shared
â””â”€â”€ assets/
    â””â”€â”€ logo.png
```

## Building from Source

```bash
dotnet restore src/PolyPrompt.sln
dotnet build src/PolyPrompt.sln
```

## Running the Automated Tests

```bash
# Local self-tests for timeout, cancellation, response disposal, CallDetails, and tool calling
dotnet run --project src/Test.Automated --framework net8.0 -- selftest

# Local self-tests through xUnit and NUnit
dotnet test src/Test.Xunit/Test.Xunit.csproj
dotnet test src/Test.Nunit/Test.Nunit.csproj

# Live provider tests through the Touchstone console runner
dotnet run --project src/Test.Automated -- ollama http://localhost:11434
dotnet run --project src/Test.Automated -- openai https://api.openai.com sk-your-key gpt-4o text-embedding-3-small
dotnet run --project src/Test.Automated -- gemini https://generativelanguage.googleapis.com your-key gemini-2.5-flash gemini-embedding-001

# Live provider tests can also be enabled for xUnit and NUnit with environment variables
set POLYPROMPT_TEST_PROVIDER=ollama
set POLYPROMPT_TEST_ENDPOINT=http://localhost:11434
set POLYPROMPT_TEST_MODEL=gemma3:4b
set POLYPROMPT_TEST_EMBEDDING_MODEL=all-minilm
dotnet test src/Test.Xunit/Test.Xunit.csproj
dotnet test src/Test.Nunit/Test.Nunit.csproj
```

## Issues and Discussions

Have a bug to report or a feature to request? Please open an issue on GitHub:

https://github.com/jchristn/PolyPrompt/issues

Want to ask a question or start a conversation? Use GitHub Discussions:

https://github.com/jchristn/PolyPrompt/discussions

## License

PolyPrompt is available under the [MIT License](LICENSE.md). See the `LICENSE.md` file for full details.
