namespace PolyPrompt.AgentConsoleSupport;

using PolyPrompt.Clients;
using PolyPrompt.Models;

/// <summary>
/// Describes one agent harness console: how to create its client, what the CLI needs before it can run, and any
/// harness-specific command-line options.
/// </summary>
public sealed class AgentConsoleProfile
{
    /// <summary>Console application name shown in usage, for example <c>ClaudeCodeConsole</c>.</summary>
    public string ApplicationName { get; set; } = "";

    /// <summary>Harness display name, for example <c>Claude Code</c>.</summary>
    public string HarnessName { get; set; } = "";

    /// <summary>Creates a new client with its default executable.</summary>
    public Func<CliAgentClientBase> CreateClient { get; set; } = () => throw new InvalidOperationException("No client factory.");

    /// <summary>Install and login steps printed by <c>--setup</c>, <c>/setup</c>, and after install or auth failures.</summary>
    public string Prerequisites { get; set; } = "";

    /// <summary>Lists models with an optional filter, or null when the harness has no model list command.</summary>
    public Func<CliAgentClientBase, string?, CancellationToken, IAsyncEnumerable<ModelInformation>>? ListModels { get; set; } = null;

    /// <summary>Harness-specific options beyond the common ones.</summary>
    public List<AgentConsoleOption> Options { get; set; } = new List<AgentConsoleOption>();

    /// <summary>Lines describing the harness-specific configuration, printed in the banner.</summary>
    public Func<CliAgentClientBase, string?>? Describe { get; set; } = null;
}

/// <summary>A harness-specific command-line option, for example Mux's <c>--endpoint</c>.</summary>
public sealed class AgentConsoleOption
{
    /// <summary>Option name without the leading dashes.</summary>
    public string Name { get; set; } = "";

    /// <summary>Usage text, for example <c>--endpoint NAME  Named Mux endpoint</c>.</summary>
    public string Usage { get; set; } = "";

    /// <summary>Applies the value to the client; returns an error message or null.</summary>
    public Func<CliAgentClientBase, string, string?> Apply { get; set; } = (_, _) => null;
}
