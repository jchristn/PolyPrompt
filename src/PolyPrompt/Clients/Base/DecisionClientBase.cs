namespace PolyPrompt.Clients
{
    using System.Diagnostics;
    using PolyPrompt.Models;
    using PolyPrompt.Telemetry;
    using SyslogLogging;

    /// <summary>
    /// Base class for decision clients. A decision model reads a state (text or structured data) and answers typed
    /// questions about it (yes/no, pick one option, or rate along a rubric) with calibrated probabilities instead of
    /// generated text. Requests are validated before any network call. Settings come from <see cref="Defaults"/>,
    /// overridden per call by a <see cref="DecisionOptions"/>.
    /// </summary>
    public abstract class DecisionClientBase : ClientBase
    {
        #region Private-Members

        private int _MaxConcurrency = 4;

        #endregion

        #region Public-Members

        /// <summary>
        /// Client-wide default settings. Per-call options override these values field by field.
        /// </summary>
        public abstract DecisionOptions Defaults { get; }

        /// <summary>
        /// Model name used when a call does not set one. Reads and writes <c>Defaults.Model</c>.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null, empty, or whitespace.</exception>
        public string? Model
        {
            get { return Defaults.Model; }
            set
            {
                if (string.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Model));
                Defaults.Model = value;
            }
        }

        /// <summary>
        /// Maximum number of requests the batch overload of <see cref="DecideAsync(List{DecisionRequest}, DecisionOptions?, CancellationToken)"/>
        /// sends at once, for providers without a native batch endpoint. Clamped to 1..64. Default: 4.
        /// </summary>
        public int MaxConcurrency
        {
            get { return _MaxConcurrency; }
            set { _MaxConcurrency = Math.Clamp(value, 1, 64); }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new decision client.
        /// </summary>
        /// <param name="endpoint">Endpoint URL.</param>
        /// <param name="apiKey">API key (nullable).</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="httpClient">Optional HTTP client; see <see cref="ClientBase"/>.</param>
        protected DecisionClientBase(string endpoint, string? apiKey, LoggingModule logging, HttpClient? httpClient = null)
            : base(endpoint, apiKey, logging, httpClient)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Answer the questions in a request about its state.
        /// </summary>
        /// <param name="request">The state and questions.</param>
        /// <param name="options">Optional per-call settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A DecisionResponse with one answer per question when successful.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request or its state is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the questions are invalid; see <see cref="ValidateRequest"/>.</exception>
        /// <exception cref="InvalidOperationException">Thrown when no model is configured.</exception>
        public Task<DecisionResponse> DecideAsync(DecisionRequest request, DecisionOptions? options = null, CancellationToken token = default)
        {
            ValidateRequest(request, nameof(request));
            string model = ResolveModel(options);
            return InstrumentAsync(PolyPromptTelemetryNames.OperationDecide, model, s => s.RecordDecisionQuestions(request.Questions),
                () => DecideCoreAsync(request, model, options, token), token);
        }

        /// <summary>
        /// Answer a batch of requests, returning one response per request in input order. Every request is validated
        /// before any is sent. Providers without a native batch endpoint send the requests concurrently, at most
        /// <see cref="MaxConcurrency"/> at a time; each response reports its own success or failure.
        /// </summary>
        /// <param name="requests">The requests. Cannot be null or empty, and no element can be null.</param>
        /// <param name="options">Optional per-call settings applied to every request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One response per request, in input order.</returns>
        /// <exception cref="ArgumentNullException">Thrown when requests is null.</exception>
        /// <exception cref="ArgumentException">Thrown when requests is empty, contains a null element, or any request is invalid.</exception>
        /// <exception cref="InvalidOperationException">Thrown when no model is configured.</exception>
        public Task<List<DecisionResponse>> DecideAsync(List<DecisionRequest> requests, DecisionOptions? options = null, CancellationToken token = default)
        {
            ValidateInputList(requests, nameof(requests), "Decision batches require at least one request.");
            for (int i = 0; i < requests.Count; i++)
            {
                ValidateRequest(requests[i], "requests[" + i + "]");
            }

            string model = ResolveModel(options);
            if (!PolyPromptTelemetry.IsEnabled) return DecideBatchCoreAsync(requests, model, options, token);
            return DecideBatchInstrumentedAsync(requests, model, options, token);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Provider implementation of <see cref="DecideAsync(DecisionRequest, DecisionOptions?, CancellationToken)"/>.
        /// </summary>
        /// <param name="request">Validated request.</param>
        /// <param name="model">Resolved model.</param>
        /// <param name="options">Per-call options as passed.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The decision response.</returns>
        protected abstract Task<DecisionResponse> DecideCoreAsync(DecisionRequest request, string model, DecisionOptions? options, CancellationToken token);

        /// <summary>
        /// Provider implementation of the batch overload. The default sends each request through
        /// <see cref="DecideCoreAsync"/>, at most <see cref="MaxConcurrency"/> at a time. Providers with a native batch
        /// endpoint override it.
        /// </summary>
        /// <param name="requests">Validated requests.</param>
        /// <param name="model">Resolved model.</param>
        /// <param name="options">Per-call options as passed.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One response per request, in input order.</returns>
        protected virtual async Task<List<DecisionResponse>> DecideBatchCoreAsync(List<DecisionRequest> requests, string model, DecisionOptions? options, CancellationToken token)
        {
            DecisionResponse[] responses = new DecisionResponse[requests.Count];
            using SemaphoreSlim gate = new SemaphoreSlim(_MaxConcurrency);

            Task[] tasks = new Task[requests.Count];
            for (int i = 0; i < requests.Count; i++)
            {
                int index = i;
                tasks[i] = Task.Run(async () =>
                {
                    bool observed = PolyPromptTelemetry.IsEnabled;
                    InFlightEntry? queued = observed ? PolyPromptTelemetry.DecisionQueued.Get(PolyPromptTelemetryNames.Provider, TelemetryProvider) : null;
                    long queuedAt = Stopwatch.GetTimestamp();
                    queued?.Increment();
                    Activity? queuedSpan = observed ? StartQueuedSpan() : null;
                    try
                    {
                        await gate.WaitAsync(token).ConfigureAwait(false);
                    }
                    finally
                    {
                        queued?.Decrement();
                        PolyPromptTelemetry.StopDetached(queuedSpan);
                    }

                    InFlightEntry? inFlight = null;
                    if (observed) inFlight = RecordSlotAcquired(queuedAt);

                    try
                    {
                        DecisionRequest request = requests[index];
                        responses[index] = await InstrumentAsync(PolyPromptTelemetryNames.OperationDecide, model, s => s.RecordDecisionQuestions(request.Questions),
                            () => DecideCoreAsync(request, model, options, token), token).ConfigureAwait(false);
                    }
                    finally
                    {
                        inFlight?.Decrement();
                        gate.Release();
                    }
                }, token);
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
            return responses.ToList();
        }

        /// <summary>
        /// Validate a decision request. Checks that the state is set; that there is at least one question; that every
        /// question is non-null with a non-empty id that is unique (ordinal) within the request and non-empty
        /// instructions; that a choice question has 2 to 255 options, each non-null with a non-empty value unique
        /// within the question; and that a score question has 2 to 10 non-null, non-empty levels.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="parameterName">Parameter name reported in exceptions.</param>
        /// <exception cref="ArgumentNullException">Thrown when request or its state is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the questions are invalid.</exception>
        protected static void ValidateRequest(DecisionRequest request, string parameterName)
        {
            if (request == null) throw new ArgumentNullException(parameterName);
            if (request.State == null) throw new ArgumentNullException(parameterName, "Decision requests require a state.");
            if (request.State is string text && string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Decision state text cannot be empty or whitespace.", parameterName);

            if (request.Questions == null || request.Questions.Count == 0)
                throw new ArgumentException("Decision requests require at least one question.", parameterName);

            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < request.Questions.Count; i++)
            {
                DecisionQuestion question = request.Questions[i];
                string where = "Questions[" + i + "]";

                if (question == null)
                    throw new ArgumentException(where + " cannot be null.", parameterName);
                if (string.IsNullOrWhiteSpace(question.Id))
                    throw new ArgumentException(where + " requires a non-empty Id.", parameterName);
                if (!ids.Add(question.Id))
                    throw new ArgumentException("Question id '" + question.Id + "' is used more than once.", parameterName);
                if (question.Instructions == null || string.IsNullOrWhiteSpace(question.Instructions.Text))
                    throw new ArgumentException("Question '" + question.Id + "' requires non-empty Instructions.", parameterName);

                if (question is ChoiceQuestion choice)
                {
                    List<DecisionOption>? choiceOptions = choice.Options;
                    int count = choiceOptions?.Count ?? 0;
                    if (count < ChoiceQuestion.MinOptions || count > ChoiceQuestion.MaxOptions)
                        throw new ArgumentException("Choice question '" + question.Id + "' requires " + ChoiceQuestion.MinOptions + " to " + ChoiceQuestion.MaxOptions + " options; it has " + count + ".", parameterName);

                    HashSet<string> values = new HashSet<string>(StringComparer.Ordinal);
                    for (int j = 0; j < count; j++)
                    {
                        DecisionOption option = choiceOptions![j];
                        if (option == null || string.IsNullOrWhiteSpace(option.Value))
                            throw new ArgumentException("Choice question '" + question.Id + "' option " + j + " requires a non-empty Value.", parameterName);
                        if (!values.Add(option.Value))
                            throw new ArgumentException("Choice question '" + question.Id + "' uses option '" + option.Value + "' more than once.", parameterName);
                    }
                }
                else if (question is ScoreQuestion score)
                {
                    int count = score.Levels?.Count ?? 0;
                    if (count < ScoreQuestion.MinLevels || count > ScoreQuestion.MaxLevels)
                        throw new ArgumentException("Score question '" + question.Id + "' requires " + ScoreQuestion.MinLevels + " to " + ScoreQuestion.MaxLevels + " levels; it has " + count + ".", parameterName);

                    for (int j = 0; j < count; j++)
                    {
                        DecisionContent level = score.Levels![j];
                        if (level == null || string.IsNullOrWhiteSpace(level.Text))
                            throw new ArgumentException("Score question '" + question.Id + "' level " + j + " cannot be empty.", parameterName);
                    }
                }
                else if (question is not BinaryQuestion)
                {
                    throw new ArgumentException("Question '" + question.Id + "' has an unsupported type " + question.GetType().Name + ".", parameterName);
                }
            }
        }

        /// <summary>
        /// Resolve the model for a call: the per-call model, else <c>Defaults.Model</c>.
        /// </summary>
        /// <param name="options">Per-call options.</param>
        /// <returns>The model.</returns>
        /// <exception cref="InvalidOperationException">Thrown when no model is set.</exception>
        protected string ResolveModel(DecisionOptions? options)
        {
            string? model = options?.Model ?? Defaults.Model;
            if (string.IsNullOrWhiteSpace(model))
                throw new InvalidOperationException("No model is configured. Set Model or pass a model in the options.");
            return model;
        }

        #endregion

        #region Private-Methods

        private async Task<List<DecisionResponse>> DecideBatchInstrumentedAsync(List<DecisionRequest> requests, string model, DecisionOptions? options, CancellationToken token)
        {
            OperationScope? scope = OperationScope.Start(this, PolyPromptTelemetryNames.OperationDecideBatch, model, token);
            scope?.RecordBatchSize(requests.Count);
            scope?.SetTag(PolyPromptTelemetryNames.MaxConcurrency, _MaxConcurrency);

            try
            {
                List<DecisionResponse> responses = await DecideBatchCoreAsync(requests, model, options, token).ConfigureAwait(false);
                if (scope != null)
                {
                    bool allSucceeded = responses.All(r => r != null && r.Success);
                    scope.CompleteWith(
                        allSucceeded ? PolyPromptTelemetryNames.OutcomeSuccess : PolyPromptTelemetryNames.OutcomeError,
                        allSucceeded ? null : PolyPromptTelemetryNames.ErrorBatchItemFailed);
                }
                return responses;
            }
            catch (Exception ex)
            {
                scope?.Fail(ex);
                throw;
            }
        }

        private Activity? StartQueuedSpan()
        {
            try
            {
                Activity? span = PolyPromptTelemetry.Source.StartActivity("stage:queued", ActivityKind.Internal);
                span?.SetTag(PolyPromptTelemetryNames.Provider, TelemetryProvider);
                span?.SetTag(PolyPromptTelemetryNames.MaxConcurrency, _MaxConcurrency);
                return span;
            }
            catch
            {
                return null;
            }
        }

        private InFlightEntry? RecordSlotAcquired(long queuedAt)
        {
            try
            {
                double seconds = Stopwatch.GetElapsedTime(queuedAt).TotalSeconds;
                if (PolyPromptTelemetry.DecisionQueueDuration.Enabled)
                {
                    TagList tags = new TagList { { PolyPromptTelemetryNames.Provider, TelemetryProvider } };
                    PolyPromptTelemetry.SafeRecord(PolyPromptTelemetry.DecisionQueueDuration, seconds, tags);
                }

                InFlightEntry inFlight = PolyPromptTelemetry.DecisionInFlight.Get(PolyPromptTelemetryNames.Provider, TelemetryProvider);
                inFlight.Increment();
                return inFlight;
            }
            catch
            {
                return null;
            }
        }

        #endregion
    }
}
