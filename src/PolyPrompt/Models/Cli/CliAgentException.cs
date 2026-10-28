namespace PolyPrompt.Models
{
    /// <summary>
    /// Thrown by agent harness operations that return data rather than a <see cref="CliAgentResult"/> (for example
    /// <c>ListModelsAsync</c>) when the CLI cannot be run or reports a failure.
    /// </summary>
    public sealed class CliAgentException : Exception
    {
        /// <summary>
        /// Why the operation failed.
        /// </summary>
        public CliFailureKind FailureKind { get; }

        /// <summary>
        /// Process exit code, or null when the process did not start or exit.
        /// </summary>
        public int? ExitCode { get; }

        /// <summary>
        /// Captured standard error (bounded), or an empty string.
        /// </summary>
        public string StandardError { get; }

        /// <summary>
        /// Initialize the exception.
        /// </summary>
        /// <param name="failureKind">Failure kind.</param>
        /// <param name="message">Message.</param>
        /// <param name="exitCode">Exit code, or null.</param>
        /// <param name="standardError">Standard error, or null.</param>
        public CliAgentException(CliFailureKind failureKind, string message, int? exitCode = null, string? standardError = null)
            : base(message)
        {
            FailureKind = failureKind;
            ExitCode = exitCode;
            StandardError = standardError ?? "";
        }
    }
}
