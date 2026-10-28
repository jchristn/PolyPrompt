namespace PolyPrompt.Clients
{
    using System.Diagnostics;
    using System.Globalization;
    using System.Text;
    using System.Text.Json;
    using PolyPrompt.Helpers;
    using PolyPrompt.Models;
    using PolyPrompt.Telemetry;
    using SyslogLogging;

    /// <summary>
    /// Base class for every PolyPrompt client, independent of transport. It owns logging, the call timeout
    /// (<see cref="TimeoutMs"/>), the record of upstream calls (<see cref="CallDetails"/>), telemetry scopes, JSON
    /// parsing helpers, and disposal. It declares no model operations and no transport: HTTP API clients derive from
    /// <see cref="HttpClientBase"/> (through one capability base per capability), and agent harness clients that drive a
    /// local CLI process derive from <see cref="CliAgentClientBase"/>.
    /// </summary>
    public abstract class ClientBase : IDisposable
    {
        #region Private-Members

        private int _TimeoutMs = 120000;
        private readonly object _CallDetailsLock = new object();
        private readonly List<CallDetail> _CallDetails = new List<CallDetail>();
        private int _MaxCallDetails = 1000;

        private bool _Disposed = false;

        private readonly string _TelemetryProvider;
        private readonly InFlightEntry? _ClientEntry;

        #endregion

        #region Protected-Members

        /// <summary>
        /// Logging module.
        /// </summary>
        protected readonly LoggingModule _Logging;

        /// <summary>
        /// Serializer instance.
        /// </summary>
        protected readonly Serializer _Serializer = new Serializer();

        /// <summary>
        /// Header prefix for log messages, for example <c>[OpenAI] </c>.
        /// </summary>
        protected string _Header = "[PolyPrompt] ";

        #endregion

        #region Public-Members

        /// <summary>
        /// Call timeout in milliseconds. Must be greater than zero. Default: 120000.
        /// For HTTP streaming operations the timeout covers the whole response, including the body; for agent harness
        /// clients it covers the whole process run unless the request sets its own timeout.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when set to zero or less.</exception>
        public int TimeoutMs
        {
            get { return _TimeoutMs; }
            set
            {
                if (value <= 0) throw new ArgumentOutOfRangeException(nameof(TimeoutMs), "TimeoutMs must be greater than zero.");
                _TimeoutMs = value;
            }
        }

        /// <summary>
        /// Detached snapshot of the recorded details of upstream calls (HTTP requests or process executions) made by this
        /// client, oldest first. Process executions are recorded as <see cref="ProcessCallDetail"/>.
        /// </summary>
        public List<CallDetail> CallDetails
        {
            get
            {
                lock (_CallDetailsLock)
                {
                    List<CallDetail> snapshot = new List<CallDetail>(_CallDetails.Count);
                    foreach (CallDetail detail in _CallDetails)
                    {
                        snapshot.Add(CloneCallDetail(detail));
                    }
                    return snapshot;
                }
            }
        }

        /// <summary>
        /// Maximum number of call details retained by this client. Set to zero to disable recording. Default: 1000.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when set to a negative value.</exception>
        public int MaxCallDetails
        {
            get
            {
                lock (_CallDetailsLock)
                {
                    return _MaxCallDetails;
                }
            }
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(MaxCallDetails), "MaxCallDetails cannot be negative.");
                lock (_CallDetailsLock)
                {
                    _MaxCallDetails = value;
                    TrimCallDetailsIfNeeded();
                }
            }
        }

        /// <summary>
        /// Bounded provider label used on telemetry, for example <c>openai</c> or <c>aws.bedrock</c>.
        /// </summary>
        internal string TelemetryProvider
        {
            get { return _TelemetryProvider; }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new client.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when logging is null.</exception>
        protected ClientBase(LoggingModule logging)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));

            _TelemetryProvider = PolyPromptTelemetry.ResolveProvider(GetType());
            try
            {
                _ClientEntry = PolyPromptTelemetry.ActiveClients.Get(
                    PolyPromptTelemetryNames.Provider, _TelemetryProvider,
                    PolyPromptTelemetryNames.Capability, PolyPromptTelemetry.ResolveCapability(this));
                _ClientEntry.Increment();
            }
            catch
            {
                _ClientEntry = null;
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Clear all retained call details.
        /// </summary>
        public void ClearCallDetails()
        {
            lock (_CallDetailsLock)
            {
                _CallDetails.Clear();
            }
        }

        /// <summary>
        /// Dispose of client resources. An HTTP client supplied by the caller is not disposed.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;

            _ClientEntry?.Decrement();

            DisposeResources();

            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Release transport resources owned by the derived client. Called once from <see cref="Dispose"/>.
        /// </summary>
        protected virtual void DisposeResources()
        {
        }

        /// <summary>
        /// Record a call detail while enforcing the configured retention limit.
        /// </summary>
        /// <param name="detail">Call detail to record.</param>
        protected void RecordCallDetail(CallDetail detail)
        {
            lock (_CallDetailsLock)
            {
                if (_MaxCallDetails == 0) return;

                _CallDetails.Add(CloneCallDetail(detail));
                TrimCallDetailsIfNeeded();
            }
        }

        /// <summary>
        /// Clone a call detail so callers cannot mutate retained state.
        /// </summary>
        /// <param name="detail">Call detail.</param>
        /// <returns>A detached copy.</returns>
        protected static CallDetail CloneCallDetail(CallDetail detail)
        {
            CallDetail clone;
            if (detail is ProcessCallDetail process)
            {
                clone = new ProcessCallDetail
                {
                    Executable = process.Executable,
                    Arguments = process.Arguments != null ? new List<string>(process.Arguments) : new List<string>(),
                    WorkingDirectory = process.WorkingDirectory,
                    StandardInput = process.StandardInput,
                    ExitCode = process.ExitCode,
                    StandardError = process.StandardError,
                    ProcessId = process.ProcessId,
                    TimedOut = process.TimedOut,
                    Cancelled = process.Cancelled,
                    OutputTruncated = process.OutputTruncated,
                    FailureKind = process.FailureKind,
                };
            }
            else
            {
                clone = new CallDetail();
            }

            clone.Url = detail.Url;
            clone.Method = detail.Method;
            clone.RequestHeaders = detail.RequestHeaders != null ? new Dictionary<string, string>(detail.RequestHeaders) : new Dictionary<string, string>();
            clone.RequestBody = detail.RequestBody;
            clone.StatusCode = detail.StatusCode;
            clone.ResponseHeaders = detail.ResponseHeaders != null ? new Dictionary<string, string>(detail.ResponseHeaders) : new Dictionary<string, string>();
            clone.ResponseBody = detail.ResponseBody;
            clone.ResponseTimeMs = detail.ResponseTimeMs;
            clone.Success = detail.Success;
            clone.Error = detail.Error;
            clone.TimestampUtc = detail.TimestampUtc;
            return clone;
        }

        /// <summary>
        /// Return the per-call value when set, otherwise the client default. Used to merge per-call options over
        /// a client's <c>Defaults</c>, including provider-specific options that only exist on a derived type.
        /// </summary>
        /// <typeparam name="TOptions">Options type that declares the value.</typeparam>
        /// <typeparam name="TValue">Value type.</typeparam>
        /// <param name="call">Per-call options (any type, may be null).</param>
        /// <param name="defaults">Client default options (any type).</param>
        /// <param name="select">Selects the value from options of type <typeparamref name="TOptions"/>.</param>
        /// <returns>The per-call value, else the default value, else null.</returns>
        protected static TValue? Pick<TOptions, TValue>(object? call, object? defaults, Func<TOptions, TValue?> select)
            where TOptions : class
            where TValue : struct
        {
            TValue? value = call is TOptions callOptions ? select(callOptions) : null;
            if (value.HasValue) return value;
            return defaults is TOptions defaultOptions ? select(defaultOptions) : null;
        }

        /// <summary>
        /// Return the per-call reference value when set, otherwise the client default. See
        /// <see cref="Pick{TOptions, TValue}(object?, object?, Func{TOptions, TValue?})"/>.
        /// </summary>
        /// <typeparam name="TOptions">Options type that declares the value.</typeparam>
        /// <typeparam name="TValue">Value type.</typeparam>
        /// <param name="call">Per-call options (any type, may be null).</param>
        /// <param name="defaults">Client default options (any type).</param>
        /// <param name="select">Selects the value from options of type <typeparamref name="TOptions"/>.</param>
        /// <returns>The per-call value, else the default value, else null.</returns>
        protected static TValue? PickRef<TOptions, TValue>(object? call, object? defaults, Func<TOptions, TValue?> select)
            where TOptions : class
            where TValue : class
        {
            TValue? value = call is TOptions callOptions ? select(callOptions) : null;
            if (value != null) return value;
            return defaults is TOptions defaultOptions ? select(defaultOptions) : null;
        }

        /// <summary>
        /// Validate a list of inputs for a batch request.
        /// </summary>
        /// <param name="inputs">The input list.</param>
        /// <param name="parameterName">Parameter name reported in exceptions.</param>
        /// <param name="emptyMessage">Message used when the list is empty.</param>
        /// <exception cref="ArgumentNullException">Thrown when inputs is null.</exception>
        /// <exception cref="ArgumentException">Thrown when inputs is empty or contains a null element.</exception>
        protected static void ValidateInputList<T>(List<T> inputs, string parameterName, string emptyMessage)
        {
            if (inputs == null) throw new ArgumentNullException(parameterName);
            if (inputs.Count == 0) throw new ArgumentException(emptyMessage, parameterName);

            for (int i = 0; i < inputs.Count; i++)
            {
                if (inputs[i] == null)
                    throw new ArgumentException(parameterName + "[" + i + "] cannot be null.", parameterName);
            }
        }

        /// <summary>
        /// Parse a float array from a JSON array element.
        /// </summary>
        /// <param name="arrayJson">The JSON string representing the array.</param>
        /// <returns>An array of floats.</returns>
        protected float[] ParseFloatArray(string arrayJson)
        {
            List<object>? values = _Serializer.DeserializeJson<List<object>>(arrayJson);
            if (values == null) return Array.Empty<float>();

            float[] result = new float[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                string? val = values[i]?.ToString();
                if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                {
                    result[i] = f;
                }
            }
            return result;
        }

        /// <summary>
        /// Try to parse an integer from a dictionary value.
        /// </summary>
        /// <param name="dict">The dictionary.</param>
        /// <param name="key">The key to look up.</param>
        /// <returns>The integer value or null.</returns>
        protected static int? TryGetInt(Dictionary<string, object> dict, string key)
        {
            if (!dict.ContainsKey(key)) return null;
            string? val = dict[key]?.ToString();
            if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)) return result;
            return null;
        }

        /// <summary>
        /// Try to parse a long from a dictionary value.
        /// </summary>
        /// <param name="dict">The dictionary.</param>
        /// <param name="key">The key to look up.</param>
        /// <returns>The long value or null.</returns>
        protected static long? TryGetLong(Dictionary<string, object> dict, string key)
        {
            if (!dict.ContainsKey(key)) return null;
            string? val = dict[key]?.ToString();
            if (long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out long result)) return result;
            return null;
        }

        /// <summary>
        /// Try to parse a double from a dictionary value using the invariant culture.
        /// </summary>
        /// <param name="dict">The dictionary.</param>
        /// <param name="key">The key to look up.</param>
        /// <returns>The double value or null.</returns>
        protected static double? TryGetDouble(Dictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.ContainsKey(key)) return null;
            string? val = dict[key]?.ToString();
            if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
                return result;
            return null;
        }

        /// <summary>
        /// Try to parse an integer from a dictionary value that may be serialized as a floating point number
        /// (for example <c>71.0</c>). The value is rounded to the nearest integer.
        /// </summary>
        /// <param name="dict">The dictionary.</param>
        /// <param name="key">The key to look up.</param>
        /// <returns>The rounded integer value or null.</returns>
        protected static int? TryGetRoundedInt(Dictionary<string, object> dict, string key)
        {
            double? value = TryGetDouble(dict, key);
            if (!value.HasValue) return null;
            if (value.Value > int.MaxValue || value.Value < int.MinValue) return null;
            return (int)Math.Round(value.Value);
        }

        /// <summary>
        /// Check if a dictionary value represents a truthy boolean.
        /// Handles bool objects, strings ("true"/"True"), and numeric values (non-zero).
        /// </summary>
        /// <param name="dict">The dictionary.</param>
        /// <param name="key">The key to look up.</param>
        /// <returns>True if the value is truthy, false otherwise.</returns>
        protected static bool IsTruthy(Dictionary<string, object> dict, string key)
        {
            if (!dict.ContainsKey(key)) return false;
            object? value = dict[key];
            if (value == null) return false;
            if (value is bool b) return b;
            string? str = value.ToString();
            if (string.IsNullOrEmpty(str)) return false;
            if (string.Equals(str, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (long.TryParse(str, out long num)) return num != 0;
            return false;
        }

        /// <summary>
        /// Deserialize a nested JSON object stored under <paramref name="key"/> into a dictionary. Returns
        /// null when the key is absent, its value is null, or its value is not a JSON object.
        /// </summary>
        /// <param name="obj">The parent dictionary.</param>
        /// <param name="key">The key whose value is a nested object.</param>
        /// <returns>The nested object as a dictionary, or null.</returns>
        protected Dictionary<string, object>? ParseNestedObject(Dictionary<string, object> obj, string key)
        {
            if (!obj.ContainsKey(key) || obj[key] == null) return null;
            if (obj[key] is JsonElement element && element.ValueKind != JsonValueKind.Object) return null;

            string nestedJson = _Serializer.SerializeJson(obj[key], false);
            return _Serializer.DeserializeJson<Dictionary<string, object>>(nestedJson);
        }

        /// <summary>
        /// Deserialize a nested JSON array of objects stored under <paramref name="key"/>. Returns null when the key
        /// is absent, its value is null, or its value is not a JSON array.
        /// </summary>
        /// <param name="obj">The parent dictionary.</param>
        /// <param name="key">The key whose value is an array of objects.</param>
        /// <returns>The array elements as dictionaries, or null.</returns>
        protected List<Dictionary<string, object>>? ParseNestedList(Dictionary<string, object> obj, string key)
        {
            if (!obj.ContainsKey(key) || obj[key] == null) return null;
            if (obj[key] is JsonElement element && element.ValueKind != JsonValueKind.Array) return null;

            string nestedJson = _Serializer.SerializeJson(obj[key], false);
            return _Serializer.DeserializeJson<List<Dictionary<string, object>>>(nestedJson);
        }

        /// <summary>
        /// Parse text as JSON without throwing.
        /// </summary>
        /// <param name="text">Text that may contain JSON.</param>
        /// <param name="element">The parsed root element (detached from its document) when parsing succeeds.</param>
        /// <returns>True when the text is a single valid JSON value; false when it is null, empty, or not JSON.</returns>
        protected static bool TryParseJson(string? text, out JsonElement element)
        {
            element = default;
            if (string.IsNullOrWhiteSpace(text)) return false;

            try
            {
                using JsonDocument document = JsonDocument.Parse(text, new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });
                element = document.RootElement.Clone();
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        /// <summary>
        /// Deserialize a JSON object into a dictionary without throwing. Text that is empty, not valid JSON, or
        /// valid JSON that is not an object (an array, a scalar, or null) yields null.
        /// </summary>
        /// <param name="json">JSON text.</param>
        /// <returns>The object as a dictionary, or null.</returns>
        protected Dictionary<string, object>? TryDeserializeObject(string? json)
        {
            if (!TryParseJson(json, out JsonElement element) || element.ValueKind != JsonValueKind.Object) return null;
            return _Serializer.DeserializeJson<Dictionary<string, object>>(element.GetRawText());
        }

        /// <summary>
        /// Deserialize tool-call arguments into a JSON object dictionary without throwing. Arguments that are empty,
        /// not valid JSON, or valid JSON that is not an object (an array, a scalar, or null) yield an empty dictionary.
        /// </summary>
        /// <param name="json">Tool-call arguments JSON.</param>
        /// <returns>The arguments object, or an empty dictionary.</returns>
        protected Dictionary<string, object> DeserializeDictionaryOrEmpty(string? json)
        {
            return TryDeserializeObject(json) ?? new Dictionary<string, object>();
        }

        /// <summary>
        /// Run an operation that returns a response inside a telemetry scope (span plus metrics). When nothing is
        /// listening the operation runs directly with no overhead.
        /// </summary>
        /// <typeparam name="TResponse">Response type.</typeparam>
        /// <param name="operation">Operation label from <see cref="PolyPromptTelemetryNames"/>.</param>
        /// <param name="model">Requested model, or null.</param>
        /// <param name="enrich">Adds request attributes to the scope, or null.</param>
        /// <param name="core">The operation.</param>
        /// <param name="token">Caller cancellation token.</param>
        /// <returns>The operation's response.</returns>
        private protected Task<TResponse> InstrumentAsync<TResponse>(
            string operation,
            string? model,
            Action<OperationScope>? enrich,
            Func<Task<TResponse>> core,
            CancellationToken token)
            where TResponse : ResponseBase
        {
            if (!PolyPromptTelemetry.IsEnabled) return core();
            return InstrumentResponseCoreAsync(operation, model, enrich, core, null, token);
        }

        /// <summary>
        /// Run a streaming operation inside a telemetry scope that stays open until the stream ends. On a successful
        /// start, <paramref name="attach"/> wraps the response's chunk stream with the scope.
        /// </summary>
        /// <typeparam name="TResponse">Streaming response type.</typeparam>
        /// <param name="operation">Operation label from <see cref="PolyPromptTelemetryNames"/>.</param>
        /// <param name="model">Requested model, or null.</param>
        /// <param name="enrich">Adds request attributes to the scope, or null.</param>
        /// <param name="core">The operation.</param>
        /// <param name="attach">Wraps the response's chunk stream with the scope.</param>
        /// <param name="token">Caller cancellation token.</param>
        /// <returns>The operation's response.</returns>
        private protected async Task<TResponse> InstrumentStreamingAsync<TResponse>(
            string operation,
            string? model,
            Action<OperationScope>? enrich,
            Func<Task<TResponse>> core,
            Action<TResponse, OperationScope> attach,
            CancellationToken token)
            where TResponse : ResponseBase
        {
            async Task<TResponse> OpenAsync()
            {
                TResponse response = await core().ConfigureAwait(false);
                if (response is StreamingResponseBase streaming)
                {
                    streaming.State = response.Success ? StreamingState.Open : StreamingState.Failed;
                    streaming.StreamError = response.Error;
                    switch (response)
                    {
                        case ChatStreamingResponse chat:
                            chat.Chunks = StreamingLifecycle.Wrap(chat.Chunks, chat, c => c.Done, token);
                            break;
                        case ToolChatStreamingResponse tools:
                            tools.Chunks = StreamingLifecycle.Wrap(tools.Chunks, tools, c => c.Done, token);
                            break;
                        case GenerationStreamingResponse generation:
                            generation.Chunks = StreamingLifecycle.Wrap(generation.Chunks, generation, c => c.Done, token);
                            break;
                        case XAiStreamingResponse native:
                            native.Events = StreamingLifecycle.Wrap(native.Events, native, e => e.Type == "response.completed" || e.Type == "response.incomplete", token);
                            break;
                    }
                }
                return response;
            }
            if (!PolyPromptTelemetry.IsEnabled) return await OpenAsync().ConfigureAwait(false);
            return await InstrumentResponseCoreAsync(operation, model, enrich, OpenAsync, attach, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Run an operation that reports success as a boolean inside a telemetry scope; false is recorded as an error.
        /// </summary>
        /// <param name="operation">Operation label from <see cref="PolyPromptTelemetryNames"/>.</param>
        /// <param name="model">Model, or null.</param>
        /// <param name="core">The operation.</param>
        /// <param name="token">Caller cancellation token.</param>
        /// <returns>The operation's result.</returns>
        private protected Task<bool> InstrumentBoolAsync(string operation, string? model, Func<Task<bool>> core, CancellationToken token)
        {
            if (!PolyPromptTelemetry.IsEnabled) return core();
            return InstrumentBoolCoreAsync(operation, model, core, token);
        }

        /// <summary>
        /// Run an operation that returns a plain value inside a telemetry scope. It succeeds unless it throws or the HTTP
        /// transport underneath reported an error.
        /// </summary>
        /// <typeparam name="TValue">Result type.</typeparam>
        /// <param name="operation">Operation label from <see cref="PolyPromptTelemetryNames"/>.</param>
        /// <param name="model">Model, or null.</param>
        /// <param name="core">The operation.</param>
        /// <param name="token">Caller cancellation token.</param>
        /// <returns>The operation's result.</returns>
        private protected Task<TValue> InstrumentValueAsync<TValue>(string operation, string? model, Func<Task<TValue>> core, CancellationToken token)
        {
            if (!PolyPromptTelemetry.IsEnabled) return core();
            return InstrumentValueCoreAsync(operation, model, core, token);
        }

        #endregion

        #region Private-Methods

        private async Task<TResponse> InstrumentResponseCoreAsync<TResponse>(
            string operation,
            string? model,
            Action<OperationScope>? enrich,
            Func<Task<TResponse>> core,
            Action<TResponse, OperationScope>? attach,
            CancellationToken token)
            where TResponse : ResponseBase
        {
            OperationScope? scope = OperationScope.Start(this, operation, model, token);
            if (scope != null && enrich != null)
            {
                try
                {
                    enrich(scope);
                }
                catch
                {
                }
            }

            TResponse response;
            try
            {
                response = await core().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                scope?.Fail(ex);
                throw;
            }

            if (scope == null) return response;

            if (attach != null && response.Success)
            {
                try
                {
                    attach(response, scope);
                }
                catch
                {
                    scope.Complete(response);
                }
            }
            else
            {
                scope.Complete(response);
            }

            return response;
        }

        private async Task<bool> InstrumentBoolCoreAsync(string operation, string? model, Func<Task<bool>> core, CancellationToken token)
        {
            OperationScope? scope = OperationScope.Start(this, operation, model, token);
            try
            {
                bool result = await core().ConfigureAwait(false);
                scope?.CompleteBool(result);
                return result;
            }
            catch (Exception ex)
            {
                scope?.Fail(ex);
                throw;
            }
        }

        private async Task<TValue> InstrumentValueCoreAsync<TValue>(string operation, string? model, Func<Task<TValue>> core, CancellationToken token)
        {
            OperationScope? scope = OperationScope.Start(this, operation, model, token);
            try
            {
                TValue result = await core().ConfigureAwait(false);
                scope?.Complete(null);
                return result;
            }
            catch (Exception ex)
            {
                scope?.Fail(ex);
                throw;
            }
        }

        private void TrimCallDetailsIfNeeded()
        {
            if (_MaxCallDetails == 0)
            {
                _CallDetails.Clear();
                return;
            }

            int excess = _CallDetails.Count - _MaxCallDetails;
            if (excess > 0)
            {
                _CallDetails.RemoveRange(0, excess);
            }
        }

        #endregion

    }
}
