namespace PolyPrompt.Telemetry
{
    using System;
    using System.Diagnostics;
    using System.Threading;

    /// <summary>
    /// Telemetry for one agent harness process: an <c>exec</c> client span, the process duration and execution metrics,
    /// and the running-processes gauge. The process counterpart of <see cref="HttpCallScope"/>.
    /// </summary>
    internal sealed class ProcessCallScope
    {
        #region Private-Members

        private readonly long _StartTimestamp;
        private readonly string _Provider;
        private readonly string _Executable;
        private readonly InFlightEntry _ActiveEntry;
        private readonly OperationScope? _Operation;
        private int _Ended = 0;

        #endregion

        #region Public-Members

        /// <summary>
        /// The process span, or null when nothing is listening.
        /// </summary>
        public Activity? Activity { get; }

        #endregion

        #region Constructors-and-Factories

        private ProcessCallScope(string provider, string executable, OperationScope? operation)
        {
            _StartTimestamp = Stopwatch.GetTimestamp();
            _Provider = provider;
            _Executable = executable;
            _Operation = operation;

            Activity = PolyPromptTelemetry.Source.StartActivity("exec " + executable, ActivityKind.Client);
            if (Activity != null)
            {
                Activity.SetTag(PolyPromptTelemetryNames.Provider, provider);
                Activity.SetTag(PolyPromptTelemetryNames.ProcessExecutableName, executable);
            }

            _ActiveEntry = PolyPromptTelemetry.ActiveProcesses.Get(PolyPromptTelemetryNames.Provider, provider, PolyPromptTelemetryNames.ProcessExecutableName, executable);
            _ActiveEntry.Increment();
        }

        /// <summary>
        /// Start a scope, or return null when nothing is listening or telemetry fails.
        /// </summary>
        /// <param name="provider">Provider label.</param>
        /// <param name="executable">Executable name (not path), for example <c>codex</c>.</param>
        /// <param name="operation">Enclosing operation scope, which receives the process error type.</param>
        /// <returns>The scope, or null.</returns>
        public static ProcessCallScope? Start(string provider, string executable, OperationScope? operation)
        {
            if (!PolyPromptTelemetry.IsEnabled) return null;

            try
            {
                return new ProcessCallScope(provider, executable, operation);
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Record the process id on the span.
        /// </summary>
        /// <param name="processId">Process id.</param>
        public void SetProcessId(int processId)
        {
            try
            {
                Activity?.SetTag(PolyPromptTelemetryNames.ProcessPid, processId);
            }
            catch
            {
            }
        }

        /// <summary>
        /// End the scope. A null <paramref name="errorType"/> means the process ran and exited cleanly.
        /// </summary>
        /// <param name="exitCode">Exit code, or null when the process did not start or exit.</param>
        /// <param name="errorType">Bounded error type, or null.</param>
        /// <param name="callerCancelled">True when the caller cancelled; the error is then not reported to the operation.</param>
        public void Complete(int? exitCode, string? errorType, bool callerCancelled)
        {
            if (Interlocked.Exchange(ref _Ended, 1) == 1) return;

            try
            {
                if (errorType != null && !callerCancelled) _Operation?.ReportError(errorType);

                double seconds = Stopwatch.GetElapsedTime(_StartTimestamp).TotalSeconds;
                TagList tags = new TagList
                {
                    { PolyPromptTelemetryNames.Provider, _Provider },
                    { PolyPromptTelemetryNames.ProcessExecutableName, _Executable },
                };
                if (exitCode.HasValue) tags.Add(PolyPromptTelemetryNames.ProcessExitCode, exitCode.Value);
                if (errorType != null) tags.Add(PolyPromptTelemetryNames.ErrorType, errorType);

                PolyPromptTelemetry.SafeRecord(PolyPromptTelemetry.ProcessDuration, seconds, tags);
                PolyPromptTelemetry.SafeAdd(PolyPromptTelemetry.ProcessExecutions, 1, tags);

                if (Activity != null)
                {
                    if (exitCode.HasValue) Activity.SetTag(PolyPromptTelemetryNames.ProcessExitCode, exitCode.Value);
                    if (errorType != null)
                    {
                        Activity.SetTag(PolyPromptTelemetryNames.ErrorType, errorType);
                        Activity.SetStatus(ActivityStatusCode.Error, errorType);
                    }
                }
            }
            catch
            {
            }
            finally
            {
                _ActiveEntry.Decrement();
                if (Activity != null) PolyPromptTelemetry.StopDetached(Activity);
            }
        }

        #endregion
    }
}
