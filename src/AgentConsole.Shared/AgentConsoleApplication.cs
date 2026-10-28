namespace PolyPrompt.AgentConsoleSupport;

using PolyPrompt.Clients;
using PolyPrompt.ConsoleSupport;
using PolyPrompt.Models;

/// <summary>
/// Interactive and one-shot console for a single agent harness. Each turn runs the harness CLI as a child process;
/// the session id it reports is passed back on the next turn so the conversation continues, and harnesses that cannot
/// resume get the earlier turns replayed as history. Ctrl+C cancels the running turn and kills the process tree.
/// </summary>
public static class AgentConsoleApplication
{
    private const string Commands =
        "Commands:\n" +
        "  /help                 Show this list\n" +
        "  /setup                Show install and login steps\n" +
        "  /status               Check that the CLI is installed and logged in\n" +
        "  /models [FILTER]      List models the harness offers\n" +
        "  /model [NAME|default] Show or change the model\n" +
        "  /permission [MODE]    Show or change the permission mode (restricted, readonly, workspacewrite, unrestricted)\n" +
        "  /cd [PATH]            Show or change the working directory (starts a new session)\n" +
        "  /stream [on|off]      Show or change streaming output\n" +
        "  /session              Show the current session id\n" +
        "  /new                  Start a new session\n" +
        "  /exit                 Quit\n" +
        "Anything else is sent to the agent. Press Ctrl+C during a turn to cancel it.";

    /// <summary>
    /// Run the console.
    /// </summary>
    /// <param name="profile">Harness profile.</param>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>Process exit code: 0 success, 1 failure, 2 invalid arguments.</returns>
    public static async Task<int> RunAsync(AgentConsoleProfile profile, string[] args)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(args);

        if (args.Contains("--help") || args.Contains("-h"))
        {
            Console.WriteLine(Usage(profile));
            return 0;
        }

        if (args.Contains("--setup"))
        {
            Console.WriteLine(profile.Prerequisites);
            return 0;
        }

        Dictionary<string, string> options = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                Console.WriteLine("Unexpected argument: " + args[i]);
                Console.WriteLine(Usage(profile));
                return 2;
            }
            bool hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
            options[args[i][2..]] = hasValue ? args[++i] : "true";
        }

        using CliAgentClientBase client = profile.CreateClient();
        string? error = Configure(profile, client, options);
        if (error != null)
        {
            Console.WriteLine(error);
            Console.WriteLine(Usage(profile));
            return 2;
        }

        AgentConsoleState state = new AgentConsoleState
        {
            Streaming = !string.Equals(options.GetValueOrDefault("stream", "true"), "false", StringComparison.OrdinalIgnoreCase),
            SystemPrompt = options.GetValueOrDefault("system")
        };

        Console.WriteLine(profile.ApplicationName + " - PolyPrompt " + typeof(ClientBase).Assembly.GetName().Version + " " + profile.HarnessName + " agent harness");
        PrintConfiguration(profile, client, state);

        bool ready = await CheckAsync(profile, client).ConfigureAwait(false);
        if (options.ContainsKey("check")) return ready ? 0 : 1;
        if (options.TryGetValue("models", out string? filter))
            return await ListModelsAsync(profile, client, filter == "true" ? null : filter).ConfigureAwait(false) ? 0 : 1;

        if (options.TryGetValue("prompt", out string? prompt))
        {
            CliAgentResult? result = await TurnAsync(profile, client, state, prompt).ConfigureAwait(false);
            return result?.Success == true ? 0 : 1;
        }

        if (!ready) Console.WriteLine("The CLI is not ready; turns will fail until the steps in /setup are done.");
        Console.WriteLine("Type /help for commands.");
        while (true)
        {
            Console.Write("\n" + profile.HarnessName + "> ");
            string? line = Console.ReadLine();
            if (line == null) return 0;
            line = line.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('/'))
            {
                if (!await CommandAsync(profile, client, state, line).ConfigureAwait(false)) return 0;
                continue;
            }

            await TurnAsync(profile, client, state, line).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Usage text for a profile.
    /// </summary>
    /// <param name="profile">Harness profile.</param>
    /// <returns>Usage text.</returns>
    public static string Usage(AgentConsoleProfile profile)
    {
        string usage =
            "Usage: " + profile.ApplicationName + " [options]\n" +
            "  --prompt TEXT           Run one turn and exit (otherwise the console is interactive)\n" +
            "  --directory PATH        Working directory the agent runs in (default: current directory)\n" +
            "  --model NAME            Model in the harness's own naming (default: the harness default)\n" +
            "  --executable PATH       CLI executable name or path (default: " + profile.CreateClient().Executable + ")\n" +
            "  --permission MODE       restricted (default), readonly, workspacewrite, unrestricted\n" +
            "  --system TEXT           System instructions for each turn\n" +
            "  --timeout MS            Timeout per turn in milliseconds\n" +
            "  --stream true|false     Stream events as they arrive (default: true)\n" +
            "  --check                 Check installation and login, then exit\n" +
            (profile.ListModels != null ? "  --models [FILTER]       List models, then exit\n" : "") +
            "  --setup                 Show install and login steps\n";
        foreach (AgentConsoleOption option in profile.Options) usage += "  " + option.Usage + "\n";
        return usage + Commands;
    }

    private static string? Configure(AgentConsoleProfile profile, CliAgentClientBase client, Dictionary<string, string> options)
    {
        HashSet<string> known = new(StringComparer.OrdinalIgnoreCase) { "prompt", "directory", "model", "executable", "permission", "system", "timeout", "stream", "check", "models" };
        foreach (AgentConsoleOption option in profile.Options) known.Add(option.Name);
        foreach (string name in options.Keys)
            if (!known.Contains(name) || (name == "models" && profile.ListModels == null)) return "Unknown option: --" + name;

        if (options.TryGetValue("executable", out string? executable))
        {
            if (executable == "true") return "--executable needs a value.";
            client.Executable = executable;
        }

        if (options.TryGetValue("model", out string? model))
        {
            if (model == "true") return "--model needs a value.";
            client.Model = model;
        }

        if (options.TryGetValue("timeout", out string? timeout))
        {
            if (!int.TryParse(timeout, out int ms) || ms < 1) return "Invalid --timeout: " + timeout;
            client.TimeoutMs = ms;
        }

        if (options.TryGetValue("permission", out string? permission))
        {
            if (!TryParsePermission(permission, out CliPermissionMode mode)) return "Unknown permission mode: " + permission;
            client.PermissionMode = mode;
        }

        string directory = Path.GetFullPath(options.GetValueOrDefault("directory", Directory.GetCurrentDirectory()));
        if (!Directory.Exists(directory)) return "Directory not found: " + directory;
        client.WorkingDirectory = directory;

        foreach (AgentConsoleOption option in profile.Options)
        {
            if (!options.TryGetValue(option.Name, out string? value)) continue;
            string? error = option.Apply(client, value);
            if (error != null) return error;
        }

        return null;
    }

    private static bool TryParsePermission(string value, out CliPermissionMode mode)
    {
        return Enum.TryParse(value, true, out mode) && Enum.IsDefined(mode) && !int.TryParse(value, out _);
    }

    private static void PrintConfiguration(AgentConsoleProfile profile, CliAgentClientBase client, AgentConsoleState state)
    {
        Console.WriteLine("Executable: " + client.Executable);
        Console.WriteLine("Directory:  " + client.WorkingDirectory);
        Console.WriteLine("Model:      " + (client.Model ?? "(harness default)"));
        Console.WriteLine("Permission: " + client.PermissionMode + "  Streaming: " + (state.Streaming ? "on" : "off") + "  Timeout: " + client.TimeoutMs + " ms");
        Console.WriteLine("Sessions:   " + (client.SupportsSessionResume ? "resumed by id when the harness reports one, otherwise history is replayed" : "history replayed into the prompt"));
        string? extra = profile.Describe?.Invoke(client);
        if (!string.IsNullOrEmpty(extra)) Console.WriteLine(extra);
    }

    private static async Task<bool> CommandAsync(AgentConsoleProfile profile, CliAgentClientBase client, AgentConsoleState state, string line)
    {
        int space = line.IndexOf(' ');
        string command = (space < 0 ? line : line[..space]).ToLowerInvariant();
        string? argument = space < 0 ? null : line[(space + 1)..].Trim();
        if (argument?.Length == 0) argument = null;

        switch (command)
        {
            case "/exit":
            case "/quit":
                return false;
            case "/help":
            case "/?":
                Console.WriteLine(Commands);
                break;
            case "/setup":
                Console.WriteLine(profile.Prerequisites);
                break;
            case "/status":
                await CheckAsync(profile, client).ConfigureAwait(false);
                break;
            case "/models":
                await ListModelsAsync(profile, client, argument).ConfigureAwait(false);
                break;
            case "/model":
                if (argument != null) client.Model = argument.Equals("default", StringComparison.OrdinalIgnoreCase) ? null : argument;
                Console.WriteLine("Model: " + (client.Model ?? "(harness default)"));
                break;
            case "/permission":
                if (argument != null)
                {
                    if (!TryParsePermission(argument, out CliPermissionMode mode))
                    {
                        Console.WriteLine("Unknown permission mode: " + argument);
                        break;
                    }
                    client.PermissionMode = mode;
                }
                Console.WriteLine("Permission: " + client.PermissionMode);
                break;
            case "/cd":
                if (argument != null)
                {
                    string directory = Path.GetFullPath(argument, client.WorkingDirectory ?? Directory.GetCurrentDirectory());
                    if (!Directory.Exists(directory))
                    {
                        Console.WriteLine("Directory not found: " + directory);
                        break;
                    }
                    client.WorkingDirectory = directory;
                    state.Reset();
                }
                Console.WriteLine("Directory: " + client.WorkingDirectory);
                break;
            case "/stream":
                if (argument != null)
                {
                    if (argument is not ("on" or "off"))
                    {
                        Console.WriteLine("Use /stream on or /stream off.");
                        break;
                    }
                    state.Streaming = argument == "on";
                }
                Console.WriteLine("Streaming: " + (state.Streaming ? "on" : "off"));
                break;
            case "/session":
                Console.WriteLine("Session: " + (state.SessionId ?? (state.History.Count > 0 ? "(none reported; history replayed)" : "(new)")) + ", turns: " + state.History.Count / 2);
                break;
            case "/new":
                state.Reset();
                Console.WriteLine("New session.");
                break;
            default:
                Console.WriteLine("Unknown command " + command + ". Type /help for commands.");
                break;
        }

        return true;
    }

    private static async Task<bool> CheckAsync(AgentConsoleProfile profile, CliAgentClientBase client)
    {
        CliConnectivityResult status = await client.ValidateConnectivityAsync().ConfigureAwait(false);
        Console.WriteLine("Status:     " + status.Status
            + (status.Version != null ? " (" + status.Version + ")" : "")
            + (status.Success ? (status.AuthenticationVerified ? ", login verified" : ", login not checked by this CLI") : "")
            + (status.Message != null ? " - " + status.Message : ""));
        if (status.Status is CliConnectivityStatus.NotInstalled or CliConnectivityStatus.NotAuthenticated)
            Console.WriteLine(profile.Prerequisites);
        return status.Success;
    }

    private static async Task<bool> ListModelsAsync(AgentConsoleProfile profile, CliAgentClientBase client, string? filter)
    {
        if (profile.ListModels == null)
        {
            Console.WriteLine(profile.HarnessName + " has no model list command; pass a model name with --model or /model.");
            return false;
        }

        try
        {
            int count = 0;
            await foreach (ModelInformation model in profile.ListModels(client, filter, CancellationToken.None).ConfigureAwait(false))
            {
                Console.WriteLine("  " + model.Name + (model.DisplayName != null && model.DisplayName != model.Name ? "  (" + model.DisplayName + ")" : ""));
                count++;
            }
            Console.WriteLine(count + " model(s).");
            return true;
        }
        catch (CliAgentException ex)
        {
            Console.WriteLine("Model list failed (" + ex.FailureKind + "): " + ex.Message);
            if (ex.FailureKind is CliFailureKind.NotInstalled or CliFailureKind.NotAuthenticated) Console.WriteLine(profile.Prerequisites);
            return false;
        }
    }

    private static async Task<CliAgentResult?> TurnAsync(AgentConsoleProfile profile, CliAgentClientBase client, AgentConsoleState state, string prompt)
    {
        CliAgentRequest request = new CliAgentRequest(prompt)
        {
            SessionId = state.SessionId,
            SystemPrompt = state.SystemPrompt,
            History = state.SessionId != null || state.History.Count == 0 ? null : new List<ChatMessage>(state.History)
        };

        using CancellationTokenSource cts = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cts.Cancel(); };
        Console.CancelKeyPress += cancel;
        ConsoleRenderer renderer = new ConsoleRenderer(Console.Out);
        CliAgentResult? result = null;
        try
        {
            if (state.Streaming)
            {
                List<CliAgentEvent> events = new List<CliAgentEvent>();
                await foreach (CliAgentEvent evt in client.RunStreamingAsync(request, cts.Token).ConfigureAwait(false))
                {
                    events.Add(evt);
                    Render(renderer, evt);
                    if (evt.Kind == CliAgentEventKind.Completed)
                        result = new CliAgentResult { Success = true, Text = evt.Text ?? "", SessionId = evt.SessionId, Usage = evt.Usage, CostUsd = evt.CostUsd, Events = events };
                    else if (evt.Kind == CliAgentEventKind.Failed)
                        result = new CliAgentResult { Success = false, Error = evt.Text, FailureKind = evt.FailureKind ?? CliFailureKind.ProviderError, ExitCode = evt.ExitCode, SessionId = evt.SessionId, Events = events };
                }
                renderer.Finish();
            }
            else
            {
                result = await client.RunAsync(request, cts.Token).ConfigureAwait(false);
                foreach (CliAgentEvent evt in result.Events.Where(e => e.Kind is CliAgentEventKind.ToolUse or CliAgentEventKind.ToolResult or CliAgentEventKind.Notice))
                    Render(renderer, evt);
                if (result.Success)
                {
                    renderer.Thinking(string.Concat(result.Events.Where(e => e.Kind == CliAgentEventKind.ReasoningDelta).Select(e => e.Text)));
                    renderer.Answer(result.Text);
                }
                renderer.Finish();
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            renderer.Finish();
            Console.WriteLine("[cancelled] The turn was stopped and the " + profile.HarnessName + " process tree was killed.");
            return null;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or CliAgentException)
        {
            renderer.Finish();
            Console.WriteLine("[error] " + ex.Message);
            return null;
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
        }

        if (result == null)
        {
            Console.WriteLine("[error] The harness stream ended without a result.");
            return null;
        }

        PrintResult(profile, result);
        if (result.Success)
        {
            state.SessionId = result.SessionId ?? state.SessionId;
            state.History.Add(ChatMessage.User(prompt));
            state.History.Add(ChatMessage.Assistant(result.Text));
        }

        return result;
    }

    private static void Render(ConsoleRenderer renderer, CliAgentEvent evt)
    {
        switch (evt.Kind)
        {
            case CliAgentEventKind.Started:
                renderer.Finish();
                Console.WriteLine("[started]" + (evt.SessionId != null ? " session " + evt.SessionId : ""));
                break;
            case CliAgentEventKind.ReasoningDelta:
                renderer.Thinking(evt.Text);
                break;
            case CliAgentEventKind.TextDelta:
                renderer.Answer(evt.Text);
                break;
            case CliAgentEventKind.ToolUse:
                renderer.Finish();
                Console.WriteLine("[tool] " + evt.ToolName + (string.IsNullOrEmpty(evt.ToolInput) ? "" : " " + Truncate(evt.ToolInput, 200)));
                break;
            case CliAgentEventKind.ToolResult:
                renderer.Finish();
                Console.WriteLine("[tool result" + (evt.IsError ? ", error" : "") + "]" + (string.IsNullOrEmpty(evt.ToolOutput) ? "" : " " + Truncate(evt.ToolOutput, 200)));
                break;
            case CliAgentEventKind.Notice:
                renderer.Finish();
                Console.WriteLine("[notice] " + evt.Text);
                break;
        }
    }

    private static void PrintResult(AgentConsoleProfile profile, CliAgentResult result)
    {
        if (!result.Success)
        {
            Console.WriteLine("[failed: " + result.FailureKind + "] " + result.Error + (result.ExitCode.HasValue ? " (exit code " + result.ExitCode + ")" : ""));
            if (result.FailureKind is CliFailureKind.NotInstalled or CliFailureKind.NotAuthenticated) Console.WriteLine(profile.Prerequisites);
            return;
        }

        List<string> parts = new List<string>();
        if (result.SessionId != null) parts.Add("session " + result.SessionId);
        int tools = result.Events.Count(e => e.Kind == CliAgentEventKind.ToolUse);
        if (tools > 0) parts.Add(tools + " tool call(s)");
        if (result.Usage != null) parts.Add("input " + (result.Usage.PromptTokens?.ToString() ?? "-") + ", output " + (result.Usage.CompletionTokens?.ToString() ?? "-") + " tokens");
        if (result.CostUsd.HasValue) parts.Add("cost $" + result.CostUsd.Value);
        Console.WriteLine("[done]" + (parts.Count > 0 ? " " + string.Join("; ", parts) : ""));
    }

    private static string Truncate(string value, int length)
    {
        value = value.ReplaceLineEndings(" ");
        return value.Length <= length ? value : value[..length] + "...";
    }

    private sealed class AgentConsoleState
    {
        public bool Streaming { get; set; } = true;
        public string? SystemPrompt { get; set; } = null;
        public string? SessionId { get; set; } = null;
        public List<ChatMessage> History { get; } = new List<ChatMessage>();

        public void Reset()
        {
            SessionId = null;
            History.Clear();
        }
    }
}
