namespace Test.Shared
{
    using System.Net;
    using System.Reflection;
    using System.Text.Json;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using Touchstone.Core;

    /// <summary>
    /// Deterministic cases for the 3.x client architecture that must hold for every client: the capability matrix, the
    /// argument validation the capability bases do before any request, the no-model rule, merging per-call options over
    /// client defaults, per-request credentials on a shared HttpClient, the Gemini key staying out of URLs, and every
    /// client's connectivity probe. Registered into the local behavior suite.
    /// </summary>
    internal static class LocalArchitectureCases
    {
        private static readonly string[] _HarnessModelListers = new[] { "CodexClient", "CursorClient", "MuxClient", "PiClient", "OpenCodeClient" };

        private static readonly string[] _ProviderClients = new[]
        {
            "XAiClient", "OllamaClient", "OllamaCloudClient", "OpenAiClient", "OpenAiCompatibleClient", "AzureOpenAiClient", "GeminiClient",
            "VertexAiClient", "AnthropicClient", "CohereClient", "BedrockClient", "VoyageAiClient", "TeiClient", "TypeSafeClient",
        };

        private static readonly string[] _HarnessClients = new[] { "ClaudeCodeClient", "CodexClient", "GeminiCliClient", "CursorClient", "MuxClient", "PiClient", "OpenCodeClient" };

        public static IEnumerable<TestCaseDescriptor> Create(string suiteId)
        {
            return new List<TestCaseDescriptor>
            {
                Case(suiteId, "arch_capability_matrix", "Every provider client derives from ProviderClientBase, harness clients from CliAgentClientBase, and every model operation is on the base", RunCapabilityMatrixAsync),
                Case(suiteId, "arch_defaults_types", "Each client's Defaults is its own get-only instance with one section per supported capability", RunDefaultsTypesAsync),
                Case(suiteId, "arch_completion_validation", "Every completion client rejects null prompts and empty or null-containing tool requests before sending", RunCompletionValidationAsync),
                Case(suiteId, "arch_embedding_validation", "Every embedding and sparse embedding client rejects null, empty, and null-containing input before sending", RunEmbeddingValidationAsync),
                Case(suiteId, "arch_rerank_validation", "Every rerank client rejects bad queries, documents, and TopN before sending", RunRerankValidationAsync),
                Case(suiteId, "arch_classification_model_validation", "Classification and model clients reject bad input before sending", RunClassificationAndModelValidationAsync),
                Case(suiteId, "arch_no_model", "Clients that need a model throw InvalidOperationException without one, and a per-call model suffices", RunNoModelAsync),
                Case(suiteId, "arch_completion_merge", "Every completion client merges per-call options over Defaults field by field, with MaxTokens defaulting to 4096 (none on Ollama)", RunCompletionMergeAsync),
                Case(suiteId, "arch_default_max_tokens_optout", "SendDefaultMaxTokens = false sends no output limit, except where the provider requires one; an explicit MaxTokens always wins", RunDefaultMaxTokensOptOutAsync),
                Case(suiteId, "arch_shared_httpclient", "Clients sharing an HttpClient send their own credentials per request and never touch DefaultRequestHeaders or dispose it", RunSharedHttpClientAsync),
                Case(suiteId, "arch_gemini_key_header", "Gemini clients send the API key only in the x-goog-api-key header, never in a URL or call detail URL", RunGeminiKeyHeaderAsync),
                Case(suiteId, "arch_connectivity_success", "Every client's ValidateConnectivityAsync returns true against a reachable server", RunConnectivitySuccessAsync),
                Case(suiteId, "arch_connectivity_failure", "Every client's ValidateConnectivityAsync returns false on HTTP 401 and on an unreachable endpoint", RunConnectivityFailureAsync),
                Case(suiteId, "arch_connectivity_cancellation", "Every client's ValidateConnectivityAsync rethrows caller cancellation", RunConnectivityCancellationAsync),
            };
        }

        private static TestCaseDescriptor Case(string suiteId, string caseId, string displayName, Func<CancellationToken, Task> executeAsync)
        {
            return new TestCaseDescriptor(suiteId, caseId, displayName, executeAsync, new[] { "local" });
        }

        #region Structure

        private static Task RunCapabilityMatrixAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            List<Type> clients = typeof(ClientBase).Assembly.GetExportedTypes()
                .Where(t => t.IsClass && !t.IsAbstract && typeof(ClientBase).IsAssignableFrom(t))
                .ToList();

            List<string> providers = clients.Where(c => typeof(ProviderClientBase).IsAssignableFrom(c)).Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            List<string> harnesses = clients.Where(c => typeof(CliAgentClientBase).IsAssignableFrom(c)).Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            SharedAssert.Equal(string.Join(", ", _ProviderClients.OrderBy(n => n, StringComparer.Ordinal)), string.Join(", ", providers), "Exactly the expected provider clients exist.");
            SharedAssert.Equal(string.Join(", ", _HarnessClients.OrderBy(n => n, StringComparer.Ordinal)), string.Join(", ", harnesses), "Exactly the expected harness clients exist.");
            SharedAssert.Equal(_ProviderClients.Length + _HarnessClients.Length, clients.Count, "There should be no client outside the two families.");

            foreach (Type client in clients)
                SharedAssert.True(client.Name.EndsWith("Client", StringComparison.Ordinal), client.Name + " should be named *Client.");

            SharedAssert.True(typeof(ProviderClientBase).IsAbstract && typeof(ProviderClientBase).BaseType == typeof(HttpClientBase), "ProviderClientBase is abstract and derives from HttpClientBase.");
            SharedAssert.True(typeof(CliAgentClientBase).IsAbstract && typeof(CliAgentClientBase).BaseType == typeof(ClientBase), "CliAgentClientBase is abstract and derives from ClientBase.");

            // Every model operation is declared once, on ProviderClientBase, so code written against it runs on any provider.
            string[] operations = { "ChatAsync", "ChatStreamingAsync", "ToolChatAsync", "ToolChatStreamingAsync", "GenerateAsync", "GenerateStreamingAsync",
                "EmbedAsync", "EmbedSparseAsync", "RerankAsync", "ClassifyAsync", "DecideAsync", "ListModelsAsync", "ModelExistsAsync", "GetModelInformationAsync" };
            foreach (string operation in operations)
            {
                MethodInfo[] declared = typeof(ProviderClientBase).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => m.Name == operation).ToArray();
                SharedAssert.True(declared.Length > 0, operation + " should be declared on ProviderClientBase.");
            }

            foreach (Type harness in clients.Where(c => typeof(CliAgentClientBase).IsAssignableFrom(c)))
            {
                SharedAssert.False(typeof(HttpClientBase).IsAssignableFrom(harness), harness.Name + " must not derive from HttpClientBase.");
                bool lists = harness.GetMethods(BindingFlags.Public | BindingFlags.Instance).Any(m => m.Name == "ListModelsAsync");
                SharedAssert.Equal(_HarnessModelListers.Contains(harness.Name), lists, harness.Name + " should expose ListModelsAsync only where the CLI can list models.");
            }

            return Task.CompletedTask;
        }

        private static Task RunDefaultsTypesAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            List<ProviderClientBase> first = LocalClients.All("http://127.0.0.1:1");
            List<ProviderClientBase> second = LocalClients.All("http://127.0.0.1:1");
            try
            {
                (string Section, Capability Capability, Type Options)[] sections =
                {
                    ("Completion", Capability.Chat, typeof(CompletionOptions)), ("Embedding", Capability.Embed, typeof(EmbeddingOptions)),
                    ("SparseEmbedding", Capability.SparseEmbed, typeof(SparseEmbeddingOptions)), ("Rerank", Capability.Rerank, typeof(RerankOptions)),
                    ("Classification", Capability.Classify, typeof(ClassificationOptions)), ("Decision", Capability.Decide, typeof(DecisionOptions)),
                };

                for (int i = 0; i < first.Count; i++)
                {
                    ProviderClientBase client = first[i];
                    string name = client.GetType().Name;
                    ClientDefaults defaults = client.Defaults;
                    SharedAssert.False(ReferenceEquals(defaults, second[i].Defaults), name + " instances should not share a Defaults object.");
                    SharedAssert.False(client.GetType().GetProperties().Any(p => p.Name == "Defaults" && p.SetMethod != null && p.SetMethod.IsPublic), name + " Defaults should be get-only.");
                    SharedAssert.True(client.GetType().GetProperty("Model", BindingFlags.Public | BindingFlags.Instance) == null, name + " should have no Model shorthand.");

                    foreach (var (section, capability, options) in sections)
                    {
                        object? value = typeof(ClientDefaults).GetProperty(section)!.GetValue(defaults);
                        if (client.Supports(capability))
                        {
                            SharedAssert.NotNull(value, name + " Defaults." + section + " should exist.");
                            SharedAssert.True(options.IsAssignableFrom(value!.GetType()), name + " Defaults." + section + " should be a " + options.Name + ".");
                            object? other = typeof(ClientDefaults).GetProperty(section)!.GetValue(second[i].Defaults);
                            SharedAssert.False(ReferenceEquals(value, other), name + " Defaults." + section + " should not be shared.");
                        }
                        else
                        {
                            SharedAssert.True(value == null, name + " Defaults." + section + " should be null through ClientDefaults.");
                        }
                    }
                }
            }
            finally
            {
                LocalClients.DisposeAll(first);
                LocalClients.DisposeAll(second);
            }

            return Task.CompletedTask;
        }

        #endregion

        #region Validation

        private static async Task RunCompletionValidationAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            List<ProviderClientBase> clients = LocalClients.Completion(server.Endpoint);
            try
            {
                foreach (ProviderClientBase client in clients)
                {
                    string name = client.GetType().Name;
                    await SharedAssert.ThrowsExactAsync<ArgumentNullException>(() => client.ChatAsync(null!, token: token), name + " ChatAsync should reject a null prompt.").ConfigureAwait(false);
                    await SharedAssert.ThrowsExactAsync<ArgumentNullException>(() => client.ChatStreamingAsync(null!, token: token), name + " ChatStreamingAsync should reject a null prompt.").ConfigureAwait(false);
                    await SharedAssert.ThrowsExactAsync<ArgumentNullException>(() => client.GenerateAsync(null!, token: token), name + " GenerateAsync should reject a null prompt.").ConfigureAwait(false);
                    await SharedAssert.ThrowsExactAsync<ArgumentNullException>(() => client.GenerateStreamingAsync(null!, token: token), name + " GenerateStreamingAsync should reject a null prompt.").ConfigureAwait(false);
                    await SharedAssert.ThrowsExactAsync<ArgumentNullException>(() => client.ToolChatAsync(null!, token), name + " ToolChatAsync should reject a null request.").ConfigureAwait(false);
                    await SharedAssert.ThrowsExactAsync<ArgumentNullException>(() => client.ToolChatStreamingAsync(null!, token), name + " ToolChatStreamingAsync should reject a null request.").ConfigureAwait(false);
                    await SharedAssert.ThrowsExactAsync<ArgumentException>(() => client.ToolChatAsync(new ToolChatRequest(), token), name + " ToolChatAsync should reject a request with no messages.").ConfigureAwait(false);
                    await SharedAssert.ThrowsExactAsync<ArgumentException>(() => client.ToolChatStreamingAsync(new ToolChatRequest(), token), name + " ToolChatStreamingAsync should reject a request with no messages.").ConfigureAwait(false);

                    ToolChatRequest withNull = new ToolChatRequest();
                    withNull.Messages.Add(ChatMessage.User("hi"));
                    withNull.Messages.Add(null!);
                    await SharedAssert.ThrowsExactAsync<ArgumentException>(() => client.ToolChatAsync(withNull, token), name + " ToolChatAsync should reject a null message.").ConfigureAwait(false);
                }

                SharedAssert.Equal(0, server.RequestPaths.Count, "No invalid completion call should reach the server.");
            }
            finally
            {
                LocalClients.DisposeAll(clients);
            }
        }

        private static async Task RunEmbeddingValidationAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            List<ProviderClientBase> clients = LocalClients.Embedding(server.Endpoint);
            using TeiClient sparse = new TeiClient(server.Endpoint, LocalClients.TestKey);
            try
            {
                foreach (ProviderClientBase client in clients)
                {
                    string name = client.GetType().Name;
                    await SharedAssert.ThrowsExactAsync<ArgumentNullException>(() => client.EmbedAsync((string)null!, null, token), name + " should reject a null input.").ConfigureAwait(false);
                    await SharedAssert.ThrowsExactAsync<ArgumentNullException>(() => client.EmbedAsync((List<string>)null!, null, token), name + " should reject a null input list.").ConfigureAwait(false);
                    await SharedAssert.ThrowsExactAsync<ArgumentException>(() => client.EmbedAsync(new List<string>(), null, token), name + " should reject an empty input list.").ConfigureAwait(false);
                    await SharedAssert.ThrowsExactAsync<ArgumentException>(() => client.EmbedAsync(new List<string> { "a", null! }, null, token), name + " should reject a null element.").ConfigureAwait(false);
                }

                await SharedAssert.ThrowsExactAsync<ArgumentNullException>(() => sparse.EmbedSparseAsync((string)null!, null, token), "Sparse embedding should reject a null input.").ConfigureAwait(false);
                await SharedAssert.ThrowsExactAsync<ArgumentNullException>(() => sparse.EmbedSparseAsync((List<string>)null!, null, token), "Sparse embedding should reject a null input list.").ConfigureAwait(false);
                await SharedAssert.ThrowsExactAsync<ArgumentException>(() => sparse.EmbedSparseAsync(new List<string>(), null, token), "Sparse embedding should reject an empty input list.").ConfigureAwait(false);
                await SharedAssert.ThrowsExactAsync<ArgumentException>(() => sparse.EmbedSparseAsync(new List<string> { null! }, null, token), "Sparse embedding should reject a null element.").ConfigureAwait(false);

                SharedAssert.Equal(0, server.RequestPaths.Count, "No invalid embedding call should reach the server.");
            }
            finally
            {
                LocalClients.DisposeAll(clients);
            }
        }

        private static async Task RunRerankValidationAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            List<ProviderClientBase> clients = LocalClients.Rerank(server.Endpoint);
            List<string> documents = new List<string> { "a", "b" };
            try
            {
                foreach (ProviderClientBase client in clients)
                {
                    string name = client.GetType().Name;
                    await SharedAssert.ThrowsExactAsync<ArgumentNullException>(() => client.RerankAsync(null!, documents, null, token), name + " should reject a null query.").ConfigureAwait(false);
                    await SharedAssert.ThrowsExactAsync<ArgumentException>(() => client.RerankAsync("  ", documents, null, token), name + " should reject a whitespace query.").ConfigureAwait(false);
                    await SharedAssert.ThrowsExactAsync<ArgumentNullException>(() => client.RerankAsync("q", null!, null, token), name + " should reject null documents.").ConfigureAwait(false);
                    await SharedAssert.ThrowsExactAsync<ArgumentException>(() => client.RerankAsync("q", new List<string>(), null, token), name + " should reject empty documents.").ConfigureAwait(false);
                    await SharedAssert.ThrowsExactAsync<ArgumentException>(() => client.RerankAsync("q", new List<string> { "a", null! }, null, token), name + " should reject a null document.").ConfigureAwait(false);
                    await SharedAssert.ThrowsExactAsync<ArgumentOutOfRangeException>(() => client.RerankAsync("q", documents, new RerankOptions { TopN = 3 }, token), name + " should reject a per-call TopN above the document count.").ConfigureAwait(false);
                }

                SharedAssert.Equal(0, server.RequestPaths.Count, "No invalid rerank call should reach the server.");
            }
            finally
            {
                LocalClients.DisposeAll(clients);
            }
        }

        private static async Task RunClassificationAndModelValidationAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            List<ProviderClientBase> classifiers = LocalClients.Classification(server.Endpoint);
            List<ProviderClientBase> models = LocalClients.Models(server.Endpoint);
            try
            {
                foreach (ProviderClientBase client in classifiers)
                {
                    string name = client.GetType().Name;
                    await SharedAssert.ThrowsExactAsync<ArgumentNullException>(() => client.ClassifyAsync((string)null!, null, token), name + " should reject a null input.").ConfigureAwait(false);
                    await SharedAssert.ThrowsExactAsync<ArgumentNullException>(() => client.ClassifyAsync((List<string>)null!, null, token), name + " should reject a null input list.").ConfigureAwait(false);
                    await SharedAssert.ThrowsExactAsync<ArgumentException>(() => client.ClassifyAsync(new List<string>(), null, token), name + " should reject an empty input list.").ConfigureAwait(false);
                    await SharedAssert.ThrowsExactAsync<ArgumentException>(() => client.ClassifyAsync(new List<string> { null! }, null, token), name + " should reject a null element.").ConfigureAwait(false);
                }

                foreach (ProviderClientBase client in models)
                {
                    string name = client.GetType().Name;
                    await SharedAssert.ThrowsExactAsync<ArgumentNullException>(() => client.ModelExistsAsync(null!, token), name + " ModelExistsAsync should reject a null name.").ConfigureAwait(false);
                    await SharedAssert.ThrowsExactAsync<ArgumentNullException>(() => client.ModelExistsAsync(" ", token), name + " ModelExistsAsync should reject a blank name.").ConfigureAwait(false);
                    await SharedAssert.ThrowsExactAsync<ArgumentNullException>(() => client.GetModelInformationAsync(string.Empty, token), name + " GetModelInformationAsync should reject an empty name.").ConfigureAwait(false);
                }

                SharedAssert.Equal(0, server.RequestPaths.Count, "No invalid classification or model call should reach the server.");
            }
            finally
            {
                LocalClients.DisposeAll(classifiers);
                LocalClients.DisposeAll(models);
            }
        }

        private static async Task RunNoModelAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            List<ProviderClientBase> clients = LocalClients.All(server.Endpoint);
            try
            {
                foreach (ProviderClientBase client in clients.Where(c => c.Supports(Capability.Chat)))
                {
                    string name = client.GetType().Name;
                    client.Defaults.Completion.Model = null;
                    await SharedAssert.ThrowsExactAsync<InvalidOperationException>(() => client.ChatAsync("hi", token: token), name + " ChatAsync should throw without a model.").ConfigureAwait(false);
                    await SharedAssert.ThrowsExactAsync<InvalidOperationException>(() => client.GenerateStreamingAsync("hi", token: token), name + " GenerateStreamingAsync should throw without a model.").ConfigureAwait(false);
                    ToolChatRequest request = new ToolChatRequest();
                    request.Messages.Add(ChatMessage.User("hi"));
                    await SharedAssert.ThrowsExactAsync<InvalidOperationException>(() => client.ToolChatAsync(request, token), name + " ToolChatAsync should throw without a model.").ConfigureAwait(false);

                    ChatResponse perCall = await client.ChatAsync("hi", new CompletionOptions { Model = "per-call-model" }, token).ConfigureAwait(false);
                    SharedAssert.True(perCall.Success, name + " chat with only a per-call model should succeed. " + perCall.Error);
                }

                foreach (ProviderClientBase client in clients.Where(c => c.Supports(Capability.Embed)))
                {
                    string name = client.GetType().Name;
                    client.Defaults.Embedding.Model = null;
                    if (client is TeiClient)
                    {
                        EmbeddingResponse tei = await client.EmbedAsync("hi", null, token).ConfigureAwait(false);
                        SharedAssert.True(tei.Success, "TEI serves one model and should embed without a model name. " + tei.Error);
                        continue;
                    }

                    await SharedAssert.ThrowsExactAsync<InvalidOperationException>(() => client.EmbedAsync("hi", null, token), name + " should throw without a model.").ConfigureAwait(false);
                    await SharedAssert.ThrowsExactAsync<InvalidOperationException>(() => client.EmbedAsync(new List<string> { "a", "b" }, null, token), name + " batch should throw without a model.").ConfigureAwait(false);
                }

                foreach (ProviderClientBase client in clients.Where(c => c.Supports(Capability.Rerank)))
                {
                    string name = client.GetType().Name;
                    client.Defaults.Rerank.Model = null;
                    if (client is TeiClient)
                    {
                        RerankResponse tei = await client.RerankAsync("q", new List<string> { "a", "b" }, null, token).ConfigureAwait(false);
                        SharedAssert.True(tei.Success, "TEI should rerank without a model name. " + tei.Error);
                        continue;
                    }

                    await SharedAssert.ThrowsExactAsync<InvalidOperationException>(() => client.RerankAsync("q", new List<string> { "a" }, null, token), name + " should throw without a model.").ConfigureAwait(false);
                }

                foreach (ProviderClientBase client in clients.Where(c => c.Supports(Capability.Decide)))
                {
                    client.Defaults.Decision.Model = null;
                    DecisionRequest request = new DecisionRequest { State = "x", Questions = { DecisionQuestion.Binary("b", "Is it?") } };
                    await SharedAssert.ThrowsExactAsync<InvalidOperationException>(() => client.DecideAsync(request, null, token), "DecideAsync should throw without a model.").ConfigureAwait(false);
                    await SharedAssert.ThrowsExactAsync<InvalidOperationException>(() => client.DecideAsync(new List<DecisionRequest> { request }, null, token), "Batch DecideAsync should throw without a model.").ConfigureAwait(false);
                    DecisionResponse perCall = await client.DecideAsync(request, new DecisionOptions { Model = "jev-per-call" }, token).ConfigureAwait(false);
                    SharedAssert.True(perCall.Success, "DecideAsync with only a per-call model should succeed. " + perCall.Error);
                    SharedAssert.Equal("jev-per-call", perCall.Model, "The per-call model should be sent.");
                }
            }
            finally
            {
                LocalClients.DisposeAll(clients);
            }
        }

        #endregion

        #region Merge

        private sealed class CompletionWire
        {
            public Func<JsonElement, int?> MaxTokens = _ => null;
            public Func<JsonElement, double?> Temperature = _ => null;
            public Func<JsonElement, string, string, bool> UsesModel = (_, _, _) => false;
        }

        private static CompletionWire WireFor(ProviderClientBase client)
        {
            Func<JsonElement, string, string, bool> bodyModel = (body, url, model) => GetString(body, "model") == model;

            switch (client)
            {
                case XAiClient:
                case OpenAiClient:
                case AzureOpenAiClient:
                    return new CompletionWire { MaxTokens = b => GetInt(b, "max_output_tokens"), Temperature = b => GetDouble(b, "temperature"), UsesModel = bodyModel };
                case OllamaClient:
                    return new CompletionWire { MaxTokens = b => GetInt(b, "options", "num_predict"), Temperature = b => GetDouble(b, "options", "temperature"), UsesModel = bodyModel };
                case OpenAiCompatibleClient:
                case AnthropicClient:
                case CohereClient:
                    return new CompletionWire { MaxTokens = b => GetInt(b, "max_tokens"), Temperature = b => GetDouble(b, "temperature"), UsesModel = bodyModel };
                case GeminiClient:
                case VertexAiClient:
                    return new CompletionWire { MaxTokens = b => GetInt(b, "generationConfig", "maxOutputTokens"), Temperature = b => GetDouble(b, "generationConfig", "temperature"), UsesModel = (b, url, m) => url.Contains("/models/" + m + ":", StringComparison.Ordinal) };
                case BedrockClient:
                    return new CompletionWire { MaxTokens = b => GetInt(b, "inferenceConfig", "maxTokens"), Temperature = b => GetDouble(b, "inferenceConfig", "temperature"), UsesModel = (b, url, m) => url.Contains("/model/" + m + "/", StringComparison.Ordinal) };
                default:
                    throw new TestFailureException("No wire mapping for " + client.GetType().Name + ".");
            }
        }

        private static async Task RunCompletionMergeAsync(CancellationToken token)
        {
            foreach (ProviderClientBase client in LocalClients.Completion("http://127.0.0.1:1"))
            {
                client.Dispose();
                using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
                ProviderClientBase live = LocalClients.Completion(server.Endpoint).First(c => c.GetType() == client.GetType());
                using (live)
                {
                    string name = live.GetType().Name;
                    CompletionWire wire = WireFor(live);
                    string defaultModel = live.Defaults.Completion.Model!;

                    SharedAssert.True((await live.ChatAsync("hi", token: token).ConfigureAwait(false)).Success, name + " baseline chat should succeed.");

                    live.Defaults.Completion.MaxTokens = 77;
                    live.Defaults.Completion.Temperature = 0.25;
                    SharedAssert.True((await live.ChatAsync("hi", token: token).ConfigureAwait(false)).Success, name + " chat with defaults should succeed.");

                    SharedAssert.True((await live.ChatAsync("hi", new CompletionOptions { MaxTokens = 11, Model = "per-call-model" }, token).ConfigureAwait(false)).Success, name + " chat with per-call options should succeed.");

                    ToolChatRequest request = new ToolChatRequest { Options = new CompletionOptions { Temperature = 0.75 } };
                    request.Messages.Add(ChatMessage.User("hi"));
                    SharedAssert.True((await live.ToolChatAsync(request, token).ConfigureAwait(false)).Success, name + " tool chat with request options should succeed.");

                    List<string> bodies = server.RequestBodies;
                    List<string> urls = server.RequestUrls;
                    SharedAssert.Equal(4, bodies.Count, name + " should have sent four requests.");

                    using JsonDocument b0 = JsonDocument.Parse(bodies[0]);
                    using JsonDocument b1 = JsonDocument.Parse(bodies[1]);
                    using JsonDocument b2 = JsonDocument.Parse(bodies[2]);
                    using JsonDocument b3 = JsonDocument.Parse(bodies[3]);

                    int? builtIn = live is OllamaClient ? null : ProviderClientBase.DefaultMaxTokens;
                    SharedAssert.Equal(!(live is OllamaClient), live.SendDefaultMaxTokens, name + " SendDefaultMaxTokens default.");
                    SharedAssert.Equal<int?>(builtIn, wire.MaxTokens(b0.RootElement), name + " should send the built-in MaxTokens (none on Ollama) when nothing sets it.");
                    SharedAssert.True(wire.Temperature(b0.RootElement) == null, name + " should send no temperature when nothing sets it.");
                    SharedAssert.True(wire.UsesModel(b0.RootElement, urls[0], defaultModel), name + " should use the default model.");

                    SharedAssert.Equal<int?>(77, wire.MaxTokens(b1.RootElement), name + " should send Defaults.MaxTokens.");
                    SharedAssert.Equal<double?>(0.25, wire.Temperature(b1.RootElement), name + " should send Defaults.Temperature.");

                    SharedAssert.Equal<int?>(11, wire.MaxTokens(b2.RootElement), name + " per-call MaxTokens should override the default.");
                    SharedAssert.Equal<double?>(0.25, wire.Temperature(b2.RootElement), name + " should keep Defaults.Temperature when the call leaves it unset.");
                    SharedAssert.True(wire.UsesModel(b2.RootElement, urls[2], "per-call-model"), name + " should use the per-call model. URL: " + urls[2]);
                    SharedAssert.False(wire.UsesModel(b2.RootElement, urls[2], defaultModel), name + " should not use the default model when the call sets one.");

                    SharedAssert.Equal<int?>(77, wire.MaxTokens(b3.RootElement), name + " tool chat should use Defaults.MaxTokens.");
                    SharedAssert.Equal<double?>(0.75, wire.Temperature(b3.RootElement), name + " tool chat should use the request's temperature.");
                    SharedAssert.True(wire.UsesModel(b3.RootElement, urls[3], defaultModel), name + " tool chat should use the default model.");
                    SharedAssert.Equal(defaultModel, live.Defaults.Completion.Model, name + " a per-call model should not change the client's model.");
                }
            }
        }

        private static async Task RunDefaultMaxTokensOptOutAsync(CancellationToken token)
        {
            foreach (ProviderClientBase probe in LocalClients.Completion("http://127.0.0.1:1"))
            {
                Type type = probe.GetType();
                probe.Dispose();
                using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
                using ProviderClientBase live = LocalClients.Completion(server.Endpoint).First(c => c.GetType() == type);
                string name = type.Name;
                CompletionWire wire = WireFor(live);
                bool required = live is AnthropicClient || live is BedrockClient;

                live.SendDefaultMaxTokens = false;
                SharedAssert.True((await live.ChatAsync("hi", token: token).ConfigureAwait(false)).Success, name + " chat with no default limit should succeed.");
                SharedAssert.True((await live.ChatAsync("hi", new CompletionOptions { MaxTokens = 50 }, token).ConfigureAwait(false)).Success, name + " chat with an explicit limit should succeed.");
                live.SendDefaultMaxTokens = true;
                SharedAssert.True((await live.ChatAsync("hi", token: token).ConfigureAwait(false)).Success, name + " chat with the default limit should succeed.");

                List<string> bodies = server.RequestBodies;
                using JsonDocument off = JsonDocument.Parse(bodies[0]);
                using JsonDocument explicitLimit = JsonDocument.Parse(bodies[1]);
                using JsonDocument on = JsonDocument.Parse(bodies[2]);
                SharedAssert.Equal<int?>(required ? ProviderClientBase.DefaultMaxTokens : null, wire.MaxTokens(off.RootElement),
                    name + (required ? " must still send a limit, because the API requires one." : " should send no limit."));
                SharedAssert.Equal<int?>(50, wire.MaxTokens(explicitLimit.RootElement), name + " should send an explicit MaxTokens.");
                SharedAssert.Equal<int?>(ProviderClientBase.DefaultMaxTokens, wire.MaxTokens(on.RootElement), name + " should send the built-in limit when asked to.");
            }

            using LocalOpenAiTestServer bedrockServer = LocalOpenAiTestServer.Start();
            using BedrockClient bedrock = (BedrockClient)LocalClients.Completion(bedrockServer.Endpoint).First(c => c is BedrockClient);
            bedrock.SendDefaultMaxTokens = false;
            bedrock.Defaults.Completion.Model = "meta.llama3-8b-instruct-v1:0";
            SharedAssert.True((await bedrock.ChatAsync("hi", token: token).ConfigureAwait(false)).Success, "Bedrock Llama chat should succeed.");
            bedrock.Defaults.Completion.Model = "us.anthropic.claude-sonnet-4-20250514-v1:0";
            SharedAssert.True((await bedrock.ChatAsync("hi", token: token).ConfigureAwait(false)).Success, "Bedrock Anthropic profile chat should succeed.");
            CompletionWire bedrockWire = WireFor(bedrock);
            using JsonDocument llama = JsonDocument.Parse(bedrockServer.RequestBodies[0]);
            using JsonDocument claude = JsonDocument.Parse(bedrockServer.RequestBodies[1]);
            SharedAssert.Equal<int?>(null, bedrockWire.MaxTokens(llama.RootElement), "Bedrock non-Anthropic models get no limit.");
            SharedAssert.Equal<int?>(ProviderClientBase.DefaultMaxTokens, bedrockWire.MaxTokens(claude.RootElement), "Bedrock Anthropic inference profiles still get a limit.");
        }

        #endregion

        #region Transport

        private static async Task RunSharedHttpClientAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using HttpClient shared = new HttpClient();

            OpenAiCompatibleClient openAiA = new OpenAiCompatibleClient(server.Endpoint, "key-a", new ClientOptions { HttpClient = shared }) { Defaults = { Completion = { Model = "test-model" } } };
            OpenAiCompatibleClient openAiB = new OpenAiCompatibleClient(server.Endpoint, "key-b", new ClientOptions { HttpClient = shared }) { Defaults = { Completion = { Model = "test-model" } } };
            AnthropicClient anthropic = new AnthropicClient("key-c", server.Endpoint, new ClientOptions { HttpClient = shared }) { Defaults = { Completion = { Model = "test-model" } } };
            GeminiClient gemini = new GeminiClient("key-d", server.Endpoint, new ClientOptions { HttpClient = shared }) { Defaults = { Completion = { Model = "test-model" } } };
            OllamaClient ollama = new OllamaClient(server.Endpoint, "key-e", new ClientOptions { HttpClient = shared }) { Defaults = { Completion = { Model = "test-model" } } };
            TypeSafeClient typeSafe = new TypeSafeClient("key-f", server.Endpoint, new ClientOptions { HttpClient = shared });
            OpenAiCompatibleClient embedding = new OpenAiCompatibleClient(server.Endpoint, "key-g", new ClientOptions { HttpClient = shared }) { Defaults = { Completion = { Model = "gpt-4o-mini" }, Embedding = { Model = "text-embedding-3-small" } } };
            List<HttpClientBase> clients = new List<HttpClientBase> { openAiA, openAiB, anthropic, gemini, ollama, typeSafe, embedding };

            await openAiA.ChatAsync("hi", token: token).ConfigureAwait(false);
            await openAiB.ChatAsync("hi", token: token).ConfigureAwait(false);
            await anthropic.ChatAsync("hi", token: token).ConfigureAwait(false);
            await gemini.ChatAsync("hi", token: token).ConfigureAwait(false);
            await ollama.ChatAsync("hi", token: token).ConfigureAwait(false);
            await typeSafe.DecideAsync(new DecisionRequest { State = "x", Questions = { DecisionQuestion.Binary("b", "Is it?") } }, null, token).ConfigureAwait(false);
            await embedding.EmbedAsync("hi", null, token).ConfigureAwait(false);

            List<Dictionary<string, string>> headers = server.RequestHeaders;
            SharedAssert.Equal(7, headers.Count, "Each client should have sent one request.");
            SharedAssert.Equal("Bearer key-a", Header(headers[0], "Authorization"), "The first OpenAI client should send its own key.");
            SharedAssert.Equal("Bearer key-b", Header(headers[1], "Authorization"), "The second OpenAI client should send its own key, not the first client's.");
            SharedAssert.Equal("key-c", Header(headers[2], "x-api-key"), "Anthropic should send its key as x-api-key.");
            SharedAssert.True(Header(headers[2], "Authorization") == null, "Anthropic should not inherit another client's Authorization header.");
            SharedAssert.Equal("key-d", Header(headers[3], "x-goog-api-key"), "Gemini should send its key as x-goog-api-key.");
            SharedAssert.True(Header(headers[3], "Authorization") == null, "Gemini should not inherit another client's Authorization header.");
            SharedAssert.Equal("Bearer key-e", Header(headers[4], "Authorization"), "Ollama should send its own key.");
            SharedAssert.Equal("Bearer key-f", Header(headers[5], "Authorization"), "TypeSafe should send its own key.");
            SharedAssert.Equal("Bearer key-g", Header(headers[6], "Authorization"), "The embedding client should send its own key.");
            SharedAssert.True(Header(headers[6], "x-api-key") == null && Header(headers[6], "x-goog-api-key") == null, "No client's provider headers should leak into another client's requests.");

            SharedAssert.Equal(0, shared.DefaultRequestHeaders.Count(), "Clients should never modify the shared HttpClient's DefaultRequestHeaders.");

            LocalClients.DisposeAll(clients);
            using HttpResponseMessage after = await shared.GetAsync(server.Endpoint + "/api/tags", token).ConfigureAwait(false);
            SharedAssert.True(after.IsSuccessStatusCode, "Disposing clients should not dispose a caller-supplied HttpClient.");
        }

        private static async Task RunGeminiKeyHeaderAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            const string key = "gemini-secret-key";

            using (GeminiClient chat = new GeminiClient(key, server.Endpoint) { Defaults = { Completion = { Model = "test-model" } } })
            using (GeminiClient embed = new GeminiClient(key, server.Endpoint) { Defaults = { Embedding = { Model = "test-embed" } } })
            using (GeminiClient models = new GeminiClient(key, server.Endpoint))
            {
                SharedAssert.True((await chat.ChatAsync("hi", token: token).ConfigureAwait(false)).Success, "Gemini chat should succeed.");
                ChatStreamingResponse stream = await chat.ChatStreamingAsync("hi", token: token).ConfigureAwait(false);
                await foreach (ChatStreamingChunk _ in stream.Chunks.WithCancellation(token).ConfigureAwait(false)) { }
                SharedAssert.True((await chat.GenerateAsync("hi", token: token).ConfigureAwait(false)).Success, "Gemini generate should succeed.");
                SharedAssert.True((await embed.EmbedAsync("hi", null, token).ConfigureAwait(false)).Success, "Gemini single embed should succeed.");
                SharedAssert.True((await embed.EmbedAsync(new List<string> { "a", "b" }, null, token).ConfigureAwait(false)).Success, "Gemini batch embed should succeed.");
                await foreach (ModelInformation _ in models.ListModelsAsync(token).ConfigureAwait(false)) { }
                SharedAssert.NotNull(await models.GetModelInformationAsync("test-model", token).ConfigureAwait(false), "Gemini model information should be found.");
                SharedAssert.True(await chat.ValidateConnectivityAsync(token).ConfigureAwait(false), "Gemini connectivity should succeed with the header key.");

                foreach (HttpClientBase client in new HttpClientBase[] { chat, embed, models })
                {
                    foreach (CallDetail detail in client.CallDetails)
                    {
                        SharedAssert.False((detail.Url ?? string.Empty).Contains(key, StringComparison.Ordinal), "A call detail URL should never contain the API key: " + detail.Url);
                    }
                }
            }

            List<string> urls = server.RequestUrls;
            List<Dictionary<string, string>> headers = server.RequestHeaders;
            SharedAssert.True(urls.Count >= 8, "Every Gemini operation should have reached the server.");
            for (int i = 0; i < urls.Count; i++)
            {
                SharedAssert.False(urls[i].Contains("key=", StringComparison.OrdinalIgnoreCase), "Gemini URLs should never carry the key: " + urls[i]);
                SharedAssert.Equal(key, Header(headers[i], "x-goog-api-key"), "Every Gemini request should carry the key header: " + urls[i]);
            }
        }

        private static async Task RunConnectivitySuccessAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            List<ProviderClientBase> clients = LocalClients.All(server.Endpoint);
            try
            {
                foreach (HttpClientBase client in clients)
                {
                    SharedAssert.True(await client.ValidateConnectivityAsync(token).ConfigureAwait(false), client.GetType().Name + " should report a reachable server.");
                }
            }
            finally
            {
                LocalClients.DisposeAll(clients);
            }
        }

        private static async Task RunConnectivityFailureAsync(CancellationToken token)
        {
            LocalFakeHandler unauthorized = LocalFakeHandler.Always(HttpStatusCode.Unauthorized, "{\"error\":{\"message\":\"bad key\"}}");
            using HttpClient http = new HttpClient(unauthorized);
            List<ProviderClientBase> rejected = LocalClients.All("http://polyprompt.test", http);
            List<ProviderClientBase> unreachable = LocalClients.All("http://127.0.0.1:1");
            try
            {
                foreach (HttpClientBase client in rejected)
                {
                    SharedAssert.False(await client.ValidateConnectivityAsync(token).ConfigureAwait(false), client.GetType().Name + " should report failure on HTTP 401.");
                }

                SharedAssert.True(unauthorized.Count >= rejected.Count, "Every client should have probed the server.");

                // Refused connections are slow on some platforms, so probe the unreachable endpoint concurrently.
                bool[] results = await Task.WhenAll(unreachable.Select(client => client.ValidateConnectivityAsync(token))).ConfigureAwait(false);
                for (int i = 0; i < unreachable.Count; i++)
                {
                    SharedAssert.False(results[i], unreachable[i].GetType().Name + " should report failure on an unreachable endpoint.");
                }
            }
            finally
            {
                LocalClients.DisposeAll(rejected);
                LocalClients.DisposeAll(unreachable);
            }
        }

        private static async Task RunConnectivityCancellationAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using CancellationTokenSource cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            List<ProviderClientBase> clients = LocalClients.All(server.Endpoint);
            try
            {
                foreach (HttpClientBase client in clients)
                {
                    await SharedAssert.ThrowsAsync<OperationCanceledException>(() => client.ValidateConnectivityAsync(cancelled.Token), client.GetType().Name + " should rethrow caller cancellation.").ConfigureAwait(false);
                }
            }
            finally
            {
                LocalClients.DisposeAll(clients);
            }
        }

        #endregion

        #region Helpers

        private static string? Header(Dictionary<string, string> headers, string name)
        {
            return headers.TryGetValue(name, out string? value) ? value : null;
        }

        private static JsonElement? At(JsonElement root, params string[] path)
        {
            JsonElement current = root;
            foreach (string name in path)
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out current)) return null;
            }

            return current;
        }

        private static int? GetInt(JsonElement root, params string[] path)
        {
            JsonElement? value = At(root, path);
            return value?.ValueKind == JsonValueKind.Number ? value.Value.GetInt32() : null;
        }

        private static double? GetDouble(JsonElement root, params string[] path)
        {
            JsonElement? value = At(root, path);
            return value?.ValueKind == JsonValueKind.Number ? value.Value.GetDouble() : null;
        }

        private static string? GetString(JsonElement root, params string[] path)
        {
            JsonElement? value = At(root, path);
            return value?.ValueKind == JsonValueKind.String ? value.Value.GetString() : null;
        }

        #endregion
    }
}
