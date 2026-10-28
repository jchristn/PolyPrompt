namespace Test.Shared
{
    using PolyPrompt.Clients;
    using PolyPrompt.Models;

    /// <summary>
    /// What the shared agent harness cases need to know about one adapter: how to build it, which fake CLI scenarios
    /// exercise each outcome, and what the happy-path scenario is expected to produce. Used by
    /// <see cref="CliAgentCaseKit"/> so every adapter gets the same coverage.
    /// </summary>
    internal sealed class CliAgentProfile
    {
        /// <summary>
        /// Short id used in case ids, for example <c>codex</c>.
        /// </summary>
        public string Id { get; set; } = "";

        /// <summary>
        /// Executable name the fake CLI is installed under, for example <c>codex</c>.
        /// </summary>
        public string ExecutableName { get; set; } = "";

        /// <summary>
        /// Creates a client with default settings.
        /// </summary>
        public Func<CliAgentClientBase> Create { get; set; } = () => throw new InvalidOperationException();

        /// <summary>
        /// Happy path: text, reasoning (when the harness exposes it), tool events, usage, cost, session id. Also answers
        /// <c>--version</c> and the login status command when the CLI has one.
        /// </summary>
        public string HappyScenario { get; set; } = "";

        /// <summary>
        /// Non-zero exit with a structured error on stdout.
        /// </summary>
        public string ErrorExitScenario { get; set; } = "";

        /// <summary>
        /// Non-zero exit with only stderr output.
        /// </summary>
        public string StderrOnlyScenario { get; set; } = "";

        /// <summary>
        /// A malformed or truncated JSON line mid-stream, then a successful completion.
        /// </summary>
        public string MalformedScenario { get; set; } = "";

        /// <summary>
        /// Starts, spawns a child process, and hangs (for cancellation and timeout).
        /// </summary>
        public string SlowScenario { get; set; } = "";

        /// <summary>
        /// Writes more output than the limit.
        /// </summary>
        public string HugeScenario { get; set; } = "";

        /// <summary>
        /// Rejected credentials; the login status command (if any) reports not logged in.
        /// </summary>
        public string UnauthenticatedScenario { get; set; } = "";

        /// <summary>
        /// Rate or usage limit failure.
        /// </summary>
        public string RateLimitedScenario { get; set; } = "";

        /// <summary>
        /// Expected final text of the happy path.
        /// </summary>
        public string HappyText { get; set; } = "";

        /// <summary>
        /// Expected session id of the happy path, or null when the harness reports none.
        /// </summary>
        public string? HappySessionId { get; set; }

        /// <summary>
        /// Substring expected in the happy path reasoning, or null when the scenario has none.
        /// </summary>
        public string? HappyReasoningContains { get; set; }

        /// <summary>
        /// Tool name expected on the happy path tool events, or null when the scenario has none.
        /// </summary>
        public string? HappyToolName { get; set; }

        /// <summary>
        /// Expected happy path usage, or null when the harness reports none.
        /// </summary>
        public TokenUsage? HappyUsage { get; set; }

        /// <summary>
        /// Expected happy path cost, or null when the harness reports none.
        /// </summary>
        public decimal? HappyCostUsd { get; set; }

        /// <summary>
        /// Expected happy path model, or null to skip the check.
        /// </summary>
        public string? HappyModel { get; set; }

        /// <summary>
        /// Substring expected in the error-exit message.
        /// </summary>
        public string ErrorExitMessageContains { get; set; } = "";

        /// <summary>
        /// Failure kind expected for the error-exit scenario. Default: <see cref="CliFailureKind.ProviderError"/>.
        /// </summary>
        public CliFailureKind ErrorExitKind { get; set; } = CliFailureKind.ProviderError;

        /// <summary>
        /// Substring expected in the stderr-only failure message.
        /// </summary>
        public string StderrOnlyMessageContains { get; set; } = "";

        /// <summary>
        /// True when the CLI has a login status command, so connectivity can report not authenticated.
        /// </summary>
        public bool HasAuthenticationStatus { get; set; }

        /// <summary>
        /// True when the prompt travels on standard input (D5); false when the CLI only accepts it as an argument.
        /// </summary>
        public bool PromptOnStandardInput { get; set; } = true;

        /// <summary>
        /// Exact argument lists for request variations: a description, a request customizer (applied to a request whose
        /// prompt is <c>hello</c>), a client customizer, and the expected arguments. Run against the happy scenario.
        /// </summary>
        public List<ArgvCase> ArgvCases { get; set; } = new List<ArgvCase>();

        /// <summary>
        /// One argument-list expectation.
        /// </summary>
        public sealed class ArgvCase
        {
            /// <summary>
            /// Description used in assertion messages.
            /// </summary>
            public string Description { get; set; } = "";

            /// <summary>
            /// Customizes the request.
            /// </summary>
            public Action<CliAgentRequest>? Request { get; set; }

            /// <summary>
            /// Customizes the client.
            /// </summary>
            public Action<CliAgentClientBase>? Client { get; set; }

            /// <summary>
            /// Expected arguments.
            /// </summary>
            public List<string> Expected { get; set; } = new List<string>();

            /// <summary>
            /// Expected standard input, or null to skip the check.
            /// </summary>
            public string? ExpectedStandardInput { get; set; }
        }
    }
}
