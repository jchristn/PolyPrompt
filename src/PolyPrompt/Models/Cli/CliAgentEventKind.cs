namespace PolyPrompt.Models
{
    /// <summary>
    /// Kind of a <see cref="CliAgentEvent"/>. A harness stream is a sequence of agent events, not token deltas.
    /// </summary>
    public enum CliAgentEventKind
    {
        /// <summary>
        /// The harness started a session or turn. <see cref="CliAgentEvent.SessionId"/> carries the session id when the
        /// harness reports one.
        /// </summary>
        Started,

        /// <summary>
        /// Assistant text. Depending on the harness this is a partial delta or a whole message; chunk boundaries are not
        /// stable across runs or versions.
        /// </summary>
        TextDelta,

        /// <summary>
        /// Reasoning or thinking text, when the harness exposes it.
        /// </summary>
        ReasoningDelta,

        /// <summary>
        /// The harness invoked one of its own tools (shell command, file edit, search, MCP call, and so on). The harness
        /// runs the tool itself; the caller only observes it.
        /// </summary>
        ToolUse,

        /// <summary>
        /// The result of a tool the harness ran.
        /// </summary>
        ToolResult,

        /// <summary>
        /// Informational or warning output from the harness, including stream lines PolyPrompt could not parse.
        /// </summary>
        Notice,

        /// <summary>
        /// The run finished successfully. Always the last event of a successful run.
        /// </summary>
        Completed,

        /// <summary>
        /// The run failed. Always the last event of a failed run; <see cref="CliAgentEvent.FailureKind"/> says why.
        /// </summary>
        Failed
    }
}
