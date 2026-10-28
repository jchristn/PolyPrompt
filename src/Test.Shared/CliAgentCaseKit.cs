namespace Test.Shared
{
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using Touchstone.Core;

    /// <summary>
    /// Shared fake-CLI cases every agent harness adapter must pass: happy path, streaming, non-zero exit with a structured
    /// error, stderr-only failure, malformed line, cancellation (process tree killed), timeout, not installed, not
    /// authenticated, rate limited, oversized output, prompt over standard input, exact argument lists, environment and
    /// working directory, call recording, connectivity, and request validation.
    /// </summary>
    internal static class CliAgentCaseKit
    {
        #region Public-Methods

        public static List<TestCaseDescriptor> Create(string suiteId, CliAgentProfile p)
        {
            string prefix = "cli_" + p.Id + "_";
            return new List<TestCaseDescriptor>
            {
                Case(suiteId, prefix + "happy_path", p.Id + ": happy path maps text, reasoning, tools, usage, cost, and session id", t => RunHappyPathAsync(p, t)),
                Case(suiteId, prefix + "usage_parsing", p.Id + ": usage and cost are reported exactly as the harness reported them", t => RunUsageAsync(p, t)),
                Case(suiteId, prefix + "streaming", p.Id + ": streaming yields the same events in order and ends with Completed", t => RunStreamingAsync(p, t)),
                Case(suiteId, prefix + "error_exit", p.Id + ": non-zero exit with a structured error is a provider failure with its message", t => RunErrorExitAsync(p, t)),
                Case(suiteId, prefix + "stderr_only", p.Id + ": non-zero exit with only stderr is a crash carrying stderr", t => RunStderrOnlyAsync(p, t)),
                Case(suiteId, prefix + "malformed_line", p.Id + ": a malformed JSON line becomes a notice and the run still completes", t => RunMalformedAsync(p, t)),
                Case(suiteId, prefix + "cancellation", p.Id + ": cancellation mid-stream throws and kills the whole process tree", t => RunCancellationAsync(p, t)),
                Case(suiteId, prefix + "timeout", p.Id + ": the timeout kills the process tree and reports TimedOut", t => RunTimeoutAsync(p, t)),
                Case(suiteId, prefix + "not_installed", p.Id + ": a missing executable is NotInstalled for runs and connectivity", t => RunNotInstalledAsync(p, t)),
                Case(suiteId, prefix + "not_authenticated", p.Id + ": rejected credentials are NotAuthenticated, distinct from not installed", t => RunNotAuthenticatedAsync(p, t)),
                Case(suiteId, prefix + "rate_limited", p.Id + ": a usage or rate limit is RateLimited", t => RunRateLimitedAsync(p, t)),
                Case(suiteId, prefix + "oversized_output", p.Id + ": oversized output is capped and the tree killed, not buffered", t => RunOversizedAsync(p, t)),
                Case(suiteId, prefix + "stdin_prompt", p.Id + ": a long prompt is delivered without hitting argument limits", t => RunLongPromptAsync(p, t)),
                Case(suiteId, prefix + "argv", p.Id + ": argument lists match exactly for each option combination", t => RunArgvAsync(p, t)),
                Case(suiteId, prefix + "environment_and_directory", p.Id + ": environment and working directory reach the process; ExtraArguments are appended", t => RunEnvironmentAsync(p, t)),
                Case(suiteId, prefix + "call_details", p.Id + ": each process is recorded as a ProcessCallDetail", t => RunCallDetailsAsync(p, t)),
                Case(suiteId, prefix + "connectivity", p.Id + ": connectivity reports installed, version, and login", t => RunConnectivityAsync(p, t)),
                Case(suiteId, prefix + "validation", p.Id + ": invalid requests are rejected before a process starts", t => RunValidationAsync(p, t)),
                Case(suiteId, prefix + "permission_default", p.Id + ": the default permission mode is Restricted and never bypasses approvals", t => RunPermissionDefaultAsync(p, t)),
            };
        }

        public static TestCaseDescriptor Case(string suiteId, string caseId, string displayName, Func<CancellationToken, Task> executeAsync)
        {
            return new TestCaseDescriptor(suiteId, caseId, displayName, executeAsync, new[] { "local", "fake-cli" });
        }

        /// <summary>
        /// Build a client attached to a fake CLI playing <paramref name="scenario"/>.
        /// </summary>
        public static (FakeCli Fake, CliAgentClientBase Client) Attach(CliAgentProfile p, string scenario)
        {
            FakeCli fake = FakeCli.Create(p.ExecutableName, scenario);
            CliAgentClientBase client = fake.Attach(p.Create());
            client.TimeoutMs = 30000;
            return (fake, client);
        }

        public static void AssertTerminal(CliAgentResult result, CliAgentEventKind kind, string context)
        {
            SharedAssert.True(result.Events.Count > 0, context + ": there should be at least the terminal event.");
            SharedAssert.Equal(kind, result.Events[result.Events.Count - 1].Kind, context + ": last event kind.");
            SharedAssert.Equal(1, result.Events.Count(e => e.Kind == CliAgentEventKind.Completed || e.Kind == CliAgentEventKind.Failed), context + ": exactly one terminal event.");
        }

        #endregion

        #region Cases

        private static async Task RunHappyPathAsync(CliAgentProfile p, CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = Attach(p, p.HappyScenario);
            using (fake)
            using (client)
            {
                CliAgentResult result = await client.RunAsync(new CliAgentRequest("List the files."), token).ConfigureAwait(false);

                SharedAssert.True(result.Success, "Happy path should succeed: " + result.Error);
                SharedAssert.True(result.Succeeded, "Succeeded mirrors Success.");
                SharedAssert.Equal(CliFailureKind.None, result.FailureKind, "No failure kind.");
                SharedAssert.Equal(0, result.ExitCode, "Exit code.");
                SharedAssert.Equal(p.HappyText, result.Text, "Final text.");
                SharedAssert.Equal(p.HappySessionId, result.SessionId, "Session id.");
                SharedAssert.Equal(0, result.MalformedLines, "No malformed lines.");
                SharedAssert.False(result.TimedOut, "Not timed out.");
                SharedAssert.True(result.RawOutput.Length > 0, "Raw output is retained.");
                SharedAssert.Equal(null, result.Error, "No error.");
                AssertTerminal(result, CliAgentEventKind.Completed, "happy");

                CliAgentEvent completed = result.Events[result.Events.Count - 1];
                SharedAssert.Equal(p.HappyText, completed.Text, "Completed event carries the final text.");
                SharedAssert.Equal(0, completed.ExitCode, "Completed event carries the exit code.");
                SharedAssert.Equal(p.HappySessionId, completed.SessionId, "Completed event carries the session id.");

                SharedAssert.True(result.Events.Any(e => e.Kind == CliAgentEventKind.TextDelta), "There should be text events.");
                if (p.HappySessionId != null)
                {
                    SharedAssert.True(result.Events.Any(e => e.Kind == CliAgentEventKind.Started && e.SessionId == p.HappySessionId), "There should be a Started event with the session id.");
                    SharedAssert.True(result.ProviderState.HasValue, "ProviderState holds the session id.");
                    SharedAssert.Equal(p.HappySessionId, result.ProviderState!.Value.GetProperty("sessionId").GetString(), "ProviderState sessionId.");
                }

                if (p.HappyReasoningContains != null)
                {
                    SharedAssert.True(result.Reasoning != null && result.Reasoning.Contains(p.HappyReasoningContains, StringComparison.Ordinal), "Reasoning should contain '" + p.HappyReasoningContains + "' but was: " + result.Reasoning);
                    SharedAssert.True(result.Events.Any(e => e.Kind == CliAgentEventKind.ReasoningDelta), "There should be reasoning events.");
                }

                if (p.HappyToolName != null)
                {
                    CliAgentEvent? use = result.Events.FirstOrDefault(e => e.Kind == CliAgentEventKind.ToolUse);
                    CliAgentEvent? done = result.Events.FirstOrDefault(e => e.Kind == CliAgentEventKind.ToolResult);
                    SharedAssert.NotNull(use, "There should be a ToolUse event.");
                    SharedAssert.NotNull(done, "There should be a ToolResult event.");
                    SharedAssert.Equal(p.HappyToolName, use!.ToolName, "Tool name on ToolUse.");
                    SharedAssert.True(result.Events.IndexOf(use) < result.Events.IndexOf(done!), "ToolUse precedes ToolResult.");
                    if (use.ToolCallId != null && done!.ToolCallId != null) SharedAssert.Equal(use.ToolCallId, done.ToolCallId, "Tool call ids pair up.");
                }

                if (p.HappyModel != null) SharedAssert.Equal(p.HappyModel, result.Model, "Reported model.");

                foreach (CliAgentEvent evt in result.Events.Where(e => e.Kind != CliAgentEventKind.Completed))
                {
                    SharedAssert.NotNull(evt.Raw, "Parsed events keep their raw line (" + evt.Kind + ").");
                    SharedAssert.NotNull(evt.ProviderEventType, "Parsed events keep the provider event type (" + evt.Kind + ").");
                }

                if (p.PromptOnStandardInput)
                {
                    SharedAssert.True((fake.StandardInput ?? "").Contains("List the files.", StringComparison.Ordinal), "The prompt should arrive on stdin. Stdin: " + fake.StandardInput);
                    SharedAssert.False(fake.Arguments.Any(a => a.Contains("List the files.", StringComparison.Ordinal)), "The prompt should not be in argv.");
                }
                else
                {
                    SharedAssert.True(fake.Arguments.Any(a => a.Contains("List the files.", StringComparison.Ordinal)), "The prompt should be in argv.");
                }
            }
        }

        private static async Task RunUsageAsync(CliAgentProfile p, CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = Attach(p, p.HappyScenario);
            using (fake)
            using (client)
            {
                CliAgentResult result = await client.RunAsync(new CliAgentRequest("usage"), token).ConfigureAwait(false);
                SharedAssert.True(result.Success, "Run should succeed: " + result.Error);

                if (p.HappyUsage == null)
                {
                    SharedAssert.Equal(null, result.Usage, "Usage stays null when not reported.");
                }
                else
                {
                    SharedAssert.NotNull(result.Usage, "Usage should be reported.");
                    SharedAssert.Equal(p.HappyUsage.PromptTokens, result.Usage!.PromptTokens, "PromptTokens.");
                    SharedAssert.Equal(p.HappyUsage.CompletionTokens, result.Usage.CompletionTokens, "CompletionTokens.");
                    SharedAssert.Equal(p.HappyUsage.TotalTokens, result.Usage.TotalTokens, "TotalTokens (null unless reported).");
                    SharedAssert.Equal(p.HappyUsage.CachedPromptTokens, result.Usage.CachedPromptTokens, "CachedPromptTokens.");
                    SharedAssert.Equal(p.HappyUsage.CacheCreationTokens, result.Usage.CacheCreationTokens, "CacheCreationTokens.");
                    SharedAssert.Equal(p.HappyUsage.ReasoningTokens, result.Usage.ReasoningTokens, "ReasoningTokens.");
                    CliAgentEvent completed = result.Events[result.Events.Count - 1];
                    SharedAssert.NotNull(completed.Usage, "Completed event carries usage.");
                }

                SharedAssert.Equal(p.HappyCostUsd, result.CostUsd, "Cost is exactly as reported (null when not reported, never estimated).");
            }
        }

        private static async Task RunStreamingAsync(CliAgentProfile p, CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = Attach(p, p.HappyScenario);
            using (fake)
            using (client)
            {
                CliAgentResult result = await client.RunAsync(new CliAgentRequest("stream"), token).ConfigureAwait(false);

                List<CliAgentEvent> streamed = new List<CliAgentEvent>();
                await foreach (CliAgentEvent evt in client.RunStreamingAsync(new CliAgentRequest("stream"), token).ConfigureAwait(false))
                {
                    streamed.Add(evt);
                }

                SharedAssert.Equal(result.Events.Count, streamed.Count, "Streaming yields the same number of events as RunAsync records.");
                for (int i = 0; i < streamed.Count; i++)
                {
                    SharedAssert.Equal(result.Events[i].Kind, streamed[i].Kind, "Event " + i + " kind.");
                    SharedAssert.Equal(result.Events[i].Text, streamed[i].Text, "Event " + i + " text.");
                }
                SharedAssert.Equal(CliAgentEventKind.Completed, streamed[streamed.Count - 1].Kind, "The stream ends with Completed.");
                SharedAssert.Equal(p.HappyText, streamed[streamed.Count - 1].Text, "Completed carries the final text.");

                // Breaking out early kills the process and records it.
                using FakeCli slow = FakeCli.Create(p.ExecutableName, p.SlowScenario);
                using CliAgentClientBase slowClient = slow.Attach(p.Create());
                await foreach (CliAgentEvent evt in slowClient.RunStreamingAsync(new CliAgentRequest("abandon"), token).ConfigureAwait(false))
                {
                    break;
                }
                int? child = slow.ChildProcessId;
                if (child.HasValue) SharedAssert.True(await FakeCli.WaitForProcessExitAsync(child.Value).ConfigureAwait(false), "Abandoning the stream kills the process tree.");
                ProcessCallDetail abandoned = (ProcessCallDetail)slowClient.CallDetails.Last();
                SharedAssert.True(abandoned.Cancelled, "An abandoned stream is recorded as cancelled.");
            }
        }

        private static async Task RunErrorExitAsync(CliAgentProfile p, CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = Attach(p, p.ErrorExitScenario);
            using (fake)
            using (client)
            {
                CliAgentResult result = await client.RunAsync(new CliAgentRequest("fail"), token).ConfigureAwait(false);
                SharedAssert.False(result.Success, "Error exit should fail.");
                SharedAssert.Equal(p.ErrorExitKind, result.FailureKind, "Failure kind for a structured error. Error: " + result.Error);
                SharedAssert.True(result.Error != null && result.Error.Contains(p.ErrorExitMessageContains, StringComparison.Ordinal), "Error should contain '" + p.ErrorExitMessageContains + "' but was: " + result.Error);
                AssertTerminal(result, CliAgentEventKind.Failed, "error exit");
                CliAgentEvent failed = result.Events[result.Events.Count - 1];
                SharedAssert.Equal(p.ErrorExitKind, failed.FailureKind, "Failed event carries the failure kind.");
                SharedAssert.Equal(result.Error, failed.Text, "Failed event carries the error.");
                SharedAssert.True(failed.IsError, "Failed event is an error.");
            }
        }

        private static async Task RunStderrOnlyAsync(CliAgentProfile p, CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = Attach(p, p.StderrOnlyScenario);
            using (fake)
            using (client)
            {
                CliAgentResult result = await client.RunAsync(new CliAgentRequest("stderr"), token).ConfigureAwait(false);
                SharedAssert.False(result.Success, "Stderr-only failure should fail.");
                SharedAssert.Equal(CliFailureKind.Crashed, result.FailureKind, "Failure kind. Error: " + result.Error);
                SharedAssert.True(result.ExitCode.HasValue && result.ExitCode.Value != 0, "Non-zero exit code.");
                SharedAssert.True(result.Error != null && result.Error.Contains(p.StderrOnlyMessageContains, StringComparison.Ordinal), "Error should carry stderr ('" + p.StderrOnlyMessageContains + "') but was: " + result.Error);
                SharedAssert.True(result.StandardError.Contains(p.StderrOnlyMessageContains, StringComparison.Ordinal), "StandardError is captured.");
                AssertTerminal(result, CliAgentEventKind.Failed, "stderr only");
            }
        }

        private static async Task RunMalformedAsync(CliAgentProfile p, CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = Attach(p, p.MalformedScenario);
            using (fake)
            using (client)
            {
                CliAgentResult result = await client.RunAsync(new CliAgentRequest("malformed"), token).ConfigureAwait(false);
                SharedAssert.True(result.Success, "A malformed line should not fail the run: " + result.Error);
                SharedAssert.Equal(1, result.MalformedLines, "One malformed line.");
                SharedAssert.True(result.Events.Any(e => e.Kind == CliAgentEventKind.Notice && e.IsError && (e.Text ?? "").StartsWith("Unparseable", StringComparison.Ordinal)), "The malformed line becomes an error notice.");
                SharedAssert.True(result.Text.Length > 0, "Text after the malformed line is still parsed.");
                AssertTerminal(result, CliAgentEventKind.Completed, "malformed");
            }
        }

        private static async Task RunCancellationAsync(CliAgentProfile p, CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = Attach(p, p.SlowScenario);
            using (fake)
            using (client)
            {
                using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                bool threw = false;
                int events = 0;
                try
                {
                    await foreach (CliAgentEvent evt in client.RunStreamingAsync(new CliAgentRequest("cancel"), cts.Token).ConfigureAwait(false))
                    {
                        events++;
                        if (events == 1)
                        {
                            await WaitForAsync(() => fake.ChildProcessId.HasValue, 5000).ConfigureAwait(false);
                            cts.Cancel();
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    threw = true;
                }

                SharedAssert.True(threw, "Cancellation surfaces as OperationCanceledException.");
                SharedAssert.True(events >= 1, "At least one event arrived before cancelling.");
                int? child = fake.ChildProcessId;
                SharedAssert.True(child.HasValue, "The scenario started a child process.");
                SharedAssert.True(await FakeCli.WaitForProcessExitAsync(child!.Value).ConfigureAwait(false), "The child process is killed with the tree (no orphan).");
                int? shim = fake.ProcessId;
                if (shim.HasValue) SharedAssert.True(await FakeCli.WaitForProcessExitAsync(shim.Value).ConfigureAwait(false), "The harness process is killed.");
                ProcessCallDetail detail = (ProcessCallDetail)client.CallDetails.Last();
                SharedAssert.True(detail.Cancelled, "Call detail records the cancellation.");
                SharedAssert.False(detail.Success, "A cancelled call is not a success.");

                // RunAsync also throws on cancellation.
                using CancellationTokenSource quick = CancellationTokenSource.CreateLinkedTokenSource(token);
                quick.CancelAfter(300);
                await SharedAssert.ThrowsAsync<OperationCanceledException>(() => client.RunAsync(new CliAgentRequest("cancel"), quick.Token), "RunAsync throws on cancellation.").ConfigureAwait(false);
            }
        }

        private static async Task RunTimeoutAsync(CliAgentProfile p, CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = Attach(p, p.SlowScenario);
            using (fake)
            using (client)
            {
                CliAgentResult result = await client.RunAsync(new CliAgentRequest("timeout") { TimeoutMs = 750 }, token).ConfigureAwait(false);
                SharedAssert.False(result.Success, "A timed-out run fails.");
                SharedAssert.Equal(CliFailureKind.TimedOut, result.FailureKind, "Failure kind. Error: " + result.Error);
                SharedAssert.True(result.TimedOut, "TimedOut flag.");
                SharedAssert.True(result.OverallRuntimeMs < 10000, "The run ended promptly after the timeout (" + result.OverallRuntimeMs + " ms).");
                int? child = fake.ChildProcessId;
                if (child.HasValue) SharedAssert.True(await FakeCli.WaitForProcessExitAsync(child.Value).ConfigureAwait(false), "The timeout kills the process tree.");
                AssertTerminal(result, CliAgentEventKind.Failed, "timeout");
                SharedAssert.True(((ProcessCallDetail)client.CallDetails.Last()).TimedOut, "Call detail records the timeout.");

                // The client-level timeout applies when the request sets none.
                client.TimeoutMs = 750;
                CliAgentResult clientTimeout = await client.RunAsync(new CliAgentRequest("timeout"), token).ConfigureAwait(false);
                SharedAssert.Equal(CliFailureKind.TimedOut, clientTimeout.FailureKind, "Client TimeoutMs applies.");
            }
        }

        private static async Task RunNotInstalledAsync(CliAgentProfile p, CancellationToken token)
        {
            using CliAgentClientBase client = p.Create();
            client.Executable = "polyprompt-missing-" + p.Id + "-" + Guid.NewGuid().ToString("N");

            CliAgentResult result = await client.RunAsync(new CliAgentRequest("hello"), token).ConfigureAwait(false);
            SharedAssert.False(result.Success, "Missing executable fails.");
            SharedAssert.Equal(CliFailureKind.NotInstalled, result.FailureKind, "Failure kind.");
            SharedAssert.Equal(null, result.ExitCode, "No exit code.");
            SharedAssert.True(result.Error != null && result.Error.Contains("not found", StringComparison.Ordinal), "Error explains the executable was not found: " + result.Error);
            AssertTerminal(result, CliAgentEventKind.Failed, "not installed");

            List<CliAgentEvent> streamed = new List<CliAgentEvent>();
            await foreach (CliAgentEvent evt in client.RunStreamingAsync(new CliAgentRequest("hello"), token).ConfigureAwait(false)) streamed.Add(evt);
            SharedAssert.Equal(1, streamed.Count, "Streaming a missing executable yields only Failed.");
            SharedAssert.Equal(CliFailureKind.NotInstalled, streamed[0].FailureKind, "Streamed failure kind.");

            CliConnectivityResult connectivity = await client.ValidateConnectivityAsync(token).ConfigureAwait(false);
            SharedAssert.Equal(CliConnectivityStatus.NotInstalled, connectivity.Status, "Connectivity is NotInstalled.");
            SharedAssert.False(connectivity.Success, "Connectivity is not a success.");
            SharedAssert.Equal(null, connectivity.ExecutablePath, "No executable path.");

            ProcessCallDetail detail = (ProcessCallDetail)client.CallDetails.First();
            SharedAssert.Equal(CliFailureKind.NotInstalled, detail.FailureKind, "Call detail failure kind.");
            SharedAssert.Equal("EXEC", detail.Method, "Call detail method.");

            // A path that does not exist behaves the same.
            client.Executable = Path.Combine(Path.GetTempPath(), "polyprompt-missing-dir", p.ExecutableName);
            CliAgentResult byPath = await client.RunAsync(new CliAgentRequest("hello"), token).ConfigureAwait(false);
            SharedAssert.Equal(CliFailureKind.NotInstalled, byPath.FailureKind, "Missing absolute path is NotInstalled.");
        }

        private static async Task RunNotAuthenticatedAsync(CliAgentProfile p, CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = Attach(p, p.UnauthenticatedScenario);
            using (fake)
            using (client)
            {
                CliAgentResult result = await client.RunAsync(new CliAgentRequest("hello"), token).ConfigureAwait(false);
                SharedAssert.False(result.Success, "Unauthenticated run fails.");
                SharedAssert.Equal(CliFailureKind.NotAuthenticated, result.FailureKind, "Failure kind. Error: " + result.Error);
                AssertTerminal(result, CliAgentEventKind.Failed, "not authenticated");

                if (p.HasAuthenticationStatus)
                {
                    CliConnectivityResult connectivity = await client.ValidateConnectivityAsync(token).ConfigureAwait(false);
                    SharedAssert.Equal(CliConnectivityStatus.NotAuthenticated, connectivity.Status, "Connectivity reports NotAuthenticated. Message: " + connectivity.Message);
                    SharedAssert.NotNull(connectivity.ExecutablePath, "The executable was found.");
                }
            }
        }

        private static async Task RunRateLimitedAsync(CliAgentProfile p, CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = Attach(p, p.RateLimitedScenario);
            using (fake)
            using (client)
            {
                CliAgentResult result = await client.RunAsync(new CliAgentRequest("hello"), token).ConfigureAwait(false);
                SharedAssert.False(result.Success, "Rate-limited run fails.");
                SharedAssert.Equal(CliFailureKind.RateLimited, result.FailureKind, "Failure kind. Error: " + result.Error);
                AssertTerminal(result, CliAgentEventKind.Failed, "rate limited");
            }
        }

        private static async Task RunOversizedAsync(CliAgentProfile p, CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = Attach(p, p.HugeScenario);
            using (fake)
            using (client)
            {
                CliAgentResult result = await client.RunAsync(new CliAgentRequest("huge") { MaxOutputCharacters = 100_000 }, token).ConfigureAwait(false);
                SharedAssert.False(result.Success, "Oversized output fails.");
                SharedAssert.Equal(CliFailureKind.OutputLimitExceeded, result.FailureKind, "Failure kind. Error: " + result.Error);
                SharedAssert.True(result.OutputTruncated, "OutputTruncated flag.");
                SharedAssert.True(result.RawOutput.Length <= 100_000, "Raw output is bounded (" + result.RawOutput.Length + ").");
                SharedAssert.True(((ProcessCallDetail)client.CallDetails.Last()).OutputTruncated, "Call detail records the truncation.");
                AssertTerminal(result, CliAgentEventKind.Failed, "oversized");
            }
        }

        private static async Task RunLongPromptAsync(CliAgentProfile p, CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = Attach(p, p.HappyScenario);
            using (fake)
            using (client)
            {
                string prompt = "BEGIN " + new string('p', 3_000_000) + " END";
                CliAgentResult result = await client.RunAsync(new CliAgentRequest(prompt), token).ConfigureAwait(false);

                if (p.PromptOnStandardInput)
                {
                    SharedAssert.True(result.Success, "A 3 MB prompt runs: " + result.Error);
                    string stdin = fake.StandardInput ?? "";
                    SharedAssert.True(stdin.Contains(prompt, StringComparison.Ordinal), "The whole prompt arrives on stdin (" + stdin.Length + " chars).");
                    SharedAssert.True(fake.Arguments.Sum(a => a.Length) < 10_000, "Arguments stay small.");
                }
                else
                {
                    // The CLI only accepts the prompt as an argument; the operating system argument limit applies.
                    SharedAssert.True(result.Success || result.FailureKind == CliFailureKind.NotInstalled, "Argument-only prompts either run or fail to start: " + result.Error);
                }
            }
        }

        private static async Task RunArgvAsync(CliAgentProfile p, CancellationToken token)
        {
            SharedAssert.True(p.ArgvCases.Count > 0, "The adapter defines argument-list expectations.");
            foreach (CliAgentProfile.ArgvCase argvCase in p.ArgvCases)
            {
                (FakeCli fake, CliAgentClientBase client) = Attach(p, p.HappyScenario);
                using (fake)
                using (client)
                {
                    argvCase.Client?.Invoke(client);
                    CliAgentRequest request = new CliAgentRequest("hello");
                    argvCase.Request?.Invoke(request);

                    CliAgentResult result = await client.RunAsync(request, token).ConfigureAwait(false);
                    SharedAssert.True(result.Success, argvCase.Description + ": run should succeed: " + result.Error);

                    List<string> actual = fake.Arguments;
                    SharedAssert.Equal(string.Join(" | ", argvCase.Expected), string.Join(" | ", actual), argvCase.Description + ": argv.");

                    ProcessCallDetail detail = (ProcessCallDetail)client.CallDetails.Last();
                    SharedAssert.Equal(string.Join(" | ", argvCase.Expected), string.Join(" | ", detail.Arguments), argvCase.Description + ": recorded arguments.");

                    if (argvCase.ExpectedStandardInput != null)
                        SharedAssert.Equal(argvCase.ExpectedStandardInput, fake.StandardInput, argvCase.Description + ": stdin.");
                }
            }
        }

        private static async Task RunEnvironmentAsync(CliAgentProfile p, CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = Attach(p, p.HappyScenario);
            using (fake)
            using (client)
            {
                string work = Path.Combine(fake.Directory, "work dir");
                Directory.CreateDirectory(work);
                client.Environment["POLYPROMPT_FAKE_CLI_ECHO"] = "from-client";

                CliAgentResult result = await client.RunAsync(new CliAgentRequest("env") { WorkingDirectory = work }, token).ConfigureAwait(false);
                SharedAssert.True(result.Success, "Run should succeed: " + result.Error);
                SharedAssert.Equal("from-client", fake.EchoedEnvironment, "Client environment reaches the process.");
                SharedAssert.Equal(Path.GetFullPath(work).TrimEnd('/'), ResolveLinks(fake.WorkingDirectory ?? ""), "Working directory.");

                CliAgentRequest overriding = new CliAgentRequest("env")
                {
                    Environment = new Dictionary<string, string?> { { "POLYPROMPT_FAKE_CLI_ECHO", "from-request" } },
                    ExtraArguments = new Dictionary<string, string?> { { "--polyprompt-extra", "value one" }, { "--polyprompt-flag", null } },
                };
                result = await client.RunAsync(overriding, token).ConfigureAwait(false);
                SharedAssert.True(result.Success, "Run should succeed: " + result.Error);
                SharedAssert.Equal("from-request", fake.EchoedEnvironment, "Request environment overrides the client's.");
                List<string> args = fake.Arguments;
                int extra = args.IndexOf("--polyprompt-extra");
                SharedAssert.True(extra >= 0 && extra + 1 < args.Count && args[extra + 1] == "value one", "ExtraArguments pass option and value as separate arguments (no shell quoting). Args: " + string.Join(" | ", args));
                SharedAssert.True(args.Contains("--polyprompt-flag"), "A flag without a value is passed alone.");

                client.Environment["POLYPROMPT_FAKE_CLI_ECHO"] = null;
                result = await client.RunAsync(new CliAgentRequest("env"), token).ConfigureAwait(false);
                SharedAssert.Equal("", fake.EchoedEnvironment, "A null value removes the variable.");
            }
        }

        private static async Task RunCallDetailsAsync(CliAgentProfile p, CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = Attach(p, p.HappyScenario);
            using (fake)
            using (client)
            {
                CliAgentResult result = await client.RunAsync(new CliAgentRequest("details"), token).ConfigureAwait(false);
                SharedAssert.True(result.Success, "Run should succeed: " + result.Error);

                List<CallDetail> details = client.CallDetails;
                SharedAssert.Equal(1, details.Count, "One process recorded.");
                ProcessCallDetail detail = details[0] as ProcessCallDetail ?? throw new TestFailureException("CallDetails should hold a ProcessCallDetail.");
                SharedAssert.Equal("EXEC", detail.Method, "Method.");
                SharedAssert.Equal(fake.ExecutablePath, detail.Url, "Url is the resolved executable path.");
                SharedAssert.Equal(p.ExecutableName, detail.Executable, "Executable as configured.");
                SharedAssert.Equal(0, detail.StatusCode, "StatusCode is the exit code.");
                SharedAssert.Equal(0, detail.ExitCode, "ExitCode.");
                SharedAssert.True(detail.Success, "Success.");
                SharedAssert.Equal(null, detail.Error, "No error.");
                SharedAssert.True(detail.ProcessId.HasValue && detail.ProcessId.Value > 0, "Process id.");
                SharedAssert.True((detail.ResponseBody ?? "").Length > 0, "ResponseBody is stdout.");
                SharedAssert.True((detail.RequestBody ?? "").StartsWith("[", StringComparison.Ordinal), "RequestBody is the argv JSON array.");
                SharedAssert.True(detail.ResponseTimeMs.HasValue, "Timing recorded.");
                if (p.PromptOnStandardInput) SharedAssert.True((detail.StandardInput ?? "").Contains("details", StringComparison.Ordinal), "StandardInput recorded.");

                // Snapshots are detached copies that keep the derived type.
                detail.Arguments.Clear();
                SharedAssert.True(((ProcessCallDetail)client.CallDetails[0]).Arguments.Count > 0, "Snapshots are detached.");

                client.MaxCallDetails = 0;
                await client.RunAsync(new CliAgentRequest("details"), token).ConfigureAwait(false);
                SharedAssert.Equal(0, client.CallDetails.Count, "MaxCallDetails = 0 disables recording.");
            }
        }

        private static async Task RunConnectivityAsync(CliAgentProfile p, CancellationToken token)
        {
            (FakeCli fake, CliAgentClientBase client) = Attach(p, p.HappyScenario);
            using (fake)
            using (client)
            {
                CliConnectivityResult result = await client.ValidateConnectivityAsync(token).ConfigureAwait(false);
                SharedAssert.Equal(CliConnectivityStatus.Ok, result.Status, "Connectivity Ok. Message: " + result.Message);
                SharedAssert.True(result.Success, "Success.");
                SharedAssert.Equal(fake.ExecutablePath, result.ExecutablePath, "Executable path.");
                SharedAssert.NotEmpty(result.Version, "Version reported.");
                SharedAssert.Equal(p.HasAuthenticationStatus, result.AuthenticationVerified, "AuthenticationVerified reflects whether the CLI can report its login.");
                SharedAssert.True(fake.Invocations.Count >= 1, "The version command ran.");

                using FakeCli broken = FakeCli.Create(p.ExecutableName, "generic-version-broken");
                using CliAgentClientBase brokenClient = broken.Attach(p.Create());
                CliConnectivityResult error = await brokenClient.ValidateConnectivityAsync(token).ConfigureAwait(false);
                SharedAssert.Equal(CliConnectivityStatus.Error, error.Status, "A failing version command is Error, distinct from NotInstalled.");

                using CancellationTokenSource cancelled = new CancellationTokenSource();
                cancelled.Cancel();
                await SharedAssert.ThrowsAsync<OperationCanceledException>(() => client.ValidateConnectivityAsync(cancelled.Token), "Connectivity rethrows caller cancellation.").ConfigureAwait(false);
            }
        }

        private static async Task RunValidationAsync(CliAgentProfile p, CancellationToken token)
        {
            using CliAgentClientBase client = p.Create();
            await SharedAssert.ThrowsAsync<ArgumentNullException>(() => client.RunAsync(null!, token), "Null request.").ConfigureAwait(false);
            await SharedAssert.ThrowsAsync<ArgumentException>(() => client.RunAsync(new CliAgentRequest(""), token), "Empty prompt.").ConfigureAwait(false);
            await SharedAssert.ThrowsAsync<ArgumentException>(() => client.RunAsync(new CliAgentRequest("   "), token), "Whitespace prompt.").ConfigureAwait(false);
            await SharedAssert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.RunAsync(new CliAgentRequest("x") { TimeoutMs = 0 }, token), "Zero timeout.").ConfigureAwait(false);
            await SharedAssert.ThrowsAsync<ArgumentException>(() => client.RunAsync(new CliAgentRequest("x") { History = new List<ChatMessage> { null! } }, token), "Null history entry.").ConfigureAwait(false);
            SharedAssert.True(Throws<ArgumentException>(() => client.RunStreamingAsync(new CliAgentRequest(""), token)), "Streaming validates eagerly.");
            SharedAssert.True(Throws<ArgumentOutOfRangeException>(() => new CliAgentRequest("x") { MaxOutputCharacters = 0 }), "MaxOutputCharacters must be positive.");
            SharedAssert.True(Throws<ArgumentException>(() => client.Executable = " "), "Executable is required.");
            SharedAssert.True(Throws<ArgumentOutOfRangeException>(() => client.TimeoutMs = 0), "TimeoutMs must be positive.");
            SharedAssert.Equal(0, client.CallDetails.Count, "Nothing ran.");
        }

        private static Task RunPermissionDefaultAsync(CliAgentProfile p, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using CliAgentClientBase client = p.Create();
            SharedAssert.Equal(CliPermissionMode.Restricted, client.PermissionMode, "Default permission mode.");
            SharedAssert.Equal(null, new CliAgentRequest("x").PermissionMode, "Requests inherit the client's mode by default.");
            return Task.CompletedTask;
        }

        #endregion

        #region Helpers

        public static async Task WaitForAsync(Func<bool> condition, int timeoutMs)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(25).ConfigureAwait(false);
        }

        private static bool Throws<TException>(Action action) where TException : Exception
        {
            try
            {
                action();
                return false;
            }
            catch (TException)
            {
                return true;
            }
        }

        private static string ResolveLinks(string path)
        {
            // macOS reports /private/var for /var temp paths.
            string trimmed = path.TrimEnd('/');
            if (trimmed.StartsWith("/private/", StringComparison.Ordinal) && !Path.GetTempPath().StartsWith("/private/", StringComparison.Ordinal))
                return trimmed.Substring("/private".Length);
            return trimmed;
        }

        #endregion
    }
}
