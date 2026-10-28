namespace PolyPrompt.Models
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Result of one agent harness run. <see cref="ResponseBase.Success"/> is true only when the process exited cleanly
    /// and the harness reported no error; otherwise <see cref="FailureKind"/> says why and <see cref="ResponseBase.Error"/>
    /// carries the message. <see cref="ResponseBase.StatusCode"/> is not used; see <see cref="ExitCode"/>.
    /// </summary>
    public sealed class CliAgentResult : ResponseBase
    {
        /// <summary>
        /// Final assistant response text.
        /// </summary>
        public string Text { get; set; } = "";

        /// <summary>
        /// Reasoning or thinking text the harness exposed, or null.
        /// </summary>
        public string? Reasoning { get; set; } = null;

        /// <summary>
        /// Session id to pass as <see cref="CliAgentRequest.SessionId"/> to continue this conversation, or null when the
        /// harness did not report one. Also stored in <see cref="ResponseBase.ProviderState"/>.
        /// </summary>
        public string? SessionId { get; set; } = null;

        /// <summary>
        /// Process exit code, or null when the process could not be started or never exited on its own.
        /// </summary>
        public int? ExitCode { get; set; } = null;

        /// <summary>
        /// Captured standard error (bounded).
        /// </summary>
        public string StandardError { get; set; } = "";

        /// <summary>
        /// Every event observed, in order, ending with <see cref="CliAgentEventKind.Completed"/> or
        /// <see cref="CliAgentEventKind.Failed"/>.
        /// </summary>
        public List<CliAgentEvent> Events { get; set; } = new List<CliAgentEvent>();

        /// <summary>
        /// Token usage exactly as the harness reported it, or null when it did not.
        /// </summary>
        public TokenUsage? Usage { get; set; } = null;

        /// <summary>
        /// Cost in US dollars exactly as the harness reported it, or null; never estimated.
        /// </summary>
        public decimal? CostUsd { get; set; } = null;

        /// <summary>
        /// Raw standard output (bounded by <see cref="CliAgentRequest.MaxOutputCharacters"/>).
        /// </summary>
        public string RawOutput { get; set; } = "";

        /// <summary>
        /// Why the run failed, or <see cref="CliFailureKind.None"/> on success.
        /// </summary>
        public CliFailureKind FailureKind { get; set; } = CliFailureKind.None;

        /// <summary>
        /// True when the run hit its timeout and the process tree was killed.
        /// </summary>
        public bool TimedOut { get; set; } = false;

        /// <summary>
        /// True when standard output exceeded the output limit and the process tree was killed.
        /// </summary>
        public bool OutputTruncated { get; set; } = false;

        /// <summary>
        /// Number of standard output lines that could not be parsed. Each one also appears as a
        /// <see cref="CliAgentEventKind.Notice"/> event.
        /// </summary>
        public int MalformedLines { get; set; } = 0;

        /// <summary>
        /// Same as <see cref="ResponseBase.Success"/>.
        /// </summary>
        [JsonIgnore]
        public bool Succeeded
        {
            get { return Success; }
        }
    }
}
