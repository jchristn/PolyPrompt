namespace PolyPrompt.Telemetry
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;

    /// <summary>
    /// Telemetry for one public client operation: a client span named <c>{provider} {operation}</c>, the operation
    /// duration and outcome metrics, GenAI token usage, and (for streaming operations) time to first chunk and chunk
    /// counts. The scope is published through an async-local so the transport layer underneath can attach its error
    /// type. Every member is best-effort: a telemetry failure is swallowed and never reaches the caller.
    /// Thread safety: a scope is owned by one operation; ending it is idempotent and safe from any thread.
    /// </summary>
    internal sealed class OperationScope
    {
        #region Private-Members

        private static readonly AsyncLocal<OperationScope?> _Current = new AsyncLocal<OperationScope?>();

        private readonly long _StartTimestamp;
        private readonly string _Provider;
        private readonly string _Capability;
        private readonly string _Operation;
        private readonly string? _GenAiOperation;
        private readonly string _ModelLabel;
        private readonly CancellationToken _CallerToken;
        private readonly InFlightEntry _ActiveEntry;
        private int _Ended = 0;
        private long _Chunks = 0;
        private bool _SawFirstChunk = false;

        #endregion

        #region Public-Members

        /// <summary>
        /// The scope of the operation running in the current async flow, or null.
        /// </summary>
        public static OperationScope? Current
        {
            get { return _Current.Value; }
        }

        /// <summary>
        /// The operation's span, or null when no trace listener sampled it.
        /// </summary>
        public Activity? Activity { get; }

        /// <summary>
        /// Error type reported by a lower layer (the HTTP transport) for this operation, or null. The first value wins.
        /// </summary>
        public string? ErrorType { get; set; }

        /// <summary>
        /// Provider label.
        /// </summary>
        public string Provider
        {
            get { return _Provider; }
        }

        #endregion

        #region Constructors-and-Factories

        private OperationScope(ClientBase client, string operation, string? model, CancellationToken token)
        {
            _StartTimestamp = Stopwatch.GetTimestamp();
            _Provider = client.TelemetryProvider;
            _Capability = PolyPromptTelemetry.ResolveCapability(client);
            _Operation = operation;
            _GenAiOperation = PolyPromptTelemetry.ToGenAiOperation(operation);
            _ModelLabel = PolyPromptTelemetry.Bound(model, 128);
            _CallerToken = token;

            Activity = PolyPromptTelemetry.Source.StartActivity(_Provider + " " + operation, ActivityKind.Client);
            if (Activity != null)
            {
                Activity.SetTag(PolyPromptTelemetryNames.Provider, _Provider);
                Activity.SetTag(PolyPromptTelemetryNames.Capability, _Capability);
                Activity.SetTag(PolyPromptTelemetryNames.Operation, operation);
                Activity.SetTag(PolyPromptTelemetryNames.GenAiOperationName, _GenAiOperation ?? operation);
                if (!string.IsNullOrWhiteSpace(model)) Activity.SetTag(PolyPromptTelemetryNames.RequestModel, model);
                if (Uri.TryCreate(client.Endpoint, UriKind.Absolute, out Uri? endpoint))
                {
                    Activity.SetTag(PolyPromptTelemetryNames.ServerAddress, endpoint.Host);
                    Activity.SetTag(PolyPromptTelemetryNames.ServerPort, endpoint.Port);
                }
            }

            _ActiveEntry = PolyPromptTelemetry.ActiveOperations.Get(PolyPromptTelemetryNames.Provider, _Provider, PolyPromptTelemetryNames.Operation, operation);
            _ActiveEntry.Increment();
            _Current.Value = this;
        }

        /// <summary>
        /// Start a scope for an operation, or return null when nothing is listening or telemetry fails. The scope becomes
        /// <see cref="Current"/> for the rest of the calling async method; callers must be async methods so the value
        /// does not leak back to their own caller.
        /// </summary>
        /// <param name="client">Client running the operation.</param>
        /// <param name="operation">Operation label from <see cref="PolyPromptTelemetryNames"/>.</param>
        /// <param name="model">Requested model, or null.</param>
        /// <param name="token">Caller cancellation token, used to tell cancellation from timeout.</param>
        /// <returns>The scope, or null.</returns>
        public static OperationScope? Start(ClientBase client, string operation, string? model, CancellationToken token)
        {
            if (!PolyPromptTelemetry.IsEnabled) return null;

            try
            {
                return new OperationScope(client, operation, model, token);
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Set a span attribute.
        /// </summary>
        /// <param name="key">Attribute key.</param>
        /// <param name="value">Attribute value.</param>
        public void SetTag(string key, object? value)
        {
            try
            {
                Activity?.SetTag(key, value);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Record the request's completion settings on the span.
        /// </summary>
        /// <param name="maxTokens">Max tokens.</param>
        /// <param name="temperature">Temperature, or null.</param>
        /// <param name="topP">Top-p, or null.</param>
        public void SetCompletionSettings(int maxTokens, double? temperature, double? topP)
        {
            if (Activity == null) return;
            SetTag(PolyPromptTelemetryNames.RequestMaxTokens, maxTokens);
            if (temperature.HasValue) SetTag(PolyPromptTelemetryNames.RequestTemperature, temperature.Value);
            if (topP.HasValue) SetTag(PolyPromptTelemetryNames.RequestTopP, topP.Value);
        }

        /// <summary>
        /// Record the number of inputs in the request.
        /// </summary>
        /// <param name="count">Input count.</param>
        public void RecordBatchSize(int count)
        {
            try
            {
                SetTag(PolyPromptTelemetryNames.BatchSizeAttribute, count);
                if (PolyPromptTelemetry.BatchSize.Enabled)
                {
                    TagList tags = new TagList
                    {
                        { PolyPromptTelemetryNames.Provider, _Provider },
                        { PolyPromptTelemetryNames.Operation, _Operation },
                    };
                    PolyPromptTelemetry.SafeRecord(PolyPromptTelemetry.BatchSize, count, tags);
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Count the questions of a decision request by type.
        /// </summary>
        /// <param name="questions">Questions.</param>
        public void RecordDecisionQuestions(List<DecisionQuestion> questions)
        {
            try
            {
                RecordBatchSize(questions.Count);
                if (!PolyPromptTelemetry.DecisionQuestions.Enabled) return;
                foreach (DecisionQuestion question in questions)
                {
                    TagList tags = new TagList
                    {
                        { PolyPromptTelemetryNames.Provider, _Provider },
                        { PolyPromptTelemetryNames.QuestionType, question.Type.ToString().ToLowerInvariant() },
                    };
                    PolyPromptTelemetry.SafeAdd(PolyPromptTelemetry.DecisionQuestions, 1, tags);
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// End the operation from its response: success when <see cref="ResponseBase.Success"/> is true, otherwise an
        /// error classified by the transport's error type, the HTTP status, or <c>invalid_response</c>.
        /// </summary>
        /// <param name="response">
        /// Response, or null for operations that return no response object; those succeed unless the transport reported
        /// an error type.
        /// </param>
        public void Complete(ResponseBase? response)
        {
            if (response == null)
            {
                if (ErrorType == null)
                    End(PolyPromptTelemetryNames.OutcomeSuccess, null, null);
                else
                    End(PolyPromptTelemetryNames.OutcomeError, ErrorType, null);
                return;
            }

            if (response.Success)
            {
                End(PolyPromptTelemetryNames.OutcomeSuccess, null, response);
                return;
            }

            string errorType = ErrorType
                ?? (response.StatusCode.HasValue && (response.StatusCode.Value < 200 || response.StatusCode.Value > 299)
                    ? response.StatusCode.Value.ToString(CultureInfo.InvariantCulture)
                    : PolyPromptTelemetryNames.ErrorInvalidResponse);

            End(PolyPromptTelemetryNames.OutcomeError, errorType, response);
        }

        /// <summary>
        /// End an operation that reports its result as a boolean.
        /// </summary>
        /// <param name="success">The operation's result.</param>
        public void CompleteBool(bool success)
        {
            if (success)
                End(PolyPromptTelemetryNames.OutcomeSuccess, null, null);
            else
                End(PolyPromptTelemetryNames.OutcomeError, ErrorType ?? PolyPromptTelemetryNames.ErrorOperationFailed, null);
        }

        /// <summary>
        /// End the operation with an explicit outcome.
        /// </summary>
        /// <param name="outcome">Outcome value.</param>
        /// <param name="errorType">Error type, or null.</param>
        public void CompleteWith(string outcome, string? errorType)
        {
            End(outcome, errorType, null);
        }

        /// <summary>
        /// End the operation from an exception: cancellation by the caller, a timeout, or an error.
        /// </summary>
        /// <param name="ex">Exception.</param>
        public void Fail(Exception ex)
        {
            Fail(ex, CancellationToken.None, null);
        }

        /// <summary>
        /// Record an error raised by a lower layer without ending the operation.
        /// </summary>
        /// <param name="errorType">Error type.</param>
        public void ReportError(string errorType)
        {
            if (ErrorType == null) ErrorType = errorType;
        }

        /// <summary>
        /// Wrap a response's chunk stream so the operation ends when the stream does: completed, failed, or abandoned by
        /// the consumer. Records time to first chunk and the chunk count.
        /// </summary>
        /// <typeparam name="TChunk">Chunk type.</typeparam>
        /// <param name="source">Chunk stream.</param>
        /// <param name="response">Response whose fields (usage, finish reason) are final once the stream ends.</param>
        /// <param name="isContent">Whether a chunk carries content (and so counts as a chunk).</param>
        /// <param name="token">Enumeration cancellation token.</param>
        /// <returns>The wrapped stream.</returns>
        public async IAsyncEnumerable<TChunk> WrapStream<TChunk>(
            IAsyncEnumerable<TChunk> source,
            ResponseBase response,
            Func<TChunk, bool> isContent,
            [EnumeratorCancellation] CancellationToken token = default)
        {
            bool completed = false;
            Exception? failure = null;
            IAsyncEnumerator<TChunk> enumerator = source.GetAsyncEnumerator(token);

            try
            {
                while (true)
                {
                    TChunk item;
                    try
                    {
                        if (!await enumerator.MoveNextAsync().ConfigureAwait(false)) break;
                        item = enumerator.Current;
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                        throw;
                    }

                    ObserveChunk(item, isContent);
                    yield return item;
                }

                completed = true;
            }
            finally
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);

                if (completed)
                {
                    Complete(response);
                }
                else if (failure != null)
                {
                    Fail(failure, token, response);
                }
                else
                {
                    End(PolyPromptTelemetryNames.OutcomeAbandoned, null, response);
                }
            }
        }

        #endregion

        #region Private-Methods

        private void ObserveChunk<TChunk>(TChunk item, Func<TChunk, bool> isContent)
        {
            try
            {
                if (!isContent(item)) return;
                _Chunks++;
                if (_SawFirstChunk) return;
                _SawFirstChunk = true;

                double seconds = Stopwatch.GetElapsedTime(_StartTimestamp).TotalSeconds;
                SetTag(PolyPromptTelemetryNames.StreamTimeToFirstChunkMs, (long)(seconds * 1000));
                if (PolyPromptTelemetry.StreamTimeToFirstChunk.Enabled)
                {
                    TagList tags = new TagList
                    {
                        { PolyPromptTelemetryNames.Provider, _Provider },
                        { PolyPromptTelemetryNames.Operation, _Operation },
                    };
                    PolyPromptTelemetry.SafeRecord(PolyPromptTelemetry.StreamTimeToFirstChunk, seconds, tags);
                }
            }
            catch
            {
            }
        }

        private void Fail(Exception ex, CancellationToken enumerationToken, ResponseBase? response)
        {
            if (ex is OperationCanceledException)
            {
                if (_CallerToken.IsCancellationRequested || enumerationToken.IsCancellationRequested)
                    End(PolyPromptTelemetryNames.OutcomeCancelled, null, response);
                else
                    End(PolyPromptTelemetryNames.OutcomeTimeout, PolyPromptTelemetryNames.ErrorTimeout, response);
                return;
            }

            try
            {
                PolyPromptTelemetry.RecordException(Activity, ex);
            }
            catch
            {
            }

            End(PolyPromptTelemetryNames.OutcomeError, ex.GetType().FullName ?? ex.GetType().Name, response);
        }

        private void End(string outcome, string? errorType, ResponseBase? response)
        {
            if (Interlocked.Exchange(ref _Ended, 1) == 1) return;

            try
            {
                double seconds = Stopwatch.GetElapsedTime(_StartTimestamp).TotalSeconds;

                TagList tags = new TagList
                {
                    { PolyPromptTelemetryNames.Provider, _Provider },
                    { PolyPromptTelemetryNames.Capability, _Capability },
                    { PolyPromptTelemetryNames.Operation, _Operation },
                    { PolyPromptTelemetryNames.Outcome, outcome },
                };
                if (errorType != null) tags.Add(PolyPromptTelemetryNames.ErrorType, errorType);

                PolyPromptTelemetry.SafeRecord(PolyPromptTelemetry.OperationDuration, seconds, tags);
                PolyPromptTelemetry.SafeAdd(PolyPromptTelemetry.Operations, 1, tags);

                if (_GenAiOperation != null && PolyPromptTelemetry.GenAiOperationDuration.Enabled)
                {
                    TagList genAiTags = new TagList
                    {
                        { PolyPromptTelemetryNames.GenAiOperationName, _GenAiOperation },
                        { PolyPromptTelemetryNames.Provider, _Provider },
                        { PolyPromptTelemetryNames.RequestModel, _ModelLabel },
                    };
                    if (errorType != null) genAiTags.Add(PolyPromptTelemetryNames.ErrorType, errorType);
                    PolyPromptTelemetry.SafeRecord(PolyPromptTelemetry.GenAiOperationDuration, seconds, genAiTags);
                }

                if (_SawFirstChunk || _Chunks > 0)
                {
                    SetTag(PolyPromptTelemetryNames.StreamChunkCount, _Chunks);
                    if (PolyPromptTelemetry.StreamChunks.Enabled)
                    {
                        TagList chunkTags = new TagList
                        {
                            { PolyPromptTelemetryNames.Provider, _Provider },
                            { PolyPromptTelemetryNames.Operation, _Operation },
                        };
                        PolyPromptTelemetry.SafeAdd(PolyPromptTelemetry.StreamChunks, _Chunks, chunkTags);
                    }
                }

                if (response != null) RecordResponse(response);
            }
            catch
            {
            }
            finally
            {
                _ActiveEntry.Decrement();
                StopActivity(outcome, errorType);
            }
        }

        private void StopActivity(string outcome, string? errorType)
        {
            if (Activity == null) return;

            try
            {
                Activity.SetTag(PolyPromptTelemetryNames.Outcome, outcome);
                if (errorType != null)
                {
                    Activity.SetTag(PolyPromptTelemetryNames.ErrorType, errorType);
                    bool isStatus = int.TryParse(errorType, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
                    Activity.SetStatus(ActivityStatusCode.Error, isStatus ? "HTTP " + errorType : errorType);
                }
                else if (outcome == PolyPromptTelemetryNames.OutcomeSuccess)
                {
                    Activity.SetStatus(ActivityStatusCode.Ok);
                }
            }
            catch
            {
            }

            PolyPromptTelemetry.StopDetached(Activity);
        }

        private void RecordResponse(ResponseBase response)
        {
            TokenUsage? usage = null;
            string? finishReason = null;
            string? responseId = null;
            int? toolCalls = null;

            switch (response)
            {
                case ChatResponse chat:
                    usage = chat.Usage;
                    break;
                case ChatStreamingResponse chatStream:
                    usage = chatStream.Usage;
                    finishReason = chatStream.FinishReason;
                    responseId = chatStream.ResponseId;
                    break;
                case ToolChatResponse toolChat:
                    usage = toolChat.Usage;
                    finishReason = toolChat.FinishReason;
                    responseId = toolChat.ResponseId;
                    toolCalls = toolChat.ToolCalls?.Count;
                    break;
                case ToolChatStreamingResponse toolChatStream:
                    usage = toolChatStream.Usage;
                    finishReason = toolChatStream.FinishReason;
                    responseId = toolChatStream.ResponseId;
                    toolCalls = toolChatStream.ToolCalls?.Count;
                    break;
                case DecisionResponse decision:
                    usage = decision.Usage;
                    break;
                case RerankResponse rerank:
                    responseId = rerank.ResponseId;
                    break;
            }

            if (Activity != null)
            {
                if (!string.IsNullOrEmpty(response.Model)) Activity.SetTag(PolyPromptTelemetryNames.ResponseModel, response.Model);
                if (!string.IsNullOrEmpty(responseId)) Activity.SetTag(PolyPromptTelemetryNames.ResponseId, responseId);
                if (!string.IsNullOrEmpty(finishReason)) Activity.SetTag(PolyPromptTelemetryNames.FinishReason, new[] { finishReason });
                if (response.StatusCode.HasValue) Activity.SetTag(PolyPromptTelemetryNames.HttpStatusCode, response.StatusCode.Value);
                if (toolCalls.HasValue) Activity.SetTag(PolyPromptTelemetryNames.ToolCallCount, toolCalls.Value);
            }

            if (toolCalls.HasValue && toolCalls.Value > 0 && PolyPromptTelemetry.ToolCalls.Enabled)
            {
                TagList tags = new TagList
                {
                    { PolyPromptTelemetryNames.Provider, _Provider },
                    { PolyPromptTelemetryNames.Operation, _Operation },
                };
                PolyPromptTelemetry.SafeAdd(PolyPromptTelemetry.ToolCalls, toolCalls.Value, tags);
            }

            if (!string.IsNullOrEmpty(finishReason) && PolyPromptTelemetry.FinishReasons.Enabled)
            {
                TagList tags = new TagList
                {
                    { PolyPromptTelemetryNames.Provider, _Provider },
                    { PolyPromptTelemetryNames.Operation, _Operation },
                    { PolyPromptTelemetryNames.FinishReasonLabel, PolyPromptTelemetry.Bound(finishReason.ToLowerInvariant(), 32) },
                };
                PolyPromptTelemetry.SafeAdd(PolyPromptTelemetry.FinishReasons, 1, tags);
            }

            if (usage != null) RecordUsage(usage);
        }

        private void RecordUsage(TokenUsage usage)
        {
            if (Activity != null)
            {
                if (usage.PromptTokens.HasValue) Activity.SetTag(PolyPromptTelemetryNames.UsageInputTokens, usage.PromptTokens.Value);
                if (usage.CompletionTokens.HasValue) Activity.SetTag(PolyPromptTelemetryNames.UsageOutputTokens, usage.CompletionTokens.Value);
                if (usage.CachedPromptTokens.HasValue) Activity.SetTag(PolyPromptTelemetryNames.UsageCachedInputTokens, usage.CachedPromptTokens.Value);
                if (usage.CacheCreationTokens.HasValue) Activity.SetTag(PolyPromptTelemetryNames.UsageCacheCreationTokens, usage.CacheCreationTokens.Value);
                if (usage.ReasoningTokens.HasValue) Activity.SetTag(PolyPromptTelemetryNames.UsageReasoningTokens, usage.ReasoningTokens.Value);
            }

            string genAiOperation = _GenAiOperation ?? _Operation;
            RecordGenAiTokens(genAiOperation, "input", usage.PromptTokens);
            RecordGenAiTokens(genAiOperation, "output", usage.CompletionTokens);

            RecordTokens("input", usage.PromptTokens);
            RecordTokens("output", usage.CompletionTokens);
            RecordTokens("cached_input", usage.CachedPromptTokens);
            RecordTokens("cache_creation", usage.CacheCreationTokens);
            RecordTokens("reasoning", usage.ReasoningTokens);
        }

        private void RecordGenAiTokens(string genAiOperation, string tokenType, int? value)
        {
            if (!value.HasValue || value.Value < 0 || !PolyPromptTelemetry.GenAiTokenUsage.Enabled) return;
            TagList tags = new TagList
            {
                { PolyPromptTelemetryNames.GenAiOperationName, genAiOperation },
                { PolyPromptTelemetryNames.Provider, _Provider },
                { PolyPromptTelemetryNames.RequestModel, _ModelLabel },
                { PolyPromptTelemetryNames.GenAiTokenType, tokenType },
            };
            PolyPromptTelemetry.SafeRecord(PolyPromptTelemetry.GenAiTokenUsage, value.Value, tags);
        }

        private void RecordTokens(string tokenType, int? value)
        {
            if (!value.HasValue || value.Value <= 0 || !PolyPromptTelemetry.Tokens.Enabled) return;
            TagList tags = new TagList
            {
                { PolyPromptTelemetryNames.Provider, _Provider },
                { PolyPromptTelemetryNames.Operation, _Operation },
                { PolyPromptTelemetryNames.RequestModel, _ModelLabel },
                { PolyPromptTelemetryNames.TokenType, tokenType },
            };
            PolyPromptTelemetry.SafeAdd(PolyPromptTelemetry.Tokens, value.Value, tags);
        }

        #endregion
    }
}
