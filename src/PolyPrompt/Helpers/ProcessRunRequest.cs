namespace PolyPrompt.Helpers
{
    /// <summary>
    /// What <see cref="ProcessRunner"/> should start. The process is always started directly, never through a shell, so
    /// arguments are passed verbatim and need no quoting.
    /// </summary>
    public sealed class ProcessRunRequest
    {
        /// <summary>
        /// Resolved path of the executable to start.
        /// </summary>
        public string FileName { get; set; } = "";

        /// <summary>
        /// Arguments, in order. Each element is one argument.
        /// </summary>
        public List<string> Arguments { get; set; } = new List<string>();

        /// <summary>
        /// Working directory, or null for the current directory.
        /// </summary>
        public string? WorkingDirectory { get; set; } = null;

        /// <summary>
        /// Environment overrides applied over the inherited environment. A null value removes the variable.
        /// </summary>
        public Dictionary<string, string?>? Environment { get; set; } = null;

        /// <summary>
        /// Text written to standard input (UTF-8, no byte order mark), or null to write nothing.
        /// </summary>
        public string? StandardInput { get; set; } = null;

        /// <summary>
        /// When false (the default) standard input is closed after <see cref="StandardInput"/> is written. When true it
        /// stays open for <see cref="ProcessRunner.WriteLineAsync"/> until <see cref="ProcessRunner.CloseStandardInput"/>.
        /// </summary>
        public bool KeepStandardInputOpen { get; set; } = false;

        /// <summary>
        /// Timeout for the whole run in milliseconds. When it elapses the process tree is killed. Default: 120000.
        /// </summary>
        public int TimeoutMs { get; set; } = 120000;

        /// <summary>
        /// Maximum characters read from standard output before the process tree is killed. Default: 1,000,000.
        /// </summary>
        public int MaxOutputCharacters { get; set; } = 1_000_000;

        /// <summary>
        /// Maximum characters of standard error retained; the rest is read and discarded. Default: 65,536.
        /// </summary>
        public int MaxStandardErrorCharacters { get; set; } = 65536;
    }
}
