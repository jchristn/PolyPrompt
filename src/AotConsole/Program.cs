namespace AotConsole;

using System.Text.Json;
using PolyPrompt.Clients;
using PolyPrompt.ConsoleSupport;
using PolyPrompt.Helpers;
using PolyPrompt.Models;

/// <summary>Native AOT console using the same conversation runner and Mux tools as the interactive harnesses.</summary>
public sealed class Program : ConsoleApplicationBase
{
    public static async Task<int> Main(string[] args)
    {
        Dictionary<string, string> options = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i].StartsWith("--", StringComparison.Ordinal)) options[args[i][2..]] = args[++i];
        string Option(string name, string fallback) => options.GetValueOrDefault(name, fallback);
        if (args.Contains("--help") || args.Contains("-h"))
        {
            System.Console.WriteLine("Usage: AotConsole --provider ollama|ollamacloud|openai|anthropic|gemini|xai [--endpoint URL] [--model NAME] [--key KEY] [--prompt TEXT] [--streaming true|false] [--directory PATH] [--save PATH]");
            return 0;
        }
        string provider = Option("provider", "ollama").ToLowerInvariant();
        string? key = options.GetValueOrDefault("key") ?? Environment.GetEnvironmentVariable(provider == "ollamacloud" ? "OLLAMA_API_KEY" : provider.ToUpperInvariant() + "_API_KEY");
        string? endpoint = options.GetValueOrDefault("endpoint");
        if ((provider == "xai" || provider == "ollamacloud") && string.IsNullOrWhiteSpace(key))
        {
            Console.WriteLine("Set " + (provider == "xai" ? "XAI_API_KEY" : "OLLAMA_API_KEY") + " or pass --key.");
            return 2;
        }
        using CompletionClientBase? client = provider switch
        {
            "xai" => new XAiCompletionClient(key!, endpoint ?? "https://api.x.ai", httpClient: CreateConsoleHttpClient()),
            "ollama" => new OllamaCompletionClient(endpoint ?? "http://localhost:11434", key, httpClient: CreateConsoleHttpClient()),
            "ollamacloud" => new OllamaCloudCompletionClient(key!, httpClient: CreateConsoleHttpClient()),
            "openai" => new OpenAiCompletionClient(endpoint ?? "https://api.openai.com", key, httpClient: CreateConsoleHttpClient()),
            "anthropic" => new AnthropicCompletionClient(endpoint ?? "https://api.anthropic.com", key, httpClient: CreateConsoleHttpClient()),
            "gemini" => new GeminiCompletionClient(endpoint ?? "https://generativelanguage.googleapis.com", key, httpClient: CreateConsoleHttpClient()),
            _ => null
        };
        if (client == null)
        {
            Console.WriteLine("Usage: AotConsole --provider ollama|ollamacloud|openai|anthropic|gemini|xai [--endpoint URL] [--model NAME] [--key KEY] [--prompt TEXT] [--streaming true|false] [--directory PATH] [--save PATH]");
            return 2;
        }
        client.Model = Option("model", provider switch
        {
            "xai" => "grok-4.7", "openai" => "gpt-4o-mini", "anthropic" => "claude-haiku-4-5-20251001", "gemini" => "gemini-2.5-flash",
            "ollamacloud" => "gpt-oss:20b", _ => "qwen3:4b"
        });
        client.TimeoutMs = 120000;
        Console.WriteLine("PolyPrompt " + typeof(ClientBase).Assembly.GetName().Version + " on " + provider + " (" + client.Model + ")");
        Console.WriteLine("Reflection-based JSON: " + (Serializer.IsReflectionEnabled ? "enabled" : "disabled (Native AOT)"));
        try
        {
            if (!await client.ValidateConnectivityAsync()) throw new IOException("Cannot reach " + provider + ". Check the endpoint and key.");
            ConsoleSession session = GetSession(client);
            session.WorkingDirectory = Path.GetFullPath(Option("directory", Directory.GetCurrentDirectory()));
            if (!Directory.Exists(session.WorkingDirectory)) throw new DirectoryNotFoundException(session.WorkingDirectory);
            if (options.TryGetValue("prompt", out string? prompt))
            {
                if (!await RunSharedChatAsync(client, prompt, !Option("streaming", "true").Equals("false", StringComparison.OrdinalIgnoreCase))) return 1;
            }
            else
            {
                await session.SendAsync("In one sentence, what is Native AOT in .NET?", false, false);
                await session.SendAsync("Count from one to five in words.", true, false);
                if (!await RunSharedChatAsync(client, "What files do you see in the current directory?", true)) return 1;
            }
            string path = Option("save", Path.Combine(Path.GetTempPath(), "polyprompt-aot-conversation.json"));
            File.WriteAllText(path, JsonSerializer.Serialize(session.Messages.ToList(), PolyPromptJsonContext.Default.ListChatMessage));
            Console.WriteLine("Saved conversation to " + path);
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("Error: " + ex.Message); return 1; }
        finally { ShutdownSharedConsole(); }
    }
}
