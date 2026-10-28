namespace OpenCodeConsole;

using PolyPrompt.AgentConsoleSupport;
using PolyPrompt.Clients;

/// <summary>
/// Interactive and one-shot console for OpenCode through <see cref="OpenCodeAgentClient"/>
/// (<c>opencode run --format json</c>). Sessions resume by id across turns.
/// </summary>
public static class Program
{
    /// <summary>Harness profile used by the console.</summary>
    public static readonly AgentConsoleProfile Profile = new AgentConsoleProfile
    {
        ApplicationName = "OpenCodeConsole",
        HarnessName = "OpenCode",
        CreateClient = () => new OpenCodeAgentClient(),
        ListModels = (client, _, token) => ((OpenCodeAgentClient)client).ListModelsAsync(token),
        Prerequisites =
            "OpenCode setup:\n" +
            "  1. Install the CLI (https://opencode.ai/docs), for example: npm install -g opencode-ai\n" +
            "  2. Sign in with the CLI itself: opencode auth login\n" +
            "  3. Make sure 'opencode' is on PATH, or pass --executable /full/path/to/opencode\n" +
            "PolyPrompt never takes an API key for OpenCode. Models are named provider/model (for example anthropic/claude-sonnet-4-5); list them with --models."
    };

    /// <summary>Entry point.</summary>
    /// <param name="args">Arguments; run with --help for the list.</param>
    /// <returns>Exit code.</returns>
    public static Task<int> Main(string[] args) => AgentConsoleApplication.RunAsync(Profile, args);
}
