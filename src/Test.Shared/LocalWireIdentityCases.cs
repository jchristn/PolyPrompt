namespace Test.Shared
{
    using System.Reflection;
    using System.Text;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using Touchstone.Core;

    /// <summary>
    /// Wire identity against 4.0.0. Each scenario runs against every client that supports it, and the exact request
    /// bytes, method, path, and Content-Type seen by the HTTP handler are compared with a fixture captured from the
    /// 4.0.0 code (Fixtures/V4Bodies). Set POLYPROMPT_CAPTURE_WIRE_FIXTURES to a directory to write fixtures instead.
    /// </summary>
    internal static class LocalWireIdentityCases
    {
        private const string CaptureVariable = "POLYPROMPT_CAPTURE_WIRE_FIXTURES";
        private const string ResourcePrefix = "V4Bodies/";

        public static IEnumerable<TestCaseDescriptor> Create(string suite)
        {
            yield return new TestCaseDescriptor(
                suite,
                "wire_identity_v4",
                "Text-only chat, tool chat, generate, and embed requests are byte-identical to 4.0.0",
                RunAsync,
                new[] { "local", "wire" });
        }

        private static readonly ToolDefinition _Weather = ToolDefinition.Function(
            "get_weather",
            "Get the weather for a city.",
            new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["city"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "City name" },
                },
                ["required"] = new List<object> { "city" },
            });

        private static List<ChatMessage> History() => new List<ChatMessage>
        {
            ChatMessage.User("What is the weather in Paris?"),
            ChatMessage.AssistantToolCalls(new[] { new ToolCall { Id = "call_1", Name = "get_weather", ArgumentsJson = "{\"city\":\"Paris\"}" } }),
            ChatMessage.ToolResult("call_1", "get_weather", "{\"temperature\":20,\"unit\":\"C\"}"),
            ChatMessage.User("And should I bring a coat? <yes/no> & why + how"),
        };

        private static List<ChatMessage> MultiTurn() => new List<ChatMessage>
        {
            ChatMessage.System("You are terse."),
            ChatMessage.User("Hi"),
            ChatMessage.Assistant("Hello."),
            ChatMessage.User("Say \"again\" in French, ünïcödé ✓"),
        };

        private static CompletionOptions Options() => new CompletionOptions { MaxTokens = 64, Temperature = 0.25, SystemPrompt = "Answer briefly." };

        // Scenario call sites are the only part of this file that changes with the public API. Inputs must not change.
        private static readonly (string Id, Capability Capability, Func<ProviderClientBase, CancellationToken, Task> Run)[] _Scenarios =
        {
            ("chat_basic", Capability.Chat, (c, t) => c.ChatAsync("Hello there", null, t)),
            ("chat_options", Capability.Chat, (c, t) => c.ChatAsync("Hello there", Options(), t)),
            ("chat_stream", Capability.Chat, async (c, t) => await Drain((await c.ChatStreamingAsync("Hello there", null, t).ConfigureAwait(false)).Chunks, t).ConfigureAwait(false)),
            ("chat_stream_options", Capability.Chat, async (c, t) => await Drain((await c.ChatStreamingAsync("Hello there", Options(), t).ConfigureAwait(false)).Chunks, t).ConfigureAwait(false)),
            ("messages_single", Capability.ToolChat, (c, t) => c.ToolChatAsync(new ToolChatRequest { Messages = { ChatMessage.User("Hello there") } }, t)),
            ("messages_multiturn", Capability.ToolChat, (c, t) => c.ToolChatAsync(new ToolChatRequest { Messages = MultiTurn() }, t)),
            ("messages_options", Capability.ToolChat, (c, t) => c.ToolChatAsync(new ToolChatRequest { Messages = { ChatMessage.User("Hello there") }, Options = Options() }, t)),
            ("tools_auto", Capability.ToolChat, (c, t) => c.ToolChatAsync(new ToolChatRequest { Messages = { ChatMessage.User("Weather in Paris?") }, Tools = { _Weather } }, t)),
            ("tools_required", Capability.ToolChat, (c, t) => c.ToolChatAsync(new ToolChatRequest { Messages = { ChatMessage.User("Weather in Paris?") }, Tools = { _Weather }, ToolChoice = "required" }, t)),
            ("tools_named", Capability.ToolChat, (c, t) => c.ToolChatAsync(new ToolChatRequest { Messages = { ChatMessage.User("Weather in Paris?") }, Tools = { _Weather }, ToolChoice = "get_weather" }, t)),
            ("tools_none", Capability.ToolChat, (c, t) => c.ToolChatAsync(new ToolChatRequest { Messages = { ChatMessage.User("Weather in Paris?") }, Tools = { _Weather }, ToolChoice = "none" }, t)),
            ("tools_history", Capability.ToolChat, (c, t) => c.ToolChatAsync(new ToolChatRequest { Messages = History(), Tools = { _Weather }, Options = Options() }, t)),
            ("tools_stream", Capability.ToolChat, async (c, t) => await Drain((await c.ToolChatStreamingAsync(new ToolChatRequest { Messages = History(), Tools = { _Weather } }, t).ConfigureAwait(false)).Chunks, t).ConfigureAwait(false)),
            ("messages_stream", Capability.ToolChat, async (c, t) => await Drain((await c.ToolChatStreamingAsync(new ToolChatRequest { Messages = MultiTurn() }, t).ConfigureAwait(false)).Chunks, t).ConfigureAwait(false)),
            ("generate_basic", Capability.Generate, (c, t) => c.GenerateAsync("Once upon a time", Options(), t)),
            ("embed_batch", Capability.Embed, (c, t) => c.EmbedAsync(new List<string> { "alpha", "beta <&+>" }, null, t)),
        };

        private static async Task Drain<T>(IAsyncEnumerable<T> chunks, CancellationToken token)
        {
            await foreach (T _ in chunks.WithCancellation(token).ConfigureAwait(false)) { }
        }

        private static async Task RunAsync(CancellationToken token)
        {
            string? captureDir = Environment.GetEnvironmentVariable(CaptureVariable);
            if (!string.IsNullOrWhiteSpace(captureDir)) Directory.CreateDirectory(captureDir);

            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            RecordingHandler handler = new RecordingHandler(server.Endpoint);
            using HttpClient http = new HttpClient(handler);

            List<string> failures = new List<string>();
            int compared = 0;

            foreach (var scenario in _Scenarios)
            {
                List<ProviderClientBase> clients = LocalClients.Supporting(scenario.Capability, server.Endpoint, http);
                try
                {
                    foreach (ProviderClientBase client in clients)
                    {
                        string name = client.GetType().Name + "." + scenario.Id + ".txt";
                        handler.Clear();
                        string outcome;
                        try
                        {
                            await scenario.Run(client, token).ConfigureAwait(false);
                            outcome = "ok";
                        }
                        catch (Exception ex)
                        {
                            outcome = ex.GetType().Name;
                        }

                        string actual = "outcome: " + outcome + "\n" + handler.Render();

                        if (!string.IsNullOrWhiteSpace(captureDir))
                        {
                            await File.WriteAllTextAsync(Path.Combine(captureDir, name), actual, new UTF8Encoding(false), token).ConfigureAwait(false);
                            continue;
                        }

                        string? expected = ReadFixture(name);
                        compared++;
                        if (expected == null) failures.Add(name + ": no 4.0.0 fixture");
                        else if (!string.Equals(expected, actual, StringComparison.Ordinal)) failures.Add(name + ": differs from 4.0.0\n--- expected\n" + expected + "\n--- actual\n" + actual);
                    }
                }
                finally { LocalClients.DisposeAll(clients); }
            }

            if (failures.Count > 0) throw new TestFailureException(failures.Count + " of " + compared + " wire bodies differ from 4.0.0:\n" + string.Join("\n\n", failures));
        }

        private static string? ReadFixture(string name)
        {
            using Stream? stream = typeof(LocalWireIdentityCases).Assembly.GetManifestResourceStream(ResourcePrefix + name);
            if (stream == null) return null;
            using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        /// <summary>
        /// Forwards to the real network handler and records every request's method, path, Content-Type, and body bytes.
        /// </summary>
        private sealed class RecordingHandler : DelegatingHandler
        {
            private readonly string _Endpoint;
            private readonly List<string> _Requests = new List<string>();
            private readonly object _Lock = new object();

            public RecordingHandler(string endpoint) : base(new HttpClientHandler())
            {
                _Endpoint = endpoint.TrimEnd('/');
            }

            public void Clear()
            {
                lock (_Lock) _Requests.Clear();
            }

            public string Render()
            {
                lock (_Lock) return string.Join("\n", _Requests);
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                string url = request.RequestUri?.ToString() ?? string.Empty;
                if (url.StartsWith(_Endpoint, StringComparison.OrdinalIgnoreCase)) url = "{endpoint}" + url.Substring(_Endpoint.Length);
                string contentType = request.Content?.Headers.ContentType?.ToString() ?? "(none)";
                string body = request.Content == null ? string.Empty : Encoding.UTF8.GetString(await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));
                lock (_Lock) _Requests.Add(request.Method + " " + url + "\ncontent-type: " + contentType + "\n" + body + "\n");
                return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
