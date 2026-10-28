namespace PolyPrompt.Clients
{
    using System.Text;
    using PolyPrompt.Models;

    /// <summary>
    /// Inputs and accumulated outcome of one agent harness run. <see cref="CliAgentClientBase"/> creates it, passes it to
    /// the client's <see cref="CliAgentClientBase.BuildInvocation"/> and <see cref="CliAgentClientBase.ParseLine"/>, and
    /// turns it into the <see cref="CliAgentResult"/> and the terminal event once the process exits. Parsers report what
    /// they observe through the helper methods; they never emit <see cref="CliAgentEventKind.Completed"/> or
    /// <see cref="CliAgentEventKind.Failed"/> themselves.
    /// </summary>
    public sealed class CliRunState
    {
        #region Private-Members

        private readonly List<CliAgentEvent> _Pending = new List<CliAgentEvent>();
        private readonly StringBuilder _Text = new StringBuilder();
        private readonly StringBuilder _Reasoning = new StringBuilder();

        #endregion

        #region Public-Members

        /// <summary>
        /// The request being run.
        /// </summary>
        public CliAgentRequest Request { get; }

        /// <summary>
        /// Model to request: the request's model, else the client's, else null for the harness default.
        /// </summary>
        public string? Model { get; }

        /// <summary>
        /// Permission mode in effect: the request's, else the client's.
        /// </summary>
        public CliPermissionMode PermissionMode { get; }

        /// <summary>
        /// Working directory in effect, or null for the current directory.
        /// </summary>
        public string? WorkingDirectory { get; }

        /// <summary>
        /// Session to resume, or null to start a new session. Taken from <see cref="CliAgentRequest.SessionId"/>, else from
        /// the <c>sessionId</c> in the provider state of the latest history message that has one; always null when the
        /// client cannot resume sessions.
        /// </summary>
        public string? ResumeSessionId { get; }

        /// <summary>
        /// The prompt to send without system instructions: the request prompt, preceded by the replayed history when the
        /// history is not carried by a resumed session.
        /// </summary>
        public string Prompt { get; }

        /// <summary>
        /// <see cref="Prompt"/> preceded by the system instructions, for CLIs without a system-prompt option.
        /// </summary>
        public string PromptWithSystem { get; }

        /// <summary>
        /// Session id reported by the harness.
        /// </summary>
        public string? SessionId { get; set; }

        /// <summary>
        /// Model reported by the harness, if any.
        /// </summary>
        public string? ReportedModel { get; set; }

        /// <summary>
        /// Final response text when the harness reports one explicitly; when null the concatenated text events are used.
        /// </summary>
        public string? FinalText { get; set; }

        /// <summary>
        /// Token usage exactly as reported, or null.
        /// </summary>
        public TokenUsage? Usage { get; set; }

        /// <summary>
        /// Cost in US dollars exactly as reported, or null.
        /// </summary>
        public decimal? CostUsd { get; set; }

        /// <summary>
        /// Structured error message reported by the harness, or null.
        /// </summary>
        public string? ErrorMessage { get; private set; }

        /// <summary>
        /// Failure kind the parser determined, or null to let the client classify the failure.
        /// </summary>
        public CliFailureKind? FailureKind { get; private set; }

        /// <summary>
        /// True once the harness reported that the run finished successfully.
        /// </summary>
        public bool Completed { get; private set; }

        /// <summary>
        /// True once the harness reported a failure.
        /// </summary>
        public bool Failed
        {
            get { return ErrorMessage != null; }
        }

        /// <summary>
        /// Text accumulated from text events.
        /// </summary>
        public string AccumulatedText
        {
            get { return _Text.ToString(); }
        }

        /// <summary>
        /// Reasoning accumulated from reasoning events, or null when there was none.
        /// </summary>
        public string? AccumulatedReasoning
        {
            get { return _Reasoning.Length == 0 ? null : _Reasoning.ToString(); }
        }

        /// <summary>
        /// Per-run scratch storage for the client's parser (for example tool calls already reported). Clients are shared
        /// across concurrent runs, so per-run parser state belongs here rather than on the client.
        /// </summary>
        public Dictionary<string, object?> Properties { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);

        /// <summary>
        /// Raw line being parsed; attached to events emitted while parsing it.
        /// </summary>
        internal string? CurrentLine { get; set; }

        /// <summary>
        /// Provider event type being parsed; attached to events emitted while parsing it.
        /// </summary>
        public string? CurrentEventType { get; set; }

        #endregion

        #region Constructors-and-Factories

        internal CliRunState(
            CliAgentRequest request,
            string? model,
            CliPermissionMode permissionMode,
            string? workingDirectory,
            string? resumeSessionId,
            string prompt,
            string promptWithSystem)
        {
            Request = request;
            Model = model;
            PermissionMode = permissionMode;
            WorkingDirectory = workingDirectory;
            ResumeSessionId = resumeSessionId;
            Prompt = prompt;
            PromptWithSystem = promptWithSystem;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Emit an event. <see cref="CliAgentEventKind.Completed"/> and <see cref="CliAgentEventKind.Failed"/> are not
        /// allowed; use <see cref="Complete"/> and <see cref="Fail"/>.
        /// </summary>
        /// <param name="evt">Event.</param>
        /// <exception cref="ArgumentException">Thrown for terminal event kinds.</exception>
        public void Emit(CliAgentEvent evt)
        {
            if (evt == null) throw new ArgumentNullException(nameof(evt));
            if (evt.Kind == CliAgentEventKind.Completed || evt.Kind == CliAgentEventKind.Failed)
                throw new ArgumentException("Terminal events are emitted by the client base; call Complete or Fail.", nameof(evt));

            evt.ProviderEventType ??= CurrentEventType;
            evt.Raw ??= CurrentLine;
            _Pending.Add(evt);
        }

        /// <summary>
        /// Record a started session.
        /// </summary>
        /// <param name="sessionId">Session id, or null.</param>
        public void Started(string? sessionId)
        {
            if (!string.IsNullOrEmpty(sessionId)) SessionId = sessionId;
            Emit(new CliAgentEvent(CliAgentEventKind.Started) { SessionId = sessionId });
        }

        /// <summary>
        /// Record assistant text and emit a <see cref="CliAgentEventKind.TextDelta"/>.
        /// </summary>
        /// <param name="text">Text.</param>
        public void Text(string? text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _Text.Append(text);
            Emit(new CliAgentEvent(CliAgentEventKind.TextDelta, text));
        }

        /// <summary>
        /// Record reasoning text and emit a <see cref="CliAgentEventKind.ReasoningDelta"/>.
        /// </summary>
        /// <param name="text">Reasoning text.</param>
        public void Reasoning(string? text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _Reasoning.Append(text);
            Emit(new CliAgentEvent(CliAgentEventKind.ReasoningDelta, text));
        }

        /// <summary>
        /// Emit a <see cref="CliAgentEventKind.ToolUse"/>.
        /// </summary>
        /// <param name="toolName">Tool name.</param>
        /// <param name="toolCallId">Tool call id, or null.</param>
        /// <param name="input">Tool input, or null.</param>
        public void ToolUse(string? toolName, string? toolCallId, string? input)
        {
            Emit(new CliAgentEvent(CliAgentEventKind.ToolUse) { ToolName = toolName, ToolCallId = toolCallId, ToolInput = input });
        }

        /// <summary>
        /// Emit a <see cref="CliAgentEventKind.ToolResult"/>.
        /// </summary>
        /// <param name="toolName">Tool name, or null.</param>
        /// <param name="toolCallId">Tool call id, or null.</param>
        /// <param name="output">Tool output, or null.</param>
        /// <param name="isError">Whether the tool failed.</param>
        public void ToolResult(string? toolName, string? toolCallId, string? output, bool isError)
        {
            Emit(new CliAgentEvent(CliAgentEventKind.ToolResult) { ToolName = toolName, ToolCallId = toolCallId, ToolOutput = output, IsError = isError });
        }

        /// <summary>
        /// Emit a <see cref="CliAgentEventKind.Notice"/>.
        /// </summary>
        /// <param name="text">Notice text.</param>
        /// <param name="isError">Whether the notice reports a (non-fatal) error.</param>
        public void Notice(string? text, bool isError = false)
        {
            Emit(new CliAgentEvent(CliAgentEventKind.Notice, text) { IsError = isError });
        }

        /// <summary>
        /// Record that the harness reported successful completion.
        /// </summary>
        public void Complete()
        {
            Completed = true;
        }

        /// <summary>
        /// Record a structured failure reported by the harness. The first failure wins.
        /// </summary>
        /// <param name="message">Error message.</param>
        /// <param name="kind">Failure kind, or null to let the client classify it from the message.</param>
        public void Fail(string? message, CliFailureKind? kind = null)
        {
            if (ErrorMessage != null) return;
            ErrorMessage = string.IsNullOrWhiteSpace(message) ? "The harness reported an error." : message;
            if (kind.HasValue) FailureKind = kind;
        }

        #endregion

        #region Internal-Methods

        internal List<CliAgentEvent> DrainPending()
        {
            List<CliAgentEvent> drained = new List<CliAgentEvent>(_Pending);
            _Pending.Clear();
            return drained;
        }

        #endregion
    }
}
