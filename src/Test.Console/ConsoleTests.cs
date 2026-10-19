namespace Test.Console;

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using PolyPrompt.Clients;
using PolyPrompt.ConsoleSupport;
using PolyPrompt.Models;

[TestFixture]
public sealed class ConsoleTests
{
    private string _Directory = null!;
    [SetUp] public void Setup() { _Directory = Path.Combine(Path.GetTempPath(), "polyprompt-console-" + Guid.NewGuid()); Directory.CreateDirectory(_Directory); }
    [TearDown] public void Cleanup() => Directory.Delete(_Directory, true);

    [Test]
    public async Task MuxToolsExecuteRealFileOperationsAndPreserveTaskState()
    {
        using ProcessToolRuntime runtime = new();
        var tools = await runtime.GetToolsAsync();
        Assert.That(tools.Select(t => t.Name), Is.SupersetOf(new[] { "read_file", "write_file", "edit_file", "multi_edit", "delete_file", "file_metadata", "list_directory", "manage_directory", "glob", "grep", "run_process", "web_retrieve", "plan_tasks", "update_task" }));
        async Task<string> Run(string name, object args)
        {
            string result = await runtime.ExecuteAsync(new ToolCall { Name = name, Id = "test-id", ArgumentsJson = JsonSerializer.Serialize(args) }, _Directory);
            Assert.That(result, Does.Not.Contain("Tool error:"), result);
            return result;
        }
        await Run("write_file", new { file_path = "sample.txt", content = "alpha\nbeta\n" });
        Assert.That(File.ReadAllText(Path.Combine(_Directory, "sample.txt")), Is.EqualTo("alpha\nbeta\n"));
        Assert.That(await Run("read_file", new { file_path = "sample.txt" }), Does.Contain("alpha"));
        await Run("edit_file", new { file_path = "sample.txt", old_string = "alpha", new_string = "gamma" });
        await Run("multi_edit", new { file_path = "sample.txt", edits = new[] { new { old_string = "gamma", new_string = "delta" }, new { old_string = "beta", new_string = "omega" } } });
        Assert.That(File.ReadAllText(Path.Combine(_Directory, "sample.txt")), Does.Contain("delta\nomega"));
        Assert.That(await Run("file_metadata", new { path = "sample.txt" }), Does.Contain("sample.txt"));
        Assert.That(await Run("list_directory", new { path = "." }), Does.Contain("sample.txt"));
        Assert.That(await Run("glob", new { pattern = "*.txt" }), Does.Contain("sample.txt"));
        Assert.That(await Run("grep", new { pattern = "omega" }), Does.Contain("omega"));
        Assert.That(await Run("run_process", new { command = "echo console-tool-ok" }), Does.Contain("console-tool-ok"));
        await Run("manage_directory", new { action = "create", path = "child" });
        Assert.That(Directory.Exists(Path.Combine(_Directory, "child")), Is.True);
        await Run("plan_tasks", new { tasks = new[] { new { id = "t1", title = "Test task" } } });
        Assert.That(await Run("update_task", new { id = "t1", status = "completed" }), Does.Contain("t1"));
        await Run("delete_file", new { file_path = "sample.txt" });
        Assert.That(File.Exists(Path.Combine(_Directory, "sample.txt")), Is.False);
    }

    [Test]
    public async Task ConfiguredOptionalMuxToolsAreAdvertised()
    {
        string settings = Path.Combine(_Directory, "settings.json");
        string agents = Path.Combine(_Directory, "subagents.json");
        string endpoints = Path.Combine(_Directory, "endpoints.json");
        File.WriteAllText(settings, "{\"externalSearch\":{\"enabled\":true,\"providers\":[{\"name\":\"fixture\",\"providerType\":\"tavily\",\"endpoint\":\"http://127.0.0.1:1\",\"apiKey\":\"test-key\",\"isDefault\":true}]}}");
        File.WriteAllText(agents, "[{\"name\":\"reader\",\"systemPrompt\":\"Read files.\"}]");
        File.WriteAllText(endpoints, "[{\"name\":\"local\",\"adapterType\":\"ollama\",\"baseUrl\":\"http://127.0.0.1:1\",\"model\":\"test-model\"}]");
        string[] names = { "POLYPROMPT_MUX_SETTINGS", "POLYPROMPT_MUX_SUBAGENTS", "POLYPROMPT_MUX_ENDPOINTS" };
        string?[] previous = names.Select(Environment.GetEnvironmentVariable).ToArray();
        try
        {
            Environment.SetEnvironmentVariable(names[0], settings);
            Environment.SetEnvironmentVariable(names[1], agents);
            Environment.SetEnvironmentVariable(names[2], endpoints);
            using ProcessToolRuntime runtime = new();
            Assert.That((await runtime.GetToolsAsync()).Select(t => t.Name), Is.SupersetOf(new[] { "web_search", "spawn_subagent" }));
        }
        finally { for (int i = 0; i < names.Length; i++) Environment.SetEnvironmentVariable(names[i], previous[i]); }
    }

    [Test]
    public async Task BrowserToolRetrievesActualLocalPage()
    {
        using ModelServer server = new();
        using ProcessToolRuntime runtime = new();
        string result = await runtime.ExecuteAsync(new ToolCall { Name = "web_retrieve", Id = "browser", ArgumentsJson = JsonSerializer.Serialize(new { url = server.Endpoint + "/tool-page", wait_until = "domcontentloaded" }) }, _Directory);
        Assert.That(result, Does.Contain("browser-tool-evidence"), result);
    }

    [Test]
    public async Task MalformedToolArgumentsBecomeResultsAndReasoningOnlyOutputStaysSeparate()
    {
        using ModelServer server = new("malformed");
        using OllamaCompletionClient client = new(server.Endpoint);
        using StringWriter output = new();
        using ConsoleSession session = new(client, new ProcessToolRuntime(), output) { WorkingDirectory = _Directory };
        await session.SendAsync("Try an invalid tool call", false);
        Assert.That(server.Bodies[1], Does.Contain("path"));
        Assert.That(output.ToString(), Does.Contain("--- Thinking ---"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ModelCallsDirectoryToolAndReceivesRealResult(bool streaming)
    {
        File.WriteAllText(Path.Combine(_Directory, "visible-to-model.txt"), "fixture");
        using ModelServer server = new();
        using OllamaCompletionClient client = new(server.Endpoint, "test-token");
        using StringWriter output = new();
        using ConsoleSession session = new(client, new ProcessToolRuntime(), output) { WorkingDirectory = _Directory };
        await session.SendAsync("List this directory", streaming);
        Assert.That(server.Bodies.Count, Is.EqualTo(2));
        Assert.That(server.Bodies[0], Does.Contain("list_directory").And.Contain(_Directory));
        Assert.That(server.Bodies[1], Does.Contain("visible-to-model.txt"));
        Assert.That(output.ToString(), Does.Contain("--- Thinking ---").And.Contain("--- Answer ---").And.Contain("--- Tool: list_directory ---").And.Contain("Directory inspected."));
        Assert.That(session.Messages.Select(m => m.Role), Is.EqualTo(new[] { "user", "assistant", "tool", "assistant" }));
        Assert.That(output.ToString(), Does.Contain("Output tokens: 270" + Environment.NewLine + Environment.NewLine));
        Assert.That(session.Messages[2].ToolName, Is.EqualTo("list_directory"));
        Assert.That(session.Messages[1].Content, Is.EqualTo("I will inspect it."));
        await session.SendAsync("Now acknowledge", streaming, false);
        Assert.That(server.Bodies[2], Does.Contain("visible-to-model.txt"));
        session.Reset();
        Assert.That(session.Messages, Is.Empty);
    }

    [Test]
    public async Task ToolFailuresReturnToModelWithoutRunningUnknownTools()
    {
        using ModelServer server = new("unknown");
        using OllamaCompletionClient client = new(server.Endpoint);
        using StringWriter output = new();
        using ConsoleSession session = new(client, new ProcessToolRuntime(), output);
        await session.SendAsync("Try unknown tool", false);
        Assert.That(server.Bodies[1], Does.Contain("unknown tool"));
    }

    [Test]
    public async Task ToolsOffOmitsDefinitionsAndNeverExecutes()
    {
        using ModelServer server = new("plain");
        using OllamaCompletionClient client = new(server.Endpoint);
        using ConsoleSession session = new(client, new ThrowingTools(), TextWriter.Null);
        await session.SendAsync("Hello", false, false);
        Assert.That(server.Bodies[0], Does.Not.Contain("\"tools\""));
    }

    [Test]
    public async Task ToolRoundLimitStopsLoopAndRestoresHistory()
    {
        using ModelServer server = new("loop");
        using OllamaCompletionClient client = new(server.Endpoint);
        using ConsoleSession session = new(client, new ProcessToolRuntime(), TextWriter.Null) { MaxRounds = 2, WorkingDirectory = _Directory };
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SendAsync("Loop", false));
        Assert.That(server.Bodies.Count, Is.EqualTo(2));
        Assert.That(session.Messages, Is.Empty);
    }

    [Test]
    public async Task CancellationKillsToolProcessAndRuntimeCanRestart()
    {
        using ProcessToolRuntime runtime = new();
        await runtime.GetToolsAsync();
        using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAsync<OperationCanceledException>(() => runtime.ExecuteAsync(new ToolCall { Name = "run_process", Id = "cancel", ArgumentsJson = "{\"command\":\"sleep 30\"}" }, _Directory, cancellation.Token));
        Assert.That(await runtime.GetToolsAsync(), Is.Not.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task NativeGenerationObserverKeepsThinkingSeparateAndPreservesAnswer(bool streaming)
    {
        using ModelServer server = new();
        using HttpClient http = new(new GenerationThinkingHandler());
        using OllamaCompletionClient client = new(server.Endpoint, httpClient: http);
        using StringWriter output = new();
        await GenerationHarness.RunAsync(client, streaming, output);
        Assert.That(output.ToString(), Does.Contain("--- Thinking ---").And.Contain("generation thought").And.Contain("--- Answer ---").And.Contain("generated answer"));
    }

    [TestCase("anthropic", false)]
    [TestCase("anthropic", true)]
    [TestCase("gemini", false)]
    [TestCase("gemini", true)]
    [TestCase("cohere", false)]
    [TestCase("cohere", true)]
    public async Task NativeGenerationReasoningFormatsRenderSeparately(string provider, bool streaming)
    {
        using ModelServer server = new("generation-" + provider);
        using HttpClient http = new(new GenerationThinkingHandler());
        using CompletionClientBase client = provider switch
        {
            "anthropic" => new AnthropicCompletionClient(server.Endpoint, "test-key", httpClient: http),
            "gemini" => new GeminiCompletionClient(server.Endpoint, "test-key", httpClient: http),
            _ => new CohereCompletionClient(server.Endpoint, "test-key", httpClient: http)
        };
        client.Model = "test-model";
        using StringWriter output = new();
        await GenerationHarness.RunAsync(client, streaming, output);
        Assert.That(output.ToString(), Does.Contain("generation thought").And.Contain("generated answer").And.Contain("--- Thinking ---").And.Contain("--- Answer ---").And.Not.Contain("Error:"));
        Assert.That(output.ToString().Split("generation thought").Length, Is.EqualTo(2), "Thinking should appear exactly once.");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task PostmanSimulationRunsMultipleRealToolRounds(bool streaming)
    {
        string? endpoint = Environment.GetEnvironmentVariable("POLYPROMPT_CONSOLE_SIMULATION");
        if (string.IsNullOrEmpty(endpoint)) { Assert.Ignore("Set POLYPROMPT_CONSOLE_SIMULATION to run the saved Postman simulation."); return; }
        File.WriteAllText(Path.Combine(_Directory, ".console-smoke.txt"), "real-file-evidence");
        using OllamaCompletionClient client = new(endpoint);
        using StringWriter output = new();
        using ConsoleSession session = new(client, new ProcessToolRuntime(), output) { WorkingDirectory = _Directory };
        await session.SendAsync("console-smoke: inspect directory and read the fixture", streaming);
        Assert.That(session.Messages.Count, Is.EqualTo(6));
        Assert.That(output.ToString(), Does.Contain("real-file-evidence").And.Contain("Inspected file:").And.Contain("--- Thinking ---").And.Contain("--- Answer ---"));
    }

    [Test]
    public void RendererHandlesReasoningOnlyAndInterleavedDeltasWithoutDuplicates()
    {
        using StringWriter output = new();
        ConsoleRenderer renderer = new(output);
        renderer.Thinking("one"); renderer.Thinking("two"); renderer.Answer("three"); renderer.Thinking("four"); renderer.Finish();
        Assert.That(output.ToString(), Is.EqualTo("--- Thinking ---" + Environment.NewLine + "onetwo" + Environment.NewLine + "--- Answer ---" + Environment.NewLine + "three" + Environment.NewLine + "--- Thinking ---" + Environment.NewLine + "four" + Environment.NewLine));
    }

    // Exercise the exact generation command path through the console base rather than duplicating its observer setup.
    private sealed class GenerationHarness : ConsoleApplicationBase
    {
        public static async Task RunAsync(CompletionClientBase client, bool streaming, TextWriter output)
        {
            TextWriter previous = System.Console.Out;
            System.Console.SetOut(output);
            try { await RunSharedGenerationAsync(client, "hello", streaming); }
            finally { System.Console.SetOut(previous); }
        }
    }

    private sealed class ThrowingTools : IConsoleToolRuntime
    {
        public Task<IReadOnlyList<ToolDefinition>> GetToolsAsync(CancellationToken token = default) => throw new Exception("Tools must not be accessed.");
        public Task<string> ExecuteAsync(ToolCall call, string directory, CancellationToken token = default) => throw new Exception("Tools must not execute.");
        public void Dispose() { }
    }

    private sealed class ModelServer : IDisposable
    {
        private readonly HttpListener _Listener = new();
        private readonly Task _Serving;
        public string Endpoint { get; }
        public List<string> Bodies { get; } = new();
        public ModelServer(string mode = "normal")
        {
            using TcpListener port = new(IPAddress.Loopback, 0); port.Start();
            int number = ((IPEndPoint)port.LocalEndpoint).Port; port.Stop();
            Endpoint = "http://127.0.0.1:" + number;
            _Listener.Prefixes.Add(Endpoint + "/"); _Listener.Start();
            _Serving = Task.Run(async () =>
            {
                try
                {
                    while (_Listener.IsListening)
                    {
                        HttpListenerContext context = await _Listener.GetContextAsync();
                        if (context.Request.Url!.AbsolutePath == "/tool-page")
                        {
                            context.Response.ContentType = "text/html";
                            await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("<html><body>browser-tool-evidence</body></html>"));
                            context.Response.Close();
                            continue;
                        }
                        string body = await new StreamReader(context.Request.InputStream).ReadToEndAsync();
                        Bodies.Add(body);
                        using JsonDocument request = JsonDocument.Parse(body);
                        bool streaming = request.RootElement.TryGetProperty("stream", out var stream) && stream.GetBoolean();
                        if (mode.StartsWith("generation-", StringComparison.Ordinal))
                        {
                            streaming |= context.Request.Url!.AbsolutePath.Contains("streamGenerateContent", StringComparison.Ordinal);
                            string payload = GenerationPayload(mode[11..], streaming);
                            context.Response.ContentType = streaming ? "text/event-stream" : "application/json";
                            await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(payload));
                            context.Response.Close();
                            continue;
                        }
                        bool generation = context.Request.Url!.AbsolutePath == "/api/generate";
                        bool final = mode == "plain" || (Bodies.Count > 1 && mode != "loop");
                        string name = mode == "unknown" ? "not_a_real_tool" : "list_directory";
                        object message = final
                            ? new { role = "assistant", content = "Directory inspected.", thinking = "Finished reasoning.", tool_calls = Array.Empty<object>() }
                            : new { role = "assistant", content = "I will inspect it.", thinking = "Need directory evidence.", tool_calls = new object[] { new { function = new { name, arguments = mode == "malformed" ? new Dictionary<string, string>() : new Dictionary<string, string> { { "path", "." } } } } } };
                        string json = generation
                            ? JsonSerializer.Serialize(new { model = "test-model", thinking = "generation thought", response = "generated answer", done = true })
                            : JsonSerializer.Serialize(new { model = "test-model", message, done = true, prompt_eval_count = 2775, eval_count = 270 });
                        context.Response.ContentType = streaming ? "application/x-ndjson" : "application/json";
                        byte[] bytes = Encoding.UTF8.GetBytes(json + (streaming ? "\n" : ""));
                        await context.Response.OutputStream.WriteAsync(bytes); context.Response.Close();
                    }
                }
                catch (HttpListenerException) { }
                catch (ObjectDisposedException) { }
            });
        }
        private static string GenerationPayload(string provider, bool streaming)
        {
            if (provider == "gemini")
            {
                const string payload = """{"candidates":[{"content":{"parts":[{"thought":true,"text":"generation thought"},{"text":"generated answer"}]},"finishReason":"STOP"}]}""";
                return streaming ? "data: " + payload + "\n\n" : payload;
            }
            if (provider == "anthropic")
            {
                if (!streaming) return """{"id":"test","model":"test-model","content":[{"type":"thinking","thinking":"generation thought"},{"type":"text","text":"generated answer"}],"stop_reason":"end_turn"}""";
                return "data: " + """{"type":"content_block_delta","delta":{"type":"thinking_delta","thinking":"generation thought"}}""" + "\n\n"
                    + "data: " + """{"type":"content_block_delta","delta":{"type":"text_delta","text":"generated answer"}}""" + "\n\n"
                    + "data: " + """{"type":"message_stop"}""" + "\n\n";
            }
            if (!streaming) return """{"id":"test","message":{"content":[{"type":"thinking","thinking":"generation thought"},{"type":"text","text":"generated answer"}]},"finish_reason":"COMPLETE"}""";
            return "data: " + """{"type":"content-delta","delta":{"message":{"content":{"thinking":"generation thought"}}}}""" + "\n\n"
                + "data: " + """{"type":"content-delta","delta":{"message":{"content":{"text":"generated answer"}}}}""" + "\n\n"
                + "data: " + """{"type":"message-end"}""" + "\n\n";
        }

        public void Dispose() { _Listener.Close(); _Serving.GetAwaiter().GetResult(); }
    }
}
