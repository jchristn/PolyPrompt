namespace Test.Shared
{
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using Touchstone.Core;

    /// <summary>
    /// Fake-CLI cases for <see cref="MuxAgentClient"/>: the shared harness kit plus model listing, sessions, the
    /// permission mapping, refused tool calls, configuration errors, the turn limit, and the remaining JSONL event types.
    /// </summary>
    internal static class LocalMuxAgentCases
    {
        private const string SessionId = "7d1c5e2a-4b8f-4e21-9a3c-0f6b2d8e1a57";

        private static readonly List<string> _Restricted = new List<string> { "--approval-policy", "auto", "--sandbox", "read-only", "--deny-tools", "web_search,web_retrieve" };

        public static CliAgentProfile Profile
        {
            get
            {
                return new CliAgentProfile
                {
                    Id = "mux",
                    ExecutableName = "mux",
                    Create = () => new MuxAgentClient(),
                    HappyScenario = "mux-happy",
                    ErrorExitScenario = "mux-error-exit",
                    StderrOnlyScenario = "mux-stderr-only",
                    MalformedScenario = "mux-malformed",
                    SlowScenario = "mux-slow",
                    HugeScenario = "mux-huge",
                    UnauthenticatedScenario = "mux-unauthenticated",
                    RateLimitedScenario = "mux-rate-limited",
                    HappyText = "a.txt",
                    HappySessionId = SessionId,
                    HappyReasoningContains = "We need to call list_directory.",
                    HappyToolName = "list_directory",
                    HappyUsage = new TokenUsage { PromptTokens = 8990, CompletionTokens = 37, CachedPromptTokens = 0, ReasoningTokens = 0, TotalTokens = 9027 },
                    HappyCostUsd = null,
                    HappyModel = "gpt-oss:20b",
                    ErrorExitMessageContains = "Failed to connect to http://127.0.0.1:9/v1: Connection refused (127.0.0.1:9) (llm_connection_error)",
                    StderrOnlyMessageContains = "Unknown option '--bogus'.",
                    HasAuthenticationStatus = true,
                    PromptOnStandardInput = true,
                    ArgvCases = new List<CliAgentProfile.ArgvCase>
                    {
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "default (Restricted)",
                            Expected = Args(_Restricted, "--show-thinking"),
                            ExpectedStandardInput = "hello",
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "ReadOnly with model",
                            Request = r => { r.PermissionMode = CliPermissionMode.ReadOnly; r.Model = "qwen3:8b"; },
                            Expected = Args(new List<string> { "--model", "qwen3:8b", "--approval-policy", "auto", "--sandbox", "read-only" }, "--show-thinking"),
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "WorkspaceWrite, endpoint, and model from the client",
                            Client = c => { c.PermissionMode = CliPermissionMode.WorkspaceWrite; c.Model = "client-model"; ((MuxAgentClient)c).Endpoint = "vllm-coder"; },
                            Expected = Args(new List<string> { "--endpoint", "vllm-coder", "--model", "client-model", "--approval-policy", "auto", "--sandbox", "workspace-write", "--deny-tools", "run_process" }, "--show-thinking"),
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "Unrestricted",
                            Request = r => r.PermissionMode = CliPermissionMode.Unrestricted,
                            Expected = Args(new List<string> { "--yolo", "--sandbox", "none" }, "--show-thinking"),
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "resume by session id",
                            Request = r => r.SessionId = "abc-123",
                            Expected = Args(_Restricted, "--show-thinking", "--resume", "abc-123"),
                            ExpectedStandardInput = "hello",
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "system prompt uses --append-system-prompt; stdin carries only the prompt",
                            Request = r => r.SystemPrompt = "Be terse.",
                            Expected = Args(_Restricted, "--show-thinking", "--append-system-prompt", "Be terse."),
                            ExpectedStandardInput = "hello",
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "ShowThinking off",
                            Client = c => ((MuxAgentClient)c).ShowThinking = false,
                            Expected = Args(_Restricted),
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "ExtraArguments are appended after the client's arguments",
                            Request = r => r.ExtraArguments = new Dictionary<string, string?> { { "--max-turns", "5" }, { "--no-project-instructions", null } },
                            Expected = Args(_Restricted, "--show-thinking", "--max-turns", "5", "--no-project-instructions"),
                        },
                    },
                };
            }
        }

        public static IEnumerable<TestCaseDescriptor> Create(string suiteId)
        {
            List<TestCaseDescriptor> cases = CliAgentCaseKit.Create(suiteId, Profile);
            cases.Add(CliAgentCaseKit.Case(suiteId, "cli_mux_list_models", "mux: ListModelsAsync parses mux endpoint models and maps failures", RunListModelsAsync));
            cases.Add(CliAgentCaseKit.Case(suiteId, "cli_mux_session_history", "mux: sessions persist only when asked; a session id in history resumes; plain history is replayed", RunSessionHistoryAsync));
            cases.Add(CliAgentCaseKit.Case(suiteId, "cli_mux_denied_tool", "mux: a refused tool call is an error tool result and notice, and exit code 2 fails the run", RunDeniedToolAsync));
            cases.Add(CliAgentCaseKit.Case(suiteId, "cli_mux_config_error", "mux: an error before run_started (unknown endpoint) is InvalidInput", RunConfigErrorAsync));
            cases.Add(CliAgentCaseKit.Case(suiteId, "cli_mux_turn_limit", "mux: max_iterations_reached is TurnLimitReached", RunTurnLimitAsync));
            cases.Add(CliAgentCaseKit.Case(suiteId, "cli_mux_event_types", "mux: task plans, hooks, and compaction become notices; failed tool results and reported cost map", RunEventTypesAsync));
            cases.Add(CliAgentCaseKit.Case(suiteId, "cli_mux_connectivity_probe", "mux: connectivity runs mux probe; credential failures are NotAuthenticated, others Error", RunConnectivityProbeAsync));
            return cases;
        }

        private static List<string> Args(List<string> middle, params string[] tail)
        {
            List<string> args = new List<string> { "print", "--output-format", "jsonl" };
            args.AddRange(middle);
            args.AddRange(tail);
            return args;
        }

        private static async Task RunListModelsAsync(CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = CliAgentCaseKit.Attach(Profile, "mux-happy");
            using (fake)
            using (client)
            {
                MuxAgentClient mux = (MuxAgentClient)client;
                List<ModelInformation> models = new List<ModelInformation>();
                await foreach (ModelInformation model in mux.ListModelsAsync(token).ConfigureAwait(false)) models.Add(model);

                SharedAssert.Equal(3, models.Count, "Models from every endpoint.");
                SharedAssert.Equal("gpt-oss:20b", models[0].Name, "Model id is the name.");
                SharedAssert.Equal("local-gpt-oss", models[0].Metadata["endpoint"], "Endpoint metadata.");
                SharedAssert.Equal("Ollama", models[0].Metadata["adapter_type"], "Adapter type metadata.");
                SharedAssert.Equal("true", models[0].Metadata["default_endpoint"], "Default endpoint metadata.");
                SharedAssert.Equal("true", models[0].Metadata["configured"], "The endpoint's configured model.");
                SharedAssert.Equal("false", models[1].Metadata["configured"], "Other models on the endpoint.");
                SharedAssert.Equal("vllm-coder", models[2].Metadata["endpoint"], "Second endpoint.");
                SharedAssert.Equal("endpoint | models | --output-format | json", string.Join(" | ", fake.Arguments), "Arguments without an endpoint.");

                mux.Endpoint = "local-gpt-oss";
                await foreach (ModelInformation model in mux.ListModelsAsync(token).ConfigureAwait(false)) { }
                SharedAssert.Equal("endpoint | models | local-gpt-oss | --output-format | json", string.Join(" | ", fake.Arguments), "Arguments with an endpoint.");

                fake.UseScenario(client, "mux-unauthenticated");
                CliAgentException? failure = null;
                try
                {
                    await foreach (ModelInformation model in mux.ListModelsAsync(token).ConfigureAwait(false)) { }
                }
                catch (CliAgentException ex)
                {
                    failure = ex;
                }
                SharedAssert.NotNull(failure, "A failing model list throws CliAgentException.");
                SharedAssert.Equal(CliFailureKind.NotAuthenticated, failure!.FailureKind, "A 401 from the backend is NotAuthenticated. " + failure.Message);
                SharedAssert.Equal(1, failure.ExitCode, "Exit code.");

                fake.UseScenario(client, "mux-stderr-only");
                failure = null;
                try
                {
                    await foreach (ModelInformation model in mux.ListModelsAsync(token).ConfigureAwait(false)) { }
                }
                catch (CliAgentException ex)
                {
                    failure = ex;
                }
                SharedAssert.Equal(CliFailureKind.Crashed, failure?.FailureKind, "Stderr-only failure is Crashed.");

                mux.Executable = "polyprompt-missing-mux";
                failure = null;
                try
                {
                    await foreach (ModelInformation model in mux.ListModelsAsync(token).ConfigureAwait(false)) { }
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
            (FakeCli fake, CliAgentClientBase client) = CliAgentCaseKit.Attach(Profile, "mux-stateless");
            using (fake)
            using (client)
            {
                MuxAgentClient mux = (MuxAgentClient)client;

                CliAgentResult stateless = await client.RunAsync(new CliAgentRequest("hi"), token).ConfigureAwait(false);
                SharedAssert.True(stateless.Success, "Stateless run succeeds: " + stateless.Error);
                SharedAssert.Equal(null, stateless.SessionId, "An empty sessionId is reported as no session.");
                SharedAssert.False(fake.Arguments.Contains("--session-id"), "No session flag by default.");
                SharedAssert.Equal("PONG", stateless.Text, "Final text.");

                mux.PersistSessions = true;
                await client.RunAsync(new CliAgentRequest("hi"), token).ConfigureAwait(false);
                List<string> args = fake.Arguments;
                int flag = args.IndexOf("--session-id");
                SharedAssert.True(flag >= 0 && flag + 1 < args.Count && Guid.TryParse(args[flag + 1], out _), "PersistSessions starts the run under a new session id. Args: " + string.Join(" | ", args));
                string firstId = args[flag + 1];
                await client.RunAsync(new CliAgentRequest("hi"), token).ConfigureAwait(false);
                SharedAssert.False(fake.Arguments.Contains(firstId), "Each new run gets its own session id.");

                fake.UseScenario(client, "mux-happy");
                mux.PersistSessions = false;
                CliAgentResult first = await client.RunAsync(new CliAgentRequest("first"), token).ConfigureAwait(false);
                SharedAssert.Equal(SessionId, first.SessionId, "Session id from run_started/run_completed.");

                ChatMessage assistant = ChatMessage.Assistant(first.Text);
                assistant.ProviderState = first.ProviderState;
                List<ChatMessage> history = new List<ChatMessage> { ChatMessage.System("Be terse."), ChatMessage.User("first"), assistant };

                mux.PersistSessions = true;
                CliAgentResult second = await client.RunAsync(new CliAgentRequest("second") { History = history }, token).ConfigureAwait(false);
                SharedAssert.True(second.Success, "Second turn succeeds: " + second.Error);
                args = fake.Arguments;
                int resume = args.IndexOf("--resume");
                SharedAssert.True(resume >= 0 && args[resume + 1] == SessionId, "History with a session id resumes it. Args: " + string.Join(" | ", args));
                SharedAssert.False(args.Contains("--session-id"), "A resumed run does not also start a new session.");
                SharedAssert.True(args.Contains("--append-system-prompt") && args[args.IndexOf("--append-system-prompt") + 1] == "Be terse.", "System messages in history become --append-system-prompt.");
                SharedAssert.Equal("second", fake.StandardInput, "Only the new prompt is sent when resuming.");

                mux.PersistSessions = false;
                List<ChatMessage> plain = new List<ChatMessage> { ChatMessage.System("Be terse."), ChatMessage.User("What is 2+2?"), ChatMessage.Assistant("4") };
                CliAgentResult replayed = await client.RunAsync(new CliAgentRequest("And 3+3?") { History = plain, SystemPrompt = "Answer in digits." }, token).ConfigureAwait(false);
                SharedAssert.True(replayed.Success, "Replayed turn succeeds: " + replayed.Error);
                args = fake.Arguments;
                SharedAssert.False(args.Contains("--resume"), "Plain history does not resume.");
                SharedAssert.Equal("Answer in digits.\n\nBe terse.", args[args.IndexOf("--append-system-prompt") + 1], "Request and history system instructions are combined.");
                SharedAssert.Equal(
                    "Conversation so far:\n\nUser: What is 2+2?\n\nAssistant: 4\n\nCurrent request:\nAnd 3+3?",
                    fake.StandardInput,
                    "Plain history is replayed into the prompt; system instructions are not duplicated on stdin.");
            }
        }

        private static async Task RunDeniedToolAsync(CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = CliAgentCaseKit.Attach(Profile, "mux-denied");
            using (fake)
            using (client)
            {
                CliAgentResult result = await client.RunAsync(new CliAgentRequest("Create b.txt"), token).ConfigureAwait(false);
                SharedAssert.False(result.Success, "Exit code 2 (a refused tool call) fails the run.");
                SharedAssert.Equal(2, result.ExitCode, "Exit code.");
                SharedAssert.Equal(CliFailureKind.ProviderError, result.FailureKind, "A refused tool call is a structured failure, not a crash. Error: " + result.Error);
                SharedAssert.True((result.Error ?? "").Contains("completed_with_errors", StringComparison.Ordinal) && (result.Error ?? "").Contains("blocked by the read-only sandbox posture", StringComparison.Ordinal), "Error names the status and the refusal: " + result.Error);
                SharedAssert.True(result.Text.StartsWith("I\u2019m sorry", StringComparison.Ordinal), "The model's answer is kept: " + result.Text);

                CliAgentEvent? use = result.Events.FirstOrDefault(e => e.Kind == CliAgentEventKind.ToolUse);
                CliAgentEvent? denied = result.Events.FirstOrDefault(e => e.Kind == CliAgentEventKind.ToolResult);
                SharedAssert.NotNull(use, "ToolUse for the proposed call.");
                SharedAssert.Equal("write_file", use!.ToolName, "Tool name.");
                SharedAssert.True((use.ToolInput ?? "").Contains("\"file_path\":\"b.txt\"", StringComparison.Ordinal), "Arguments are rendered: " + use.ToolInput);
                SharedAssert.NotNull(denied, "The refused call gets an error ToolResult.");
                SharedAssert.True(denied!.IsError, "ToolResult IsError.");
                SharedAssert.Equal("call_7lzz2uct", denied.ToolCallId, "ToolResult pairs with the proposed call.");
                SharedAssert.True(result.Events.Any(e => e.Kind == CliAgentEventKind.Notice && e.IsError && (e.Text ?? "").Contains("tool_call_denied", StringComparison.Ordinal)), "The refusal is an error notice.");
                AssertTerminalFailed(result);
            }
        }

        private static async Task RunConfigErrorAsync(CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = CliAgentCaseKit.Attach(Profile, "mux-config-error");
            using (fake)
            using (client)
            {
                ((MuxAgentClient)client).Endpoint = "no-such-endpoint";
                CliAgentResult result = await client.RunAsync(new CliAgentRequest("hi"), token).ConfigureAwait(false);
                SharedAssert.Equal(CliFailureKind.InvalidInput, result.FailureKind, "endpoint_not_found is InvalidInput. Error: " + result.Error);
                SharedAssert.True((result.Error ?? "").Contains("No endpoint named 'no-such-endpoint'", StringComparison.Ordinal), "Error message: " + result.Error);
                SharedAssert.Equal(1, result.ExitCode, "Exit code.");
                SharedAssert.Equal(null, result.SessionId, "No session.");
                AssertTerminalFailed(result);
            }
        }

        private static async Task RunTurnLimitAsync(CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = CliAgentCaseKit.Attach(Profile, "mux-turn-limit");
            using (fake)
            using (client)
            {
                CliAgentResult result = await client.RunAsync(new CliAgentRequest("hi"), token).ConfigureAwait(false);
                SharedAssert.Equal(CliFailureKind.TurnLimitReached, result.FailureKind, "Failure kind. Error: " + result.Error);
                SharedAssert.True((result.Error ?? "").Contains("Maximum iterations (1) reached.", StringComparison.Ordinal), "Error message: " + result.Error);
                SharedAssert.Equal(110, result.Usage?.TotalTokens, "Usage is still reported on a failed run.");
                AssertTerminalFailed(result);
            }
        }

        private static async Task RunEventTypesAsync(CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = CliAgentCaseKit.Attach(Profile, "mux-events");
            using (fake)
            using (client)
            {
                CliAgentResult result = await client.RunAsync(new CliAgentRequest("plan"), token).ConfigureAwait(false);
                SharedAssert.True(result.Success, "Run succeeds: " + result.Error);
                SharedAssert.Equal("Summary done.", result.Text, "Final text is the concatenated assistant text.");
                SharedAssert.Equal("plan-session-1", result.SessionId, "Session id.");
                SharedAssert.Equal("gpt-5-mini", result.Model, "Model reported on run_started.");
                SharedAssert.Equal(0.008237m, result.CostUsd, "costUsd is reported exactly.");
                SharedAssert.Equal(1234, result.Usage?.PromptTokens, "inputTokens.");
                SharedAssert.Equal(567, result.Usage?.CompletionTokens, "outputTokens.");
                SharedAssert.Equal(512, result.Usage?.CachedPromptTokens, "cachedTokens.");
                SharedAssert.Equal(64, result.Usage?.ReasoningTokens, "reasoningTokens.");
                SharedAssert.Equal(1801, result.Usage?.TotalTokens, "totalTokens as reported.");
                SharedAssert.Equal(null, result.Usage?.CacheCreationTokens, "Not reported.");

                List<CliAgentEvent> notices = result.Events.Where(e => e.Kind == CliAgentEventKind.Notice).ToList();
                SharedAssert.True(notices.Any(n => n.ProviderEventType == "task_plan_updated" && n.Text == "Task plan plan_created (0/2 done)\n[ ] Read README.md\n[ ] Summarize"), "Plan created notice.");
                SharedAssert.True(notices.Any(n => n.ProviderEventType == "task_plan_updated" && n.Text == "Task plan task_status_changed (1/2 done)\n[x] Read README.md\n[ ] Summarize (in_progress)"), "Task status notice.");
                SharedAssert.True(notices.Any(n => n.ProviderEventType == "hook" && n.IsError && (n.Text ?? "").Contains("lint (post-tool-use) warning", StringComparison.Ordinal)), "Hook warning notice.");
                SharedAssert.True(notices.Any(n => n.ProviderEventType == "context_compacted" && n.Text == "Context compacted (summary): 12 -> 4 messages"), "Compaction notice.");
                SharedAssert.False(notices.Any(n => n.ProviderEventType == "context_status"), "context_status is not surfaced.");

                CliAgentEvent tool = result.Events.First(e => e.Kind == CliAgentEventKind.ToolResult);
                SharedAssert.True(tool.IsError, "success=false is an error tool result.");
                SharedAssert.Equal("File not found: MISSING.md", tool.ToolOutput, "Tool output.");
                SharedAssert.Equal("read_file", tool.ToolName, "Tool name.");
            }
        }

        private static async Task RunConnectivityProbeAsync(CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = CliAgentCaseKit.Attach(Profile, "mux-happy");
            using (fake)
            using (client)
            {
                MuxAgentClient mux = (MuxAgentClient)client;
                mux.Endpoint = "local-gpt-oss";
                mux.Model = "gpt-oss:20b";
                CliConnectivityResult ok = await client.ValidateConnectivityAsync(token).ConfigureAwait(false);
                SharedAssert.Equal(CliConnectivityStatus.Ok, ok.Status, "Ok. Message: " + ok.Message);
                SharedAssert.Equal("mux v1.5.0", ok.Version, "Version line.");
                SharedAssert.Equal("probe | --output-format | json | --endpoint | local-gpt-oss | --model | gpt-oss:20b", string.Join(" | ", fake.Arguments), "Probe arguments.");
                SharedAssert.True((ok.Message ?? "").Contains("local-gpt-oss", StringComparison.Ordinal), "Message names the endpoint: " + ok.Message);

                fake.UseScenario(client, "mux-unauthenticated");
                CliConnectivityResult unauth = await client.ValidateConnectivityAsync(token).ConfigureAwait(false);
                SharedAssert.Equal(CliConnectivityStatus.NotAuthenticated, unauth.Status, "Authentication failure. Message: " + unauth.Message);
                SharedAssert.False(unauth.AuthenticationVerified, "Not verified.");

                fake.UseScenario(client, "mux-probe-unreachable");
                CliConnectivityResult unreachable = await client.ValidateConnectivityAsync(token).ConfigureAwait(false);
                SharedAssert.Equal(CliConnectivityStatus.Error, unreachable.Status, "An unreachable backend is Error, not NotAuthenticated. Message: " + unreachable.Message);
                SharedAssert.True((unreachable.Message ?? "").Contains("backend_unreachable, network", StringComparison.Ordinal), "Message carries the probe's code and category: " + unreachable.Message);
                SharedAssert.Equal("mux v1.5.0", unreachable.Version, "Version is still reported.");

                fake.UseScenario(client, "mux-stderr-only");
                CliConnectivityResult broken = await client.ValidateConnectivityAsync(token).ConfigureAwait(false);
                SharedAssert.Equal(CliConnectivityStatus.Error, broken.Status, "A version failure is Error. Message: " + broken.Message);
            }
        }

        private static void AssertTerminalFailed(CliAgentResult result)
        {
            CliAgentCaseKit.AssertTerminal(result, CliAgentEventKind.Failed, "mux failure");
        }
    }
}
