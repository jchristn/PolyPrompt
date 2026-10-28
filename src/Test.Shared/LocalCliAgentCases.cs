namespace Test.Shared
{
    using System.Text.Json;
    using PolyPrompt.Clients;
    using PolyPrompt.Helpers;
    using PolyPrompt.Models;
    using Touchstone.Core;

    /// <summary>
    /// Cases for the agent harness foundation: <see cref="ProcessRunner"/> against small shell shims (echo, false, sleep,
    /// partial lines, huge output, kill on cancel, stdin, stderr bounds, PATH resolution), and the model types
    /// (<see cref="ProcessCallDetail"/> polymorphism, source-generated JSON, defaults).
    /// </summary>
    internal static class LocalCliAgentCases
    {
        public static IEnumerable<TestCaseDescriptor> Create(string suiteId)
        {
            return new List<TestCaseDescriptor>
            {
                CliAgentCaseKit.Case(suiteId, "process_runner_lines", "ProcessRunner returns stdout lines (LF and CRLF, final partial line) and the exit code", RunLinesAsync),
                CliAgentCaseKit.Case(suiteId, "process_runner_exit_codes", "ProcessRunner reports non-zero exit codes and bounded stderr", RunExitCodesAsync),
                CliAgentCaseKit.Case(suiteId, "process_runner_timeout", "ProcessRunner kills the process tree when the timeout elapses", RunTimeoutAsync),
                CliAgentCaseKit.Case(suiteId, "process_runner_cancellation", "ProcessRunner kills the whole process tree on cancellation", RunCancellationAsync),
                CliAgentCaseKit.Case(suiteId, "process_runner_output_cap", "ProcessRunner caps a single huge line without buffering it", RunOutputCapAsync),
                CliAgentCaseKit.Case(suiteId, "process_runner_stdin", "ProcessRunner writes stdin (UTF-8, no BOM), closes it, and supports interactive lines", RunStdinAsync),
                CliAgentCaseKit.Case(suiteId, "process_runner_no_shell", "ProcessRunner passes arguments verbatim without a shell", RunNoShellAsync),
                CliAgentCaseKit.Case(suiteId, "process_runner_resolve", "ResolveExecutable searches PATH, honors explicit paths, and skips non-executable files", RunResolveAsync),
                CliAgentCaseKit.Case(suiteId, "process_runner_validation", "ProcessRunner rejects invalid requests and missing executables", RunRunnerValidationAsync),
                CliAgentCaseKit.Case(suiteId, "cli_models_json", "Agent harness models round-trip through the source-generated JSON context, including ProcessCallDetail in CallDetails", RunModelsJsonAsync),
                CliAgentCaseKit.Case(suiteId, "cli_telemetry", "Harness runs report an invoke_agent operation span, an exec process span, process metrics, and bounded error types", RunTelemetryAsync),
                CliAgentCaseKit.Case(suiteId, "cli_models_defaults", "Agent harness models have safe defaults when minimally deserialized", RunModelDefaultsAsync),
            };
        }

        #region ProcessRunner

        private static ProcessRunRequest Shell(string script, int timeoutMs = 10000)
        {
            return new ProcessRunRequest { FileName = "/bin/sh", Arguments = new List<string> { "-c", script }, TimeoutMs = timeoutMs };
        }

        private static async Task<(List<string> Lines, ProcessRunResult Result)> RunToEndAsync(ProcessRunRequest request, CancellationToken token)
        {
            using ProcessRunner runner = ProcessRunner.Start(request);
            List<string> lines = new List<string>();
            string? line;
            while ((line = await runner.ReadLineAsync(token).ConfigureAwait(false)) != null) lines.Add(line);
            ProcessRunResult result = await runner.WaitForExitAsync(token).ConfigureAwait(false);
            return (lines, result);
        }

        private static async Task RunLinesAsync(CancellationToken token)
        {
            RequirePosix();
            (List<string> lines, ProcessRunResult result) = await RunToEndAsync(Shell("printf 'one\\ntwo\\r\\nthree'"), token).ConfigureAwait(false);
            SharedAssert.Equal("one|two|three", string.Join("|", lines), "Lines without terminators, final partial line kept.");
            SharedAssert.Equal(0, result.ExitCode, "Exit code.");
            SharedAssert.False(result.TimedOut || result.Cancelled || result.OutputTruncated, "Clean exit.");
            SharedAssert.True(result.ProcessId > 0, "Process id.");
            SharedAssert.Equal(14L, result.StandardOutputCharacters, "Characters counted.");

            (lines, result) = await RunToEndAsync(Shell("echo '{\"a\":1}'; echo '{\"broken'; echo '{\"b\":2}'"), token).ConfigureAwait(false);
            SharedAssert.Equal(3, lines.Count, "Malformed lines are passed through as lines.");
        }

        private static async Task RunExitCodesAsync(CancellationToken token)
        {
            RequirePosix();
            (List<string> lines, ProcessRunResult result) = await RunToEndAsync(new ProcessRunRequest { FileName = "/usr/bin/false", TimeoutMs = 10000 }, token).ConfigureAwait(false);
            SharedAssert.Equal(1, result.ExitCode, "false exits 1.");
            SharedAssert.Equal(0, lines.Count, "No output.");

            ProcessRunRequest noisy = Shell("echo out; i=0; while [ $i -lt 2000 ]; do echo 'stderr line that is long enough' >&2; i=$((i+1)); done; exit 7");
            noisy.MaxStandardErrorCharacters = 1000;
            (lines, result) = await RunToEndAsync(noisy, token).ConfigureAwait(false);
            SharedAssert.Equal(7, result.ExitCode, "Exit code 7.");
            SharedAssert.Equal(1000, result.StandardError.Length, "Stderr is bounded.");
            SharedAssert.True(result.StandardErrorTruncated, "Stderr truncation flagged.");
            SharedAssert.Equal("out", lines.Single(), "Stdout still read while stderr overflows.");
        }

        private static async Task RunTimeoutAsync(CancellationToken token)
        {
            RequirePosix();
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            (List<string> lines, ProcessRunResult result) = await RunToEndAsync(Shell("echo started; sleep 30", 500), token).ConfigureAwait(false);
            SharedAssert.True(result.TimedOut, "Timed out.");
            SharedAssert.Equal("started", lines.Single(), "Output before the timeout is kept.");
            SharedAssert.True(sw.ElapsedMilliseconds < 8000, "Returned promptly (" + sw.ElapsedMilliseconds + " ms).");
            SharedAssert.True(result.ExitCode.HasValue && result.ExitCode.Value != 0, "Killed process has a non-zero exit code.");
        }

        private static async Task RunCancellationAsync(CancellationToken token)
        {
            RequirePosix();
            string pidFile = Path.Combine(Path.GetTempPath(), "polyprompt-runner-" + Guid.NewGuid().ToString("N"));
            using ProcessRunner runner = ProcessRunner.Start(Shell("sleep 30 & echo $! > '" + pidFile + "'; echo ready; wait", 60000));
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token);

            string? first = await runner.ReadLineAsync(cts.Token).ConfigureAwait(false);
            SharedAssert.Equal("ready", first, "First line.");
            await CliAgentCaseKit.WaitForAsync(() => File.Exists(pidFile) && File.ReadAllText(pidFile).Trim().Length > 0, 5000).ConfigureAwait(false);
            int child = int.Parse(File.ReadAllText(pidFile).Trim());
            SharedAssert.True(FakeCli.IsProcessAlive(child), "Child is running before cancellation.");

            cts.CancelAfter(100);
            await SharedAssert.ThrowsAsync<OperationCanceledException>(() => runner.ReadLineAsync(cts.Token), "ReadLineAsync throws on cancellation.").ConfigureAwait(false);
            ProcessRunResult result = await runner.WaitForExitAsync(token).ConfigureAwait(false);
            SharedAssert.True(result.Cancelled, "Cancelled flag.");
            SharedAssert.True(await FakeCli.WaitForProcessExitAsync(child).ConfigureAwait(false), "The grandchild was killed with the tree (SIGKILL), not orphaned.");
            SharedAssert.True(runner.HasExited, "The process exited.");
            File.Delete(pidFile);
        }

        private static async Task RunOutputCapAsync(CancellationToken token)
        {
            RequirePosix();
            ProcessRunRequest request = Shell("echo first; head -c 50000000 /dev/zero | tr '\\0' 'x'; echo; echo never");
            request.MaxOutputCharacters = 10000;
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            (List<string> lines, ProcessRunResult result) = await RunToEndAsync(request, token).ConfigureAwait(false);
            SharedAssert.True(result.OutputTruncated, "Truncated.");
            SharedAssert.Equal("first", lines.Single(), "Only complete lines before the cap are returned; the partial giant line is dropped.");
            SharedAssert.True(result.StandardOutputCharacters <= 10001, "Reading stopped at the cap (" + result.StandardOutputCharacters + ").");
            SharedAssert.True(sw.ElapsedMilliseconds < 8000, "The writer was killed rather than drained (" + sw.ElapsedMilliseconds + " ms).");
        }

        private static async Task RunStdinAsync(CancellationToken token)
        {
            RequirePosix();
            string text = "h\u00e9llo w\u00f6rld\nsecond line";
            ProcessRunRequest request = new ProcessRunRequest { FileName = "/bin/cat", StandardInput = text, TimeoutMs = 10000 };
            (List<string> lines, ProcessRunResult result) = await RunToEndAsync(request, token).ConfigureAwait(false);
            SharedAssert.Equal(text, string.Join("\n", lines), "cat echoes stdin exactly (UTF-8, stdin closed so cat exits).");
            SharedAssert.Equal(0, result.ExitCode, "cat exits 0.");

            ProcessRunRequest bomCheck = Shell("head -c 3 | od -An -c");
            bomCheck.StandardInput = "abc";
            (lines, result) = await RunToEndAsync(bomCheck, token).ConfigureAwait(false);
            SharedAssert.Equal("a   b   c", string.Join("", lines).Trim(), "No byte order mark precedes stdin.");

            ProcessRunRequest interactive = Shell("while IFS= read -r l; do echo \"got:$l\"; done; echo eof");
            interactive.StandardInput = "first\n";
            interactive.KeepStandardInputOpen = true;
            using ProcessRunner runner = ProcessRunner.Start(interactive);
            SharedAssert.Equal("got:first", await runner.ReadLineAsync(token).ConfigureAwait(false), "Initial input.");
            SharedAssert.True(await runner.WriteLineAsync("second", token).ConfigureAwait(false), "Interactive write.");
            SharedAssert.Equal("got:second", await runner.ReadLineAsync(token).ConfigureAwait(false), "Interactive response.");
            runner.CloseStandardInput();
            runner.CloseStandardInput();
            SharedAssert.Equal("eof", await runner.ReadLineAsync(token).ConfigureAwait(false), "Closing stdin delivers end of file.");
            SharedAssert.Equal(null, await runner.ReadLineAsync(token).ConfigureAwait(false), "End of output.");
            SharedAssert.False(await runner.WriteLineAsync("late", token).ConfigureAwait(false), "Writing after close reports false.");
        }

        private static async Task RunNoShellAsync(CancellationToken token)
        {
            RequirePosix();
            List<string> tricky = new List<string> { "a b", "$HOME", "`id`", "; echo injected", "'quoted'", "\"double\"", "" };
            ProcessRunRequest request = new ProcessRunRequest { FileName = "/usr/bin/printf", TimeoutMs = 10000 };
            request.Arguments.Add("%s\\n");
            request.Arguments.AddRange(tricky);
            (List<string> lines, ProcessRunResult result) = await RunToEndAsync(request, token).ConfigureAwait(false);
            SharedAssert.Equal(string.Join("|", tricky), string.Join("|", lines), "Arguments arrive verbatim: no expansion, splitting, or injection.");

            ProcessRunRequest env = Shell("printf '%s|%s' \"${PP_SET:-unset}\" \"${HOME:-removed}\"");
            env.Environment = new Dictionary<string, string?> { { "PP_SET", "value" }, { "HOME", null } };
            (lines, result) = await RunToEndAsync(env, token).ConfigureAwait(false);
            SharedAssert.Equal("value|removed", lines.Single(), "Environment overrides set and remove variables.");
        }

        private static Task RunResolveAsync(CancellationToken token)
        {
            RequirePosix();
            string dir = Path.Combine(Path.GetTempPath(), "polyprompt-resolve-" + Guid.NewGuid().ToString("N"));
            string other = Path.Combine(dir, "other");
            Directory.CreateDirectory(other);
            try
            {
                string tool = Path.Combine(other, "pp-tool");
                File.WriteAllText(tool, "#!/bin/sh\n");
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(tool, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                string plain = Path.Combine(dir, "pp-tool");
                File.WriteAllText(plain, "not executable");

                string search = dir + Path.PathSeparator + other;
                SharedAssert.Equal(tool, ProcessRunner.ResolveExecutable("pp-tool", search), "Non-executable files are skipped; the executable later on the path wins.");
                SharedAssert.Equal(null, ProcessRunner.ResolveExecutable("pp-missing", search), "Missing names resolve to null.");
                SharedAssert.Equal(tool, ProcessRunner.ResolveExecutable(tool), "Absolute paths are checked directly.");
                SharedAssert.Equal(null, ProcessRunner.ResolveExecutable(plain), "A non-executable absolute path is null.");
                SharedAssert.Equal(null, ProcessRunner.ResolveExecutable(" "), "Blank names are null.");
                SharedAssert.NotNull(ProcessRunner.ResolveExecutable("sh"), "sh is found on the process PATH.");
            }
            finally
            {
                Directory.Delete(dir, true);
            }
            return Task.CompletedTask;
        }

        private static Task RunRunnerValidationAsync(CancellationToken token)
        {
            SharedAssert.True(Throws<ArgumentNullException>(() => ProcessRunner.Start(null!)), "Null request.");
            SharedAssert.True(Throws<ArgumentException>(() => ProcessRunner.Start(new ProcessRunRequest())), "Empty file name.");
            SharedAssert.True(Throws<ArgumentException>(() => ProcessRunner.Start(new ProcessRunRequest { FileName = "/bin/sh", TimeoutMs = 0 })), "Zero timeout.");
            SharedAssert.True(Throws<ArgumentException>(() => ProcessRunner.Start(new ProcessRunRequest { FileName = "/bin/sh", MaxOutputCharacters = 0 })), "Zero output cap.");
            SharedAssert.True(Throws<FileNotFoundException>(() => ProcessRunner.Start(new ProcessRunRequest { FileName = "/nonexistent/polyprompt-binary" })), "Missing executable is FileNotFoundException.");
            return Task.CompletedTask;
        }

        #endregion

        #region Models

        private static Task RunModelsJsonAsync(CancellationToken token)
        {
            CliAgentResult result = new CliAgentResult
            {
                Success = true,
                Text = "done",
                SessionId = "s-1",
                ExitCode = 0,
                Usage = new TokenUsage { PromptTokens = 10, CompletionTokens = 2 },
                CostUsd = 0.0125m,
                FailureKind = CliFailureKind.None,
                Events = new List<CliAgentEvent>
                {
                    new CliAgentEvent(CliAgentEventKind.ToolUse) { ToolName = "Bash", ToolInput = "ls" },
                    new CliAgentEvent(CliAgentEventKind.Completed, "done") { ExitCode = 0 },
                },
            };

            string json = JsonSerializer.Serialize(result, PolyPromptJsonContext.Default.CliAgentResult);
            SharedAssert.True(json.Contains("\"Kind\":\"ToolUse\"", StringComparison.Ordinal) || json.Contains("\"kind\":\"ToolUse\"", StringComparison.OrdinalIgnoreCase), "Enums serialize as strings: " + json);
            SharedAssert.False(json.Contains("Succeeded", StringComparison.Ordinal), "Succeeded is not serialized.");
            CliAgentResult back = JsonSerializer.Deserialize(json, PolyPromptJsonContext.Default.CliAgentResult)!;
            SharedAssert.Equal("s-1", back.SessionId, "Session id round-trips.");
            SharedAssert.Equal(0.0125m, back.CostUsd, "Cost round-trips exactly.");
            SharedAssert.Equal(2, back.Events.Count, "Events round-trip.");
            SharedAssert.Equal("Bash", back.Events[0].ToolName, "Event fields round-trip.");

            List<CallDetail> details = new List<CallDetail>
            {
                new CallDetail { Method = "POST", Url = "https://example.test/v1", StatusCode = 200 },
                new ProcessCallDetail { Method = "EXEC", Url = "/usr/bin/codex", Executable = "codex", Arguments = new List<string> { "exec", "--json" }, ExitCode = 3, StatusCode = 3, FailureKind = CliFailureKind.Crashed },
            };
            string detailsJson = JsonSerializer.Serialize(details, PolyPromptJsonContext.Default.ListCallDetail);
            List<CallDetail> detailsBack = JsonSerializer.Deserialize(detailsJson, PolyPromptJsonContext.Default.ListCallDetail)!;
            SharedAssert.False(detailsBack[0] is ProcessCallDetail, "HTTP call details stay CallDetail.");
            ProcessCallDetail process = detailsBack[1] as ProcessCallDetail ?? throw new TestFailureException("ProcessCallDetail survives a List<CallDetail> round trip: " + detailsJson);
            SharedAssert.Equal("exec | --json", string.Join(" | ", process.Arguments), "Arguments round-trip.");
            SharedAssert.Equal(CliFailureKind.Crashed, process.FailureKind, "Failure kind round-trips.");

            string plainJson = JsonSerializer.Serialize(new CallDetail { Method = "GET" }, PolyPromptJsonContext.Default.CallDetail);
            SharedAssert.False(plainJson.Contains("$type", StringComparison.Ordinal), "Plain HTTP call details carry no type discriminator.");

            CliConnectivityResult connectivity = new CliConnectivityResult { Status = CliConnectivityStatus.NotAuthenticated, Message = "Not logged in" };
            string connectivityJson = JsonSerializer.Serialize(connectivity, PolyPromptJsonContext.Default.CliConnectivityResult);
            SharedAssert.Equal(CliConnectivityStatus.NotAuthenticated, JsonSerializer.Deserialize(connectivityJson, PolyPromptJsonContext.Default.CliConnectivityResult)!.Status, "Connectivity round-trips.");

            CliAgentRequest request = new CliAgentRequest("p") { PermissionMode = CliPermissionMode.WorkspaceWrite, ExtraArguments = new Dictionary<string, string?> { { "--flag", null } } };
            CliAgentRequest requestBack = JsonSerializer.Deserialize(JsonSerializer.Serialize(request, PolyPromptJsonContext.Default.CliAgentRequest), PolyPromptJsonContext.Default.CliAgentRequest)!;
            SharedAssert.Equal(CliPermissionMode.WorkspaceWrite, requestBack.PermissionMode, "Request permission mode round-trips.");
            return Task.CompletedTask;
        }

        private static Task RunModelDefaultsAsync(CancellationToken token)
        {
            CliAgentRequest request = JsonSerializer.Deserialize("{\"Prompt\":\"x\"}", PolyPromptJsonContext.Default.CliAgentRequest)!;
            SharedAssert.Equal(1_000_000, request.MaxOutputCharacters, "Default output cap.");
            SharedAssert.Equal(null, request.PermissionMode, "Permission mode inherits from the client.");
            SharedAssert.Equal(null, request.SessionId, "No session.");

            CliAgentResult result = JsonSerializer.Deserialize("{}", PolyPromptJsonContext.Default.CliAgentResult)!;
            SharedAssert.Equal("", result.Text, "Empty text, not null.");
            SharedAssert.NotNull(result.Events, "Events list exists.");
            SharedAssert.Equal(CliFailureKind.None, result.FailureKind, "No failure kind.");
            SharedAssert.False(result.Success, "Not successful by default.");

            CliAgentEvent evt = JsonSerializer.Deserialize("{}", PolyPromptJsonContext.Default.CliAgentEvent)!;
            SharedAssert.Equal(CliAgentEventKind.Notice, evt.Kind, "Default event kind.");

            CliConnectivityResult connectivity = JsonSerializer.Deserialize("{}", PolyPromptJsonContext.Default.CliConnectivityResult)!;
            SharedAssert.Equal(CliConnectivityStatus.Error, connectivity.Status, "Default connectivity is not Ok.");
            SharedAssert.False(connectivity.Success, "Default connectivity is not a success.");

            ProcessCallDetail detail = JsonSerializer.Deserialize("{}", PolyPromptJsonContext.Default.ProcessCallDetail)!;
            SharedAssert.NotNull(detail.Arguments, "Arguments list exists.");
            SharedAssert.Equal(CliFailureKind.None, detail.FailureKind, "No failure kind.");
            return Task.CompletedTask;
        }

        #endregion

        #region Telemetry

        private static async Task RunTelemetryAsync(CancellationToken token)
        {
            using FakeCli fake = FakeCli.Create("codex", "codex-happy");
            using CodexAgentClient client = fake.Attach(new CodexAgentClient());
            using TelemetryCapture capture = new TelemetryCapture("cli_telemetry");

            CliAgentResult result = await client.RunAsync(new CliAgentRequest("hi") { Model = "gpt-6.1-sol" }, token).ConfigureAwait(false);
            SharedAssert.True(result.Success, "Run succeeds: " + result.Error);

            System.Diagnostics.Activity op = capture.Span("codex agent_run");
            SharedAssert.Equal("codex", op.GetTagItem(PolyPrompt.Telemetry.PolyPromptTelemetryNames.Provider) as string, "Provider label.");
            SharedAssert.Equal("agent", op.GetTagItem(PolyPrompt.Telemetry.PolyPromptTelemetryNames.Capability) as string, "Capability label.");
            SharedAssert.Equal("invoke_agent", op.GetTagItem(PolyPrompt.Telemetry.PolyPromptTelemetryNames.GenAiOperationName) as string, "GenAI operation name.");
            SharedAssert.Equal(null, op.GetTagItem(PolyPrompt.Telemetry.PolyPromptTelemetryNames.ServerAddress), "No server address for a process.");

            System.Diagnostics.Activity exec = capture.Span("exec codex");
            SharedAssert.Equal(op.SpanId, exec.ParentSpanId, "The exec span is a child of the operation span.");
            SharedAssert.Equal("codex", exec.GetTagItem(PolyPrompt.Telemetry.PolyPromptTelemetryNames.ProcessExecutableName) as string, "Executable name.");
            SharedAssert.Equal(0, exec.GetTagItem(PolyPrompt.Telemetry.PolyPromptTelemetryNames.ProcessExitCode), "Exit code tag.");
            SharedAssert.NotNull(exec.GetTagItem(PolyPrompt.Telemetry.PolyPromptTelemetryNames.ProcessPid), "Pid tag.");

            capture.Require(PolyPrompt.Telemetry.PolyPromptTelemetryNames.ProcessExecutions, "gen_ai.provider.name", "codex", "process.executable.name", "codex", "process.exit.code", "0");
            capture.Require(PolyPrompt.Telemetry.PolyPromptTelemetryNames.ProcessDuration, "gen_ai.provider.name", "codex");
            capture.Require(PolyPrompt.Telemetry.PolyPromptTelemetryNames.Operations, "gen_ai.provider.name", "codex", "polyprompt.capability", "agent", "polyprompt.operation", "agent_run", "polyprompt.outcome", "success");
            capture.Require(PolyPrompt.Telemetry.PolyPromptTelemetryNames.Tokens, "gen_ai.provider.name", "codex");

            fake.UseScenario(client, "codex-unauthenticated");
            await client.RunAsync(new CliAgentRequest("hi"), token).ConfigureAwait(false);
            capture.Require(PolyPrompt.Telemetry.PolyPromptTelemetryNames.Operations, "gen_ai.provider.name", "codex", "polyprompt.operation", "agent_run", "polyprompt.outcome", "error", "error.type", "not_authenticated");
            capture.Require(PolyPrompt.Telemetry.PolyPromptTelemetryNames.ProcessExecutions, "gen_ai.provider.name", "codex", "error.type", "not_authenticated", "process.exit.code", "1");

            fake.UseScenario(client, "codex-slow");
            await client.RunAsync(new CliAgentRequest("hi") { TimeoutMs = 500 }, token).ConfigureAwait(false);
            capture.Require(PolyPrompt.Telemetry.PolyPromptTelemetryNames.Operations, "gen_ai.provider.name", "codex", "polyprompt.operation", "agent_run", "polyprompt.outcome", "timeout", "error.type", "timeout");

            fake.UseScenario(client, "codex-happy");
            await foreach (CliAgentEvent evt in client.RunStreamingAsync(new CliAgentRequest("hi"), token).ConfigureAwait(false)) { }
            capture.Require(PolyPrompt.Telemetry.PolyPromptTelemetryNames.Operations, "gen_ai.provider.name", "codex", "polyprompt.operation", "agent_run_stream", "polyprompt.outcome", "success");
            capture.Require(PolyPrompt.Telemetry.PolyPromptTelemetryNames.StreamTimeToFirstChunk, "gen_ai.provider.name", "codex");

            CliConnectivityResult connectivity = await client.ValidateConnectivityAsync(token).ConfigureAwait(false);
            SharedAssert.True(connectivity.Success, "Connectivity Ok.");
            capture.Require(PolyPrompt.Telemetry.PolyPromptTelemetryNames.Operations, "gen_ai.provider.name", "codex", "polyprompt.operation", "validate_connectivity", "polyprompt.outcome", "success");

            client.Executable = "polyprompt-missing-codex";
            await client.ValidateConnectivityAsync(token).ConfigureAwait(false);
            capture.Require(PolyPrompt.Telemetry.PolyPromptTelemetryNames.Operations, "gen_ai.provider.name", "codex", "polyprompt.operation", "validate_connectivity", "polyprompt.outcome", "error", "error.type", "not_installed");
        }

        #endregion

        #region Helpers

        private static void RequirePosix()
        {
            if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("These cases use /bin/sh and need macOS or Linux.");
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

        #endregion
    }
}
