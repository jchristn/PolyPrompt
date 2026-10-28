namespace PolyPrompt.Models
{
    /// <summary>
    /// One agent harness turn: a prompt plus how and where the harness should run it. Values left null fall back to the
    /// client's own settings (<c>Model</c>, <c>PermissionMode</c>, <c>WorkingDirectory</c>, <c>TimeoutMs</c>).
    /// </summary>
    public sealed class CliAgentRequest
    {
        #region Private-Members

        private int _MaxOutputCharacters = 1_000_000;

        #endregion

        #region Public-Members

        /// <summary>
        /// The user prompt. Required; must not be empty or whitespace. Delivered over standard input wherever the CLI
        /// supports it, so it is not limited by command-line length.
        /// </summary>
        public string Prompt { get; set; } = "";

        /// <summary>
        /// Optional system instructions. Passed with the harness's own system-prompt flag where one exists, otherwise
        /// prepended to the prompt, because harnesses have no system role.
        /// </summary>
        public string? SystemPrompt { get; set; } = null;

        /// <summary>
        /// Model to use, in the harness's own naming (for example <c>gpt-5-codex</c>, <c>sonnet</c>, or
        /// <c>provider/model</c>). Null uses the client's model, and then the harness default.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Session to resume, as returned in <see cref="CliAgentResult.SessionId"/> by an earlier run. Null starts a new
        /// session.
        /// </summary>
        public string? SessionId { get; set; } = null;

        /// <summary>
        /// Earlier conversation turns. Replayed into the prompt only when no <see cref="SessionId"/> is supplied or the
        /// harness cannot resume sessions; when a session is resumed the harness already holds the history.
        /// </summary>
        public List<ChatMessage>? History { get; set; } = null;

        /// <summary>
        /// Directory the harness runs in and treats as its workspace. Null uses the client's working directory, and then
        /// the current directory of this process.
        /// </summary>
        public string? WorkingDirectory { get; set; } = null;

        /// <summary>
        /// Timeout for the whole run in milliseconds. Null uses the client's <c>TimeoutMs</c>. When it elapses the process
        /// tree is killed and the run fails with <see cref="CliFailureKind.TimedOut"/>.
        /// </summary>
        public int? TimeoutMs { get; set; } = null;

        /// <summary>
        /// What the harness may do. Null uses the client's permission mode, which defaults to
        /// <see cref="CliPermissionMode.Restricted"/>.
        /// </summary>
        public CliPermissionMode? PermissionMode { get; set; } = null;

        /// <summary>
        /// Environment variables for the process, applied over the client's environment. A null value removes the
        /// variable from the inherited environment.
        /// </summary>
        public Dictionary<string, string?>? Environment { get; set; } = null;

        /// <summary>
        /// Additional command-line options appended after the client's own arguments, in order. The key is the option
        /// (for example <c>--max-turns</c>); a null or empty value passes the option alone. Arguments are passed directly to
        /// the process, never through a shell.
        /// </summary>
        public Dictionary<string, string?>? ExtraArguments { get; set; } = null;

        /// <summary>
        /// Maximum number of characters read from standard output before the process tree is killed and the run fails
        /// with <see cref="CliFailureKind.OutputLimitExceeded"/>. Must be greater than zero. Default: 1,000,000.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when set to zero or less.</exception>
        public int MaxOutputCharacters
        {
            get { return _MaxOutputCharacters; }
            set
            {
                if (value <= 0) throw new ArgumentOutOfRangeException(nameof(MaxOutputCharacters), "MaxOutputCharacters must be greater than zero.");
                _MaxOutputCharacters = value;
            }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize an empty request.
        /// </summary>
        public CliAgentRequest()
        {
        }

        /// <summary>
        /// Initialize a request with a prompt.
        /// </summary>
        /// <param name="prompt">The user prompt.</param>
        public CliAgentRequest(string prompt)
        {
            Prompt = prompt;
        }

        #endregion
    }
}
