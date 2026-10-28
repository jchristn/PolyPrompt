namespace PolyPrompt.Clients
{
    using System.Diagnostics;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using PolyPrompt.Helpers;
    using PolyPrompt.Models;
    using PolyPrompt.Telemetry;
    using SyslogLogging;

    /// <summary>
    /// Base class for agent harness clients: Claude Code, Codex, Gemini CLI, Cursor CLI, Mux, Pi, and OpenCode. A harness
    /// is not a model API; it is a local CLI that runs its own tool loop against the filesystem and reports agent events.
    /// This base runs the CLI as a child process (never through a shell), delivers the prompt over standard input where
    /// the CLI allows it, maps output lines to <see cref="CliAgentEvent"/>s through the client's parser, enforces the
    /// timeout and output limit by killing the whole process tree, records each process in
    /// <see cref="ClientBase.CallDetails"/> as a <see cref="ProcessCallDetail"/>, and reports telemetry. Authentication is
    /// delegated to the CLI's own login; there is no API key parameter.
    /// </summary>
    public abstract class CliAgentClientBase : ClientBase
    {
        #region Private-Members

        private const int _CompletionExitGraceMs = 5000;
        private const int _MaxConnectivityTimeoutMs = 30000;
        private const int _MaxCommandOutputCharacters = 4_000_000;

        private static readonly Regex _AuthenticationPattern = new Regex(
            @"\b401\b|\b403\b|unauthori[sz]ed|unauthenticated|not (logged|signed) in|not authenticated|authentication (failed|required|error)|invalid (x-)?api[ -_]?key|api[ -_]?key (is )?(missing|invalid|not (set|found|configured))|missing (bearer|api key|credentials)|please (run )?[`'""]?/?log ?in|login required|run [`'""]?[a-z-]+ (auth )?login|credentials? (expired|invalid|not found|are invalid)|token (has )?expired|no auth credentials",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex _RateLimitPattern = new Regex(
            @"\b429\b|rate[ -_]?limit|too many requests|quota|usage limit|resource[ _]exhausted|limit (reached|exceeded)|try again (later|in)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private string _Executable;
        private CliPermissionMode _PermissionMode = CliPermissionMode.Restricted;

        #endregion

        #region Public-Members

        /// <summary>
        /// Executable name (resolved on <c>PATH</c>) or full path of the harness CLI, for example <c>codex</c>.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown when set to null, empty, or whitespace.</exception>
        public string Executable
        {
            get { return _Executable; }
            set
            {
                if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Executable is required.", nameof(Executable));
                _Executable = value;
            }
        }

        /// <summary>
        /// Default model, in the harness's own naming. Null lets the harness choose. Overridden by
        /// <see cref="CliAgentRequest.Model"/>.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Default permission mode. Default: <see cref="CliPermissionMode.Restricted"/>. Overridden by
        /// <see cref="CliAgentRequest.PermissionMode"/>.
        /// </summary>
        public CliPermissionMode PermissionMode
        {
            get { return _PermissionMode; }
            set { _PermissionMode = value; }
        }

        /// <summary>
        /// Default working directory, or null for the current directory. Overridden by
        /// <see cref="CliAgentRequest.WorkingDirectory"/>.
        /// </summary>
        public string? WorkingDirectory { get; set; } = null;

        /// <summary>
        /// Environment variables applied to every process this client starts, under the request's own environment. A null
        /// value removes the variable from the inherited environment.
        /// </summary>
        public Dictionary<string, string?> Environment { get; set; } = new Dictionary<string, string?>(StringComparer.Ordinal);

        /// <summary>
        /// True when the harness can resume a session by id. When false, <see cref="CliAgentRequest.History"/> is always
        /// replayed into the prompt.
        /// </summary>
        public abstract bool SupportsSessionResume { get; }

        #endregion

        #region Protected-Members

        /// <summary>
        /// Display name of the harness for messages, for example <c>Codex</c>.
        /// </summary>
        protected abstract string HarnessName { get; }

        /// <summary>
        /// Arguments that print the CLI version. Default: <c>--version</c>.
        /// </summary>
        protected virtual IReadOnlyList<string> VersionArguments
        {
            get { return new[] { "--version" }; }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a harness client.
        /// </summary>
        /// <param name="executable">Executable name or path.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentException">Thrown when the executable is empty.</exception>
        /// <exception cref="ArgumentNullException">Thrown when logging is null.</exception>
        protected CliAgentClientBase(string executable, LoggingModule logging) : base(logging)
        {
            if (string.IsNullOrWhiteSpace(executable)) throw new ArgumentException("Executable is required.", nameof(executable));
            _Executable = executable;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run one agent turn to completion. Failures to run (not installed, not authenticated, rate limited, timed out,
        /// crashed, provider error) are reported on the result with <see cref="CliAgentResult.FailureKind"/>; only caller
        /// cancellation throws.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token. Cancelling kills the process tree.</param>
        /// <returns>The run result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the prompt is empty or the history contains null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the request timeout is not positive.</exception>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="token"/> is cancelled.</exception>
        public Task<CliAgentResult> RunAsync(CliAgentRequest request, CancellationToken token = default)
        {
            ValidateRequest(request);
            return InstrumentAsync(PolyPromptTelemetryNames.OperationAgentRun, request.Model ?? Model, null, () => RunCoreAsync(request, token), token);
        }

        /// <summary>
        /// Run one agent turn and stream its events as the harness reports them. The last event is always
        /// <see cref="CliAgentEventKind.Completed"/> or <see cref="CliAgentEventKind.Failed"/>. A harness stream is coarse:
        /// events are whole agent steps or partial text whose boundaries vary between runs and versions; it is not token
        /// streaming. Stopping the enumeration early kills the process tree.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token. Cancelling kills the process tree.</param>
        /// <returns>The events.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the prompt is empty or the history contains null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the request timeout is not positive.</exception>
        /// <exception cref="OperationCanceledException">Thrown during enumeration when <paramref name="token"/> is cancelled.</exception>
        public IAsyncEnumerable<CliAgentEvent> RunStreamingAsync(CliAgentRequest request, CancellationToken token = default)
        {
            ValidateRequest(request);
            return StreamCoreAsync(request, token);
        }

        /// <summary>
        /// Check that the harness is installed, runs, and (where the CLI can report it) is logged in. Never throws for a
        /// missing or broken CLI; rethrows only caller cancellation.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The connectivity result.</returns>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="token"/> is cancelled.</exception>
        public async Task<CliConnectivityResult> ValidateConnectivityAsync(CancellationToken token = default)
        {
            OperationScope? scope = OperationScope.Start(this, PolyPromptTelemetryNames.OperationValidateConnectivity, null, token);
            try
            {
                CliConnectivityResult result = await ValidateConnectivityCoreAsync(token).ConfigureAwait(false);
                if (!result.Success) scope?.ReportError(ToErrorType(result.Status));
                scope?.CompleteBool(result.Success);
                return result;
            }
            catch (Exception ex)
            {
                scope?.Fail(ex);
                throw;
            }
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Build the argument list and standard input for one run. Prompts should travel over standard input wherever
        /// the CLI supports it. <see cref="CliAgentRequest.ExtraArguments"/> are appended by the base afterwards.
        /// </summary>
        /// <param name="state">Run inputs (model, permission mode, prompt, session to resume).</param>
        /// <returns>The invocation.</returns>
        protected abstract CliInvocation BuildInvocation(CliRunState state);

        /// <summary>
        /// Map one line of standard output to events and outcome on <paramref name="state"/>. Throw (for example a
        /// <see cref="JsonException"/>) for a line that cannot be parsed; the base reports it as a notice, counts it in
        /// <see cref="CliAgentResult.MalformedLines"/>, and continues.
        /// </summary>
        /// <param name="line">Output line without its terminator.</param>
        /// <param name="state">Run state.</param>
        protected abstract void ParseLine(string line, CliRunState state);

        /// <summary>
        /// Classify a failed run (one that did not time out or exceed the output limit). The default uses the kind the
        /// parser set when it is specific, then recognizes authentication and rate-limit messages in the structured error
        /// (or, when there is none, in standard error), then reports a structured error as
        /// <see cref="CliFailureKind.ProviderError"/> and anything else as <see cref="CliFailureKind.Crashed"/>.
        /// </summary>
        /// <param name="state">Run state.</param>
        /// <param name="process">Process outcome.</param>
        /// <returns>The failure kind.</returns>
        protected virtual CliFailureKind ClassifyFailure(CliRunState state, ProcessRunResult process)
        {
            if (state.FailureKind.HasValue && state.FailureKind.Value != CliFailureKind.ProviderError && state.FailureKind.Value != CliFailureKind.None)
                return state.FailureKind.Value;

            CliFailureKind? recognized = ClassifyMessage(state.ErrorMessage ?? process.StandardError);
            if (recognized.HasValue) return recognized.Value;
            if (state.ErrorMessage != null) return CliFailureKind.ProviderError;
            return CliFailureKind.Crashed;
        }

        /// <summary>
        /// Recognize authentication and rate-limit failures in a message.
        /// </summary>
        /// <param name="message">Message, or null.</param>
        /// <returns><see cref="CliFailureKind.NotAuthenticated"/>, <see cref="CliFailureKind.RateLimited"/>, or null.</returns>
        protected static CliFailureKind? ClassifyMessage(string? message)
        {
            if (string.IsNullOrWhiteSpace(message)) return null;
            if (_AuthenticationPattern.IsMatch(message)) return CliFailureKind.NotAuthenticated;
            if (_RateLimitPattern.IsMatch(message)) return CliFailureKind.RateLimited;
            return null;
        }

        /// <summary>
        /// Check the login. The default reports that the CLI offers no way to check (null); clients whose CLI has a
        /// status command override this.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The check, or null when the CLI cannot report its login.</returns>
        protected virtual Task<CliAuthenticationCheck?> CheckAuthenticationAsync(CancellationToken token)
        {
            return Task.FromResult<CliAuthenticationCheck?>(null);
        }

        /// <summary>
        /// Run the connectivity check: resolve the executable, run the version command, then
        /// <see cref="CheckAuthenticationAsync"/>.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The connectivity result.</returns>
        protected virtual async Task<CliConnectivityResult> ValidateConnectivityCoreAsync(CancellationToken token)
        {
            CliConnectivityResult result = new CliConnectivityResult();
            int timeoutMs = Math.Min(TimeoutMs, _MaxConnectivityTimeoutMs);

            CliCommandResult version = await RunCommandAsync(VersionArguments, timeoutMs, token).ConfigureAwait(false);
            result.ExecutablePath = version.ExecutablePath;
            if (!version.Started)
            {
                result.Status = CliConnectivityStatus.NotInstalled;
                result.Message = version.Error;
                return result;
            }

            if (!version.Succeeded)
            {
                result.Status = CliConnectivityStatus.Error;
                result.Message = version.TimedOut
                    ? HarnessName + " did not report its version within " + timeoutMs + " ms."
                    : HarnessName + " version check exited with code " + version.ExitCode + ": " + FirstLine(version.StandardError, version.StandardOutput);
                return result;
            }

            result.Version = FirstLine(version.StandardOutput, version.StandardError);

            CliAuthenticationCheck? auth = await CheckAuthenticationAsync(token).ConfigureAwait(false);
            if (auth == null)
            {
                result.Status = CliConnectivityStatus.Ok;
                result.AuthenticationVerified = false;
                result.Message = HarnessName + " is installed; its login cannot be checked without running a turn.";
                return result;
            }

            result.AuthenticationVerified = auth.Authenticated;
            result.Status = auth.Authenticated ? CliConnectivityStatus.Ok : CliConnectivityStatus.NotAuthenticated;
            result.Message = auth.Message;
            return result;
        }

        /// <summary>
        /// Run a short auxiliary command (version, login status, model list) and capture its output. The process is
        /// recorded in <see cref="ClientBase.CallDetails"/> and reported to telemetry like a run.
        /// </summary>
        /// <param name="arguments">Arguments.</param>
        /// <param name="timeoutMs">Timeout in milliseconds.</param>
        /// <param name="token">Cancellation token. Cancelling kills the process tree.</param>
        /// <param name="standardInput">Standard input, or null.</param>
        /// <returns>The command result.</returns>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="token"/> is cancelled.</exception>
        protected async Task<CliCommandResult> RunCommandAsync(IEnumerable<string> arguments, int timeoutMs, CancellationToken token, string? standardInput = null)
        {
            token.ThrowIfCancellationRequested();

            Dictionary<string, string?> environment = MergeEnvironment(null, null);
            List<string> args = new List<string>(arguments);
            CliCommandResult result = new CliCommandResult();
            ProcessCallDetail detail = NewDetail(args, WorkingDirectory, standardInput);
            Stopwatch sw = Stopwatch.StartNew();

            string? path = ResolveExecutablePath(environment);
            result.ExecutablePath = path;
            if (path == null)
            {
                result.Error = NotInstalledMessage();
                FinishDetail(detail, null, "", "", CliFailureKind.NotInstalled, result.Error, sw, false, false, false);
                RecordCallDetail(detail);
                return result;
            }

            detail.Url = path;
            ProcessCallScope? scope = ProcessCallScope.Start(TelemetryProvider, ExecutableLabel, OperationScope.Current);
            ProcessRunner runner;
            try
            {
                runner = ProcessRunner.Start(new ProcessRunRequest
                {
                    FileName = path,
                    Arguments = args,
                    WorkingDirectory = WorkingDirectory,
                    Environment = environment,
                    StandardInput = standardInput,
                    TimeoutMs = timeoutMs,
                    MaxOutputCharacters = _MaxCommandOutputCharacters,
                });
            }
            catch (FileNotFoundException ex)
            {
                result.Error = NotInstalledMessage() + " " + ex.Message;
                FinishDetail(detail, null, "", "", CliFailureKind.NotInstalled, result.Error, sw, false, false, false);
                RecordCallDetail(detail);
                scope?.Complete(null, ToErrorType(CliFailureKind.NotInstalled), false);
                return result;
            }

            using (runner)
            {
                result.Started = true;
                detail.ProcessId = runner.ProcessId;
                scope?.SetProcessId(runner.ProcessId);

                StringBuilder output = new StringBuilder();
                ProcessRunResult process;
                try
                {
                    string? line;
                    while ((line = await runner.ReadLineAsync(token).ConfigureAwait(false)) != null) output.Append(line).Append('\n');
                    process = await runner.WaitForExitAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    FinishDetail(detail, null, output.ToString(), "", CliFailureKind.None, "Cancelled by the caller.", sw, false, true, false);
                    RecordCallDetail(detail);
                    scope?.Complete(null, null, true);
                    throw;
                }

                result.ExitCode = process.ExitCode;
                result.StandardOutput = output.ToString();
                result.StandardError = process.StandardError;
                result.TimedOut = process.TimedOut;
                result.OutputTruncated = process.OutputTruncated;

                CliFailureKind kind = CliFailureKind.None;
                if (process.TimedOut) kind = CliFailureKind.TimedOut;
                else if (process.OutputTruncated) kind = CliFailureKind.OutputLimitExceeded;
                else if (process.ExitCode != 0) kind = ClassifyMessage(result.StandardError + "\n" + result.StandardOutput) ?? CliFailureKind.Crashed;

                if (kind != CliFailureKind.None) result.Error = DescribeCommandFailure(result, kind);
                FinishDetail(detail, process.ExitCode, result.StandardOutput, process.StandardError, kind, result.Error, sw, process.TimedOut, false, process.OutputTruncated);
                RecordCallDetail(detail);
                scope?.Complete(process.ExitCode, kind == CliFailureKind.None ? null : ToErrorType(kind), false);
                return result;
            }
        }

        /// <summary>
        /// Run a model-listing command and parse its output, instrumented as <c>model_list</c>.
        /// </summary>
        /// <param name="arguments">Arguments.</param>
        /// <param name="parse">Parses standard output into models.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The models.</returns>
        /// <exception cref="CliAgentException">Thrown when the command cannot run or fails.</exception>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="token"/> is cancelled.</exception>
        protected async IAsyncEnumerable<ModelInformation> ListModelsFromCommandAsync(
            IReadOnlyList<string> arguments,
            Func<string, List<ModelInformation>> parse,
            [EnumeratorCancellation] CancellationToken token)
        {
            List<ModelInformation> models = await InstrumentValueAsync(PolyPromptTelemetryNames.OperationModelList, null, async () =>
            {
                CliCommandResult command = await RunCommandAsync(arguments, TimeoutMs, token).ConfigureAwait(false);
                if (!command.Succeeded)
                {
                    CliFailureKind kind = !command.Started ? CliFailureKind.NotInstalled
                        : command.TimedOut ? CliFailureKind.TimedOut
                        : command.OutputTruncated ? CliFailureKind.OutputLimitExceeded
                        : ClassifyMessage(command.StandardError + "\n" + command.StandardOutput) ?? CliFailureKind.Crashed;
                    throw new CliAgentException(kind, command.Error ?? DescribeCommandFailure(command, kind), command.ExitCode, command.StandardError);
                }

                try
                {
                    return parse(command.StandardOutput);
                }
                catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException || ex is FormatException || ex is KeyNotFoundException)
                {
                    throw new CliAgentException(CliFailureKind.InvalidOutput, HarnessName + " model list could not be parsed: " + ex.Message, command.ExitCode, command.StandardError);
                }
            }, token).ConfigureAwait(false);

            foreach (ModelInformation model in models)
            {
                yield return model;
            }
        }

        /// <summary>
        /// Arguments for <see cref="CliAgentRequest.ExtraArguments"/>, in order.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <returns>The arguments.</returns>
        protected static List<string> ExpandExtraArguments(CliAgentRequest request)
        {
            List<string> args = new List<string>();
            if (request.ExtraArguments == null) return args;

            foreach (KeyValuePair<string, string?> entry in request.ExtraArguments)
            {
                if (string.IsNullOrWhiteSpace(entry.Key)) continue;
                args.Add(entry.Key);
                if (!string.IsNullOrEmpty(entry.Value)) args.Add(entry.Value);
            }
            return args;
        }

        /// <summary>
        /// Parse a JSON line into an object element, or throw <see cref="JsonException"/>.
        /// </summary>
        /// <param name="line">Line.</param>
        /// <returns>A cloned root element.</returns>
        /// <exception cref="JsonException">Thrown when the line is not a JSON object.</exception>
        protected static JsonElement ParseJsonObject(string line)
        {
            using JsonDocument document = JsonDocument.Parse(line);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Expected a JSON object.");
            return document.RootElement.Clone();
        }

        /// <summary>
        /// Read a string property, or null when it is missing or not a string.
        /// </summary>
        /// <param name="element">Object element.</param>
        /// <param name="name">Property name.</param>
        /// <returns>The value, or null.</returns>
        protected static string? GetString(JsonElement element, string name)
        {
            if (element.ValueKind != JsonValueKind.Object) return null;
            if (!element.TryGetProperty(name, out JsonElement value)) return null;
            return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }

        /// <summary>
        /// Read an integer property, or null when it is missing or not a number.
        /// </summary>
        /// <param name="element">Object element.</param>
        /// <param name="name">Property name.</param>
        /// <returns>The value, or null.</returns>
        protected static int? GetInt(JsonElement element, string name)
        {
            if (element.ValueKind != JsonValueKind.Object) return null;
            if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Number) return null;
            if (value.TryGetInt32(out int i)) return i;
            if (value.TryGetInt64(out long l)) return l > int.MaxValue ? int.MaxValue : (int)l;
            return null;
        }

        /// <summary>
        /// Read a decimal property, or null when it is missing or not a number.
        /// </summary>
        /// <param name="element">Object element.</param>
        /// <param name="name">Property name.</param>
        /// <returns>The value, or null.</returns>
        protected static decimal? GetDecimal(JsonElement element, string name)
        {
            if (element.ValueKind != JsonValueKind.Object) return null;
            if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Number) return null;
            return value.TryGetDecimal(out decimal d) ? d : null;
        }

        /// <summary>
        /// Read a boolean property, or null when it is missing or not a boolean.
        /// </summary>
        /// <param name="element">Object element.</param>
        /// <param name="name">Property name.</param>
        /// <returns>The value, or null.</returns>
        protected static bool? GetBool(JsonElement element, string name)
        {
            if (element.ValueKind != JsonValueKind.Object) return null;
            if (!element.TryGetProperty(name, out JsonElement value)) return null;
            if (value.ValueKind == JsonValueKind.True) return true;
            if (value.ValueKind == JsonValueKind.False) return false;
            return null;
        }

        /// <summary>
        /// Read an object property, or null when it is missing or not an object.
        /// </summary>
        /// <param name="element">Object element.</param>
        /// <param name="name">Property name.</param>
        /// <returns>The value, or null.</returns>
        protected static JsonElement? GetObject(JsonElement element, string name)
        {
            if (element.ValueKind != JsonValueKind.Object) return null;
            if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Object) return null;
            return value;
        }

        /// <summary>
        /// Render any JSON value as text: strings as is, everything else as compact JSON. Null for missing values.
        /// </summary>
        /// <param name="element">Element, or null.</param>
        /// <returns>The text, or null.</returns>
        protected static string? RenderJson(JsonElement? element)
        {
            if (!element.HasValue) return null;
            JsonElement value = element.Value;
            if (value.ValueKind == JsonValueKind.Undefined || value.ValueKind == JsonValueKind.Null) return null;
            if (value.ValueKind == JsonValueKind.String) return value.GetString();
            return value.GetRawText();
        }

        /// <summary>
        /// Read a property of any kind and render it with <see cref="RenderJson"/>.
        /// </summary>
        /// <param name="element">Object element.</param>
        /// <param name="name">Property name.</param>
        /// <returns>The text, or null.</returns>
        protected static string? GetRendered(JsonElement element, string name)
        {
            if (element.ValueKind != JsonValueKind.Object) return null;
            if (!element.TryGetProperty(name, out JsonElement value)) return null;
            return RenderJson(value);
        }

        #endregion

        #region Private-Methods

        private static void ValidateRequest(CliAgentRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.Prompt)) throw new ArgumentException("Prompt is required.", nameof(request));
            if (request.TimeoutMs.HasValue && request.TimeoutMs.Value <= 0)
                throw new ArgumentOutOfRangeException(nameof(request), "TimeoutMs must be greater than zero.");
            if (request.History != null && request.History.Any(m => m == null))
                throw new ArgumentException("History cannot contain null messages.", nameof(request));
        }

        private async Task<CliAgentResult> RunCoreAsync(CliAgentRequest request, CancellationToken token)
        {
            CliAgentResult result = new CliAgentResult();
            await foreach (CliAgentEvent evt in ExecuteAsync(request, result, OperationScope.Current, token).ConfigureAwait(false))
            {
            }
            return result;
        }

        private async IAsyncEnumerable<CliAgentEvent> StreamCoreAsync(CliAgentRequest request, [EnumeratorCancellation] CancellationToken token)
        {
            OperationScope? scope = OperationScope.Start(this, PolyPromptTelemetryNames.OperationAgentRunStream, request.Model ?? Model, token);
            CliAgentResult result = new CliAgentResult();

            IAsyncEnumerable<CliAgentEvent> source = ExecuteAsync(request, result, scope, token);
            if (scope != null) source = scope.WrapStream(source, result, e => e.Kind == CliAgentEventKind.TextDelta, token);

            await foreach (CliAgentEvent evt in source.ConfigureAwait(false))
            {
                yield return evt;
            }
        }

        private async IAsyncEnumerable<CliAgentEvent> ExecuteAsync(
            CliAgentRequest request,
            CliAgentResult result,
            OperationScope? operation,
            [EnumeratorCancellation] CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            Stopwatch sw = Stopwatch.StartNew();
            CliRunState state = CreateState(request);
            int timeoutMs = request.TimeoutMs ?? TimeoutMs;
            Dictionary<string, string?> environment = MergeEnvironment(request.Environment, null);
            result.Model = state.Model;

            string? path = ResolveExecutablePath(environment);
            if (path == null)
            {
                ProcessCallDetail missing = NewDetail(new List<string>(), state.WorkingDirectory, null);
                CliAgentEvent failed = Finish(result, state, null, null, CliFailureKind.NotInstalled, NotInstalledMessage(), "", sw, missing);
                operation?.ReportError(ToErrorType(CliFailureKind.NotInstalled));
                yield return failed;
                yield break;
            }

            CliInvocation invocation = BuildInvocation(state);
            List<string> args = new List<string>(invocation.Arguments ?? new List<string>());
            args.AddRange(ExpandExtraArguments(request));
            if (invocation.Environment != null) environment = MergeEnvironment(request.Environment, invocation.Environment);

            ProcessCallDetail detail = NewDetail(args, state.WorkingDirectory, invocation.StandardInput);
            detail.Url = path;

            ProcessCallScope? scope = ProcessCallScope.Start(TelemetryProvider, ExecutableLabel, operation);
            ProcessRunner? runner = null;
            string? startError = null;
            try
            {
                runner = ProcessRunner.Start(new ProcessRunRequest
                {
                    FileName = path,
                    Arguments = args,
                    WorkingDirectory = state.WorkingDirectory,
                    Environment = environment,
                    StandardInput = invocation.StandardInput,
                    KeepStandardInputOpen = invocation.KeepStandardInputOpen,
                    TimeoutMs = timeoutMs,
                    MaxOutputCharacters = request.MaxOutputCharacters,
                });
            }
            catch (FileNotFoundException ex)
            {
                startError = NotInstalledMessage() + " " + ex.Message;
            }

            if (runner == null)
            {
                CliAgentEvent failed = Finish(result, state, null, null, CliFailureKind.NotInstalled, startError ?? NotInstalledMessage(), "", sw, detail);
                scope?.Complete(null, ToErrorType(CliFailureKind.NotInstalled), false);
                yield return failed;
                yield break;
            }

            _Logging.Debug(_Header + "started " + ExecutableLabel + " (pid " + runner.ProcessId + ") with " + args.Count + " arguments");
            detail.ProcessId = runner.ProcessId;
            scope?.SetProcessId(runner.ProcessId);

            StringBuilder raw = new StringBuilder();
            CancellationTokenSource? completionKill = null;
            bool killedAfterCompletion = false;
            bool finished = false;
            CliAgentEvent? terminal = null;

            try
            {
                while (true)
                {
                    string? line;
                    try
                    {
                        line = await runner.ReadLineAsync(token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        FinishCancelled(detail, raw, sw, scope);
                        finished = true;
                        throw;
                    }

                    if (line == null) break;
                    raw.Append(line).Append('\n');
                    ParseSafely(line, state, result);

                    foreach (CliAgentEvent evt in state.DrainPending())
                    {
                        result.Events.Add(evt);
                        yield return evt;
                    }

                    if (invocation.KeepStandardInputOpen && completionKill == null && (state.Completed || state.Failed))
                    {
                        ProcessRunner running = runner;
                        running.CloseStandardInput();
                        completionKill = new CancellationTokenSource();
                        completionKill.Token.Register(() =>
                        {
                            if (running.HasExited) return;
                            killedAfterCompletion = true;
                            running.Kill();
                        });
                        completionKill.CancelAfter(_CompletionExitGraceMs);
                    }
                }

                ProcessRunResult process;
                try
                {
                    process = await runner.WaitForExitAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    FinishCancelled(detail, raw, sw, scope);
                    finished = true;
                    throw;
                }

                CliFailureKind kind = CliFailureKind.None;
                string? error = null;
                if (process.TimedOut)
                {
                    kind = CliFailureKind.TimedOut;
                    error = HarnessName + " did not finish within " + timeoutMs + " ms; the process tree was killed.";
                }
                else if (process.OutputTruncated)
                {
                    kind = CliFailureKind.OutputLimitExceeded;
                    error = HarnessName + " wrote more than " + request.MaxOutputCharacters + " characters to standard output; the process tree was killed.";
                }
                else if (!state.Failed && (process.ExitCode == 0 || (killedAfterCompletion && state.Completed)))
                {
                    if (!state.Completed && result.MalformedLines > 0 && state.AccumulatedText.Length == 0 && state.FinalText == null)
                    {
                        kind = CliFailureKind.InvalidOutput;
                        error = HarnessName + " exited without a result PolyPrompt could parse.";
                    }
                }
                else
                {
                    kind = ClassifyFailure(state, process);
                    error = state.ErrorMessage ?? DescribeExit(process);
                }

                result.TimedOut = process.TimedOut;
                result.OutputTruncated = process.OutputTruncated;
                detail.TimedOut = process.TimedOut;
                detail.OutputTruncated = process.OutputTruncated;

                terminal = Finish(result, state, process.ExitCode, process, kind, error, raw.ToString(), sw, detail);
                scope?.Complete(process.ExitCode, kind == CliFailureKind.None ? null : ToErrorType(kind), false);
                if (process.TimedOut) operation?.CompleteWith(PolyPromptTelemetryNames.OutcomeTimeout, PolyPromptTelemetryNames.ErrorTimeout);
                finished = true;
            }
            finally
            {
                completionKill?.Dispose();
                if (!finished)
                {
                    // The caller stopped enumerating before the run ended.
                    runner.Kill();
                    FinishCancelled(detail, raw, sw, scope);
                }
                runner.Dispose();
            }

            yield return terminal;
        }

        private void ParseSafely(string line, CliRunState state, CliAgentResult result)
        {
            if (string.IsNullOrWhiteSpace(line)) return;

            state.CurrentLine = line;
            state.CurrentEventType = null;
            try
            {
                ParseLine(line, state);
            }
            catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException || ex is FormatException || ex is KeyNotFoundException || ex is InvalidDataException)
            {
                result.MalformedLines++;
                state.Notice("Unparseable output line: " + Truncate(line, 512), true);
                _Logging.Debug(_Header + "unparseable " + ExecutableLabel + " output line: " + ex.Message);
            }
            finally
            {
                state.CurrentLine = null;
                state.CurrentEventType = null;
            }
        }

        private CliAgentEvent Finish(
            CliAgentResult result,
            CliRunState state,
            int? exitCode,
            ProcessRunResult? process,
            CliFailureKind kind,
            string? error,
            string rawOutput,
            Stopwatch sw,
            ProcessCallDetail detail)
        {
            bool success = kind == CliFailureKind.None;

            result.Success = success;
            result.Error = success ? null : error;
            result.FailureKind = kind;
            result.ExitCode = exitCode;
            result.StandardError = process?.StandardError ?? "";
            result.Text = state.FinalText ?? state.AccumulatedText;
            result.Reasoning = state.AccumulatedReasoning;
            result.SessionId = state.SessionId ?? state.ResumeSessionId;
            result.Usage = state.Usage;
            result.CostUsd = state.CostUsd;
            result.Model = state.ReportedModel ?? state.Model;
            result.RawOutput = rawOutput;
            result.OverallRuntimeMs = sw.ElapsedMilliseconds;
            if (!string.IsNullOrEmpty(result.SessionId))
            {
                Dictionary<string, string> providerState = new Dictionary<string, string> { { "sessionId", result.SessionId } };
                result.ProviderState = JsonSerializer.SerializeToElement(providerState, PolyPromptJsonContext.Default.DictionaryStringString);
            }

            CliAgentEvent terminal = new CliAgentEvent(success ? CliAgentEventKind.Completed : CliAgentEventKind.Failed, success ? result.Text : error)
            {
                SessionId = result.SessionId,
                Usage = result.Usage,
                CostUsd = result.CostUsd,
                ExitCode = exitCode,
                FailureKind = success ? null : kind,
                IsError = !success,
            };
            result.Events.Add(terminal);

            FinishDetail(detail, exitCode, rawOutput, result.StandardError, kind, result.Error, sw, result.TimedOut, false, result.OutputTruncated);
            RecordCallDetail(detail);

            if (success)
                _Logging.Debug(_Header + ExecutableLabel + " run completed in " + sw.ElapsedMilliseconds + " ms");
            else
                _Logging.Warn(_Header + ExecutableLabel + " run failed (" + kind + "): " + Truncate(error ?? "", 512));

            return terminal;
        }

        private void FinishCancelled(ProcessCallDetail detail, StringBuilder raw, Stopwatch sw, ProcessCallScope? scope)
        {
            FinishDetail(detail, null, raw.ToString(), "", CliFailureKind.None, "Cancelled by the caller.", sw, false, true, false);
            RecordCallDetail(detail);
            scope?.Complete(null, null, true);
        }

        private ProcessCallDetail NewDetail(List<string> args, string? workingDirectory, string? standardInput)
        {
            return new ProcessCallDetail
            {
                Method = "EXEC",
                Url = Executable,
                Executable = Executable,
                Arguments = new List<string>(args),
                RequestBody = JsonSerializer.Serialize(args, PolyPromptJsonContext.Default.ListString),
                WorkingDirectory = workingDirectory,
                StandardInput = standardInput,
                TimestampUtc = DateTime.UtcNow,
            };
        }

        private static void FinishDetail(
            ProcessCallDetail detail,
            int? exitCode,
            string standardOutput,
            string standardError,
            CliFailureKind kind,
            string? error,
            Stopwatch sw,
            bool timedOut,
            bool cancelled,
            bool truncated)
        {
            detail.ExitCode = exitCode;
            detail.StatusCode = exitCode;
            detail.ResponseBody = standardOutput;
            detail.StandardError = standardError;
            detail.FailureKind = kind;
            detail.Success = kind == CliFailureKind.None && !cancelled && exitCode.HasValue;
            detail.Error = detail.Success ? null : error;
            detail.ResponseTimeMs = sw.ElapsedMilliseconds;
            detail.TimedOut = timedOut;
            detail.Cancelled = cancelled;
            detail.OutputTruncated = truncated;
        }

        private CliRunState CreateState(CliAgentRequest request)
        {
            string? resume = null;
            if (SupportsSessionResume)
            {
                resume = !string.IsNullOrWhiteSpace(request.SessionId) ? request.SessionId : SessionIdFromHistory(request.History);
            }

            bool replay = request.History != null && request.History.Count > 0 && resume == null;
            StringBuilder prompt = new StringBuilder();
            if (replay)
            {
                prompt.Append("Conversation so far:\n");
                foreach (ChatMessage message in request.History!)
                {
                    if (string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.IsNullOrEmpty(message.Content)) continue;
                    prompt.Append('\n').Append(RoleLabel(message.Role)).Append(": ").Append(message.Content).Append('\n');
                }
                prompt.Append("\nCurrent request:\n");
            }
            prompt.Append(request.Prompt);

            string withoutSystem = prompt.ToString();
            string withSystem = withoutSystem;

            string? system = request.SystemPrompt;
            if (request.History != null)
            {
                foreach (ChatMessage message in request.History)
                {
                    if (string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(message.Content))
                        system = string.IsNullOrWhiteSpace(system) ? message.Content : system + "\n\n" + message.Content;
                }
            }

            if (!string.IsNullOrWhiteSpace(system))
                withSystem = "System instructions:\n" + system + "\n\n" + (replay ? "" : "Request:\n") + withoutSystem;

            return new CliRunState(
                request,
                string.IsNullOrWhiteSpace(request.Model) ? Model : request.Model,
                request.PermissionMode ?? PermissionMode,
                string.IsNullOrWhiteSpace(request.WorkingDirectory) ? WorkingDirectory : request.WorkingDirectory,
                resume,
                withoutSystem,
                withSystem);
        }

        private static string? SessionIdFromHistory(List<ChatMessage>? history)
        {
            if (history == null) return null;

            for (int i = history.Count - 1; i >= 0; i--)
            {
                JsonElement? providerState = history[i]?.ProviderState;
                if (!providerState.HasValue || providerState.Value.ValueKind != JsonValueKind.Object) continue;
                string? sessionId = GetString(providerState.Value, "sessionId");
                if (!string.IsNullOrWhiteSpace(sessionId)) return sessionId;
            }
            return null;
        }

        private static string RoleLabel(string? role)
        {
            if (string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase)) return "Assistant";
            if (string.Equals(role, "tool", StringComparison.OrdinalIgnoreCase)) return "Tool";
            return "User";
        }

        private Dictionary<string, string?> MergeEnvironment(Dictionary<string, string?>? request, Dictionary<string, string?>? invocation)
        {
            Dictionary<string, string?> merged = new Dictionary<string, string?>(StringComparer.Ordinal);
            if (Environment != null) foreach (KeyValuePair<string, string?> entry in Environment) merged[entry.Key] = entry.Value;
            if (request != null) foreach (KeyValuePair<string, string?> entry in request) merged[entry.Key] = entry.Value;
            if (invocation != null) foreach (KeyValuePair<string, string?> entry in invocation) merged[entry.Key] = entry.Value;
            return merged;
        }

        private string? ResolveExecutablePath(Dictionary<string, string?> environment)
        {
            string? searchPath = null;
            if (environment.TryGetValue("PATH", out string? path) && path != null) searchPath = path;
            return ProcessRunner.ResolveExecutable(Executable, searchPath);
        }

        private string ExecutableLabel
        {
            get
            {
                string name = Path.GetFileName(Executable.TrimEnd('/', '\\'));
                if (OperatingSystem.IsWindows()) name = Path.GetFileNameWithoutExtension(name);
                return string.IsNullOrEmpty(name) ? Executable : name;
            }
        }

        private string NotInstalledMessage()
        {
            return HarnessName + " CLI '" + Executable + "' was not found. Install it and make sure it is on PATH, or set Executable to its full path.";
        }

        private string DescribeExit(ProcessRunResult process)
        {
            string message = HarnessName + " exited with code " + (process.ExitCode?.ToString() ?? "unknown");
            string detail = FirstLine(process.StandardError, null) ?? "";
            return detail.Length > 0 ? message + ": " + Truncate(detail, 512) : message + ".";
        }

        private string DescribeCommandFailure(CliCommandResult command, CliFailureKind kind)
        {
            if (kind == CliFailureKind.TimedOut) return HarnessName + " command timed out.";
            if (kind == CliFailureKind.OutputLimitExceeded) return HarnessName + " command produced too much output.";
            string detail = FirstLine(command.StandardError, command.StandardOutput) ?? "";
            return HarnessName + " command exited with code " + command.ExitCode + (detail.Length > 0 ? ": " + Truncate(detail, 512) : ".");
        }

        private static string? FirstLine(string? primary, string? secondary)
        {
            foreach (string? text in new[] { primary, secondary })
            {
                if (string.IsNullOrWhiteSpace(text)) continue;
                foreach (string line in text.Split('\n'))
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length > 0) return trimmed;
                }
            }
            return null;
        }

        private static string Truncate(string text, int max)
        {
            return text.Length <= max ? text : text.Substring(0, max) + "...";
        }

        private static string ToErrorType(CliFailureKind kind)
        {
            switch (kind)
            {
                case CliFailureKind.NotInstalled: return "not_installed";
                case CliFailureKind.NotAuthenticated: return "not_authenticated";
                case CliFailureKind.RateLimited: return "rate_limited";
                case CliFailureKind.TimedOut: return PolyPromptTelemetryNames.ErrorTimeout;
                case CliFailureKind.Crashed: return "crashed";
                case CliFailureKind.ProviderError: return "provider_error";
                case CliFailureKind.InvalidInput: return "invalid_input";
                case CliFailureKind.TurnLimitReached: return "turn_limit_reached";
                case CliFailureKind.InvalidOutput: return PolyPromptTelemetryNames.ErrorInvalidResponse;
                case CliFailureKind.OutputLimitExceeded: return "output_limit_exceeded";
                default: return PolyPromptTelemetryNames.ErrorOperationFailed;
            }
        }

        private static string ToErrorType(CliConnectivityStatus status)
        {
            switch (status)
            {
                case CliConnectivityStatus.NotInstalled: return "not_installed";
                case CliConnectivityStatus.NotAuthenticated: return "not_authenticated";
                default: return PolyPromptTelemetryNames.ErrorOperationFailed;
            }
        }

        #endregion

        #region Protected-Classes

        /// <summary>
        /// Outcome of an auxiliary command run by <see cref="RunCommandAsync"/>.
        /// </summary>
        protected sealed class CliCommandResult
        {
            /// <summary>
            /// Resolved executable path, or null when it was not found.
            /// </summary>
            public string? ExecutablePath { get; set; }

            /// <summary>
            /// True when the process started.
            /// </summary>
            public bool Started { get; set; }

            /// <summary>
            /// Exit code, or null.
            /// </summary>
            public int? ExitCode { get; set; }

            /// <summary>
            /// Standard output.
            /// </summary>
            public string StandardOutput { get; set; } = "";

            /// <summary>
            /// Standard error (bounded).
            /// </summary>
            public string StandardError { get; set; } = "";

            /// <summary>
            /// True when the command timed out.
            /// </summary>
            public bool TimedOut { get; set; }

            /// <summary>
            /// True when the output limit was exceeded.
            /// </summary>
            public bool OutputTruncated { get; set; }

            /// <summary>
            /// Failure message, or null.
            /// </summary>
            public string? Error { get; set; }

            /// <summary>
            /// True when the process started, did not time out or overflow, and exited with code zero.
            /// </summary>
            public bool Succeeded
            {
                get { return Started && !TimedOut && !OutputTruncated && ExitCode == 0; }
            }
        }

        /// <summary>
        /// Outcome of a login check.
        /// </summary>
        protected sealed class CliAuthenticationCheck
        {
            /// <summary>
            /// True when the CLI confirmed it is logged in.
            /// </summary>
            public bool Authenticated { get; set; }

            /// <summary>
            /// Status message reported by the CLI.
            /// </summary>
            public string? Message { get; set; }
        }

        #endregion
    }
}
