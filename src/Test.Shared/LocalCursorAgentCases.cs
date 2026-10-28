namespace Test.Shared
{
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using Touchstone.Core;

    /// <summary>
    /// Fake-CLI cases for <see cref="CursorAgentClient"/>: the shared harness kit plus model listing, session resume,
    /// the partial-output de-duplication, stderr-reported failures, and the permission mapping. The Cursor CLI was not
    /// installed when these were written, so every scenario is constructed from Cursor's documentation and the published
    /// CLI package rather than recorded (each scenario header says so).
    /// </summary>
    internal static class LocalCursorAgentCases
    {
        private const string SessionId = "c6b62c6f-7ead-4fd6-9922-e952131177ff";

        private static readonly List<string> _Print = new List<string> { "-p", "--output-format", "stream-json", "--stream-partial-output" };

        public static CliAgentProfile Profile
        {
            get
            {
                return new CliAgentProfile
                {
                    Id = "cursor",
                    ExecutableName = "cursor-agent",
                    Create = () => new CursorAgentClient(),
                    HappyScenario = "cursor-happy",
                    ErrorExitScenario = "cursor-error-exit",
                    StderrOnlyScenario = "cursor-stderr-only",
                    MalformedScenario = "cursor-malformed",
                    SlowScenario = "cursor-slow",
                    HugeScenario = "cursor-huge",
                    UnauthenticatedScenario = "cursor-unauthenticated",
                    RateLimitedScenario = "cursor-rate-limited",
                    HappyText = "I will read a.txt.It says hello.",
                    HappySessionId = SessionId,
                    HappyReasoningContains = "Reading a.txt answers the question.",
                    HappyToolName = "readToolCall",
                    HappyUsage = new TokenUsage { PromptTokens = 7, CachedPromptTokens = 147695, CacheCreationTokens = 39331, CompletionTokens = 548 },
                    HappyCostUsd = null,
                    HappyModel = "Claude 4.6 Sonnet",
                    ErrorExitMessageContains = "ConnectError: [invalid_argument] Model 'no-such-model-xyz' is not available for this account.",
                    StderrOnlyMessageContains = "unknown option '--bogus'",
                    HasAuthenticationStatus = true,
                    PromptOnStandardInput = true,
                    ArgvCases = new List<CliAgentProfile.ArgvCase>
                    {
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "default (Restricted)",
                            Expected = Print("--mode", "ask", "--sandbox", "enabled"),
                            ExpectedStandardInput = "hello",
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "ReadOnly with model",
                            Request = r => { r.PermissionMode = CliPermissionMode.ReadOnly; r.Model = "sonnet-4.6-thinking"; },
                            Expected = Print("--mode", "ask", "--sandbox", "enabled", "--model", "sonnet-4.6-thinking"),
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "WorkspaceWrite from the client (default agent mode, sandboxed, never --force)",
                            Client = c => { c.PermissionMode = CliPermissionMode.WorkspaceWrite; c.Model = "client-model"; },
                            Expected = Print("--sandbox", "enabled", "--model", "client-model"),
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "Unrestricted",
                            Request = r => r.PermissionMode = CliPermissionMode.Unrestricted,
                            Expected = Print("--force", "--sandbox", "disabled"),
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "resume by session id",
                            Request = r => r.SessionId = "abc-123",
                            Expected = Print("--mode", "ask", "--sandbox", "enabled", "--resume", "abc-123"),
                            ExpectedStandardInput = "hello",
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "system prompt is prepended (Cursor has no documented system-prompt option)",
                            Request = r => r.SystemPrompt = "Be terse.",
                            Expected = Print("--mode", "ask", "--sandbox", "enabled"),
                            ExpectedStandardInput = "System instructions:\nBe terse.\n\nRequest:\nhello",
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "ExtraArguments are appended after the client's arguments",
                            Request = r => r.ExtraArguments = new Dictionary<string, string?> { { "--trust", null }, { "--workspace", "/work/project" } },
                            Expected = Print("--mode", "ask", "--sandbox", "enabled", "--trust", "--workspace", "/work/project"),
                        },
                    },
                };
            }
        }

        public static IEnumerable<TestCaseDescriptor> Create(string suiteId)
        {
            List<TestCaseDescriptor> cases = CliAgentCaseKit.Create(suiteId, Profile);
            cases.Add(CliAgentCaseKit.Case(suiteId, "cli_cursor_list_models", "cursor: ListModelsAsync parses cursor-agent models", RunListModelsAsync));
            cases.Add(CliAgentCaseKit.Case(suiteId, "cli_cursor_session_history", "cursor: a session id in history provider state resumes; plain history is replayed", RunSessionHistoryAsync));
            cases.Add(CliAgentCaseKit.Case(suiteId, "cli_cursor_partial_output", "cursor: partial-output flushes are not duplicated; retry, reconnect, task and rejected tool events map", RunPartialOutputAsync));
            cases.Add(CliAgentCaseKit.Case(suiteId, "cli_cursor_stderr_failures", "cursor: errors written to stderr after the session started are provider failures without warnings", RunStderrFailuresAsync));
            cases.Add(CliAgentCaseKit.Case(suiteId, "cli_cursor_permissions", "cursor: only Unrestricted passes --force; read-only modes use ask mode", RunPermissionsAsync));
            return cases;
        }

        private static List<string> Print(params string[] rest)
        {
            List<string> args = new List<string>(_Print);
            args.AddRange(rest);
            return args;
        }

        private static async Task RunListModelsAsync(CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = CliAgentCaseKit.Attach(Profile, "cursor-happy");
            using (fake)
            using (client)
            {
                CursorAgentClient cursor = (CursorAgentClient)client;
                List<ModelInformation> models = new List<ModelInformation>();
                await foreach (ModelInformation model in cursor.ListModelsAsync(token).ConfigureAwait(false)) models.Add(model);

                SharedAssert.Equal(4, models.Count, "Four models; heading, blank and tip lines are skipped.");
                SharedAssert.Equal("auto", models[0].Name, "Id is the name.");
                SharedAssert.Equal("Auto", models[0].DisplayName, "Display name.");
                SharedAssert.Equal("composer-2.5", models[1].Name, "Second id.");
                SharedAssert.Equal("Composer 2.5", models[1].DisplayName, "The (default) marker is not part of the display name.");
                SharedAssert.Equal("true", models[1].Metadata["default"], "Default marker.");
                SharedAssert.False(models[1].Metadata.ContainsKey("current"), "Not current.");
                SharedAssert.Equal("Claude 4.6 Sonnet (Thinking)", models[2].DisplayName, "A parenthesized display name survives.");
                SharedAssert.Equal("true", models[2].Metadata["current"], "Current marker.");
                SharedAssert.Equal(0, models[3].Metadata.Count, "No markers.");
                SharedAssert.Equal("cursor", models[0].OwnedBy, "Owner.");
                SharedAssert.Equal("models", string.Join(" | ", fake.Arguments), "Arguments.");

                fake.UseScenario(client, "cursor-stderr-only");
                CliAgentException? failure = null;
                try
                {
                    await foreach (ModelInformation model in cursor.ListModelsAsync(token).ConfigureAwait(false)) { }
                }
                catch (CliAgentException ex)
                {
                    failure = ex;
                }
                SharedAssert.NotNull(failure, "A failing model list throws CliAgentException.");
                SharedAssert.Equal(CliFailureKind.Crashed, failure!.FailureKind, "Failure kind.");
                SharedAssert.Equal(1, failure.ExitCode, "Exit code.");

                fake.UseScenario(client, "cursor-unauthenticated");
                failure = null;
                try
                {
                    await foreach (ModelInformation model in cursor.ListModelsAsync(token).ConfigureAwait(false)) { }
                }
                catch (CliAgentException ex)
                {
                    failure = ex;
                }
                SharedAssert.Equal(CliFailureKind.NotAuthenticated, failure?.FailureKind, "A missing login is NotAuthenticated.");

                cursor.Executable = "polyprompt-missing-cursor-agent";
                failure = null;
                try
                {
                    await foreach (ModelInformation model in cursor.ListModelsAsync(token).ConfigureAwait(false)) { }
                }
                catch (CliAgentException ex)
                {
                    failure = ex;
                }
                SharedAssert.Equal(CliFailureKind.NotInstalled, failure?.FailureKind, "Missing CLI is NotInstalled.");
            }
        }

        private static async Task RunSessionHistoryAsync(CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = CliAgentCaseKit.Attach(Profile, "cursor-happy");
            using (fake)
            using (client)
            {
                CliAgentResult first = await client.RunAsync(new CliAgentRequest("first"), token).ConfigureAwait(false);
                SharedAssert.Equal(SessionId, first.SessionId, "First turn session id.");

                ChatMessage assistant = ChatMessage.Assistant(first.Text);
                assistant.ProviderState = first.ProviderState;
                List<ChatMessage> history = new List<ChatMessage> { ChatMessage.User("first"), assistant };

                CliAgentResult second = await client.RunAsync(new CliAgentRequest("second") { History = history }, token).ConfigureAwait(false);
                SharedAssert.True(second.Success, "Second turn succeeds: " + second.Error);
                List<string> args = fake.Arguments;
                int resume = args.IndexOf("--resume");
                SharedAssert.True(resume >= 0, "History with a session id resumes. Args: " + string.Join(" | ", args));
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

        private static async Task RunPartialOutputAsync(CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = CliAgentCaseKit.Attach(Profile, "cursor-replay");
            using (fake)
            using (client)
            {
                CliAgentResult result = await client.RunAsync(new CliAgentRequest("hi"), token).ConfigureAwait(false);
                SharedAssert.True(result.Success, "Replay succeeds: " + result.Error);
                SharedAssert.Equal("Checking the repo.Done.", result.Text, "Final text comes from the result event.");
                string streamed = string.Concat(result.Events.Where(e => e.Kind == CliAgentEventKind.TextDelta).Select(e => e.Text));
                SharedAssert.Equal("Checking the repo.Done.", streamed, "Streamed deltas carry each piece of text once (flushes are skipped).");
                SharedAssert.Equal(null, result.Usage, "No usage reported, none invented.");
                SharedAssert.Equal(null, result.CostUsd, "No cost.");
                SharedAssert.Equal("GPT-5.4 High", result.Model, "Model display name from system init.");

                List<CliAgentEvent> errorNotices = result.Events.Where(e => e.Kind == CliAgentEventKind.Notice && e.IsError).ToList();
                SharedAssert.Equal(2, errorNotices.Count, "Retry and reconnect become error notices.");
                SharedAssert.True((errorNotices[0].Text ?? "").Contains("attempt 1", StringComparison.Ordinal), "Retry attempt: " + errorNotices[0].Text);
                SharedAssert.Equal("retry.starting", errorNotices[0].ProviderEventType, "Provider event type includes the subtype.");
                SharedAssert.True(result.Events.Any(e => e.Kind == CliAgentEventKind.Notice && !e.IsError && e.Text == "Indexing (completed)"), "Task notifications become notices.");

                CliAgentEvent? use = result.Events.FirstOrDefault(e => e.Kind == CliAgentEventKind.ToolUse);
                CliAgentEvent? done = result.Events.FirstOrDefault(e => e.Kind == CliAgentEventKind.ToolResult);
                SharedAssert.NotNull(use, "ToolUse.");
                SharedAssert.Equal("grep", use!.ToolName, "Function tools use their name.");
                SharedAssert.Equal("{\"pattern\":\"TODO\"}", use.ToolInput, "Function arguments are the input.");
                SharedAssert.NotNull(done, "ToolResult.");
                SharedAssert.True(done!.IsError, "A non-success tool result is an error.");
                SharedAssert.Equal("rejected: Denied in print mode", done.ToolOutput, "Rejected reason.");
                SharedAssert.Equal("call_02", done.ToolCallId, "Call id.");
            }
        }

        private static async Task RunStderrFailuresAsync(CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = CliAgentCaseKit.Attach(Profile, "cursor-error-exit");
            using (fake)
            using (client)
            {
                CliAgentResult result = await client.RunAsync(new CliAgentRequest("hi") { Model = "no-such-model-xyz" }, token).ConfigureAwait(false);
                SharedAssert.Equal(CliFailureKind.ProviderError, result.FailureKind, "A stderr error after the session started is a provider error.");
                SharedAssert.Equal("ConnectError: [invalid_argument] Model 'no-such-model-xyz' is not available for this account.", result.Error, "Warnings are dropped from the message.");
                SharedAssert.Equal("6f1d2b0e-3a4c-4d5e-8f60-718293a4b5c6", result.SessionId, "Session id is reported even when the turn fails.");
                SharedAssert.Equal(1, result.ExitCode, "Exit code.");
                SharedAssert.Equal("Auto", result.Model, "The model reported by system init wins over the requested one.");

                fake.UseScenario(client, "cursor-rate-limited");
                result = await client.RunAsync(new CliAgentRequest("hi"), token).ConfigureAwait(false);
                SharedAssert.Equal(CliFailureKind.RateLimited, result.FailureKind, "resource_exhausted is RateLimited.");
                SharedAssert.True((result.Error ?? "").Contains("usage limit", StringComparison.Ordinal), "Rate-limit message: " + result.Error);

                fake.UseScenario(client, "cursor-unauthenticated");
                result = await client.RunAsync(new CliAgentRequest("hi"), token).ConfigureAwait(false);
                SharedAssert.Equal(CliFailureKind.NotAuthenticated, result.FailureKind, "Authentication required is NotAuthenticated.");
                SharedAssert.Equal(null, result.SessionId, "No session started.");
                CliConnectivityResult connectivity = await client.ValidateConnectivityAsync(token).ConfigureAwait(false);
                SharedAssert.Equal("Not logged in", connectivity.Message, "The status message is reported.");
                SharedAssert.Equal("status | --format | json", string.Join(" | ", fake.Arguments), "Status arguments.");

                fake.UseScenario(client, "cursor-happy");
                connectivity = await client.ValidateConnectivityAsync(token).ConfigureAwait(false);
                SharedAssert.Equal("2026.10.22-27dcf5f", connectivity.Version, "Version.");
                SharedAssert.Equal("Logged in (user details not available)", connectivity.Message, "Logged-in status message.");
            }
        }

        private static async Task RunPermissionsAsync(CancellationToken token)
        {
            foreach (CliPermissionMode mode in new[] { CliPermissionMode.Restricted, CliPermissionMode.ReadOnly, CliPermissionMode.WorkspaceWrite, CliPermissionMode.Unrestricted })
            {
                (FakeCli fake, CliAgentClientBase client) = CliAgentCaseKit.Attach(Profile, "cursor-happy");
                using (fake)
                using (client)
                {
                    CliAgentResult result = await client.RunAsync(new CliAgentRequest("hello") { PermissionMode = mode }, token).ConfigureAwait(false);
                    SharedAssert.True(result.Success, mode + " runs: " + result.Error);
                    List<string> args = fake.Arguments;
                    bool bypass = args.Contains("--force") || args.Contains("-f") || args.Contains("--yolo") || args.Contains("--approve-mcps") || args.Contains("--trust");
                    SharedAssert.Equal(mode == CliPermissionMode.Unrestricted, bypass, mode + " bypass flags. Args: " + string.Join(" | ", args));
                    bool ask = args.Contains("--mode") && args[args.IndexOf("--mode") + 1] == "ask";
                    SharedAssert.Equal(mode == CliPermissionMode.Restricted || mode == CliPermissionMode.ReadOnly, ask, mode + " ask mode.");
                    SharedAssert.False(args.Contains("hello"), "The prompt is never an argument.");
                }
            }
        }
    }
}
