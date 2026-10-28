namespace Test.Shared
{
    using System.Text.Json;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using Touchstone.Core;

    /// <summary>
    /// Fake-CLI cases for <see cref="OpenCodeAgentClient"/>: the shared harness kit plus model listing, the permission
    /// configuration, session resume, per-step usage accounting, and recorded-output replay.
    /// </summary>
    internal static class LocalOpenCodeAgentCases
    {
        private const string SessionId = "ses_e7caeda0cffeo3jV9PTh327SWT";

        private static readonly List<string> _Base = new List<string> { "run", "--format", "json", "--standalone", "--thinking" };

        public static CliAgentProfile Profile
        {
            get
            {
                return new CliAgentProfile
                {
                    Id = "opencode",
                    ExecutableName = "opencode",
                    Create = () => new OpenCodeAgentClient(),
                    HappyScenario = "opencode-happy",
                    ErrorExitScenario = "opencode-error-exit",
                    StderrOnlyScenario = "opencode-stderr-only",
                    MalformedScenario = "opencode-malformed",
                    SlowScenario = "opencode-slow",
                    HugeScenario = "opencode-huge",
                    UnauthenticatedScenario = "opencode-unauthenticated",
                    RateLimitedScenario = "opencode-rate-limited",
                    HappyText = "a.txt",
                    HappySessionId = SessionId,
                    HappyReasoningContains = "Listing files",
                    HappyToolName = "read",
                    HappyUsage = new TokenUsage { PromptTokens = 1902, CompletionTokens = 36, ReasoningTokens = 192, CachedPromptTokens = 966, CacheCreationTokens = 0 },
                    HappyCostUsd = 0.00335m,
                    ErrorExitMessageContains = "Model unavailable: opencode/no-such-model-xyz (provider.no-route)",
                    StderrOnlyMessageContains = "the private server exited unexpectedly",
                    HasAuthenticationStatus = false,
                    PromptOnStandardInput = true,
                    ArgvCases = new List<CliAgentProfile.ArgvCase>
                    {
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "default (Restricted)",
                            Expected = Args("--agent", "build"),
                            ExpectedStandardInput = "hello",
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "ReadOnly with model",
                            Request = r => { r.PermissionMode = CliPermissionMode.ReadOnly; r.Model = "opencode/big-pickle"; },
                            Expected = Args("--agent", "build", "-m", "opencode/big-pickle"),
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "WorkspaceWrite from the client",
                            Client = c => { c.PermissionMode = CliPermissionMode.WorkspaceWrite; c.Model = "anthropic/client-model"; },
                            Expected = Args("--agent", "build", "-m", "anthropic/client-model"),
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "Unrestricted",
                            Request = r => r.PermissionMode = CliPermissionMode.Unrestricted,
                            Expected = Args("--auto"),
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "resume by session id",
                            Request = r => r.SessionId = "ses_abc123",
                            Expected = Args("--agent", "build", "--session", "ses_abc123"),
                            ExpectedStandardInput = "hello",
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "system prompt is prepended (opencode run has no system-prompt option)",
                            Request = r => r.SystemPrompt = "Be terse.",
                            Expected = Args("--agent", "build"),
                            ExpectedStandardInput = "System instructions:\nBe terse.\n\nRequest:\nhello",
                        },
                        new CliAgentProfile.ArgvCase
                        {
                            Description = "ExtraArguments are appended after the client's arguments",
                            Request = r => r.ExtraArguments = new Dictionary<string, string?> { { "--title", "PolyPrompt run" } },
                            Expected = Args("--agent", "build", "--title", "PolyPrompt run"),
                        },
                    },
                };
            }
        }

        public static IEnumerable<TestCaseDescriptor> Create(string suiteId)
        {
            List<TestCaseDescriptor> cases = CliAgentCaseKit.Create(suiteId, Profile);
            cases.Add(CliAgentCaseKit.Case(suiteId, "cli_opencode_list_models", "opencode: ListModelsAsync parses opencode models", RunListModelsAsync));
            cases.Add(CliAgentCaseKit.Case(suiteId, "cli_opencode_permission_config", "opencode: each permission mode maps to the documented OPENCODE_CONFIG_CONTENT rules", RunPermissionConfigAsync));
            cases.Add(CliAgentCaseKit.Case(suiteId, "cli_opencode_session_history", "opencode: a session id in history provider state resumes; plain history is replayed", RunSessionHistoryAsync));
            cases.Add(CliAgentCaseKit.Case(suiteId, "cli_opencode_step_usage", "opencode: per-step usage is reported on notices and totalled only when every step reported it", RunStepUsageAsync));
            cases.Add(CliAgentCaseKit.Case(suiteId, "cli_opencode_recorded_replay", "opencode: recorded output replays without a live CLI, including plain-text usage errors", RunRecordedReplayAsync));
            return cases;
        }

        private static List<string> Args(params string[] tail)
        {
            List<string> args = new List<string>(_Base);
            args.AddRange(tail);
            return args;
        }

        private static async Task RunListModelsAsync(CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = CliAgentCaseKit.Attach(Profile, "opencode-happy");
            using (fake)
            using (client)
            {
                OpenCodeAgentClient opencode = (OpenCodeAgentClient)client;
                List<ModelInformation> models = new List<ModelInformation>();
                await foreach (ModelInformation model in opencode.ListModelsAsync(token).ConfigureAwait(false)) models.Add(model);

                SharedAssert.Equal(3, models.Count, "Three models; the blank line is skipped.");
                SharedAssert.Equal("opencode/big-pickle", models[0].Name, "Name is provider/model.");
                SharedAssert.Equal("big-pickle", models[0].DisplayName, "Display name is the model part.");
                SharedAssert.Equal("opencode", models[0].OwnedBy, "OwnedBy is the provider.");
                SharedAssert.Equal("anthropic", models[2].Metadata["provider"], "Provider metadata.");
                SharedAssert.Equal("models", string.Join(" | ", fake.Arguments), "Arguments.");

                fake.UseScenario(client, "opencode-stderr-only");
                CliAgentException? failure = await ListFailureAsync(opencode, token).ConfigureAwait(false);
                SharedAssert.NotNull(failure, "A failing model list throws CliAgentException.");
                SharedAssert.Equal(CliFailureKind.Crashed, failure!.FailureKind, "Failure kind.");
                SharedAssert.Equal(1, failure.ExitCode, "Exit code.");

                fake.UseScenario(client, "opencode-garbage");
                failure = await ListFailureAsync(opencode, token).ConfigureAwait(false);
                SharedAssert.Equal(CliFailureKind.InvalidOutput, failure?.FailureKind, "Output without provider/model lines is InvalidOutput.");

                opencode.Executable = "polyprompt-missing-opencode";
                failure = await ListFailureAsync(opencode, token).ConfigureAwait(false);
                SharedAssert.Equal(CliFailureKind.NotInstalled, failure?.FailureKind, "Missing CLI is NotInstalled.");
            }
        }

        private static async Task<CliAgentException?> ListFailureAsync(OpenCodeAgentClient client, CancellationToken token)
        {
            try
            {
                await foreach (ModelInformation model in client.ListModelsAsync(token).ConfigureAwait(false)) { }
            }
            catch (CliAgentException ex)
            {
                return ex;
            }
            return null;
        }

        private static async Task RunPermissionConfigAsync(CancellationToken token)
        {
            using FakeCli fake = FakeCli.Create("opencode", "opencode-happy");
            using CapturingOpenCodeClient client = fake.Attach(new CapturingOpenCodeClient());

            // Restricted (the default) and ReadOnly: everything denied except read (not .env), glob, grep.
            foreach (CliPermissionMode? mode in new CliPermissionMode?[] { null, CliPermissionMode.Restricted, CliPermissionMode.ReadOnly })
            {
                CliAgentResult result = await client.RunAsync(new CliAgentRequest("hi") { PermissionMode = mode }, token).ConfigureAwait(false);
                SharedAssert.True(result.Success, "Run succeeds: " + result.Error);
                string label = mode?.ToString() ?? "default";
                SharedAssert.False(fake.Arguments.Contains("--auto"), label + ": never auto-approves.");
                JsonElement config = CapturedConfig(client, label);
                foreach (JsonElement rules in new[] { config.GetProperty("permission"), config.GetProperty("agent").GetProperty("build").GetProperty("permission") })
                {
                    AssertReadOnlyRules(rules, label);
                    SharedAssert.False(rules.TryGetProperty("edit", out _), label + ": edit falls to the catch-all deny.");
                    SharedAssert.False(rules.TryGetProperty("bash", out _), label + ": the shell falls to the catch-all deny.");
                }
            }

            // WorkspaceWrite: edits allowed, nothing outside the working directory, still no shell.
            CliAgentResult write = await client.RunAsync(new CliAgentRequest("hi") { PermissionMode = CliPermissionMode.WorkspaceWrite }, token).ConfigureAwait(false);
            SharedAssert.True(write.Success, "Run succeeds: " + write.Error);
            JsonElement writeRules = CapturedConfig(client, "WorkspaceWrite").GetProperty("permission");
            AssertReadOnlyRules(writeRules, "WorkspaceWrite");
            SharedAssert.Equal("allow", writeRules.GetProperty("edit").GetString(), "WorkspaceWrite allows edit.");
            SharedAssert.False(writeRules.TryGetProperty("bash", out _), "WorkspaceWrite keeps the shell denied.");

            // Unrestricted: --auto and OpenCode's configuration untouched.
            CliAgentResult unrestricted = await client.RunAsync(new CliAgentRequest("hi") { PermissionMode = CliPermissionMode.Unrestricted }, token).ConfigureAwait(false);
            SharedAssert.True(unrestricted.Success, "Run succeeds: " + unrestricted.Error);
            SharedAssert.True(fake.Arguments.Contains("--auto"), "Unrestricted passes --auto.");
            SharedAssert.False(fake.Arguments.Contains("--agent"), "Unrestricted does not pin the agent.");
            SharedAssert.False(client.Last!.Environment!.ContainsKey(OpenCodeAgentClient.ConfigContentVariable), "Unrestricted sets no inline configuration.");

            // OpenCode resolves its project from PWD, so PWD follows the run's working directory.
            string work = Path.Combine(fake.Directory, "pwd dir");
            Directory.CreateDirectory(work);
            await client.RunAsync(new CliAgentRequest("hi") { WorkingDirectory = work }, token).ConfigureAwait(false);
            SharedAssert.Equal(Path.GetFullPath(work), client.Last!.Environment!["PWD"], "PWD is the run's working directory.");
            await client.RunAsync(new CliAgentRequest("hi"), token).ConfigureAwait(false);
            SharedAssert.Equal(Path.GetFullPath(Directory.GetCurrentDirectory()), client.Last!.Environment!["PWD"], "PWD is the current directory when none is set.");

            // The caller's own inline configuration is kept; only the permission rules are replaced.
            client.Environment[OpenCodeAgentClient.ConfigContentVariable] =
                "{\"model\":\"opencode/big-pickle\",\"permission\":{\"*\":\"allow\"},\"agent\":{\"build\":{\"temperature\":0.1,\"permission\":{\"bash\":\"allow\"}},\"review\":{\"mode\":\"subagent\"}}}";
            await client.RunAsync(new CliAgentRequest("hi"), token).ConfigureAwait(false);
            JsonElement merged = CapturedConfig(client, "merged");
            SharedAssert.Equal("opencode/big-pickle", merged.GetProperty("model").GetString(), "Other settings are kept.");
            SharedAssert.Equal("subagent", merged.GetProperty("agent").GetProperty("review").GetProperty("mode").GetString(), "Other agents are kept.");
            SharedAssert.Equal(0.1m, merged.GetProperty("agent").GetProperty("build").GetProperty("temperature").GetDecimal(), "Other build-agent settings are kept.");
            AssertReadOnlyRules(merged.GetProperty("permission"), "merged");
            AssertReadOnlyRules(merged.GetProperty("agent").GetProperty("build").GetProperty("permission"), "merged build agent");
            SharedAssert.False(merged.GetProperty("agent").GetProperty("build").GetProperty("permission").TryGetProperty("bash", out _), "The caller's build-agent bash allow is replaced.");

            // A request value wins over the client's; invalid JSON is replaced; a null request value removes the client's.
            await client.RunAsync(new CliAgentRequest("hi") { Environment = new Dictionary<string, string?> { { OpenCodeAgentClient.ConfigContentVariable, "{not json" } } }, token).ConfigureAwait(false);
            JsonElement replaced = CapturedConfig(client, "invalid");
            SharedAssert.False(replaced.TryGetProperty("model", out _), "Invalid request configuration is replaced, not merged with the client's.");
            AssertReadOnlyRules(replaced.GetProperty("permission"), "invalid");

            await client.RunAsync(new CliAgentRequest("hi") { Environment = new Dictionary<string, string?> { { OpenCodeAgentClient.ConfigContentVariable, null } } }, token).ConfigureAwait(false);
            SharedAssert.False(CapturedConfig(client, "removed").TryGetProperty("model", out _), "A null request value drops the client's configuration.");
        }

        private static JsonElement CapturedConfig(CapturingOpenCodeClient client, string label)
        {
            SharedAssert.NotNull(client.Last?.Environment, label + ": inline configuration is set.");
            string? content = client.Last!.Environment![OpenCodeAgentClient.ConfigContentVariable];
            SharedAssert.NotNull(content, label + ": OPENCODE_CONFIG_CONTENT has a value.");
            using JsonDocument document = JsonDocument.Parse(content!);
            return document.RootElement.Clone();
        }

        private static void AssertReadOnlyRules(JsonElement rules, string label)
        {
            List<string> keys = rules.EnumerateObject().Select(p => p.Name).ToList();
            SharedAssert.Equal("*", keys[0], label + ": the catch-all comes first (last match wins).");
            SharedAssert.Equal("deny", rules.GetProperty("*").GetString(), label + ": everything else is denied.");
            JsonElement read = rules.GetProperty("read");
            SharedAssert.Equal("allow", read.GetProperty("*").GetString(), label + ": read is allowed.");
            SharedAssert.Equal("deny", read.GetProperty("*.env").GetString(), label + ": .env files are denied.");
            SharedAssert.Equal("allow", rules.GetProperty("glob").GetString(), label + ": glob is allowed.");
            SharedAssert.Equal("allow", rules.GetProperty("grep").GetString(), label + ": grep is allowed.");
            SharedAssert.Equal("deny", rules.GetProperty("external_directory").GetString(), label + ": paths outside the directory are denied.");
            foreach (JsonProperty rule in rules.EnumerateObject())
            {
                if (rule.Value.ValueKind == JsonValueKind.String) SharedAssert.True(rule.Value.GetString() != "ask", label + ": no rule asks (nobody can answer).");
            }
        }

        private static async Task RunSessionHistoryAsync(CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = CliAgentCaseKit.Attach(Profile, "opencode-happy");
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
                int session = args.IndexOf("--session");
                SharedAssert.True(session >= 0 && session + 1 < args.Count && args[session + 1] == SessionId, "History with a session id resumes it. Args: " + string.Join(" | ", args));
                SharedAssert.Equal("second", fake.StandardInput, "Only the new prompt is sent when resuming.");

                List<ChatMessage> plain = new List<ChatMessage> { ChatMessage.System("Be terse."), ChatMessage.User("What is 2+2?"), ChatMessage.Assistant("4") };
                CliAgentResult replayed = await client.RunAsync(new CliAgentRequest("And 3+3?") { History = plain }, token).ConfigureAwait(false);
                SharedAssert.True(replayed.Success, "Replayed turn succeeds: " + replayed.Error);
                SharedAssert.False(fake.Arguments.Contains("--session"), "Plain history does not resume.");
                SharedAssert.Equal(
                    "System instructions:\nBe terse.\n\nConversation so far:\n\nUser: What is 2+2?\n\nAssistant: 4\n\nCurrent request:\nAnd 3+3?",
                    fake.StandardInput,
                    "Plain history is replayed into the prompt, with system messages as instructions.");
            }
        }

        private static async Task RunStepUsageAsync(CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = CliAgentCaseKit.Attach(Profile, "opencode-happy");
            using (fake)
            using (client)
            {
                CliAgentResult happy = await client.RunAsync(new CliAgentRequest("hi"), token).ConfigureAwait(false);
                List<CliAgentEvent> steps = happy.Events.Where(e => e.Kind == CliAgentEventKind.Notice && e.ProviderEventType == "step_finish").ToList();
                SharedAssert.Equal(2, steps.Count, "One notice per step_finish.");
                SharedAssert.Equal(878, steps[0].Usage?.PromptTokens, "First step's input tokens as reported.");
                SharedAssert.Equal(0.0021m, steps[0].CostUsd, "First step's cost as reported.");
                SharedAssert.Equal(896, steps[1].Usage?.CachedPromptTokens, "Second step's cache reads as reported.");
                SharedAssert.Equal("Step finished (tool-calls).", steps[0].Text, "The notice names the finish reason.");
                SharedAssert.Equal(null, happy.Usage!.TotalTokens, "No step reported a total, so the turn has none.");

                // The recorded run: OpenCode exited before the final step's step_finish, so the turn total is unknown.
                fake.UseScenario(client, "opencode-replay");
                CliAgentResult partial = await client.RunAsync(new CliAgentRequest("hi"), token).ConfigureAwait(false);
                SharedAssert.True(partial.Success, "The recorded run succeeds: " + partial.Error);
                SharedAssert.Equal(null, partial.Usage, "Usage stays null when a step went unreported (never an under-count).");
                SharedAssert.Equal(null, partial.CostUsd, "Cost stays null when a step went unreported.");
                CliAgentEvent? reported = partial.Events.FirstOrDefault(e => e.ProviderEventType == "step_finish");
                SharedAssert.Equal(878, reported?.Usage?.PromptTokens, "The reported step still carries its usage.");

                // A field reported by only some steps is not totalled.
                fake.UseScenario(client, "opencode-tool-error");
                CliAgentResult mixed = await client.RunAsync(new CliAgentRequest("hi"), token).ConfigureAwait(false);
                SharedAssert.True(mixed.Success, "Run succeeds: " + mixed.Error);
                SharedAssert.Equal(1850, mixed.Usage?.PromptTokens, "Input tokens summed across both reported steps.");
                SharedAssert.Equal(null, mixed.Usage?.TotalTokens, "Only one step reported a total, so the turn total is unknown.");
                SharedAssert.Equal(0m, mixed.CostUsd, "Zero cost is reported as zero, not null.");
            }
        }

        private static async Task RunRecordedReplayAsync(CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = CliAgentCaseKit.Attach(Profile, "opencode-replay");
            using (fake)
            using (client)
            {
                CliAgentResult result = await client.RunAsync(new CliAgentRequest("List the files."), token).ConfigureAwait(false);
                SharedAssert.True(result.Success, "Recorded run succeeds: " + result.Error);
                SharedAssert.Equal("a.txt", result.Text, "The final text is the last message's text, not the earlier commentary.");
                SharedAssert.True(result.Events.Any(e => e.Kind == CliAgentEventKind.TextDelta && (e.Text ?? "").StartsWith("I'll list", StringComparison.Ordinal)), "Earlier commentary is still streamed.");
                CliAgentEvent? done = result.Events.FirstOrDefault(e => e.Kind == CliAgentEventKind.ToolResult);
                SharedAssert.Equal("read", done?.ToolName, "Tool name.");
                SharedAssert.True((done?.ToolOutput ?? "").Contains("a.txt", StringComparison.Ordinal), "Tool output.");
                SharedAssert.False(done!.IsError, "A completed tool is not an error.");
                CliAgentEvent? use = result.Events.FirstOrDefault(e => e.Kind == CliAgentEventKind.ToolUse);
                SharedAssert.True((use?.ToolInput ?? "").Contains("\"path\"", StringComparison.Ordinal), "Tool input is the rendered JSON input.");
                SharedAssert.Equal(null, result.Reasoning, "No reasoning was emitted.");
                SharedAssert.Equal(1, result.Events.Count(e => e.Kind == CliAgentEventKind.Started), "Started is emitted once.");

                fake.UseScenario(client, "opencode-tool-error");
                result = await client.RunAsync(new CliAgentRequest("Read missing.txt."), token).ConfigureAwait(false);
                CliAgentEvent? failedTool = result.Events.FirstOrDefault(e => e.Kind == CliAgentEventKind.ToolResult);
                SharedAssert.True(failedTool != null && failedTool.IsError, "A failed tool is an error result.");
                SharedAssert.Equal("File not found: /work/project/missing.txt", failedTool!.ToolOutput, "The tool error is the output.");
                SharedAssert.True(result.Success, "A failed tool does not fail the run.");

                fake.UseScenario(client, "opencode-error-exit");
                result = await client.RunAsync(new CliAgentRequest("hi") { Model = "opencode/no-such-model-xyz" }, token).ConfigureAwait(false);
                SharedAssert.Equal("ses_e7cb040caffecJtmL0N5Cx0SRx", result.SessionId, "Session id is reported even when the turn fails.");
                SharedAssert.Equal(1, result.ExitCode, "Exit code.");
                SharedAssert.Equal("opencode/no-such-model-xyz", result.Model, "Requested model is reported when the harness reports none.");

                fake.UseScenario(client, "opencode-unauthenticated");
                result = await client.RunAsync(new CliAgentRequest("hi"), token).ConfigureAwait(false);
                SharedAssert.Equal("invalid x-api-key (provider.auth, status 401)", result.Error, "Auth error message, type, and status.");

                fake.UseScenario(client, "opencode-rate-limited");
                result = await client.RunAsync(new CliAgentRequest("hi"), token).ConfigureAwait(false);
                SharedAssert.True((result.Error ?? "").StartsWith("Free usage limit reached.", StringComparison.Ordinal), "Rate-limit message: " + result.Error);
                SharedAssert.True((result.Error ?? "").EndsWith("(provider.free-limit, status 429)", StringComparison.Ordinal), "Rate-limit type and status: " + result.Error);

                fake.UseScenario(client, "opencode-usage-error");
                result = await client.RunAsync(new CliAgentRequest("hi"), token).ConfigureAwait(false);
                SharedAssert.Equal(CliFailureKind.Crashed, result.FailureKind, "A plain-text usage error is Crashed. Error: " + result.Error);
                SharedAssert.Equal("Unrecognized flag: --bogus in command opencode run", result.Error, "The usage error is the message.");
                SharedAssert.Equal(1, result.ExitCode, "Exit code.");

                fake.UseScenario(client, "opencode-garbage");
                result = await client.RunAsync(new CliAgentRequest("hi"), token).ConfigureAwait(false);
                SharedAssert.Equal(CliFailureKind.InvalidOutput, result.FailureKind, "Exit 0 with nothing parseable is InvalidOutput.");
                SharedAssert.Equal(2, result.MalformedLines, "Both lines are malformed.");
            }
        }

        /// <summary>
        /// Records the invocation the client builds, so the inline permission configuration can be inspected.
        /// </summary>
        private sealed class CapturingOpenCodeClient : OpenCodeAgentClient
        {
            public CliInvocation? Last { get; private set; }

            protected override CliInvocation BuildInvocation(CliRunState state)
            {
                Last = base.BuildInvocation(state);
                return Last;
            }
        }
    }
}
