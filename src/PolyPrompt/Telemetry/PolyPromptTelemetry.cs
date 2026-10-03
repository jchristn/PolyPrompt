namespace PolyPrompt.Telemetry
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Reflection;
    using PolyPrompt.Auth;
    using PolyPrompt.Clients;

    /// <summary>
    /// The process-wide <see cref="Meter"/>, <see cref="ActivitySource"/>, and instruments behind PolyPrompt's telemetry.
    /// Emission goes through the BCL only: PolyPrompt takes no exporter or SDK dependency, and every recording path is
    /// skipped when no listener is attached. Thread safety: all members are safe for concurrent use.
    /// </summary>
    internal static class PolyPromptTelemetry
    {
        #region Private-Members

        private static readonly ConcurrentDictionary<Type, string> _Providers = new ConcurrentDictionary<Type, string>();
        private static readonly ConcurrentDictionary<Type, string> _CredentialSources = new ConcurrentDictionary<Type, string>();

        private static readonly string[] _CapabilitySuffixes = new[]
        {
            "SparseEmbeddingClient", "ClassificationClient", "CompletionClient", "EmbeddingClient", "DecisionClient", "RerankClient", "ModelClient", "Client"
        };

        private static readonly Dictionary<string, string> _ProviderNames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "OpenAi", "openai" },
            { "AzureOpenAi", "azure.ai.openai" },
            { "Gemini", "gcp.gemini" },
            { "VertexAi", "gcp.vertex_ai" },
            { "Anthropic", "anthropic" },
            { "Bedrock", "aws.bedrock" },
            { "Cohere", "cohere" },
            { "Ollama", "ollama" },
            { "Tei", "tei" },
            { "VoyageAi", "voyageai" },
            { "TypeSafe", "typesafe" },
        };

        #endregion

        #region Public-Members

        /// <summary>
        /// Library version reported on spans and the build-info gauge.
        /// </summary>
        public static readonly string Version = ResolveVersion();

        /// <summary>
        /// Source for every PolyPrompt span.
        /// </summary>
        public static readonly ActivitySource Source = new ActivitySource(PolyPromptTelemetryNames.ActivitySourceName, Version);

        /// <summary>
        /// Meter for every PolyPrompt metric.
        /// </summary>
        public static readonly Meter Meter = new Meter(PolyPromptTelemetryNames.MeterName, Version);

        /// <summary>
        /// In-flight operations by provider and operation.
        /// </summary>
        public static readonly InFlightRegistry ActiveOperations = new InFlightRegistry();

        /// <summary>
        /// In-flight HTTP requests by provider and method.
        /// </summary>
        public static readonly InFlightRegistry ActiveRequests = new InFlightRegistry();

        /// <summary>
        /// Decision batch items holding a concurrency slot, by provider.
        /// </summary>
        public static readonly InFlightRegistry DecisionInFlight = new InFlightRegistry();

        /// <summary>
        /// Decision batch items waiting for a concurrency slot, by provider.
        /// </summary>
        public static readonly InFlightRegistry DecisionQueued = new InFlightRegistry();

        /// <summary>
        /// Live clients by provider and capability.
        /// </summary>
        public static readonly InFlightRegistry ActiveClients = new InFlightRegistry();

        /// <summary>
        /// Operation duration histogram.
        /// </summary>
        public static readonly Histogram<double> OperationDuration = Meter.CreateHistogram<double>(
            PolyPromptTelemetryNames.OperationDuration, "s", "Duration of PolyPrompt client operations.");

        /// <summary>
        /// Operation counter.
        /// </summary>
        public static readonly Counter<long> Operations = Meter.CreateCounter<long>(
            PolyPromptTelemetryNames.Operations, "{operation}", "Completed PolyPrompt client operations.");

        /// <summary>
        /// GenAI semantic-convention operation duration histogram.
        /// </summary>
        public static readonly Histogram<double> GenAiOperationDuration = Meter.CreateHistogram<double>(
            PolyPromptTelemetryNames.GenAiOperationDuration, "s", "GenAI operation duration.");

        /// <summary>
        /// GenAI semantic-convention token usage histogram.
        /// </summary>
        public static readonly Histogram<long> GenAiTokenUsage = Meter.CreateHistogram<long>(
            PolyPromptTelemetryNames.GenAiTokenUsage, "{token}", "Number of input and output tokens used.");

        /// <summary>
        /// Token counter.
        /// </summary>
        public static readonly Counter<long> Tokens = Meter.CreateCounter<long>(
            PolyPromptTelemetryNames.Tokens, "{token}", "Tokens consumed by PolyPrompt client operations.");

        /// <summary>
        /// Streaming time-to-first-chunk histogram.
        /// </summary>
        public static readonly Histogram<double> StreamTimeToFirstChunk = Meter.CreateHistogram<double>(
            PolyPromptTelemetryNames.StreamTimeToFirstChunk, "s", "Time from the start of a streaming operation to its first content chunk.");

        /// <summary>
        /// Streaming chunk counter.
        /// </summary>
        public static readonly Counter<long> StreamChunks = Meter.CreateCounter<long>(
            PolyPromptTelemetryNames.StreamChunks, "{chunk}", "Content chunks delivered by streaming operations.");

        /// <summary>
        /// Batch size histogram.
        /// </summary>
        public static readonly Histogram<long> BatchSize = Meter.CreateHistogram<long>(
            PolyPromptTelemetryNames.BatchSize, "{item}", "Inputs per PolyPrompt request.");

        /// <summary>
        /// Tool call counter.
        /// </summary>
        public static readonly Counter<long> ToolCalls = Meter.CreateCounter<long>(
            PolyPromptTelemetryNames.ToolCalls, "{call}", "Tool calls requested by the model.");

        /// <summary>
        /// Finish reason counter.
        /// </summary>
        public static readonly Counter<long> FinishReasons = Meter.CreateCounter<long>(
            PolyPromptTelemetryNames.FinishReasons, "{response}", "Completion responses by finish reason.");

        /// <summary>
        /// HTTP request duration histogram.
        /// </summary>
        public static readonly Histogram<double> HttpRequestDuration = Meter.CreateHistogram<double>(
            PolyPromptTelemetryNames.HttpRequestDuration, "s", "Duration of outbound HTTP requests to providers.");

        /// <summary>
        /// HTTP request counter.
        /// </summary>
        public static readonly Counter<long> HttpRequests = Meter.CreateCounter<long>(
            PolyPromptTelemetryNames.HttpRequests, "{request}", "Outbound HTTP requests to providers.");

        /// <summary>
        /// HTTP request body size histogram.
        /// </summary>
        public static readonly Histogram<long> HttpRequestBodySize = Meter.CreateHistogram<long>(
            PolyPromptTelemetryNames.HttpRequestBodySize, "By", "Outbound HTTP request body size.");

        /// <summary>
        /// HTTP response body size histogram.
        /// </summary>
        public static readonly Histogram<long> HttpResponseBodySize = Meter.CreateHistogram<long>(
            PolyPromptTelemetryNames.HttpResponseBodySize, "By", "HTTP response body size for non-streaming requests.");

        /// <summary>
        /// Decision batch queue wait histogram.
        /// </summary>
        public static readonly Histogram<double> DecisionQueueDuration = Meter.CreateHistogram<double>(
            PolyPromptTelemetryNames.DecisionQueueDuration, "s", "Time a decision batch item waited for a concurrency slot.");

        /// <summary>
        /// Decision question counter.
        /// </summary>
        public static readonly Counter<long> DecisionQuestions = Meter.CreateCounter<long>(
            PolyPromptTelemetryNames.DecisionQuestions, "{question}", "Decision questions sent, by type.");

        /// <summary>
        /// Credential refresh duration histogram.
        /// </summary>
        public static readonly Histogram<double> CredentialRefreshDuration = Meter.CreateHistogram<double>(
            PolyPromptTelemetryNames.CredentialRefreshDuration, "s", "Duration of credential token fetches.");

        /// <summary>
        /// Credential refresh counter.
        /// </summary>
        public static readonly Counter<long> CredentialRefreshes = Meter.CreateCounter<long>(
            PolyPromptTelemetryNames.CredentialRefreshes, "{refresh}", "Credential token fetches.");

        /// <summary>
        /// Credential cache lookup counter.
        /// </summary>
        public static readonly Counter<long> CredentialCacheLookups = Meter.CreateCounter<long>(
            PolyPromptTelemetryNames.CredentialCacheLookups, "{lookup}", "Credential cache lookups.");

        /// <summary>
        /// True when a listener is attached to the activity source or to any synchronous PolyPrompt instrument. When
        /// false, instrumentation is skipped entirely.
        /// </summary>
        public static bool IsEnabled
        {
            get
            {
                return Source.HasListeners()
                    || OperationDuration.Enabled || Operations.Enabled || GenAiOperationDuration.Enabled || GenAiTokenUsage.Enabled
                    || Tokens.Enabled || StreamTimeToFirstChunk.Enabled || StreamChunks.Enabled || BatchSize.Enabled
                    || ToolCalls.Enabled || FinishReasons.Enabled || HttpRequestDuration.Enabled || HttpRequests.Enabled
                    || HttpRequestBodySize.Enabled || HttpResponseBodySize.Enabled || DecisionQueueDuration.Enabled
                    || DecisionQuestions.Enabled || CredentialRefreshDuration.Enabled || CredentialRefreshes.Enabled
                    || CredentialCacheLookups.Enabled;
            }
        }

        #endregion

        #region Constructors-and-Factories

        static PolyPromptTelemetry()
        {
            Meter.CreateObservableUpDownCounter(PolyPromptTelemetryNames.ActiveOperations, () => ActiveOperations.Observe(),
                "{operation}", "PolyPrompt client operations in progress.");
            Meter.CreateObservableUpDownCounter(PolyPromptTelemetryNames.HttpActiveRequests, () => ActiveRequests.Observe(),
                "{request}", "Outbound HTTP requests to providers in flight.");
            Meter.CreateObservableUpDownCounter(PolyPromptTelemetryNames.DecisionInFlight, () => DecisionInFlight.Observe(),
                "{request}", "Decision batch items holding a concurrency slot.");
            Meter.CreateObservableUpDownCounter(PolyPromptTelemetryNames.DecisionQueued, () => DecisionQueued.Observe(),
                "{request}", "Decision batch items waiting for a concurrency slot.");
            Meter.CreateObservableUpDownCounter(PolyPromptTelemetryNames.ActiveClients, () => ActiveClients.Observe(),
                "{client}", "Constructed and not yet disposed PolyPrompt clients.");
            Meter.CreateObservableGauge(PolyPromptTelemetryNames.BuildInfo,
                () => new Measurement<long>(1, new KeyValuePair<string, object?>(PolyPromptTelemetryNames.Version, Version)),
                "{build}", "PolyPrompt build information.");
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Resolve the bounded provider label for a client type, for example <c>openai</c> for
        /// <see cref="OpenAiCompletionClient"/> or any type derived from it.
        /// </summary>
        /// <param name="type">Client type.</param>
        /// <returns>The provider label.</returns>
        public static string ResolveProvider(Type type)
        {
            return _Providers.GetOrAdd(type, t =>
            {
                Assembly library = typeof(ClientBase).Assembly;
                Type? current = t;
                while (current != null && current.Assembly != library) current = current.BaseType;

                // A custom client that derives straight from a capability base is labeled by its own type name.
                if (current == null || current.IsAbstract) current = t;

                string prefix = StripCapabilitySuffix(current.Name);
                if (_ProviderNames.TryGetValue(prefix, out string? name)) return name;
                return string.IsNullOrEmpty(prefix) ? "custom" : prefix.ToLowerInvariant();
            });
        }

        /// <summary>
        /// Resolve the bounded capability label for a client.
        /// </summary>
        /// <param name="client">Client.</param>
        /// <returns>The capability label.</returns>
        public static string ResolveCapability(ClientBase client)
        {
            if (client is CompletionClientBase) return "completion";
            if (client is EmbeddingClientBase) return "embedding";
            if (client is SparseEmbeddingClientBase) return "sparse_embedding";
            if (client is RerankClientBase) return "rerank";
            if (client is ClassificationClientBase) return "classification";
            if (client is DecisionClientBase) return "decision";
            if (client is ModelClientBase) return "model";
            return "other";
        }

        /// <summary>
        /// Resolve the bounded credential source label for a credential provider type.
        /// </summary>
        /// <param name="type">Credential provider type.</param>
        /// <returns>The credential source label.</returns>
        public static string ResolveCredentialSource(Type type)
        {
            return _CredentialSources.GetOrAdd(type, t =>
            {
                if (t == typeof(ServiceAccountCredential)) return "service_account";
                if (t == typeof(AdcCredential)) return "adc";
                return t.Name.ToLowerInvariant();
            });
        }

        /// <summary>
        /// Map a PolyPrompt operation to its OpenTelemetry GenAI operation name, or null when the operation is not a
        /// model call (model management, connectivity checks, batch wrappers).
        /// </summary>
        /// <param name="operation">PolyPrompt operation.</param>
        /// <returns>The GenAI operation name, or null.</returns>
        public static string? ToGenAiOperation(string operation)
        {
            switch (operation)
            {
                case PolyPromptTelemetryNames.OperationChat:
                case PolyPromptTelemetryNames.OperationChatStream:
                case PolyPromptTelemetryNames.OperationToolChat:
                case PolyPromptTelemetryNames.OperationToolChatStream:
                    return "chat";
                case PolyPromptTelemetryNames.OperationGenerate:
                case PolyPromptTelemetryNames.OperationGenerateStream:
                    return "text_completion";
                case PolyPromptTelemetryNames.OperationEmbed:
                case PolyPromptTelemetryNames.OperationSparseEmbed:
                    return "embeddings";
                case PolyPromptTelemetryNames.OperationRerank:
                    return "rerank";
                case PolyPromptTelemetryNames.OperationClassify:
                    return "classify";
                case PolyPromptTelemetryNames.OperationDecide:
                    return "decide";
                default:
                    return null;
            }
        }

        /// <summary>
        /// Bound a free-form label value (a model name or finish reason) so a misbehaving caller or provider cannot emit
        /// arbitrarily long label values.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <param name="maxLength">Maximum length kept.</param>
        /// <returns>The bounded value, or <c>unknown</c> when empty.</returns>
        public static string Bound(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return "unknown";
            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }

        /// <summary>
        /// Record an exception on a span as an OpenTelemetry <c>exception</c> event and mark the span as failed.
        /// </summary>
        /// <param name="activity">Span, or null.</param>
        /// <param name="ex">Exception.</param>
        public static void RecordException(Activity? activity, Exception ex)
        {
            if (activity == null) return;
            ActivityTagsCollection tags = new ActivityTagsCollection
            {
                { "exception.type", ex.GetType().FullName },
                { "exception.message", ex.Message },
                { "exception.stacktrace", ex.ToString() },
            };
            activity.AddEvent(new ActivityEvent("exception", DateTimeOffset.UtcNow, tags));
        }

        /// <summary>
        /// Record a histogram value, swallowing any exception a listener throws so one faulty listener cannot suppress
        /// the remaining signals.
        /// </summary>
        /// <param name="histogram">Histogram.</param>
        /// <param name="value">Value.</param>
        /// <param name="tags">Labels.</param>
        public static void SafeRecord(Histogram<double> histogram, double value, in TagList tags)
        {
            try
            {
                histogram.Record(value, tags);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Record a histogram value, swallowing any exception a listener throws.
        /// </summary>
        /// <param name="histogram">Histogram.</param>
        /// <param name="value">Value.</param>
        /// <param name="tags">Labels.</param>
        public static void SafeRecord(Histogram<long> histogram, long value, in TagList tags)
        {
            try
            {
                histogram.Record(value, tags);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Add to a counter, swallowing any exception a listener throws.
        /// </summary>
        /// <param name="counter">Counter.</param>
        /// <param name="value">Value.</param>
        /// <param name="tags">Labels.</param>
        public static void SafeAdd(Counter<long> counter, long value, in TagList tags)
        {
            try
            {
                counter.Add(value, tags);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Stop a span without disturbing the caller's current activity. A span ended from a different async flow (for
        /// example a stream finished by the consumer) must not reset <see cref="Activity.Current"/> for that flow.
        /// </summary>
        /// <param name="activity">Span, or null.</param>
        public static void StopDetached(Activity? activity)
        {
            if (activity == null) return;
            Activity? current = Activity.Current;
            try
            {
                activity.Stop();
            }
            catch
            {
            }
            if (current != activity && Activity.Current != current) Activity.Current = current;
        }

        #endregion

        #region Private-Methods

        private static string StripCapabilitySuffix(string name)
        {
            foreach (string suffix in _CapabilitySuffixes)
            {
                if (name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > suffix.Length)
                    return name.Substring(0, name.Length - suffix.Length);
            }
            return name;
        }

        private static string ResolveVersion()
        {
            Assembly assembly = typeof(PolyPromptTelemetry).Assembly;
            string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrEmpty(informational))
            {
                int plus = informational.IndexOf('+');
                return plus >= 0 ? informational.Substring(0, plus) : informational;
            }
            return assembly.GetName().Version?.ToString() ?? "unknown";
        }

        #endregion
    }
}
