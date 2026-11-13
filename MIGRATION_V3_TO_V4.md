# Migrating from PolyPrompt 3.x to 4.0

4.0 goes back to **one client per provider**. In 3.x each provider had one client per capability (`OpenAiCompletionClient`, `OpenAiEmbeddingClient`, `OpenAiModelClient`), so using one provider for chat, embeddings, and model listing meant three objects, three copies of the key and `HttpClient`, and three `Defaults`. In 4.0 you construct `OpenAiClient` once and call any operation on it. Every provider client has every operation; an operation the provider does not support throws `CapabilityNotSupportedException` before any request, and `client.Supports(Capability.X)` lets you check first. The README capability matrix is checked against the code by tests.

The second change is OpenAI: **`OpenAiClient` now speaks the Responses API** (`/v1/responses`). Chat Completions moved to `OpenAiCompatibleClient`, for vLLM, Ollama `/v1`, LM Studio, gateways, and OpenAI itself when you want Chat Completions.

## The OpenAI decision

Every 3.x `OpenAiCompletionClient` becomes one of two clients. Decide by what you were calling:

| You were calling | 4.0 client | Notes |
|---|---|---|
| OpenAI (`api.openai.com`) | `OpenAiClient` | Responses API. Reasoning carries across tool turns. Options become `OpenAiCompletionOptions` (the Responses fields). |
| Anything else that speaks Chat Completions: vLLM, Ollama `/v1`, LM Studio, OpenRouter, Gemini's OpenAI endpoint, a gateway | `OpenAiCompatibleClient` | Same wire behavior as 3.x. Options become `OpenAiCompatibleCompletionOptions` (the 3.x `OpenAiCompletionOptions`). No default model. |
| OpenAI, but you need Chat Completions specifically | `OpenAiCompatibleClient("https://api.openai.com/v1", key)` | |

If `OpenAiClient` gets a 404 or 405 from `/responses`, its `Error` tells you to use `OpenAiCompatibleClient`.

## Client mapping

| Provider | 3.x clients | 4.0 client |
|---|---|---|
| OpenAI | `OpenAiCompletionClient` (against api.openai.com), `OpenAiEmbeddingClient`, `OpenAiModelClient` | `OpenAiClient` |
| OpenAI-compatible servers | `OpenAiCompletionClient` (any other server), `OpenAiEmbeddingClient`, `OpenAiModelClient` | `OpenAiCompatibleClient` |
| Azure OpenAI | `AzureOpenAiCompletionClient`, `AzureOpenAiEmbeddingClient`, `AzureOpenAiModelClient` | `AzureOpenAiClient` |
| xAI | `XAiCompletionClient`, `XAiEmbeddingClient`, `XAiModelClient` | `XAiClient` (no embeddings) |
| Anthropic | `AnthropicCompletionClient`, `AnthropicModelClient` | `AnthropicClient` |
| Google Gemini | `GeminiCompletionClient`, `GeminiEmbeddingClient`, `GeminiModelClient` | `GeminiClient` |
| Google Vertex AI | `VertexAiCompletionClient`, `VertexAiEmbeddingClient` | `VertexAiClient` |
| AWS Bedrock | `BedrockCompletionClient`, `BedrockEmbeddingClient`, `BedrockRerankClient`, `BedrockModelClient` | `BedrockClient` |
| Ollama | `OllamaCompletionClient`, `OllamaEmbeddingClient`, `OllamaModelClient` | `OllamaClient` |
| Ollama Cloud | `OllamaCloudCompletionClient`, `OllamaCloudEmbeddingClient`, `OllamaCloudModelClient` | `OllamaCloudClient` |
| Cohere | `CohereCompletionClient`, `CohereEmbeddingClient`, `CohereRerankClient`, `CohereClassificationClient`, `CohereModelClient` | `CohereClient` |
| VoyageAI | `VoyageAiEmbeddingClient`, `VoyageAiRerankClient` | `VoyageAiClient` |
| Hugging Face TEI | `TeiEmbeddingClient`, `TeiSparseEmbeddingClient`, `TeiRerankClient`, `TeiClassificationClient`, `TeiModelClient` | `TeiClient` |
| TypeSafe | `TypeSafeDecisionClient` | `TypeSafeClient` |

Before and after:

<!-- no-compile -->
```csharp
// 3.x
using OpenAiCompletionClient chat = new OpenAiCompletionClient(apiKey: key);
using OpenAiEmbeddingClient embed = new OpenAiEmbeddingClient(apiKey: key);
chat.Model = "gpt-4o-mini";
embed.Model = "text-embedding-3-small";
```

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

string key = "sk-...";
using OpenAiClient openai = new OpenAiClient(key);
openai.Defaults.Completion.Model = "gpt-6.1-sol";
openai.Defaults.Embedding.Model = "text-embedding-3-small";

ChatResponse reply = await openai.ChatAsync("Hello");
EmbeddingResponse vector = await openai.EmbedAsync("Hello");
```

## Every rename

Apply these mechanically. Names not listed are unchanged.

| 3.x | 4.0 |
|---|---|
| `OpenAiCompletionClient` | `OpenAiClient` (OpenAI) or `OpenAiCompatibleClient` (other servers); see above |
| `OpenAiEmbeddingClient`, `OpenAiModelClient` | `OpenAiClient` or `OpenAiCompatibleClient` |
| `AzureOpenAiCompletionClient`, `AzureOpenAiEmbeddingClient`, `AzureOpenAiModelClient` | `AzureOpenAiClient` |
| `XAiCompletionClient`, `XAiModelClient` | `XAiClient` |
| `XAiEmbeddingClient` | removed (xAI no longer offers embeddings) |
| `AnthropicCompletionClient`, `AnthropicModelClient` | `AnthropicClient` |
| `GeminiCompletionClient`, `GeminiEmbeddingClient`, `GeminiModelClient` | `GeminiClient` |
| `VertexAiCompletionClient`, `VertexAiEmbeddingClient` | `VertexAiClient` |
| `BedrockCompletionClient`, `BedrockEmbeddingClient`, `BedrockRerankClient`, `BedrockModelClient` | `BedrockClient` |
| `OllamaCompletionClient`, `OllamaEmbeddingClient`, `OllamaModelClient` | `OllamaClient` |
| `OllamaCloudCompletionClient`, `OllamaCloudEmbeddingClient`, `OllamaCloudModelClient` | `OllamaCloudClient` |
| `CohereCompletionClient`, `CohereEmbeddingClient`, `CohereRerankClient`, `CohereClassificationClient`, `CohereModelClient` | `CohereClient` |
| `VoyageAiEmbeddingClient`, `VoyageAiRerankClient` | `VoyageAiClient` |
| `TeiEmbeddingClient`, `TeiSparseEmbeddingClient`, `TeiRerankClient`, `TeiClassificationClient`, `TeiModelClient` | `TeiClient` |
| `TypeSafeDecisionClient` | `TypeSafeClient` |
| `CompletionClientBase`, `EmbeddingClientBase`, `SparseEmbeddingClientBase`, `RerankClientBase`, `ClassificationClientBase`, `DecisionClientBase`, `ModelClientBase` | `ProviderClientBase` (check `Supports`) |
| `ClaudeCodeAgentClient` | `ClaudeCodeClient` |
| `CodexAgentClient` | `CodexClient` |
| `GeminiCliAgentClient` | `GeminiCliClient` |
| `CursorAgentClient` | `CursorClient` |
| `MuxAgentClient` | `MuxClient` |
| `PiAgentClient` | `PiClient` |
| `OpenCodeAgentClient` | `OpenCodeClient` |
| `OpenAiCompletionOptions` (Chat Completions fields) | `OpenAiCompatibleCompletionOptions` |
| (new) | `OpenAiCompletionOptions` is now the OpenAI Responses options (`: ResponsesApiCompletionOptions`) |
| `XAiCompletionOptions : CompletionOptions` | `XAiCompletionOptions : ResponsesApiCompletionOptions` (shared field names unchanged) |
| `XAiCompletionOptions.ReasoningEffortOverride` (string) | `ReasoningEffortValue` (`ReasoningEffortValue?`) |
| `XAiCompletionOptions.ReasoningSummary` (string) | `ReasoningSummary` (`ReasoningSummary?`, converts from string) |
| `XAiCompletionOptions.ServiceTier` (string) | `ServiceTier` (`ServiceTier?`, converts from string) |
| `XAiCompletionOptions.HostedTools` (`List<XAiHostedTool>`) | `List<ResponsesApiHostedTool>` |
| `XAiEmbeddingOptions` | removed |
| `XAiResponse` | `ResponsesApiResponse` (`RawResponse` moved to `ResponseBase`; `Stored` added) |
| `XAiJsonResponse` | `ResponsesApiJsonResponse` |
| `XAiResponseEvent` | `ResponsesApiEvent` (adds `Kind`) |
| `XAiStreamingResponse` | `ResponsesApiStreamingResponse` |
| `XAiHostedTool` | `ResponsesApiHostedTool` (factories: `ResponsesApiTools`, `OpenAiTools`, `XAiTools`) |
| `PolyPromptJsonContext.Default.ListXAiHostedTool` | `PolyPromptJsonContext.Default.ListResponsesApiHostedTool` |
| `XAiCompletionClient.CompactConversationAsync(input, model, token)` | `CompactConversationAsync(input, model, previousResponseId, instructions, promptCacheKey, serviceTier, token)`; pass `token:` by name |
| `AzureOpenAiDefaults.ApiVersion`, the `apiVersion` constructor parameter, `ApiVersion` property | removed (the v1 API needs no `api-version`) |
| `client.Model` | `client.Defaults.<Section>.Model` (`Completion`, `Embedding`, `Rerank`, `Classification`, `Decision`, `SparseEmbedding`) |
| `client.Defaults.X` | `client.Defaults.<Section>.X`, for example `Defaults.Completion.Temperature` |
| `ListModelsAsync` override (custom clients) | override `ListModelsCoreAsync` |
| `RequiresModel` override (custom clients) | `EmbeddingRequiresModel`, `SparseEmbeddingRequiresModel`, `RerankRequiresModel`, `ClassificationRequiresModel` |
| `ResolveModel(options)` (custom clients) | `ResolveEmbeddingModel`, `ResolveSparseEmbeddingModel`, `ResolveClassificationModel`, `ResolveDecisionModel` |
| `ReasoningEffort.ToOpenAiWireValue()` returns `string` | returns `string?` (null for `None`) |
| `ReasoningEffort.ToAnthropicEffort()` returns `string` | returns `string?` (null for `None`) |

## Defaults and the removed Model shorthand

`Defaults` is now sectioned: one section per supported capability, typed with the provider's options. `client.Model` meant `Defaults.Model` in 3.x; with one client per provider that is ambiguous (chat model or embedding model?), so it is gone.

<!-- no-compile -->
```csharp
// 3.x
ollamaChat.Model = "gpt-oss:20b";
ollamaChat.Defaults.ContextLength = 8192;
ollamaEmbed.Model = "all-minilm";
```

```csharp
using PolyPrompt.Clients;

using OllamaClient ollama = new OllamaClient();
ollama.Defaults.Completion.Model = "gpt-oss:20b";
ollama.Defaults.Completion.ContextLength = 8192;
ollama.Defaults.Embedding.Model = "all-minilm";
```

A section the provider does not support is a compile error on its defaults type (`AnthropicDefaults.Embedding`) and null through the base `ClientDefaults`. An empty or null model is no longer rejected by a setter; the call throws `InvalidOperationException` naming the section instead.

## Constructors and ClientOptions

Every constructor's trailing `LoggingModule? logging = null, HttpClient? httpClient = null` parameters are replaced by one `ClientOptions? options = null`, which also carries `TimeoutMs`, `CallDetailCapture`, and `RedactCredentials`. Keyed providers now take the key first and require it.

| Client | 3.x | 4.0 |
|---|---|---|
| `OpenAiClient` | `OpenAiCompletionClient(endpoint = "https://api.openai.com", apiKey = null, logging, httpClient)` | `OpenAiClient(string apiKey, string? endpoint = null, ClientOptions? options = null)` |
| `OpenAiCompatibleClient` | `OpenAiCompletionClient(endpoint, apiKey, ...)` | `OpenAiCompatibleClient(string endpoint, string? apiKey = null, ClientOptions? options = null)`; `OpenAiCompatibleClientOptions.AuthPlacement` |
| `AzureOpenAiClient` | `(endpoint, deployment, apiKey, apiVersion, ...)` | `(string endpoint, string apiKey, ClientOptions?)` or `(string endpoint, ICredentialProvider, ClientOptions?)`; set `Defaults.Completion.Model` to the deployment |
| `AnthropicClient`, `GeminiClient`, `CohereClient`, `VoyageAiClient`, `TypeSafeClient` | `(endpoint = default, apiKey = null, ...)` | `(string apiKey, string? endpoint = null, ClientOptions?)` |
| `XAiClient` | `(apiKey, endpoint, ...)` | `(string apiKey, string? endpoint = null, ClientOptions?)` |
| `OllamaCloudClient` | `(apiKey, logging, httpClient)` | `(string apiKey, string? endpoint = null, ClientOptions?)` |
| `OllamaClient`, `TeiClient` | `(endpoint, apiKey, ...)` | `(endpoint, apiKey, ClientOptions?)` (TEI's endpoint is now required) |
| `BedrockClient` | `(credentialProvider, region, endpoint, ...)` | `(string region, IAwsCredentialProvider credential, string? endpoint = null, ClientOptions?)` |
| `VertexAiClient` | `(project, region, credential, endpoint, ...)` | same, with `ClientOptions?` last |
| Harness clients | `(executable, logging)` | `(executable, ClientOptions?)` |

<!-- no-compile -->
```csharp
// 3.x
using GeminiCompletionClient gemini = new GeminiCompletionClient(apiKey: key, httpClient: shared);
using AzureOpenAiCompletionClient azure = new AzureOpenAiCompletionClient(endpoint, "my-deployment", key, apiVersion: "2024-10-21");
using BedrockCompletionClient bedrock = new BedrockCompletionClient(credential, "us-east-1");
```

```csharp
using PolyPrompt.Auth;
using PolyPrompt.Clients;

string key = "your-key";
using HttpClient shared = new HttpClient();
using GeminiClient gemini = new GeminiClient(key, options: new ClientOptions { HttpClient = shared });
using AzureOpenAiClient azure = new AzureOpenAiClient("https://my-resource.openai.azure.com", key);
azure.Defaults.Completion.Model = "my-deployment";
using BedrockClient bedrock = new BedrockClient("us-east-1", new EnvironmentAwsCredential("us-east-1"));
bedrock.Defaults.Completion.Model = "anthropic.claude-sonnet-5-5";
```

Construction now fails fast: a required key that is null, blank, contains whitespace, or looks like a URL throws `ArgumentException` (for `OpenAiClient`, the message points to `OpenAiCompatibleClient`), and an endpoint that is not an absolute http(s) URL throws.

## Behavior changes

- **OpenAI uses Responses.** `OpenAiClient` sends `/v1/responses`. Encrypted reasoning is requested on every call and replayed through `ProviderState`.
- **`Store` is sent explicitly as `false`** unless you set `Store = true`. OpenAI's own API and SDKs default to `true`. `ContinueResponseAsync` throws before sending when the previous response was not stored. `Background = true` requires `Store = true`.
- **Unsupported capabilities throw** `CapabilityNotSupportedException` synchronously (no request, no call detail, no telemetry). In 3.x the operation did not exist on the type.
- **Another provider's options type throws** `ArgumentException` (for example `XAiCompletionOptions` passed to `OpenAiClient`). In 3.x its provider-specific fields were silently ignored.
- **Azure moves to the v1 API.** Requests go to `/openai/v1/responses`, `/openai/v1/embeddings`, and `/openai/v1/models` with the deployment name as `model`. The deployment-in-path routes and `api-version` are removed. Chat Completions on Azure: `OpenAiCompatibleClient` with endpoint `https://{resource}.openai.azure.com/openai/v1` and `AuthPlacement.Header("api-key")`.
- **xAI has no embeddings.** xAI lists no embedding models and documents no `/v1/embeddings`; `XAiClient.EmbedAsync` throws `CapabilityNotSupportedException`.
- **Continuation state is written in the 4.0 envelope** `{ protocol, provider, scope, value }`, including for xAI (which wrote a bare output array in 3.x). 3.x state, including persisted Mux sessions and xAI arrays, is still read. State from another provider, protocol, or endpoint now falls back to the plain message history. Chat Completions state is written with protocol `chat_completions` (3.x wrote `openai`).
- **New result fields:** `RawResponse`, `Citations` (never null), and `AppliedReasoningEffort` on every response.
- **Reasoning effort** adds `None`, `XHigh`, and `Max`. The enum values shift (`None` is now first), so code that persisted the numeric value should persist the name. See the mapping tables in [PROVIDERS.md](PROVIDERS.md#reasoning-effort): Anthropic maps `XHigh`/`Max` to `xhigh`/`max`, OpenAI sends every level by name, xAI omits reasoning for `None` and sends `xhigh` for `Max`, and other providers use `High`'s mapping for `XHigh` and `Max`.
- **Responses finish reasons:** `incomplete` with `content_filter` now reports `content_filter` (3.x reported `incomplete`); other incomplete reasons pass through by name. `cache_write_tokens` maps to `CacheCreationTokens`.
- **Telemetry:** the `polyprompt.capability` attribute comes from the operation called (same values as 3.x; connectivity probes report `connectivity`), the active-clients gauge reports `provider` for provider clients, and `polyprompt.reasoning.effort` is added. Provider labels are unchanged; `OpenAiCompatibleClient` reports `openai`.
- **TypeSafe, Anthropic, Gemini, Cohere, and VoyageAI require a key** at construction (a local Ollaya server accepts any value).
- **Ollama** strips a trailing `/v1` from its endpoint.

### Changed default models

| Client and section | 3.x default | 4.0 default |
|---|---|---|
| `OpenAiClient` completion | `gpt-4o-mini` (Chat Completions) | `gpt-6.1-sol` |
| `OpenAiCompatibleClient` completion | `gpt-4o-mini` | none (server-specific) |
| `OpenAiCompatibleClient` embedding | `text-embedding-3-small` | none (server-specific) |
| `AnthropicClient` completion | `claude-opus-4-8` | `claude-sonnet-5-5` |
| `GeminiClient` completion | `gemini-2.5-flash` | `gemini-3.8-flash` |
| `VertexAiClient` completion | `gemini-2.5-flash` | `gemini-3.8-flash` |
| `VertexAiClient` embedding | `text-embedding-005` | `gemini-embedding-001` (Vertex no longer lists `text-embedding-005`; vectors from the two models are not comparable) |
| `BedrockClient` completion | `anthropic.claude-3-5-sonnet-20240620-v1:0` | none (IDs and inference profiles vary by region and account) |
| `OllamaCloudClient` completion and embedding | `gemma3:4b` / `all-minilm` (inherited from local Ollama) | none (the hosted catalog changes) |
| `CohereClient` completion | `command-a-03-2025` | `command-a-plus-05-2026` |
| `CohereClient` rerank | `rerank-v3.5` | `rerank-v5.0-fast` |
| `VoyageAiClient` rerank | `rerank-2.5` | `rerank-3` |

A client with no default throws `InvalidOperationException` ("No model is configured. Set client.Defaults.Completion.Model (or options.Model). See ...") before sending.

## Provider-agnostic code

3.x code typed against a capability base becomes code typed against `ProviderClientBase` that checks `Supports`:

<!-- no-compile -->
```csharp
// 3.x
static Task<EmbeddingResponse> Embed(EmbeddingClientBase client, string text) => client.EmbedAsync(text);
```

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

static Task<EmbeddingResponse> Embed(ProviderClientBase client, string text)
{
    if (!client.Supports(Capability.Embed)) throw new InvalidOperationException(client.ProviderName + " has no embeddings.");
    return client.EmbedAsync(text);
}

using OllamaClient ollama = new OllamaClient();
EmbeddingResponse response = await Embed(ollama, "hello");
```

A list of mixed 3.x clients (`List<CompletionClientBase>`) becomes `List<ProviderClientBase>`; filter with `Where(c => c.Supports(Capability.Chat))`. Reading defaults through the base type: `client.Defaults.Completion?.Model`.

## Custom subclasses

A 3.x class derived from a capability client derives from the provider client instead. Override the protected `...CoreAsync` members (`ChatCoreAsync`, `EmbedCoreAsync`, `ListModelsCoreAsync`, ...); the public operations validate, check capabilities, merge options, and record telemetry before calling them. A client built from scratch derives from `ProviderClientBase` and implements `Capabilities`, `ProviderName`, `Defaults` (a `ClientDefaults` subclass with the sections it supports), `ValidateConnectivityAsync`, and the core members for its capabilities.

<!-- no-compile -->
```csharp
// 3.x
public sealed class AuditedClient : OpenAiCompletionClient
{
    public AuditedClient(string endpoint, string key) : base(endpoint, key) { }
}
```

```csharp
using PolyPrompt.Clients;

using AuditedClient audited = new AuditedClient("http://localhost:8000/v1", "key");

public sealed class AuditedClient : OpenAiCompatibleClient
{
    public AuditedClient(string endpoint, string key) : base(endpoint, key) { }

    protected override Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
    {
        request.Headers.TryAddWithoutValidation("x-audit", "on");
        return base.PrepareRequestAsync(request, body, token);
    }
}
```

## Checklist

1. Bump `PolyPrompt` to 4.0.0.
2. Replace every 3.x client with its 4.0 client (table above). For each `OpenAiCompletionClient`, decide `OpenAiClient` or `OpenAiCompatibleClient`.
3. Merge per-capability clients of one provider into one instance.
4. Move constructor arguments to the 4.0 order; put `logging`/`httpClient` in `ClientOptions`.
5. Replace `client.Model` and `client.Defaults.X` with `client.Defaults.<Section>.Model` and `client.Defaults.<Section>.X`.
6. Set a model on `OpenAiCompatibleClient`, `AzureOpenAiClient` (deployment), Bedrock completion, and `OllamaCloudClient`.
7. Rename `OpenAiCompletionOptions` (Chat Completions) to `OpenAiCompatibleCompletionOptions`, and the `XAi*` result types to `ResponsesApi*`.
8. Replace capability base types with `ProviderClientBase` plus `Supports`.
9. Rename the harness clients.
10. Review the changed default models, especially embedding models that change vector spaces (Vertex AI).
11. If you relied on OpenAI storing responses, set `Store = true`.
12. Rebuild: any remaining mismatch is a compile error, and another provider's options type is now an `ArgumentException` at the call.
