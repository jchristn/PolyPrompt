namespace CursorCliConsole;

using PolyPrompt.AgentConsoleSupport;
using PolyPrompt.Clients;

/// <summary>
/// Interactive and one-shot console for the Cursor CLI through <see cref="CursorAgentClient"/>
/// (<c>cursor-agent -p --output-format stream-json</c>). Sessions resume by id across turns.
/// </summary>
public static class Program
{
    /// <summary>Harness profile used by the console.</summary>
    public static readonly AgentConsoleProfile Profile = new AgentConsoleProfile
    {
        ApplicationName = "CursorCliConsole",
        HarnessName = "Cursor CLI",
        CreateClient = () => new CursorAgentClient(),
        ListModels = (client, _, token) => ((CursorAgentClient)client).ListModelsAsync(token),
        Prerequisites =
            "Cursor CLI setup:\n" +
            "  1. Install the CLI (https://cursor.com/docs/cli/installation), for example: curl https://cursor.com/install -fsS | bash\n" +
            "  2. Sign in with the CLI itself: cursor-agent login, or set CURSOR_API_KEY in the environment\n" +
            "  3. Trust the workspace once: run 'cursor-agent' interactively in the --directory you will use (print mode requires a trusted workspace)\n" +
            "  4. Make sure 'cursor-agent' is on PATH (it is also installed as 'agent'), or pass --executable\n" +
            "PolyPrompt never takes an API key for the Cursor CLI. List models with --models."
    };

    /// <summary>Entry point.</summary>
    /// <param name="args">Arguments; run with --help for the list.</param>
    /// <returns>Exit code.</returns>
    public static Task<int> Main(string[] args) => AgentConsoleApplication.RunAsync(Profile, args);
}
