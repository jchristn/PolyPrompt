namespace PolyPrompt.ConsoleSupport;

using PolyPrompt.Models;

/// <summary>Tools advertised to the model and their local execution runtime.</summary>
public interface IConsoleToolRuntime : IDisposable
{
    Task<IReadOnlyList<ToolDefinition>> GetToolsAsync(CancellationToken token = default);
    Task<string> ExecuteAsync(ToolCall call, string workingDirectory, CancellationToken token = default);
}
