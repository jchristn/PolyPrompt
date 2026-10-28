namespace CliAgentConsole;

using PolyPrompt.Clients;
using PolyPrompt.Models;

/// <summary>
/// Interactive and one-shot console for the PolyPrompt agent harness clients. Each turn runs the selected CLI as a child
/// process; the session id it reports is passed back on the next turn so the conversation continues.
/// </summary>
public static class Program
{
    private const string Usage =
        "Usage: CliAgentConsole --provider codex|claudecode|geminicli|cursor|mux|pi|opencode\n" +
        "         [--model NAME] [--prompt TEXT] [--directory PATH] [--permission restricted|readonly|workspacewrite|unrestricted]\n" +
        "         [--stream true|false] [--timeout MS] [--executable PATH] [--check] [--models]\n" +
        "Without --prompt the console is interactive: /new starts a new session, /status checks the CLI, /models lists models, /exit quits.";

    public static async Task<int> Main(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h"))
        {
            Console.WriteLine(Usage);
            return 0;
        }

        Dictionary<string, string> options = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) continue;
            bool hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
            options[args[i][2..]] = hasValue ? args[++i] : "true";
        }

        string provider = options.GetValueOrDefault("provider", "codex").ToLowerInvariant();
        using CliAgentClientBase? client = Create(provider);
        if (client == null)
        {
            Console.WriteLine(Usage);
            return 2;
        }

        if (options.TryGetValue("executable", out string? executable)) client.Executable = executable;
        if (options.TryGetValue("model", out string? model)) client.Model = model;
        if (options.TryGetValue("timeout", out string? timeout) && int.TryParse(timeout, out int ms)) client.TimeoutMs = ms;
        client.WorkingDirectory = Path.GetFullPath(options.GetValueOrDefault("directory", Directory.GetCurrentDirectory()));
        if (!Directory.Exists(client.WorkingDirectory))
        {
            Console.WriteLine("Directory not found: " + client.WorkingDirectory);
            return 2;
        }

        if (options.TryGetValue("permission", out string? permission))
        {
            if (!Enum.TryParse(permission, true, out CliPermissionMode mode))
            {
                Console.WriteLine("Unknown permission mode: " + permission);
                return 2;
            }
            client.PermissionMode = mode;
        }

        bool stream = !string.Equals(options.GetValueOrDefault("stream", "true"), "false", StringComparison.OrdinalIgnoreCase);

        Console.WriteLine("PolyPrompt " + typeof(ClientBase).Assembly.GetName().Version + " agent harness: " + provider + " (" + client.Executable + ")");
        Console.WriteLine("Directory: " + client.WorkingDirectory + "  Permission: " + client.PermissionMode + (client.Model != null ? "  Model: " + client.Model : ""));

        if (!await CheckAsync(client).ConfigureAwait(false) && !options.ContainsKey("prompt")) return 1;
        if (options.ContainsKey("check")) return 0;
        if (options.ContainsKey("models")) return await ListModelsAsync(client).ConfigureAwait(false) ? 0 : 1;

        if (options.TryGetValue("prompt", out string? prompt))
        {
            CliAgentResult? result = await TurnAsync(client, prompt, null, stream).ConfigureAwait(false);
            return result?.Success == true ? 0 : 1;
        }

        string? sessionId = null;
        List<ChatMessage> history = new List<ChatMessage>();
        while (true)
        {
            Console.Write("\n> ");
            string? line = Console.ReadLine();
            if (line == null || line.Trim() == "/exit") return 0;
            if (string.IsNullOrWhiteSpace(line)) continue;

            switch (line.Trim())
            {
                case "/new":
                    sessionId = null;
                    history.Clear();
                    Console.WriteLine("New session.");
                    continue;
                case "/status":
                    await CheckAsync(client).ConfigureAwait(false);
                    continue;
                case "/models":
                    await ListModelsAsync(client).ConfigureAwait(false);
                    continue;
            }

            CliAgentRequest request = new CliAgentRequest(line) { SessionId = sessionId, History = client.SupportsSessionResume ? null : new List<ChatMessage>(history) };
            CliAgentResult? result = await TurnAsync(client, line, request, stream).ConfigureAwait(false);
            if (result?.Success == true)
            {
                sessionId = result.SessionId ?? sessionId;
                history.Add(ChatMessage.User(line));
                history.Add(ChatMessage.Assistant(result.Text));
            }
        }
    }

    private static CliAgentClientBase? Create(string provider)
    {
        return provider switch
        {
            "codex" => new CodexAgentClient(),
            "claudecode" or "claude" => new ClaudeCodeAgentClient(),
            "geminicli" or "gemini" => new GeminiCliAgentClient(),
            "cursor" or "cursorcli" => new CursorAgentClient(),
            "mux" => new MuxAgentClient(),
            "pi" => new PiAgentClient(),
            "opencode" => new OpenCodeAgentClient(),
            _ => null,
        };
    }

    private static async Task<bool> CheckAsync(CliAgentClientBase client)
    {
        CliConnectivityResult status = await client.ValidateConnectivityAsync().ConfigureAwait(false);
        Console.WriteLine("Status: " + status.Status + (status.Version != null ? " (" + status.Version + ")" : "") + (status.Message != null ? " - " + status.Message : ""));
        return status.Success;
    }

    private static async Task<bool> ListModelsAsync(CliAgentClientBase client)
    {
        IAsyncEnumerable<ModelInformation>? models = client switch
        {
            CodexAgentClient c => c.ListModelsAsync(),
            CursorAgentClient c => c.ListModelsAsync(),
            MuxAgentClient c => c.ListModelsAsync(),
            PiAgentClient c => c.ListModelsAsync(),
            OpenCodeAgentClient c => c.ListModelsAsync(),
            _ => null,
        };

        if (models == null)
        {
            Console.WriteLine("This harness has no model list command.");
            return false;
        }

        try
        {
            await foreach (ModelInformation model in models.ConfigureAwait(false))
                Console.WriteLine("  " + model.Name + (model.DisplayName != null ? "  (" + model.DisplayName + ")" : ""));
            return true;
        }
        catch (CliAgentException ex)
        {
            Console.WriteLine("Model list failed (" + ex.FailureKind + "): " + ex.Message);
            return false;
        }
    }

    private static async Task<CliAgentResult?> TurnAsync(CliAgentClientBase client, string prompt, CliAgentRequest? request, bool stream)
    {
        request ??= new CliAgentRequest(prompt);
        using CancellationTokenSource cts = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cts.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            if (!stream)
            {
                CliAgentResult result = await client.RunAsync(request, cts.Token).ConfigureAwait(false);
                foreach (CliAgentEvent evt in result.Events.Where(e => e.Kind == CliAgentEventKind.ToolUse)) Console.WriteLine("[tool] " + evt.ToolName + " " + evt.ToolInput);
                Console.WriteLine(result.Success ? result.Text : "[failed: " + result.FailureKind + "] " + result.Error);
                PrintUsage(result.Usage, result.CostUsd);
                return result;
            }

            CliAgentResult? summary = null;
            await foreach (CliAgentEvent evt in client.RunStreamingAsync(request, cts.Token).ConfigureAwait(false))
            {
                switch (evt.Kind)
                {
                    case CliAgentEventKind.TextDelta: Console.Write(evt.Text); break;
                    case CliAgentEventKind.ReasoningDelta: Console.Write("[thinking] " + evt.Text + "\n"); break;
                    case CliAgentEventKind.ToolUse: Console.WriteLine("\n[tool] " + evt.ToolName + " " + evt.ToolInput); break;
                    case CliAgentEventKind.ToolResult: Console.WriteLine("[tool result" + (evt.IsError ? ", error" : "") + "]"); break;
                    case CliAgentEventKind.Notice: Console.WriteLine("[notice] " + evt.Text); break;
                    case CliAgentEventKind.Completed:
                        Console.WriteLine();
                        PrintUsage(evt.Usage, evt.CostUsd);
                        summary = new CliAgentResult { Success = true, Text = evt.Text ?? "", SessionId = evt.SessionId, Usage = evt.Usage, CostUsd = evt.CostUsd };
                        break;
                    case CliAgentEventKind.Failed:
                        Console.WriteLine("\n[failed: " + evt.FailureKind + "] " + evt.Text);
                        summary = new CliAgentResult { Success = false, Error = evt.Text, FailureKind = evt.FailureKind ?? CliFailureKind.ProviderError };
                        break;
                }
            }
            return summary;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\n[cancelled; the harness process tree was killed]");
            return null;
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
        }
    }

    private static void PrintUsage(TokenUsage? usage, decimal? cost)
    {
        if (usage == null && cost == null) return;
        Console.WriteLine("[usage] input " + (usage?.PromptTokens?.ToString() ?? "-") + ", output " + (usage?.CompletionTokens?.ToString() ?? "-") + (cost.HasValue ? ", cost $" + cost.Value : ""));
    }
}
