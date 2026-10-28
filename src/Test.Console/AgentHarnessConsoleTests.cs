namespace Test.Console;

using NUnit.Framework;
using PolyPrompt.AgentConsoleSupport;
using PolyPrompt.Clients;
using PolyPrompt.Models;
using Test.Shared;

/// <summary>
/// End-to-end checks for the seven per-harness console samples (ClaudeCodeConsole, CodexConsole, GeminiCliConsole,
/// CursorCliConsole, MuxConsole, PiConsole, OpenCodeConsole) against the fake CLI. Expected values come from running the
/// same recorded scenario through the client directly, so the console is checked against the library, not against
/// hand-copied strings. No live CLI or paid model call is needed; requires macOS or Linux.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class AgentHarnessConsoleTests
{
    public sealed record Harness(string Name, string Executable, string Prefix, AgentConsoleProfile Profile, Func<string[], Task<int>> Main)
    {
        public override string ToString() => Name;
    }

    public static IEnumerable<Harness> Harnesses()
    {
        yield return new Harness("ClaudeCodeConsole", "claude", "claudecode", ClaudeCodeConsole.Program.Profile, ClaudeCodeConsole.Program.Main);
        yield return new Harness("CodexConsole", "codex", "codex", CodexConsole.Program.Profile, CodexConsole.Program.Main);
        yield return new Harness("GeminiCliConsole", "gemini", "geminicli", GeminiCliConsole.Program.Profile, GeminiCliConsole.Program.Main);
        yield return new Harness("CursorCliConsole", "cursor-agent", "cursor", CursorCliConsole.Program.Profile, CursorCliConsole.Program.Main);
        yield return new Harness("MuxConsole", "mux", "mux", MuxConsole.Program.Profile, MuxConsole.Program.Main);
        yield return new Harness("PiConsole", "pi", "pi", PiConsole.Program.Profile, PiConsole.Program.Main);
        yield return new Harness("OpenCodeConsole", "opencode", "opencode", OpenCodeConsole.Program.Profile, OpenCodeConsole.Program.Main);
    }

    private string? _PreviousDir;
    private string? _PreviousScript;
    private TextWriter _PreviousOut = null!;
    private TextReader _PreviousIn = null!;
    private StringWriter _Output = null!;

    [SetUp]
    public void Setup()
    {
        if (OperatingSystem.IsWindows()) Assert.Ignore("The fake agent CLI needs macOS or Linux.");
        _PreviousDir = Environment.GetEnvironmentVariable("POLYPROMPT_FAKE_CLI_DIR");
        _PreviousScript = Environment.GetEnvironmentVariable("POLYPROMPT_FAKE_CLI_SCRIPT");
        _PreviousOut = System.Console.Out;
        _PreviousIn = System.Console.In;
        _Output = new StringWriter();
        System.Console.SetOut(_Output);
    }

    [TearDown]
    public void Cleanup()
    {
        System.Console.SetOut(_PreviousOut);
        System.Console.SetIn(_PreviousIn);
        Environment.SetEnvironmentVariable("POLYPROMPT_FAKE_CLI_DIR", _PreviousDir);
        Environment.SetEnvironmentVariable("POLYPROMPT_FAKE_CLI_SCRIPT", _PreviousScript);
    }

    [Test]
    public void ProfilesMatchTheirClients()
    {
        Dictionary<string, Type> expected = new()
        {
            ["ClaudeCodeConsole"] = typeof(ClaudeCodeAgentClient),
            ["CodexConsole"] = typeof(CodexAgentClient),
            ["GeminiCliConsole"] = typeof(GeminiCliAgentClient),
            ["CursorCliConsole"] = typeof(CursorAgentClient),
            ["MuxConsole"] = typeof(MuxAgentClient),
            ["PiConsole"] = typeof(PiAgentClient),
            ["OpenCodeConsole"] = typeof(OpenCodeAgentClient),
        };
        foreach (Harness harness in Harnesses())
        {
            using CliAgentClientBase client = harness.Profile.CreateClient();
            Assert.That(client, Is.TypeOf(expected[harness.Name]), harness.Name);
            Assert.That(client.Executable, Is.EqualTo(harness.Executable), harness.Name);
            Assert.That(harness.Profile.ApplicationName, Is.EqualTo(harness.Name));
            Assert.That(harness.Profile.Prerequisites, Does.Contain(harness.Profile.HarnessName + " setup:").And.Contain("PolyPrompt never takes an API key").And.Contain("PATH"), harness.Name);
        }

        Assert.That(Harnesses().Where(h => h.Profile.ListModels != null).Select(h => h.Name),
            Is.EquivalentTo(new[] { "CodexConsole", "CursorCliConsole", "MuxConsole", "PiConsole", "OpenCodeConsole" }));
    }

    [TestCaseSource(nameof(Harnesses))]
    public async Task OneShotStreamingAndBufferedTurnsMatchTheClient(Harness harness)
    {
        using FakeCli fake = Use(harness, "happy");
        CliAgentResult expected = await RunDirectAsync(harness, fake, "happy");
        Assert.That(expected.Success, Is.True, expected.Error);
        Assert.That(expected.Text, Is.Not.Empty);

        int exit = await harness.Main(new[] { "--executable", fake.ExecutablePath, "--directory", fake.Directory, "--prompt", "List the files.", "--stream", "false" });
        string text = _Output.ToString();
        Assert.That(exit, Is.EqualTo(0), text);
        Assert.That(text, Does.Contain(harness.Name + " - PolyPrompt"));
        Assert.That(text, Does.Contain("Directory:  " + fake.Directory));
        Assert.That(text, Does.Contain("Status:     Ok"));
        Assert.That(text, Does.Contain("--- Answer ---\n" + expected.Text).Or.Contain("--- Answer ---" + System.Environment.NewLine + expected.Text));
        AssertDoneLine(text, expected);
        Assert.That(fake.WorkingDirectory, Does.EndWith(Path.GetFileName(fake.Directory)));

        _Output.GetStringBuilder().Clear();
        exit = await harness.Main(new[] { "--executable", fake.ExecutablePath, "--directory", fake.Directory, "--prompt", "List the files." });
        text = _Output.ToString();
        Assert.That(exit, Is.EqualTo(0), text);
        Assert.That(text, Does.Contain("--- Answer ---"));
        foreach (CliAgentEvent delta in expected.Events.Where(e => e.Kind == CliAgentEventKind.TextDelta && !string.IsNullOrEmpty(e.Text)))
            Assert.That(text, Does.Contain(delta.Text));
        if (expected.Events.Any(e => e.Kind == CliAgentEventKind.ReasoningDelta)) Assert.That(text, Does.Contain("--- Thinking ---"));
        foreach (CliAgentEvent tool in expected.Events.Where(e => e.Kind == CliAgentEventKind.ToolUse))
            Assert.That(text, Does.Contain("[tool] " + tool.ToolName));
        AssertDoneLine(text, expected);
    }

    [TestCaseSource(nameof(Harnesses))]
    public async Task UnauthenticatedTurnFailsWithSetupSteps(Harness harness)
    {
        using FakeCli fake = Use(harness, "unauthenticated");
        CliAgentResult expected = await RunDirectAsync(harness, fake, "unauthenticated");
        Assert.That(expected.Success, Is.False);

        int exit = await harness.Main(new[] { "--executable", fake.ExecutablePath, "--directory", fake.Directory, "--prompt", "hello" });
        string text = _Output.ToString();
        Assert.That(exit, Is.EqualTo(1), text);
        Assert.That(text, Does.Contain("[failed: " + expected.FailureKind + "]"));
        Assert.That(text, Does.Not.Contain("[done]"));
        if (expected.FailureKind == CliFailureKind.NotAuthenticated) Assert.That(text, Does.Contain(harness.Profile.HarnessName + " setup:"));
    }

    [TestCaseSource(nameof(Harnesses))]
    public async Task MissingExecutableReportsNotInstalledWithSetupSteps(Harness harness)
    {
        string missing = Path.Combine(Path.GetTempPath(), "polyprompt-missing-" + Guid.NewGuid().ToString("N"), harness.Executable);
        int exit = await harness.Main(new[] { "--executable", missing, "--check" });
        string text = _Output.ToString();
        Assert.That(exit, Is.EqualTo(1), text);
        Assert.That(text, Does.Contain("Status:     NotInstalled"));
        Assert.That(text, Does.Contain(harness.Profile.HarnessName + " setup:"));

        _Output.GetStringBuilder().Clear();
        exit = await harness.Main(new[] { "--executable", missing, "--prompt", "hello" });
        text = _Output.ToString();
        Assert.That(exit, Is.EqualTo(1), text);
        Assert.That(text, Does.Contain("[failed: NotInstalled]"));
    }

    [TestCaseSource(nameof(Harnesses))]
    public async Task TimeoutKillsTheTurnAndReportsTimedOut(Harness harness)
    {
        using FakeCli fake = Use(harness, "slow");
        int exit = await harness.Main(new[] { "--executable", fake.ExecutablePath, "--directory", fake.Directory, "--prompt", "hello", "--timeout", "1500" });
        string text = _Output.ToString();
        Assert.That(exit, Is.EqualTo(1), text);
        Assert.That(text, Does.Contain("[failed: TimedOut]"));
        if (fake.ChildProcessId is int child) Assert.That(await FakeCli.WaitForProcessExitAsync(child), Is.True, "child process survived the timeout");
    }

    [TestCaseSource(nameof(Harnesses))]
    public async Task ModelListMatchesTheClient(Harness harness)
    {
        using FakeCli fake = Use(harness, "happy");
        int exit = await harness.Main(new[] { "--executable", fake.ExecutablePath, "--models" });
        string text = _Output.ToString();
        if (harness.Profile.ListModels == null)
        {
            Assert.That(exit, Is.EqualTo(2), text);
            Assert.That(text, Does.Contain("Unknown option: --models"));
            return;
        }

        using CliAgentClientBase client = fake.Attach(harness.Profile.CreateClient());
        List<ModelInformation> models = new();
        await foreach (ModelInformation model in harness.Profile.ListModels(client, null, CancellationToken.None)) models.Add(model);
        Assert.That(models, Is.Not.Empty);
        Assert.That(exit, Is.EqualTo(0), text);
        foreach (ModelInformation model in models) Assert.That(text, Does.Contain("  " + model.Name));
        Assert.That(text, Does.Contain(models.Count + " model(s)."));
    }

    [TestCaseSource(nameof(Harnesses))]
    public async Task InvalidArgumentsExitWithUsage(Harness harness)
    {
        foreach (string[] args in new[]
        {
            new[] { "--permission", "sometimes" },
            new[] { "--permission", "7" },
            new[] { "--directory", Path.Combine(Path.GetTempPath(), "polyprompt-missing-" + Guid.NewGuid().ToString("N")) },
            new[] { "--timeout", "soon" },
            new[] { "--bogus" },
            new[] { "stray" },
        })
        {
            _Output.GetStringBuilder().Clear();
            int exit = await harness.Main(args);
            Assert.That(exit, Is.EqualTo(2), string.Join(" ", args) + ": " + _Output);
            Assert.That(_Output.ToString(), Does.Contain("Usage: " + harness.Name), string.Join(" ", args));
        }

        _Output.GetStringBuilder().Clear();
        Assert.That(await harness.Main(new[] { "--help" }), Is.EqualTo(0));
        Assert.That(_Output.ToString(), Does.Contain("--executable PATH").And.Contain("(default: " + harness.Executable + ")").And.Contain("/new"));

        _Output.GetStringBuilder().Clear();
        Assert.That(await harness.Main(new[] { "--setup" }), Is.EqualTo(0));
        Assert.That(_Output.ToString().TrimEnd(), Is.EqualTo(harness.Profile.Prerequisites));
    }

    [Test]
    public async Task InteractiveSessionResumesChangesSettingsAndStartsOver()
    {
        Harness harness = Harnesses().Single(h => h.Name == "CodexConsole");
        using FakeCli fake = Use(harness, "happy");
        CliAgentResult expected = await RunDirectAsync(harness, fake, "happy");
        fake.ClearRecord();
        string other = Path.Combine(fake.Directory, "scripts");

        System.Console.SetIn(new StringReader(string.Join("\n",
            "/help", "/model gpt-test", "/permission bogus", "/permission readonly", "/stream sideways", "/stream off",
            "List the files.", "/session", "And again.", "/cd /definitely/not/here", "/cd scripts", "/session",
            "Fresh start.", "/new", "/model default", "/nope", "/exit", "never sent")));

        int exit = await harness.Main(new[] { "--executable", fake.ExecutablePath, "--directory", fake.Directory });
        string text = _Output.ToString();
        Assert.That(exit, Is.EqualTo(0), text);
        Assert.That(text, Does.Contain("/permission [MODE]"));
        Assert.That(text, Does.Contain("Model: gpt-test"));
        Assert.That(text, Does.Contain("Unknown permission mode: bogus"));
        Assert.That(text, Does.Contain("Permission: ReadOnly"));
        Assert.That(text, Does.Contain("Use /stream on or /stream off."));
        Assert.That(text, Does.Contain("Streaming: off"));
        Assert.That(text, Does.Contain("Session: " + expected.SessionId + ", turns: 1"));
        Assert.That(text, Does.Contain("Directory not found: /definitely/not/here"));
        Assert.That(text, Does.Contain("Directory: " + other));
        Assert.That(text, Does.Contain("Session: (new), turns: 0"));
        Assert.That(text, Does.Contain("Model: (harness default)"));
        Assert.That(text, Does.Contain("Unknown command /nope"));

        List<string> turns = fake.Invocations.Where(i => i.Contains("exec")).ToList();
        Assert.That(turns, Has.Count.EqualTo(3), string.Join("\n", fake.Invocations));
        Assert.That(turns[0], Does.Contain("gpt-test").And.Not.Contain("resume"));
        Assert.That(turns[1], Does.Contain("resume").And.Contain(expected.SessionId!));
        Assert.That(turns[2], Does.Not.Contain("resume"), "changing directory must start a new session");
        Assert.That(fake.WorkingDirectory, Does.EndWith("scripts"));
        Assert.That(fake.Invocations.Count(i => i.Contains("never sent")), Is.Zero);
    }

    [Test]
    public async Task MuxWithoutPersistedSessionsReplaysHistory()
    {
        Harness harness = Harnesses().Single(h => h.Name == "MuxConsole");
        using FakeCli fake = Use(harness, "stateless");
        CliAgentResult expected = await RunDirectAsync(harness, fake, "stateless");
        Assert.That(expected.Success, Is.True, expected.Error);
        Assert.That(expected.SessionId, Is.Null);
        fake.ClearRecord();

        System.Console.SetIn(new StringReader("First question.\nSecond question.\n/session\n"));
        int exit = await harness.Main(new[] { "--executable", fake.ExecutablePath, "--directory", fake.Directory, "--endpoint", "local-test", "--show-thinking", "false" });
        string text = _Output.ToString();
        Assert.That(exit, Is.EqualTo(0), text);
        Assert.That(text, Does.Contain("endpoint local-test, show thinking False, persist sessions False"));
        Assert.That(text, Does.Contain("(none reported; history replayed), turns: 2"));
        Assert.That(fake.Arguments, Does.Contain("--endpoint").And.Contain("local-test").And.Not.Contain("--show-thinking").And.Not.Contain("--resume"));
        Assert.That(fake.StandardInput, Does.Contain("First question.").And.Contain("Second question."));

        _Output.GetStringBuilder().Clear();
        Assert.That(await harness.Main(new[] { "--persist", "maybe" }), Is.EqualTo(2));
        Assert.That(_Output.ToString(), Does.Contain("--persist must be true or false."));
    }

    private FakeCli Use(Harness harness, string scenario)
    {
        FakeCli fake = FakeCli.Create(harness.Executable, harness.Prefix + "-" + scenario);
        Environment.SetEnvironmentVariable("POLYPROMPT_FAKE_CLI_DIR", fake.Directory);
        Environment.SetEnvironmentVariable("POLYPROMPT_FAKE_CLI_SCRIPT", fake.Scenario);
        return fake;
    }

    private static async Task<CliAgentResult> RunDirectAsync(Harness harness, FakeCli fake, string scenario)
    {
        using CliAgentClientBase client = fake.Attach(harness.Profile.CreateClient());
        client.WorkingDirectory = fake.Directory;
        fake.UseScenario(client, harness.Prefix + "-" + scenario);
        return await client.RunAsync(new CliAgentRequest("List the files."));
    }

    private static void AssertDoneLine(string text, CliAgentResult expected)
    {
        string done = text.Split('\n').Select(l => l.TrimEnd('\r')).LastOrDefault(l => l.StartsWith("[done]", StringComparison.Ordinal)) ?? "";
        Assert.That(done, Is.Not.Empty, text);
        if (expected.SessionId != null) Assert.That(done, Does.Contain("session " + expected.SessionId));
        if (expected.Usage?.PromptTokens != null) Assert.That(done, Does.Contain("input " + expected.Usage.PromptTokens));
        if (expected.CostUsd.HasValue) Assert.That(done, Does.Contain("cost $" + expected.CostUsd.Value));
        int tools = expected.Events.Count(e => e.Kind == CliAgentEventKind.ToolUse);
        if (tools > 0) Assert.That(done, Does.Contain(tools + " tool call(s)"));
    }
}
