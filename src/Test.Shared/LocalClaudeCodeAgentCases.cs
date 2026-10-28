namespace Test.Shared
{
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using Touchstone.Core;

    /// <summary>
    /// Fake-CLI cases for <see cref="ClaudeCodeAgentClient"/>: the shared harness kit plus session resume and history,
    /// the system prompt option, structured error codes, turn limits, permission denials, and a recorded-output replay.
    /// </summary>
    internal static class LocalClaudeCodeAgentCases
    {
        private const string SessionId = "f9b8046c-200a-4a38-972d-fa0a96abd921";

        private static readonly List<string> _Prefix = new List<string> { "-p", "--output-format", "stream-json", "--verbose", "--include-partial-messages" };

        private static readonly List<string> _Restricted = new List<string> { "--permission-mode", "dontAsk", "--tools", "Read,Glob,Grep", "--strict-mcp-config" };

        public static CliAgentProfile Profile
        {
            get
            {
                return new CliAgentProfile
                {
                    Id = "claudecode",
                    ExecutableName = "claude",
                    Create = () => new ClaudeCodeAgentClient(),
                    HappyScenario = "claudecode-happy",
                    ErrorExitScenario = "claudecode-error-exit",
                    StderrOnlyScenario = "claudecode-stderr-only",
                    MalformedScenario = "claudecode-malformed",
                    SlowScenario = "claudecode-slow",
                    HugeScenario = "claudecode-huge",
                    UnauthenticatedScenario = "claudecode-unauthenticated",
                    RateLimitedScenario = "claudecode-rate-limited",
                    HappyText = "a.txt",
                    HappySessionId = SessionId,
                    HappyReasoningContains = "Listing the directory first.",
                    HappyToolName = "Glob",
                    HappyUsage = new TokenUsage { PromptTokens = 4, CachedPromptTokens = 77358, CacheCreationTokens = 114298, CompletionTokens = 193, ReasoningTokens = 104 },
                    HappyCostUsd = 0.05469159999999999m,
                    HappyModel = "claude-haiku-5-5",
                    ErrorExitMessageContains = "There's an issue with the selected model (no-such-model-xyz). It may not exist or you may not have access to it. Run --model to pick a different model. (model_not_found, status 404)",
                    StderrOnlyMessageContains = "unknown option '--bogus'",
                    HasAuthenticationStatus = true,
                    PromptOnStandardInput = true,
                    ArgvCases = new List<CliAgentProfile.ArgvCase>
                    {
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "default (Restricted)",
                            Expected = Args(_Restricted),
                            ExpectedStandardInput = "hello",
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "ReadOnly with model",
                            Request = r => { r.PermissionMode = CliPermissionMode.ReadOnly; r.Model = "haiku"; },
                            Expected = Args(new List<string> { "--permission-mode", "dontAsk", "--disallowedTools", "Edit,Write,NotebookEdit", "--model", "haiku" }),
                            ExpectedStandardInput = "hello",
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "WorkspaceWrite from the client",
                            Client = c => { c.PermissionMode = CliPermissionMode.WorkspaceWrite; c.Model = "client-model"; },
                            Expected = Args(new List<string> { "--permission-mode", "acceptEdits", "--model", "client-model" }),
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "Unrestricted",
                            Request = r => r.PermissionMode = CliPermissionMode.Unrestricted,
                            Expected = Args(new List<string> { "--permission-mode", "bypassPermissions" }),
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "resume by session id",
                            Request = r => r.SessionId = "abc-123",
                            Expected = Args(_Restricted, "--resume", "abc-123"),
                            ExpectedStandardInput = "hello",
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "system prompt uses --append-system-prompt and stays out of stdin",
                            Request = r => r.SystemPrompt = "Be terse.",
                            Expected = Args(_Restricted, "--append-system-prompt", "Be terse."),
                            ExpectedStandardInput = "hello",
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "ExtraArguments are appended after the client's arguments",
                            Request = r => r.ExtraArguments = new Dictionary<string, string?> { { "--max-budget-usd", "0.5" }, { "--no-session-persistence", null } },
                            Expected = Args(_Restricted, "--max-budget-usd", "0.5", "--no-session-persistence"),
                        },
                    },
                };
            }
        }

        public static IEnumerable<TestCaseDescriptor> Create(string suiteId)
        {
            List<TestCaseDescriptor> cases = CliAgentCaseKit.Create(suiteId, Profile);
            cases.Add(CliAgentCaseKit.Case(suiteId, "cli_claudecode_session_history", "claudecode: a session id in history provider state resumes; plain history is replayed with system messages as --append-system-prompt", RunSessionHistoryAsync));
            cases.Add(CliAgentCaseKit.Case(suiteId, "cli_claudecode_text_not_doubled", "claudecode: partial deltas build the text and the assistant message does not repeat it; result.result is final", RunTextNotDoubledAsync));
            cases.Add(CliAgentCaseKit.Case(suiteId, "cli_claudecode_turn_limit", "claudecode: error_max_turns is TurnLimitReached; denials, compaction, and subagent messages map as documented", RunTurnLimitAsync));
            cases.Add(CliAgentCaseKit.Case(suiteId, "cli_claudecode_recorded_replay", "claudecode: recorded error streams replay with structured error codes and no live CLI", RunRecordedReplayAsync));
            cases.Add(CliAgentCaseKit.Case(suiteId, "cli_claudecode_restricted_never_bypasses", "claudecode: only Unrestricted passes bypassPermissions; no mode passes --dangerously-skip-permissions", RunPermissionFlagsAsync));
            return cases;
        }

        private static List<string> Args(List<string> permission, params string[] tail)
        {
            List<string> args = new List<string>(_Prefix);
            args.AddRange(permission);
            args.AddRange(tail);
            return args;
        }

        private static async Task RunSessionHistoryAsync(CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = CliAgentCaseKit.Attach(Profile, "claudecode-happy");
            using (fake)
            using (client)
            {
                CliAgentResult first = await client.RunAsync(new CliAgentRequest("first"), token).ConfigureAwait(false);
                SharedAssert.Equal(SessionId, first.SessionId, "First turn session id.");

                ChatMessage assistant = ChatMessage.Assistant(first.Text);
                assistant.ProviderState = first.ProviderState;
                List<ChatMessage> history = new List<ChatMessage> { ChatMessage.System("Be terse."), ChatMessage.User("first"), assistant };

                CliAgentResult second = await client.RunAsync(new CliAgentRequest("second") { History = history }, token).ConfigureAwait(false);
                SharedAssert.True(second.Success, "Second turn succeeds: " + second.Error);
                List<string> args = fake.Arguments;
                int resume = args.IndexOf("--resume");
                SharedAssert.True(resume >= 0 && resume + 1 < args.Count && args[resume + 1] == SessionId, "History with a session id resumes it. Args: " + string.Join(" | ", args));
                int system = args.IndexOf("--append-system-prompt");
                SharedAssert.True(system >= 0 && args[system + 1] == "Be terse.", "System messages in history become --append-system-prompt.");
                SharedAssert.Equal("second", fake.StandardInput, "Only the new prompt is sent when resuming.");

                List<ChatMessage> plain = new List<ChatMessage> { ChatMessage.System("Be terse."), ChatMessage.User("What is 2+2?"), ChatMessage.Assistant("4") };
                CliAgentResult replayed = await client.RunAsync(new CliAgentRequest("And 3+3?") { History = plain, SystemPrompt = "Answer in digits." }, token).ConfigureAwait(false);
                SharedAssert.True(replayed.Success, "Replayed turn succeeds: " + replayed.Error);
                args = fake.Arguments;
                SharedAssert.False(args.Contains("--resume"), "Plain history does not resume.");
                system = args.IndexOf("--append-system-prompt");
                SharedAssert.True(system >= 0 && args[system + 1] == "Answer in digits.\n\nBe terse.", "Request and history system prompts are combined. Args: " + string.Join(" | ", args));
                SharedAssert.Equal(
                    "Conversation so far:\n\nUser: What is 2+2?\n\nAssistant: 4\n\nCurrent request:\nAnd 3+3?",
                    fake.StandardInput,
                    "Plain history is replayed into the prompt; system text stays out of stdin.");
            }
        }

        private static async Task RunTextNotDoubledAsync(CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = CliAgentCaseKit.Attach(Profile, "claudecode-happy");
            using (fake)
            using (client)
            {
                CliAgentResult result = await client.RunAsync(new CliAgentRequest("List the files."), token).ConfigureAwait(false);
                SharedAssert.True(result.Success, "Run succeeds: " + result.Error);
                List<CliAgentEvent> text = result.Events.Where(e => e.Kind == CliAgentEventKind.TextDelta).ToList();
                SharedAssert.Equal("a | .txt", string.Join(" | ", text.Select(e => e.Text)), "Text deltas come from the partial stream only, once each.");
                SharedAssert.Equal("stream_event/content_block_delta", text[0].ProviderEventType, "Text deltas keep the stream event type.");
                SharedAssert.Equal(1, result.Events.Count(e => e.Kind == CliAgentEventKind.ReasoningDelta), "One non-empty thinking delta; empty deltas and the repeated thinking block are skipped.");
                SharedAssert.Equal(1, result.Events.Count(e => e.Kind == CliAgentEventKind.ToolUse), "One ToolUse (partial tool input is not a second ToolUse).");
                CliAgentEvent? tool = result.Events.FirstOrDefault(e => e.Kind == CliAgentEventKind.ToolUse);
                SharedAssert.Equal("toolu_012zFXikKXUZAhDa2LczgB3L", tool?.ToolCallId, "Tool call id.");
                SharedAssert.True((tool?.ToolInput ?? "").Contains("\"pattern\":\"*\"", StringComparison.Ordinal), "Tool input is the JSON input: " + tool?.ToolInput);
                CliAgentEvent? done = result.Events.FirstOrDefault(e => e.Kind == CliAgentEventKind.ToolResult);
                SharedAssert.Equal("a.txt", done?.ToolOutput, "Tool output.");
                SharedAssert.Equal("Glob", done?.ToolName, "ToolResult carries the tool name from its ToolUse.");
                SharedAssert.False(done!.IsError, "Successful tool result.");
                SharedAssert.True(result.Events.Any(e => e.Kind == CliAgentEventKind.Started && e.ProviderEventType == "system/init"), "Started comes from system/init.");
                SharedAssert.False(result.Events.Any(e => e.Kind == CliAgentEventKind.Notice), "Allowed rate-limit events and status lines are not notices.");
            }
        }

        private static async Task RunTurnLimitAsync(CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = CliAgentCaseKit.Attach(Profile, "claudecode-max-turns");
            using (fake)
            using (client)
            {
                CliAgentResult result = await client.RunAsync(new CliAgentRequest("touch a file"), token).ConfigureAwait(false);
                SharedAssert.False(result.Success, "A turn-limit stop fails.");
                SharedAssert.Equal(CliFailureKind.TurnLimitReached, result.FailureKind, "Failure kind. Error: " + result.Error);
                SharedAssert.Equal("Reached maximum number of turns (2) (error_max_turns)", result.Error, "Error comes from errors[] and the subtype.");
                SharedAssert.Equal(0.0012m, result.CostUsd, "Cost is reported even on failure.");
                SharedAssert.Equal(10, result.Usage?.PromptTokens, "Usage is reported even on failure.");
                SharedAssert.Equal(null, result.Usage?.ReasoningTokens, "ReasoningTokens stays null when not reported.");
                SharedAssert.Equal(null, result.Usage?.TotalTokens, "TotalTokens stays null.");
                SharedAssert.Equal("6f1b2c3d-0000-4000-8000-000000000006", result.SessionId, "Session id.");
                SharedAssert.Equal(1, result.Events.Count(e => e.Kind == CliAgentEventKind.Started), "Hook events before init do not start the run.");
                SharedAssert.Equal("Running it now.", string.Join("", result.Events.Where(e => e.Kind == CliAgentEventKind.TextDelta).Select(e => e.Text)), "Without partial deltas the assistant text is used; subagent text is ignored.");
                CliAgentEvent? denied = result.Events.FirstOrDefault(e => e.Kind == CliAgentEventKind.ToolResult);
                SharedAssert.True(denied != null && denied.IsError && (denied.ToolOutput ?? "").Contains("don't ask mode", StringComparison.Ordinal), "A denied tool result is an error result with the array content flattened.");
                SharedAssert.Equal("Bash", denied!.ToolName, "Denied tool name.");
                SharedAssert.True(result.Events.Any(e => e.Kind == CliAgentEventKind.Notice && e.IsError && (e.Text ?? "").StartsWith("Permission denied: Bash", StringComparison.Ordinal)), "permission_denials become error notices.");
                SharedAssert.True(result.Events.Any(e => e.Kind == CliAgentEventKind.Notice && !e.IsError && (e.Text ?? "").StartsWith("Conversation compacted (auto)", StringComparison.Ordinal)), "compact_boundary becomes a notice.");
            }
        }

        private static async Task RunRecordedReplayAsync(CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = CliAgentCaseKit.Attach(Profile, "claudecode-error-exit");
            using (fake)
            using (client)
            {
                CliAgentResult result = await client.RunAsync(new CliAgentRequest("hi") { Model = "no-such-model-xyz" }, token).ConfigureAwait(false);
                SharedAssert.Equal(CliFailureKind.ProviderError, result.FailureKind, "Unknown model is a provider error.");
                SharedAssert.Equal("4cf3d629-4307-49cd-8cc4-2dae3b285041", result.SessionId, "Session id is reported even when the turn fails.");
                SharedAssert.Equal(1, result.ExitCode, "Exit code.");
                SharedAssert.Equal("no-such-model-xyz", result.Model, "The model from system/init is reported.");
                SharedAssert.Equal(0m, result.CostUsd, "A zero cost is reported as zero, not null.");
                SharedAssert.False(result.Events.Any(e => e.Kind == CliAgentEventKind.TextDelta), "The synthetic error message is not answer text.");
                SharedAssert.True(result.Events.Any(e => e.Kind == CliAgentEventKind.Notice && e.IsError && (e.Text ?? "").StartsWith("There's an issue with the selected model", StringComparison.Ordinal)), "The synthetic error message is an error notice.");

                fake.UseScenario(client, "claudecode-unauthenticated");
                result = await client.RunAsync(new CliAgentRequest("hi"), token).ConfigureAwait(false);
                SharedAssert.Equal(CliFailureKind.NotAuthenticated, result.FailureKind, "authentication_failed is NotAuthenticated.");
                SharedAssert.True((result.Error ?? "").Contains("(authentication_failed)", StringComparison.Ordinal), "The error code is part of the error: " + result.Error);
                CliConnectivityResult connectivity = await client.ValidateConnectivityAsync(token).ConfigureAwait(false);
                SharedAssert.Equal(CliConnectivityStatus.NotAuthenticated, connectivity.Status, "auth status exit 1 is NotAuthenticated.");
                SharedAssert.Equal("auth | status | --json", string.Join(" | ", fake.Arguments), "Login check arguments.");

                fake.UseScenario(client, "claudecode-happy");
                connectivity = await client.ValidateConnectivityAsync(token).ConfigureAwait(false);
                SharedAssert.Equal("Logged in using claude.ai (firstParty).", connectivity.Message, "Login message names the method and provider only.");
                SharedAssert.Equal("2.1.312 (Claude Code)", connectivity.Version, "Version.");

                fake.UseScenario(client, "claudecode-rate-limited");
                result = await client.RunAsync(new CliAgentRequest("hi"), token).ConfigureAwait(false);
                SharedAssert.Equal(CliFailureKind.RateLimited, result.FailureKind, "rate_limit is RateLimited.");
                SharedAssert.True(result.Events.Any(e => e.Kind == CliAgentEventKind.Notice && e.IsError && (e.Text ?? "") == "Rate limit rejected (five_hour)."), "A rejected rate_limit_event is an error notice.");
            }
        }

        private static async Task RunPermissionFlagsAsync(CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = CliAgentCaseKit.Attach(Profile, "claudecode-happy");
            using (fake)
            using (client)
            {
                foreach (CliPermissionMode mode in Enum.GetValues(typeof(CliPermissionMode)))
                {
                    CliAgentResult result = await client.RunAsync(new CliAgentRequest("hello") { PermissionMode = mode }, token).ConfigureAwait(false);
                    SharedAssert.True(result.Success, mode + ": run succeeds: " + result.Error);
                    List<string> args = fake.Arguments;
                    SharedAssert.False(args.Contains("--dangerously-skip-permissions") || args.Contains("--allow-dangerously-skip-permissions"), mode + ": never passes the skip-permissions flags.");
                    SharedAssert.Equal(mode == CliPermissionMode.Unrestricted, args.Contains("bypassPermissions"), mode + ": bypassPermissions only for Unrestricted.");
                    if (mode == CliPermissionMode.Restricted || mode == CliPermissionMode.ReadOnly)
                        SharedAssert.Equal("dontAsk", args[args.IndexOf("--permission-mode") + 1], mode + ": nothing that would prompt is allowed.");
                }
            }
        }
    }
}
