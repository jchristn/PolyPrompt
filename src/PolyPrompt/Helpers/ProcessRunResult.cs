namespace PolyPrompt.Helpers
{
    /// <summary>
    /// Outcome of a process started by <see cref="ProcessRunner"/>.
    /// </summary>
    public sealed class ProcessRunResult
    {
        /// <summary>
        /// Exit code, or null when the process did not exit (for example when it could not be killed).
        /// </summary>
        public int? ExitCode { get; set; } = null;

        /// <summary>
        /// Captured standard error, up to <see cref="ProcessRunRequest.MaxStandardErrorCharacters"/>.
        /// </summary>
        public string StandardError { get; set; } = "";

        /// <summary>
        /// True when standard error was longer than the retained portion.
        /// </summary>
        public bool StandardErrorTruncated { get; set; } = false;

        /// <summary>
        /// True when the process tree was killed because the timeout elapsed.
        /// </summary>
        public bool TimedOut { get; set; } = false;

        /// <summary>
        /// True when the process tree was killed because the caller cancelled.
        /// </summary>
        public bool Cancelled { get; set; } = false;

        /// <summary>
        /// True when standard output exceeded the limit and the process tree was killed.
        /// </summary>
        public bool OutputTruncated { get; set; } = false;

        /// <summary>
        /// Characters read from standard output.
        /// </summary>
        public long StandardOutputCharacters { get; set; } = 0;

        /// <summary>
        /// Operating system process id.
        /// </summary>
        public int ProcessId { get; set; } = 0;

        /// <summary>
        /// Wall-clock run time in milliseconds.
        /// </summary>
        public long DurationMs { get; set; } = 0;
    }
}
