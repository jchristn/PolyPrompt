namespace PolyPrompt.Clients
{
    /// <summary>
    /// How an agent harness client starts its CLI for one run: the argument list and what to write to standard input.
    /// Built by <see cref="CliAgentClientBase.BuildInvocation"/>.
    /// </summary>
    public sealed class CliInvocation
    {
        /// <summary>
        /// Arguments, in order, not including the executable. Passed directly to the process, never through a shell.
        /// </summary>
        public List<string> Arguments { get; set; } = new List<string>();

        /// <summary>
        /// Text written to standard input, or null. Prompts travel here wherever the CLI accepts them on standard input,
        /// so they are not limited by command-line length or platform quoting rules.
        /// </summary>
        public string? StandardInput { get; set; } = null;

        /// <summary>
        /// When true, standard input stays open after <see cref="StandardInput"/> is written and is closed once the run
        /// reports completion or failure (for request/response protocols such as an RPC mode). When the process has not
        /// exited shortly after that, its tree is killed and the run keeps its reported outcome.
        /// </summary>
        public bool KeepStandardInputOpen { get; set; } = false;

        /// <summary>
        /// Extra environment variables for this run, applied over the client's and the request's environment.
        /// </summary>
        public Dictionary<string, string?>? Environment { get; set; } = null;
    }
}
