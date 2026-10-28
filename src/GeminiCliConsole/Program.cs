namespace GeminiCliConsole;

using PolyPrompt.AgentConsoleSupport;
using PolyPrompt.Clients;

/// <summary>
/// Interactive and one-shot console for the Gemini CLI agent harness through <see cref="GeminiCliAgentClient"/>
/// (<c>gemini --output-format stream-json</c>). For the Gemini HTTP API use GeminiConsole instead.
/// </summary>
public static class Program
{
    /// <summary>Harness profile used by the console.</summary>
    public static readonly AgentConsoleProfile Profile = new AgentConsoleProfile
    {
        ApplicationName = "GeminiCliConsole",
        HarnessName = "Gemini CLI",
        CreateClient = () => new GeminiCliAgentClient(),
        Prerequisites =
            "Gemini CLI setup:\n" +
            "  1. Install the CLI (https://github.com/google-gemini/gemini-cli), for example: npm install -g @google/gemini-cli\n" +
            "  2. Sign in with the CLI itself: run 'gemini' once and sign in with Google, or set GEMINI_API_KEY in the environment\n" +
            "  3. Make sure 'gemini' is on PATH, or pass --executable /full/path/to/gemini\n" +
            "PolyPrompt never takes an API key for the Gemini CLI. It has no model list command; pass --model, for example gemini-2.5-pro.\n" +
            "This console drives the local CLI harness; GeminiConsole is the sample for the Gemini HTTP API."
    };

    /// <summary>Entry point.</summary>
    /// <param name="args">Arguments; run with --help for the list.</param>
    /// <returns>Exit code.</returns>
    public static Task<int> Main(string[] args) => AgentConsoleApplication.RunAsync(Profile, args);
}
