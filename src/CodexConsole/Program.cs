namespace CodexConsole;

using PolyPrompt.AgentConsoleSupport;
using PolyPrompt.Clients;

/// <summary>
/// Interactive and one-shot console for OpenAI Codex through <see cref="CodexAgentClient"/> (<c>codex exec --json</c>).
/// Sessions resume by id across turns (<c>codex exec resume</c>).
/// </summary>
public static class Program
{
    /// <summary>Harness profile used by the console.</summary>
    public static readonly AgentConsoleProfile Profile = new AgentConsoleProfile
    {
        ApplicationName = "CodexConsole",
        HarnessName = "Codex",
        CreateClient = () => new CodexAgentClient(),
        ListModels = (client, _, token) => ((CodexAgentClient)client).ListModelsAsync(token),
        Prerequisites =
            "Codex setup:\n" +
            "  1. Install the CLI (https://developers.openai.com/codex/cli), for example: npm install -g @openai/codex\n" +
            "  2. Sign in with the CLI itself: codex login, or set OPENAI_API_KEY / CODEX_API_KEY in the environment\n" +
            "  3. Make sure 'codex' is on PATH, or pass --executable /full/path/to/codex\n" +
            "PolyPrompt never takes an API key for Codex. List models with --models."
    };

    /// <summary>Entry point.</summary>
    /// <param name="args">Arguments; run with --help for the list.</param>
    /// <returns>Exit code.</returns>
    public static Task<int> Main(string[] args) => AgentConsoleApplication.RunAsync(Profile, args);
}
