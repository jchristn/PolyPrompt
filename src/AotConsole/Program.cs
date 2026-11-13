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
            System.Console.WriteLine("Usage: AotConsole --provider ollama|ollamacloud|openai|openai-compatible|anthropic|gemini|xai [--endpoint URL] [--model NAME] [--key KEY] [--prompt TEXT] [--streaming true|false] [--directory PATH] [--save PATH]");
            return 0;
        }
        string provider = Option("provider", "ollama").ToLowerInvariant();
        string? key = options.GetValueOrDefault("key") ?? Environment.GetEnvironmentVariable(provider == "ollamacloud" ? "OLLAMA_API_KEY" : provider.ToUpperInvariant() + "_API_KEY");
        string? endpoint = options.GetValueOrDefault("endpoint");
        if ((provider is "xai" or "ollamacloud" or "openai" or "anthropic" or "gemini") && string.IsNullOrWhiteSpace(key))
        {
            Console.WriteLine("Pass --key (or set the provider's API key environment variable).");
            return 2;
        }
        using ProviderClientBase? client = provider switch
        {
            "xai" => new XAiClient(key!, endpoint ?? "https://api.x.ai", new ClientOptions { HttpClient = CreateConsoleHttpClient() }),
            "ollama" => new OllamaClient(endpoint ?? "http://localhost:11434", key, new ClientOptions { HttpClient = CreateConsoleHttpClient() }),
            "ollamacloud" => new OllamaCloudClient(key!, options: new ClientOptions { HttpClient = CreateConsoleHttpClient() }),
            "openai" => new OpenAiClient(key!, endpoint, new ClientOptions { HttpClient = CreateConsoleHttpClient() }),
            "openai-compatible" => new OpenAiCompatibleClient(endpoint ?? "http://localhost:8000/v1", key, new ClientOptions { HttpClient = CreateConsoleHttpClient() }),
            "anthropic" => new AnthropicClient(key!, endpoint, new ClientOptions { HttpClient = CreateConsoleHttpClient() }),
            "gemini" => new GeminiClient(key!, endpoint, new ClientOptions { HttpClient = CreateConsoleHttpClient() }),
            _ => null
        };
        if (client == null)
        {
            Console.WriteLine("Usage: AotConsole --provider ollama|ollamacloud|openai|openai-compatible|anthropic|gemini|xai [--endpoint URL] [--model NAME] [--key KEY] [--prompt TEXT] [--streaming true|false] [--directory PATH] [--save PATH]");
            return 2;
        }
        client.Defaults.Completion!.Model = Option("model", provider switch
        {
            "xai" => "grok-4.7", "openai" => "gpt-6.1-sol", "openai-compatible" => "qwen3:4b", "anthropic" => "claude-haiku-5-5", "gemini" => "gemini-3.8-flash",
            "ollamacloud" => "gpt-oss:20b", _ => "qwen3:4b"
        });
        client.TimeoutMs = 120000;
        Console.WriteLine("PolyPrompt " + typeof(ClientBase).Assembly.GetName().Version + " on " + provider + " (" + client.Defaults.Completion.Model + ")");
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
