namespace Test.Shared
{
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using Touchstone.Core;

    /// <summary>
    /// Fake-CLI cases for <see cref="PiAgentClient"/>: the shared harness kit plus the RPC lifecycle, model listing, session
    /// resume, connectivity, input rejection, and less common RPC records.
    /// </summary>
    internal static class LocalPiAgentCases
    {
        private const string SessionId = "01a18352-0000-7000-8000-000000000001";
        private const string StateCommand = "{\"id\":\"polyprompt-state\",\"type\":\"get_state\"}\n";
        private const string ReadOnlyTools = "read,grep,find,ls";

        public static CliAgentProfile Profile
        {
            get
            {
                return new CliAgentProfile
                {
                    Id = "pi",
                    ExecutableName = "pi",
                    Create = () => new PiAgentClient(),
                    HappyScenario = "pi-happy",
                    ErrorExitScenario = "pi-error-exit",
                    StderrOnlyScenario = "pi-stderr-only",
                    MalformedScenario = "pi-malformed",
                    SlowScenario = "pi-slow",
                    HugeScenario = "pi-huge",
                    UnauthenticatedScenario = "pi-unauthenticated",
                    RateLimitedScenario = "pi-rate-limited",
                    HappyText = "PONG",
                    HappySessionId = SessionId,
                    HappyReasoningContains = "use the ls tool once",
                    HappyToolName = "ls",
                    HappyUsage = new TokenUsage { PromptTokens = 5086, CompletionTokens = 128, CachedPromptTokens = 1024, CacheCreationTokens = 512, ReasoningTokens = 83, TotalTokens = 6750 },
                    HappyCostUsd = 0.005726m,
                    HappyModel = "anthropic/claude-haiku-4-5",
                    ErrorExitMessageContains = "prompt is too long: 215000 tokens > 200000 maximum (invalid_request_error, status 400)",
                    StderrOnlyMessageContains = "Managed Pi executable is missing",
                    HasAuthenticationStatus = true,
                    PromptOnStandardInput = true,
                    ArgvCases = new List<CliAgentProfile.ArgvCase>
                    {
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "default (Restricted)",
                            Expected = new List<string> { "--mode", "rpc", "--tools", ReadOnlyTools, "--no-mcp", "--no-approve" },
                            ExpectedStandardInput = Stdin("hello"),
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "ReadOnly with model",
                            Request = r => { r.PermissionMode = CliPermissionMode.ReadOnly; r.Model = "anthropic/claude-haiku-4-5"; },
                            Expected = new List<string> { "--mode", "rpc", "--model", "anthropic/claude-haiku-4-5", "--tools", ReadOnlyTools, "--no-mcp", "--no-approve" },
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "WorkspaceWrite from the client",
                            Client = c => { c.PermissionMode = CliPermissionMode.WorkspaceWrite; c.Model = "client-model"; },
                            Expected = new List<string> { "--mode", "rpc", "--model", "client-model", "--tools", "read,grep,find,ls,edit,write", "--no-mcp", "--no-approve" },
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "Unrestricted",
                            Request = r => r.PermissionMode = CliPermissionMode.Unrestricted,
                            Expected = new List<string> { "--mode", "rpc" },
                            ExpectedStandardInput = Stdin("hello"),
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "resume by session id",
                            Request = r => r.SessionId = "abc-123",
                            Expected = new List<string> { "--mode", "rpc", "--session", "abc-123", "--tools", ReadOnlyTools, "--no-mcp", "--no-approve" },
                            ExpectedStandardInput = Stdin("hello"),
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "system prompt is sent ahead of the prompt (not --append-system-prompt, which reads existing paths as files)",
                            Request = r => r.SystemPrompt = "Be terse.",
                            Expected = new List<string> { "--mode", "rpc", "--tools", ReadOnlyTools, "--no-mcp", "--no-approve" },
                            ExpectedStandardInput = Stdin("System instructions:\\nBe terse.\\n\\nRequest:\\nhello"),
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "ExtraArguments are appended after the client's arguments",
                            Request = r => r.ExtraArguments = new Dictionary<string, string?> { { "--thinking", "low" }, { "--no-session", null } },
                            Expected = new List<string> { "--mode", "rpc", "--tools", ReadOnlyTools, "--no-mcp", "--no-approve", "--thinking", "low", "--no-session" },
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "quotes, backslashes, and control characters in the prompt are JSON-escaped on one line",
                            Request = r => r.Prompt = "say \"hi\"\\n\tthen\nstop & <go> 2+2",
                            Expected = new List<string> { "--mode", "rpc", "--tools", ReadOnlyTools, "--no-mcp", "--no-approve" },
                            ExpectedStandardInput = Stdin("say \\\"hi\\\"\\\\n\\tthen\\nstop & <go> 2+2"),
                        },
                    },
                };
            }
        }

        public static IEnumerable<TestCaseDescriptor> Create(string suiteId)
        {
            List<TestCaseDescriptor> cases = CliAgentCaseKit.Create(suiteId, Profile);
            cases.Add(CliAgentCaseKit.Case(suiteId, "cli_pi_rpc_lifecycle", "pi: get_state and prompt go on stdin, stdin closes when the run settles, a lingering process is killed", RunRpcLifecycleAsync));
            cases.Add(CliAgentCaseKit.Case(suiteId, "cli_pi_list_models", "pi: ListModelsAsync parses pi --list-models", RunListModelsAsync));
            cases.Add(CliAgentCaseKit.Case(suiteId, "cli_pi_session_history", "pi: a session id in history provider state resumes with --session; plain history is replayed", RunSessionHistoryAsync));
            cases.Add(CliAgentCaseKit.Case(suiteId, "cli_pi_connectivity", "pi: connectivity reads the provider from get_state and runs pi auth check", RunConnectivityAsync));
            cases.Add(CliAgentCaseKit.Case(suiteId, "cli_pi_outcomes", "pi: rejected input, aborted runs, and handled prompts map to distinct outcomes", RunOutcomesAsync));
            cases.Add(CliAgentCaseKit.Case(suiteId, "cli_pi_recorded_replay", "pi: retries, extension records, compaction, and failed tools replay without a live CLI", RunRecordedReplayAsync));
            return cases;
        }

        private static string Stdin(string escapedMessage)
        {
            // Two command lines, then the empty line the fake records when its final readline reaches end of file.
            return StateCommand + "{\"id\":\"polyprompt-prompt\",\"type\":\"prompt\",\"message\":\"" + escapedMessage + "\"}\n\n";
        }

        private static async Task RunRpcLifecycleAsync(CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = CliAgentCaseKit.Attach(Profile, "pi-happy");
            using (fake)
            using (client)
            {
                CliAgentResult result = await client.RunAsync(new CliAgentRequest("hello"), token).ConfigureAwait(false);
                SharedAssert.True(result.Success, "Run should succeed: " + result.Error);
                SharedAssert.Equal(Stdin("hello"), fake.StandardInput, "get_state, then prompt, then end of file once the run settled.");
                SharedAssert.Equal(0, result.ExitCode, "Pi exits by itself once stdin closes.");
                SharedAssert.True(result.OverallRuntimeMs < 4000, "Closing stdin ends the process without waiting for the kill grace period (" + result.OverallRuntimeMs + " ms).");
                CliAgentEvent started = result.Events.First(e => e.Kind == CliAgentEventKind.Started);
                SharedAssert.Equal("response", started.ProviderEventType, "The session id comes from the get_state response.");
                SharedAssert.Equal(1, result.Events.Count(e => e.Kind == CliAgentEventKind.Started), "One Started event.");
                CliAgentEvent tool = result.Events.First(e => e.Kind == CliAgentEventKind.ToolResult);
                SharedAssert.Equal("a.txt", tool.ToolOutput, "Tool output is the result's text content.");
                SharedAssert.False(tool.IsError, "Successful tool.");
                SharedAssert.Equal("toolu_01AAAAAAAAAAAAAAAAAAAAAA", tool.ToolCallId, "Tool call id.");
                SharedAssert.Equal("{}", result.Events.First(e => e.Kind == CliAgentEventKind.ToolUse).ToolInput, "Tool input is the rendered args.");
                SharedAssert.Equal(2, result.Events.Count(e => e.Kind == CliAgentEventKind.TextDelta), "Each text_delta is a TextDelta.");

                fake.UseScenario(client, "pi-linger");
                result = await client.RunAsync(new CliAgentRequest("linger"), token).ConfigureAwait(false);
                SharedAssert.True(result.Success, "A settled run keeps its outcome when the process has to be killed: " + result.Error);
                SharedAssert.Equal("PONG", result.Text, "Text.");
                SharedAssert.True(result.OverallRuntimeMs >= 4000 && result.OverallRuntimeMs < 20000, "The process was killed after the grace period (" + result.OverallRuntimeMs + " ms).");
                int? child = fake.ChildProcessId;
                SharedAssert.True(child.HasValue, "The scenario started a child.");
                SharedAssert.True(await FakeCli.WaitForProcessExitAsync(child!.Value).ConfigureAwait(false), "The lingering tree was killed.");
            }
        }

        private static async Task RunListModelsAsync(CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = CliAgentCaseKit.Attach(Profile, "pi-happy");
            using (fake)
            using (client)
            {
                PiAgentClient pi = (PiAgentClient)client;
                List<ModelInformation> models = new List<ModelInformation>();
                await foreach (ModelInformation model in pi.ListModelsAsync(token: token).ConfigureAwait(false)) models.Add(model);

                SharedAssert.Equal(2, models.Count, "Two models.");
                SharedAssert.Equal("anthropic/claude-haiku-4-5", models[0].Name, "Name is provider/model.");
                SharedAssert.Equal("claude-haiku-4-5", models[0].DisplayName, "Display name is the model id.");
                SharedAssert.Equal("anthropic", models[0].OwnedBy, "OwnedBy is the provider.");
                SharedAssert.Equal("200K", models[0].Metadata["context"], "Context metadata as printed.");
                SharedAssert.Equal("64K", models[0].Metadata["max_out"], "Max output metadata as printed.");
                SharedAssert.Equal("yes", models[0].Metadata["thinking"], "Thinking metadata.");
                SharedAssert.Equal("1M", models[1].Metadata["context"], "Second model context.");
                SharedAssert.Equal("--list-models", string.Join(" | ", fake.Arguments), "Arguments.");

                await foreach (ModelInformation model in pi.ListModelsAsync("haiku", token).ConfigureAwait(false)) { }
                SharedAssert.Equal("--list-models | haiku", string.Join(" | ", fake.Arguments), "Search is passed through.");

                fake.UseScenario(client, "pi-unauthenticated");
                models.Clear();
                await foreach (ModelInformation model in pi.ListModelsAsync(token: token).ConfigureAwait(false)) models.Add(model);
                SharedAssert.Equal(0, models.Count, "'No models available' is an empty list.");

                fake.UseScenario(client, "pi-stderr-only");
                CliAgentException? failure = null;
                try
                {
                    await foreach (ModelInformation model in pi.ListModelsAsync(token: token).ConfigureAwait(false)) { }
                }
                catch (CliAgentException ex)
                {
                    failure = ex;
                }
                SharedAssert.NotNull(failure, "A failing model list throws CliAgentException.");
                SharedAssert.Equal(CliFailureKind.Crashed, failure!.FailureKind, "Failure kind.");
                SharedAssert.Equal(1, failure.ExitCode, "Exit code.");

                pi.Executable = "polyprompt-missing-pi";
                failure = null;
                try
                {
                    await foreach (ModelInformation model in pi.ListModelsAsync(token: token).ConfigureAwait(false)) { }
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
            (FakeCli fake, CliAgentClientBase client) = CliAgentCaseKit.Attach(Profile, "pi-happy");
            using (fake)
            using (client)
            {
                CliAgentResult first = await client.RunAsync(new CliAgentRequest("first"), token).ConfigureAwait(false);
                SharedAssert.Equal(SessionId, first.SessionId, "First turn session id.");

                ChatMessage assistant = ChatMessage.Assistant(first.Text);
                assistant.ProviderState = first.ProviderState;
                List<ChatMessage> history = new List<ChatMessage> { ChatMessage.User("first"), assistant };

                fake.ClearRecord();
                CliAgentResult second = await client.RunAsync(new CliAgentRequest("second") { History = history }, token).ConfigureAwait(false);
                SharedAssert.True(second.Success, "Second turn succeeds: " + second.Error);
                List<string> args = fake.Arguments;
                int session = args.IndexOf("--session");
                SharedAssert.True(session >= 0 && session + 1 < args.Count && args[session + 1] == SessionId, "History with a session id resumes with --session <id>. Args: " + string.Join(" | ", args));
                SharedAssert.Equal(Stdin("second"), fake.StandardInput, "Only the new prompt is sent when resuming.");

                fake.ClearRecord();
                List<ChatMessage> plain = new List<ChatMessage> { ChatMessage.System("Be terse."), ChatMessage.User("What is 2+2?"), ChatMessage.Assistant("4") };
                CliAgentResult replayed = await client.RunAsync(new CliAgentRequest("And 3+3?") { History = plain }, token).ConfigureAwait(false);
                SharedAssert.True(replayed.Success, "Replayed turn succeeds: " + replayed.Error);
                SharedAssert.False(fake.Arguments.Contains("--session"), "Plain history does not resume.");
                SharedAssert.Equal(
                    Stdin("System instructions:\\nBe terse.\\n\\nConversation so far:\\n\\nUser: What is 2+2?\\n\\nAssistant: 4\\n\\nCurrent request:\\nAnd 3+3?"),
                    fake.StandardInput,
                    "Plain history is replayed into the prompt, with system messages as instructions.");
            }
        }

        private static async Task RunConnectivityAsync(CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = CliAgentCaseKit.Attach(Profile, "pi-happy");
            using (fake)
            using (client)
            {
                CliConnectivityResult result = await client.ValidateConnectivityAsync(token).ConfigureAwait(false);
                SharedAssert.Equal(CliConnectivityStatus.Ok, result.Status, "Ok. Message: " + result.Message);
                SharedAssert.Equal("1.4.0", result.Version, "Version.");
                SharedAssert.Equal("anthropic: ready (oauth)", result.Message, "Readiness message.");
                List<string> invocations = fake.Invocations;
                SharedAssert.Equal(3, invocations.Count, "Version, state probe, auth check. Invocations: " + string.Join(" / ", invocations));
                SharedAssert.Equal("--mode rpc --no-session --no-tools --no-mcp --no-approve --offline", invocations[1], "The probe starts no run and saves no session.");
                SharedAssert.Equal("auth check --provider anthropic --json", invocations[2], "The auth check uses the probed provider.");
                SharedAssert.Equal(StateCommand, ((ProcessCallDetail)client.CallDetails[1]).StandardInput, "The probe sends only get_state.");

                fake.ClearRecord();
                client.Model = "anthropic/claude-haiku-4-5";
                result = await client.ValidateConnectivityAsync(token).ConfigureAwait(false);
                SharedAssert.Equal(CliConnectivityStatus.Ok, result.Status, "Ok with a model. Message: " + result.Message);
                SharedAssert.Equal("--mode rpc --no-session --no-tools --no-mcp --no-approve --offline --model anthropic/claude-haiku-4-5", fake.Invocations[1], "The probe resolves the configured model.");

                fake.UseScenario(client, "pi-unauthenticated");
                result = await client.ValidateConnectivityAsync(token).ConfigureAwait(false);
                SharedAssert.Equal(CliConnectivityStatus.NotAuthenticated, result.Status, "No usable model is NotAuthenticated.");
                SharedAssert.True((result.Message ?? "").Contains("no usable model", StringComparison.Ordinal), "Message: " + result.Message);
                SharedAssert.Equal(2, fake.Invocations.Count, "The auth check is skipped when no provider is usable.");

                fake.UseScenario(client, "pi-stderr-only");
                result = await client.ValidateConnectivityAsync(token).ConfigureAwait(false);
                SharedAssert.Equal(CliConnectivityStatus.Error, result.Status, "A launcher failure on --version is Error.");
            }
        }

        private static async Task RunOutcomesAsync(CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = CliAgentCaseKit.Attach(Profile, "pi-rejected-input");
            using (fake)
            using (client)
            {
                CliAgentResult result = await client.RunAsync(new CliAgentRequest("hi") { SessionId = "99999999-dead-beef-0000-000000000000" }, token).ConfigureAwait(false);
                SharedAssert.Equal(CliFailureKind.InvalidInput, result.FailureKind, "An unknown session is InvalidInput. Error: " + result.Error);
                SharedAssert.True((result.Error ?? "").Contains("No session found matching", StringComparison.Ordinal), "Error carries stderr: " + result.Error);
                SharedAssert.Equal(1, result.ExitCode, "Exit code.");

                result = await client.RunAsync(new CliAgentRequest("hi") { Model = "no-such-model-xyz" }, token).ConfigureAwait(false);
                SharedAssert.Equal(CliFailureKind.InvalidInput, result.FailureKind, "An unknown model is InvalidInput. Error: " + result.Error);
                SharedAssert.Equal("no-such-model-xyz", result.Model, "The requested model is reported when Pi reports none.");

                fake.UseScenario(client, "pi-aborted");
                result = await client.RunAsync(new CliAgentRequest("hi"), token).ConfigureAwait(false);
                SharedAssert.False(result.Success, "An aborted run fails.");
                SharedAssert.Equal(CliFailureKind.ProviderError, result.FailureKind, "Aborted is a harness-reported failure. Error: " + result.Error);
                SharedAssert.True((result.Error ?? "").StartsWith("Pi aborted the run", StringComparison.Ordinal), "Error: " + result.Error);
                SharedAssert.Equal("Partial", result.Text, "Partial text is kept.");
                CliAgentCaseKit.AssertTerminal(result, CliAgentEventKind.Failed, "aborted");

                fake.UseScenario(client, "pi-handled");
                result = await client.RunAsync(new CliAgentRequest("/handled-by-extension"), token).ConfigureAwait(false);
                SharedAssert.True(result.Success, "A handled prompt completes: " + result.Error);
                SharedAssert.Equal("", result.Text, "No text.");
                SharedAssert.Equal("01a18352-0000-7000-8000-00000000000a", result.SessionId, "Session id.");
                SharedAssert.Equal(null, result.Usage, "No usage when no model ran.");
                SharedAssert.Equal(null, result.CostUsd, "No cost when no model ran.");
                SharedAssert.Equal(0, result.ExitCode, "Exit code.");
            }
        }

        private static async Task RunRecordedReplayAsync(CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = CliAgentCaseKit.Attach(Profile, "pi-replay");
            using (fake)
            using (client)
            {
                CliAgentResult result = await client.RunAsync(new CliAgentRequest("hi"), token).ConfigureAwait(false);
                SharedAssert.True(result.Success, "A successful retry completes the run: " + result.Error);
                SharedAssert.Equal("done", result.Text, "Text.");
                SharedAssert.Equal(0.000017m, result.CostUsd, "Cost in exponent notation is parsed exactly.");
                SharedAssert.Equal(12, result.Usage?.PromptTokens, "Usage of the failed attempt (all zero) and the retry are summed.");
                SharedAssert.Equal(null, result.Usage?.ReasoningTokens, "Reasoning stays null when not reported.");

                List<CliAgentEvent> notices = result.Events.Where(e => e.Kind == CliAgentEventKind.Notice).ToList();
                SharedAssert.True(notices.Any(e => e.IsError && (e.Text ?? "").Contains("audit log unavailable", StringComparison.Ordinal)), "Extension errors are error notices.");
                SharedAssert.True(notices.Any(e => !e.IsError && e.Text == "Command blocked by user"), "notify is a notice.");
                SharedAssert.True(notices.Any(e => e.IsError && (e.Text ?? "").Contains("cannot answer (confirm: Allow dangerous command?)", StringComparison.Ordinal)), "Dialogs are error notices.");
                SharedAssert.True(notices.Any(e => e.IsError && e.Text == "Retrying (attempt 1 of 3) after: Overloaded (overloaded_error, status 529)"), "Retries are error notices with the provider message.");
                SharedAssert.True(notices.Any(e => e.Text == "Compacting context (threshold)."), "Compaction start.");
                SharedAssert.True(notices.Any(e => e.Text == "Context compacted."), "Compaction end.");
                CliAgentEvent tool = result.Events.First(e => e.Kind == CliAgentEventKind.ToolResult);
                SharedAssert.True(tool.IsError, "A failed tool is an error result.");
                SharedAssert.Equal("ENOENT: no such file or directory", tool.ToolOutput, "Tool error text.");
                SharedAssert.Equal("{\"path\":\"missing.txt\"}", result.Events.First(e => e.Kind == CliAgentEventKind.ToolUse).ToolInput, "Tool args.");

                fake.UseScenario(client, "pi-rate-limited");
                result = await client.RunAsync(new CliAgentRequest("hi"), token).ConfigureAwait(false);
                SharedAssert.Equal(CliFailureKind.RateLimited, result.FailureKind, "Rate limited after retries.");
                SharedAssert.Equal("Number of request tokens has exceeded your per-minute rate limit (rate_limit_error, status 429)", result.Error, "The final error is described.");
                SharedAssert.Equal("01a18352-0000-7000-8000-000000000007", result.SessionId, "Session id is reported even when the run fails.");

                fake.UseScenario(client, "pi-unauthenticated");
                result = await client.RunAsync(new CliAgentRequest("hi"), token).ConfigureAwait(false);
                SharedAssert.Equal(CliFailureKind.NotAuthenticated, result.FailureKind, "A rejected prompt without credentials is NotAuthenticated.");
                SharedAssert.True((result.Error ?? "").StartsWith("No API key found", StringComparison.Ordinal), "Error: " + result.Error);
                SharedAssert.Equal(0, result.ExitCode, "Pi exits 0 once stdin closes after the rejection.");
            }
        }
    }
}
