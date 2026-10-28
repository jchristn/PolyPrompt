namespace PolyPrompt.Models
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Result of an agent harness connectivity check: whether the CLI is installed, runs, and is logged in.
    /// </summary>
    public sealed class CliConnectivityResult
    {
        /// <summary>
        /// Overall status.
        /// </summary>
        public CliConnectivityStatus Status { get; set; } = CliConnectivityStatus.Error;

        /// <summary>
        /// True when <see cref="Status"/> is <see cref="CliConnectivityStatus.Ok"/>.
        /// </summary>
        [JsonIgnore]
        public bool Success
        {
            get { return Status == CliConnectivityStatus.Ok; }
        }

        /// <summary>
        /// Resolved path of the executable, or null when it was not found.
        /// </summary>
        public string? ExecutablePath { get; set; } = null;

        /// <summary>
        /// Version string reported by the CLI, or null.
        /// </summary>
        public string? Version { get; set; } = null;

        /// <summary>
        /// True when the CLI has a status command and it confirmed the login; false when the CLI offers no way to check,
        /// in which case <see cref="Status"/> reflects installation only.
        /// </summary>
        public bool AuthenticationVerified { get; set; } = false;

        /// <summary>
        /// Human-readable detail, for example the CLI's login status line or the reason a check failed.
        /// </summary>
        public string? Message { get; set; } = null;
    }
}
