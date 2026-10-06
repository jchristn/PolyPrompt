namespace AotConsole
{
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using PolyPrompt.Clients;
    using PolyPrompt.Helpers;
    using PolyPrompt.Models;

    /// <summary>
    /// Sample: PolyPrompt in a Native AOT application. Runs a chat, a streaming chat, and a tool-calling loop with typed
    /// tool arguments, then saves the conversation with <see cref="PolyPromptJsonContext"/>. Nothing here uses
    /// reflection: your own types go in a source-generated <see cref="JsonSerializerContext"/> (see
    /// <see cref="AppJsonContext"/>) that is registered once with <see cref="Serializer.AddTypeInfoResolver"/>.
    /// </summary>
    public static class Program
    {
        #region Entrypoint

        public static async Task<int> Main(string[] args)
        {
            Dictionary<string, string> options = ParseArgs(args);
            string provider = Option(options, "provider", "ollama").ToLowerInvariant();
            string? apiKey = OptionalOption(options, "key") ?? Environment.GetEnvironmentVariable(provider.ToUpperInvariant() + "_API_KEY");

            using CompletionClientBase? client = CreateClient(provider, OptionalOption(options, "endpoint"), apiKey);
            if (client == null)
            {
                Console.WriteLine("Usage: AotConsole --provider ollama|openai|anthropic|gemini [--endpoint URL] [--model NAME] [--key KEY] [--save PATH]");
                Console.WriteLine("The key may also come from <PROVIDER>_API_KEY, for example OPENAI_API_KEY.");
                return 2;
            }

            client.Model = Option(options, "model", DefaultModel(provider));
            client.TimeoutMs = 120000;

            // Register your source-generated metadata once, before the first call. It covers the typed tool arguments
            // used by DeserializeArguments<T>() below and anything else of yours that PolyPrompt serializes.
            Serializer.AddTypeInfoResolver(AppJsonContext.Default);

            Console.WriteLine("PolyPrompt " + typeof(ClientBase).Assembly.GetName().Version + " on " + provider + " (" + client.Model + ")");
            Console.WriteLine("Reflection-based JSON: " + (Serializer.IsReflectionEnabled ? "enabled" : "disabled (Native AOT)"));
            Console.WriteLine();

            if (!await client.ValidateConnectivityAsync().ConfigureAwait(false))
            {
                Console.WriteLine("Cannot reach " + provider + ". Check the endpoint and key.");
                return 1;
            }

            await ChatAsync(client).ConfigureAwait(false);
            await StreamAsync(client).ConfigureAwait(false);
            List<ChatMessage> conversation = await ToolLoopAsync(client).ConfigureAwait(false);

            string savePath = Option(options, "save", Path.Combine(Path.GetTempPath(), "polyprompt-aot-conversation.json"));
            SaveConversation(conversation, savePath);
            return 0;
        }

        #endregion

        #region Steps

        private static async Task ChatAsync(CompletionClientBase client)
        {
            Console.WriteLine("== Chat");
            ChatResponse response = await client.ChatAsync("In one sentence, what is Native AOT in .NET?").ConfigureAwait(false);
            Console.WriteLine(response.Success ? response.Text : "Error: " + response.Error);
            Console.WriteLine();
        }

        private static async Task StreamAsync(CompletionClientBase client)
        {
            Console.WriteLine("== Streaming chat");
            ChatStreamingResponse response = await client.ChatStreamingAsync("Count from one to five in words.").ConfigureAwait(false);
            await foreach (ChatStreamingChunk chunk in response.Chunks.ConfigureAwait(false)) Console.Write(chunk.Text);
            Console.WriteLine();
            if (!response.Success) Console.WriteLine("Error: " + response.Error);
            Console.WriteLine();
        }

        private static async Task<List<ChatMessage>> ToolLoopAsync(CompletionClientBase client)
        {
            Console.WriteLine("== Tool calling");

            ToolChatRequest request = new ToolChatRequest
            {
                Messages = new List<ChatMessage> { ChatMessage.User("What is the weather in Paris and in Tokyo, in celsius?") },
                Tools = new List<ToolDefinition> { WeatherTool() }
            };

            for (int round = 0; round < 4; round++)
            {
                ToolChatResponse response = await client.ToolChatAsync(request).ConfigureAwait(false);
                if (!response.Success)
                {
                    Console.WriteLine("Error: " + response.Error);
                    break;
                }

                if (response.ToolCalls.Count == 0)
                {
                    Console.WriteLine(response.Text);
                    request.Messages.Add(ChatMessage.Assistant(response.Text ?? string.Empty));
                    break;
                }

                request.Messages.Add(ChatMessage.AssistantToolCalls(response.ToolCalls));
                foreach (ToolCall call in response.ToolCalls)
                {
                    // Typed arguments without reflection: WeatherArgs is in AppJsonContext, registered in Main. Passing
                    // the metadata explicitly also works: call.DeserializeArguments(AppJsonContext.Default.WeatherArgs).
                    WeatherArgs args = call.DeserializeArguments<WeatherArgs>() ?? new WeatherArgs();
                    WeatherReport report = LookUpWeather(args);
                    string result = JsonSerializer.Serialize(report, AppJsonContext.Default.WeatherReport);

                    Console.WriteLine("  " + call.Name + "(" + call.ArgumentsJson + ") -> " + result);
                    request.Messages.Add(ChatMessage.ToolResult(call.Id, call.Name, result));
                }
            }

            Console.WriteLine();
            return request.Messages;
        }

        private static void SaveConversation(List<ChatMessage> conversation, string path)
        {
            // PolyPromptJsonContext covers the public models, so conversations, including tool calls and their thought
            // signatures, can be saved and restored without reflection.
            string json = JsonSerializer.Serialize(conversation, PolyPromptJsonContext.Default.ListChatMessage);
            File.WriteAllText(path, json);

            List<ChatMessage>? restored = JsonSerializer.Deserialize(File.ReadAllText(path), PolyPromptJsonContext.Default.ListChatMessage);
            Console.WriteLine("== Saved " + conversation.Count + " messages to " + path + " and restored " + (restored?.Count ?? 0) + ".");
        }

        #endregion

        #region Helpers

        private static ToolDefinition WeatherTool()
        {
            return ToolDefinition.Function("get_weather", "Get the current weather for a city.", new Dictionary<string, object>
            {
                { "type", "object" },
                { "properties", new Dictionary<string, object>
                    {
                        { "city", new Dictionary<string, object> { { "type", "string" }, { "description", "City name" } } },
                        { "unit", new Dictionary<string, object> { { "type", "string" }, { "enum", new[] { "celsius", "fahrenheit" } } } },
                    }
                },
                { "required", new[] { "city" } },
            });
        }

        private static WeatherReport LookUpWeather(WeatherArgs args)
        {
            // A stand-in for a real weather service.
            int celsius = args.City.Sum(c => (int)c) % 30;
            bool fahrenheit = string.Equals(args.Unit, "fahrenheit", StringComparison.OrdinalIgnoreCase);
            return new WeatherReport
            {
                City = args.City,
                Temperature = fahrenheit ? celsius * 9 / 5 + 32 : celsius,
                Unit = fahrenheit ? "fahrenheit" : "celsius",
                Conditions = celsius > 15 ? "sunny" : "cloudy"
            };
        }

        private static CompletionClientBase? CreateClient(string provider, string? endpoint, string? apiKey)
        {
            // With no endpoint, each client uses its provider's default.
            switch (provider)
            {
                case "ollama": return endpoint == null ? new OllamaCompletionClient(apiKey: apiKey) : new OllamaCompletionClient(endpoint, apiKey);
                case "openai": return endpoint == null ? new OpenAiCompletionClient(apiKey: apiKey) : new OpenAiCompletionClient(endpoint, apiKey);
                case "anthropic": return endpoint == null ? new AnthropicCompletionClient(apiKey: apiKey) : new AnthropicCompletionClient(endpoint, apiKey);
                case "gemini": return endpoint == null ? new GeminiCompletionClient(apiKey: apiKey) : new GeminiCompletionClient(endpoint, apiKey);
                default: return null;
            }
        }

        private static string DefaultModel(string provider)
        {
            switch (provider)
            {
                case "openai": return "gpt-4o-mini";
                case "anthropic": return "claude-haiku-4-5-20251001";
                case "gemini": return "gemini-2.5-flash";
                default: return "qwen3:4b";
            }
        }

        private static Dictionary<string, string> ParseArgs(string[] args)
        {
            Dictionary<string, string> options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].StartsWith("--", StringComparison.Ordinal)) options[args[i].Substring(2)] = args[++i];
            }

            return options;
        }

        private static string Option(Dictionary<string, string> options, string name, string defaultValue)
        {
            return options.TryGetValue(name, out string? value) ? value : defaultValue;
        }

        private static string? OptionalOption(Dictionary<string, string> options, string name)
        {
            return options.TryGetValue(name, out string? value) ? value : null;
        }

        #endregion
    }

    /// <summary>
    /// Arguments the model sends to get_weather.
    /// </summary>
    public sealed class WeatherArgs
    {
        [JsonPropertyName("city")]
        public string City { get; set; } = string.Empty;

        [JsonPropertyName("unit")]
        public string? Unit { get; set; }
    }

    /// <summary>
    /// The tool result returned to the model.
    /// </summary>
    public sealed class WeatherReport
    {
        [JsonPropertyName("city")]
        public string City { get; set; } = string.Empty;

        [JsonPropertyName("temperature")]
        public int Temperature { get; set; }

        [JsonPropertyName("unit")]
        public string Unit { get; set; } = "celsius";

        [JsonPropertyName("conditions")]
        public string Conditions { get; set; } = string.Empty;
    }

    /// <summary>
    /// Source-generated JSON metadata for this application's own types.
    /// </summary>
    [JsonSerializable(typeof(WeatherArgs))]
    [JsonSerializable(typeof(WeatherReport))]
    internal sealed partial class AppJsonContext : JsonSerializerContext
    {
    }
}
