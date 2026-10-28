namespace Test.Shared
{
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using Touchstone.Core;

    /// <summary>
    /// Fake-CLI cases for <see cref="GeminiCliAgentClient"/>: the shared harness kit plus Gemini CLI's documented exit codes,
    /// session resume, and non-fatal error events. The scenarios are constructed from the Gemini CLI 0.66.0 documentation
    /// and source, not recorded (the CLI was not available when the adapter was written).
    /// </summary>
    internal static class LocalGeminiCliAgentCases
    {
        private const string SessionId = "8f1c2d3e-4b5a-4c6d-9e7f-0a1b2c3d4e5f";

        public static CliAgentProfile Profile
        {
            get
            {
                return new CliAgentProfile
                {
                    Id = "geminicli",
                    ExecutableName = "gemini",
                    Create = () => new GeminiCliAgentClient(),
                    HappyScenario = "geminicli-happy",
                    ErrorExitScenario = "geminicli-error-exit",
                    StderrOnlyScenario = "geminicli-stderr-only",
                    MalformedScenario = "geminicli-malformed",
                    SlowScenario = "geminicli-slow",
                    HugeScenario = "geminicli-huge",
                    UnauthenticatedScenario = "geminicli-unauthenticated",
                    RateLimitedScenario = "geminicli-rate-limited",
                    HappyText = "`a.txt`",
                    HappySessionId = SessionId,
                    HappyReasoningContains = null,
                    HappyToolName = "list_directory",
                    HappyUsage = new TokenUsage { PromptTokens = 12000, CachedPromptTokens = 8000, CompletionTokens = 45, TotalTokens = 12345 },
                    HappyCostUsd = null,
                    HappyModel = "gemini-2.5-pro",
                    ErrorExitMessageContains = "models/no-such-model-xyz is not found for API version v1beta",
                    StderrOnlyMessageContains = "Unknown argument: bogus",
                    HasAuthenticationStatus = false,
                    PromptOnStandardInput = true,
                    ArgvCases = new List<CliAgentProfile.ArgvCase>
                    {
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "default (Restricted)",
                            Expected = new List<string> { "--output-format", "stream-json", "--approval-mode", "default" },
                            ExpectedStandardInput = "hello",
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "ReadOnly with model",
                            Request = r => { r.PermissionMode = CliPermissionMode.ReadOnly; r.Model = "gemini-2.5-flash"; },
                            Expected = new List<string> { "--output-format", "stream-json", "--approval-mode", "default", "-m", "gemini-2.5-flash" },
                            ExpectedStandardInput = "hello",
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "WorkspaceWrite from the client",
                            Client = c => { c.PermissionMode = CliPermissionMode.WorkspaceWrite; c.Model = "client-model"; },
                            Expected = new List<string> { "--output-format", "stream-json", "--approval-mode", "auto_edit", "-m", "client-model" },
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "Unrestricted",
                            Request = r => r.PermissionMode = CliPermissionMode.Unrestricted,
                            Expected = new List<string> { "--output-format", "stream-json", "--approval-mode", "yolo" },
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "resume by session id",
                            Request = r => r.SessionId = "abc-123",
                            Expected = new List<string> { "--output-format", "stream-json", "--approval-mode", "default", "--resume", "abc-123" },
                            ExpectedStandardInput = "hello",
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "system prompt is prepended (Gemini CLI has no system-prompt option)",
                            Request = r => r.SystemPrompt = "Be terse.",
                            Expected = new List<string> { "--output-format", "stream-json", "--approval-mode", "default" },
                            ExpectedStandardInput = "System instructions:\nBe terse.\n\nRequest:\nhello",
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "ExtraArguments are appended after the client's arguments",
                            Request = r => r.ExtraArguments = new Dictionary<string, string?> { { "--sandbox", null }, { "--include-directories", "../lib" } },
                            Expected = new List<string> { "--output-format", "stream-json", "--approval-mode", "default", "--sandbox", "--include-directories", "../lib" },
                        },
                    },
                };
            }
        }

        public static IEnumerable<TestCaseDescriptor> Create(string suiteId)
        {
            List<TestCaseDescriptor> cases = CliAgentCaseKit.Create(suiteId, Profile);
            cases.Add(CliAgentCaseKit.Case(suiteId, "cli_geminicli_exit_codes", "geminicli: exit codes 41, 42, and 53 are NotAuthenticated, InvalidInput, and TurnLimitReached", RunExitCodesAsync));
            cases.Add(CliAgentCaseKit.Case(suiteId, "cli_geminicli_session_history", "geminicli: a session id in history provider state resumes; plain history is replayed", RunSessionHistoryAsync));
            cases.Add(CliAgentCaseKit.Case(suiteId, "cli_geminicli_error_events", "geminicli: error events are notices, failed tools are error results, and an error result fails the run", RunErrorEventsAsync));
            cases.Add(CliAgentCaseKit.Case(suiteId, "cli_geminicli_final_text", "geminicli: text deltas stream in order and the final text is the last turn's text", RunFinalTextAsync));
            return cases;
        }

        private static async Task RunExitCodesAsync(CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = CliAgentCaseKit.Attach(Profile, "geminicli-unauthenticated");
            using (fake)
            using (client)
            {
                CliAgentResult result = await client.RunAsync(new CliAgentRequest("hi"), token).ConfigureAwait(false);
                SharedAssert.Equal(CliFailureKind.NotAuthenticated, result.FailureKind, "Exit 41 is NotAuthenticated. Error: " + result.Error);
                SharedAssert.Equal(41, result.ExitCode, "Exit code 41.");
                SharedAssert.True(result.Error != null && result.Error.Contains("Please set an Auth method", StringComparison.Ordinal), "The stderr message is the error: " + result.Error);

                fake.UseScenario(client, "geminicli-input-error");
                result = await client.RunAsync(new CliAgentRequest("hi") { SessionId = "no-such-session" }, token).ConfigureAwait(false);
                SharedAssert.Equal(CliFailureKind.InvalidInput, result.FailureKind, "Exit 42 is InvalidInput. Error: " + result.Error);
                SharedAssert.Equal(42, result.ExitCode, "Exit code 42.");
                SharedAssert.True(result.Error != null && result.Error.Contains("Invalid session identifier", StringComparison.Ordinal), "The stderr message is the error: " + result.Error);
                SharedAssert.True(fake.Arguments.Contains("--resume") && fake.Arguments.Contains("no-such-session"), "The session id was passed to --resume.");
                CliAgentCaseKit.AssertTerminal(result, CliAgentEventKind.Failed, "input error");

                fake.UseScenario(client, "geminicli-turn-limit");
                result = await client.RunAsync(new CliAgentRequest("hi"), token).ConfigureAwait(false);
                SharedAssert.Equal(CliFailureKind.TurnLimitReached, result.FailureKind, "Exit 53 is TurnLimitReached. Error: " + result.Error);
                SharedAssert.Equal(53, result.ExitCode, "Exit code 53.");
                SharedAssert.True(result.Error != null && result.Error.Contains("Reached max session turns", StringComparison.Ordinal), "The result event's error is the message: " + result.Error);
                SharedAssert.Equal("5a6b7c8d-9e0f-4a1b-8c2d-3e4f5a6b7c84", result.SessionId, "Session id is reported even when the turn fails.");
                CliAgentEvent failed = result.Events[result.Events.Count - 1];
                SharedAssert.Equal(CliFailureKind.TurnLimitReached, failed.FailureKind, "Failed event carries the failure kind.");

                // Exit 1 keeps the base classification: a structured error is a provider error.
                fake.UseScenario(client, "geminicli-error-exit");
                result = await client.RunAsync(new CliAgentRequest("hi"), token).ConfigureAwait(false);
                SharedAssert.Equal(CliFailureKind.ProviderError, result.FailureKind, "Exit 1 with a structured error is ProviderError.");
            }
        }

        private static async Task RunSessionHistoryAsync(CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = CliAgentCaseKit.Attach(Profile, "geminicli-happy");
            using (fake)
            using (client)
            {
                SharedAssert.True(client.SupportsSessionResume, "Gemini CLI resumes sessions.");
                CliAgentResult first = await client.RunAsync(new CliAgentRequest("first"), token).ConfigureAwait(false);
                SharedAssert.Equal(SessionId, first.SessionId, "First turn session id.");
                SharedAssert.False(fake.Arguments.Contains("--resume"), "A first turn does not resume.");

                ChatMessage assistant = ChatMessage.Assistant(first.Text);
                assistant.ProviderState = first.ProviderState;
                List<ChatMessage> history = new List<ChatMessage> { ChatMessage.User("first"), assistant };

                CliAgentResult second = await client.RunAsync(new CliAgentRequest("second") { History = history }, token).ConfigureAwait(false);
                SharedAssert.True(second.Success, "Second turn succeeds: " + second.Error);
                List<string> args = fake.Arguments;
                int resume = args.IndexOf("--resume");
                SharedAssert.True(resume >= 0 && resume + 1 < args.Count, "History with a session id resumes. Args: " + string.Join(" | ", args));
                SharedAssert.Equal(SessionId, args[resume + 1], "The session id from provider state follows --resume.");
                SharedAssert.Equal("second", fake.StandardInput, "Only the new prompt is sent when resuming.");

                List<ChatMessage> plain = new List<ChatMessage> { ChatMessage.System("Be terse."), ChatMessage.User("What is 2+2?"), ChatMessage.Assistant("4") };
                CliAgentResult replayed = await client.RunAsync(new CliAgentRequest("And 3+3?") { History = plain }, token).ConfigureAwait(false);
                SharedAssert.True(replayed.Success, "Replayed turn succeeds: " + replayed.Error);
                SharedAssert.False(fake.Arguments.Contains("--resume"), "Plain history does not resume.");
                SharedAssert.Equal(
                    "System instructions:\nBe terse.\n\nConversation so far:\n\nUser: What is 2+2?\n\nAssistant: 4\n\nCurrent request:\nAnd 3+3?",
                    fake.StandardInput,
                    "Plain history is replayed into the prompt, with system messages as instructions.");
            }
        }

        private static async Task RunErrorEventsAsync(CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = CliAgentCaseKit.Attach(Profile, "geminicli-invalid-stream");
            using (fake)
            using (client)
            {
                CliAgentResult result = await client.RunAsync(new CliAgentRequest("hi"), token).ConfigureAwait(false);
                SharedAssert.False(result.Success, "A result event with status error fails the run even with exit code 0.");
                SharedAssert.Equal(0, result.ExitCode, "Exit code 0.");
                SharedAssert.Equal(CliFailureKind.ProviderError, result.FailureKind, "Failure kind. Error: " + result.Error);
                SharedAssert.Equal("Model stream ended with an invalid chunk or missing finish reason.", result.Error, "Without an error object the last error event is the message.");
                SharedAssert.True(result.Events.Any(e => e.Kind == CliAgentEventKind.Notice && e.IsError && e.ProviderEventType == "error" && (e.Text ?? "").StartsWith("Agent execution blocked", StringComparison.Ordinal)), "Warning events become error notices.");

                CliAgentEvent? toolResult = result.Events.FirstOrDefault(e => e.Kind == CliAgentEventKind.ToolResult);
                SharedAssert.NotNull(toolResult, "There is a tool result.");
                SharedAssert.True(toolResult!.IsError, "A tool result with status error is an error.");
                SharedAssert.Equal("read_file", toolResult.ToolName, "The tool name is carried over from tool_use.");
                SharedAssert.Equal("File not found: missing.txt", toolResult.ToolOutput, "The tool error message is the output.");

                CliAgentEvent? toolUse = result.Events.FirstOrDefault(e => e.Kind == CliAgentEventKind.ToolUse);
                SharedAssert.True(toolUse != null && (toolUse.ToolInput ?? "").Contains("missing.txt", StringComparison.Ordinal), "Tool parameters are the tool input.");

                SharedAssert.NotNull(result.Usage, "Usage from an error result is still reported.");
                SharedAssert.Equal(250, result.Usage!.PromptTokens, "PromptTokens.");
                SharedAssert.Equal(300, result.Usage.TotalTokens, "TotalTokens as reported.");
                SharedAssert.Equal(null, result.Usage.ReasoningTokens, "Reasoning tokens are not reported.");
                SharedAssert.Equal(null, result.CostUsd, "Cost is not reported.");
                CliAgentCaseKit.AssertTerminal(result, CliAgentEventKind.Failed, "invalid stream");
            }
        }

        private static async Task RunFinalTextAsync(CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = CliAgentCaseKit.Attach(Profile, "geminicli-happy");
            using (fake)
            using (client)
            {
                List<string> deltas = new List<string>();
                CliAgentEvent? last = null;
                await foreach (CliAgentEvent evt in client.RunStreamingAsync(new CliAgentRequest("List the files."), token).ConfigureAwait(false))
                {
                    if (evt.Kind == CliAgentEventKind.TextDelta) deltas.Add(evt.Text ?? "");
                    last = evt;
                }

                SharedAssert.Equal("I will list the files |in the current directory.|`a.txt`", string.Join("|", deltas), "Assistant deltas stream in order; the echoed user message is not text.");
                SharedAssert.NotNull(last, "The stream ends.");
                SharedAssert.Equal(CliAgentEventKind.Completed, last!.Kind, "The stream ends with Completed.");
                SharedAssert.Equal("`a.txt`", last.Text, "The final text is the text after the last tool round.");
                SharedAssert.Equal("List the files.", fake.StandardInput, "The prompt is the whole of standard input; no -p argument.");
                SharedAssert.False(fake.Arguments.Contains("-p") || fake.Arguments.Contains("--prompt"), "No -p argument.");
                SharedAssert.False(fake.Arguments.Contains("yolo") || fake.Arguments.Contains("--yolo") || fake.Arguments.Contains("plan"), "Restricted never uses yolo or plan mode.");
            }
        }
    }
}
