# PolyPrompt — New Providers Plan (Bedrock, Vertex AI, Azure OpenAI)

_Status: proposal / design doc. Follows the repo convention of a `<FEATURE>_SUPPORT.md` design doc that is
moved to `archive/` once shipped (see `archive/ANTHROPIC_SUPPORT.md`, `archive/VOYAGEAI_SUPPORT.md`)._

_Target library version at time of writing: **2.4.1**._

---

## 0. TL;DR — is there actually a gap?

**Partially. Two of the four commonly-requested "frontier" providers are already shipped; two are genuinely missing.**

| Provider | In PolyPrompt today? | Action |
|---|---|---|
| **OpenAI** (+ OpenAI-compatible) | ✅ `OpenAiClient` | none — done |
| **Anthropic Claude** (Messages API) | ✅ `AnthropicClient` (native `/v1/messages`) | none — done |
| **Google Gemini** (AI Studio) | ✅ `GeminiClient` (`generativelanguage` `:generateContent`) | none — done |
| **AWS Bedrock** | ❌ absent | **add `BedrockClient`** (this plan) |
| **Google Vertex AI** | ❌ absent (Gemini today is AI-Studio auth, **not** Vertex) | **add `VertexAiClient`** (this plan) |
| **Azure OpenAI** | ⚠️ works only by pointing `OpenAiClient` at a custom endpoint; no deployment routing / `api-version` / `api-key`/AAD auth as a first-class shape | **add `AzureOpenAiClient`** (this plan, optional/low-effort) |

So: **do not re-plan Anthropic or OpenAI — they exist.** This document plans **Bedrock**, **Vertex AI**, and
**Azure OpenAI**, covering **inference (chat + tools + streaming + reasoning) and embeddings**, plus tests,
CHANGELOG, and README updates.

> **Companion note for the primary consumer (mux).** mux routes all inference through PolyPrompt but its
> `LlmClient.CreateClient` switch only constructs `OllamaClient` or `OpenAiClient` (adapter types
> `ollama`/`openai`/`vllm`/`openai-compatible`). **mux therefore cannot use PolyPrompt's already-shipped
> `AnthropicClient`/`GeminiClient` today.** The cheapest frontier-model win is a *mux-side* change (new
> adapter enum values + `CreateClient` cases + config/docs) that is independent of this plan. See §10.

---

## 1. Design principles (honor the existing architecture)

The library is deliberately small and hand-rolled (README: "no official provider SDKs"). New providers must
fit the established seams rather than introduce a framework:

1. **One abstract base class, no interfaces/factory/enum.** Every provider is a
   `public class XClient : CompletionClientBase` (`src/PolyPrompt/Clients/`). There is no `IChatClient` and no
   central registry to update inside the library — dispatch is `new XClient(...)`. (Consumers keep their own
   switch; see §10.)
2. **Implement all 10 abstract methods; throw `NotSupportedException` for genuinely-absent capabilities**
   (the VoyageAI client is the reference for "embeddings-only, everything else throws"; Anthropic is the
   reference for "chat/tools/reasoning yes, embeddings no").
3. **Reuse the base HTTP plumbing** — `PostAndRecordAsync`, `GetAndRecordAsync`, `PostStreamingAsync`,
   `WrapToolChatChunksWithTiming` / `WrapChunksWithTiming` / `WrapGenerationChunksWithTiming`,
   `ResolveToolChatRequest` / `ResolveOptions`, and the parse helpers (`ParseFloatArray`, `TryGetInt`,
   `IsTruthy`, `NormalizeReasoning`). Providers only supply `Build*Body`, `Read*Chunks` iterators, and
   `Parse*`/`Extract*` for their wire shapes.
4. **JSON via `Dictionary<string,object>`/`List<object>` round-tripping through `SerializationHelper`** — no
   `System.Text.Json` POCOs/attributes. Match the existing style.
5. **No emulation.** If a provider can't do JSON-schema response_format or vision, don't fake it (consistent
   with the current "unsupported ops throw" policy).
6. **The one genuinely new architectural need is dynamic/expiring credentials** — see §2. Keep it as small and
   local as possible.

---

## 2. Cross-cutting: a per-request credential seam (the only real new concept)

Today auth is **static**: each ctor stamps `_HttpClient.DefaultRequestHeaders` once (OpenAI/Voyage bearer;
Anthropic `x-api-key`+`anthropic-version`) or injects a key into the URL (Gemini `?key=`). That model does not
fit the two new cloud providers:

- **AWS Bedrock** requires **SigV4** — every request is signed over its method, canonical path, headers, and a
  SHA-256 hash of the body, stamped with the current timestamp. Signature is per-request; it cannot live in
  `DefaultRequestHeaders`.
- **Google Vertex AI** requires a **short-lived OAuth access token** (from Application Default Credentials or a
  service-account key), which **expires (~1h)** and must be refreshed.

**Proposal — a minimal per-request hook on the base class** (additive, non-breaking):

```csharp
// CompletionClientBase (new, virtual, default no-op)
protected virtual Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken ct)
    => Task.CompletedTask;
```

- `PostAndRecordAsync` / `GetAndRecordAsync` / `PostStreamingAsync` call `PrepareRequestAsync` **after building
  the `HttpRequestMessage` and serializing the body, before send** — so a subclass can compute SigV4 or attach
  a fresh bearer token per request. Existing clients override nothing → behavior unchanged.
- **Prerequisite refactor (larger than it first appears).** Today only `PostStreamingAsync` builds an explicit
  `HttpRequestMessage`. The non-streaming paths call the convenience methods `_HttpClient.PostAsync(url,
  content, …)` and `_HttpClient.GetAsync(url, …)` (base class ~L672 / ~L530), which construct the request
  internally and never expose an `HttpRequestMessage` — so there is nothing for a per-request hook to mutate.
  Adding the seam therefore requires **rewriting `PostAndRecordAsync` and `GetAndRecordAsync` to build an
  `HttpRequestMessage` + `SendAsync`** (still additive and non-breaking, but a real change to the two hottest
  methods in the base class — every existing provider's chat/embed/generate flows through them). Gate the
  refactor on the existing hermetic suite passing unchanged, same as the Gemini extraction. Note also that
  SigV4 must read `Content-Type` (and any other content headers) off `request.Content.Headers`, **not**
  `request.Headers`, and that GET signs the hash of an empty body.
- Introduce a tiny credential abstraction consumed by the new clients (kept internal to those clients, not a
  public framework):
  - `ICredentialProvider { Task<string> GetBearerTokenAsync(CancellationToken) }` for Vertex/Azure-AAD, with
    concrete `StaticTokenCredential`, `ServiceAccountCredential` (JWT→token exchange), and
    `AdcCredential` (env `GOOGLE_APPLICATION_CREDENTIALS` / metadata server).
  - `IAwsCredentialProvider { AwsCredentials Resolve() }` (access key / secret / session token / region) with
    `StaticAwsCredential` and `EnvironmentAwsCredential` (`AWS_*` env vars, `~/.aws`).

**Dependency decision (OPEN — see §11).** SigV4 and GCP JWT signing are non-trivial. Two options:
- **(A) Hand-roll** a minimal SigV4 signer and a service-account JWT signer (keeps the zero-SDK ethos; ~1 file
  each; well-documented algorithms). **Recommended default.**
- **(B) Take a narrow dependency** — `AWSSDK.Core` (SigV4 + credential resolution) and
  `Google.Apis.Auth` (ADC/JWT) — less code, but breaks the "two dependencies only" posture.

This plan assumes **(A)** and isolates the crypto in `src/PolyPrompt/Auth/` so a later swap to (B) is contained.

---

## 3. Provider 1 — Azure OpenAI (`AzureOpenAiClient`) — do first (lowest effort)

Azure OpenAI is ~95% OpenAI-compatible; the differences are **URL shape**, **auth**, and **model==deployment**.

- **Endpoint / routing:** `https://{resource}.openai.azure.com/openai/deployments/{deployment}/{op}?api-version={apiVersion}`
  where `{op}` ∈ `chat/completions`, `embeddings`, `completions`. `Model` maps to the **deployment name**.
- **Auth:** header `api-key: {key}` **or** AAD `Authorization: Bearer {token}` (via the §2 credential seam).
- **Implementation:** `public class AzureOpenAiClient : OpenAiClient` — override only:
  - URL construction (replace `BuildApiUrl`'s `/v1/...` with the deployment path + `api-version` query).
    **Prerequisite:** `OpenAiClient.BuildApiUrl` is currently `private` (OpenAiClient ~L657), so it cannot be
    overridden as-is. Promote it to `protected virtual` first — a trivial, behavior-preserving edit to
    `OpenAiClient` itself, but note that this "thin subclass" provider does touch the shared OpenAI client.
    All existing call sites (`chat/completions`, `embeddings`, `completions`, `models`) already route through
    it, so a single virtual override covers Azure's URL shape.
  - auth header registration (`api-key` instead of bearer; or AAD bearer via `PrepareRequestAsync`),
  - a required `ApiVersion` property (default a current GA value; user-overridable).
  - Everything else — request/response body, tool-call delta assembly, reasoning (`reasoning_effort`),
    streaming SSE, usage — **inherits from `OpenAiClient` unchanged.**
- **Inference:** chat, tool-chat, streaming, generation — inherited.
- **Embeddings:** inherited OpenAI `data[].embedding` parsing; `AzureOpenAiEmbeddingOptions : OpenAiEmbeddingOptions`
  (Dimensions/EncodingFormat) if desired.
- **Reasoning effort:** reuses `ToOpenAiWireValue()` — **no `ReasoningEffort.cs` change needed.**
- **Model mgmt:** `ListModelsAsync` → Azure `GET /openai/models?api-version=...`; `Pull/DeleteModelAsync` throw.

**Files:** `src/PolyPrompt/Clients/AzureOpenAiClient.cs` (new, thin),
`src/PolyPrompt/Clients/OpenAiClient.cs` (edit — promote `BuildApiUrl` to `protected virtual`), optional
`src/PolyPrompt/Options/AzureOpenAi*Options.cs`, `src/AzureOpenAIConsole/` (optional harness).

---

## 4. Provider 2 — Google Vertex AI (`VertexAiClient`) — reuse Gemini wire

Vertex serves the **same Gemini `generateContent` content schema** as the existing `GeminiClient` — so the
**body building and response/stream parsing are reusable** — but differs in **base URL, model path, auth, and
embeddings shape**.

- **Endpoint / routing:** `https://{region}-aiplatform.googleapis.com/v1/projects/{project}/locations/{region}/publishers/google/models/{model}:generateContent`
  (and `:streamGenerateContent?alt=sse`). Requires `Project`, `Region` properties.
- **Auth:** `Authorization: Bearer {access_token}` from the §2 credential provider (ADC / service account),
  refreshed per request via `PrepareRequestAsync`. **Not** an API key.
- **Reuse strategy:** extract Gemini's wire logic into a shared internal helper (e.g.
  `GeminiWireProtocol` static/partial) that both `GeminiClient` and `VertexAiClient` call, OR make
  `VertexAiClient : GeminiClient` and override only URL building + auth + embeddings. **Prefer extracting a
  shared helper** to avoid an inheritance coupling that leaks AI-Studio auth into Vertex. Keep the change to
  `GeminiClient` a pure refactor (covered by its existing hermetic tests).
- **Inference:** chat, tool-chat (`functionDeclarations` / `toolConfig.functionCallingConfig`), streaming SSE,
  reasoning (truthy `thought` parts) — all reused from the Gemini protocol.
- **Embeddings (different from AI Studio!):** Vertex uses `:predict` on an embedding model
  (`.../models/text-embedding-004:predict`) with `{ "instances": [{ "content": "..." }] }` and reads
  `predictions[].embeddings.values`. This needs a **Vertex-specific embed body/parse** (not the AI-Studio
  `:embedContent` path). `VertexAiEmbeddingOptions : EmbeddingOptions` (e.g. `TaskType`, `OutputDimensionality`,
  `AutoTruncate`).
- **Reasoning effort:** reuses `ToGeminiThinkingBudget()` — **no `ReasoningEffort.cs` change needed.**
- **Model mgmt:** `ListModelsAsync` via Vertex `publishers/google/models` (or throw if scoped out);
  `Pull/DeleteModelAsync` throw.

**Files:** `src/PolyPrompt/Clients/VertexAiClient.cs` (new), `src/PolyPrompt/Clients/GeminiWireProtocol.cs`
(new, extracted), `src/PolyPrompt/Auth/*` (credential providers), `src/PolyPrompt/Options/VertexAi*Options.cs`,
`src/VertexAIConsole/` (optional).

> Note: Vertex can also serve Anthropic and Llama models under different publisher paths. **Scope this plan to
> Gemini-on-Vertex**; Anthropic-on-Vertex can be a later follow-up that reuses `AnthropicClient`'s body with a
> Vertex URL/auth.

---

## 5. Provider 3 — AWS Bedrock (`BedrockClient`) — highest effort

Bedrock is the most work: **SigV4 auth**, a **region-routed endpoint**, and **model-family-specific bodies**
(Bedrock multiplexes Anthropic, Amazon Titan/Nova, Meta Llama, Cohere, Mistral behind one API).

- **Endpoint / routing:** `https://bedrock-runtime.{region}.amazonaws.com/model/{modelId}/invoke` and
  `/invoke-with-response-stream`; **prefer the unified `Converse` / `ConverseStream` API**
  (`/model/{modelId}/converse`) which normalizes messages/tools/usage across families and dramatically reduces
  per-family branching. Requires `Region`.
- **Auth:** **SigV4** over each request (service `bedrock`), via §2 `PrepareRequestAsync` +
  `IAwsCredentialProvider`. Supports static keys and `AWS_*` env/role credentials (incl. session token).
- **Inference (Converse):**
  - `BuildConverseBody` — map `ChatMessage`/`ToolChatRequest` → Converse `messages[]` (role + content blocks),
    `system[]`, `toolConfig.tools[].toolSpec` (name/description/`inputSchema.json`), `inferenceConfig`
    (maxTokens/temperature/topP).
  - Response: read `output.message.content[]` (text + `toolUse` blocks → `ToolCall`), `stopReason`, and
    `usage` (`inputTokens`/`outputTokens`/`totalTokens`) → `ChatStreamingUsage`.
  - **Streaming (`ConverseStream`):** AWS **event-stream** framing (`application/vnd.amazon.eventstream`) —
    binary length-prefixed frames, **not** SSE/NDJSON. Needs a small event-stream frame decoder (a new
    `ReadBedrockEventStream` iterator) feeding `ToolChatStreamingChunk`s (`contentBlockDelta.text`,
    `toolUse.input` fragments → `ToolCallDelta`, `metadata.usage`). This is the single most novel piece; budget
    accordingly.
  - Reasoning: Anthropic-on-Bedrock exposes `reasoningContent` blocks in Converse → map to `ReasoningText`.
- **Reasoning effort:** map via a new `ToBedrockConverse(...)` projection (Converse
  `additionalModelRequestFields` for Anthropic thinking budget, etc.). **Adds a method + optional override
  property to `ReasoningEffort.cs`** (mirrors `ToAnthropicEffort()`).
- **Embeddings:** `InvokeModel` on an embedding model — family-specific bodies:
  - Amazon Titan (`amazon.titan-embed-text-v2:0`): `{ "inputText": "..." }` → `{ "embedding": [...] }`
    (single-input only → loop for batch).
  - Cohere (`cohere.embed-english-v3`): `{ "texts": [...], "input_type": "..." }` → `{ "embeddings": [...] }`
    (native batch).
  - `BedrockEmbeddingOptions : EmbeddingOptions` (`InputType`, `Dimensions`, `Normalize`), with a small
    per-family switch keyed off the model id.
- **Generation (`GenerateAsync`):** map onto Converse single-turn, or throw `NotSupportedException` if scoped
  out (document either way).
- **Model mgmt:** `ListModelsAsync` via `bedrock.{region}.amazonaws.com` `GET /foundation-models` (control-plane
  host, also SigV4); `Pull/DeleteModelAsync` throw.

**Files:** `src/PolyPrompt/Clients/BedrockClient.cs` (new), `src/PolyPrompt/Auth/SigV4Signer.cs`,
`src/PolyPrompt/Auth/AwsCredentials.cs` + providers, `src/PolyPrompt/Wire/EventStreamDecoder.cs` (new),
`src/PolyPrompt/Options/Bedrock*Options.cs`, `src/PolyPrompt/Models/ReasoningEffort.cs` (edit),
`src/BedrockConsole/` (optional).

---

## 6. ReasoningEffort changes (only Bedrock needs one)

`src/PolyPrompt/Models/ReasoningEffort.cs` currently holds `Level` + per-provider overrides
(`OpenAiValue`, `GeminiThinkingBudget`, `OllamaThink`, `AnthropicEffort`) with projections
`ToOpenAiWireValue()`, `ToGeminiThinkingBudget()`, `ToOllamaThink()`, `ToAnthropicEffort()`,
`SendsAnthropicThinking()`.

- **Azure** → reuse `ToOpenAiWireValue()`. No change.
- **Vertex** → reuse `ToGeminiThinkingBudget()`. No change.
- **Bedrock** → **add** `BedrockThinkingBudget` (or reuse `AnthropicEffort` when the underlying model is
  Anthropic) + `ToBedrockConverse()` projection. Keep the "unset ⇒ field omitted" contract.

---

## 7. Test plan (mirror the Touchstone + hermetic-HTTP pattern exactly)

Tests are declared once as Touchstone `TestCaseDescriptor`s in `Test.Shared` and run via `Test.Automated`
(`selftest`), `Test.Xunit`, and `Test.Nunit`. Two tiers:

### 7.1 Hermetic (always run — no network)
Extend `src/Test.Shared/LocalOpenAiTestServer.cs` + `LocalRequestParser.cs`:
- **Azure:** add routes for `/openai/deployments/{deployment}/chat/completions`, `/embeddings`,
  `/completions`, `/models` keyed on the `api-version` query and `api-key` header; add
  `LocalAzureRequest` DTOs. Assert URL/deployment/`api-version`/auth-header translation.
- **Vertex:** add routes for `.../models/{model}:generateContent`, `:streamGenerateContent`, `:predict`
  (embeddings); assert `Authorization: Bearer` present, project/region path, and `:predict` instances body.
- **Bedrock:** add routes for `/model/{id}/converse`, `/converse-stream` (emit a canned **event-stream**
  binary frame sequence to exercise the decoder), and `/model/{id}/invoke` (embeddings); assert the request is
  **SigV4-signed** (presence + shape of `Authorization: AWS4-HMAC-SHA256 ...`, `x-amz-date`,
  `x-amz-content-sha256`) without needing real AWS.

Add cases to `src/Test.Shared/LocalBehaviorSuite.cs` and register in `Create()`. Concrete case ids to add
(mirroring existing names like `openai_tool_chat_streaming`, `anthropic_tool_choice_translation`):

```
azure_chat_translation                 azure_embeddings_translation
azure_tool_chat_streaming              azure_apiversion_and_auth_header
azure_reasoning_effort_passthrough
vertex_generatecontent_translation     vertex_tool_chat_streaming
vertex_predict_embeddings              vertex_oauth_bearer_attached
vertex_reasoning_thinkingbudget
bedrock_converse_translation           bedrock_converse_stream_eventframes
bedrock_tooluse_assembly               bedrock_titan_embeddings
bedrock_cohere_embeddings              bedrock_sigv4_signature_shape
bedrock_reasoning_converse
credential_refresh_per_request         unsupported_ops_throw_<provider>
injected_http_client_<provider>        streaming_body_timeout_<provider>
```

Also add SigV4 unit cases against the **AWS-published canonical test vectors** (`sigv4_canonical_request`,
`sigv4_signing_key`, `sigv4_authorization_header`) so the signer is verified independent of Bedrock.

### 7.2 Live (opt-in, gated on env)
Extend `src/Test.Shared/ProviderLiveSuite.cs` (its `CreateClient` switch),
`src/Test.Shared/ProviderTestConfiguration.cs` (`NormalizeProviderType`, `ResolveDefaultEndpoint`,
`ResolveEmbeddingModelName`, `FromProviderSpecificEnvironment`), and `src/Test.Automated/Program.cs` (CLI args
+ provider switch). New provider strings + env groups:
- `azure` → `POLYPROMPT_TEST_AZURE_ENDPOINT`, `_AZURE_API_KEY`, `_AZURE_DEPLOYMENT`, `_AZURE_API_VERSION`,
  `_AZURE_EMBED_DEPLOYMENT`.
- `vertex` → `_VERTEX_PROJECT`, `_VERTEX_REGION`, `GOOGLE_APPLICATION_CREDENTIALS` (or `_VERTEX_ACCESS_TOKEN`),
  `_VERTEX_MODEL`, `_VERTEX_EMBED_MODEL`.
- `bedrock` → `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, `AWS_SESSION_TOKEN?`, `_BEDROCK_REGION`,
  `_BEDROCK_MODEL`, `_BEDROCK_EMBED_MODEL`.
Skip capabilities a provider lacks and assert `NotSupportedException` where applicable (same policy as the
Anthropic-embeddings / VoyageAI-chat skips today). Keep the "exactly one provider env group" enforcement.

---

## 8. Documentation updates

### 8.1 CHANGELOG.md (Keep-a-Changelog; newest-first; `## vX.Y.Z (YYYY-MM-DD)` with `### Added/Changed/Fixed/Tests`)
Ship one **minor version per provider** (matches history: Anthropic = 2.3.0, VoyageAI = 2.4.0):
- **v2.5.0 — Azure OpenAI:** `### Added` (client, deployment/api-version routing, api-key + AAD auth,
  embeddings, inherited tools/streaming/reasoning, console harness); `### Tests`; `### Changed` (README, version).
- **v2.6.0 — Google Vertex AI:** `### Added` (client, ADC/service-account auth + per-request token refresh,
  reused Gemini wire, `:predict` embeddings); `### Changed` (note `GeminiWireProtocol` refactor — behavior
  unchanged, covered by existing tests); `### Tests`.
- **v2.7.0 — AWS Bedrock:** `### Added` (client, SigV4, Converse/ConverseStream, event-stream decoder,
  Titan/Cohere embeddings, Bedrock reasoning projection); `### Tests` (incl. SigV4 vectors); `### Changed`.

Each entry documents endpoint, auth, supported/unsupported surface, options, and reasoning projection —
matching the depth of the existing Anthropic/VoyageAI entries.

### 8.2 README.md (~910 lines — update every provider-enumerating section)
- Intro/tagline line (add Azure, Vertex, Bedrock to the supported list) and package `tags`.
- A **quick-start block** per provider (ctor, auth setup, a chat + an embeddings snippet).
- **Provider Feature Support matrix** — add three rows (chat / tools / streaming / reasoning / embeddings /
  generation / model-listing / pull-delete), honestly marking Bedrock generation and any `NotSupported` cells.
- **Provider options table** (`Azure*Options`, `VertexAi*Options`, `Bedrock*Options`).
- **Default-models table** (e.g. `gpt-4o` deployment, `gemini-1.5-pro`, `anthropic.claude-3-5-sonnet…`,
  `amazon.titan-embed-text-v2:0`).
- The illustrative `CreateClient(provider, endpoint, apiKey)` example (~L660) — add `azure`/`vertex`/`bedrock`
  cases **and a note that Bedrock/Vertex take credential providers, not a bare apiKey/endpoint**.
- Auth section — document SigV4 (AWS creds/region/env) and Vertex ADC/service-account + token refresh.
- Project-structure + build/test-run examples (new console projects, new env-var groups).
- Bump "Current documented package version".

### 8.3 csproj + design docs
- `src/PolyPrompt/PolyPrompt.csproj`: bump `<Version>` per release and extend `<PackageTags>`
  (`azure;bedrock;vertex;aws;gcp;sigv4`).
- Produce `archive/AZURE_OPENAI_SUPPORT.md`, `archive/VERTEX_SUPPORT.md`, `archive/BEDROCK_SUPPORT.md`
  (this doc splits into those on ship), per the repo's `<FEATURE>_SUPPORT.md` convention.

---

## 9. Files touched — master checklist

**Library (new):** `Clients/AzureOpenAiClient.cs`, `Clients/VertexAiClient.cs`, `Clients/GeminiWireProtocol.cs`
(extracted), `Clients/BedrockClient.cs`, `Auth/SigV4Signer.cs`, `Auth/AwsCredentials.cs` (+ providers),
`Auth/ICredentialProvider.cs` (+ Static/ServiceAccount/Adc), `Wire/EventStreamDecoder.cs`,
`Options/AzureOpenAi*Options.cs`, `Options/VertexAi*Options.cs`, `Options/Bedrock*Options.cs`.
**Library (edit):** `Clients/CompletionClientBase.cs` (add `PrepareRequestAsync` hook + **rewrite
`PostAndRecordAsync`/`GetAndRecordAsync` to build `HttpRequestMessage` + `SendAsync`** so the hook has a request
to mutate; add the call site in `PostStreamingAsync`), `Clients/OpenAiClient.cs` (promote `BuildApiUrl` to
`protected virtual` for the Azure subclass), `Clients/GeminiClient.cs` (refactor to shared wire — no behavior
change), `Models/ReasoningEffort.cs` (Bedrock projection), `PolyPrompt.csproj`.
**Tests (edit):** `Test.Shared/LocalOpenAiTestServer.cs`, `LocalRequestParser.cs`, `LocalBehaviorSuite.cs`,
`ProviderLiveSuite.cs`, `ProviderTestConfiguration.cs`, `Test.Automated/Program.cs`.
**Solution/harness (optional):** `src/AzureOpenAIConsole/`, `src/VertexAIConsole/`, `src/BedrockConsole/` +
`PolyPrompt.sln` entries.
**Docs:** `README.md`, `CHANGELOG.md`, `archive/*_SUPPORT.md`.

---

## 10. Consumer integration (mux) — required to expose any of this to users

PolyPrompt exposes no factory, so the consumer's own dispatch must learn the new clients. In **mux**:
- Add `AdapterTypeEnum` values: `Anthropic`, `Gemini` (**already-shipped PolyPrompt clients — immediate win**),
  and later `AzureOpenAi`, `Vertex`, `Bedrock`.
- Add `case`s to `LlmClient.CreateClient` constructing the corresponding PolyPrompt client, and thread the new
  auth inputs (Bedrock creds/region; Vertex project/region/credentials; Azure deployment/api-version) through
  `EndpointConfig` + the endpoint Add/Edit wizard + `CONFIG.md`.
- mux's static-header auth model (`EndpointConfig.Headers`) suffices for Anthropic/Gemini/Azure-key, but
  **Bedrock/Vertex need the dynamic-credential fields**, so `EndpointConfig` gains optional provider-credential
  sub-objects.

**Sequencing recommendation:** ship the **mux `anthropic`/`gemini` adapter types first** (no PolyPrompt work —
pure consumer wiring against existing clients); then Azure (small); then Vertex; then Bedrock.

---

## 11. Risks & open questions

1. **Dependency posture (§2).** Hand-roll SigV4 + GCP JWT (keeps 2-dependency ethos, more code to test against
   published vectors) vs. take `AWSSDK.Core` / `Google.Apis.Auth` (less code, breaks the posture). **Decision
   needed before Bedrock/Vertex start.** Recommendation: hand-roll, isolated under `Auth/`.
2. **Bedrock event-stream decoding** is the highest-novelty item (binary framed protocol, unlike every existing
   SSE/NDJSON path). De-risk with a standalone `EventStreamDecoder` + unit tests before wiring streaming.
3. **Vertex token refresh** — expiring tokens mean the per-request `PrepareRequestAsync` hook must cache +
   refresh with a safety margin; add a hermetic "refresh-on-expiry" test.
4. **Bedrock model-family sprawl** — `Converse` normalizes most of it; **scope embeddings to Titan + Cohere**
   initially and document the rest as future work rather than branching for every family.
5. **`GeminiClient` refactor** must be behavior-preserving — gate on the existing Gemini hermetic suite passing
   unchanged.
6. **Azure `api-version`** values drift; expose it as a user-settable property with a sensible default rather
   than hard-coding.

---

## 12. Effort estimate (rough, relative)

| Item | Effort | Notes |
|---|---|---|
| `PrepareRequestAsync` seam + credential abstractions | S | additive, non-breaking |
| Azure OpenAI client + tests + docs | S | thin subclass of `OpenAiClient` |
| Gemini wire extraction (refactor) | S–M | must stay behavior-preserving |
| Vertex client + `:predict` embeddings + auth + tests + docs | M | reuses Gemini wire; new auth + embed path |
| SigV4 signer + AWS creds + vectors | M | crypto correctness is the risk |
| Bedrock Converse (non-stream) + tools + embeddings + tests | M | Converse normalizes families |
| Bedrock event-stream decoder + streaming + tests | M–L | the novel piece |
| Reasoning projection (Bedrock) | S | mirrors `ToAnthropicEffort()` |
| README/CHANGELOG/csproj/archive docs (×3 releases) | M | many enumerating sections |

**Order:** credential seam → Azure → Gemini refactor → Vertex → SigV4 → Bedrock (non-stream) → Bedrock stream.
