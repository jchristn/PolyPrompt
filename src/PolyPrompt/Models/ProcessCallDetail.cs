namespace PolyPrompt.Models
{
    /// <summary>
    /// Details of one process execution made by an agent harness client, recorded in <c>CallDetails</c> next to HTTP
    /// calls. The inherited fields keep their meaning: <see cref="CallDetail.Method"/> is <c>EXEC</c>,
    /// <see cref="CallDetail.Url"/> is the resolved executable path, <see cref="CallDetail.RequestBody"/> is the argument
    /// list as a JSON array, <see cref="CallDetail.StatusCode"/> is the exit code, <see cref="CallDetail.ResponseBody"/> is
    /// standard output (bounded), and <see cref="CallDetail.Error"/> is the failure message or standard error.
    /// </summary>
    public sealed class ProcessCallDetail : CallDetail
    {
        /// <summary>
        /// Executable as configured on the client, for example <c>codex</c>.
        /// </summary>
        public string? Executable { get; set; }

        /// <summary>
        /// Arguments passed to the process, in order (the executable itself is not included).
        /// </summary>
        public List<string> Arguments { get; set; } = new List<string>();

        /// <summary>
        /// Working directory, or null for the current directory.
        /// </summary>
        public string? WorkingDirectory { get; set; }

        /// <summary>
        /// Text written to standard input, or null when none was written.
        /// </summary>
        public string? StandardInput { get; set; }

        /// <summary>
        /// Process exit code, or null when the process could not be started.
        /// </summary>
        public int? ExitCode { get; set; }

        /// <summary>
        /// Captured standard error (bounded).
        /// </summary>
        public string? StandardError { get; set; }

        /// <summary>
        /// Operating system process id, or null when the process could not be started.
        /// </summary>
        public int? ProcessId { get; set; }

        /// <summary>
        /// True when the process tree was killed because the run timed out.
        /// </summary>
        public bool TimedOut { get; set; }

        /// <summary>
        /// True when the process tree was killed because the caller cancelled.
        /// </summary>
        public bool Cancelled { get; set; }

        /// <summary>
        /// True when standard output exceeded the configured limit and the process tree was killed.
        /// </summary>
        public bool OutputTruncated { get; set; }

        /// <summary>
        /// Failure classification, or <see cref="CliFailureKind.None"/>.
        /// </summary>
        public CliFailureKind FailureKind { get; set; } = CliFailureKind.None;
    }
}
