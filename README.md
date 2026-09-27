<img src="https://github.com/jchristn/PolyPrompt/blob/main/assets/logo.png?raw=true" width="192" height="192">

# PolyPrompt

[![NuGet Version](https://img.shields.io/nuget/v/PolyPrompt.svg?style=flat)](https://www.nuget.org/packages/PolyPrompt/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/PolyPrompt.svg?style=flat)](https://www.nuget.org/packages/PolyPrompt/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE.md)

PolyPrompt is a lightweight, unified .NET library for chat completions, tool calling, text generation, embeddings, reranking, classification, and model management across **Ollama**, **OpenAI**, **Azure OpenAI**, **Google Gemini**, **Google Vertex AI**, **Anthropic Claude**, **AWS Bedrock**, **VoyageAI**, **Cohere**, and **Hugging Face Text Embeddings Inference (TEI)** APIs. Write your LLM integration code once and swap providers without changing your application logic.

| Provider | Client | Auth | Chat / Tools / Streaming | Reasoning | Embeddings | Rerank | Classify |
|---|---|---|---|---|---|---|---|
| Ollama | `OllamaClient` | none / bearer | ✅ | ✅ | ✅ | ❌ | ❌ |
| OpenAI (+ compatible) | `OpenAiClient` | bearer | ✅ | ✅ | ✅ | ❌ | ❌ |
| Azure OpenAI | `AzureOpenAiClient` | `api-key` or Azure AD | ✅ | ✅ | ✅ | ❌ | ❌ |
| Google Gemini (AI Studio) | `GeminiClient` | API key | ✅ | ✅ | ✅ | ❌ | ❌ |
| Google Vertex AI | `VertexAiClient` | OAuth (ADC / service account) | ✅ | ✅ | ✅ (`:predict`) | ❌ | ❌ |
| Anthropic Claude | `AnthropicClient` | `x-api-key` | ✅ | ✅ | ❌ | ❌ | ❌ |
| AWS Bedrock | `BedrockClient` | SigV4 | ✅ (Converse) | ✅ | ✅ (Titan / Cohere) | ✅ (Cohere / Amazon) | ❌ |
| VoyageAI | `VoyageAiClient` | bearer | ❌ | ❌ | ✅ | ✅ | ❌ |
| Cohere | `CohereClient` | bearer | ✅ (v2 Chat) | ✅ | ✅ | ✅ | ✅ |
| Hugging Face TEI | `TeiClient` | none / bearer | ❌ | ❌ | ✅ (+ sparse) | ✅ | ✅ |

## What It Does

PolyPrompt provides a single, consistent API surface for interacting with multiple LLM providers. Instead of learning ten different SDKs with different conventions, response formats, and streaming patterns, you use one set of methods that work identically across all supported providers. Not every provider offers every capability (VoyageAI and TEI have no chat API, Anthropic has no embeddings API, and only Cohere, TEI, VoyageAI, and Bedrock can rerank), so the [Provider Feature Support](#provider-feature-support) matrix is explicit about what each provider can do, and unsupported operations throw a clear `NotSupportedException` rather than faking a protocol. PolyPrompt takes **no provider SDK dependencies**; even AWS SigV4 request signing and Google service-account token exchange are implemented in-library (two package dependencies total).

- **Chat Completions** - Streaming and non-streaming conversational AI with system prompts
- **Tool Calling** - Provider-normalized function declarations, model tool calls, streaming tool-call deltas, and tool-result follow-up messages
- **Text Generation** - Streaming and non-streaming text generation (completion-style)
- **Embeddings** - Single and batch embedding vector generation for semantic search and RAG, plus sparse (SPLADE) embeddings on TEI
- **Reranking** - Score candidate documents against a query with a cross-encoder and get them back highest score first (Cohere, TEI, VoyageAI, Bedrock)
- **Classification** - Label texts with a classification model or few-shot examples (Cohere, TEI)
- **Model Management** - List models, check existence, get model details, pull, and delete
- **Connectivity Validation** - Verify provider reachability before running workloads
- **Timing & Usage Metrics** - Built-in performance tracking including time-to-first-token, tokens/sec, and overall throughput, plus provider-reported token usage (prompt/completion/total, and cached-prompt/cache-creation/reasoning tokens where reported) on both streaming and non-streaming responses when the provider returns it
- **Call Recording** - Every HTTP call is recorded with full request/response details for debugging and auditing
- **Provider-Specific Options** - Fine-tune each provider's unique parameters without losing portability

## Use Cases

PolyPrompt is a good fit when you need to:

- **Build provider-agnostic applications** - Let users choose their preferred LLM provider (local Ollama, cloud OpenAI, Google Gemini, Anthropic Claude, or VoyageAI for embeddings) without rewriting integration code
- **Add tool-backed workflows** - Let models request application functions while your code stays in charge of tool execution
- **Compare providers side-by-side** - Benchmark the same prompts across Ollama, OpenAI, Gemini, and Anthropic to evaluate quality, latency, and cost
- **Prototype rapidly** - Get a chat completion, embedding, or text generation working in a few lines of code without studying provider-specific SDKs
- **Build RAG pipelines** - Generate embeddings for document chunks using Ollama, OpenAI, Gemini, Cohere, a self-hosted TEI server, or purpose-built VoyageAI embedding models (with retrieval-role `input_type` hints and Matryoshka output dimensions), query with semantic search, then rerank the candidates with Cohere, TEI, VoyageAI, or Bedrock before sending the best ones to the model
- **Self-host retrieval models** - Run embedding, reranker, and classifier models on your own hardware with Hugging Face Text Embeddings Inference and call them through the same interface as the hosted providers
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

Current documented package version: **2.7.0**.

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

VoyageAI is an embeddings and reranking provider: chat, tool calling, generation, and model management throw `NotSupportedException`, and `ValidateConnectivityAsync` probes with a minimal embeddings request because VoyageAI has no model listing endpoint. Reranking uses `client.RerankModel` (default `rerank-2.5`); see [Reranking](#reranking).

### Cohere

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;
using PolyPrompt.Options;

using CohereClient client = new CohereClient("https://api.cohere.com", "your-cohere-key");
client.Model = "command-a-03-2025";      // chat, tool chat, and generation
client.EmbeddingModel = "embed-v4.0";    // EmbedAsync
client.RerankModel = "rerank-v3.5";      // RerankAsync

ChatResponse chat = await client.ChatAsync("What is the capital of France?");
Console.WriteLine(chat.Text);

CohereEmbeddingOptions embedOptions = new CohereEmbeddingOptions();
embedOptions.InputType = "search_query";  // search_document (default), search_query, classification, clustering
EmbeddingResponse embedding = await client.EmbedAsync("capital of France", embedOptions);
```

Cohere serves chat, embeddings, reranking, and classification from different model families, so `CohereClient` has a separate default model for each. Chat, tool chat, and streaming use the v2 Chat API; text generation is sent as a single-turn v2 chat because Cohere retired its legacy generate endpoint. Cohere requires `input_type` for its v3 and later embedding models, so `search_document` is sent when you do not set one.

### Hugging Face Text Embeddings Inference (TEI)

[Text Embeddings Inference](https://github.com/huggingface/text-embeddings-inference) is a self-hosted server for embedding, reranker, and classifier models. Each server hosts exactly one model:

```bash
docker run -p 8080:80 ghcr.io/huggingface/text-embeddings-inference:cpu-latest --model-id BAAI/bge-small-en-v1.5
```

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

using TeiClient client = new TeiClient("http://localhost:8080");   // API key only if the server uses --api-key

EmbeddingResponse embedding = await client.EmbedAsync("The quick brown fox.");

await foreach (ModelInformation model in client.ListModelsAsync())
{
    Console.WriteLine(model.Name + " (" + model.Metadata["model_type"] + ")");   // embedding, reranker, or classifier
}
```

`TeiClient` supports dense embeddings (`/embed`), sparse embeddings (`/embed_sparse`), reranking (`/rerank`), classification (`/predict`), model information (`/info`), and connectivity validation (`/health`). Which operations succeed depends on the hosted model: for example, calling `EmbedAsync` against a reranker returns an unsuccessful response carrying TEI's HTTP 424 error. The `Model` property and per-request model overrides are informational because the server decides the model. Chat and generation belong to Hugging Face Text Generation Inference, which is OpenAI-compatible and works with `OpenAiClient`; on `TeiClient` they throw `NotSupportedException`.

### Azure OpenAI

Azure OpenAI is wire-compatible with OpenAI; the model **is** the deployment name, and requests carry an `api-version`. Authenticate with an `api-key` header or an Azure AD bearer token.

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

// API-key auth. The deployment name is the model.
using AzureOpenAiClient client = new AzureOpenAiClient(
    "https://my-resource.openai.azure.com",  // resource endpoint
    "gpt-4o",                                  // deployment name
    "your-azure-api-key",
    apiVersion: "2024-10-21");                 // optional; sensible GA default

ChatResponse response = await client.ChatAsync("Hello from Azure!");
Console.WriteLine(response.Text);

// Azure AD (Entra ID) auth instead of an api-key:
// using var aad = new AzureOpenAiClient(endpoint, "gpt-4o", new StaticTokenCredential(token));
```

Everything else — tools, streaming, `reasoning_effort`, embeddings, usage — is inherited from the OpenAI client unchanged.

### Google Vertex AI

Vertex AI serves Gemini models under a project/region path and authenticates with a short-lived OAuth token (Application Default Credentials or a service account), refreshed automatically per request.

```csharp
using PolyPrompt.Auth;
using PolyPrompt.Clients;
using PolyPrompt.Models;

// Application Default Credentials (GOOGLE_APPLICATION_CREDENTIALS key file, or a GCE/Cloud Run metadata token).
using VertexAiClient client = new VertexAiClient(
    "my-gcp-project",
    "us-central1",
    new AdcCredential());
client.Model = "gemini-2.5-flash";

ChatResponse response = await client.ChatAsync("Hello from Vertex!");
Console.WriteLine(response.Text);

// Service-account key JSON instead of ADC:
// var cred = ServiceAccountCredential.FromJson(File.ReadAllText("sa.json"));
// using var client = new VertexAiClient("my-gcp-project", "us-central1", cred);

// Embeddings use the :predict endpoint:
// client.Model = "text-embedding-004";
// var embed = await client.EmbedAsync("The quick brown fox.");
```

### AWS Bedrock

Bedrock uses the unified Converse API and signs every request with AWS Signature V4. Provide credentials through a provider (static keys or the standard `AWS_*` environment variables) and a region.

```csharp
using PolyPrompt.Auth;
using PolyPrompt.Clients;
using PolyPrompt.Models;

// Static credentials (or use new EnvironmentAwsCredential() to read AWS_ACCESS_KEY_ID/AWS_SECRET_ACCESS_KEY).
using BedrockClient client = new BedrockClient(
    new StaticAwsCredential("AKIA...", "secret...", "us-east-1"),
    "us-east-1");
client.Model = "anthropic.claude-3-5-sonnet-20240620-v1:0";

ChatResponse response = await client.ChatAsync("Hello from Bedrock!");
Console.WriteLine(response.Text);

// Embeddings (Amazon Titan or Cohere), selected by the model id:
// var embed = await client.EmbedAsync("The quick brown fox.",
//     new EmbeddingOptions { Model = "amazon.titan-embed-text-v2:0" });

// Reranking through InvokeModel (default client.RerankModel is cohere.rerank-v3-5:0):
// var ranked = await client.RerankAsync("capital of France", documents);
```

Chat, tools, streaming (over the AWS binary event-stream), reasoning (Anthropic extended thinking), embeddings, and reranking (Cohere Rerank or Amazon Rerank) are all supported; see the [feature matrix](#provider-feature-support).

## Authentication

Most providers authenticate with a single static credential passed to the constructor (a bearer key for OpenAI/Ollama/VoyageAI/Cohere, an optional bearer key for TEI servers started with `--api-key`, an `x-api-key` for Anthropic, an API key in the query string for Gemini). Azure OpenAI, Vertex AI, and Bedrock need richer, per-request credentials, all implemented in-library under `PolyPrompt.Auth` with no provider SDK:

- **Azure OpenAI** — an `api-key` header (pass the key string) or an Azure AD bearer token (pass an `ICredentialProvider`, e.g. `new StaticTokenCredential(token)`), refreshed per request.
- **Vertex AI** — a short-lived OAuth token via `ICredentialProvider`: `AdcCredential` (Application Default Credentials: `GOOGLE_APPLICATION_CREDENTIALS` key file or the GCE/Cloud Run metadata server), `ServiceAccountCredential.FromJson(...)` (RS256 JWT assertion → token exchange), or `StaticTokenCredential` (e.g. `gcloud auth print-access-token`). Tokens are cached and refreshed ahead of expiry.
- **AWS Bedrock** — AWS Signature Version 4 on every request via `IAwsCredentialProvider`: `StaticAwsCredential(accessKey, secretKey, region, sessionToken?)` or `EnvironmentAwsCredential()` (reads `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, `AWS_SESSION_TOKEN`, and `AWS_REGION`/`AWS_DEFAULT_REGION`). Temporary/role credentials with a session token are supported.

The per-request signing/token attachment is handled by the `PrepareRequestAsync` hook on `CompletionClientBase`; the other clients override nothing and keep their static-header auth.

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

### Token Usage

Provider-reported token usage is surfaced on `ChatStreamingUsage`, available as `Usage` on all four response types — `ChatResponse`, `ToolChatResponse`, `ChatStreamingResponse`, and `ToolChatStreamingResponse` — so the same telemetry is available whether you call the streaming or non-streaming API. `Usage` is `null` when the provider returns no usage data, and each field is nullable so "not reported" stays distinct from "reported as zero".

Alongside `PromptTokens`, `CompletionTokens`, and `TotalTokens`, three fields carry cache and reasoning accounting:

- **`CachedPromptTokens`** — prompt tokens served from the provider's prompt cache (a cache read), billed at a fraction of full input.
- **`CacheCreationTokens`** — prompt tokens written into the cache (a cache-creation/write), billed at a premium by the providers that report it.
- **`ReasoningTokens`** — tokens billed separately for reasoning/thinking.

```csharp
ChatResponse response = await client.ChatAsync("Summarize the attached document.");
ChatStreamingUsage? usage = response.Usage;
if (usage != null)
{
    Console.WriteLine($"Prompt: {usage.PromptTokens}, cached: {usage.CachedPromptTokens}, " +
                      $"completion: {usage.CompletionTokens}, reasoning: {usage.ReasoningTokens}");
}
```

What each provider reports, and one **cross-provider semantic** that matters for cost math — whether cached tokens are counted *inside* `PromptTokens` or *in addition to* it:

| Provider | `CachedPromptTokens` | `CacheCreationTokens` | `ReasoningTokens` | Cached vs `PromptTokens` |
|---|---|---|---|---|
| OpenAI / Azure OpenAI | yes | — | yes | subset of `PromptTokens` |
| Gemini / Vertex | yes | — | yes | subset of `PromptTokens` |
| Anthropic | yes | yes | — (thinking billed as output) | additional to `PromptTokens` |
| Bedrock | yes | yes | — (thinking billed as output) | additional to `PromptTokens` |
| Ollama | — | — | — (thinking is text only) | n/a |
| Cohere | yes (`cached_tokens`) | no | no (thinking billed as output) | subset of `PromptTokens` |

On OpenAI, Azure, Gemini, and Vertex, `CachedPromptTokens` is already included in `PromptTokens`. On Anthropic and Bedrock, `PromptTokens` counts only the uncached input, and the cache buckets are additional — so full input is `PromptTokens + CachedPromptTokens + CacheCreationTokens`. `PromptTokens` keeps its provider-native meaning; no previously returned value changed with this addition.

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

### Reranking

`RerankAsync` scores each document against a query and returns the results sorted by score, highest first. Each result's `Index` points back into the list you passed in. The same call works on every provider that supports reranking:

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

using CompletionClientBase client = new CohereClient("https://api.cohere.com", "your-cohere-key");
// or: new TeiClient("http://localhost:8080")   (a server hosting a reranker, e.g. BAAI/bge-reranker-base)
// or: new VoyageAiClient("https://api.voyageai.com", "pa-your-key")
// or: new BedrockClient(new EnvironmentAwsCredential(), "us-east-1")

List<string> documents = new List<string>
{
    "The Great Wall of China is thousands of kilometers long.",
    "Photosynthesis converts light into chemical energy in plants.",
    "Paris is the capital and largest city of France."
};

RerankOptions options = new RerankOptions();
options.TopN = 2;                 // optional; must not exceed documents.Count
options.ReturnDocuments = true;   // attach each document's text to its result

RerankResponse response = await client.RerankAsync("What is the capital of France?", documents, options);
if (response.Success)
{
    foreach (RerankResult result in response.Results)
    {
        Console.WriteLine("[" + result.Index + "] " + result.Score.ToString("F4") + " " + result.Document);
    }
}
```

Things to know about reranking:

- **Invalid arguments throw.** A null query or document list throws `ArgumentNullException`; an empty or whitespace query, an empty document list, or a null document throws `ArgumentException`; and `TopN` larger than the number of documents throws `ArgumentOutOfRangeException` (setting `TopN` below 1 throws immediately). This is checked before any request is sent and is identical on every provider.
- **HTTP errors do not throw.** As with the other operations, a provider error returns `Success = false` with `StatusCode` and `Error` set.
- **Scores are provider-specific.** Cohere, VoyageAI, and Bedrock return a normalized 0..1 relevance; TEI returns a sigmoid 0..1 score by default or raw logits with `TeiRerankOptions.RawScores = true`. Compare scores within one provider, not across providers.
- **TopN.** Cohere and Bedrock send it as `top_n` and VoyageAI as `top_k`. TEI has no such parameter, so every document is scored and the list is trimmed client-side.
- **Document text** is attached from your own list by index, so `ReturnDocuments` never requests extra data from the provider.
- **Usage.** `RerankResponse.TotalTokens` is populated by Cohere (input tokens) and VoyageAI (total tokens); `SearchUnits` is populated by Cohere.

### Classification

`ClassifyAsync` returns one result per input, in input order, with the top label and every scored label (highest first):

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;
using PolyPrompt.Options;

// TEI: the server hosts a sequence classification model (or a reranker, which scores a single label).
using TeiClient tei = new TeiClient("http://localhost:8080");
ClassificationResponse teiResult = await tei.ClassifyAsync(new List<string> { "I love this!", "This is terrible." });

// Cohere: few-shot examples (at least 2 per label), or set ClassificationModel to a fine-tuned model.
using CohereClient cohere = new CohereClient("https://api.cohere.com", "your-cohere-key");
CohereClassificationOptions options = new CohereClassificationOptions();
options.Examples.Add(new ClassificationExample("I love it", "positive"));
options.Examples.Add(new ClassificationExample("This is fantastic", "positive"));
options.Examples.Add(new ClassificationExample("I hate it", "negative"));
options.Examples.Add(new ClassificationExample("This is awful", "negative"));

ClassificationResponse cohereResult = await cohere.ClassifyAsync("The service was great", options);
Console.WriteLine(cohereResult.Classifications[0].Label + " " + cohereResult.Classifications[0].Score);
```

TEI inputs are always sent in its batch form (each input wrapped in its own array) so that two inputs are never interpreted as a single text pair.

### Sparse Embeddings (TEI)

A TEI server hosting a SPLADE-style model returns sparse vectors through `EmbedSparseAsync`:

```csharp
using TeiClient client = new TeiClient("http://localhost:8080");
SparseEmbeddingResponse response = await client.EmbedSparseAsync(new List<string> { "sparse retrieval" });
foreach (SparseValue value in response.Embeddings[0].Values)
{
    Console.WriteLine(value.Index + ": " + value.Value);
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
            return new VoyageAiClient(endpoint, apiKey); // embeddings and reranking
        case "cohere":
            return new CohereClient(endpoint, apiKey);
        case "tei":
            return new TeiClient(endpoint, apiKey);      // embeddings, reranking, classification
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

The single-key clients (`OllamaClient`, `OpenAiClient`, `GeminiClient`, `AnthropicClient`, `VoyageAiClient`, `CohereClient`, `TeiClient`) share a constructor with the same optional parameters, all with provider-appropriate defaults:

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `endpoint` | `string` | provider default | API endpoint URL |
| `apiKey` | `string?` | `null` | API key; when non-empty an `Authorization: Bearer` header is added (Anthropic instead sends `x-api-key` plus `anthropic-version`, and Gemini passes the key as a query parameter) |
| `logging` | `LoggingModule?` | `null` | Logging module; a new instance is created when omitted |
| `httpClient` | `HttpClient?` | `null` | Transport to use. When supplied, the caller owns and disposes it (see [Custom HttpClient](#custom-httpclient-custom-transport-tls-or-proxy)); when omitted, the client creates and owns its own |

The three cloud providers added in 2.5.0 take provider-shaped constructors because their credentials and routing differ:

| Client | Signature | Notes |
|--------|-----------|-------|
| `AzureOpenAiClient` | `(endpoint, deployment, apiKey, apiVersion?, logging?, httpClient?)` or `(endpoint, deployment, ICredentialProvider, apiVersion?, …)` | `deployment` becomes `Model`; `api-key` header or Azure AD bearer; settable `ApiVersion` (default `2024-10-21`) |
| `VertexAiClient` | `(project, region, ICredentialProvider, endpoint?, logging?, httpClient?)` | OAuth bearer via `AdcCredential` / `ServiceAccountCredential` / `StaticTokenCredential`; endpoint defaults to `https://{region}-aiplatform.googleapis.com` |
| `BedrockClient` | `(IAwsCredentialProvider, region, logging?, httpClient?, endpoint?)` | SigV4-signed; `StaticAwsCredential` / `EnvironmentAwsCredential`; endpoint defaults to `https://bedrock-runtime.{region}.amazonaws.com` |

Credential providers live in `PolyPrompt.Auth`: `StaticAwsCredential`/`EnvironmentAwsCredential` (AWS), and `StaticTokenCredential`/`ServiceAccountCredential`/`AdcCredential` (OAuth bearer). Bearer tokens are cached and refreshed automatically ahead of expiry.

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

`AnthropicClient` adds three provider-specific properties: `AnthropicVersion` (the `anthropic-version` header value, default `2023-06-01`), `WorkspaceId` (the `anthropic-workspace-id` header, default null; required for identity-linked API keys), and `ModelsPageLimit` (models list page size, 1..1,000, default 1,000). `AzureOpenAiClient` adds `ApiVersion` (the `api-version` query value, default `2024-10-21`).

`CohereClient` adds `EmbeddingModel` (default `embed-v4.0`), `RerankModel` (default `rerank-v3.5`), `ClassificationModel` (default null, which lets Cohere pick its default model for few-shot examples), and `ModelsPageSize` (1..1,000, default 1,000). `VoyageAiClient` adds `RerankModel` (default `rerank-2.5`), and `BedrockClient` adds `RerankModel` (default `cohere.rerank-v3-5:0`; `amazon.rerank-v1:0` also works). `TeiClient` has no extra properties because the server decides the model.

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
| `RerankAsync(query, documents)` | Score documents against a query, highest score first (Cohere, TEI, VoyageAI, Bedrock) |
| `ClassifyAsync(string)` / `ClassifyAsync(List<string>)` | Classify one or more texts (Cohere, TEI) |
| `EmbedSparseAsync(string)` / `EmbedSparseAsync(List<string>)` | Generate sparse embeddings (TEI with a SPLADE model) |
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

| Provider | Chat Options | Embedding Options | Generation Options | Rerank / Classify / Sparse Options |
|----------|-------------|-------------------|-------------------|------------------------------------|
| **Ollama** | `OllamaChatCompletionOptions` | `OllamaEmbeddingOptions` | `OllamaGenerationOptions` | (unsupported) |
| **OpenAI** | `OpenAiChatCompletionOptions` | `OpenAiEmbeddingOptions` | `OpenAiGenerationOptions` | (unsupported) |
| **Azure OpenAI** | `AzureOpenAiChatCompletionOptions` | `AzureOpenAiEmbeddingOptions` | (OpenAI generation options) | (unsupported) |
| **Gemini** | `GeminiChatCompletionOptions` | `GeminiEmbeddingOptions` | `GeminiGenerationOptions` | (unsupported) |
| **Vertex AI** | (Gemini chat options) | `VertexAiEmbeddingOptions` | (Gemini generation options) | (unsupported) |
| **Anthropic** | `AnthropicChatCompletionOptions` | (embeddings unsupported) | `AnthropicGenerationOptions` | (unsupported) |
| **Bedrock** | (base chat options) | `BedrockEmbeddingOptions` | (base generation options) | `BedrockRerankOptions` |
| **VoyageAI** | (chat unsupported) | `VoyageAiEmbeddingOptions` | (generation unsupported) | `VoyageAiRerankOptions` |
| **Cohere** | `CohereChatCompletionOptions` | `CohereEmbeddingOptions` | `CohereGenerationOptions` | `CohereRerankOptions`, `CohereClassificationOptions` |
| **TEI** | (chat unsupported) | `TeiEmbeddingOptions` | (generation unsupported) | `TeiRerankOptions`, `TeiClassificationOptions`, `TeiSparseEmbeddingOptions` |

The base option types for the new operations are `RerankOptions` (`Model`, `TopN`, `ReturnDocuments`), `ClassificationOptions` (`Model`), and `SparseEmbeddingOptions` (`Model`).

**Ollama-specific parameters:** `ContextLength`, `TopK`, `RepeatPenalty`, `Seed`, `MinP`, `RepeatLastN`

**OpenAI / Azure OpenAI-specific parameters:** `FrequencyPenalty`, `PresencePenalty`, `Seed`, `Dimensions`, `EncodingFormat`, `Echo`, `Suffix`, `Logprobs` (Azure inherits the OpenAI options unchanged)

**Gemini / Vertex AI-specific parameters:** `TopK`, `CandidateCount`, `PresencePenalty`, `FrequencyPenalty`, `TaskType`, `Title`. `VertexAiEmbeddingOptions` adds `OutputDimensionality` and `AutoTruncate` for the `:predict` endpoint.

**Anthropic-specific parameters:** `TopK`, `StopSequences`. Note that current Claude models (Opus 4.7 and later) reject sampling parameters (`temperature`, `top_p`, `top_k`) with a 400; leave them unset for those models.

**Bedrock-specific parameters:** `BedrockEmbeddingOptions` exposes `InputType` (Cohere), `Dimensions` and `Normalize` (Amazon Titan v2). Reasoning maps to Converse extended thinking via `ReasoningEffort.BedrockThinkingBudget` / `ToBedrockThinkingBudget()`.

**VoyageAI-specific parameters:** `InputType` (`query`/`document` retrieval-role hint), `Truncation`, `OutputDimension` (256/512/1024/2048 on Matryoshka-capable models), `OutputDtype` (`float`/`int8`/`uint8`/`binary`/`ubinary`). `VoyageAiRerankOptions` adds `Truncation`.

**Bedrock rerank parameters:** `BedrockRerankOptions.MaxTokensPerDoc` (Cohere rerank models).

**Cohere-specific parameters:** chat and generation options add `TopK` (`k`, 0..500), `Seed`, `FrequencyPenalty` and `PresencePenalty` (0..1), and `StopSequences` (at most 5); `TopP` is sent as `p` and clamped to Cohere's 0.01..0.99. `CohereEmbeddingOptions` adds `InputType` (`search_document`/`search_query`/`classification`/`clustering`, default `search_document`), `EmbeddingType` (`float`/`int8`/`uint8`/`binary`/`ubinary`), `OutputDimension` (256/512/1024/1536 on embed-v4.0), and `Truncate` (`NONE`/`START`/`END`). `CohereRerankOptions` adds `MaxTokensPerDoc`; `CohereClassificationOptions` adds `Examples` and `Truncate`. Reasoning maps to `thinking.token_budget` via `ReasoningEffort.CohereThinkingBudget` / `ToCohereThinkingBudget()` (Minimal turns thinking off, Low 1024, Medium 4096, High 16384).

**TEI-specific parameters:** `TeiEmbeddingOptions` adds `Normalize`, `Truncate`, `TruncationDirection` (`left`/`right`), `PromptName` (a sentence-transformers prompt such as `query`), and `Dimensions`. `TeiRerankOptions` and `TeiClassificationOptions` add `RawScores`, `Truncate`, and `TruncationDirection`; `TeiSparseEmbeddingOptions` adds `Truncate`, `TruncationDirection`, and `PromptName`.

### Default Models

| Provider | Default Inference Model | Suggested Embedding Model | Default Rerank Model |
|----------|------------------------|--------------------------|----------------------|
| Ollama | `gemma3:4b` | `all-minilm` | (none) |
| OpenAI | `gpt-4o-mini` | `text-embedding-3-small` | (none) |
| Azure OpenAI | (deployment name; no default) | (deployment name) | (none) |
| Gemini | `gemini-2.5-flash` | `gemini-embedding-001` | (none) |
| Vertex AI | `gemini-2.5-flash` | `text-embedding-004` | (none) |
| Anthropic | `claude-opus-4-8` | (no embeddings API) | (none) |
| Bedrock | `anthropic.claude-3-5-sonnet-20240620-v1:0` | `amazon.titan-embed-text-v2:0` | `cohere.rerank-v3-5:0` |
| VoyageAI | (no chat API) | `voyage-3.5` | `rerank-2.5` |
| Cohere | `command-a-03-2025` | `embed-v4.0` (`EmbeddingModel`) | `rerank-v3.5` |
| TEI | (no chat API) | (whatever the server hosts) | (whatever the server hosts) |

### Provider Feature Support

| Feature | Ollama | OpenAI | Azure OpenAI | Gemini | Vertex AI | Anthropic | Bedrock | VoyageAI | Cohere | TEI |
|---------|--------|--------|--------------|--------|-----------|-----------|---------|----------|--------|-----|
| Chat (streaming + non-streaming) | Yes | Yes | Yes | Yes | Yes | Yes | Yes (Converse) | No | Yes (v2 Chat) | No |
| Tool Chat (streaming + non-streaming) | Model-dependent | Yes | Yes | Yes | Yes | Yes | Yes | No | Yes | No |
| Reasoning Effort | Via `think` | `reasoning_effort` | `reasoning_effort` | `thinkingConfig` budget | `thinkingConfig` budget | Adaptive `thinking` + effort | Converse thinking budget | No | `thinking.token_budget` | No |
| Reasoning Capture | `message.thinking` | `reasoning_content` | `reasoning_content` | `thought` parts | `thought` parts | `thinking` blocks | `reasoningContent` | No | `thinking` content + tool plan | No |
| Text Generation | Yes | Legacy completions | Legacy completions | Yes | Yes | Via Messages API | Via Converse | No | Via v2 Chat | No |
| Embeddings (single + batch) | Yes | Yes | Yes | Yes | Yes (`:predict`) | No | Yes (Titan / Cohere) | Yes | Yes | Yes |
| Sparse Embeddings | No | No | No | No | No | No | No | No | No | Yes (SPLADE models) |
| Rerank | No | No | No | No | No | No | Yes (Cohere / Amazon) | Yes | Yes | Yes (reranker models) |
| Classify | No | No | No | No | No | No | No | No | Yes | Yes (classifier / reranker models) |
| List / Exists / Get Model | Yes | Yes | Yes | Yes | No | Yes (paginated) | Yes (control-plane) | No | Yes (paginated) | Yes (the hosted model, from `/info`) |
| Pull / Delete Model | Yes | No | No | No | No | No | No | No | No | No |
| Validate Connectivity | Yes | Yes | Yes | Yes | Yes (via `:predict`) | Yes | Yes | Yes (via embeddings) | Yes (via `/v1/models`) | Yes (via `/health`) |

Every "No" is enforced with a provider-level `NotSupportedException` carrying a message that names the missing capability: `PullModelAsync`/`DeleteModelAsync` on the cloud providers, `EmbedAsync` on Anthropic, model management on Vertex AI, everything completion-shaped (chat, tool chat, generation, model management) on VoyageAI, chat and generation on TEI, and `RerankAsync`, `ClassifyAsync`, and `EmbedSparseAsync` wherever the provider has no such API. These are thrown before any request is sent.

Unsupported entries are intentionally explicit. PolyPrompt prefers a clear provider-level `NotSupportedException` over silently falling back to a different protocol shape. One VoyageAI-specific note: `ListModelsAsync` throws at call time (VoyageAI has no model listing endpoint), and `ValidateConnectivityAsync` therefore probes with a minimal one-word embeddings request instead.

TEI is the one provider where support depends on the deployment rather than the client: a TEI server hosts one model, and its type (`embedding`, `reranker`, or `classifier`, reported in `ModelInformation.Metadata["model_type"]`) decides which of `EmbedAsync`, `RerankAsync`, `ClassifyAsync`, and `EmbedSparseAsync` succeed. The others return an unsuccessful response carrying TEI's HTTP 424 error rather than throwing, because the client cannot know the model type without asking the server.

Cohere tool choice: Cohere accepts only `REQUIRED` and `NONE`. `auto` (or null) omits the field, `required`/`any` sends `REQUIRED`, `none` sends `NONE`, and a specific tool name sends only that tool with `REQUIRED` so the model must call it. Cohere's tool plan (the reasoning it emits before calling tools) is surfaced as `Reasoning` and, like all reasoning, is never sent back on follow-up turns.

Ollama tool calling is model-dependent. For example, `gemma3:4b` is a valid Ollama chat, streaming chat, and generation model, but Ollama reports that it does not support tools. Use a tool-capable model such as `gpt-oss:20b` when you want the live suite to exercise actual Ollama tool-call and streaming tool-call paths.

## Project Structure

```
PolyPrompt/
|-- src/
|   |-- PolyPrompt/              # Core library (NuGet package)
|   |   |-- Auth/                # SigV4 signing and OAuth / AWS credential providers
|   |   |-- Clients/             # CompletionClientBase and one client per provider
|   |   |-- Wire/                # AWS event-stream decoder
|   |   |-- Models/              # Request/response data models
|   |   `-- Options/             # Provider-specific option classes
|   |-- OllamaConsole/           # Interactive Ollama test harness, including tc/toolchat
|   |-- OpenAIConsole/           # Interactive OpenAI test harness, including tc/toolchat
|   |-- GeminiConsole/           # Interactive Gemini test harness, including tc/toolchat
|   |-- AnthropicConsole/        # Interactive Anthropic test harness, including tc/toolchat
|   |-- VoyageAIConsole/         # Interactive VoyageAI embeddings and rerank test harness
|   |-- CohereConsole/           # Interactive Cohere test harness, including tc/toolchat, rr/rerank, cl/classify
|   |-- TeiConsole/              # Interactive TEI test harness: embed, sparse, rerank, classify, info
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
# Local self-tests for request translation, timeout, cancellation, response disposal, CallDetails, chat, streaming chat, tool chat, streaming tool chat, generation, embeddings, reranking, classification, sparse embeddings, and model management
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

# VoyageAI has no chat API; chat, tool-chat, generation, and model-listing live cases
# are skipped, and model-management cases assert the unsupported behavior.
dotnet run --project src/Test.Automated -- --voyageai-key pa-your-key --voyageai-embedding-model voyage-3.5 --voyageai-rerank-model rerank-2.5

# Cohere runs every live case, including rerank and few-shot classification.
dotnet run --project src/Test.Automated -- --cohere-key your-cohere-key --cohere-model command-a-03-2025 --cohere-rerank-model rerank-v3.5

# TEI: chat and generation cases are skipped. The embed, rerank, and classify cases read the hosted
# model type from /info and assert success for operations the model serves and a clean HTTP 424
# failure for the rest, so run the suite once per model type you deploy.
docker run -p 8081:80 ghcr.io/huggingface/text-embeddings-inference:cpu-latest --model-id BAAI/bge-reranker-base
docker run -p 8082:80 ghcr.io/huggingface/text-embeddings-inference:cpu-latest --model-id BAAI/bge-small-en-v1.5
dotnet run --project src/Test.Automated -- --tei-endpoint http://localhost:8081
dotnet run --project src/Test.Automated -- --tei-endpoint http://localhost:8082

# The live rerank and classify cases assert NotSupportedException on providers without those APIs.

# Ollama can also be validated through its OpenAI-compatible /v1 API.
dotnet run --project src/Test.Automated -- --openai-endpoint http://localhost:11434/v1 --openai-model gpt-oss:20b --openai-embedding-model all-minilm

# Live tool-chat cases verify successful tool use when the configured model supports tools,
# and verify the provider's unsupported-model error when it does not.

# Generic named form and positional form are also supported (provider: ollama | openai | gemini | anthropic | voyageai | cohere | tei)
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
# (POLYPROMPT_TEST_OPENAI_*, POLYPROMPT_TEST_OLLAMA_*, POLYPROMPT_TEST_GEMINI_*, POLYPROMPT_TEST_ANTHROPIC_*, POLYPROMPT_TEST_VOYAGEAI_*,
#  POLYPROMPT_TEST_COHERE_* including COHERE_RERANK_MODEL, and POLYPROMPT_TEST_TEI_API_KEY / TEI_ENDPOINT).
# POLYPROMPT_TEST_RERANK_MODEL sets the rerank model for the generic POLYPROMPT_TEST_PROVIDER form.
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
