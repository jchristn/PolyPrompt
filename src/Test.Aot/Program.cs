namespace Test.Aot
{
    using System.Diagnostics;
    using System.Runtime.CompilerServices;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using PolyPrompt.Clients;
    using PolyPrompt.Helpers;
    using PolyPrompt.Models;
    using Test.Shared;

    /// <summary>
    /// Native AOT verification for PolyPrompt. Runs every client's operations against the local mock server, plus
    /// serializer checks, and prints a digest of every request body sent. verify-aot.sh runs it with reflection
    /// (<c>dotnet run -p:PublishAot=false</c>) and as a native binary (<c>dotnet publish -r &lt;rid&gt;</c>): both must
    /// pass and their digests must match, which shows the native build sends byte-identical requests.
    /// </summary>
    public static class Program
    {
        #region Private-Members

        private const string Model = "test-model";
        private const string RerankModel = "test-rerank-model";
        private static int _Passed = 0;
        private static readonly List<string> _ContinuationBodies = new();
        private static readonly List<string> _Failures = new List<string>();
        private static readonly List<string> _AgentInvocations = new List<string>();

        #endregion

        #region Entrypoint

        public static async Task<int> Main(string[] args)
        {
            string? digestPath = null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--digest-out") digestPath = args[i + 1];
            }

            // A native binary has no dynamic code and no reflection-based JSON. "dotnet run" applies the same feature
            // switches because the project sets PublishAot; "dotnet run -p:PublishAot=false" runs with reflection.
            bool reflection = Serializer.IsReflectionEnabled;
            Console.WriteLine("PolyPrompt AOT verification");
            Console.WriteLine("  Framework:          " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
            Console.WriteLine("  Dynamic code:       " + (RuntimeFeature.IsDynamicCodeSupported ? "supported" : "not supported"));
            Console.WriteLine("  Reflection JSON:    " + (reflection ? "enabled" : "disabled"));
            Console.WriteLine();

            RunSerializerChecks(reflection);

            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            await RunClientChecksAsync(server).ConfigureAwait(false);
            await RunXAiAdvancedChecksAsync(server).ConfigureAwait(false);
            await RunReleaseThreeSevenChecksAsync(server).ConfigureAwait(false);
            foreach (string provider in new[] { "ollama", "openai", "anthropic", "bedrock", "gemini", "cohere", "ollamacloud", "vertex" })
                foreach (bool streaming in new[] { false, true })
                {
                    try { await LocalToolTurnCases.RunAsync(provider, streaming, CancellationToken.None, _ContinuationBodies); _Passed++; }
                    catch (Exception ex) { _Failures.Add("Continuation " + provider + " stream=" + streaming + ": " + ex.Message); }
                }

            await RunAgentHarnessChecksAsync().ConfigureAwait(false);

            string digest = Digest(server);
            Console.WriteLine();
            Console.WriteLine("Requests sent:  " + (server.RequestBodies.Count + _ContinuationBodies.Count) + " HTTP, " + _AgentInvocations.Count + " harness processes");
            Console.WriteLine("Request digest: " + digest);
            if (digestPath != null) File.WriteAllText(digestPath, digest);

            Console.WriteLine("Passed: " + _Passed + "  Failed: " + _Failures.Count);
            foreach (string failure in _Failures) Console.WriteLine("  FAIL " + failure);
            return _Failures.Count == 0 ? 0 : 1;
        }

        #endregion

        #region Agent-Harness-Checks

        /// <summary>
        /// Every agent harness client against the fake CLI (a POSIX shell shim placed first on the client's PATH): a run,
        /// a streamed run, connectivity, and model listing where the client supports it. The arguments, standard input,
        /// final text, and event kinds of every run go into the request digest, so the JIT and native builds must spawn
        /// byte-identical processes and parse them identically.
        /// </summary>
        private static async Task RunAgentHarnessChecksAsync()
        {
            if (OperatingSystem.IsWindows())
            {
                _Failures.Add("Agent harness checks need a POSIX shell for the fake CLI.");
                return;
            }

            List<(string Name, string Executable, string Scenario, Func<CliAgentClientBase> Create)> harnesses = new()
            {
                ("codex", "codex", "codex-happy", () => new CodexClient()),
                ("claudecode", "claude", "claudecode-happy", () => new ClaudeCodeClient()),
                ("geminicli", "gemini", "geminicli-happy", () => new GeminiCliClient()),
                ("cursor", "cursor-agent", "cursor-happy", () => new CursorClient()),
                ("mux", "mux", "mux-happy", () => new MuxClient()),
                ("pi", "pi", "pi-happy", () => new PiClient()),
                ("opencode", "opencode", "opencode-happy", () => new OpenCodeClient()),
            };

            foreach ((string name, string executable, string scenario, Func<CliAgentClientBase> create) in harnesses)
            {
                await CheckAsync("Harness " + name + ": run, stream, connectivity, models", async () =>
                {
                    using FakeCli fake = FakeCli.Create(executable, scenario);
                    using CliAgentClientBase client = fake.Attach(create());
                    client.TimeoutMs = 30000;

                    CliAgentResult result = await client.RunAsync(new CliAgentRequest("AOT harness check for " + name)).ConfigureAwait(false);
                    if (!result.Success) throw new InvalidOperationException("Run failed (" + result.FailureKind + "): " + result.Error);
                    if (string.IsNullOrEmpty(result.Text)) throw new InvalidOperationException("Run returned no text.");
                    RecordInvocation(name + " run", fake, result.Text, result.Events);

                    string json = JsonSerializer.Serialize(result, PolyPromptJsonContext.Default.CliAgentResult);
                    CliAgentResult back = JsonSerializer.Deserialize(json, PolyPromptJsonContext.Default.CliAgentResult)!;
                    Expect(result.Text, back.Text, name + " result JSON round trip");
                    Expect(result.Events.Count, back.Events.Count, name + " event JSON round trip");

                    List<CliAgentEvent> streamed = new List<CliAgentEvent>();
                    await foreach (CliAgentEvent evt in client.RunStreamingAsync(new CliAgentRequest("AOT streaming check for " + name)).ConfigureAwait(false)) streamed.Add(evt);
                    Expect(CliAgentEventKind.Completed, streamed[streamed.Count - 1].Kind, name + " stream terminal event");
                    RecordInvocation(name + " stream", fake, streamed[streamed.Count - 1].Text ?? "", streamed);

                    CliConnectivityResult connectivity = await client.ValidateConnectivityAsync().ConfigureAwait(false);
                    Expect(CliConnectivityStatus.Ok, connectivity.Status, name + " connectivity (" + connectivity.Message + ")");

                    List<string> models = new List<string>();
                    IAsyncEnumerable<ModelInformation>? listing = client switch
                    {
                        CodexClient c => c.ListModelsAsync(),
                        CursorClient c => c.ListModelsAsync(),
                        MuxClient c => c.ListModelsAsync(),
                        PiClient c => c.ListModelsAsync(),
                        OpenCodeClient c => c.ListModelsAsync(),
                        _ => null,
                    };
                    if (listing != null)
                    {
                        await foreach (ModelInformation model in listing.ConfigureAwait(false)) models.Add(model.Name);
                        if (models.Count == 0) throw new InvalidOperationException("Model list was empty.");
                        _AgentInvocations.Add(name + " models\n" + string.Join(",", models));
                    }

                    using CliAgentClientBase notInstalled = create();
                    notInstalled.Executable = "polyprompt-aot-missing-" + name;
                    CliAgentResult failure = await notInstalled.RunAsync(new CliAgentRequest("x")).ConfigureAwait(false);
                    Expect(CliFailureKind.NotInstalled, failure.FailureKind, name + " missing executable");
                }).ConfigureAwait(false);
            }
        }

        private static void RecordInvocation(string label, FakeCli fake, string text, List<CliAgentEvent> events)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(label).Append('\n');
            foreach (string arg in fake.Arguments) sb.Append(arg).Append('\0');
            sb.Append('\n').Append(fake.StandardInput ?? "").Append('\n').Append(text).Append('\n');
            sb.Append(string.Join(",", events.Select(e => e.Kind.ToString())));
            _AgentInvocations.Add(sb.ToString());
        }

        #endregion

        #region Serializer-Checks

        private static void RunSerializerChecks(bool reflection)
        {
            Serializer serializer = new Serializer();

            Check("serializer: dictionary and list graph", () =>
            {
                Dictionary<string, object> body = new Dictionary<string, object>
                {
                    { "model", Model },
                    { "stream", false },
                    { "n", 2 },
                    { "temperature", 0.5 },
                    { "stop", new[] { "END" } },
                    { "embedding", new List<float> { 1.5f, -2f } },
                    { "day", DayOfWeek.Monday },
                    { "when", new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc) },
                    { "raw", new byte[] { 1, 2, 3 } },
                    { "schema", JsonDocument.Parse("{\"type\":\"object\"}").RootElement.Clone() },
                    { "messages", new List<Dictionary<string, object>> { new Dictionary<string, object> { { "role", "user" }, { "content", "hi" } } } },
                };

                Expect("{\"model\":\"test-model\",\"stream\":false,\"n\":2,\"temperature\":0.5,\"stop\":[\"END\"],\"embedding\":[1.5,-2],\"day\":\"Monday\",\"when\":\"2026-01-02T03:04:05.000000Z\",\"raw\":\"AQID\",\"schema\":{\"type\":\"object\"},\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}]}",
                    serializer.SerializeJson(body, false), "graph output");
            });

            Check("serializer: untyped reads", () =>
            {
                Dictionary<string, object>? body = serializer.DeserializeJson<Dictionary<string, object>>("{\"a\":[1,2,],\"b\":\"3\" /* c */}");
                Expect(2, ((JsonElement)body!["a"]).GetArrayLength(), "array length");
                List<List<Dictionary<string, object>>>? nested = serializer.DeserializeJson<List<List<Dictionary<string, object>>>>("[[{\"label\":\"x\"}]]");
                Expect("x", ((JsonElement)nested![0][0]["label"]).GetString(), "nested label");
            });

            Check("serializer: PolyPromptJsonContext round trip", () =>
            {
                List<ChatMessage> messages = new List<ChatMessage>
                {
                    ChatMessage.User("Weather?"),
                    ChatMessage.AssistantToolCalls(new[] { new ToolCall { Id = "c1", Name = "get_weather", ArgumentsJson = "{\"city\":\"Paris\"}", ThoughtSignature = "sig" } }),
                };

                string json = serializer.SerializeJson(messages, false);
                List<ChatMessage>? restored = serializer.DeserializeJson<List<ChatMessage>>(json);
                Expect("sig", restored![1].ToolCalls[0].ThoughtSignature, "thought signature");
                Expect(json, JsonSerializer.Serialize(messages, PolyPromptJsonContext.Default.ListChatMessage), "context output");
            });

            Check("serializer: unregistered type is rejected with guidance", () =>
            {
                ToolCall call = new ToolCall { Name = "get_weather", ArgumentsJson = "{\"City\":\"Paris\",\"Days\":2}" };
                if (reflection)
                {
                    Expect("Paris", call.DeserializeArguments<WeatherArgs>()!.City, "reflection fallback");
                    return;
                }

                try
                {
                    call.DeserializeArguments<WeatherArgs>();
                    throw new InvalidOperationException("Expected NotSupportedException for an unregistered type.");
                }
                catch (NotSupportedException ex)
                {
                    if (!ex.Message.Contains("AddTypeInfoResolver", StringComparison.Ordinal))
                        throw new InvalidOperationException("The exception should explain how to register the type: " + ex.Message);
                }
            });

            Check("serializer: explicit JsonTypeInfo", () =>
            {
                ToolCall call = new ToolCall { Name = "get_weather", ArgumentsJson = "{\"City\":\"Rome\",\"Days\":3}" };
                WeatherArgs? args = call.DeserializeArguments(AotTestJsonContext.Default.WeatherArgs);
                Expect("Rome", args!.City, "city");
                Expect("{\"City\":\"Rome\",\"Days\":3}", serializer.SerializeJson(args, AotTestJsonContext.Default.WeatherArgs, false), "typed output");
            });

            CheckAsync("clients: unregistered caller types fail the call with guidance", async () =>
            {
                if (reflection) return;

                // A separate handler keeps these calls out of the request digest; nothing should be sent anyway.
                LocalFakeHandler handler = LocalFakeHandler.Always(System.Net.HttpStatusCode.OK, "{}");
                using HttpClient http = new HttpClient(handler);

                using TypeSafeClient decision = new TypeSafeClient("test-key", "http://127.0.0.1:1", new ClientOptions { HttpClient = http }) { Defaults = { Decision = { Model = Model } } };
                DecisionResponse decided = await decision.DecideAsync(new DecisionRequest
                {
                    State = new UnregisteredState { Note = "x" },
                    Questions = new List<DecisionQuestion> { DecisionQuestion.Binary("q", "Yes?") }
                }).ConfigureAwait(false);
                ExpectFailureWithGuidance(decided, "decision with an unregistered state type");

                using OpenAiCompatibleClient completion = new OpenAiCompatibleClient("http://127.0.0.1:1", "test-key", new ClientOptions { HttpClient = http }) { Defaults = { Completion = { Model = Model } } };
                ToolChatRequest request = WeatherToolRequest();
                request.Tools[0].Parameters["x-extra"] = new UnregisteredState { Note = "x" };
                ExpectFailureWithGuidance(await completion.ToolChatAsync(request).ConfigureAwait(false), "tool schema with an unregistered value type");

                Expect(0, handler.Count, "requests sent");
            }).GetAwaiter().GetResult();

            Serializer.AddTypeInfoResolver(AotTestJsonContext.Default);

            Check("serializer: registered resolver", () =>
            {
                ToolCall call = new ToolCall { Name = "get_weather", ArgumentsJson = "{\"City\":\"Oslo\",\"Days\":1}" };
                Expect("Oslo", call.DeserializeArguments<WeatherArgs>()!.City, "registered type read");
                Expect("{\"state\":{\"City\":\"Oslo\",\"Days\":1}}", serializer.SerializeJson(new Dictionary<string, object> { { "state", new WeatherArgs { City = "Oslo", Days = 1 } } }, false), "registered type nested write");
            });

            Check("serializer: failures", () =>
            {
                ExpectThrows<ArgumentNullException>(() => serializer.DeserializeJson<Dictionary<string, object>>(null!), "null json");
                ExpectThrows<JsonException>(() => serializer.DeserializeJson<Dictionary<string, object>>("{oops"), "malformed json");
                Dictionary<string, object> cyclic = new Dictionary<string, object>();
                cyclic["self"] = cyclic;
                ExpectThrows<JsonException>(() => serializer.SerializeJson(cyclic), "cycle");
                ExpectThrows<ArgumentException>(() => serializer.SerializeJson(new Dictionary<string, object> { { "v", double.NaN } }), "NaN");
            });
        }

        #endregion

        #region Client-Checks

        /// <summary>
        /// 3.7.0 paths: Ollama keep_alive (a boxed long in the request graph), model load and unload, SupportsTools from
        /// /api/show, the optional output limit, and metadata-only call details with redacted credentials.
        /// </summary>
        private static async Task RunReleaseThreeSevenChecksAsync(LocalOpenAiTestServer server)
        {
            using OllamaClient ollama = new OllamaClient(server.Endpoint, "aot-secret") { Defaults = { Completion = { Model = "test-model" } } };
            ollama.Defaults.Completion.KeepAlive = TimeSpan.FromMinutes(2);
            await CheckAsync("ollama: chat with keep_alive and no default limit", async () =>
            {
                Ok(await ollama.ChatAsync("keep alive").ConfigureAwait(false));
                string body = server.RequestBodies.Last();
                Expect(true, body.Contains("\"keep_alive\":120", StringComparison.Ordinal), "keep_alive seconds in " + body);
                Expect(false, body.Contains("num_predict", StringComparison.Ordinal), "no num_predict");
            }).ConfigureAwait(false);

            Check("call details: metadata only, credentials redacted", () =>
            {
                CallDetail detail = ollama.CallDetails.Last();
                Expect(true, detail.RequestBody == null && detail.ResponseBody == null, "bodies dropped");
                Expect("[redacted]", detail.RequestHeaders?["Authorization"], "authorization");
            });

            using OllamaClient models = new OllamaClient(server.Endpoint);
            await CheckAsync("ollama: load and unload", async () =>
            {
                Expect(true, await models.LoadModelAsync("test-model", Timeout.InfiniteTimeSpan).ConfigureAwait(false), "load");
                Expect(true, server.RequestBodies.Last().Contains("\"keep_alive\":-1", StringComparison.Ordinal), "keep_alive -1");
                Expect(true, await models.UnloadModelAsync("test-model").ConfigureAwait(false), "unload");
            }).ConfigureAwait(false);

            await CheckAsync("ollama: SupportsTools from /api/show", async () =>
            {
                ModelInformation? info = await models.GetModelInformationAsync("probe-renderer").ConfigureAwait(false);
                Expect(true, info?.SupportsTools == true, "renderer handles tools");
                Expect("qwen3-coder", info?.Metadata["parser"], "parser");
                ModelInformation? deepseek = await models.GetModelInformationAsync("probe-deepseek").ConfigureAwait(false);
                Expect(true, deepseek?.SupportsTools == false, "deepseek template drops tools");
                string json = System.Text.Json.JsonSerializer.Serialize(info!, PolyPromptJsonContext.Default.ModelInformation);
                Expect(true, json.Contains("SupportsTools\":true", StringComparison.OrdinalIgnoreCase), "serialized SupportsTools in " + json);
            }).ConfigureAwait(false);
        }

        private static async Task RunXAiAdvancedChecksAsync(LocalOpenAiTestServer server)
        {
            using XAiClient client = new(LocalXAiRoutes.Key, server.Endpoint);
            using JsonDocument prompt = JsonDocument.Parse("\"native xAI\"");
            await CheckAsync("XAi: raw Responses and saved encrypted state", async () =>
            {
                ResponsesApiResponse response = await client.CreateResponseAsync(prompt.RootElement);
                Ok(response);
                Ok(await client.CreateResponseAsync(prompt.RootElement, new PolyPrompt.Options.XAiCompletionOptions { FunctionTools = WeatherToolRequest().Tools }));
                string json = JsonSerializer.Serialize(response.ToAssistantMessage(), PolyPromptJsonContext.Default.ChatMessage);
                ChatMessage? restored = JsonSerializer.Deserialize(json, PolyPromptJsonContext.Default.ChatMessage);
                Expect(true, restored!.ProviderState.HasValue, "encrypted output survives native JSON persistence");
            });
            await CheckAsync("XAi: native event stream", async () =>
            {
                ResponsesApiStreamingResponse response = await client.CreateResponseStreamingAsync(prompt.RootElement);
                await foreach (ResponsesApiEvent _ in response.Events) { }
                Ok(response); Expect(true, response.Response != null, "terminal response parsed");
            });
            await CheckAsync("XAi: stored response resources", async () =>
            {
                Ok(await client.GetResponseAsync("resp-test")); Ok(await client.ListResponseInputItemsAsync("resp-test"));
                Ok(await client.CompactConversationAsync(prompt.RootElement)); Ok(await client.DeleteResponseAsync("resp-test"));
            });
        }

        private static async Task RunClientChecksAsync(LocalOpenAiTestServer server)
        {
            List<ProviderClientBase> clients = LocalClients.All(server.Endpoint);
            try
            {
                foreach (ProviderClientBase client in clients)
                {
                    string name = client.GetType().Name;
                    SetModel(client);

                    await CheckAsync(name + ": connectivity", async () => Expect(true, await client.ValidateConnectivityAsync().ConfigureAwait(false), "reachable")).ConfigureAwait(false);

                    if (client.Supports(Capability.Chat)) await RunCompletionAsync(name, client).ConfigureAwait(false);
                    if (client.Supports(Capability.Embed))
                        await CheckAsync(name + ": embed", async () => Ok(await client.EmbedAsync(new List<string> { "alpha", "beta" }).ConfigureAwait(false))).ConfigureAwait(false);
                    if (client.Supports(Capability.SparseEmbed))
                        await CheckAsync(name + ": sparse embed", async () => Ok(await client.EmbedSparseAsync("alpha").ConfigureAwait(false))).ConfigureAwait(false);
                    if (client.Supports(Capability.Rerank))
                        await CheckAsync(name + ": rerank", async () => Ok(await client.RerankAsync("capital of France", new List<string> { "Paris", "Berlin" }).ConfigureAwait(false))).ConfigureAwait(false);
                    if (client.Supports(Capability.Classify))
                        await CheckAsync(name + ": classify", async () => Ok(await client.ClassifyAsync("I love it").ConfigureAwait(false))).ConfigureAwait(false);
                    if (client.Supports(Capability.Decide))
                        await CheckAsync(name + ": decide", async () => Ok(await client.DecideAsync(DecisionRequestWithTypedState()).ConfigureAwait(false))).ConfigureAwait(false);
                    if (client.Supports(Capability.Models))
                        await CheckAsync(name + ": list models", async () =>
                        {
                            int count = 0;
                            await foreach (ModelInformation model in client.ListModelsAsync().ConfigureAwait(false)) count++;
                            Expect(true, count > 0, "at least one model");
                        }).ConfigureAwait(false);
                }
            }
            finally
            {
                LocalClients.DisposeAll(clients);
            }
        }

        private static async Task RunCompletionAsync(string name, ProviderClientBase client)
        {
            await CheckAsync(name + ": chat", async () => Ok(await client.ChatAsync("Say hello.").ConfigureAwait(false))).ConfigureAwait(false);
            await CheckAsync(name + ": generate", async () => Ok(await client.GenerateAsync("Say hello.").ConfigureAwait(false))).ConfigureAwait(false);
            await CheckAsync(name + ": tool chat", async () => Ok(await client.ToolChatAsync(WeatherToolRequest()).ConfigureAwait(false))).ConfigureAwait(false);

            await CheckAsync(name + ": chat streaming", async () =>
            {
                ChatStreamingResponse response = await client.ChatStreamingAsync("Say hello.").ConfigureAwait(false);
                Ok(response);
                await foreach (ChatStreamingChunk chunk in response.Chunks.ConfigureAwait(false)) { }
                Expect(true, response.Success, "stream completed: " + response.Error);
            }).ConfigureAwait(false);

            await CheckAsync(name + ": tool chat streaming", async () =>
            {
                ToolChatStreamingResponse response = await client.ToolChatStreamingAsync(WeatherToolRequest()).ConfigureAwait(false);
                Ok(response);
                await foreach (ToolChatStreamingChunk chunk in response.Chunks.ConfigureAwait(false)) { }
                Expect(true, response.Success, "stream completed: " + response.Error);
            }).ConfigureAwait(false);
        }

        private static void SetModel(ProviderClientBase client)
        {
            ClientDefaults defaults = client.Defaults;
            if (defaults.Completion != null) defaults.Completion.Model = Model;
            if (defaults.Embedding != null) defaults.Embedding.Model = Model;
            if (defaults.SparseEmbedding != null) defaults.SparseEmbedding.Model = Model;
            if (defaults.Rerank != null) defaults.Rerank.Model = RerankModel;
            if (defaults.Classification != null) defaults.Classification.Model = Model;
            if (defaults.Decision != null) defaults.Decision.Model = Model;
        }

        private static ToolChatRequest WeatherToolRequest()
        {
            // The schema mixes dictionaries, lists, arrays, and a JsonElement, as callers build them.
            Dictionary<string, object> parameters = new Dictionary<string, object>
            {
                { "type", "object" },
                { "properties", new Dictionary<string, object>
                    {
                        { "city", new Dictionary<string, object> { { "type", "string" }, { "description", "City name" } } },
                        { "unit", new Dictionary<string, object> { { "type", "string" }, { "enum", new List<string> { "celsius", "fahrenheit" } } } },
                        { "days", JsonDocument.Parse("{\"type\":\"integer\",\"minimum\":1}").RootElement.Clone() },
                    }
                },
                { "required", new[] { "city" } },
            };

            return new ToolChatRequest
            {
                Messages = new List<ChatMessage> { ChatMessage.System("Use tools."), ChatMessage.User("Weather in Paris?") },
                Tools = new List<ToolDefinition> { ToolDefinition.Function("get_weather", "Get the weather.", parameters) },
                Options = new CompletionOptions { Temperature = 0.1, MaxTokens = 64 }
            };
        }

        private static DecisionRequest DecisionRequestWithTypedState()
        {
            return new DecisionRequest
            {
                State = new WeatherArgs { City = "Paris", Days = 2 },
                Questions = new List<DecisionQuestion>
                {
                    DecisionQuestion.Binary("rain", "Will it rain?"),
                    DecisionQuestion.Choice("outfit", "What should I wear?", "coat", "shirt"),
                    DecisionQuestion.Score("comfort", "How comfortable is it?", new[] { "bad", "ok", "great" }),
                }
            };
        }

        #endregion

        #region Helpers

        private static void Check(string name, Action action)
        {
            CheckAsync(name, () => { action(); return Task.CompletedTask; }).GetAwaiter().GetResult();
        }

        private static async Task CheckAsync(string name, Func<Task> action)
        {
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                await action().ConfigureAwait(false);
                _Passed++;
                Console.WriteLine("PASS  " + name + " (" + sw.ElapsedMilliseconds + "ms)");
            }
            catch (Exception ex)
            {
                _Failures.Add(name + ": " + ex.GetType().Name + ": " + ex.Message);
                Console.WriteLine("FAIL  " + name + ": " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void Ok(ResponseBase response)
        {
            if (!response.Success) throw new InvalidOperationException("Response failed (" + response.StatusCode + "): " + response.Error);
        }

        private static void ExpectFailureWithGuidance(ResponseBase response, string what)
        {
            if (response.Success) throw new InvalidOperationException(what + ": expected an unsuccessful response.");
            if (response.Error == null || !response.Error.Contains("AddTypeInfoResolver", StringComparison.Ordinal))
                throw new InvalidOperationException(what + ": the error should explain how to register the type: " + response.Error);
        }

        private static void Expect<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException(what + ": expected '" + expected + "', got '" + actual + "'.");
        }

        private static void ExpectThrows<TException>(Action action, string what) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }

            throw new InvalidOperationException(what + ": expected " + typeof(TException).Name + ".");
        }

        private static string Digest(LocalOpenAiTestServer server)
        {
            StringBuilder all = new StringBuilder();
            for (int i = 0; i < server.RequestBodies.Count; i++)
            {
                all.Append(server.RequestUrls[i]).Append('\n').Append(server.RequestBodies[i]).Append('\n');
            }

            foreach (string body in _ContinuationBodies) all.Append(body).Append('\n');
            foreach (string invocation in _AgentInvocations) all.Append(invocation).Append('\n');
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(all.ToString())));
        }

        #endregion
    }

    /// <summary>
    /// A caller-defined type: typed tool arguments and decision state.
    /// </summary>
    public sealed class WeatherArgs
    {
        public string City { get; set; } = string.Empty;

        public int Days { get; set; }
    }

    /// <summary>
    /// A caller-defined type that is never registered, for the failure checks.
    /// </summary>
    public sealed class UnregisteredState
    {
        public string Note { get; set; } = string.Empty;
    }

    /// <summary>
    /// The caller's source-generated metadata, registered with <see cref="Serializer.AddTypeInfoResolver"/>.
    /// </summary>
    [JsonSerializable(typeof(WeatherArgs))]
    internal sealed partial class AotTestJsonContext : JsonSerializerContext
    {
    }
}
