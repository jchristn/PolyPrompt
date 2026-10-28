namespace PolyPrompt.Models
{
    /// <summary>
    /// Outcome of an agent harness connectivity check.
    /// </summary>
    public enum CliConnectivityStatus
    {
        /// <summary>
        /// The harness is installed, runs, and (where the CLI can report it) is authenticated.
        /// </summary>
        Ok,

        /// <summary>
        /// The harness executable could not be found or started.
        /// </summary>
        NotInstalled,

        /// <summary>
        /// The harness runs but is not logged in.
        /// </summary>
        NotAuthenticated,

        /// <summary>
        /// The harness was found but its version or status command failed or timed out.
        /// </summary>
        Error
    }
}
