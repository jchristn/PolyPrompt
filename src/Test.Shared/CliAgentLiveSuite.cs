namespace Test.Shared
{
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using Touchstone.Core;

    /// <summary>
    /// Opt-in live suites for the agent harness clients. They run the real CLIs with whatever login the machine already
    /// has, so they are skipped unless enabled per harness with an environment variable:
    /// <c>POLYPROMPT_TEST_CODEX</c>, <c>POLYPROMPT_TEST_CLAUDE_CODE</c>, <c>POLYPROMPT_TEST_GEMINI_CLI</c>,
    /// <c>POLYPROMPT_TEST_CURSOR_CLI</c>, <c>POLYPROMPT_TEST_MUX</c>, <c>POLYPROMPT_TEST_PI</c>, and
    /// <c>POLYPROMPT_TEST_OPENCODE</c> set to <c>1</c>. <c>POLYPROMPT_TEST_&lt;NAME&gt;_MODEL</c> optionally selects the model.
    /// Every run is a trivial, read-only prompt in a temporary directory with the default Restricted permission mode.
    /// </summary>
    public static class CliAgentLiveSuite
    {
        private const string SuiteId = "cli_agent_live";

        private static readonly List<(string Id, string Variable, Func<CliAgentClientBase> Create)> _Harnesses = new()
        {
            ("codex", "POLYPROMPT_TEST_CODEX", () => new CodexAgentClient()),
            ("claudecode", "POLYPROMPT_TEST_CLAUDE_CODE", () => new ClaudeCodeAgentClient()),
            ("geminicli", "POLYPROMPT_TEST_GEMINI_CLI", () => new GeminiCliAgentClient()),
            ("cursor", "POLYPROMPT_TEST_CURSOR_CLI", () => new CursorAgentClient()),
            ("mux", "POLYPROMPT_TEST_MUX", () => new MuxAgentClient()),
            ("pi", "POLYPROMPT_TEST_PI", () => new PiAgentClient()),
            ("opencode", "POLYPROMPT_TEST_OPENCODE", () => new OpenCodeAgentClient()),
        };

        /// <summary>
        /// Names of the environment variables that enable the live suites.
        /// </summary>
        public static IReadOnlyList<string> EnvironmentVariableNames
        {
            get { return _Harnesses.Select(h => h.Variable).ToList(); }
        }

        /// <summary>
        /// Create the suite. Cases for harnesses that are not enabled are marked skipped.
        /// </summary>
        public static TestSuiteDescriptor Create()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            foreach ((string id, string variable, Func<CliAgentClientBase> create) in _Harnesses)
            {
                bool enabled = Environment.GetEnvironmentVariable(variable) == "1";
                string? model = Environment.GetEnvironmentVariable(variable + "_MODEL");
                string reason = "Set " + variable + "=1 to run the live " + id + " suite.";

                cases.Add(Live(id + "_connectivity", id + " live: CLI installed and logged in", enabled, reason, t => RunConnectivityAsync(create, t)));
                cases.Add(Live(id + "_run", id + " live: one read-only turn", enabled, reason, t => RunTurnAsync(create, model, t)));
                cases.Add(Live(id + "_stream", id + " live: streamed turn ends with Completed", enabled, reason, t => RunStreamAsync(create, model, t)));
                cases.Add(Live(id + "_resume", id + " live: a second turn continues the session", enabled, reason, t => RunResumeAsync(create, model, t)));
                cases.Add(Live(id + "_models", id + " live: model listing where supported", enabled, reason, t => RunModelsAsync(create, t)));
            }

            return new TestSuiteDescriptor(SuiteId, "Agent harness live (opt-in)", cases);
        }

        private static TestCaseDescriptor Live(string caseId, string displayName, bool enabled, string reason, Func<CancellationToken, Task> run)
        {
            return new TestCaseDescriptor(SuiteId, "cli_live_" + caseId, displayName, run, new[] { "live", "cli" }, !enabled, enabled ? null : reason);
        }

        private static CliAgentClientBase Configure(Func<CliAgentClientBase> create, string? model, out string directory)
        {
            directory = Path.Combine(Path.GetTempPath(), "polyprompt-live-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "README.txt"), "PolyPrompt live harness test directory.\n");

            CliAgentClientBase client = create();
            client.TimeoutMs = 300000;
            client.WorkingDirectory = directory;
            if (!string.IsNullOrWhiteSpace(model)) client.Model = model;
            return client;
        }

        private static void Cleanup(string directory)
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch
            {
            }
        }

        private static async Task RunConnectivityAsync(Func<CliAgentClientBase> create, CancellationToken token)
        {
            using CliAgentClientBase client = Configure(create, null, out string directory);
            try
            {
                CliConnectivityResult result = await client.ValidateConnectivityAsync(token).ConfigureAwait(false);
                SharedAssert.Equal(CliConnectivityStatus.Ok, result.Status, "Connectivity: " + result.Message);
                SharedAssert.NotEmpty(result.Version, "Version.");
            }
            finally
            {
                Cleanup(directory);
            }
        }

        private static async Task RunTurnAsync(Func<CliAgentClientBase> create, string? model, CancellationToken token)
        {
            using CliAgentClientBase client = Configure(create, model, out string directory);
            try
            {
                CliAgentResult result = await client.RunAsync(new CliAgentRequest("Reply with exactly the word PONG and nothing else."), token).ConfigureAwait(false);
                SharedAssert.True(result.Success, "Live turn failed (" + result.FailureKind + "): " + result.Error);
                SharedAssert.True(result.Text.Contains("PONG", StringComparison.OrdinalIgnoreCase), "Text should contain PONG: " + result.Text);
                SharedAssert.Equal(CliAgentEventKind.Completed, result.Events[result.Events.Count - 1].Kind, "Terminal event.");
                SharedAssert.True(client.CallDetails.Count > 0 && client.CallDetails[client.CallDetails.Count - 1] is ProcessCallDetail, "Process recorded.");
            }
            finally
            {
                Cleanup(directory);
            }
        }

        private static async Task RunStreamAsync(Func<CliAgentClientBase> create, string? model, CancellationToken token)
        {
            using CliAgentClientBase client = Configure(create, model, out string directory);
            try
            {
                List<CliAgentEvent> events = new List<CliAgentEvent>();
                await foreach (CliAgentEvent evt in client.RunStreamingAsync(new CliAgentRequest("Reply with exactly the word PONG and nothing else."), token).ConfigureAwait(false)) events.Add(evt);
                CliAgentEvent last = events[events.Count - 1];
                SharedAssert.Equal(CliAgentEventKind.Completed, last.Kind, "Terminal event: " + last.Text);
                SharedAssert.True((last.Text ?? "").Contains("PONG", StringComparison.OrdinalIgnoreCase), "Completed text should contain PONG: " + last.Text);
            }
            finally
            {
                Cleanup(directory);
            }
        }

        private static async Task RunResumeAsync(Func<CliAgentClientBase> create, string? model, CancellationToken token)
        {
            using CliAgentClientBase client = Configure(create, model, out string directory);
            try
            {
                CliAgentResult first = await client.RunAsync(new CliAgentRequest("Remember the code word ZEBRA. Reply with exactly OK."), token).ConfigureAwait(false);
                SharedAssert.True(first.Success, "First turn failed: " + first.Error);

                ChatMessage assistant = ChatMessage.Assistant(first.Text);
                assistant.ProviderState = first.ProviderState;
                CliAgentRequest second = new CliAgentRequest("What was the code word? Reply with just the word.")
                {
                    History = new List<ChatMessage> { ChatMessage.User("Remember the code word ZEBRA. Reply with exactly OK."), assistant },
                };
                CliAgentResult result = await client.RunAsync(second, token).ConfigureAwait(false);
                SharedAssert.True(result.Success, "Second turn failed: " + result.Error);
                SharedAssert.True(result.Text.Contains("ZEBRA", StringComparison.OrdinalIgnoreCase), "The second turn remembers the first (resumed or replayed): " + result.Text);
                if (client.SupportsSessionResume && first.SessionId != null)
                    SharedAssert.Equal(first.SessionId, result.SessionId, "The resumed turn reports the same session.");
            }
            finally
            {
                Cleanup(directory);
            }
        }

        private static async Task RunModelsAsync(Func<CliAgentClientBase> create, CancellationToken token)
        {
            using CliAgentClientBase client = Configure(create, null, out string directory);
            try
            {
                IAsyncEnumerable<ModelInformation>? listing = client switch
                {
                    CodexAgentClient c => c.ListModelsAsync(token),
                    CursorAgentClient c => c.ListModelsAsync(token),
                    MuxAgentClient c => c.ListModelsAsync(token),
                    PiAgentClient c => c.ListModelsAsync(null, token),
                    OpenCodeAgentClient c => c.ListModelsAsync(token),
                    _ => null,
                };
                if (listing == null) return;

                int count = 0;
                await foreach (ModelInformation model in listing.ConfigureAwait(false))
                {
                    SharedAssert.NotEmpty(model.Name, "Model name.");
                    count++;
                }
                SharedAssert.True(count > 0, "At least one model.");
            }
            finally
            {
                Cleanup(directory);
            }
        }
    }
}
