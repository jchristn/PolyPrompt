namespace PolyPrompt.Models
{
    /// <summary>
    /// One event from an agent harness run. Fields that do not apply to the event's <see cref="Kind"/> are null.
    /// </summary>
    public sealed class CliAgentEvent
    {
        /// <summary>
        /// Event kind.
        /// </summary>
        public CliAgentEventKind Kind { get; set; } = CliAgentEventKind.Notice;

        /// <summary>
        /// Text for text, reasoning, and notice events; the final response text on <see cref="CliAgentEventKind.Completed"/>;
        /// the error message on <see cref="CliAgentEventKind.Failed"/>.
        /// </summary>
        public string? Text { get; set; } = null;

        /// <summary>
        /// Harness tool name for tool events, for example <c>command_execution</c>, <c>Bash</c>, or <c>read</c>.
        /// </summary>
        public string? ToolName { get; set; } = null;

        /// <summary>
        /// Harness identifier pairing a tool use with its result, when reported.
        /// </summary>
        public string? ToolCallId { get; set; } = null;

        /// <summary>
        /// Tool input as reported by the harness (a command line or a JSON object rendered as text).
        /// </summary>
        public string? ToolInput { get; set; } = null;

        /// <summary>
        /// Tool output as reported by the harness.
        /// </summary>
        public string? ToolOutput { get; set; } = null;

        /// <summary>
        /// True when a tool result or notice reports an error.
        /// </summary>
        public bool IsError { get; set; } = false;

        /// <summary>
        /// Session id, on <see cref="CliAgentEventKind.Started"/> and terminal events.
        /// </summary>
        public string? SessionId { get; set; } = null;

        /// <summary>
        /// Token usage exactly as the harness reported it, on terminal events. Null when not reported.
        /// </summary>
        public TokenUsage? Usage { get; set; } = null;

        /// <summary>
        /// Cost in US dollars exactly as the harness reported it, on terminal events. Null when not reported; never
        /// estimated.
        /// </summary>
        public decimal? CostUsd { get; set; } = null;

        /// <summary>
        /// Failure reason, on <see cref="CliAgentEventKind.Failed"/>.
        /// </summary>
        public CliFailureKind? FailureKind { get; set; } = null;

        /// <summary>
        /// Process exit code, on terminal events when the process exited.
        /// </summary>
        public int? ExitCode { get; set; } = null;

        /// <summary>
        /// The harness's own event type, for example <c>item.completed</c> or <c>stream_event</c>.
        /// </summary>
        public string? ProviderEventType { get; set; } = null;

        /// <summary>
        /// The raw output line the event was parsed from, when there was one.
        /// </summary>
        public string? Raw { get; set; } = null;

        /// <summary>
        /// UTC time the event was observed.
        /// </summary>
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Initialize an empty event.
        /// </summary>
        public CliAgentEvent()
        {
        }

        /// <summary>
        /// Initialize an event.
        /// </summary>
        /// <param name="kind">Event kind.</param>
        /// <param name="text">Event text.</param>
        public CliAgentEvent(CliAgentEventKind kind, string? text = null)
        {
            Kind = kind;
            Text = text;
        }
    }
}
