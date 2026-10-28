namespace MuxConsole;

using PolyPrompt.AgentConsoleSupport;
using PolyPrompt.Clients;

/// <summary>
/// Interactive and one-shot console for Mux through <see cref="MuxAgentClient"/>
/// (<c>mux print --output-format jsonl</c>). Mux-specific options select the endpoint, reasoning display, and session
/// persistence.
/// </summary>
public static class Program
{
    /// <summary>Harness profile used by the console.</summary>
    public static readonly AgentConsoleProfile Profile = new AgentConsoleProfile
    {
        ApplicationName = "MuxConsole",
        HarnessName = "Mux",
        CreateClient = () => new MuxAgentClient(),
        ListModels = (client, _, token) => ((MuxAgentClient)client).ListModelsAsync(token),
        Describe = client =>
        {
            MuxAgentClient mux = (MuxAgentClient)client;
            return "Mux:        endpoint " + (mux.Endpoint ?? "(Mux default)") + ", show thinking " + mux.ShowThinking + ", persist sessions " + mux.PersistSessions;
        },
        Options = new List<AgentConsoleOption>
        {
            new AgentConsoleOption
            {
                Name = "endpoint",
                Usage = "--endpoint NAME         Named endpoint from the Mux configuration (default: Mux default)",
                Apply = (client, value) =>
                {
                    if (value == "true") return "--endpoint needs a value.";
                    ((MuxAgentClient)client).Endpoint = value;
                    return null;
                }
            },
            new AgentConsoleOption
            {
                Name = "show-thinking",
                Usage = "--show-thinking true|false  Stream model reasoning (default: true)",
                Apply = (client, value) => SetBool(value, "--show-thinking", v => ((MuxAgentClient)client).ShowThinking = v)
            },
            new AgentConsoleOption
            {
                Name = "persist",
                Usage = "--persist true|false    Save sessions so later turns resume them (default: false; history is replayed instead)",
                Apply = (client, value) => SetBool(value, "--persist", v => ((MuxAgentClient)client).PersistSessions = v)
            }
        },
        Prerequisites =
            "Mux setup:\n" +
            "  1. Install the CLI (https://usemux.ai): dotnet tool install -g mux.cli\n" +
            "  2. Configure an endpoint in Mux (its own settings); 'mux probe' checks it. The status check runs a probe, which costs one model call.\n" +
            "  3. Make sure 'mux' is on PATH, or pass --executable /full/path/to/mux\n" +
            "PolyPrompt never takes an API key for Mux. Select a configured endpoint with --endpoint and list its models with --models."
    };

    /// <summary>Entry point.</summary>
    /// <param name="args">Arguments; run with --help for the list.</param>
    /// <returns>Exit code.</returns>
    public static Task<int> Main(string[] args) => AgentConsoleApplication.RunAsync(Profile, args);

    private static string? SetBool(string value, string name, Action<bool> set)
    {
        if (!bool.TryParse(value, out bool parsed)) return name + " must be true or false.";
        set(parsed);
        return null;
    }
}
