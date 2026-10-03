# PolyPrompt Telemetry

PolyPrompt emits metrics and traces for every call it makes to a model provider, so an operator can see from dashboards and traces alone where the time went (which provider, which operation, network or queueing or credential refresh) and what failed (HTTP status, timeout, transport error, unusable response).

PolyPrompt is a library. It emits through the .NET base class library only (`System.Diagnostics.Metrics.Meter` and `System.Diagnostics.ActivitySource`) and takes no dependency on OpenTelemetry, Radiant, or any exporter. Your host application chooses the collector and the backend (Prometheus, Tempo, Loki, Grafana, or any OTLP endpoint). When nothing is listening, each instrumented call costs a few boolean checks and two small delegate allocations, which is negligible next to the HTTP request it wraps.

## Contents

1. [Quick start](#quick-start)
2. [Meter and activity source](#meter-and-activity-source)
3. [Configuration](#configuration)
4. [Spans](#spans)
5. [Metrics catalog](#metrics-catalog)
6. [Attributes and values](#attributes-and-values)
7. [Prometheus names](#prometheus-names)
8. [Recommended alerts](#recommended-alerts)
9. [Dashboard map](#dashboard-map)
10. [Design notes and limits](#design-notes-and-limits)

## Quick start

Subscribe your collector to the meter and activity source named `PolyPrompt`. Nothing else is required: there is no switch inside PolyPrompt to turn on.

### With Radiant

[Radiant](https://www.nuget.org/packages/Radiant) is a .NET telemetry host that exports to Prometheus, OTLP (Tempo), and Loki. Add it to your application (not to PolyPrompt) and subscribe to PolyPrompt alongside your own sources:

```csharp
RadiantSettings settings = new RadiantSettings("my-service");
settings.Sources.AddMeter("PolyPrompt");
settings.Sources.AddActivitySource("PolyPrompt");

using (RadiantHost host = RadiantHost.Start(settings))
{
    // run the application
}
```

You can use the constants instead of string literals: `PolyPromptTelemetryNames.MeterName` and `PolyPromptTelemetryNames.ActivitySourceName`.

### With the OpenTelemetry SDK

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter(PolyPromptTelemetryNames.MeterName).AddOtlpExporter())
    .WithTracing(t => t.AddSource(PolyPromptTelemetryNames.ActivitySourceName).AddOtlpExporter());
```

### Ad hoc

`dotnet-counters monitor --counters PolyPrompt -p <pid>` shows the live metrics of a running process. A `MeterListener` or `ActivityListener` in your own code works too; the test suite does exactly that (see `src/Test.Shared/TelemetryCapture.cs`).

## Meter and activity source

| Name | Type | Version |
|------|------|---------|
| `PolyPrompt` | `Meter` | the package version, for example `3.1.1` |
| `PolyPrompt` | `ActivitySource` | the package version |

These names, every metric name, every attribute key, and every bounded attribute value are public contract, defined in `PolyPrompt.Telemetry.PolyPromptTelemetryNames`. They change only in a major version.

The `LoggingModule` PolyPrompt logs through (SyslogLogging 2.3 and later) emits its own metrics and spans on a separate `Meter` and `ActivitySource` named `SyslogLogging` (`SyslogLogging.SyslogLoggingTelemetry`). Subscribing to `PolyPrompt` does not pick them up; add those names too if you want them.

## Configuration

PolyPrompt has no telemetry settings of its own: emission is controlled entirely by whether a collector subscribes. Everything else is configured in the host:

| Concern | Where it is configured |
|---------|------------------------|
| Enable or disable | Subscribe or do not subscribe to `PolyPrompt` in your collector. |
| Sampling | Your tracer's sampler. PolyPrompt honors the sampling decision and records span attributes only for sampled spans. |
| Histogram buckets | Your collector's views. Suggested boundaries for the `*.duration` histograms: `0.01, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60, 120` seconds (model calls are slower than typical HTTP). |
| Dropping a high-detail label | Your collector's views, for example dropping `gen_ai.request.model` if you pass many distinct model names. |
| Export endpoints, service name | Your collector (for example Radiant's settings, modeled on `127.0.0.1` loopback defaults). |

The client settings that shape what you see are the existing ones: `TimeoutMs` (what counts as a timeout), `MaxConcurrency` on decision clients (the decision queue), and `PullTimeout` on `OllamaModelClient`.

## Spans

All spans come from the `PolyPrompt` activity source. Each span sets its status explicitly: `Ok` on success, `Error` with a short description (`HTTP 429`, `timeout`, or an exception type) on failure, and unset on caller cancellation. Exceptions are attached as OpenTelemetry `exception` events. Request and response bodies, prompts, embeddings, and credentials are never recorded.

| Span name | Kind | Emitted by | Parent | Key attributes |
|-----------|------|------------|--------|----------------|
| `{provider} {operation}` (for example `openai chat`, `aws.bedrock embed`, `typesafe decide`) | Client | Every public operation: `ChatAsync`, `ChatStreamingAsync`, `ToolChatAsync`, `ToolChatStreamingAsync`, `GenerateAsync`, `GenerateStreamingAsync`, `EmbedAsync`, `EmbedSparseAsync`, `RerankAsync`, `ClassifyAsync`, `DecideAsync`, `GetModelInformationAsync`, `ModelExistsAsync`, `ValidateConnectivityAsync`, and Ollama `PullModelAsync` and `DeleteModelAsync` | The caller's current span (for example the Watson request span) | `gen_ai.provider.name`, `gen_ai.operation.name`, `polyprompt.operation`, `polyprompt.capability`, `gen_ai.request.model`, `gen_ai.request.max_tokens`, `gen_ai.request.temperature`, `gen_ai.request.top_p`, `gen_ai.response.model`, `gen_ai.response.id`, `gen_ai.response.finish_reasons`, `gen_ai.usage.input_tokens`, `gen_ai.usage.output_tokens`, `polyprompt.usage.*`, `polyprompt.tool.definitions`, `polyprompt.tool.calls`, `polyprompt.batch.size`, `polyprompt.stream.chunks`, `polyprompt.stream.time_to_first_chunk_ms`, `http.response.status_code`, `server.address`, `server.port`, `polyprompt.outcome`, `error.type` |
| `{provider} decide_batch` | Client | `DecideAsync(List<DecisionRequest>)` | Caller's span | `polyprompt.batch.size`, `polyprompt.decision.max_concurrency`, outcome. Error with `batch_item_failed` when any item failed. |
| `stage:queued` | Internal | Each decision batch item while it waits for a concurrency slot | `{provider} decide_batch` | `gen_ai.provider.name`, `polyprompt.decision.max_concurrency` |
| `GET`, `POST`, `DELETE` | Client | Every outbound HTTP request to a provider | The operation span | `http.request.method`, `http.response.status_code`, `url.full` (query string removed), `server.address`, `server.port`, `gen_ai.provider.name`, `error.type` |
| `{credential source} token` (for example `service_account token`, `adc token`) | Client | A credential cache miss that fetches a new OAuth token (Vertex AI) | The operation that needed the token | `polyprompt.credential.source`, outcome, `error.type` |

A streaming operation's span stays open until the stream ends, so its duration covers the whole response. It ends as `success` when the stream completes, `error` or `timeout` when enumeration throws, `cancelled` when the caller cancels, and `abandoned` when the caller stops enumerating early. The streaming HTTP span ends when the response is disposed.

Batch items run on background tasks; the batch span is captured in the execution context, so every `decide` and `stage:queued` span nests under it. The HTTP span is current while the request is sent, so HttpClient's W3C propagation sends a `traceparent` in your trace to the provider.

A typical trace:

```text
POST /answer                              (Watson server span, from your service)
  openai chat                             client, 1.84 s, gen_ai.usage.input_tokens=812
    POST                                  client, 1.83 s, http.response.status_code=200
  gcp.vertex_ai embed                     client, 0.21 s
    service_account token                 client, 0.09 s (cache miss)
    POST                                  client, 0.11 s
  typesafe decide_batch                   client, 0.64 s, polyprompt.batch.size=8
    stage:queued                          internal, 0.00 s
    typesafe decide                       client, 0.31 s
      POST                                client, 0.30 s
    stage:queued                          internal, 0.31 s   (waited for a slot)
    ...
```

## Metrics catalog

Units follow UCUM. Durations are seconds. No quantiles are computed in process: derive p50, p95, and p99 from histogram buckets in Prometheus or Grafana.

### Operations

| Name | Type | Unit | Labels | Description |
|------|------|------|--------|-------------|
| `polyprompt.client.operation.duration` | Histogram | `s` | `gen_ai.provider.name`, `polyprompt.capability`, `polyprompt.operation`, `polyprompt.outcome`, `error.type` | Duration of each public operation; streaming operations are measured to the end of the stream. |
| `polyprompt.client.operations` | Counter | `{operation}` | same as above | Completed operations by outcome. |
| `polyprompt.client.operation.active` | Observable up-down counter | `{operation}` | `gen_ai.provider.name`, `polyprompt.operation` | Operations in progress. |
| `polyprompt.client.batch.size` | Histogram | `{item}` | `gen_ai.provider.name`, `polyprompt.operation` | Inputs per request: embedding inputs, rerank documents, classification inputs, decision questions, decision batch size. |

### GenAI (OpenTelemetry semantic conventions)

| Name | Type | Unit | Labels | Description |
|------|------|------|--------|-------------|
| `gen_ai.client.operation.duration` | Histogram | `s` | `gen_ai.operation.name`, `gen_ai.provider.name`, `gen_ai.request.model`, `error.type` | Model call duration (chat, text_completion, embeddings, rerank, classify, decide). |
| `gen_ai.client.token.usage` | Histogram | `{token}` | `gen_ai.operation.name`, `gen_ai.provider.name`, `gen_ai.request.model`, `gen_ai.token.type` (`input`, `output`) | Tokens per call, when the provider reports usage. |
| `polyprompt.client.tokens` | Counter | `{token}` | `gen_ai.provider.name`, `polyprompt.operation`, `gen_ai.request.model`, `polyprompt.token.type` (`input`, `output`, `cached_input`, `cache_creation`, `reasoning`) | Token consumption for rate and cost panels, including the cache and reasoning breakdowns. |
| `polyprompt.client.stream.time_to_first_chunk` | Histogram | `s` | `gen_ai.provider.name`, `polyprompt.operation` | Time from the call to the first content chunk of a stream. |
| `polyprompt.client.stream.chunks` | Counter | `{chunk}` | `gen_ai.provider.name`, `polyprompt.operation` | Content chunks delivered by streams. |
| `polyprompt.client.tool_calls` | Counter | `{call}` | `gen_ai.provider.name`, `polyprompt.operation` | Tool calls requested by the model. |
| `polyprompt.client.finish_reasons` | Counter | `{response}` | `gen_ai.provider.name`, `polyprompt.operation`, `gen_ai.response.finish_reason` | Completions by finish reason; `length` or `max_tokens` means truncated output. |

### Outbound HTTP (integrations)

| Name | Type | Unit | Labels | Description |
|------|------|------|--------|-------------|
| `polyprompt.http.client.request.duration` | Histogram | `s` | `gen_ai.provider.name`, `http.request.method`, `http.response.status_code`, `error.type`, `server.address`, `server.port` | Duration of each request to a provider. Streaming requests are measured until the body is read. Credential refresh is excluded (it has its own metric). |
| `polyprompt.http.client.requests` | Counter | `{request}` | same as above | Requests by status and error type. |
| `polyprompt.http.client.active_requests` | Observable up-down counter | `{request}` | `gen_ai.provider.name`, `http.request.method` | Requests in flight. |
| `polyprompt.http.client.request.body.size` | Histogram | `By` | `gen_ai.provider.name`, `http.request.method` | Request body size. |
| `polyprompt.http.client.response.body.size` | Histogram | `By` | `gen_ai.provider.name`, `http.request.method` | Response body size (non-streaming requests). |

### Decision batches (queue and concurrency limiter)

| Name | Type | Unit | Labels | Description |
|------|------|------|--------|-------------|
| `polyprompt.decision.batch.queue.duration` | Histogram | `s` | `gen_ai.provider.name` | Time each batch item waited for a concurrency slot (the queued stage). |
| `polyprompt.decision.batch.queued` | Observable up-down counter | `{request}` | `gen_ai.provider.name` | Items waiting for a slot. |
| `polyprompt.decision.batch.in_flight` | Observable up-down counter | `{request}` | `gen_ai.provider.name` | Items holding a slot. The capacity is the client's `MaxConcurrency`, recorded on the batch span. |
| `polyprompt.decision.questions` | Counter | `{question}` | `gen_ai.provider.name`, `polyprompt.question.type` (`binary`, `choice`, `score`) | Questions sent, by type. |

### Credentials (cache)

| Name | Type | Unit | Labels | Description |
|------|------|------|--------|-------------|
| `polyprompt.credential.refresh.duration` | Histogram | `s` | `polyprompt.credential.source`, `polyprompt.outcome`, `error.type` | Duration of OAuth token fetches (service account exchange or GCE metadata server). |
| `polyprompt.credential.refreshes` | Counter | `{refresh}` | same as above | Token fetches by outcome. |
| `polyprompt.credential.cache.lookups` | Counter | `{lookup}` | `polyprompt.credential.source`, `polyprompt.cache.result` (`hit`, `miss`) | Token cache lookups. |

### Lifecycle and build

| Name | Type | Unit | Labels | Description |
|------|------|------|--------|-------------|
| `polyprompt.clients.active` | Observable up-down counter | `{client}` | `gen_ai.provider.name`, `polyprompt.capability` | Clients constructed and not yet disposed. A value that only grows means clients are not being disposed. |
| `polyprompt.build.info` | Observable gauge | `{build}` | `polyprompt.version` | Always 1; carries the library version. |

Runtime metrics (GC, thread pool, exceptions) belong to the host: enable Radiant's runtime metrics or `OpenTelemetry.Instrumentation.Runtime` in your application.

## Attributes and values

| Attribute | Values |
|-----------|--------|
| `gen_ai.provider.name` | `openai`, `azure.ai.openai`, `gcp.gemini`, `gcp.vertex_ai`, `anthropic`, `aws.bedrock`, `cohere`, `ollama`, `tei`, `voyageai`, `typesafe`. A client you derive from a PolyPrompt client inherits its provider; a client you derive directly from a capability base is labeled by its lower-cased type name without the `...Client` suffix. |
| `polyprompt.capability` | `completion`, `embedding`, `sparse_embedding`, `rerank`, `classification`, `decision`, `model` |
| `polyprompt.operation` | `chat`, `chat_stream`, `tool_chat`, `tool_chat_stream`, `generate`, `generate_stream`, `embed`, `sparse_embed`, `rerank`, `classify`, `decide`, `decide_batch`, `model_info`, `model_exists`, `model_pull`, `model_delete`, `validate_connectivity` |
| `gen_ai.operation.name` | `chat`, `text_completion`, `embeddings`, `rerank`, `classify`, `decide` (and the PolyPrompt operation on spans for non-model operations) |
| `polyprompt.outcome` | `success`, `error`, `timeout`, `cancelled`, `abandoned` |
| `error.type` | An HTTP status code (`404`, `429`, `529`), `invalid_response` (a successful status with a body PolyPrompt could not use), `timeout` (exceeded `TimeoutMs`), `batch_item_failed`, `operation_failed` (a boolean operation returned false without a status), or the exception's full type name (`System.Net.Http.HttpRequestException`). Absent on success and on cancellation. |
| `gen_ai.request.model` | The model name your application configured or passed per call, truncated to 128 characters. |
| `gen_ai.response.finish_reason` | The provider's finish reason, lower-cased, truncated to 32 characters (for example `stop`, `length`, `tool_calls`, `end_turn`, `max_tokens`). |
| `polyprompt.credential.source` | `service_account`, `adc`, or the lower-cased type name of a custom `CachingCredentialProvider`. |

Every metric label is bounded: provider, capability, operation, outcome, and error type are fixed sets; model, endpoint host, and port are configured by your application; finish reasons are a provider vocabulary. Ids (response ids, URLs) appear only on spans.

## Prometheus names

A Prometheus exporter rewrites dotted names to snake case and appends unit and type suffixes. Bracketed units such as `{token}` are dropped:

| Instrument | Prometheus series |
|------------|-------------------|
| `polyprompt.client.operation.duration` | `polyprompt_client_operation_duration_seconds_bucket`, `_sum`, `_count` |
| `polyprompt.client.operations` | `polyprompt_client_operations_total` |
| `gen_ai.client.token.usage` | `gen_ai_client_token_usage_bucket` |
| `polyprompt.client.tokens` | `polyprompt_client_tokens_total` |
| `polyprompt.http.client.request.duration` | `polyprompt_http_client_request_duration_seconds_bucket` |
| `polyprompt.http.client.requests` | `polyprompt_http_client_requests_total` |
| `polyprompt.http.client.request.body.size` | `polyprompt_http_client_request_body_size_bytes_bucket` |
| `polyprompt.client.operation.active` | `polyprompt_client_operation_active` |
| `polyprompt.decision.batch.queue.duration` | `polyprompt_decision_batch_queue_duration_seconds_bucket` |
| `polyprompt.credential.refreshes` | `polyprompt_credential_refreshes_total` |
| `polyprompt.build.info` | `polyprompt_build_info` |

Labels become `gen_ai_provider_name`, `polyprompt_operation`, `polyprompt_outcome`, `error_type`, `http_response_status_code`, and so on.

## Recommended alerts

```yaml
groups:
  - name: polyprompt
    rules:
      - alert: PolyPromptProviderErrorRateHigh
        expr: |
          sum by (gen_ai_provider_name, polyprompt_operation) (rate(polyprompt_client_operations_total{polyprompt_outcome=~"error|timeout"}[5m]))
            / sum by (gen_ai_provider_name, polyprompt_operation) (rate(polyprompt_client_operations_total{polyprompt_outcome!="cancelled"}[5m])) > 0.05
        for: 10m
        annotations:
          summary: "{{ $labels.gen_ai_provider_name }} {{ $labels.polyprompt_operation }} is failing more than 5% of calls"

      - alert: PolyPromptProviderRateLimited
        expr: sum by (gen_ai_provider_name) (rate(polyprompt_http_client_requests_total{http_response_status_code=~"429|529"}[5m])) > 0.1
        for: 5m
        annotations:
          summary: "{{ $labels.gen_ai_provider_name }} is rate limiting or overloaded"

      - alert: PolyPromptTimeouts
        expr: sum by (gen_ai_provider_name) (rate(polyprompt_client_operations_total{polyprompt_outcome="timeout"}[5m])) > 0
        for: 10m
        annotations:
          summary: "Calls to {{ $labels.gen_ai_provider_name }} are exceeding TimeoutMs"

      - alert: PolyPromptLatencyHigh
        expr: |
          histogram_quantile(0.95, sum by (le, gen_ai_provider_name, polyprompt_operation)
            (rate(polyprompt_client_operation_duration_seconds_bucket{polyprompt_operation!~".*_stream|model_pull"}[5m]))) > 30
        for: 15m
        annotations:
          summary: "p95 of {{ $labels.gen_ai_provider_name }} {{ $labels.polyprompt_operation }} is above 30 s"

      - alert: PolyPromptCredentialRefreshFailing
        expr: sum by (polyprompt_credential_source) (rate(polyprompt_credential_refreshes_total{polyprompt_outcome!="success"}[15m])) > 0
        for: 5m
        annotations:
          summary: "OAuth token refresh ({{ $labels.polyprompt_credential_source }}) is failing; Vertex AI calls will fail"

      - alert: PolyPromptDecisionQueueSaturated
        expr: |
          histogram_quantile(0.95, sum by (le, gen_ai_provider_name) (rate(polyprompt_decision_batch_queue_duration_seconds_bucket[5m]))) > 5
        for: 10m
        annotations:
          summary: "Decision batch items wait more than 5 s for a slot; raise MaxConcurrency or split batches"

      - alert: PolyPromptTruncatedOutput
        expr: |
          sum by (gen_ai_provider_name) (rate(polyprompt_client_finish_reasons_total{gen_ai_response_finish_reason=~"length|max_tokens"}[15m]))
            / sum by (gen_ai_provider_name) (rate(polyprompt_client_finish_reasons_total[15m])) > 0.1
        for: 30m
        annotations:
          summary: "More than 10% of {{ $labels.gen_ai_provider_name }} completions hit the token limit"
```

## Dashboard map

PolyPrompt ships no dashboards or compose stack because it is not a service. A host service that uses it should add a **Model Providers** dashboard to its product folder in Grafana, built from these queries:

| Panel | Query |
|-------|-------|
| Calls per second by provider and outcome | `sum by (gen_ai_provider_name, polyprompt_outcome) (rate(polyprompt_client_operations_total[5m]))` |
| Error ratio by provider | `sum by (gen_ai_provider_name) (rate(polyprompt_client_operations_total{polyprompt_outcome=~"error\|timeout"}[5m])) / sum by (gen_ai_provider_name) (rate(polyprompt_client_operations_total[5m]))` |
| Errors by type | `sum by (gen_ai_provider_name, error_type) (rate(polyprompt_client_operations_total{error_type!=""}[5m]))` |
| p95 latency by operation | `histogram_quantile(0.95, sum by (le, gen_ai_provider_name, polyprompt_operation) (rate(polyprompt_client_operation_duration_seconds_bucket[5m])))` |
| p95 network time by provider | `histogram_quantile(0.95, sum by (le, gen_ai_provider_name) (rate(polyprompt_http_client_request_duration_seconds_bucket[5m])))` |
| HTTP status by provider | `sum by (gen_ai_provider_name, http_response_status_code) (rate(polyprompt_http_client_requests_total[5m]))` |
| Streaming time to first chunk (p95) | `histogram_quantile(0.95, sum by (le, gen_ai_provider_name) (rate(polyprompt_client_stream_time_to_first_chunk_seconds_bucket[5m])))` |
| Tokens per second by model and type | `sum by (gen_ai_request_model, polyprompt_token_type) (rate(polyprompt_client_tokens_total[5m]))` |
| Cache hit share of input tokens | `sum(rate(polyprompt_client_tokens_total{polyprompt_token_type="cached_input"}[1h])) / sum(rate(polyprompt_client_tokens_total{polyprompt_token_type="input"}[1h]))` |
| In flight | `sum by (gen_ai_provider_name) (polyprompt_client_operation_active)` and `polyprompt_http_client_active_requests` |
| Decision queue | `polyprompt_decision_batch_queued`, `polyprompt_decision_batch_in_flight`, p95 of `polyprompt_decision_batch_queue_duration_seconds_bucket` |
| Credential refresh | `sum by (polyprompt_credential_source, polyprompt_outcome) (rate(polyprompt_credential_refreshes_total[15m]))` |
| Live clients | `sum by (gen_ai_provider_name, polyprompt_capability) (polyprompt_clients_active)` |
| Library version | `polyprompt_build_info` |

Link the panels to Tempo with a TraceQL query such as `{ resource.service.name = "my-service" && span.gen_ai.provider.name = "openai" && status = error }` to jump from an error spike to example traces.

## Design notes and limits

- **Best effort.** Telemetry never changes a call's result. A failure while recording a metric or span, including a listener that throws, is swallowed, and the remaining signals are still recorded.
- **Near-zero cost when unobserved.** With no listener attached, no span, metric, tag list, or async state machine is created; the instrumented paths call the original code directly after a few boolean checks.
- **No secrets or payloads.** Prompts, messages, embeddings, documents, request and response bodies, API keys, and tokens are never recorded. `url.full` has its query string removed, and span status descriptions carry only the HTTP status, `timeout`, or an exception type. Exception events carry the exception message, as HttpClient reports it.
- **Logs.** PolyPrompt logs through the `LoggingModule` you pass to each client and does no background work of its own, so it ships no Loki pipeline. If your host sends its logs to Loki, records written during a PolyPrompt call share `Activity.Current` with the PolyPrompt spans and correlate with the trace.
- **`ListModelsAsync`** is a public abstract async enumerable on each model client, so it has no operation span of its own. Each page request still emits an HTTP span and metrics, and `ModelExistsAsync` (which enumerates it) has an operation span.
- **A stream that is never enumerated** never ends its span. Always enumerate or dispose `Chunks` (this also releases the HTTP response).
- **Overridden `DecideBatchCoreAsync`.** A custom decision client that overrides the batch core keeps the batch span but not the per-item queued and decide spans.
- **HttpClient's own instrumentation.** If your host also enables `System.Net.Http` tracing, its spans nest under PolyPrompt's HTTP span. PolyPrompt's HTTP span already carries the method, status, host, and error type, so you can leave `System.Net.Http` tracing off for provider calls.
