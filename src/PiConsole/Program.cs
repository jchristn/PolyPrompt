namespace PiConsole;

using PolyPrompt.AgentConsoleSupport;
using PolyPrompt.Clients;

/// <summary>
/// Interactive and one-shot console for Pi through <see cref="PiAgentClient"/> (<c>pi --mode rpc</c>). Model listing
/// accepts Pi's fuzzy search: <c>--models sonnet</c> or <c>/models sonnet</c>.
/// </summary>
public static class Program
{
    /// <summary>Harness profile used by the console.</summary>
    public static readonly AgentConsoleProfile Profile = new AgentConsoleProfile
    {
        ApplicationName = "PiConsole",
        HarnessName = "Pi",
        CreateClient = () => new PiAgentClient(),
        ListModels = (client, filter, token) => ((PiAgentClient)client).ListModelsAsync(filter, token),
        Prerequisites =
            "Pi setup:\n" +
            "  1. Install the CLI (https://pi.dev)\n" +
            "  2. Sign in with the CLI itself: run 'pi' then /login, or set a provider key such as ANTHROPIC_API_KEY in the environment\n" +
            "  3. Make sure 'pi' is on PATH, or pass --executable /full/path/to/pi\n" +
            "PolyPrompt never takes an API key for Pi. An empty model list means Pi has no usable provider credentials. Search models with --models FILTER."
    };

    /// <summary>Entry point.</summary>
    /// <param name="args">Arguments; run with --help for the list.</param>
    /// <returns>Exit code.</returns>
    public static Task<int> Main(string[] args) => AgentConsoleApplication.RunAsync(Profile, args);
}
