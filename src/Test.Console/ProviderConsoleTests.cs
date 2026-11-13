namespace Test.Console;

using NUnit.Framework;
using Test.Shared;

/// <summary>
/// Drives each 4.0 provider console end to end, in process, with scripted input against the local fixture server: the
/// setup prompts, then settings, connectivity, model listing, and the console's capability commands. Each console now
/// holds one provider client, so every command goes through the same object.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class ProviderConsoleTests
{
    public sealed record ProviderConsole(string Name, Func<string, string[]> Script, Func<string[], Task> Main, string[] Expected, string[]? Args = null)
    {
        public override string ToString() => Name;
    }

    public static IEnumerable<ProviderConsole> Consoles()
    {
        const string key = LocalClients.TestKey;
        yield return new ProviderConsole("OllamaConsole",
            e => new[] { e, key, "test-model", "all-minilm", "", "", "settings", "val", "mod", "gen", "hello", "em", "hello" },
            OllamaConsole.Program.Main, new[] { "Connectivity: OK", "Capabilities: Chat, ToolChat, Generate, Embed, Models", "test-model", "dimensions" });
        yield return new ProviderConsole("OllamaCloudConsole",
            e => new[] { key, e, "test-model", "all-minilm", "", "", "settings", "val", "mod", "gen", "hello", "em", "hello" },
            OllamaCloudConsole.Program.Main, new[] { "Connectivity: OK", "test-model", "dimensions" });
        yield return new ProviderConsole("OpenAIConsole",
            e => new[] { e, LocalOpenAiResponsesRoutes.Key, "test-model", "test-embedding", "", "", "settings", "val", "mod", "gen", "hello", "em", "hello" },
            OpenAIConsole.Program.Main, new[] { "OpenAiClient", "Connectivity: OK", "Capabilities: Chat, ToolChat, Generate, Embed, Models", "pong", "dimensions" });
        yield return new ProviderConsole("OpenAIConsole --compatible",
            e => new[] { key, "test-model", "text-embedding-3-small", "", "", "settings", "val", "mod", "gen", "hello", "em", "hello" },
            OpenAIConsole.Program.Main, new[] { "OpenAiCompatibleClient", "Connectivity: OK", "dimensions" }, new[] { "--compatible", "{endpoint}" });
        yield return new ProviderConsole("XAiConsole",
            e => new[] { e, LocalXAiRoutes.Key, "test-model", "", "", "settings", "val", "mod", "gen", "hello" },
            XAiConsole.Program.Main, new[] { "Connectivity: OK", "Capabilities: Chat, ToolChat, Generate, Models", "pong" });
        yield return new ProviderConsole("AnthropicConsole",
            e => new[] { e, key, "", "test-model", "", "", "settings", "val", "mod", "gen", "hello" },
            AnthropicConsole.Program.Main, new[] { "Connectivity: OK", "Capabilities: Chat, ToolChat, Generate, Models" });
        yield return new ProviderConsole("GeminiConsole",
            e => new[] { e, key, "test-model", "test-embed", "", "", "settings", "val", "mod", "gen", "hello", "em", "hello" },
            GeminiConsole.Program.Main, new[] { "Connectivity: OK", "Capabilities: Chat, ToolChat, Generate, Embed, Models", "dimensions" });
        yield return new ProviderConsole("CohereConsole",
            e => new[] { e, LocalExtendedRoutes.CohereTestKey, "command-a-plus-05-2026", "embed-v4.0", "rerank-v5.0-fast", "", "", "settings", "val", "mod", "gen", "hello", "em", "hello" },
            CohereConsole.Program.Main, new[] { "Connectivity: OK", "Capabilities: Chat, ToolChat, Generate, Embed, Rerank, Classify, Models", "pong", "Embeddings returned" });
        yield return new ProviderConsole("TeiConsole",
            e => new[] { e, key, "", "settings", "val", "info", "mod" },
            TeiConsole.Program.Main, new[] { "Connectivity: OK", "Capabilities: Embed, SparseEmbed, Rerank, Classify, Models" });
        yield return new ProviderConsole("VoyageAIConsole",
            e => new[] { e, key, "voyage-3.5", "", "settings", "val" },
            VoyageAIConsole.Program.Main, new[] { "Connectivity: OK", "Capabilities: Embed, Rerank", "voyage-3.5" });
        yield return new ProviderConsole("TypeSafeConsole",
            e => new[] { e, key, "jev-latest", "", "settings", "val" },
            TypeSafeConsole.Program.Main, new[] { "Connectivity: OK", "Capabilities: Decide", "jev-latest" });
    }

    private TextWriter _PreviousOut = null!;
    private TextReader _PreviousIn = null!;

    [SetUp]
    public void Setup()
    {
        _PreviousOut = System.Console.Out;
        _PreviousIn = System.Console.In;
    }

    [TearDown]
    public void Cleanup()
    {
        System.Console.SetOut(_PreviousOut);
        System.Console.SetIn(_PreviousIn);
    }

    [TestCaseSource(nameof(Consoles))]
    public async Task ConsoleRunsEveryScriptedCommandThroughOneClient(ProviderConsole console)
    {
        using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
        string script = string.Join(Environment.NewLine, console.Script(server.Endpoint)) + Environment.NewLine;
        string[] args = (console.Args ?? Array.Empty<string>()).Select(a => a.Replace("{endpoint}", server.Endpoint)).ToArray();
        using StringWriter output = new();
        System.Console.SetIn(new StringReader(script));
        System.Console.SetOut(output);

        Task run = console.Main(args);
        Task finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(60))).ConfigureAwait(false);
        System.Console.SetOut(_PreviousOut);
        Assert.That(finished, Is.SameAs(run), console.Name + " did not finish its script. Output:" + Environment.NewLine + output);
        await run.ConfigureAwait(false);

        string text = output.ToString();
        foreach (string expected in console.Expected)
            Assert.That(text, Does.Contain(expected), console.Name + " output:" + Environment.NewLine + text);
        Assert.That(text, Does.Not.Contain("Error:"), console.Name + " reported an error:" + Environment.NewLine + text);
        Assert.That(server.RequestPaths.Count, Is.GreaterThan(0), console.Name + " should have reached the server.");
    }
}
