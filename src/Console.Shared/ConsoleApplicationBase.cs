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

    protected static ConsoleSession GetSession(ProviderClientBase client) => _Session ??= new(client, new ProcessToolRuntime(), Console.Out)
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

    protected static async Task RunSharedGenerationAsync(ProviderClientBase client, string prompt, bool streaming)
    {
        ConsoleRenderer renderer = new(Console.Out);
        GenerationThinkingHandler.Current.Value = renderer;
        try
        {
            if (streaming)
            {
                GenerationStreamingResponse response = await client.GenerateStreamingAsync(prompt);
                await foreach (GenerationStreamingChunk chunk in response.Chunks) { renderer.Thinking(chunk.ReasoningText); renderer.Answer(chunk.Text); }
                if (response.State != StreamingState.Completed) throw new InvalidOperationException(response.StreamError ?? "Generation stream did not complete.");
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

    protected static async Task<bool> RunSharedChatAsync(ProviderClientBase client, string prompt, bool streaming)
    {
        using CancellationTokenSource cancellation = new();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += handler;
        try { await GetSession(client).SendAsync(prompt, streaming, _ToolsEnabled, cancellation.Token); return true; }
        catch (OperationCanceledException) { Console.WriteLine("Canceled."); return false; }
        catch (Exception ex) { Console.WriteLine("Error: " + ex.Message); return false; }
        finally { Console.CancelKeyPress -= handler; }
    }

    protected static async Task<bool> TryHandleSharedCommandAsync(string input, ProviderClientBase? client = null)
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
            case "reset": _Session?.Reset(); Console.WriteLine("Conversation and attachments cleared."); return true;
            case "attach": Attach(client, argument); return true;
            case "transcribe": await TranscribeAsync(client, argument); return true;
            case "transcription-model":
                if (client?.Defaults.Transcription == null) { Console.WriteLine("This provider does not support transcription (Transcribe)."); return true; }
                if (argument.Length > 0) client.Defaults.Transcription.Model = argument;
                Console.WriteLine("Transcription model: " + (client.Defaults.Transcription.Model ?? "(none)"));
                return true;
            case "reasoning":
                if (client == null) { Console.WriteLine("This provider has no chat reasoning capability."); return true; }
                if (argument.Equals("default", StringComparison.OrdinalIgnoreCase)) client.Defaults.Completion!.ReasoningEffort = null;
                else if (Enum.TryParse(argument, true, out ReasoningEffortLevel level) && Enum.IsDefined(level))
                    client.Defaults.Completion!.ReasoningEffort = new ReasoningEffort(level);
                else { Console.WriteLine("Usage: reasoning default|minimal|low|medium|high"); return true; }
                Console.WriteLine("Reasoning setting updated. Returned thinking is always displayed separately.");
                return true;
            default: return false;
        }
    }

    private static void Attach(ProviderClientBase? client, string argument)
    {
        if (client == null || !client.Supports(Capability.Chat)) { Console.WriteLine("This provider has no chat capability, so it cannot take attachments."); return; }
        ConsoleSession session = GetSession(client);
        if (argument.Length == 0)
        {
            if (session.Attachments.Count == 0) Console.WriteLine("No attachments queued. Usage: attach PATH | attach clear");
            foreach (ContentPart queued in session.Attachments) Console.WriteLine("  [" + queued.Type.ToString().ToLowerInvariant() + "] " + (queued.Name ?? queued.Url ?? queued.FileReference) + " (" + queued.MediaType + ")");
            return;
        }
        if (argument.Equals("clear", StringComparison.OrdinalIgnoreCase)) { session.ClearAttachments(); Console.WriteLine("Attachments cleared."); return; }

        ContentPart part = ContentPart.FromFile(Path.GetFullPath(argument, _WorkingDirectory));
        Capability needed = part.Type switch { ContentPartType.Image => Capability.ImageInput, ContentPartType.Audio => Capability.AudioInput, _ => Capability.VideoInput };
        if (!client.Supports(needed)) { Console.WriteLine(client.ProviderName + " does not support " + part.Type.ToString().ToLowerInvariant() + " input (" + needed + "); nothing was attached."); return; }
        session.Attach(part);
        Console.WriteLine("Attached [" + part.Type.ToString().ToLowerInvariant() + "] " + part.Name + " (" + part.MediaType + ", " + part.Data!.Length.ToString("N0") + " bytes). It is sent with the next chat prompt.");
    }

    private static async Task TranscribeAsync(ProviderClientBase? client, string argument)
    {
        if (client == null || !client.Supports(Capability.Transcribe)) { Console.WriteLine("This provider does not support transcription (Transcribe)."); return; }
        if (argument.Length == 0) { Console.WriteLine("Usage: transcribe PATH [language]"); return; }
        if (string.IsNullOrWhiteSpace(client.Defaults.Transcription?.Model)) { Console.WriteLine("No transcription model is set; use 'transcription-model NAME' first."); return; }
        string[] pieces = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string? language = pieces.Length > 1 && pieces[^1].Length == 2 ? pieces[^1] : null;
        string path = Path.GetFullPath(language == null ? argument : argument.Substring(0, argument.LastIndexOf(' ')).Trim(), _WorkingDirectory);
        TranscriptionRequest request = new() { Audio = File.ReadAllBytes(path), FileName = Path.GetFileName(path), MediaType = MediaTypes.FromFileExtension(path), Language = language, Format = TranscriptionFormat.VerboseJson, TimestampGranularities = { TranscriptionTimestampGranularity.Segment } };
        TranscriptionResponse response;
        try { response = await client.TranscribeAsync(request); }
        catch (ArgumentException)
        {
            // The model has no segments (for example gpt-4o-transcribe): fall back to plain JSON.
            request.Format = TranscriptionFormat.Json;
            request.TimestampGranularities.Clear();
            response = await client.TranscribeAsync(request);
        }
        if (!response.Success) { Console.WriteLine("Error: " + response.Error); return; }
        Console.WriteLine("--- Transcript ---");
        if (response.Segments.Count == 0) Console.WriteLine(response.Text);
        foreach (TranscriptionSegment segment in response.Segments)
            Console.WriteLine("[" + Time(segment.StartSeconds) + " - " + Time(segment.EndSeconds) + "] " + segment.Text.Trim());
        if (response.Language != null || response.DurationSeconds != null)
            Console.WriteLine("Language: " + (response.Language ?? "?") + "  Duration: " + (response.DurationSeconds?.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) ?? "?") + " s");
    }

    private static string Time(double seconds) => TimeSpan.FromSeconds(seconds).ToString(@"mm\:ss\.f", System.Globalization.CultureInfo.InvariantCulture);

    protected static void PrintSharedHelp()
    {
        Console.WriteLine("  tools [on|off]  List Mux tools / enable or disable automatic execution (default: on)");
        Console.WriteLine("  pwd / cd PATH   Show or change the working directory for tools");
        Console.WriteLine("  reset           Clear the shared chat conversation and attachments");
        Console.WriteLine("  attach PATH     Queue an image, audio, or video file for the next chat prompt (attach clear)");
        Console.WriteLine("  transcribe PATH [lang]  Transcribe an audio file and print timed segments");
        Console.WriteLine("  transcription-model NAME  Set the transcription model (an Azure deployment name on Azure)");
        Console.WriteLine("  reasoning LEVEL Set default, minimal, low, medium, or high (model dependent)");
        Console.WriteLine("  Ctrl+C          Cancel a chat turn and its running tool process");
    }
}
