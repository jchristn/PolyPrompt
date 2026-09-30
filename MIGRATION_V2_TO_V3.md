# Migrating from PolyPrompt 2.x to 3.0

PolyPrompt 3.0 replaces the single `CompletionClientBase` hierarchy, where every provider client carried every operation and threw `NotSupportedException` for the ones it could not do, with **one client per capability per provider**. It also consolidates settings into reusable options classes, adds decision models (TypeSafe and Ollaya), and fixes a number of long-standing inconsistencies.

The wire protocols are unchanged except where noted under [Behavior changes](#behavior-changes). Most migrations are mechanical: pick the client for the operation, and move settings into `Defaults` or the per-call options.

## Contents

1. [The new shape](#the-new-shape)
2. [Client mapping](#client-mapping)
3. [Settings: Defaults and per-call options](#settings-defaults-and-per-call-options)
4. [Operation-by-operation changes](#operation-by-operation-changes)
5. [Renamed and removed types](#renamed-and-removed-types)
6. [Behavior changes](#behavior-changes)
7. [Writing provider-agnostic code](#writing-provider-agnostic-code)
8. [Custom subclasses](#custom-subclasses)
9. [Checklist](#checklist)

## The new shape

```
ClientBase                     HTTP transport, timeouts, CallDetails, ValidateConnectivityAsync
├── CompletionClientBase       ChatAsync, ChatStreamingAsync, ToolChatAsync, ToolChatStreamingAsync,
│                              GenerateAsync, GenerateStreamingAsync
├── EmbeddingClientBase        EmbedAsync(string), EmbedAsync(List<string>)
├── SparseEmbeddingClientBase  EmbedSparseAsync(string), EmbedSparseAsync(List<string>)
├── RerankClientBase           RerankAsync(query, documents)
├── ClassificationClientBase   ClassifyAsync(string), ClassifyAsync(List<string>)
├── DecisionClientBase         DecideAsync(DecisionRequest), DecideAsync(List<DecisionRequest>)   (new)
└── ModelClientBase            ListModelsAsync, ModelExistsAsync, GetModelInformationAsync
```

Every client:

- inherits only the operations it actually supports, so nothing throws `NotSupportedException` and the compiler tells you what a provider can do;
- has its own `Model` and a `Defaults` object of its capability's options type (for example `OllamaCompletionOptions`), so a Cohere chat model, embedding model, and rerank model are simply three clients;
- attaches credentials and provider headers to each request instead of modifying `HttpClient.DefaultRequestHeaders`, so several clients can safely share one `HttpClient`.

## Client mapping

| 2.x client | 3.0 clients |
|---|---|
| `OllamaClient` | `OllamaCompletionClient`, `OllamaEmbeddingClient`, `OllamaModelClient` (includes `PullModelAsync` and `DeleteModelAsync`) |
| `OpenAiClient` | `OpenAiCompletionClient`, `OpenAiEmbeddingClient`, `OpenAiModelClient` |
| `AzureOpenAiClient` | `AzureOpenAiCompletionClient`, `AzureOpenAiEmbeddingClient` (each takes a deployment), `AzureOpenAiModelClient` |
| `GeminiClient` | `GeminiCompletionClient`, `GeminiEmbeddingClient`, `GeminiModelClient` |
| `VertexAiClient` | `VertexAiCompletionClient`, `VertexAiEmbeddingClient` (Vertex model listing was never supported) |
| `AnthropicClient` | `AnthropicCompletionClient`, `AnthropicModelClient` |
| `BedrockClient` | `BedrockCompletionClient`, `BedrockEmbeddingClient`, `BedrockRerankClient`, `BedrockModelClient` |
| `CohereClient` | `CohereCompletionClient`, `CohereEmbeddingClient`, `CohereRerankClient`, `CohereClassificationClient`, `CohereModelClient` |
| `VoyageAiClient` | `VoyageAiEmbeddingClient`, `VoyageAiRerankClient` |
| `TeiClient` | `TeiEmbeddingClient`, `TeiSparseEmbeddingClient`, `TeiRerankClient`, `TeiClassificationClient`, `TeiModelClient` |
| (new) | `TypeSafeDecisionClient` (TypeSafe hosted API and local Ollaya) |

Constructors keep their 2.x parameters (endpoint, API key, logging, HTTP client), with two adjustments:

- **Bedrock** clients take `(credentialProvider, region, endpoint = null, logging = null, httpClient = null)`. In 2.x `endpoint` came last.
- **Azure OpenAI** completion and embedding clients take a `deployment`, as before; `AzureOpenAiModelClient` does not.

```csharp
// 2.x
using CohereClient cohere = new CohereClient(apiKey: key);
cohere.Model = "command-a-03-2025";
cohere.EmbeddingModel = "embed-v4.0";
ChatResponse chat = await cohere.ChatAsync("Hello");
EmbeddingResponse vectors = await cohere.EmbedAsync(texts);

// 3.0
using CohereCompletionClient chat = new CohereCompletionClient(apiKey: key);
using CohereEmbeddingClient embeddings = new CohereEmbeddingClient(apiKey: key);
ChatResponse reply = await chat.ChatAsync("Hello");
EmbeddingResponse vectors = await embeddings.EmbedAsync(texts);
```

To share a connection pool, pass the same `HttpClient` to each client; the caller keeps ownership and disposes it.

Connection settings belong to each client, not to a provider. When one 2.x client becomes several 3.0 clients, set them on each:

- `TimeoutMs` (default 120,000), `MaxCallDetails`, `CallDetails`, and `ClearCallDetails()` are per client, so a Cohere chat client and a Cohere embedding client keep separate call histories.
- `AnthropicVersion` and `WorkspaceId` exist on both `AnthropicCompletionClient` and `AnthropicModelClient`. An identity-linked key needs `WorkspaceId` on each.
- `ApiVersion` exists on each Azure OpenAI client.

## Settings: Defaults and per-call options

Client-level sampling properties are gone. Each client has a `Defaults` object, typed to the provider's options class, and every operation takes the same options type per call. For each setting, a non-null per-call value wins, otherwise the client default, otherwise the provider's own default (no field is sent).

| 2.x | 3.0 |
|---|---|
| `client.Model = "x"` | `client.Model = "x"` (reads and writes `Defaults.Model`; assigning null or whitespace throws, so clear it with `client.Defaults.Model = null`) |
| `client.MaxTokens`, `Temperature`, `TopP`, `SystemPrompt`, `ReasoningEffort` | `client.Defaults.MaxTokens`, `.Temperature`, `.TopP`, `.SystemPrompt`, `.ReasoningEffort` |
| `OllamaClient.ContextLength` | `ollama.Defaults.ContextLength` |
| `CohereClient.EmbeddingModel` / `RerankModel` / `ClassificationModel` | `Model` on the Cohere embedding, rerank, and classification clients |
| `VoyageAiClient.RerankModel`, `BedrockClient.RerankModel` | `Model` on the rerank clients |
| `GeminiClient.ToolSchemaMode` | `GeminiCompletionOptions.ToolSchemaMode` (in `Defaults` or per call) |
| `AnthropicClient.ModelsPageLimit`, `CohereClient.ModelsPageSize` | `PageSize` on `AnthropicModelClient` and `CohereModelClient` |

Options classes:

| 2.x | 3.0 |
|---|---|
| `ChatCompletionOptions`, `GenerationOptions` | `CompletionOptions` |
| `OllamaChatCompletionOptions`, `OllamaGenerationOptions` | `OllamaCompletionOptions` |
| `OpenAiChatCompletionOptions`, `OpenAiGenerationOptions`, `AzureOpenAiChatCompletionOptions` | `OpenAiCompletionOptions` (`Echo`, `Suffix`, and `Logprobs` apply to generation only) |
| `GeminiChatCompletionOptions`, `GeminiGenerationOptions` | `GeminiCompletionOptions` |
| `AnthropicChatCompletionOptions`, `AnthropicGenerationOptions` | `AnthropicCompletionOptions` |
| `CohereChatCompletionOptions`, `CohereGenerationOptions` | `CohereCompletionOptions` |
| `AzureOpenAiEmbeddingOptions` | `OpenAiEmbeddingOptions` |
| Embedding, rerank, classification, and sparse options | Unchanged names; now also usable as `Defaults` |

```csharp
// 2.x
OllamaClient ollama = new OllamaClient();
ollama.Model = "gemma3:4b";
ollama.MaxTokens = 512;
ollama.ContextLength = 8192;
ChatResponse r = await ollama.ChatAsync("Hi", new OllamaChatCompletionOptions { Temperature = 0.2 });

// 3.0
OllamaCompletionClient ollama = new OllamaCompletionClient();
ollama.Model = "gemma3:4b";
ollama.Defaults.MaxTokens = 512;
ollama.Defaults.ContextLength = 8192;
ChatResponse r = await ollama.ChatAsync("Hi", new OllamaCompletionOptions { Temperature = 0.2 });
```

The same options object works for `ChatAsync`, `ToolChatAsync` (via `ToolChatRequest.Options`), and `GenerateAsync`.

Settings merge field by field. Setting `Temperature` per call does not discard `Defaults.MaxTokens`:

```csharp
chat.Defaults.MaxTokens = 512;
chat.Defaults.Temperature = 0.2;
await chat.ChatAsync("Hi", new CompletionOptions { Temperature = 0.9 });   // sends max tokens 512, temperature 0.9
```

When neither the call nor `Defaults` sets `MaxTokens`, 4096 is sent (`CompletionClientBase.DefaultMaxTokens`), as in 2.x.

## Operation-by-operation changes

### Chat and generation

Signatures change only in the options type: `ChatAsync(string prompt, CompletionOptions? options = null, ...)` and `GenerateAsync(string prompt, CompletionOptions? options = null, ...)`. `CompletionOptions.Model` now lets a single chat call use another model (2.x could only do this for generation and tool chat).

### Tool chat

`ToolChatRequest` loses its inline `Model`, `MaxTokens`, `Temperature`, `TopP`, and `ReasoningEffort`; set them on `request.Options` instead:

```csharp
// 2.x
ToolChatRequest request = new ToolChatRequest { Model = "gpt-4o", Temperature = 0.1 };

// 3.0
ToolChatRequest request = new ToolChatRequest { Options = new CompletionOptions { Model = "gpt-4o", Temperature = 0.1 } };
```

Provider options (`OllamaCompletionOptions` and so on) now also apply to tool chat; in 2.x tool chat ignored them.

### Embeddings, sparse embeddings, reranking, classification

Signatures are unchanged. Calls are on the capability client, and the model comes from that client's `Model` or the per-call options. `RerankOptions.ReturnDocuments` is now `bool?` (null means false unless `Defaults` sets it).

`TopN` behaves differently depending on where it is set. A per-call `TopN` larger than the number of documents throws `ArgumentOutOfRangeException`, as in 2.x. A `Defaults.TopN` larger than the number of documents is capped to the document count, so one client-wide default works for any batch size.

### Models

`ListModelsAsync`, `ModelExistsAsync`, and `GetModelInformationAsync` move to the `...ModelClient` classes. `PullModelAsync` and `DeleteModelAsync` are only on `OllamaModelClient`, which also adds `PullTimeout` (default 30 minutes, previously fixed).

### Decisions (new)

```csharp
using TypeSafeDecisionClient jev = new TypeSafeDecisionClient(apiKey: key);    // or new TypeSafeDecisionClient("http://localhost:<port>", "local") for Ollaya

DecisionResponse answer = await jev.DecideAsync(new DecisionRequest
{
    State = ticketText,
    Questions =
    {
        DecisionQuestion.Choice("intent", "What does the customer want?", "refund", "billing", "other"),
        DecisionQuestion.Binary("urgent", "Is this time-sensitive?"),
        DecisionQuestion.Score("tone", "How upset is the customer?", "calm", "annoyed", "angry")
    }
});

if (answer.Choice("intent").Confidence >= 0.9) Route(answer.Choice("intent").Value);
```

Choice options can carry descriptions with `DecisionOption.Of("refund", "Wants money back")`. Answers expose the calibrated `Confidence`, the full `Probabilities` distribution, `ChoiceAnswer.Probability` (the chosen option's probability), `BinaryAnswer.Probability`, and `ScoreAnswer.Value` with its nearest rubric `Level` and `LevelText`. `DecideAsync(List<DecisionRequest>)` sends a batch concurrently (`MaxConcurrency`, default 4) and returns the responses in input order.

## Renamed and removed types

| 2.x | 3.0 |
|---|---|
| `CompletionCallDetail` | `CallDetail` |
| `CompletionHttpResult` | `HttpCallResult` |
| `ChatStreamingUsage` | `TokenUsage` (used by streaming and non-streaming responses and by decisions) |
| `GeminiToolSchemaMode` in `PolyPrompt.Models` | `GeminiToolSchemaMode` in `PolyPrompt.Options` |
| `AzureOpenAiClient.DefaultApiVersion` | `AzureOpenAiDefaults.ApiVersion` |
| Response `Success`, `StatusCode`, `Error`, `Model`, `OverallRuntimeMs` | Unchanged names, now declared once on `ResponseBase`. `StatusCode` is `int?` on every response and `Model` is `string?`. |

Removed without replacement: `NotSupportedException` paths (the operations no longer exist on clients that cannot do them), and `CompletionClientBase.ReasoningEffort`/`SystemPrompt`/`MaxTokens`/`Temperature`/`TopP` (see `Defaults`).

## Behavior changes

Intentional changes to what is sent or returned. Most fix bugs that 2.x tests did not cover.

- **Settings apply consistently.** `Defaults.SystemPrompt` and `Defaults.ReasoningEffort` apply to chat and tool chat. In tool chat, the system prompt is prepended as a system message only when the request's messages contain no system message, so a conversation that already carries one is sent as-is. In 2.x the client-level reasoning default applied to tool chat only, and the client-level system prompt never applied to tool chat. Text generation still sends its prompt unchanged.
- **Credentials are per request.** No client modifies `HttpClient.DefaultRequestHeaders`. Sharing an injected `HttpClient` across clients or providers is now safe.
- **Gemini API key moved from the URL to the `x-goog-api-key` header.** In 2.x the key was in the query string, so it appeared in `CallDetail.Url`.
- **Gemini chat system prompt uses `systemInstruction`.** 2.x sent it as a fake user/model exchange.
- **Gemini tool results need a function name.** When `ChatMessage.ToolName` is missing, the name is taken from the assistant tool call with the same `ToolCallId` earlier in the conversation. If there is none, `ToolChatAsync` throws `ArgumentException` before sending, instead of sending the call id as the function name (which Gemini rejects). Always set `ToolName`; `ChatMessage.ToolResult` does.
- **Gemini responses without candidates are failures.** A blocked prompt reports `Error = "Prompt blocked: <reason>"`. 2.x reported success with null text.
- **Assistant text beside tool calls is kept** when replaying history to Gemini.
- **Per-call models are honored everywhere.** 2.x ignored a per-call model in some paths (for example Ollama and OpenAI plain chat always used the client model, and Azure always routed to the client's deployment).
- **Each capability has its own default model.** 2.x reused the chat model for embeddings on Ollama, OpenAI, Gemini, and Bedrock unless a model was passed per call. New defaults: Ollama `all-minilm`, OpenAI `text-embedding-3-small`, Gemini `gemini-embedding-001`, Vertex AI `text-embedding-005`, Bedrock `amazon.titan-embed-text-v2:0`. Azure OpenAI embeddings use the deployment passed to `AzureOpenAiEmbeddingClient`. If you relied on 2.x embedding with your chat model, set the embedding client's `Model` explicitly.
- **`OllamaEmbeddingOptions.Truncate` is `bool?`**, matching Ollama's API (2.x sent an integer).
- **OpenAI base64 embeddings are decoded** (`EncodingFormat = "base64"`); 2.x could not parse them.
- **Connectivity checks fail on HTTP errors.** `ValidateConnectivityAsync` now returns false for an HTTP error status, an unreachable server, or a timeout on every client, and rethrows only your own cancellation. In 2.x several providers returned true when the model listing request failed. Each client probes something its own credentials can reach:

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
  | `VoyageAiEmbeddingClient`, `VoyageAiRerankClient` | a one-word embedding or rerank (no model listing API) |
  | TEI (all) | `GET /health` |
  | `TypeSafeDecisionClient` | `GET /v1/models` (Ollaya); on HTTP 404 (hosted API), a one-question decision |
- **Model list pagination.** `GeminiModelClient` follows `nextPageToken`.
- **Ollama delete is recorded** in `CallDetails` and pull requests carry credentials.
- **Arguments are validated before any request on every provider**: null prompts, null or empty input lists, null list elements, and (for tool chat) null messages throw.
- **Missing model.** An operation that needs a model throws `InvalidOperationException` when neither the call nor `Defaults` sets one.

## Writing provider-agnostic code

Hold the capability base type:

```csharp
CompletionClientBase chat = useLocal
    ? new OllamaCompletionClient()
    : new OpenAiCompletionClient(apiKey: key);

EmbeddingClientBase embeddings = useLocal
    ? new OllamaEmbeddingClient()
    : new OpenAiEmbeddingClient(apiKey: key);
```

In 2.x a single `CompletionClientBase` reference could reach every operation and you discovered unsupported ones at run time. In 3.0 you hold one reference per capability you use, and a provider that lacks a capability simply has no client for it.

## Custom subclasses

If you subclassed `CompletionClientBase`:

- Derive from the capability base for what your provider does.
- Implement the protected `...CoreAsync` methods. The public methods are no longer virtual: the base class validates arguments and merges settings, then calls your implementation with a `ResolvedCompletion` (model, token limit, sampling, system prompt, reasoning effort, and the original per-call options).
- Override `Defaults` with your options type, `ValidateConnectivityAsync`, and `PrepareRequestAsync` to attach credentials.
- Use `ExecutePostAsync`, `ExecuteGetAsync`, and `ExecuteStreamingAsync` for requests; they handle status codes, errors, timing, and call recording. `Pick` and `PickRef` read provider-specific settings from the per-call options, falling back to `Defaults`.

## Checklist

1. Replace each 2.x client with the capability clients you use (see [Client mapping](#client-mapping)).
2. Move `MaxTokens`, `Temperature`, `TopP`, `SystemPrompt`, `ReasoningEffort`, and provider settings to `Defaults`. Set `TimeoutMs`, `MaxCallDetails`, and provider headers (`WorkspaceId`, `ApiVersion`) on every client you create.
3. Replace `ChatCompletionOptions`/`GenerationOptions` (and provider variants) with `CompletionOptions` (and provider variants).
4. Move `ToolChatRequest` inline settings to `request.Options`.
5. Rename `CompletionCallDetail`, `CompletionHttpResult`, and `ChatStreamingUsage` to `CallDetail`, `HttpCallResult`, and `TokenUsage`.
6. Remove `try`/`catch (NotSupportedException)` around operations; they are now compile-time.
7. Make sure every tool result sets `ToolName` (required for Gemini).
8. If you persist conversations, keep persisting `ToolCall.ThoughtSignature` (unchanged from 2.8).
9. Set `Model` on each embedding client explicitly if you relied on 2.x embedding with your chat model; the embedding clients now have their own defaults.
10. If a tool-chat conversation has no system message and you set `Defaults.SystemPrompt`, expect it to be sent now.
11. Review the rest of [Behavior changes](#behavior-changes).
