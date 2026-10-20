<img src="https://github.com/jchristn/PolyPrompt/blob/main/assets/logo.png?raw=true" width="192" height="192">

# PolyPrompt

[![NuGet Version](https://img.shields.io/nuget/v/PolyPrompt.svg?style=flat)](https://www.nuget.org/packages/PolyPrompt/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/PolyPrompt.svg?style=flat)](https://www.nuget.org/packages/PolyPrompt/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE.md)

PolyPrompt is a lightweight, unified .NET library for chat completions, tool calling, text generation, embeddings, reranking, classification, decision models, and model management across **Ollama**, **Ollama Cloud**, **OpenAI**, **xAI Grok**, **Azure OpenAI**, **Google Gemini**, **Google Vertex AI**, **Anthropic Claude**, **AWS Bedrock**, **VoyageAI**, **Cohere**, **Hugging Face Text Embeddings Inference (TEI)**, and **TypeSafe** (Jev and the local Ollaya runtime). Write your integration code once against a capability, and swap providers without changing your application logic.

> **Upgrading from 2.x?** Version 3.0 splits each provider client into one client per capability. See [MIGRATION_V2_TO_V3.md](MIGRATION_V2_TO_V3.md).

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

## What It Does

PolyPrompt gives you one consistent API per capability. A chat call looks the same on Ollama, OpenAI, Gemini, Claude, Bedrock, and Cohere; an embedding call looks the same across providers; a rerank call looks the same on four. Each client exposes only what its provider supports, so a missing capability is a compile error rather than a runtime surprise. PolyPrompt takes **no provider SDK dependencies**: AWS SigV4 signing and Google service-account token exchange are implemented in-library, and JSON handling uses the built-in `System.Text.Json` (one package dependency, for logging).

- **Chat completions**: streaming and non-streaming conversational AI with system prompts
- **Tool calling**: provider-normalized function declarations, model tool calls, streaming tool-call deltas, and tool-result follow-up messages
- **Text generation**: streaming and non-streaming completion-style generation
- **Embeddings**: single and batch dense vectors for semantic search and RAG, plus sparse (SPLADE) vectors on TEI
- **Reranking**: score candidate documents against a query and get them back highest score first
- **Classification**: label texts with a classifier model or few-shot examples
- **Decision models**: ask typed questions (yes/no, pick one, rate on a rubric) about a state and get calibrated probabilities instead of generated text
- **Model management**: list models, check existence, get details, and pull or delete (Ollama)
- **Connectivity validation**: verify each client's endpoint and credentials before running workloads
- **Timing and usage metrics**: time-to-first-token, tokens per second, and provider-reported token usage, including cached, cache-write, and reasoning tokens where reported
- **Call recording**: every HTTP call is recorded with full request and response details for debugging and auditing
- **Observability**: OpenTelemetry-shaped metrics and traces for every operation, outbound request, stream, decision batch, and credential refresh, emitted through the BCL `Meter` and `ActivitySource` named `PolyPrompt` with no exporter dependency (see [TELEMETRY.md](TELEMETRY.md))
- **Settings that compose**: client-wide `Defaults` plus per-call options, merged field by field, with provider-specific options that extend the common ones
- **Native AOT and trimming**: no reflection required, verified by publishing a native binary that exercises every client; your own types plug in through source-generated JSON metadata (see [AOT.md](AOT.md))

## Use Cases

PolyPrompt is a good fit when you need to:

- **Build provider-agnostic applications**: let users choose local Ollama, OpenAI, Gemini, Claude, or Bedrock without rewriting integration code
- **Add tool-backed workflows**: let models request application functions while your code stays in charge of running them
- **Build RAG pipelines**: embed document chunks with any of nine providers, retrieve, then rerank the candidates with Cohere, TEI, VoyageAI, or Bedrock before sending the best ones to a model
- **Route and triage with decision models**: classify intent, flag urgency, and score severity in one call, with calibrated confidence you can threshold on
- **Self-host retrieval models**: run embedding, reranker, and classifier models on your own hardware with TEI, or decision models with Ollaya, through the same interfaces as the hosted providers
- **Compare providers side by side**: benchmark the same prompts across providers for quality, latency, and cost
- **Manage local model infrastructure**: pull, list, inspect, and delete Ollama models programmatically
- **Monitor LLM performance**: use the built-in timing metrics and call recording to track latency, throughput, and errors

## When Not to Use It

PolyPrompt may not be the right choice if you need:

- **Advanced multimodal or lifecycle APIs**: vision inputs, structured outputs, fine-tuning, provider batch APIs, and agent runtimes are not supported
- **Automatic agent execution**: PolyPrompt returns requested tool calls; your application runs the tools and appends the results
- **Conversation storage**: PolyPrompt sends the messages you provide; it does not persist history or manage context windows
- **Token counting or cost estimation** before a request
- **Official SDK parity**: if you need every feature of one provider's API, use its official SDK

## Installation

```bash
dotnet add package PolyPrompt
```

Current documented package version: **3.4.1**. PolyPrompt targets **.NET 8.0** and **.NET 10.0**.

## Architecture

Every client derives from `ClientBase`, which owns the HTTP transport, per-request credentials, `TimeoutMs`, `CallDetails`, and `ValidateConnectivityAsync`. Each client then derives from exactly one capability base:

```
ClientBase                     HTTP transport, timeouts, credentials, CallDetails, ValidateConnectivityAsync
├── CompletionClientBase       ChatAsync, ChatStreamingAsync, ToolChatAsync, ToolChatStreamingAsync,
│                              GenerateAsync, GenerateStreamingAsync
├── EmbeddingClientBase        EmbedAsync(string), EmbedAsync(List<string>)
├── SparseEmbeddingClientBase  EmbedSparseAsync(string), EmbedSparseAsync(List<string>)
├── RerankClientBase           RerankAsync(query, documents)
├── ClassificationClientBase   ClassifyAsync(string), ClassifyAsync(List<string>)
├── DecisionClientBase         DecideAsync(DecisionRequest), DecideAsync(List<DecisionRequest>)
└── ModelClientBase            ListModelsAsync, ModelExistsAsync, GetModelInformationAsync
```

Three rules hold for every client:

1. **Arguments are validated before any request**, identically on every provider. Null prompts or inputs throw `ArgumentNullException`; empty lists, null list elements, empty tool-chat messages, and invalid decision questions throw `ArgumentException`; an operation that needs a model throws `InvalidOperationException` when neither the call nor `Defaults` sets one.
2. **HTTP errors do not throw.** A provider error returns `Success = false` with `StatusCode` and `Error` set. Cancellation and `TimeoutMs` timeouts propagate as `OperationCanceledException`.
3. **Credentials are attached per request.** No client modifies `HttpClient.DefaultRequestHeaders`, so clients for different providers can share one `HttpClient`.

### Settings: Defaults and per-call options

Each client has a get-only `Defaults` object of its capability's options type (for example `OllamaCompletionOptions` on `OllamaCompletionClient`), and every operation accepts the same type per call. For each setting, a non-null per-call value wins, otherwise `Defaults`, otherwise the provider's own default (the field is not sent). `Model` is shorthand for `Defaults.Model`.

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;
using PolyPrompt.Options;

using OllamaCompletionClient chat = new OllamaCompletionClient("http://localhost:11434");
chat.Model = "gemma3:4b";
chat.Defaults.MaxTokens = 512;
chat.Defaults.Temperature = 0.2;
chat.Defaults.ContextLength = 8192;          // Ollama-specific
chat.Defaults.SystemPrompt = "Be concise.";

// Sends max tokens 512 and context length 8192 from Defaults, and temperature 0.9 from this call.
ChatResponse reply = await chat.ChatAsync("Explain dependency injection.", new CompletionOptions { Temperature = 0.9 });

// A per-call model does not change the client's model.
ChatResponse other = await chat.ChatAsync("Hi", new CompletionOptions { Model = "gpt-oss:20b" });
```

The same options object is used by `ChatAsync`, `GenerateAsync`, and `ToolChatAsync` (through `ToolChatRequest.Options`). When nothing sets `MaxTokens`, 4096 is sent.

## Quick Start

### xAI Grok (native Responses API)

`XAiCompletionClient` uses `https://api.x.ai/v1/responses` for chat, tool chat, and generation, including their streaming variants. It does not use legacy Chat Completions or `/completions`. API root and `/v1` base URLs are both accepted. All xAI clients require a nonempty bearer API key from the xAI API console.

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;
using PolyPrompt.Options;

using XAiCompletionClient client = new XAiCompletionClient(
    Environment.GetEnvironmentVariable("XAI_API_KEY")!);
client.Model = "grok-4.7";
client.Defaults.ReasoningEffort = ReasoningEffort.Medium;
ChatResponse answer = await client.ChatAsync("Explain the result.");
Console.WriteLine(answer.Reasoning); // Returned reasoning summary, separate from Text
Console.WriteLine(answer.Text);
```

The provider can expose readable reasoning summaries and encrypted reasoning. Encrypted content is opaque continuation state, not displayable thinking. Common efforts map to `low` (Minimal/Low), `medium`, and `high`; `XAiCompletionOptions.ReasoningEffortOverride = "xhigh"` selects the provider's extra-high effort on models that support it. Unsupported model features remain provider errors; reasoning cannot be disabled on models that require it.

**Conversation state and tools.** `Store` defaults to `false`, and `reasoning.encrypted_content` is always requested. The returned `ProviderState` contains the complete native output array, including encrypted reasoning, citations, hosted-tool results, and unknown item fields. For tool follow-ups, use `response.ToAssistantMessage()`; it preserves the output state and replays it once in its original order. Append `ChatMessage.ToolResult(call.Id, call.Name, result)` for each local function. Streamed calls use the function's `call_id`, not the output item's `id`, and fragmented/parallel calls are assembled separately. Fully enumerate a stream before using its state or tool calls. A truncated/failed stream or incomplete function is reported as unsuccessful and must not be executed.

`ToAssistantMessage()` now preserves assistant text alongside tool calls for every provider. Persisting the resulting `ChatMessage` through `PolyPromptJsonContext.Default.ChatMessage` or `ListChatMessage` also preserves xAI state under Native AOT.

**Options.** `XAiCompletionOptions` adds storage and `PreviousResponseId`, parallel/strict function tools, `FunctionTools` for raw/multimodal requests, hosted/native tools, provider tool-turn limits, reasoning overrides and summary preferences, `TextFormat` for structured JSON outputs, explicit tool choice, inclusion fields, TopK/MinP, model-dependent log probabilities, service tier, cache key, end-user/safety identifiers, guidelines, and `AdditionalParameters` for future fields. Per-call settings override defaults field by field. Extensions cannot overwrite built-in request fields. For example:

```csharp
XAiCompletionOptions options = new XAiCompletionOptions
{
    MaxTokens = 2048,
    HostedTools = new List<XAiHostedTool>
    {
        XAiHostedTool.Create("web_search"),
        XAiHostedTool.Create("x_search", new Dictionary<string, object>
        {
            ["allowed_x_handles"] = new[] { "SpaceX" }
        })
    },
    TextFormat = new Dictionary<string, object> { ["type"] = "json_object" }
};
ChatResponse result = await client.ChatAsync("Summarize recent launch news as JSON.", options);
```

Native tool definitions support `web_search`, `x_search`, `code_interpreter`, `file_search`, `mcp`, `image_generation`, `shell`, and `tool_search`, retaining all their parameter fields. Supply required provider parameters, such as collection IDs for `file_search` and the server URL/label for `mcp`. Hosted tools execute on xAI; local Mux tools remain ordinary function calls executed by the application. Native `shell_call` actions are available through the raw API and require caller handling and `shell_call_output` inputs; they are not automatically executed by the generic function-call loop. Model/account availability and provider tool charges apply to explicitly enabled tools.

**Native Responses resources and rich inputs.** These methods preserve complete provider JSON, so image/file content parts, citations, hosted outputs, and encrypted continuation items are not reduced to plain text:

| Method | Result / behavior |
|---|---|
| `CreateResponseAsync(JsonElement input, options)` | `XAiResponse`, including `RawResponse`, text, reasoning, local calls, usage, ID, status, and `ProviderState` |
| `CreateResponseStreamingAsync(JsonElement input, options)` | Every `XAiResponseEvent` with native JSON; terminal result in `XAiStreamingResponse.Response` |
| `GetResponseAsync(id)` | Retrieve a response originally created with `Store = true` |
| `DeleteResponseAsync(id)` | Delete a stored response; inspect `Data.deleted` |
| `ListResponseInputItemsAsync(id, limit, order, after)` | A native page, including data/cursors/has_more; limit 1..100 |
| `CompactConversationAsync(input, model)` | Native compacted output; reuse `Data.output` unchanged as the next input |

Raw `input` accepts a JSON string or a nonempty array of native message/output items. Use native content parts (`input_text`, `input_image`, `input_file`) to send text, image URLs, inline files, uploaded file IDs, or public file URLs. For stateful continuation, set `Store = true` on the response to retain, then pass its `ResponseId` as `PreviousResponseId` with **only new input**, rather than resending the full history. Instructions are omitted when a previous response ID is supplied because xAI reuses the earlier instructions. No previous response ID or conversation cache is hidden inside the client.

**Embeddings and models.** `XAiEmbeddingClient` supports text and token inputs, batches of up to 128, dimensions, float/base64 vectors, preview/user settings, sorted input indices, and token usage. Choose an accessible model from `XAiModelClient.ListEmbeddingModelsAsync()`; the library deliberately does not guess an embedding model. Token-input methods are `EmbedTokensAsync` and `EmbedTokenBatchAsync`. General and detailed model catalogs are exposed by `ListModelsAsync`, `ListLanguageModelsAsync`, `ListEmbeddingModelsAsync`, `ListImageGenerationModelsAsync`, and `ListVideoGenerationModelsAsync`. Use `GetCatalogModelInformationAsync(catalog, model)` for detailed metadata; nested provider values are retained as JSON strings in `ModelInformation.Metadata`.

The integration covers PolyPrompt's completion, embedding, and model capabilities plus the native Responses resource operations above. Standalone media/voice generation, file/collection management, and account/billing APIs are outside these capability interfaces. Image generation is available through the native Responses tool where the model supports it.

**Console and validation.** Run `dotnet run --project src/XAiConsole -f net10.0` with `XAI_API_KEY` set, or enter the key at startup. The console inherits automatic Mux execution, persistent history, separate thinking/answer rendering, usage spacing, cancellation, working-directory commands, and reset. `catalog language|embedding|image|video` lists specialized models; leave the initial embedding model empty to skip embedding setup. Optional `XAI_HOSTED_TOOLS_FILE` points to a JSON array of tool definitions, for example:

```json
[
  { "type": "web_search", "parameters": { "allowed_domains": ["wikipedia.org"] } },
  { "type": "x_search", "parameters": { "allowed_x_handles": ["SpaceX"] } }
]
```

`AotConsole --provider xai` uses the same native-safe client and shared tool bridge. The saved official contract subset is `postman/xai.openapi.json`. Local tests cover request mapping, auth, usage, encrypted-state persistence, fragmented/parallel function calls, native SSE framing and failures, hosted outputs, embeddings, model catalogs, response resources, and AOT.

For the end-to-end model/tool simulation, start `postman simulation run postman/simulations/xai.sim.yaml --port 4912 --no-history`, then set `POLYPROMPT_XAI_SIMULATION=http://127.0.0.1:4912/xai` when running `dotnet test src/Test.Console -c Release`. These tests run actual Mux directory/file operations and replay encrypted state through the native Responses contract. They are local fixture evidence, not live-provider validation. Live suite configuration accepts `--xai-key`, `--xai-model`, and an optional `--xai-embedding-model`, or the `POLYPROMPT_TEST_XAI_*` environment group.

### Ollama Cloud

OllamaCloud uses the native Ollama API, fixes the read-only endpoint to `https://ollama.com:443`, and requires a bearer token. It inherits the same operations and `OllamaCompletionOptions` / `OllamaEmbeddingOptions` as Ollama. There is no API-type or endpoint override.

```csharp
using PolyPrompt.Clients;

using OllamaCloudCompletionClient client = new OllamaCloudCompletionClient(
    Environment.GetEnvironmentVariable("OLLAMA_API_KEY")!);
client.Model = "gpt-oss:20b";
var response = await client.ChatAsync("Say hello.");
Console.WriteLine(response.Text);
```

`OllamaCloudEmbeddingClient` and `OllamaCloudModelClient` take the same required token. Available models and operations depend on the hosted service; local defaults, embeddings, pull, or delete may not be supported in the cloud. Choose a model returned by the cloud model listing.

Run `dotnet run --project src/OllamaCloudConsole -f net10.0` for the interactive harness. Set `OLLAMA_API_KEY` or enter the bearer token when prompted. The harness offers the same commands as OllamaConsole.

The local test suite checks token validation, the fixed HTTPS destination, bearer headers, and native API operations. To repeat those checks through the saved Postman smoke simulation, start `postman simulation run postman/simulations/ollamacloud.sim.yaml --port 4911 --no-history` and run:

```bash
POLYPROMPT_OLLAMACLOUD_SIMULATION=http://127.0.0.1:4911/ollamacloud \
  dotnet run --project src/Test.Automated -c Release -f net10.0 -- selftest
```

The simulation uses local response fixtures from the native Ollama response shapes; it does not establish hosted model or capability availability.


### Ollama

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

using OllamaCompletionClient client = new OllamaCompletionClient("http://localhost:11434");
client.Model = "gemma3:4b";

ChatResponse response = await client.ChatAsync("What is the capital of France?");
Console.WriteLine(response.Text);
```

### OpenAI

```csharp
using OpenAiCompletionClient client = new OpenAiCompletionClient("https://api.openai.com", "sk-your-api-key");
client.Model = "gpt-4o";

ChatResponse response = await client.ChatAsync("What is the capital of France?");
Console.WriteLine(response.Text);
```

OpenAI-compatible endpoints may be given as the API root or as a versioned `/v1` base URL. For example, Ollama's OpenAI-compatible API:

```csharp
using OpenAiCompletionClient client = new OpenAiCompletionClient("http://localhost:11434/v1");
client.Model = "gpt-oss:20b";
```

### Gemini

```csharp
using GeminiCompletionClient client = new GeminiCompletionClient(apiKey: "your-api-key");
client.Model = "gemini-2.5-flash";

ChatResponse response = await client.ChatAsync("What is the capital of France?");
Console.WriteLine(response.Text);
```

The key is sent in the `x-goog-api-key` header, never in the URL.

### Anthropic

```csharp
using AnthropicCompletionClient client = new AnthropicCompletionClient(apiKey: "sk-ant-your-api-key");
client.Model = "claude-opus-4-8";

ChatResponse response = await client.ChatAsync("What is the capital of France?");
Console.WriteLine(response.Text);
```

Anthropic authenticates with the `x-api-key` and `anthropic-version` headers, both set automatically. `AnthropicVersion` (default `2023-06-01`) and `WorkspaceId` (the `anthropic-workspace-id` header, required for identity-linked keys) exist on both `AnthropicCompletionClient` and `AnthropicModelClient`.

### Cohere

Cohere serves chat, embeddings, reranking, and classification from different model families, so each is its own client with its own default model:

```csharp
using PolyPrompt.Options;

using CohereCompletionClient chat = new CohereCompletionClient(apiKey: "your-cohere-key");        // command-a-03-2025
using CohereEmbeddingClient embed = new CohereEmbeddingClient(apiKey: "your-cohere-key");         // embed-v4.0
using CohereRerankClient rerank = new CohereRerankClient(apiKey: "your-cohere-key");              // rerank-v3.5

ChatResponse reply = await chat.ChatAsync("What is the capital of France?");

CohereEmbeddingOptions options = new CohereEmbeddingOptions { InputType = "search_query" };
EmbeddingResponse vector = await embed.EmbedAsync("capital of France", options);
```

Chat, tool chat, and streaming use the v2 Chat API; text generation is sent as a single-turn v2 chat because Cohere retired its generate endpoint. Cohere requires `input_type` for its v3 and later embedding models, so `search_document` is sent when you do not set one.

### VoyageAI

```csharp
using VoyageAiEmbeddingClient client = new VoyageAiEmbeddingClient(apiKey: "pa-your-api-key");   // voyage-3.5

VoyageAiEmbeddingOptions options = new VoyageAiEmbeddingOptions();
options.InputType = "document";      // or "query" at retrieval time
options.OutputDimension = 1024;      // Matryoshka dimensions: 256, 512, 1024, 2048

EmbeddingResponse response = await client.EmbedAsync("The quick brown fox.", options);
Console.WriteLine("Dimensions: " + response.Embeddings[0].Embedding.Length);
```

VoyageAI has embeddings and reranking only (`VoyageAiRerankClient`, default `rerank-2.5`), and no model listing API.

### Hugging Face Text Embeddings Inference (TEI)

[Text Embeddings Inference](https://github.com/huggingface/text-embeddings-inference) is a self-hosted server for embedding, reranker, and classifier models. Each server hosts exactly one model:

```bash
docker run -p 8080:80 ghcr.io/huggingface/text-embeddings-inference:cpu-latest --model-id BAAI/bge-small-en-v1.5
```

```csharp
using TeiEmbeddingClient embed = new TeiEmbeddingClient("http://localhost:8080");   // API key only if the server uses --api-key
using TeiModelClient models = new TeiModelClient("http://localhost:8080");

EmbeddingResponse embedding = await embed.EmbedAsync("The quick brown fox.");

await foreach (ModelInformation model in models.ListModelsAsync())
{
    Console.WriteLine(model.Name + " (" + model.Metadata["model_type"] + ")");   // embedding, reranker, or classifier
}
```

The TEI clients cover dense embeddings (`/embed`), sparse embeddings (`/embed_sparse`), reranking (`/rerank`), classification (`/predict`), model information (`/info`), and connectivity (`/health`). The server decides the model, so the TEI clients need no model name. Which operations succeed depends on the hosted model: calling `EmbedAsync` against a reranker returns an unsuccessful response carrying TEI's HTTP 424 error. Chat and generation belong to Hugging Face Text Generation Inference, which is OpenAI-compatible and works with `OpenAiCompletionClient`.

### Azure OpenAI

Azure OpenAI is wire-compatible with OpenAI; the model **is** the deployment name, and requests carry an `api-version`.

```csharp
using AzureOpenAiCompletionClient chat = new AzureOpenAiCompletionClient(
    "https://my-resource.openai.azure.com",   // resource endpoint
    "gpt-4o",                                  // chat deployment
    "your-azure-api-key",
    apiVersion: "2024-10-21");                 // optional; default AzureOpenAiDefaults.ApiVersion

using AzureOpenAiEmbeddingClient embed = new AzureOpenAiEmbeddingClient(
    "https://my-resource.openai.azure.com", "text-embedding-3-small", "your-azure-api-key");

ChatResponse response = await chat.ChatAsync("Hello from Azure!");

// A per-call model routes that call to another deployment.
ChatResponse mini = await chat.ChatAsync("Hi", new CompletionOptions { Model = "gpt-4o-mini" });

// Azure AD (Entra ID) instead of an api-key:
// new AzureOpenAiCompletionClient(endpoint, "gpt-4o", new StaticTokenCredential(token));
```

### Google Vertex AI

Vertex AI serves Gemini models under a project and region, and authenticates with a short-lived OAuth token refreshed automatically.

```csharp
using PolyPrompt.Auth;

// Application Default Credentials (GOOGLE_APPLICATION_CREDENTIALS key file, or a GCE/Cloud Run metadata token).
ICredentialProvider credential = new AdcCredential();
// or: ServiceAccountCredential.FromJson(File.ReadAllText("sa.json"))

using VertexAiCompletionClient chat = new VertexAiCompletionClient("my-gcp-project", "us-central1", credential);
chat.Model = "gemini-2.5-flash";
ChatResponse response = await chat.ChatAsync("Hello from Vertex!");

using VertexAiEmbeddingClient embed = new VertexAiEmbeddingClient("my-gcp-project", "us-central1", credential);   // text-embedding-005
EmbeddingResponse vectors = await embed.EmbedAsync(new List<string> { "one", "two" });
```

### AWS Bedrock

Bedrock uses the Converse API for chat and InvokeModel for embeddings and reranking, and signs every request with AWS Signature V4.

```csharp
using PolyPrompt.Auth;

IAwsCredentialProvider aws = new StaticAwsCredential("AKIA...", "secret...", "us-east-1");
// or: new EnvironmentAwsCredential()   (AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY, AWS_SESSION_TOKEN, AWS_REGION)

using BedrockCompletionClient chat = new BedrockCompletionClient(aws, "us-east-1");
chat.Model = "anthropic.claude-3-5-sonnet-20240620-v1:0";
ChatResponse response = await chat.ChatAsync("Hello from Bedrock!");

using BedrockEmbeddingClient embed = new BedrockEmbeddingClient(aws, "us-east-1");   // amazon.titan-embed-text-v2:0, or a Cohere model
using BedrockRerankClient rerank = new BedrockRerankClient(aws, "us-east-1");        // cohere.rerank-v3-5:0, or amazon.rerank-v1:0
```

Bedrock constructors take `(credentialProvider, region, endpoint = null, logging = null, httpClient = null)`.

### TypeSafe decision models (Jev, Ollaya)

```csharp
using TypeSafeDecisionClient jev = new TypeSafeDecisionClient(apiKey: "your-typesafe-key");   // jev-latest
// Local Ollaya server: new TypeSafeDecisionClient("http://localhost:<port>")

DecisionResponse answer = await jev.DecideAsync(new DecisionRequest
{
    State = "I was charged twice for my order and need the money back today.",
    Questions =
    {
        DecisionQuestion.Choice("intent", "What does the customer want?", "refund", "billing", "other"),
        DecisionQuestion.Binary("urgent", "Does the customer need a response today?"),
        DecisionQuestion.Score("tone", "How upset is the customer?", "calm", "annoyed", "angry", "furious")
    }
});

ChoiceAnswer intent = answer.Choice("intent");
if (intent.Confidence >= 0.9) Console.WriteLine("Route to " + intent.Value);
Console.WriteLine("Urgent: " + answer.Binary("urgent").Probability);
Console.WriteLine("Tone: " + answer.Score("tone").LevelText);
```

See [Decision Models](#decision-models) for details.

## Authentication

Most providers take one static credential in the constructor: a bearer key for OpenAI, Ollama, VoyageAI, Cohere, TypeSafe, and TEI servers started with `--api-key`; an `x-api-key` for Anthropic; and an `x-goog-api-key` for Gemini. Azure OpenAI, Vertex AI, and Bedrock need richer, per-request credentials, implemented in-library under `PolyPrompt.Auth` with no provider SDK:

- **Azure OpenAI**: an `api-key` header (pass the key string) or an Azure AD bearer token (pass an `ICredentialProvider`, such as `new StaticTokenCredential(token)`).
- **Vertex AI**: a short-lived OAuth token from an `ICredentialProvider`: `AdcCredential` (Application Default Credentials: the `GOOGLE_APPLICATION_CREDENTIALS` key file or the GCE/Cloud Run metadata server), `ServiceAccountCredential.FromJson(...)` (an RS256 JWT assertion exchanged for a token), or `StaticTokenCredential` (for example from `gcloud auth print-access-token`). Tokens are cached and refreshed ahead of expiry.
- **AWS Bedrock**: Signature Version 4 on every request from an `IAwsCredentialProvider`: `StaticAwsCredential(accessKey, secretKey, region, sessionToken?)` or `EnvironmentAwsCredential()`. Temporary credentials with a session token are supported.

Every client attaches its credentials to each request in the `PrepareRequestAsync` hook, so credentials never leak between clients that share an `HttpClient`.

## Detailed Examples

### Chat with a System Prompt

```csharp
using OllamaCompletionClient client = new OllamaCompletionClient("http://localhost:11434");
client.Model = "gemma3:4b";
client.Defaults.SystemPrompt = "You are a helpful assistant that responds in haiku format.";
client.Defaults.Temperature = 0.7;
client.Defaults.MaxTokens = 256;

ChatResponse response = await client.ChatAsync("Tell me about the ocean.");
if (response.Success)
{
    Console.WriteLine(response.Text);
    Console.WriteLine("Runtime: " + response.OverallRuntimeMs + " ms");
}
else
{
    Console.WriteLine("Error " + response.StatusCode + ": " + response.Error);
}
```

`Defaults.SystemPrompt` applies to chat and tool chat. In tool chat it is prepended as a system message only when the request's messages contain none, so a conversation that already carries one is sent as-is.

### Chat with Provider-Specific Options

```csharp
using OllamaCompletionClient client = new OllamaCompletionClient("http://localhost:11434");
client.Model = "gemma3:4b";

OllamaCompletionOptions options = new OllamaCompletionOptions();
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

A client ignores options that belong to another provider; pass a base `CompletionOptions` in provider-agnostic code.

### Tool Calling

Tool calling is explicit. Use `ToolChatAsync` or `ToolChatStreamingAsync` when a model may request application functions, run those functions in your code, and send the results back as messages. PolyPrompt normalizes the provider protocol; it does not run your tools.

```csharp
using OpenAiCompletionClient client = new OpenAiCompletionClient("https://api.openai.com", "sk-your-api-key");
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
                { "city", new Dictionary<string, object> { { "type", "string" }, { "description", "City name." } } },
                { "unit", new Dictionary<string, object> { { "type", "string" }, { "enum", new List<string> { "fahrenheit", "celsius" } } } }
            }
        },
        { "required", new List<string> { "city" } }
    }));
request.Options = new CompletionOptions { Temperature = 0 };   // optional per-request settings

ToolChatResponse first = await client.ToolChatAsync(request);

if (first.ToolCalls.Count > 0)
{
    request.Messages.Add(first.ToAssistantMessage());

    foreach (ToolCall call in first.ToolCalls)
    {
        string weatherJson = "{\"temperature\":72,\"conditions\":\"clear\"}";
        request.Messages.Add(ChatMessage.ToolResult(call.Id, call.Name, weatherJson));
    }
}

ToolChatResponse final = await client.ToolChatAsync(request);
Console.WriteLine(final.Text);
```

Always give a tool result its tool name; `ChatMessage.ToolResult` does. Gemini requires it (see [Gemini Tool Calling Notes](#gemini-tool-calling-notes)).

### Streaming Tool Calling

`ToolChatStreamingAsync` streams assistant text and tool-call deltas and accumulates the final `Text` and `ToolCalls` on the response as you enumerate `Chunks`.

```csharp
ToolChatStreamingResponse stream = await client.ToolChatStreamingAsync(request);

await foreach (ToolChatStreamingChunk chunk in stream.Chunks)
{
    if (!string.IsNullOrEmpty(chunk.Text)) Console.Write(chunk.Text);
}

if (stream.ToolCalls.Count > 0)
{
    request.Messages.Add(stream.ToAssistantMessage());
    foreach (ToolCall call in stream.ToolCalls)
    {
        request.Messages.Add(ChatMessage.ToolResult(call.Id, call.Name, "{\"temperature\":72}"));
    }
}
```

Provider protocol shapes differ:

- **OpenAI-compatible** streams `/v1/chat/completions` SSE chunks and assembles `delta.tool_calls` argument fragments.
- **Ollama** streams `/api/chat` newline-delimited JSON and reads `message.tool_calls`.
- **Gemini** uses `models/{model}:streamGenerateContent?alt=sse` with the same body as `ToolChatAsync` (`contents`, `systemInstruction`, `tools.functionDeclarations`, `toolConfig`) and reads complete `functionCall` parts, `finishReason`, `responseId`, `modelVersion`, and `usageMetadata`.
- **Anthropic** streams `/v1/messages` event-typed SSE: `message_start`, `content_block_start` for `text`, `thinking`, and `tool_use`, `content_block_delta` with `text_delta`, `thinking_delta`, and `input_json_delta`, and `message_delta`. Tool results are sent as user-role `tool_result` blocks, and consecutive results merge into one user turn.
- **Bedrock** streams ConverseStream over the AWS binary event-stream.
- **Cohere** streams v2 Chat SSE events, including `tool-call-start` and `tool-call-delta`.

### Gemini Tool Calling Notes

These rules apply to `GeminiCompletionClient` and `VertexAiCompletionClient`. The thought-signature rule also applies to `OpenAiCompletionClient` pointed at Gemini's OpenAI-compatible endpoint (`https://generativelanguage.googleapis.com/v1beta/openai/`).

- **Thought signatures.** Gemini 3 attaches an opaque `thoughtSignature` to each function call and rejects the follow-up request (HTTP 400, "Function call is missing a thought_signature") if a replayed call lost it. PolyPrompt captures it into `ToolCall.ThoughtSignature` (streaming and non-streaming) and sends it back unchanged; `ToAssistantMessage()` keeps it.
- **If you persist conversations, persist `ToolCall.ThoughtSignature` too**, or the next Gemini 3 turn will fail.
- **History without signatures.** When a replayed assistant turn has no signature at all (from another provider or built by hand), the Gemini clients put Google's documented `skip_thought_signature_validator` placeholder on the turn's first function call. Signed turns are sent exactly as received, and signatures are never invented. `OpenAiCompletionClient` never adds the placeholder and sends `extra_content` only when a signature exists.
- **Tool result names.** Gemini requires the function name on every tool result. When `ChatMessage.ToolName` is missing, the name is taken from the assistant tool call with the same `ToolCallId` earlier in the conversation. If there is no such call, `ToolChatAsync` throws `ArgumentException` before sending, naming the message and the id. Set `ToolName` anyway; other callers of your history may need it.
- **Tool results.** Results are sent as `functionResponse` parts in a `user` turn (Gemini accepts only `user` and `model` roles), and consecutive results merge into one turn. `functionResponse.id` carries the call id when Gemini issued one. A JSON object is sent as the response; an array or scalar is wrapped as `{"result": <value>}`; empty or non-JSON text is wrapped as `{"result": "<text>"}`. Tool-call arguments that are not a JSON object are sent as `{}`.
- **Tool schemas.** By default (`GeminiCompletionOptions.ToolSchemaMode = GeminiToolSchemaMode.JsonSchema`) schemas go in `functionDeclarations[].parametersJsonSchema`, which accepts standard JSON Schema; only a root `$schema` keyword is removed. For endpoints that only accept `parameters`, set `ToolSchemaMode = GeminiToolSchemaMode.OpenApiSubset` in `Defaults` or per call; the schema is then reduced to the OpenAPI subset (unsupported keys are removed and logged, `"type": ["string", "null"]` becomes `"nullable": true`, `const` becomes a one-value `enum`, and `oneOf` becomes `anyOf`).

```csharp
using GeminiCompletionClient gemini = new GeminiCompletionClient(apiKey: "your-google-api-key");
gemini.Model = "gemini-3.5-flash";
// gemini.Defaults.ToolSchemaMode = GeminiToolSchemaMode.OpenApiSubset;   // only for endpoints without parametersJsonSchema

ToolChatResponse turn = await gemini.ToolChatAsync(request);
request.Messages.Add(turn.ToAssistantMessage());   // keeps ToolCall.ThoughtSignature

foreach (ToolCall call in turn.ToolCalls)
{
    request.Messages.Add(ChatMessage.ToolResult(call.Id, call.Name, "[{\"city\":\"Seattle\",\"temperature\":72}]"));
}

ToolChatResponse answer = await gemini.ToolChatAsync(request);
```

### Reasoning Effort

Reasoning-capable models trade latency and cost against depth of reasoning. `ReasoningEffort` is a provider-neutral value: a semantic `ReasoningEffortLevel` (`Minimal`, `Low`, `Medium`, `High`) with per-provider defaults that PolyPrompt projects onto each provider's parameter. Set it in `Defaults.ReasoningEffort` for every chat and tool chat call, or per call in `CompletionOptions.ReasoningEffort` (for tool chat, `ToolChatRequest.Options.ReasoningEffort`). When nothing sets it, no reasoning field is sent.

```csharp
// Client-wide default for chat and tool chat.
client.Defaults.ReasoningEffort = ReasoningEffort.Medium;

// One tool chat at high effort.
ToolChatRequest request = new ToolChatRequest { Options = new CompletionOptions { ReasoningEffort = ReasoningEffort.High } };

// Keep the semantic level, override one provider's parameter.
request.Options.ReasoningEffort = new ReasoningEffort(ReasoningEffortLevel.High) { GeminiThinkingBudget = 16000 };

// Turn thinking off for a quick structured answer.
ChatResponse quick = await client.ChatAsync("Rate each passage 0 to 10 as JSON.",
    new CompletionOptions { Temperature = 0, ReasoningEffort = ReasoningEffort.Minimal });
```

| `ReasoningEffortLevel` | OpenAI `reasoning_effort` | Gemini `thinkingBudget` | Ollama `think` | Anthropic `output_config.effort` + `thinking` | Cohere `thinking` | Bedrock thinking budget |
|---|---|---|---|---|---|---|
| `Minimal` | `"minimal"` | `0` (off) | `false` | `"low"`, no thinking field | disabled | off |
| `Low` | `"low"` | `1024` | `"low"` | `"low"` + adaptive thinking | 1024 | 1024 |
| `Medium` | `"medium"` | `8192` | `"medium"` | `"medium"` + adaptive thinking | 4096 | 4096 |
| `High` | `"high"` | `-1` (dynamic) | `"high"` | `"high"` + adaptive thinking | 16384 | 16384 |
| _unset_ | omitted | omitted | omitted | omitted | omitted | omitted |

Overrides live on the value object and clamp or validate their input: `OpenAiValue`, `GeminiThinkingBudget` (-1 to 32768), `OllamaThink`, `AnthropicEffort` (`low`, `medium`, `high`, `xhigh`, `max`; the last two are reachable only through the override), `CohereThinkingBudget`, and `BedrockThinkingBudget`. An unrecognized string override reverts to null and falls back to the level default. Ollama support depends on the model (for example `gpt-oss`).

### Reasoning Output

A reasoning model emits its deliberation on a separate channel: OpenAI `reasoning_content`, Ollama `message.thinking`, Gemini `thought` parts, Anthropic `thinking` blocks, Bedrock `reasoningContent`, and Cohere thinking content and tool plans. PolyPrompt keeps it out of `Text`. Streamed chunks carry a `ReasoningText` delta, and responses carry the accumulated `Reasoning`. Both are null when there is none.

```csharp
ToolChatStreamingResponse stream = await client.ToolChatStreamingAsync(request);
await foreach (ToolChatStreamingChunk chunk in stream.Chunks)
{
    if (chunk.ReasoningText != null) Console.Write(chunk.ReasoningText);   // the thinking
    if (chunk.Text != null) Console.Write(chunk.Text);                     // the answer
}
```

Use `ToAssistantMessage()` for tool follow-ups. It carries native continuation state in the existing `ProviderState` property while keeping displayable `Reasoning` separate from `Content`. Fully enumerate streaming responses before converting them. Preserve the returned assistant message unchanged, including its state, text, and tool calls; copying only text and tool calls loses signed or encrypted context. Persist conversations with `PolyPromptJsonContext.Default.ListChatMessage` (or `.ChatMessage` for a single turn), including in Native AOT applications.

| Protocol / clients | State preserved for the assistant turn |
|---|---|
| Ollama / Ollama Cloud | Original `thinking`, including whitespace; tool results also include native `tool_name` |
| OpenAI-compatible / Azure OpenAI | Returned `reasoning_content` and `reasoning` extensions; existing Gemini-compatible tool signatures remain supported |
| Anthropic | Ordered content blocks, including thinking signatures and `redacted_thinking` |
| Bedrock Converse | Ordered content blocks, signed reasoning text, and binary redacted reasoning |
| Gemini / Vertex AI | Native parts with thought state or signatures, including signatures on text parts |
| Cohere | Thinking content blocks and tool plans |
| xAI | Existing complete Responses output array, including encrypted reasoning and hosted-tool state |

Native state is tagged by protocol (xAI retains its existing array format). An incompatible provider ignores it; reasoning is never copied into visible assistant text. OpenAI reasoning extensions are replayed only to the configured endpoint that returned them; moving a conversation to another OpenAI-compatible endpoint omits those extensions. Responses without reasoning do not acquire another turn's thinking.

The continuation rules follow the native [Ollama tool loop](https://docs.ollama.com/capabilities/tool-calling), [Anthropic thinking preservation](https://platform.claude.com/docs/en/build-with-claude/thinking), [Bedrock Converse signatures](https://docs.aws.amazon.com/bedrock/latest/userguide/conversation-inference.html), [Gemini thought signatures](https://ai.google.dev/gemini-api/docs/thought-signatures), [DeepSeek thinking-mode tool calls](https://api-docs.deepseek.com/guides/thinking_mode/), and [Cohere tool-loop state management](https://docs.cohere.com/docs/tool-use-usage-patterns/). Embedding, reranking, classification, model discovery, and TypeSafe decision APIs have no assistant tool-turn replay path.

Local regressions inspect the second and third HTTP requests across all nine chat client variants, with parallel tools, fragmented thinking/signatures, redacted blocks, JSON persistence, turns without thinking, reasoning without tool calls, and cross-protocol isolation including xAI. Native AOT runs the same round trips and compares request digests against the JIT. The saved Ollama Postman console simulation rejects follow-ups missing thinking or tool-result names. These are deterministic local protocol checks; they do not establish a cause for Armada's observed latency or mission failures.

### Token Usage

Provider-reported usage is a `TokenUsage` on `Usage` of `ChatResponse`, `ChatStreamingResponse`, `ToolChatResponse`, `ToolChatStreamingResponse`, and `DecisionResponse`. `Usage` is null when the provider reports none, and each field is nullable so "not reported" differs from zero. Besides `PromptTokens`, `CompletionTokens`, and `TotalTokens`:

- **`CachedPromptTokens`**: prompt tokens read from the provider's cache.
- **`CacheCreationTokens`**: prompt tokens written to the cache.
- **`ReasoningTokens`**: tokens billed separately for reasoning.

```csharp
ChatResponse response = await client.ChatAsync("Summarize the attached document.");
TokenUsage? usage = response.Usage;
if (usage != null)
{
    Console.WriteLine($"Prompt: {usage.PromptTokens}, cached: {usage.CachedPromptTokens}, " +
                      $"completion: {usage.CompletionTokens}, reasoning: {usage.ReasoningTokens}");
}
```

| Provider | `CachedPromptTokens` | `CacheCreationTokens` | `ReasoningTokens` | Cached vs `PromptTokens` |
|---|---|---|---|---|
| OpenAI / Azure OpenAI | yes | no | yes | included in `PromptTokens` |
| Gemini / Vertex AI | yes | no | yes | included in `PromptTokens` |
| Anthropic | yes | yes | no (billed as output) | in addition to `PromptTokens` |
| Bedrock | yes | yes | no (billed as output) | in addition to `PromptTokens` |
| Cohere | yes | no | no (billed as output) | included in `PromptTokens` |
| Ollama | no | no | no | n/a |

On Anthropic and Bedrock, full input is `PromptTokens + CachedPromptTokens + CacheCreationTokens`.

### Streaming Chat

```csharp
using OpenAiCompletionClient client = new OpenAiCompletionClient("https://api.openai.com", "sk-your-api-key");
client.Model = "gpt-4o";

ChatStreamingResponse stream = await client.ChatStreamingAsync("Write a short story about a robot.");

await foreach (ChatStreamingChunk chunk in stream.Chunks)
{
    if (!string.IsNullOrEmpty(chunk.Text)) Console.Write(chunk.Text);
}

Console.WriteLine();
Console.WriteLine("Time to first token: " + stream.TimeToFirstTokenMs + " ms");
Console.WriteLine("Tokens/sec: " + stream.OverallTokensPerSecond.ToString("F1"));
Console.WriteLine("Total chunks: " + stream.ChunkCount);
```

### Embeddings

`EmbedAsync` has a single-input and a batch overload. The batch overload returns one vector per input, in input order (`EmbeddingResult.Index`).

```csharp
using OpenAiEmbeddingClient client = new OpenAiEmbeddingClient("https://api.openai.com", "sk-your-api-key");   // text-embedding-3-small

EmbeddingResponse single = await client.EmbedAsync("The quick brown fox jumps over the lazy dog.");
float[] vector = single.Embeddings[0].Embedding;

List<string> documents = new List<string>
{
    "Machine learning is a subset of artificial intelligence.",
    "Neural networks are inspired by biological neurons.",
    "Deep learning uses multiple layers of neural networks."
};

EmbeddingResponse batch = await client.EmbedAsync(documents, new OpenAiEmbeddingOptions { Dimensions = 256 });
for (int i = 0; i < batch.Embeddings.Count; i++)
{
    Console.WriteLine("Document " + i + ": " + batch.Embeddings[i].Embedding.Length + " dimensions");
}
```

`OpenAiEmbeddingOptions.EncodingFormat = "base64"` requests base64 vectors, which are decoded to floats. Providers without a native batch endpoint (Bedrock Titan) send one request per input and assemble the results in order.

### Reranking

`RerankAsync` scores each document against a query and returns the results sorted by score, highest first. Each result's `Index` points into the list you passed.

```csharp
RerankClientBase client = new CohereRerankClient(apiKey: "your-cohere-key");
// or: new TeiRerankClient("http://localhost:8080")   (a server hosting a reranker such as BAAI/bge-reranker-base)
// or: new VoyageAiRerankClient(apiKey: "pa-your-key")
// or: new BedrockRerankClient(new EnvironmentAwsCredential(), "us-east-1")

List<string> documents = new List<string>
{
    "The Great Wall of China is thousands of kilometers long.",
    "Photosynthesis converts light into chemical energy in plants.",
    "Paris is the capital and largest city of France."
};

RerankResponse response = await client.RerankAsync("What is the capital of France?", documents,
    new RerankOptions { TopN = 2, ReturnDocuments = true });

foreach (RerankResult result in response.Results)
{
    Console.WriteLine("[" + result.Index + "] " + result.Score.ToString("F4") + " " + result.Document);
}
```

- **Invalid arguments throw** before any request: a null query or document list throws `ArgumentNullException`; an empty or whitespace query, an empty list, or a null document throws `ArgumentException`; and a per-call `TopN` larger than the number of documents throws `ArgumentOutOfRangeException`. A `Defaults.TopN` larger than the number of documents is capped instead, so one default works for any batch size.
- **Scores are provider-specific.** Cohere, VoyageAI, and Bedrock return a normalized 0..1 relevance; TEI returns a sigmoid score by default or raw logits with `TeiRerankOptions.RawScores = true`. Compare scores within one provider.
- **TopN** is sent natively where supported (`top_n` on Cohere and Bedrock, `top_k` on VoyageAI); TEI scores every document and the list is trimmed client-side.
- **Document text** is attached from your own list by index, so `ReturnDocuments` never requests extra data.
- **Usage.** `RerankResponse.TotalTokens` is populated by Cohere and VoyageAI; `SearchUnits` by Cohere.

### Classification

`ClassifyAsync` returns one result per input, in input order, with the top label and every scored label (highest first):

```csharp
// TEI: the server hosts a sequence classification model (or a reranker, which scores a single label).
using TeiClassificationClient tei = new TeiClassificationClient("http://localhost:8080");
ClassificationResponse teiResult = await tei.ClassifyAsync(new List<string> { "I love this!", "This is terrible." });

// Cohere: few-shot examples (at least 2 per label), or set Model to a fine-tuned classifier.
using CohereClassificationClient cohere = new CohereClassificationClient(apiKey: "your-cohere-key");
cohere.Defaults.Examples.Add(new ClassificationExample("I love it", "positive"));
cohere.Defaults.Examples.Add(new ClassificationExample("This is fantastic", "positive"));
cohere.Defaults.Examples.Add(new ClassificationExample("I hate it", "negative"));
cohere.Defaults.Examples.Add(new ClassificationExample("This is awful", "negative"));

ClassificationResponse cohereResult = await cohere.ClassifyAsync("The service was great");
Console.WriteLine(cohereResult.Classifications[0].Label + " " + cohereResult.Classifications[0].Score);
```

Examples set in `Defaults` are used when the call passes none. TEI inputs are always sent in batch form, so two inputs are never read as one text pair.

### Sparse Embeddings (TEI)

A TEI server hosting a SPLADE-style model returns sparse vectors:

```csharp
using TeiSparseEmbeddingClient client = new TeiSparseEmbeddingClient("http://localhost:8080");
SparseEmbeddingResponse response = await client.EmbedSparseAsync(new List<string> { "sparse retrieval" });
foreach (SparseValue value in response.Embeddings[0].Values)
{
    Console.WriteLine(value.Index + ": " + value.Value);
}
```

### Decision Models

A decision model reads a state and answers typed questions about it with calibrated probabilities. Nothing is generated, so answers are fast, cheap, and always well-formed.

| Question | Factory | Answer | Answer fields |
|---|---|---|---|
| Yes/no | `DecisionQuestion.Binary(id, instructions, trueCriterion?, falseCriterion?)` | `BinaryAnswer` | `Probability` (of true) |
| Pick one | `DecisionQuestion.Choice(id, instructions, "a", "b", ...)` or with `DecisionOption.Of(value, description)` | `ChoiceAnswer` | `Value`, `Probability` (of the chosen value) |
| Rate on a rubric | `DecisionQuestion.Score(id, instructions, "level 0", "level 1", ...)` | `ScoreAnswer` | `Value` (may be fractional), `Level` (nearest level index), `LevelText`, `Legend` |

Every answer also has `Confidence` (calibrated by the provider, 0 to 1) and `Probabilities` (the full distribution). `DecisionResponse.Choice(id)`, `Binary(id)`, and `Score(id)` return typed answers; a missing id throws `KeyNotFoundException` and the wrong type throws `InvalidOperationException`.

```csharp
DecisionRequest request = new DecisionRequest
{
    State = ticketText,   // text, or a structured object sent as JSON
    Questions =
    {
        DecisionQuestion.Choice("intent", "What does the customer want?",
            DecisionOption.Of("refund", "Wants money back"),
            DecisionOption.Of("billing", "A question about a charge or an invoice"),
            DecisionOption.Of("other")),
        DecisionQuestion.Binary("urgent", "Does the customer need a response today?"),
        DecisionQuestion.Score("tone", "How upset is the customer?", "calm", "annoyed", "angry", "furious")
    }
};

DecisionResponse response = await jev.DecideAsync(request);
if (response.Success)
{
    ChoiceAnswer intent = response.Choice("intent");
    Console.WriteLine(intent.Value + " (p=" + intent.Probability + ", confidence=" + intent.Confidence + ")");
}

// A batch returns one response per request, in input order, sending at most MaxConcurrency (default 4) at once.
jev.MaxConcurrency = 8;
List<DecisionResponse> responses = await jev.DecideAsync(tickets.Select(t => new DecisionRequest { State = t, Questions = request.Questions }).ToList());
```

Requests are validated before sending: a state, at least one question, unique non-empty ids, non-empty instructions, 2 to 255 unique choice options, and 2 to 10 score levels. A batch is validated in full before any request is sent, and a failed request in a batch fails only its own response. Question text is a `DecisionContent`, which converts implicitly from `string`.

`TypeSafeDecisionClient` speaks the TypeSafe System One API (`POST /v1/systemone`), served by TypeSafe's hosted Jev models and by Ollaya, the local runtime for open decision models. Binary questions are TypeSafe `noul` questions. HTTP 422 (validation), 429 (rate limit), and 529 (overloaded) are reported on the response.

### Text Generation

```csharp
using OllamaCompletionClient client = new OllamaCompletionClient("http://localhost:11434");
client.Model = "gemma3:4b";

GenerationResponse response = await client.GenerateAsync("Once upon a time, in a land far away,");
Console.WriteLine(response.Text);

GenerationStreamingResponse stream = await client.GenerateStreamingAsync("Write a limerick about coding.");
await foreach (GenerationStreamingChunk chunk in stream.Chunks)
{
    if (!string.IsNullOrEmpty(chunk.Text)) Console.Write(chunk.Text);
}
```

OpenAI and Azure OpenAI generation uses the legacy completions API, which current chat models do not serve; Anthropic, Bedrock, and Cohere send the prompt as a single-turn chat.

### Models

```csharp
using OllamaModelClient models = new OllamaModelClient("http://localhost:11434");

await foreach (ModelInformation model in models.ListModelsAsync())
{
    Console.WriteLine(model.Name + (model.SizeBytes.HasValue ? " [" + (model.SizeBytes.Value / 1_000_000_000.0).ToString("F1") + " GB]" : ""));
}

bool exists = await models.ModelExistsAsync("gemma3");            // also matches "gemma3:latest"
ModelInformation? info = await models.GetModelInformationAsync("gemma3:4b");

// Ollama only: pull with progress, and delete.
models.PullTimeout = TimeSpan.FromMinutes(60);
bool pulled = await models.PullModelAsync("gemma3:4b", progress =>
{
    Console.Write("\r" + progress.Status + " " + progress.PercentComplete?.ToString("F1"));
    return Task.CompletedTask;
});
bool deleted = await models.DeleteModelAsync("old-model:1b");
```

`AnthropicModelClient` and `CohereModelClient` page through their catalogs (`PageSize`, 1 to 1,000), and `GeminiModelClient` follows `nextPageToken`.

### Validate Connectivity

Every client has `ValidateConnectivityAsync`. It returns true when the provider accepted a lightweight request made with that client's endpoint and credentials, and false on an HTTP error, an unreachable server, or a timeout. Only your own cancellation is rethrown.

```csharp
bool reachable = await client.ValidateConnectivityAsync();
Console.WriteLine(reachable ? "Connected." : "Cannot reach provider.");
```

| Client | Probe |
|---|---|
| Ollama (all) | `GET /api/tags` |
| OpenAI and Azure OpenAI (all) | `GET /v1/models` (Azure: `/openai/models?api-version=`) |
| Gemini (all) | `GET /v1beta/models?pageSize=1` |
| `VertexAiCompletionClient` | `POST :countTokens` on the client's model |
| `VertexAiEmbeddingClient` | a one-word embedding |
| Anthropic (all) | `GET /v1/models?limit=1` |
| Bedrock (all) | `GET /foundation-models` on the control plane |
| Cohere (all) | `GET /v1/models` |
| VoyageAI (both) | a one-word embedding or rerank (VoyageAI has no model listing API) |
| TEI (all) | `GET /health` |
| `TypeSafeDecisionClient` | `GET /v1/models` (Ollaya); on HTTP 404 (hosted API), a one-question decision |

### Inspect Call Details

```csharp
ChatResponse response = await client.ChatAsync("Hello!");

foreach (CallDetail detail in client.CallDetails)
{
    Console.WriteLine(detail.Method + " " + detail.Url);
    Console.WriteLine("  Status: " + detail.StatusCode + ", time: " + detail.ResponseTimeMs + " ms, success: " + detail.Success);
}

// CallDetails is a detached snapshot. MaxCallDetails bounds retention (0 disables recording).
client.MaxCallDetails = 100;
client.ClearCallDetails();
```

Call details record the request headers, which include credentials; treat them as sensitive.

### Telemetry (Metrics and Traces)

Every client emits metrics and traces through the BCL `Meter` and `ActivitySource` named `PolyPrompt`. PolyPrompt takes no OpenTelemetry or exporter dependency and records nothing until your application subscribes a collector, so its cost when unused is a few boolean checks per call. Subscribe with Radiant, the OpenTelemetry SDK, or any `MeterListener` / `ActivityListener`:

```csharp
// Radiant (in your application)
RadiantSettings settings = new RadiantSettings("my-service");
settings.Sources.AddMeter(PolyPromptTelemetryNames.MeterName);           // "PolyPrompt"
settings.Sources.AddActivitySource(PolyPromptTelemetryNames.ActivitySourceName);
using RadiantHost host = RadiantHost.Start(settings);

// OpenTelemetry SDK
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter(PolyPromptTelemetryNames.MeterName))
    .WithTracing(t => t.AddSource(PolyPromptTelemetryNames.ActivitySourceName));
```

What you get:

- A client span per operation (`openai chat`, `aws.bedrock embed`, `typesafe decide_batch`) with OpenTelemetry GenAI attributes (model, max tokens, token usage, finish reason, response id), nested under your current span, plus a child span per HTTP request (`POST`) that propagates W3C `traceparent` to the provider.
- Operation duration and outcome by provider, capability, operation, and `error.type` (HTTP status, `timeout`, `invalid_response`, or exception type); the GenAI semantic-convention `gen_ai.client.operation.duration` and `gen_ai.client.token.usage`; token counters including cached, cache-write, and reasoning tokens.
- Streaming time to first chunk and chunk counts; a streaming span stays open until the stream ends and records `abandoned` if the caller stops early.
- Per-provider HTTP request duration, status codes, body sizes, and in-flight requests.
- Decision batch queue wait (a `stage:queued` span per item), queued and in-flight gauges, and questions by type.
- OAuth token refresh duration, failures, and cache hits and misses; live client counts; and build info.

Labels are bounded (no ids or prompts), and no prompts, bodies, or credentials are recorded. The `LoggingModule` from SyslogLogging 2.3+ emits its own telemetry on a separate `SyslogLogging` meter and activity source; subscribe to `SyslogLoggingTelemetry.MeterName` and `SyslogLoggingTelemetry.ActivitySourceName` as well if you want logging metrics and spans. [TELEMETRY.md](TELEMETRY.md) has the full metrics and spans catalog, PromQL alerts, and a Grafana dashboard map.

### Native AOT and Trimming

PolyPrompt works in Native AOT and trimmed applications with no setup for the common cases: every client, streaming, tool calling with dictionary-based schemas, decisions with string state, credentials, and telemetry. Set `<PublishAot>true</PublishAot>` and publish for a runtime identifier.

If PolyPrompt handles your own types (typed tool arguments, a decision `State` object, or a class inside a tool schema), give it source-generated metadata, since reflection is unavailable in a native binary:

```csharp
[JsonSerializable(typeof(WeatherArgs))]
internal partial class AppJsonContext : JsonSerializerContext { }

Serializer.AddTypeInfoResolver(AppJsonContext.Default);          // once, at startup

WeatherArgs? args = call.DeserializeArguments<WeatherArgs>();    // now works under Native AOT
WeatherArgs? same = call.DeserializeArguments(AppJsonContext.Default.WeatherArgs);   // or pass metadata explicitly
```

`PolyPromptJsonContext` covers PolyPrompt's own models, so conversations and responses can be saved without reflection:

```csharp
string json = JsonSerializer.Serialize(messages, PolyPromptJsonContext.Default.ListChatMessage);
List<ChatMessage>? restored = JsonSerializer.Deserialize(json, PolyPromptJsonContext.Default.ListChatMessage);
```

On the JIT nothing changes: types you do not register are still handled by reflection. [AOT.md](AOT.md) covers the supported value types, how registered and explicit metadata differ, errors, and verification; [src/AotConsole](src/AotConsole/Program.cs) is a complete native sample.

### Cancellation and Timeouts

```csharp
client.TimeoutMs = 10000;   // per client; default 120000; must be greater than zero

using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
try
{
    ChatResponse response = await client.ChatAsync("Write a very long essay.", token: cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Request was cancelled.");
}
```

`TimeoutMs` is enforced with per-call cancellation tokens for both non-streaming requests and streaming response bodies. A timeout throws `OperationCanceledException` (a `TaskCanceledException`), as your own cancellation does; check `token.IsCancellationRequested` to tell them apart. `ValidateConnectivityAsync` is the exception: it returns false on a timeout.

### Custom or Shared HttpClient

Every constructor accepts an optional `HttpClient`. When you supply one, PolyPrompt uses it and never disposes it. Because credentials are attached per request, one `HttpClient` can serve every client, across providers:

```csharp
HttpClientHandler handler = new HttpClientHandler
{
    // Example: trust a self-signed certificate on an internal endpoint.
    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
};
using HttpClient http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };

using OpenAiCompletionClient chat = new OpenAiCompletionClient("https://internal-llm.example.corp/v1", "sk-a", httpClient: http);
using OpenAiEmbeddingClient embed = new OpenAiEmbeddingClient("https://internal-llm.example.corp/v1", "sk-b", httpClient: http);
using AnthropicCompletionClient claude = new AnthropicCompletionClient(apiKey: "sk-ant-c", httpClient: http);
```

Give a shared `HttpClient` an infinite timeout, as above, so each client's `TimeoutMs` governs its requests.

### Provider-Agnostic Code

Hold the capability base type:

```csharp
CompletionClientBase CreateChat(string provider, string? apiKey) => provider switch
{
    "xai" => new XAiCompletionClient(apiKey!),
    "ollama" => new OllamaCompletionClient(),
    "openai" => new OpenAiCompletionClient(apiKey: apiKey),
    "gemini" => new GeminiCompletionClient(apiKey: apiKey),
    "anthropic" => new AnthropicCompletionClient(apiKey: apiKey),
    "cohere" => new CohereCompletionClient(apiKey: apiKey),
    _ => throw new ArgumentException("Unknown provider: " + provider)
};

EmbeddingClientBase CreateEmbedding(string provider, string? apiKey) => provider switch
{
    "ollama" => new OllamaEmbeddingClient(),
    "openai" => new OpenAiEmbeddingClient(apiKey: apiKey),
    "voyageai" => new VoyageAiEmbeddingClient(apiKey: apiKey),
    "tei" => new TeiEmbeddingClient("http://localhost:8080"),
    _ => throw new ArgumentException("Unknown provider: " + provider)
};

using CompletionClientBase chat = CreateChat("ollama", null);
chat.Defaults.MaxTokens = 256;
ChatResponse reply = await chat.ChatAsync("Hello!", new CompletionOptions { Temperature = 0.3 });
```

## API Reference

### Constructors

Most single-key clients share one constructor shape, with every parameter optional. xAI instead takes a required `apiKey` first, followed by optional endpoint/logging/httpClient; OllamaCloud also requires its key and fixes the endpoint:

| Parameter | Type | Default | Description |
|---|---|---|---|
| `endpoint` | `string` | the provider's public API (Ollama `http://localhost:11434`, TEI `http://localhost:8080`) | API endpoint URL |
| `apiKey` | `string?` | `null` | API key, attached to each request in the provider's format |
| `logging` | `LoggingModule?` | `null` | Logging module; a new one is created when omitted |
| `httpClient` | `HttpClient?` | `null` | Transport to use; the caller owns it (see [Custom or Shared HttpClient](#custom-or-shared-httpclient)) |

Clients with provider-shaped credentials or routing:

| Client | Signature |
|---|---|
| `AzureOpenAiCompletionClient`, `AzureOpenAiEmbeddingClient` | `(endpoint, deployment, apiKey, apiVersion?, logging?, httpClient?)` or `(endpoint, deployment, ICredentialProvider, apiVersion?, ...)` |
| `AzureOpenAiModelClient` | `(endpoint, apiKey, apiVersion?, ...)` or `(endpoint, ICredentialProvider, apiVersion?, ...)` |
| `VertexAiCompletionClient`, `VertexAiEmbeddingClient` | `(project, region, ICredentialProvider, endpoint?, logging?, httpClient?)`; the endpoint defaults to `https://{region}-aiplatform.googleapis.com` |
| `Bedrock*Client` | `(IAwsCredentialProvider, region, endpoint?, logging?, httpClient?)`; runtime and control-plane endpoints are derived from the region |

### Common Members

| Member | On | Description |
|---|---|---|
| `Endpoint`, `ApiKey` | every client | Read-only |
| `TimeoutMs` | every client | HTTP timeout in milliseconds (default 120,000, must be greater than zero) |
| `CallDetails`, `MaxCallDetails`, `ClearCallDetails()` | every client | Recorded HTTP calls (default retention 1,000) |
| `ValidateConnectivityAsync()` | every client | See [Validate Connectivity](#validate-connectivity) |
| `Defaults` | every client except model clients | Client-wide settings of the capability's options type |
| `Model` | every client except model clients | Shorthand for `Defaults.Model`; assigning null or whitespace throws |
| `MaxConcurrency` | decision clients | Batch concurrency, 1 to 64 (default 4) |
| `AnthropicVersion`, `WorkspaceId` | Anthropic clients | `anthropic-version` (default `2023-06-01`) and `anthropic-workspace-id` headers |
| `ApiVersion` | Azure OpenAI clients | `api-version` query value (default `AzureOpenAiDefaults.ApiVersion`) |
| `PageSize` | `AnthropicModelClient`, `CohereModelClient` | Model-list page size, 1 to 1,000 |
| `PullTimeout`, `PullModelAsync`, `DeleteModelAsync` | `OllamaModelClient` | Model pull and delete |

### Options

| Capability | Base options | Provider options |
|---|---|---|
| Completion | `CompletionOptions`: `Model`, `MaxTokens` (1 to 10,000,000), `Temperature` (0 to 2), `TopP` (0 to 1), `SystemPrompt`, `ReasoningEffort` | `OllamaCompletionOptions`, `XAiCompletionOptions`, `OpenAiCompletionOptions` (also Azure), `GeminiCompletionOptions` (also Vertex AI), `AnthropicCompletionOptions`, `CohereCompletionOptions`; Bedrock uses the base type |
| Embedding | `EmbeddingOptions`: `Model` | `OllamaEmbeddingOptions`, `XAiEmbeddingOptions`, `OpenAiEmbeddingOptions` (also Azure), `GeminiEmbeddingOptions`, `VertexAiEmbeddingOptions`, `BedrockEmbeddingOptions`, `VoyageAiEmbeddingOptions`, `CohereEmbeddingOptions`, `TeiEmbeddingOptions` |
| Sparse embedding | `SparseEmbeddingOptions`: `Model` | `TeiSparseEmbeddingOptions` |
| Rerank | `RerankOptions`: `Model`, `TopN`, `ReturnDocuments` | `BedrockRerankOptions`, `VoyageAiRerankOptions`, `CohereRerankOptions`, `TeiRerankOptions` |
| Classification | `ClassificationOptions`: `Model` | `CohereClassificationOptions`, `TeiClassificationOptions` |
| Decision | `DecisionOptions`: `Model` | (none) |

Provider-specific parameters:

- **Ollama**: `ContextLength`, `TopK`, `RepeatPenalty`, `Seed`, `MinP`, `RepeatLastN`; embeddings add `ContextLength` and `Truncate` (bool).
- **xAI**: Responses-specific settings are in `XAiCompletionOptions`; embeddings use `XAiEmbeddingOptions` (including Dimensions/EncodingFormat, Preview, and User). See the xAI section for the complete native API.
- **OpenAI and Azure OpenAI**: `FrequencyPenalty`, `PresencePenalty`, `Seed`, and, for generation only, `Echo`, `Suffix`, and `Logprobs`; embeddings add `Dimensions` and `EncodingFormat` (`float` or `base64`).
- **Gemini and Vertex AI**: `TopK`, `CandidateCount`, `PresencePenalty`, `FrequencyPenalty`, `ToolSchemaMode`; embeddings add `TaskType` and `Title`, and Vertex AI adds `OutputDimensionality` and `AutoTruncate`.
- **Anthropic**: `TopK`, `StopSequences`. Current Claude models (Opus 4.7 and later) reject `temperature`, `top_p`, and `top_k`; leave them unset for those models.
- **Bedrock**: embeddings add `InputType` (Cohere models), `Dimensions`, and `Normalize` (Titan v2); rerank adds `MaxTokensPerDoc`.
- **VoyageAI**: `InputType` (`query` or `document`), `Truncation`, `OutputDimension` (256, 512, 1024, 2048), `OutputDtype`; rerank adds `Truncation`.
- **Cohere**: `TopK` (0 to 500), `Seed`, `FrequencyPenalty` and `PresencePenalty` (0 to 1), `StopSequences` (at most 5), with `TopP` clamped to 0.01..0.99; embeddings add `InputType`, `EmbeddingType`, `OutputDimension`, and `Truncate`; rerank adds `MaxTokensPerDoc`; classification adds `Examples` and `Truncate`.
- **TEI**: embeddings add `Normalize`, `Truncate`, `TruncationDirection`, `PromptName`, and `Dimensions`; rerank and classification add `RawScores`, `Truncate`, and `TruncationDirection`; sparse embeddings add `Truncate`, `TruncationDirection`, and `PromptName`.

### Default Models

| Provider | Completion | Embedding | Rerank | Decision |
|---|---|---|---|---|
| xAI | `grok-4.7` | explicit catalog model | | |
| Ollama | `gemma3:4b` | `all-minilm` | | |
| OpenAI | `gpt-4o-mini` | `text-embedding-3-small` | | |
| Azure OpenAI | the deployment | the deployment | | |
| Gemini | `gemini-2.5-flash` | `gemini-embedding-001` | | |
| Vertex AI | `gemini-2.5-flash` | `text-embedding-005` | | |
| Anthropic | `claude-opus-4-8` | | | |
| Bedrock | `anthropic.claude-3-5-sonnet-20240620-v1:0` | `amazon.titan-embed-text-v2:0` | `cohere.rerank-v3-5:0` | |
| VoyageAI | | `voyage-3.5` | `rerank-2.5` | |
| Cohere | `command-a-03-2025` | `embed-v4.0` | `rerank-v3.5` | |
| TEI | | the hosted model | the hosted model | |
| TypeSafe | | | | `jev-latest` |

Cohere classification has no default model, which lets Cohere pick one for few-shot examples.

### Feature Notes

| Feature | Ollama | OpenAI | Azure | Gemini | Vertex | Anthropic | Bedrock | Cohere |
|---|---|---|---|---|---|---|---|---|
| Tool chat | model-dependent | yes | yes | yes | yes | yes | yes | yes |
| Reasoning effort | `think` | `reasoning_effort` | `reasoning_effort` | `thinkingBudget` | `thinkingBudget` | adaptive thinking + effort | thinking budget | `thinking.token_budget` |
| Reasoning output | `message.thinking` | `reasoning_content` | `reasoning_content` | `thought` parts | `thought` parts | `thinking` blocks | `reasoningContent` | thinking content and tool plan |
| Text generation | yes | legacy completions | legacy completions | yes | yes | via Messages | via Converse | via v2 Chat |

- Cohere accepts only `REQUIRED` and `NONE` tool choice: `auto` (or null) omits the field, `required` or `any` sends `REQUIRED`, `none` sends `NONE`, and a tool name sends only that tool with `REQUIRED`.
- Ollama tool calling depends on the model. `gemma3:4b` does not support tools; use a tool-capable model such as `gpt-oss:20b`.
- A TEI server hosts one model, and its type (`ModelInformation.Metadata["model_type"]`: `embedding`, `reranker`, or `classifier`) decides which TEI clients succeed. The others return an unsuccessful response with TEI's HTTP 424 error.

## Console Harnesses and Automatic Tools

All `*Console` applications share `ConsoleApplicationBase` in `Console.Shared`. The chat-capable harnesses (Ollama, OllamaCloud, OpenAI, Anthropic, Gemini, Cohere, and xAI) use one `ConsoleSession` implementation for streaming, conversation history, reasoning display, and automatic tool execution. Both `ch/chat` and `tc/toolchat` now advertise tools, execute requested calls, send their results back, and continue until the model answers. The manual weather-result prompt has been removed. Use a tool-capable model; OllamaConsole now suggests `gpt-oss:20b`.

Model output appears under separate `Thinking` and `Answer` labels, with `Tool: NAME` sections for actual calls and results. Usage blocks end with a blank line before the next command prompt. Thinking is displayed as it arrives when the provider streams it, and the accumulated response is a fallback. Providers that do not return reasoning produce only an answer. Native `gen/generate` and `gens/genstream` keep their original APIs; a console-only HTTP observer displays returned Ollama, Anthropic, Gemini, and Cohere generation thinking without changing the library's generation models.

| Command | Behavior |
|---|---|
| `ch/chat`, `tc/toolchat` | Chat with automatic tools and persistent conversation history |
| `tools` | List the configured Mux tools and current execution setting |
| `tools off`, `tools on` | Disable or enable automatic tools (enabled by default) |
| `pwd`, `cd PATH` | Show or change the working directory used by tools |
| `reset` | Clear conversation history |
| `reasoning LEVEL` | Set model-dependent reasoning effort; `default` leaves the provider default |
| Ctrl+C during chat | Cancel the model turn and terminate its running tool-host process tree |

Automatic tools run with the console user's filesystem and process permissions. `cd` supplies working-directory context; it does not confine tools to that directory. `tools off` is available for plain chat. The loop is bounded to 12 model rounds and 64 tool calls per round; errors become tool results when possible, and failed or canceled turns are removed from conversation history.

The shared tool runtime reuses **Mux.Core 1.1.1's built-in registry**, including `read_file`, `write_file`, `edit_file`, `multi_edit`, `delete_file`, `file_metadata`, `list_directory`, `manage_directory`, `glob`, `grep`, `run_process`, `web_retrieve`, `plan_tasks`, and `update_task`. Task-plan state is retained by the host across calls. Optional `web_search` and `spawn_subagent` tools are supported when configured:

- `POLYPROMPT_MUX_SETTINGS`: absolute path to a Mux settings JSON object, including `externalSearch.enabled` and its configured providers/API keys for web search, tool timeouts, and task-planning settings.
- `POLYPROMPT_MUX_SUBAGENTS`: absolute path to a JSON array of Mux subagent definitions (`name`, `systemPrompt`, optional `endpointName`, `allowedTools`, and `maxIterations`).
- `POLYPROMPT_MUX_ENDPOINTS`: absolute path to a JSON array of Mux endpoint configurations, required when subagents are configured. The default endpoint (or first entry) is inherited unless a definition selects another. Configured subagents use automatic tool execution, matching the console.

For example, web search can use a settings file with this shape (supply your own provider endpoint and API key, outside source control):

```json
{
  "externalSearch": {
    "enabled": true,
    "providers": [
      { "name": "search", "providerType": "tavily", "endpoint": "https://api.tavily.com", "apiKey": "$TAVILY_API_KEY", "isDefault": true }
    ]
  }
}
```

`Console.Tools` is a companion JIT process, copied into each console's `tools/` output and publish directory. This keeps Mux dependencies out of the PolyPrompt library and lets AotConsole use the same tools without loading Mux into its native process. Keep the companion directory alongside the console and install the matching .NET runtime. `POLYPROMPT_TOOL_HOST` can override the full path to `Console.Tools.dll`. Browser retrieval uses Playwright and requires an installed browser; from a console output directory, run `dotnet tools/Console.Tools.dll --install-browser chromium` if Chromium is not installed.

VoyageAI, TEI, and TypeSafe retain their capability-specific commands and expose shared tool configuration/help, but their providers do not offer chat or model-driven tool calls. These harnesses do not send tools to embedding, reranking, classification, or decision endpoints.

The native console accepts a one-shot tool prompt, for example:

```bash
dotnet publish src/AotConsole -c Release -f net10.0 -r osx-arm64 -o out/aot
./out/aot/AotConsole --provider ollama --model gpt-oss:20b \
  --prompt "What files do you see in the current directory?" --directory .
```

Run the shared console tests with `dotnet test src/Test.Console -c Release`. They exercise real Mux file/process/task/browser tools against temporary files and a local page. Browser tests require Playwright Chromium. To include the saved multi-round model simulation, start `postman simulation run postman/simulations/ollamacloud.sim.yaml --port 4911 --no-history`, then run:

```bash
POLYPROMPT_CONSOLE_SIMULATION=http://127.0.0.1:4911/ollamacloud \
  dotnet test src/Test.Console -c Release
```

The simulation asks the console to list a temporary directory, read an actual fixture file, and return its contents through separate tool-result rounds. It is a local model fixture, not evidence of a live provider's model capabilities.

## Project Structure

```
PolyPrompt/
|-- src/
|   |-- PolyPrompt/              # Core library (NuGet package)
|   |   |-- Auth/                # SigV4 signing and OAuth / AWS credential providers
|   |   |-- Clients/
|   |   |   |-- Base/            # ClientBase and the seven capability bases
|   |   |   `-- <Provider>/      # One folder per provider: capability clients and a shared protocol helper
|   |   |-- Helpers/             # AOT-safe JSON serializer, PolyPromptJsonContext, UtcDateTimeConverter
|   |   |-- Telemetry/           # Meter, ActivitySource, and the stable telemetry names (PolyPromptTelemetryNames)
|   |   |-- Wire/                # AWS event-stream decoder
|   |   |-- Models/              # Request, response, and base options models
|   |   `-- Options/             # Provider-specific options
|   |-- XAiConsole/              # Native Responses, model catalogs, embeddings, shared Mux tools
|   |-- OllamaCloudConsole/      # Ollama Cloud harness (fixed URL, required bearer token)
|   |-- OllamaConsole/           # Interactive harnesses, one per provider
|   |-- OpenAIConsole/
|   |-- GeminiConsole/
|   |-- AnthropicConsole/
|   |-- VoyageAIConsole/
|   |-- CohereConsole/
|   |-- TeiConsole/
|   |-- TypeSafeConsole/
|   |-- AotConsole/              # Native console: shared tools, thinking display, saved conversation
|   |-- Console.Shared/          # Shared base, conversation/tool loop, rendering, native-safe tool bridge
|   |-- Console.Tools/           # Companion Mux built-in tool host (JIT)
|   |-- Test.Console/            # Shared console and real tool execution tests
|   |-- Test.Shared/             # Shared Touchstone test descriptors and the local mock server
|   |-- Test.Automated/          # Touchstone console runner
|   |-- Test.Aot/                # Native AOT verification: every client, JIT vs native binary (verify-aot.sh)
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

The local suite (270 cases, including the telemetry and serializer cases) runs against an in-process mock server and needs no credentials:

```bash
dotnet run --project src/Test.Automated --framework net8.0 -- selftest
dotnet test src/Test.Xunit/Test.Xunit.csproj
dotnet test src/Test.Nunit/Test.Nunit.csproj
```

The Native AOT check runs every client on the JIT and as a native binary (any trim or AOT warning fails the publish), and requires both runs to send byte-identical requests. It needs the platform's [Native AOT prerequisites](https://learn.microsoft.com/dotnet/core/deploying/native-aot/#prerequisites):

```bash
src/Test.Aot/verify-aot.sh                      # current platform, net8.0 and net10.0
```

The live suite runs against one provider at a time. Cases for a capability the provider does not have are skipped with the reason.

```bash
# Hosted APIs default to their public endpoints.
dotnet run --project src/Test.Automated -- --openai-key sk-... --openai-model gpt-4o-mini
dotnet run --project src/Test.Automated -- --gemini-key AIza... --gemini-model gemini-2.5-flash
dotnet run --project src/Test.Automated -- --anthropic-key sk-ant-... --anthropic-model claude-opus-4-8 [--anthropic-workspace wrkspc_...]
dotnet run --project src/Test.Automated -- --voyageai-key pa-... --voyageai-embedding-model voyage-3.5 --voyageai-rerank-model rerank-2.5
dotnet run --project src/Test.Automated -- --cohere-key ... --cohere-model command-a-03-2025 --cohere-rerank-model rerank-v3.5
dotnet run --project src/Test.Automated -- --typesafe-key ts-... [--typesafe-endpoint <Ollaya URL>]

# Local servers.
dotnet run --project src/Test.Automated -- --ollama-endpoint http://localhost:11434 --ollama-model gpt-oss:20b --ollama-embedding-model all-minilm
dotnet run --project src/Test.Automated -- --tei-endpoint http://localhost:8080

# Cloud providers.
dotnet run --project src/Test.Automated -- --azure-endpoint https://my.openai.azure.com --azure-key ... --azure-model gpt-4o [--azure-embedding-model text-embedding-3-small]
dotnet run --project src/Test.Automated -- --vertex-project my-project [--vertex-region us-central1] [--vertex-credentials sa.json]
dotnet run --project src/Test.Automated -- --bedrock-region us-east-1 [--bedrock-access-key-id ... --bedrock-secret-access-key ...]

# Generic form and positional form.
dotnet run --project src/Test.Automated -- --provider ollama --endpoint http://localhost:11434 --model gpt-oss:20b
dotnet run --project src/Test.Automated -- ollama http://localhost:11434 "" gpt-oss:20b all-minilm
```

The same settings can come from environment variables, which also enable the live suite under xUnit and NUnit: the generic `POLYPROMPT_TEST_PROVIDER`, `_ENDPOINT`, `_API_KEY`, `_MODEL`, `_EMBEDDING_MODEL`, `_RERANK_MODEL`, `_REGION`, `_PROJECT`, `_CREDENTIALS`, `_AWS_ACCESS_KEY_ID`, `_AWS_SECRET_ACCESS_KEY`, `_AWS_SESSION_TOKEN`, and `_API_VERSION`, or exactly one provider group such as `POLYPROMPT_TEST_OPENAI_API_KEY` and `POLYPROMPT_TEST_OPENAI_MODEL`. Run `Test.Automated --help` for the full list.

A TEI server hosts one model, so run the TEI live suite once per model type you deploy (embedding, reranker, classifier). The TEI cases read the hosted model type from `/info` and assert success for what it serves and a clean HTTP 424 failure for the rest.

## Issues and Discussions

Have a bug to report or a feature to request? Please open an issue on GitHub:

https://github.com/jchristn/PolyPrompt/issues

Want to ask a question or start a conversation? Use GitHub Discussions:

https://github.com/jchristn/PolyPrompt/discussions

## License

PolyPrompt is available under the [MIT License](LICENSE.md). See the `LICENSE.md` file for full details.
