namespace PolyPrompt.Models
{
    /// <summary>
    /// Why an agent harness run or connectivity check failed.
    /// </summary>
    public enum CliFailureKind
    {
        /// <summary>
        /// No failure.
        /// </summary>
        None,

        /// <summary>
        /// The harness executable could not be found or started. Install the CLI or set the client's
        /// <c>Executable</c> to its full path.
        /// </summary>
        NotInstalled,

        /// <summary>
        /// The harness is installed but not logged in, or its credentials were rejected.
        /// </summary>
        NotAuthenticated,

        /// <summary>
        /// The harness or its model provider reported a rate limit, quota, or usage limit.
        /// </summary>
        RateLimited,

        /// <summary>
        /// The run exceeded its timeout and the process tree was killed.
        /// </summary>
        TimedOut,

        /// <summary>
        /// The process exited with a non-zero code (or was killed by a signal) without reporting a structured error.
        /// </summary>
        Crashed,

        /// <summary>
        /// The harness reported a structured error (for example an invalid model or a failed turn).
        /// </summary>
        ProviderError,

        /// <summary>
        /// The harness rejected its input (for example an invalid argument or prompt).
        /// </summary>
        InvalidInput,

        /// <summary>
        /// The harness stopped because it reached its turn limit.
        /// </summary>
        TurnLimitReached,

        /// <summary>
        /// The process exited successfully but its output could not be understood.
        /// </summary>
        InvalidOutput,

        /// <summary>
        /// Standard output exceeded <see cref="CliAgentRequest.MaxOutputCharacters"/> and the process tree was killed.
        /// </summary>
        OutputLimitExceeded
    }
}
