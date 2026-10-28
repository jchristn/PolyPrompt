namespace Test.Console;

using NUnit.Framework;
using PolyPrompt.Clients;
using PolyPrompt.Models;
using Test.Shared;

/// <summary>
/// Console-level checks for the agent harness clients: a recorded Codex JSONL replay through the fake CLI (no live CLI
/// needed), the CliAgentConsole sample end to end against the fake CLI, and an opt-in live Codex turn
/// (POLYPROMPT_TEST_CODEX=1).
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class CliAgentConsoleTests
{
    [Test]
    public async Task CodexRecordedReplayRunsWithoutLiveCli()
    {
        using FakeCli fake = FakeCli.Create("codex", "codex-happy");
        using CodexAgentClient client = fake.Attach(new CodexAgentClient());

        CliAgentResult result = await client.RunAsync(new CliAgentRequest("List the files."));
        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(result.Text, Is.EqualTo("`a.txt`"));
        Assert.That(result.SessionId, Is.EqualTo("01a18335-b272-7883-8861-31ba989c1ddf"));
        Assert.That(result.Usage?.PromptTokens, Is.EqualTo(37907));
        Assert.That(result.Events.Select(e => e.Kind), Does.Contain(CliAgentEventKind.ToolUse));
        Assert.That(client.CallDetails.Single(), Is.InstanceOf<ProcessCallDetail>());

        List<CliAgentEvent> streamed = new();
        await foreach (CliAgentEvent evt in client.RunStreamingAsync(new CliAgentRequest("List the files."))) streamed.Add(evt);
        Assert.That(streamed.Last().Kind, Is.EqualTo(CliAgentEventKind.Completed));
        Assert.That(streamed.Select(e => e.Kind), Is.EqualTo(result.Events.Select(e => e.Kind)));
    }

    [Test]
    public async Task CliAgentConsoleRunsOneShotPromptAgainstFakeCli()
    {
        using FakeCli fake = FakeCli.Create("codex", "codex-happy");
        string? previousDir = Environment.GetEnvironmentVariable("POLYPROMPT_FAKE_CLI_DIR");
        string? previousScript = Environment.GetEnvironmentVariable("POLYPROMPT_FAKE_CLI_SCRIPT");
        TextWriter previousOut = System.Console.Out;
        StringWriter output = new StringWriter();
        try
        {
            Environment.SetEnvironmentVariable("POLYPROMPT_FAKE_CLI_DIR", fake.Directory);
            Environment.SetEnvironmentVariable("POLYPROMPT_FAKE_CLI_SCRIPT", "codex-happy");
            System.Console.SetOut(output);

            int exit = await CliAgentConsole.Program.Main(new[] { "--provider", "codex", "--executable", fake.ExecutablePath, "--prompt", "List the files.", "--directory", fake.Directory });
            Assert.That(exit, Is.EqualTo(0), output.ToString());

            string text = output.ToString();
            Assert.That(text, Does.Contain("Status: Ok (codex-cli 0.163.0)"));
            Assert.That(text, Does.Contain("[tool] command_execution /bin/zsh -lc ls"));
            Assert.That(text, Does.Contain("`a.txt`"));
            Assert.That(text, Does.Contain("[usage] input 37907, output 75"));
            Assert.That(fake.StandardInput, Is.EqualTo("List the files."));

            output.GetStringBuilder().Clear();
            exit = await CliAgentConsole.Program.Main(new[] { "--provider", "codex", "--executable", fake.ExecutablePath, "--models" });
            Assert.That(exit, Is.EqualTo(0), output.ToString());
            Assert.That(output.ToString(), Does.Contain("gpt-6.1-sol  (GPT-6.1-Sol)"));

            output.GetStringBuilder().Clear();
            exit = await CliAgentConsole.Program.Main(new[] { "--provider", "nope" });
            Assert.That(exit, Is.EqualTo(2));
            Assert.That(output.ToString(), Does.Contain("Usage: CliAgentConsole"));
        }
        finally
        {
            System.Console.SetOut(previousOut);
            Environment.SetEnvironmentVariable("POLYPROMPT_FAKE_CLI_DIR", previousDir);
            Environment.SetEnvironmentVariable("POLYPROMPT_FAKE_CLI_SCRIPT", previousScript);
        }
    }

    [Test]
    public async Task CodexLiveTurn()
    {
        if (Environment.GetEnvironmentVariable("POLYPROMPT_TEST_CODEX") != "1")
            Assert.Ignore("Set POLYPROMPT_TEST_CODEX=1 to run a live Codex turn (requires the codex CLI, logged in).");

        string directory = Path.Combine(Path.GetTempPath(), "polyprompt-console-codex-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using CodexAgentClient client = new CodexAgentClient { WorkingDirectory = directory, TimeoutMs = 300000 };
            CliConnectivityResult status = await client.ValidateConnectivityAsync();
            Assert.That(status.Status, Is.EqualTo(CliConnectivityStatus.Ok), status.Message);

            CliAgentResult result = await client.RunAsync(new CliAgentRequest("Reply with exactly the word PONG and nothing else."));
            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(result.Text, Does.Contain("PONG").IgnoreCase);
            Assert.That(result.SessionId, Is.Not.Null.And.Not.Empty);
            Assert.That(result.Usage?.PromptTokens, Is.GreaterThan(0));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
