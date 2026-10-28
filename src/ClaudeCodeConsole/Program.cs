namespace ClaudeCodeConsole;

using PolyPrompt.AgentConsoleSupport;
using PolyPrompt.Clients;

/// <summary>
/// Interactive and one-shot console for Claude Code through <see cref="ClaudeCodeAgentClient"/>
/// (<c>claude -p --output-format stream-json</c>). Sessions resume by id across turns.
/// </summary>
public static class Program
{
    /// <summary>Harness profile used by the console.</summary>
    public static readonly AgentConsoleProfile Profile = new AgentConsoleProfile
    {
        ApplicationName = "ClaudeCodeConsole",
        HarnessName = "Claude Code",
        CreateClient = () => new ClaudeCodeAgentClient(),
        Prerequisites =
            "Claude Code setup:\n" +
            "  1. Install the CLI (https://code.claude.com/docs/en/setup), for example: npm install -g @anthropic-ai/claude-code\n" +
            "  2. Sign in with the CLI itself: claude auth login (check with: claude auth status)\n" +
            "  3. Make sure 'claude' is on PATH, or pass --executable /full/path/to/claude\n" +
            "PolyPrompt never takes an API key for Claude Code. Claude Code has no model list command; pass --model sonnet, opus, haiku, or a full model id."
    };

    /// <summary>Entry point.</summary>
    /// <param name="args">Arguments; run with --help for the list.</param>
    /// <returns>Exit code.</returns>
    public static Task<int> Main(string[] args) => AgentConsoleApplication.RunAsync(Profile, args);
}
