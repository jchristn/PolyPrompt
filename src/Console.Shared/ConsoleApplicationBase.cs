namespace PolyPrompt.ConsoleSupport;

using PolyPrompt.Clients;
using PolyPrompt.Models;

/// <summary>Common console commands and the shared model/tool execution session.</summary>
public abstract class ConsoleApplicationBase
{
    private static ConsoleSession? _Session;
    private static readonly List<HttpClient> _Transports = new();
    private static bool _ToolsEnabled = true;
    private static string _WorkingDirectory = Directory.GetCurrentDirectory();

    protected static ConsoleSession GetSession(CompletionClientBase client) => _Session ??= new(client, new ProcessToolRuntime(), Console.Out)
    {
        WorkingDirectory = _WorkingDirectory
    };

    protected static string? ReadCommand()
    {
        Console.Write("Command [?/help]: ");
        return Console.ReadLine();
    }

    protected static void ShutdownSharedConsole()
    {
        _Session?.Dispose();
        _Session = null;
        foreach (HttpClient transport in _Transports) transport.Dispose();
        _Transports.Clear();
    }

    protected static HttpClient CreateConsoleHttpClient()
    {
        HttpClient transport = new(new GenerationThinkingHandler());
        _Transports.Add(transport);
        return transport;
    }

    protected static async Task RunSharedGenerationAsync(CompletionClientBase client, string prompt, bool streaming)
    {
        ConsoleRenderer renderer = new(Console.Out);
        GenerationThinkingHandler.Current.Value = renderer;
        try
        {
            if (streaming)
            {
                GenerationStreamingResponse response = await client.GenerateStreamingAsync(prompt);
                await foreach (GenerationStreamingChunk chunk in response.Chunks) { renderer.Thinking(chunk.ReasoningText); renderer.Answer(chunk.Text); }
                if (!response.Success) throw new InvalidOperationException(response.Error);
            }
            else
            {
                GenerationResponse response = await client.GenerateAsync(prompt);
                if (!response.Success) throw new InvalidOperationException(response.Error);
                if (!renderer.HasReasoning) renderer.Thinking(response.Reasoning);
                renderer.Answer(response.Text);
            }
        }
        catch (Exception ex) { renderer.Finish(); Console.WriteLine("Error: " + ex.Message); }
        finally { renderer.Finish(); GenerationThinkingHandler.Current.Value = null; }
    }

    protected static async Task<bool> RunSharedChatAsync(CompletionClientBase client, string prompt, bool streaming)
    {
        using CancellationTokenSource cancellation = new();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += handler;
        try { await GetSession(client).SendAsync(prompt, streaming, _ToolsEnabled, cancellation.Token); return true; }
        catch (OperationCanceledException) { Console.WriteLine("Canceled."); return false; }
        catch (Exception ex) { Console.WriteLine("Error: " + ex.Message); return false; }
        finally { Console.CancelKeyPress -= handler; }
    }

    protected static async Task<bool> TryHandleSharedCommandAsync(string input, CompletionClientBase? client = null)
    {
        string[] parts = input.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        string argument = parts.Length > 1 ? parts[1].Trim() : "";
        switch (parts[0].ToLowerInvariant())
        {
            case "tools":
                if (argument.Equals("off", StringComparison.OrdinalIgnoreCase)) _ToolsEnabled = false;
                else if (argument.Equals("on", StringComparison.OrdinalIgnoreCase)) _ToolsEnabled = true;
                else if (argument.Length > 0) { Console.WriteLine("Usage: tools [on|off]"); return true; }
                Console.WriteLine("Automatic tools: " + (_ToolsEnabled ? "on" : "off") + ". Working directory: " + _WorkingDirectory);
                using (ProcessToolRuntime runtime = new())
                    foreach (ToolDefinition definition in await runtime.GetToolsAsync()) Console.WriteLine("  " + definition.Name + " - " + definition.Description);
                if (client == null) Console.WriteLine("This provider has no chat capability; model-driven tool calling requires a completion provider.");
                return true;
            case "pwd": Console.WriteLine(_WorkingDirectory); return true;
            case "cd":
                if (argument.Length == 0) { Console.WriteLine(_WorkingDirectory); return true; }
                string target = Path.GetFullPath(argument, _WorkingDirectory);
                if (!Directory.Exists(target)) throw new DirectoryNotFoundException(target);
                _WorkingDirectory = target;
                if (_Session != null) _Session.WorkingDirectory = target;
                Console.WriteLine(target);
                return true;
            case "reset": _Session?.Reset(); Console.WriteLine("Conversation cleared."); return true;
            case "reasoning":
                if (client == null) { Console.WriteLine("This provider has no chat reasoning capability."); return true; }
                if (argument.Equals("default", StringComparison.OrdinalIgnoreCase)) client.Defaults.ReasoningEffort = null;
                else if (Enum.TryParse(argument, true, out ReasoningEffortLevel level) && Enum.IsDefined(level))
                    client.Defaults.ReasoningEffort = new ReasoningEffort(level);
                else { Console.WriteLine("Usage: reasoning default|minimal|low|medium|high"); return true; }
                Console.WriteLine("Reasoning setting updated. Returned thinking is always displayed separately.");
                return true;
            default: return false;
        }
    }

    protected static void PrintSharedHelp()
    {
        Console.WriteLine("  tools [on|off]  List Mux tools / enable or disable automatic execution (default: on)");
        Console.WriteLine("  pwd / cd PATH   Show or change the working directory for tools");
        Console.WriteLine("  reset           Clear the shared chat conversation");
        Console.WriteLine("  reasoning LEVEL Set default, minimal, low, medium, or high (model dependent)");
        Console.WriteLine("  Ctrl+C          Cancel a chat turn and its running tool process");
    }
}
