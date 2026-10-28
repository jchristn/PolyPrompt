namespace Test.Shared
{
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using Touchstone.Core;

    /// <summary>
    /// Fake-CLI cases for <see cref="CodexAgentClient"/>: the shared harness kit plus model listing, session resume, and
    /// the permission mapping.
    /// </summary>
    internal static class LocalCodexAgentCases
    {
        private const string SessionId = "01a18335-b272-7883-8861-31ba989c1ddf";

        public static CliAgentProfile Profile
        {
            get
            {
                return new CliAgentProfile
                {
                    Id = "codex",
                    ExecutableName = "codex",
                    Create = () => new CodexAgentClient(),
                    HappyScenario = "codex-happy",
                    ErrorExitScenario = "codex-error-exit",
                    StderrOnlyScenario = "codex-stderr-only",
                    MalformedScenario = "codex-malformed",
                    SlowScenario = "codex-slow",
                    HugeScenario = "codex-huge",
                    UnauthenticatedScenario = "codex-unauthenticated",
                    RateLimitedScenario = "codex-rate-limited",
                    HappyText = "`a.txt`",
                    HappySessionId = SessionId,
                    HappyReasoningContains = "Listing files",
                    HappyToolName = "command_execution",
                    HappyUsage = new TokenUsage { PromptTokens = 37907, CachedPromptTokens = 32000, CacheCreationTokens = 0, CompletionTokens = 75, ReasoningTokens = 12 },
                    HappyCostUsd = null,
                    ErrorExitMessageContains = "model is not supported when using Codex with a ChatGPT account. (model_not_found, status 400)",
                    StderrOnlyMessageContains = "unexpected argument '--bogus' found",
                    HasAuthenticationStatus = true,
                    PromptOnStandardInput = true,
                    ArgvCases = new List<CliAgentProfile.ArgvCase>
                    {
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "default (Restricted)",
                            Expected = new List<string> { "exec", "--json", "-c", "sandbox_mode=\"read-only\"", "-c", "approval_policy=\"never\"", "--skip-git-repo-check", "-" },
                            ExpectedStandardInput = "hello",
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "ReadOnly with model",
                            Request = r => { r.PermissionMode = CliPermissionMode.ReadOnly; r.Model = "gpt-6.1-sol"; },
                            Expected = new List<string> { "exec", "--json", "-c", "sandbox_mode=\"read-only\"", "-c", "approval_policy=\"never\"", "--skip-git-repo-check", "-m", "gpt-6.1-sol", "-" },
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "WorkspaceWrite from the client",
                            Client = c => { c.PermissionMode = CliPermissionMode.WorkspaceWrite; c.Model = "client-model"; },
                            Expected = new List<string> { "exec", "--json", "-c", "sandbox_mode=\"workspace-write\"", "-c", "approval_policy=\"never\"", "-m", "client-model", "-" },
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "Unrestricted",
                            Request = r => r.PermissionMode = CliPermissionMode.Unrestricted,
                            Expected = new List<string> { "exec", "--json", "--dangerously-bypass-approvals-and-sandbox", "-" },
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "resume by session id",
                            Request = r => r.SessionId = "abc-123",
                            Expected = new List<string> { "exec", "resume", "--json", "-c", "sandbox_mode=\"read-only\"", "-c", "approval_policy=\"never\"", "--skip-git-repo-check", "abc-123", "-" },
                            ExpectedStandardInput = "hello",
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "system prompt is prepended (Codex has no system-prompt option)",
                            Request = r => r.SystemPrompt = "Be terse.",
                            Expected = new List<string> { "exec", "--json", "-c", "sandbox_mode=\"read-only\"", "-c", "approval_policy=\"never\"", "--skip-git-repo-check", "-" },
                            ExpectedStandardInput = "System instructions:\nBe terse.\n\nRequest:\nhello",
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "ExtraArguments are appended after the client's arguments",
                            Request = r => r.ExtraArguments = new Dictionary<string, string?> { { "--ephemeral", null } },
                            Expected = new List<string> { "exec", "--json", "-c", "sandbox_mode=\"read-only\"", "-c", "approval_policy=\"never\"", "--skip-git-repo-check", "-", "--ephemeral" },
                        },
                    },
                };
            }
        }

        public static IEnumerable<TestCaseDescriptor> Create(string suiteId)
        {
            List<TestCaseDescriptor> cases = CliAgentCaseKit.Create(suiteId, Profile);
            cases.Add(CliAgentCaseKit.Case(suiteId, "cli_codex_list_models", "codex: ListModelsAsync parses codex debug models", RunListModelsAsync));
            cases.Add(CliAgentCaseKit.Case(suiteId, "cli_codex_session_history", "codex: a session id in history provider state resumes; plain history is replayed", RunSessionHistoryAsync));
            cases.Add(CliAgentCaseKit.Case(suiteId, "cli_codex_recorded_replay", "codex: recorded JSONL replays without a live CLI and ignores non-fatal error items", RunRecordedReplayAsync));
            return cases;
        }

        private static async Task RunListModelsAsync(CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = CliAgentCaseKit.Attach(Profile, "codex-happy");
            using (fake)
            using (client)
            {
                CodexAgentClient codex = (CodexAgentClient)client;
                List<ModelInformation> models = new List<ModelInformation>();
                await foreach (ModelInformation model in codex.ListModelsAsync(token).ConfigureAwait(false)) models.Add(model);

                SharedAssert.Equal(2, models.Count, "Two models.");
                SharedAssert.Equal("gpt-6.1-sol", models[0].Name, "Slug is the name.");
                SharedAssert.Equal("GPT-6.1-Sol", models[0].DisplayName, "Display name.");
                SharedAssert.Equal("list", models[0].Metadata["visibility"], "Visibility metadata.");
                SharedAssert.Equal("low,high", models[0].Metadata["supported_reasoning_levels"], "Reasoning levels metadata.");
                SharedAssert.Equal("debug | models", string.Join(" | ", fake.Arguments), "Arguments.");

                fake.UseScenario(client, "codex-stderr-only");
                CliAgentException? failure = null;
                try
                {
                    await foreach (ModelInformation model in codex.ListModelsAsync(token).ConfigureAwait(false)) { }
                }
                catch (CliAgentException ex)
                {
                    failure = ex;
                }
                SharedAssert.NotNull(failure, "A failing model list throws CliAgentException.");
                SharedAssert.Equal(CliFailureKind.Crashed, failure!.FailureKind, "Failure kind.");
                SharedAssert.Equal(2, failure.ExitCode, "Exit code.");

                codex.Executable = "polyprompt-missing-codex";
                failure = null;
                try
                {
                    await foreach (ModelInformation model in codex.ListModelsAsync(token).ConfigureAwait(false)) { }
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
            (FakeCli fake, CliAgentClientBase client) = CliAgentCaseKit.Attach(Profile, "codex-happy");
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
                SharedAssert.Equal("resume", args[1], "History with a session id resumes. Args: " + string.Join(" | ", args));
                SharedAssert.True(args.Contains(SessionId), "The session id from provider state is passed.");
                SharedAssert.Equal("second", fake.StandardInput, "Only the new prompt is sent when resuming.");

                List<ChatMessage> plain = new List<ChatMessage> { ChatMessage.System("Be terse."), ChatMessage.User("What is 2+2?"), ChatMessage.Assistant("4") };
                CliAgentResult replayed = await client.RunAsync(new CliAgentRequest("And 3+3?") { History = plain }, token).ConfigureAwait(false);
                SharedAssert.True(replayed.Success, "Replayed turn succeeds: " + replayed.Error);
                SharedAssert.False(fake.Arguments.Contains("resume"), "Plain history does not resume.");
                SharedAssert.Equal(
                    "System instructions:\nBe terse.\n\nConversation so far:\n\nUser: What is 2+2?\n\nAssistant: 4\n\nCurrent request:\nAnd 3+3?",
                    fake.StandardInput,
                    "Plain history is replayed into the prompt, with system messages as instructions.");
            }
        }

        private static async Task RunRecordedReplayAsync(CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = CliAgentCaseKit.Attach(Profile, "codex-error-exit");
            using (fake)
            using (client)
            {
                CliAgentResult result = await client.RunAsync(new CliAgentRequest("hi") { Model = "no-such-model-xyz" }, token).ConfigureAwait(false);
                SharedAssert.Equal("01a18336-0d5d-7963-8a6f-1e358f364c06", result.SessionId, "Session id is reported even when the turn fails.");
                SharedAssert.True(result.Events.Any(e => e.Kind == CliAgentEventKind.Notice && e.IsError && (e.Text ?? "").Contains("Model metadata", StringComparison.Ordinal)), "Non-fatal error items become error notices.");
                SharedAssert.Equal(1, result.ExitCode, "Exit code.");
                SharedAssert.Equal("no-such-model-xyz", result.Model, "Requested model is reported when the harness reports none.");

                fake.UseScenario(client, "codex-unauthenticated");
                result = await client.RunAsync(new CliAgentRequest("hi"), token).ConfigureAwait(false);
                SharedAssert.Equal(CliFailureKind.NotAuthenticated, result.FailureKind, "401 is NotAuthenticated.");
                SharedAssert.True(result.Events.Any(e => e.Kind == CliAgentEventKind.Notice && (e.Text ?? "").StartsWith("Reconnecting", StringComparison.Ordinal)), "Reconnect errors are notices, not failures.");

                fake.UseScenario(client, "codex-garbage");
                result = await client.RunAsync(new CliAgentRequest("hi"), token).ConfigureAwait(false);
                SharedAssert.Equal(CliFailureKind.InvalidOutput, result.FailureKind, "Exit 0 with nothing parseable is InvalidOutput.");
                SharedAssert.Equal(2, result.MalformedLines, "Both lines are malformed.");
            }
        }
    }
}
