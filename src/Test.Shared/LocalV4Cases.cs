namespace Test.Shared
{
    using System.Diagnostics;
    using System.Net;
    using System.Reflection;
    using System.Text.Json;
    using PolyPrompt.Auth;
    using PolyPrompt.Clients;
    using PolyPrompt.Helpers;
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using Touchstone.Core;
    using N = PolyPrompt.Telemetry.PolyPromptTelemetryNames;

    /// <summary>
    /// PolyPrompt 4.0 behavior that holds across providers: the capability contract, constructors and ClientOptions, the
    /// options-type rule, extensible values, reasoning effort mapping and AppliedReasoningEffort, citations per provider,
    /// RawResponse, the continuation envelope, Azure's v1 routes, and telemetry for unsupported calls.
    /// </summary>
    internal static class LocalV4Cases
    {
        public static IEnumerable<TestCaseDescriptor> Create(string suite)
        {
            (string, string, Func<CancellationToken, Task>)[] cases =
            {
                ("v4_unsupported_capabilities_throw", "Every unsupported operation throws CapabilityNotSupportedException synchronously and sends nothing", UnsupportedAsync),
                ("v4_supported_capabilities_overridden", "Every supported capability is implemented by the client (its core members are overridden)", SupportedOverriddenAsync),
                ("v4_supports", "Supports checks every requested flag", SupportsAsync),
                ("v4_constructor_validation", "Constructors reject blank, whitespace, and URL-shaped keys and non-URL endpoints with guidance", ConstructorsAsync),
                ("v4_client_options", "ClientOptions fields are applied, and an injected HttpClient is never disposed", ClientOptionsAsync),
                ("v4_options_type_rule", "Another provider's options type throws for every operation; base options are accepted", OptionsTypeAsync),
                ("v4_extensible_values", "Extensible values normalize, compare, round-trip as JSON, and are validated per provider", ExtensibleValuesAsync),
                ("v4_effort_mapping", "Every provider maps every reasoning effort level and reports AppliedReasoningEffort", EffortMappingAsync),
                ("v4_citations_providers", "Citations normalize for Anthropic, Gemini, Vertex AI, Cohere, Bedrock, Chat Completions, and none for Ollama", CitationsAsync),
                ("v4_citation_offsets", "Gemini byte offsets convert to UTF-16 indices over non-ASCII and non-BMP text", CitationOffsetsAsync),
                ("v4_raw_response", "RawResponse holds the provider JSON for every provider and capability, and never enters call details", RawResponseAsync),
                ("v4_state_envelope", "Continuation state is the 4.0 envelope; 3.x envelopes and xAI arrays are still read; foreign state is ignored", StateEnvelopeAsync),
                ("v4_azure_v1", "Azure uses /openai/v1 routes, api-key or Entra bearer, the proxy hint, cancel, and the compatible-client route", AzureAsync),
                ("v4_telemetry", "Unsupported calls record no telemetry; the applied reasoning effort is a span attribute", TelemetryAsync),
                ("v4_default_models", "Clients without a default model throw a guiding InvalidOperationException before any request", NoDefaultModelAsync),
            };
            foreach (var (id, name, run) in cases) yield return new TestCaseDescriptor(suite, id, name, run, new[] { "local", "v4" });
        }

        #region Capabilities

        private static readonly (Capability Capability, string Operation, Func<ProviderClientBase, Task> Call)[] _Operations =
        {
            (Capability.Chat, "ChatAsync", c => c.ChatAsync("x")),
            (Capability.Chat, "ChatStreamingAsync", c => c.ChatStreamingAsync("x")),
            (Capability.ToolChat, "ToolChatAsync", c => c.ToolChatAsync(new ToolChatRequest { Messages = { ChatMessage.User("x") } })),
            (Capability.ToolChat, "ToolChatStreamingAsync", c => c.ToolChatStreamingAsync(new ToolChatRequest { Messages = { ChatMessage.User("x") } })),
            (Capability.Generate, "GenerateAsync", c => c.GenerateAsync("x")),
            (Capability.Generate, "GenerateStreamingAsync", c => c.GenerateStreamingAsync("x")),
            (Capability.Embed, "EmbedAsync", c => c.EmbedAsync("x")),
            (Capability.Embed, "EmbedAsync", c => c.EmbedAsync(new List<string> { "x" })),
            (Capability.SparseEmbed, "EmbedSparseAsync", c => c.EmbedSparseAsync("x")),
            (Capability.SparseEmbed, "EmbedSparseAsync", c => c.EmbedSparseAsync(new List<string> { "x" })),
            (Capability.Rerank, "RerankAsync", c => c.RerankAsync("q", new List<string> { "d" })),
            (Capability.Classify, "ClassifyAsync", c => c.ClassifyAsync("x")),
            (Capability.Classify, "ClassifyAsync", c => c.ClassifyAsync(new List<string> { "x" })),
            (Capability.Decide, "DecideAsync", c => c.DecideAsync(new DecisionRequest { State = "s", Questions = { DecisionQuestion.Binary("b", "Is it?") } })),
            (Capability.Decide, "DecideAsync", c => c.DecideAsync(new List<DecisionRequest> { new DecisionRequest { State = "s", Questions = { DecisionQuestion.Binary("b", "Is it?") } } })),
            (Capability.Models, "ListModelsAsync", c => { c.ListModelsAsync(); return Task.CompletedTask; }),
            (Capability.Models, "ModelExistsAsync", c => c.ModelExistsAsync("m")),
            (Capability.Models, "GetModelInformationAsync", c => c.GetModelInformationAsync("m")),
        };

        private static Task UnsupportedAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            List<ProviderClientBase> clients = LocalClients.All(server.Endpoint);
            clients.Add(new OllamaCloudClient("k", server.Endpoint));
            try
            {
                int checkedCalls = 0;
                foreach (ProviderClientBase client in clients)
                {
                    foreach (var (capability, operation, call) in _Operations)
                    {
                        if (client.Supports(capability)) continue;
                        CapabilityNotSupportedException? caught = null;
                        Task? returned = null;
                        try { returned = call(client); }
                        catch (CapabilityNotSupportedException ex) { caught = ex; }
                        SharedAssert.True(returned == null, client.GetType().Name + "." + operation + " should throw synchronously, not return a task.");
                        SharedAssert.NotNull(caught, client.GetType().Name + "." + operation + " should throw CapabilityNotSupportedException.");
                        SharedAssert.Equal(client.ProviderName, caught!.Provider, "Provider.");
                        SharedAssert.Equal(capability, caught.Capability, "Capability.");
                        SharedAssert.Equal(operation, caught.Operation, "Operation.");
                        SharedAssert.True(caught.Message.Contains("#capability-matrix", StringComparison.Ordinal), "The message links the matrix.");
                        SharedAssert.True(caught is NotSupportedException, "It is a NotSupportedException.");
                        checkedCalls++;
                    }
                }
                SharedAssert.True(checkedCalls > 80, "Unsupported operations were exercised (" + checkedCalls + ").");
                SharedAssert.Equal(0, server.RequestPaths.Count, "No unsupported call reaches the server.");
                foreach (ProviderClientBase client in clients) SharedAssert.Equal(0, client.CallDetails.Count, client.GetType().Name + " records no call detail for unsupported calls.");
            }
            finally { LocalClients.DisposeAll(clients); }
            return Task.CompletedTask;
        }

        private static readonly Dictionary<Capability, string[]> _CoreMembers = new()
        {
            [Capability.Chat] = new[] { "ChatCoreAsync", "ChatStreamingCoreAsync" },
            [Capability.ToolChat] = new[] { "ToolChatCoreAsync", "ToolChatStreamingCoreAsync" },
            [Capability.Generate] = new[] { "GenerateCoreAsync", "GenerateStreamingCoreAsync" },
            [Capability.Embed] = new[] { "EmbedCoreAsync" },
            [Capability.SparseEmbed] = new[] { "EmbedSparseCoreAsync" },
            [Capability.Rerank] = new[] { "RerankCoreAsync" },
            [Capability.Classify] = new[] { "ClassifyCoreAsync" },
            [Capability.Decide] = new[] { "DecideCoreAsync" },
            [Capability.Models] = new[] { "ListModelsCoreAsync", "GetModelInformationCoreAsync" },
        };

        private static Task SupportedOverriddenAsync(CancellationToken token)
        {
            foreach (Type type in typeof(ProviderClientBase).Assembly.GetExportedTypes().Where(t => !t.IsAbstract && typeof(ProviderClientBase).IsSubclassOf(typeof(HttpClientBase)) && typeof(ProviderClientBase).IsAssignableFrom(t)))
            {
                using ProviderClientBase client = Construct(type);
                foreach (var pair in _CoreMembers)
                {
                    foreach (string member in pair.Value)
                    {
                        MethodInfo method = type.GetMethod(member, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
                        bool overridden = method.DeclaringType != typeof(ProviderClientBase);
                        SharedAssert.Equal(client.Supports(pair.Key), overridden, type.Name + "." + member + " is overridden exactly when " + pair.Key + " is supported.");
                    }
                }
            }
            return Task.CompletedTask;
        }

        internal static ProviderClientBase Construct(Type type, string endpoint = "http://127.0.0.1:1")
        {
            return type.Name switch
            {
                nameof(OllamaClient) => new OllamaClient(endpoint),
                nameof(OllamaCloudClient) => new OllamaCloudClient("k", endpoint),
                nameof(OpenAiCompatibleClient) => new OpenAiCompatibleClient(endpoint),
                nameof(OpenAiClient) => new OpenAiClient("k", endpoint),
                nameof(AzureOpenAiClient) => new AzureOpenAiClient(endpoint, "k"),
                nameof(XAiClient) => new XAiClient("k", endpoint),
                nameof(AnthropicClient) => new AnthropicClient("k", endpoint),
                nameof(GeminiClient) => new GeminiClient("k", endpoint),
                nameof(VertexAiClient) => new VertexAiClient("p", "us-central1", new StaticTokenCredential("t"), endpoint),
                nameof(BedrockClient) => new BedrockClient("us-east-1", LocalClients.AwsCredential(), endpoint),
                nameof(CohereClient) => new CohereClient("k", endpoint),
                nameof(VoyageAiClient) => new VoyageAiClient("k", endpoint),
                nameof(TeiClient) => new TeiClient(endpoint),
                nameof(TypeSafeClient) => new TypeSafeClient("k", endpoint),
                _ => throw new TestFailureException("No constructor for " + type.Name),
            };
        }

        private static Task SupportsAsync(CancellationToken token)
        {
            using AnthropicClient claude = new("k");
            SharedAssert.True(claude.Supports(Capability.Chat | Capability.Models), "All requested flags supported.");
            SharedAssert.False(claude.Supports(Capability.Chat | Capability.Embed), "One unsupported flag fails.");
            SharedAssert.True(claude.Supports(Capability.None), "None is trivially supported.");
            return Task.CompletedTask;
        }

        #endregion

        #region Construction

        private static async Task ConstructorsAsync(CancellationToken token)
        {
            Func<string, ProviderClientBase>[] keyed =
            {
                k => new OpenAiClient(k), k => new XAiClient(k), k => new AnthropicClient(k), k => new GeminiClient(k), k => new OllamaCloudClient(k),
                k => new CohereClient(k), k => new VoyageAiClient(k), k => new TypeSafeClient(k), k => new AzureOpenAiClient("https://r.openai.azure.com", k),
            };
            foreach (Func<string, ProviderClientBase> make in keyed)
            {
                foreach (string? bad in new string?[] { null, "", "  ", "two words", "https://api.example.com/v1" })
                {
                    bool threw = false;
                    try { make(bad!).Dispose(); } catch (ArgumentException) { threw = true; }
                    SharedAssert.True(threw, "Key '" + bad + "' should throw.");
                }
            }
            ArgumentException? hint = null;
            try { new OpenAiClient("http://localhost:11434/v1").Dispose(); } catch (ArgumentException ex) { hint = ex; }
            SharedAssert.True(hint!.Message.Contains("use OpenAiCompatibleClient", StringComparison.Ordinal), "OpenAiClient URL hint: " + hint.Message);
            foreach (string bad in new[] { "not a url", "ftp://host", "/relative" })
            {
                await SharedAssert.ThrowsAsync<ArgumentException>(() => { new OpenAiCompatibleClient(bad).Dispose(); return Task.CompletedTask; }, "Endpoint '" + bad + "' should throw.").ConfigureAwait(false);
                await SharedAssert.ThrowsAsync<ArgumentException>(() => { new AnthropicClient("k", bad).Dispose(); return Task.CompletedTask; }, "Endpoint '" + bad + "' should throw.").ConfigureAwait(false);
            }
            using (OllamaClient ollama = new("http://localhost:11434/v1")) SharedAssert.Equal("http://localhost:11434", ollama.Endpoint, "Ollama strips a trailing /v1.");
            using (OllamaClient local = new()) SharedAssert.Equal("http://localhost:11434", local.Endpoint, "Ollama default endpoint.");
            using (OpenAiCompatibleClient keyless = new("http://localhost:8000/v1")) SharedAssert.True(keyless.ApiKey == null, "OpenAiCompatibleClient key is optional.");
            using (TeiClient tei = new("http://localhost:8080")) SharedAssert.True(tei.ApiKey == null, "TEI key is optional.");
            using (OllamaCloudClient cloud = new("k")) SharedAssert.Equal("https://ollama.com:443", cloud.Endpoint, "Ollama Cloud default endpoint.");
            using (BedrockClient bedrock = new("eu-west-1", LocalClients.AwsCredential())) SharedAssert.Equal("https://bedrock-runtime.eu-west-1.amazonaws.com", bedrock.Endpoint, "Bedrock regional endpoint.");
            using (VertexAiClient vertex = new("p", "europe-west4", new StaticTokenCredential("t"))) SharedAssert.Equal("https://europe-west4-aiplatform.googleapis.com", vertex.Endpoint, "Vertex regional endpoint.");
        }

        private static async Task ClientOptionsAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using HttpClient shared = new HttpClient();
            ClientOptions options = new ClientOptions { HttpClient = shared, TimeoutMs = 4321, CallDetailCapture = CallDetailCapture.Full, RedactCredentials = false, Logging = new SyslogLogging.LoggingModule() };
            using (OllamaClient client = new(server.Endpoint, "secret", options) { Defaults = { Completion = { Model = "m" } } })
            {
                SharedAssert.Equal(4321, client.TimeoutMs, "TimeoutMs applied.");
                SharedAssert.Equal(CallDetailCapture.Full, client.CallDetailCapture, "CallDetailCapture applied.");
                SharedAssert.False(client.RedactCredentials, "RedactCredentials applied.");
                SharedAssert.True((await client.ChatAsync("hi", token: token).ConfigureAwait(false)).Success, "Chat through the shared HttpClient.");
                SharedAssert.Equal("Bearer secret", client.CallDetails.Last().RequestHeaders!["Authorization"], "Unredacted when RedactCredentials = false.");
                SharedAssert.NotNull(client.CallDetails.Last().RequestBody, "Full capture keeps bodies.");
            }
            using HttpResponseMessage after = await shared.GetAsync(server.Endpoint + "/api/tags", token).ConfigureAwait(false);
            SharedAssert.True(after.IsSuccessStatusCode, "The injected HttpClient was not disposed.");
            using (CodexClient harness = new("codex", new ClientOptions { TimeoutMs = 999, CallDetailCapture = CallDetailCapture.None }))
            {
                SharedAssert.Equal(999, harness.TimeoutMs, "Harness clients take ClientOptions.");
                SharedAssert.Equal(CallDetailCapture.None, harness.CallDetailCapture, "Harness capture applied.");
            }
            using (OpenAiCompatibleClient placed = new(server.Endpoint, "k", new OpenAiCompatibleClientOptions { AuthPlacement = AuthPlacement.Query("key") }))
                SharedAssert.Equal(AuthPlacementKind.Query, placed.AuthPlacement.Kind, "AuthPlacement applied.");
        }

        #endregion

        #region Options

        private static async Task OptionsTypeAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiCompatibleClient openAi = new(server.Endpoint) { Defaults = { Completion = { Model = "m" }, Embedding = { Model = "e" } } };
            using CohereClient cohere = new(LocalExtendedRoutes.CohereTestKey, server.Endpoint);
            using TypeSafeClient typeSafe = new("k", server.Endpoint);
            ArgumentException? ex = null;
            try { await openAi.ChatAsync("x", new XAiCompletionOptions()).ConfigureAwait(false); } catch (ArgumentException e) { ex = e; }
            SharedAssert.True(ex!.Message.StartsWith("XAiCompletionOptions cannot be used with OpenAiCompatibleClient. Use OpenAiCompatibleCompletionOptions or CompletionOptions.", StringComparison.Ordinal), "Message: " + ex.Message);
            await SharedAssert.ThrowsAsync<ArgumentException>(() => openAi.ToolChatAsync(new ToolChatRequest { Messages = { ChatMessage.User("x") }, Options = new AnthropicCompletionOptions() }), "Tool chat options type.").ConfigureAwait(false);
            await SharedAssert.ThrowsAsync<ArgumentException>(() => openAi.GenerateStreamingAsync("x", new GeminiCompletionOptions()), "Generation options type.").ConfigureAwait(false);
            await SharedAssert.ThrowsAsync<ArgumentException>(() => openAi.EmbedAsync("x", new VoyageAiEmbeddingOptions()), "Embedding options type.").ConfigureAwait(false);
            await SharedAssert.ThrowsAsync<ArgumentException>(() => cohere.RerankAsync("q", new List<string> { "d" }, new VoyageAiRerankOptions()), "Rerank options type.").ConfigureAwait(false);
            await SharedAssert.ThrowsAsync<ArgumentException>(() => cohere.ClassifyAsync("x", new TeiClassificationOptions()), "Classification options type.").ConfigureAwait(false);
            await SharedAssert.ThrowsAsync<ArgumentException>(() => cohere.EmbedAsync("x", new OpenAiEmbeddingOptions()), "Cohere embedding options type.").ConfigureAwait(false);
            SharedAssert.Equal(0, server.RequestPaths.Count, "Rejected options send nothing.");
            SharedAssert.True((await openAi.ChatAsync("x", new CompletionOptions { MaxTokens = 5 }, token).ConfigureAwait(false)).Success, "Base completion options are accepted.");
            SharedAssert.True((await openAi.ChatAsync("x", new OpenAiCompatibleCompletionOptions { Seed = 1 }, token).ConfigureAwait(false)).Success, "Own options are accepted.");
            SharedAssert.True((await openAi.EmbedAsync("x", new EmbeddingOptions(), token).ConfigureAwait(false)).Success, "Base embedding options are accepted.");
            SharedAssert.True((await cohere.RerankAsync("q", new List<string> { "d" }, new RerankOptions(), token).ConfigureAwait(false)).Success, "Base rerank options are accepted.");
            using OpenAiClient responses = new(LocalOpenAiResponsesRoutes.Key, server.Endpoint);
            SharedAssert.True((await responses.ChatAsync("x", new ResponsesApiCompletionOptions(), token).ConfigureAwait(false)).Success, "A type between base and own is accepted.");
        }

        private static async Task ExtensibleValuesAsync(CancellationToken token)
        {
            ServiceTier tier = " FLEX ";
            SharedAssert.Equal("flex", tier.Value, "Normalized."); SharedAssert.True(tier == ServiceTier.Flex, "Equal to the known value.");
            SharedAssert.True(tier.IsKnown, "Known."); SharedAssert.False(new ServiceTier("future").IsKnown, "Unknown.");
            SharedAssert.Equal(6, ServiceTier.Known.Count, "Six known tiers."); SharedAssert.Equal(7, ReasoningEffortValue.Known.Count, "Seven known efforts.");
            SharedAssert.Equal(3, ReasoningSummary.Known.Count, "Three known summaries.");
            await SharedAssert.ThrowsAsync<ArgumentException>(() => { _ = new ReasoningSummary(" "); return Task.CompletedTask; }, "Blank values throw.").ConfigureAwait(false);
            string json = JsonSerializer.Serialize(new OpenAiCompletionOptions { ServiceTier = "priority", ReasoningSummary = ReasoningSummary.Auto, ReasoningEffortValue = "xhigh" }, PolyPromptJsonContext.Default.OpenAiCompletionOptions);
            SharedAssert.True(json.Contains("\"ServiceTier\":\"priority\"", StringComparison.Ordinal) && json.Contains("\"ReasoningEffortValue\":\"xhigh\"", StringComparison.Ordinal), "Written as strings: " + json);
            OpenAiCompletionOptions back = JsonSerializer.Deserialize(json, PolyPromptJsonContext.Default.OpenAiCompletionOptions)!;
            SharedAssert.True(back.ServiceTier == ServiceTier.Priority && back.ReasoningSummary == ReasoningSummary.Auto, "Round trip.");

            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using XAiClient xai = new(LocalXAiRoutes.Key, server.Endpoint);
            await SharedAssert.ThrowsAsync<ArgumentException>(() => xai.ChatAsync("x", new XAiCompletionOptions { ServiceTier = ServiceTier.Flex }), "xAI rejects a known tier it does not accept.").ConfigureAwait(false);
            await SharedAssert.ThrowsAsync<ArgumentException>(() => xai.ChatAsync("x", new XAiCompletionOptions { ReasoningEffortValue = ReasoningEffortValue.Max }), "xAI rejects max as a wire value.").ConfigureAwait(false);
            SharedAssert.True((await xai.ChatAsync("x", new XAiCompletionOptions { ServiceTier = new ServiceTier("future-tier") }, token).ConfigureAwait(false)).Success, "Unknown tiers pass through.");
            SharedAssert.True(server.RequestBodies.Last().Contains("\"service_tier\":\"future-tier\"", StringComparison.Ordinal), "Sent unchanged.");
            using OpenAiClient openAi = new(LocalOpenAiResponsesRoutes.Key, server.Endpoint);
            foreach (ServiceTier known in ServiceTier.Known)
            {
                await openAi.ChatAsync("x", new OpenAiCompletionOptions { ServiceTier = known }, token).ConfigureAwait(false);
                SharedAssert.True(server.RequestBodies.Last().Contains("\"service_tier\":\"" + known.Value + "\"", StringComparison.Ordinal), "OpenAI accepts " + known + ".");
            }
        }

        #endregion

        #region Reasoning

        private static readonly ReasoningEffortLevel[] _Levels = { ReasoningEffortLevel.None, ReasoningEffortLevel.Minimal, ReasoningEffortLevel.Low, ReasoningEffortLevel.Medium, ReasoningEffortLevel.High, ReasoningEffortLevel.XHigh, ReasoningEffortLevel.Max };

        private static readonly Dictionary<string, string?[]> _ExpectedEffort = new()
        {
            [nameof(OllamaClient)] = new[] { "false", "false", "low", "medium", "high", "high", "high" },
            [nameof(OpenAiCompatibleClient)] = new[] { null, "minimal", "low", "medium", "high", "high", "high" },
            [nameof(OpenAiClient)] = new[] { "none", "minimal", "low", "medium", "high", "xhigh", "max" },
            [nameof(AzureOpenAiClient)] = new[] { "none", "minimal", "low", "medium", "high", "xhigh", "max" },
            [nameof(XAiClient)] = new[] { null, "low", "low", "medium", "high", "xhigh", "xhigh" },
            [nameof(AnthropicClient)] = new[] { null, "low", "low", "medium", "high", "xhigh", "max" },
            [nameof(GeminiClient)] = new[] { "0", "0", "1024", "8192", "-1", "-1", "-1" },
            [nameof(VertexAiClient)] = new[] { "0", "0", "1024", "8192", "-1", "-1", "-1" },
            [nameof(CohereClient)] = new[] { "0", "0", "1024", "4096", "16384", "16384", "16384" },
            [nameof(BedrockClient)] = new[] { null, null, "1024", "4096", "16384", "16384", "16384" },
        };

        private static async Task EffortMappingAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            List<ProviderClientBase> clients = LocalClients.Completion(server.Endpoint);
            try
            {
                foreach (ProviderClientBase client in clients)
                {
                    string name = client.GetType().Name;
                    string?[] expected = _ExpectedEffort[name];
                    for (int i = 0; i < _Levels.Length; i++)
                    {
                        ChatResponse r = await client.ChatAsync("effort", new CompletionOptions { ReasoningEffort = _Levels[i] }, token).ConfigureAwait(false);
                        SharedAssert.True(r.Success, name + " " + _Levels[i] + ": " + r.Error);
                        SharedAssert.Equal(expected[i], r.AppliedReasoningEffort, name + " " + _Levels[i] + " applied effort.");
                    }
                    ChatResponse plain = await client.ChatAsync("effort", token: token).ConfigureAwait(false);
                    SharedAssert.True(plain.AppliedReasoningEffort == null, name + " with no effort reports none.");
                }
            }
            finally { LocalClients.DisposeAll(clients); }
            using AnthropicClient anthropic = new(LocalClients.TestKey, server.Endpoint);
            await anthropic.ChatAsync("x", new CompletionOptions { ReasoningEffort = ReasoningEffort.None }, token).ConfigureAwait(false);
            string body = server.RequestBodies.Last();
            SharedAssert.False(body.Contains("output_config", StringComparison.Ordinal) || body.Contains("\"thinking\"", StringComparison.Ordinal), "Anthropic None sends neither effort nor thinking.");
            await anthropic.ChatAsync("x", new CompletionOptions { ReasoningEffort = ReasoningEffort.Max }, token).ConfigureAwait(false);
            SharedAssert.True(server.RequestBodies.Last().Contains("\"effort\":\"max\"", StringComparison.Ordinal), "Anthropic Max sends max.");
        }

        #endregion

        #region Results

        private const string AnthropicCited = "{\"id\":\"msg_1\",\"type\":\"message\",\"role\":\"assistant\",\"model\":\"m\",\"content\":[{\"type\":\"text\",\"text\":\"Intro. \"},{\"type\":\"text\",\"text\":\"Caf\\u00e9 \\ud83d\\ude00 fact.\",\"citations\":[{\"type\":\"char_location\",\"cited_text\":\"fact\",\"document_index\":0,\"document_title\":\"Doc\",\"start_char_index\":3,\"end_char_index\":7},{\"type\":\"page_location\",\"cited_text\":\"p\",\"document_index\":1,\"document_title\":\"Pdf\",\"start_page_number\":2,\"end_page_number\":3},{\"type\":\"web_search_result_location\",\"url\":\"https://e.com\",\"title\":\"E\",\"cited_text\":\"x\",\"encrypted_index\":\"abc\"}]}],\"stop_reason\":\"end_turn\",\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}";
        private const string GeminiCited = "{\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[{\"text\":\"Caf\\u00e9 \\u6771\\u4eac \\ud83d\\ude00 see.\"}]},\"finishReason\":\"STOP\",\"groundingMetadata\":{\"groundingChunks\":[{\"web\":{\"uri\":\"https://g.com\",\"title\":\"G\"}}],\"groundingSupports\":[{\"segment\":{\"startIndex\":18,\"endIndex\":21,\"text\":\"see\"},\"groundingChunkIndices\":[0]}]},\"citationMetadata\":{\"citationSources\":[{\"startIndex\":0,\"endIndex\":5,\"uri\":\"https://c.com\",\"license\":\"mit\"}]}}],\"usageMetadata\":{\"promptTokenCount\":1,\"candidatesTokenCount\":1,\"totalTokenCount\":2}}";
        private const string CohereCited = "{\"id\":\"c\",\"finish_reason\":\"COMPLETE\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"Answer here.\"}],\"citations\":[{\"start\":7,\"end\":11,\"text\":\"here\",\"sources\":[{\"type\":\"document\",\"id\":\"doc-1\",\"document\":{\"title\":\"T\"}}],\"type\":\"TEXT_CONTENT\"},{\"start\":0,\"end\":3,\"text\":\"thk\",\"sources\":[],\"type\":\"THINKING_CONTENT\"}]},\"usage\":{\"tokens\":{\"input_tokens\":1,\"output_tokens\":1}}}";
        private const string BedrockCited = "{\"output\":{\"message\":{\"role\":\"assistant\",\"content\":[{\"text\":\"Hi.\"},{\"citationsContent\":{\"content\":[{\"text\":\"cited\"}],\"citations\":[{\"title\":\"Doc\",\"sourceContent\":[{\"text\":\"src\"}],\"location\":{\"documentChar\":{\"documentIndex\":0,\"start\":1,\"end\":4}}},{\"location\":{\"web\":{\"url\":\"https://w.com\",\"domain\":\"w.com\"}}}]}}]}},\"stopReason\":\"end_turn\",\"usage\":{\"inputTokens\":1,\"outputTokens\":1,\"totalTokens\":2}}";
        private const string ChatCompletionsCited = "{\"id\":\"x\",\"object\":\"chat.completion\",\"model\":\"m\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"See ref.\",\"annotations\":[{\"type\":\"url_citation\",\"url_citation\":{\"url\":\"https://o.com\",\"title\":\"O\",\"start_index\":4,\"end_index\":7}}]},\"finish_reason\":\"stop\"}]}";

        private static async Task<ChatResponse> CannedAsync(Func<HttpClient, ProviderClientBase> make, string json, CancellationToken token)
        {
            using LocalFakeHandler handler = LocalFakeHandler.Always(HttpStatusCode.OK, json);
            using HttpClient http = new(handler);
            using ProviderClientBase client = make(http);
            ChatResponse r = await client.ChatAsync("cite", new CompletionOptions { Model = "m" }, token).ConfigureAwait(false);
            SharedAssert.True(r.Success, client.GetType().Name + ": " + r.Error);
            return r;
        }

        private static ClientOptions With(HttpClient http) => new ClientOptions { HttpClient = http };

        private static async Task CitationsAsync(CancellationToken token)
        {
            ChatResponse a = await CannedAsync(h => new AnthropicClient("k", "http://fixture", With(h)), AnthropicCited, token).ConfigureAwait(false);
            SharedAssert.Equal("Intro. Café 😀 fact.", a.Text, "Anthropic text.");
            SharedAssert.Equal(3, a.Citations.Count, "Anthropic citations.");
            SharedAssert.Equal(CitationKind.Document, a.Citations[0].Kind, "char_location is a document.");
            SharedAssert.Equal("Café 😀 fact.", a.Text!.Substring(a.Citations[0].TextStart!.Value, a.Citations[0].TextEnd!.Value - a.Citations[0].TextStart!.Value), "Span is the containing block.");
            SharedAssert.Equal("char", a.Citations[0].Source!.Unit, "Char range in Source."); SharedAssert.Equal(3, a.Citations[0].Source!.Start, "Source start.");
            SharedAssert.Equal("page", a.Citations[1].Source!.Unit, "Page range."); SharedAssert.Equal(2, a.Citations[1].Source!.Start, "Pages are 1-indexed as reported.");
            SharedAssert.Equal(CitationKind.Url, a.Citations[2].Kind, "Web search result is a URL."); SharedAssert.Equal("https://e.com", a.Citations[2].Url, "Url.");
            SharedAssert.Equal("fact", a.Citations[0].CitedText, "Cited text.");
            SharedAssert.Equal("abc", a.Citations[2].Raw.GetProperty("encrypted_index").GetString(), "Raw kept, including encrypted_index.");

            foreach (Func<HttpClient, ProviderClientBase> gemini in new Func<HttpClient, ProviderClientBase>[]
                { h => new GeminiClient("k", "http://fixture", With(h)), h => new VertexAiClient("p", "us-central1", new StaticTokenCredential("t"), "http://fixture", With(h)) })
            {
                ChatResponse g = await CannedAsync(gemini, GeminiCited, token).ConfigureAwait(false);
                SharedAssert.Equal(2, g.Citations.Count, "Grounding support plus citation source.");
                SharedAssert.Equal("see", g.Text!.Substring(g.Citations[0].TextStart!.Value, g.Citations[0].TextEnd!.Value - g.Citations[0].TextStart!.Value), "Byte offsets converted.");
                SharedAssert.Equal("https://g.com", g.Citations[0].Url, "Chunk joined."); SharedAssert.Equal("G", g.Citations[0].Title, "Title.");
                SharedAssert.Equal("Café", g.Text.Substring(g.Citations[1].TextStart!.Value, g.Citations[1].TextEnd!.Value - g.Citations[1].TextStart!.Value), "Citation source bytes converted.");
            }

            ChatResponse c = await CannedAsync(h => new CohereClient("k", "http://fixture", With(h)), CohereCited, token).ConfigureAwait(false);
            SharedAssert.Equal(1, c.Citations.Count, "Thinking-content citations are excluded.");
            SharedAssert.Equal("here", c.Text!.Substring(c.Citations[0].TextStart!.Value, c.Citations[0].TextEnd!.Value - c.Citations[0].TextStart!.Value), "Cohere span.");
            SharedAssert.Equal("doc-1", c.Citations[0].Source!.DocumentId, "Document id."); SharedAssert.Equal("T", c.Citations[0].Title, "Title.");

            ChatResponse b = await CannedAsync(h => new BedrockClient("us-east-1", LocalClients.AwsCredential(), "http://fixture", With(h)), BedrockCited, token).ConfigureAwait(false);
            SharedAssert.Equal(2, b.Citations.Count, "Bedrock citations.");
            SharedAssert.Equal("char", b.Citations[0].Source!.Unit, "documentChar."); SharedAssert.Equal(1, b.Citations[0].Source!.Start, "Start."); SharedAssert.Equal("src", b.Citations[0].CitedText, "Source content.");
            SharedAssert.True(b.Citations[0].TextStart == null, "No span in the response text.");
            SharedAssert.Equal(CitationKind.Url, b.Citations[1].Kind, "Web location."); SharedAssert.Equal("https://w.com", b.Citations[1].Url, "Url.");

            ChatResponse o = await CannedAsync(h => new OpenAiCompatibleClient("http://fixture", "k", With(h)), ChatCompletionsCited, token).ConfigureAwait(false);
            SharedAssert.Equal(1, o.Citations.Count, "Chat Completions annotation.");
            SharedAssert.Equal(4, o.Citations[0].TextStart, "start_index"); SharedAssert.Equal(7, o.Citations[0].TextEnd, "end_index kept as reported.");

            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OllamaClient ollama = new(server.Endpoint);
            ChatResponse none = await ollama.ChatAsync("hi", token: token).ConfigureAwait(false);
            SharedAssert.NotNull(none.Citations, "Never null."); SharedAssert.Equal(0, none.Citations.Count, "Ollama has no citations.");
        }

        private static async Task CitationOffsetsAsync(CancellationToken token)
        {
            string text = "a é 東 😀 z!";
            for (int start = 0; start < text.Length; start++)
            {
                if (char.IsLowSurrogate(text[start])) continue;
                int end = start + (char.IsHighSurrogate(text[start]) ? 2 : 1);
                int byteStart = System.Text.Encoding.UTF8.GetByteCount(text.Substring(0, start));
                int byteEnd = System.Text.Encoding.UTF8.GetByteCount(text.Substring(0, end));
                var root = new Dictionary<string, object>
                {
                    ["candidates"] = new object[] { new Dictionary<string, object>
                    {
                        ["content"] = new Dictionary<string, object> { ["role"] = "model", ["parts"] = new object[] { new Dictionary<string, object> { ["text"] = text } } },
                        ["finishReason"] = "STOP",
                        ["citationMetadata"] = new Dictionary<string, object> { ["citationSources"] = new object[] { new Dictionary<string, object> { ["startIndex"] = byteStart, ["endIndex"] = byteEnd, ["uri"] = "u" } } },
                    } },
                };
                ChatResponse r = await CannedAsync(h => new GeminiClient("k", "http://fixture", With(h)), new Serializer().SerializeJson(root, false), token).ConfigureAwait(false);
                SharedAssert.Equal(text.Substring(start, end - start), r.Text!.Substring(r.Citations[0].TextStart!.Value, r.Citations[0].TextEnd!.Value - r.Citations[0].TextStart!.Value), "Bytes " + byteStart + ".." + byteEnd + " map to the character at " + start + ".");
            }
        }

        private static async Task RawResponseAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            List<ProviderClientBase> clients = LocalClients.All(server.Endpoint);
            try
            {
                foreach (ProviderClientBase client in clients)
                {
                    string name = client.GetType().Name;
                    ResponseBase? r = null;
                    if (client.Supports(Capability.Chat)) r = await client.ChatAsync("hi", token: token).ConfigureAwait(false);
                    else if (client.Supports(Capability.Embed)) r = await client.EmbedAsync("hi", null, token).ConfigureAwait(false);
                    else if (client.Supports(Capability.Decide)) r = await client.DecideAsync(new DecisionRequest { State = "s", Questions = { DecisionQuestion.Binary("b", "Is it?") } }, null, token).ConfigureAwait(false);
                    SharedAssert.True(r!.Success, name + ": " + r.Error);
                    SharedAssert.NotNull(r.RawResponse, name + " RawResponse.");
                    SharedAssert.True(client.CallDetails.All(d => d.ResponseBody == null), name + " default capture keeps no bodies.");
                }
            }
            finally { LocalClients.DisposeAll(clients); }
        }

        #endregion

        #region State

        private static JsonElement J(string json) { using JsonDocument d = JsonDocument.Parse(json); return d.RootElement.Clone(); }

        private static async Task StateEnvelopeAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using AnthropicClient anthropic = new(LocalClients.TestKey, server.Endpoint);
            ToolChatRequest Make(JsonElement state) => new ToolChatRequest { Messages = { ChatMessage.User("q"), new ChatMessage { Role = "assistant", Content = "plain answer", ProviderState = state }, ChatMessage.User("next") } };
            string legacyBlocks = "[{\"type\":\"thinking\",\"thinking\":\"legacy-thought\",\"signature\":\"s\"},{\"type\":\"text\",\"text\":\"plain answer\"}]";

            await anthropic.ToolChatAsync(Make(J("{\"protocol\":\"anthropic\",\"value\":" + legacyBlocks + "}")), token).ConfigureAwait(false);
            SharedAssert.True(server.RequestBodies.Last().Contains("legacy-thought", StringComparison.Ordinal), "A 3.x envelope is still replayed.");
            await anthropic.ToolChatAsync(Make(J("{\"protocol\":\"anthropic\",\"provider\":\"anthropic\",\"scope\":\"" + server.Endpoint.TrimEnd('/') + "\",\"value\":" + legacyBlocks + "}")), token).ConfigureAwait(false);
            SharedAssert.True(server.RequestBodies.Last().Contains("legacy-thought", StringComparison.Ordinal), "A matching 4.0 envelope is replayed.");
            await anthropic.ToolChatAsync(Make(J("{\"protocol\":\"anthropic\",\"provider\":\"anthropic\",\"scope\":\"https://elsewhere\",\"value\":" + legacyBlocks + "}")), token).ConfigureAwait(false);
            SharedAssert.False(server.RequestBodies.Last().Contains("legacy-thought", StringComparison.Ordinal), "Another endpoint's state falls back to plain history.");
            await anthropic.ToolChatAsync(Make(J("{\"protocol\":\"anthropic\",\"provider\":\"aws.bedrock\",\"scope\":\"" + server.Endpoint.TrimEnd('/') + "\",\"value\":" + legacyBlocks + "}")), token).ConfigureAwait(false);
            SharedAssert.False(server.RequestBodies.Last().Contains("legacy-thought", StringComparison.Ordinal), "Another provider's state falls back to plain history.");

            using OpenAiCompatibleClient compatible = new(server.Endpoint, "k") { Defaults = { Completion = { Model = "m" } } };
            string legacyOpenAi = "{\"protocol\":\"openai\",\"scope\":\"" + server.Endpoint.TrimEnd('/') + "\",\"value\":{\"reasoning_content\":\"legacy-reasoning\"}}";
            await compatible.ToolChatAsync(Make(J(legacyOpenAi)), token).ConfigureAwait(false);
            SharedAssert.True(server.RequestBodies.Last().Contains("legacy-reasoning", StringComparison.Ordinal), "3.x Chat Completions state is read by OpenAiCompatibleClient.");
            ToolChatResponse written = await compatible.ToolChatAsync(new ToolChatRequest { Messages = { ChatMessage.User("reasoncapture") } }, token).ConfigureAwait(false);
            if (written.ProviderState is JsonElement state)
            {
                SharedAssert.Equal("chat_completions", state.GetProperty("protocol").GetString(), "4.0 writes the chat_completions protocol.");
                SharedAssert.True(state.TryGetProperty("provider", out _) && state.TryGetProperty("scope", out _), "4.0 envelope fields.");
            }

            using XAiClient xai = new(LocalXAiRoutes.Key, server.Endpoint);
            await xai.ToolChatAsync(Make(J("[{\"type\":\"reasoning\",\"encrypted_content\":\"legacy-xai\"},{\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"output_text\",\"text\":\"plain answer\"}]}]")), token).ConfigureAwait(false);
            SharedAssert.True(server.RequestBodies.Last().Contains("legacy-xai", StringComparison.Ordinal), "xAI still reads its 3.x untagged output array.");
            using OpenAiClient openAi = new(LocalOpenAiResponsesRoutes.Key, server.Endpoint);
            await openAi.ToolChatAsync(Make(J("[{\"type\":\"reasoning\",\"encrypted_content\":\"legacy-xai\"}]")), token).ConfigureAwait(false);
            SharedAssert.False(server.RequestBodies.Last().Contains("legacy-xai", StringComparison.Ordinal), "OpenAI ignores an untagged xAI array.");
            ToolChatResponse fromOpenAi = await openAi.ToolChatAsync(new ToolChatRequest { Messages = { ChatMessage.User("hi") } }, token).ConfigureAwait(false);
            await xai.ToolChatAsync(Make(fromOpenAi.ProviderState!.Value), token).ConfigureAwait(false);
            SharedAssert.False(server.RequestBodies.Last().Contains("oai-encrypted-state", StringComparison.Ordinal), "xAI ignores OpenAI state.");
            await compatible.ToolChatAsync(Make(fromOpenAi.ProviderState!.Value), token).ConfigureAwait(false);
            SharedAssert.False(server.RequestBodies.Last().Contains("oai-encrypted-state", StringComparison.Ordinal), "OpenAiCompatibleClient ignores Responses state.");
        }

        #endregion

        #region Azure

        private static async Task AzureAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            foreach (string endpoint in new[] { server.Endpoint, server.Endpoint + "/openai", server.Endpoint + "/openai/v1/" })
            {
                using AzureOpenAiClient azure = new(endpoint, "azure-key") { Defaults = { Completion = { Model = "my-deployment" }, Embedding = { Model = "my-embeddings" } } };
                SharedAssert.True((await azure.ChatAsync("hi", token: token).ConfigureAwait(false)).Success, "Azure chat via " + endpoint);
                SharedAssert.Equal("/openai/v1/responses", server.RequestPaths.Last(), "Responses route from " + endpoint);
                SharedAssert.Equal("azure-key", server.RequestHeaders.Last()["api-key"], "api-key header.");
                SharedAssert.True(server.RequestBodies.Last().Contains("\"model\":\"my-deployment\"", StringComparison.Ordinal), "Deployment as model.");
            }
            using AzureOpenAiClient client = new(server.Endpoint, "azure-key") { Defaults = { Completion = { Model = "d" }, Embedding = { Model = "e" } } };
            SharedAssert.True((await client.EmbedAsync("x", null, token).ConfigureAwait(false)).Success, "Embeddings.");
            SharedAssert.Equal("/openai/v1/embeddings", server.RequestPaths.Last(), "Embeddings route.");
            int n = 0; await foreach (var _ in client.ListModelsAsync(token).ConfigureAwait(false)) n++;
            SharedAssert.Equal(1, n, "Models."); SharedAssert.Equal("/openai/v1/models", server.RequestPaths.Last(), "Models route.");
            SharedAssert.True(await client.ValidateConnectivityAsync(token).ConfigureAwait(false), "Connectivity.");
            SharedAssert.Equal("cancelled", (await client.CancelResponseAsync("r", token).ConfigureAwait(false)).Status, "Cancel.");
            SharedAssert.Equal("/openai/v1/responses/r/cancel", server.RequestPaths.Last(), "Cancel route.");
            ChatResponse invalid = await client.ChatAsync("oai-invalid-encrypted", token: token).ConfigureAwait(false);
            SharedAssert.True(invalid.Error!.Contains("API Management", StringComparison.Ordinal), "Proxy hint: " + invalid.Error);
            using AzureOpenAiClient entra = new(server.Endpoint, new StaticTokenCredential("entra-token")) { Defaults = { Completion = { Model = "d" } } };
            SharedAssert.True((await entra.ChatAsync("hi", token: token).ConfigureAwait(false)).Success, "Entra chat.");
            SharedAssert.Equal("Bearer entra-token", server.RequestHeaders.Last()["Authorization"], "Entra bearer.");
            SharedAssert.False(server.RequestHeaders.Last().ContainsKey("api-key"), "No api-key with Entra.");
            using OpenAiCompatibleClient chat = new(server.Endpoint + "/openai/v1", "azure-key", new OpenAiCompatibleClientOptions { AuthPlacement = AuthPlacement.Header("api-key") }) { Defaults = { Completion = { Model = "d" } } };
            ChatResponse viaCompatible = await chat.ChatAsync("hi", token: token).ConfigureAwait(false);
            SharedAssert.True(viaCompatible.Success, viaCompatible.Error ?? "Compatible Azure chat.");
            SharedAssert.Equal("/openai/v1/chat/completions", server.RequestPaths.Last(), "Azure v1 Chat Completions route.");
            SharedAssert.Equal("azure-key", server.RequestHeaders.Last()["api-key"], "api-key placement.");
            SharedAssert.False(server.RequestHeaders.Last().ContainsKey("Authorization"), "No bearer with header placement.");
            using OpenAiCompatibleClient query = new(server.Endpoint, "q-key", new OpenAiCompatibleClientOptions { AuthPlacement = AuthPlacement.Query("key") }) { Defaults = { Completion = { Model = "m" } } };
            await query.ChatAsync("hi", token: token).ConfigureAwait(false);
            SharedAssert.True(server.RequestUrls.Last().Contains("key=q-key", StringComparison.Ordinal), "Query placement: " + server.RequestUrls.Last());
        }

        #endregion

        #region Telemetry

        private static async Task TelemetryAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using TelemetryCapture capture = new TelemetryCapture("v4_telemetry");
            using AnthropicClient claude = new(LocalClients.TestKey, server.Endpoint);
            try { await claude.EmbedAsync("x").ConfigureAwait(false); } catch (CapabilityNotSupportedException) { }
            SharedAssert.Equal(0, capture.Spans().Count(s => s.DisplayName.Contains("embed", StringComparison.Ordinal)), "No span for an unsupported call.");
            ChatResponse r = await claude.ChatAsync("hi", new CompletionOptions { ReasoningEffort = ReasoningEffort.High }, token).ConfigureAwait(false);
            SharedAssert.Equal("high", r.AppliedReasoningEffort, "Applied effort.");
            Activity span = capture.Span("anthropic chat");
            SharedAssert.Equal("high", span.GetTagItem(N.ReasoningEffort)?.ToString(), "polyprompt.reasoning.effort attribute.");
            SharedAssert.Equal("completion", span.GetTagItem(N.Capability)?.ToString(), "Capability from the operation.");
            await foreach (var _ in claude.ListModelsAsync(token).ConfigureAwait(false)) { }
            await claude.ModelExistsAsync("x", token).ConfigureAwait(false);
            SharedAssert.Equal("model", capture.Span("anthropic model_exists").GetTagItem(N.Capability)?.ToString(), "One client, capability per operation.");
        }

        private static async Task NoDefaultModelAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            ProviderClientBase[] clients =
            {
                new OpenAiCompatibleClient(server.Endpoint), new BedrockClient("us-east-1", LocalClients.AwsCredential(), server.Endpoint),
                new OllamaCloudClient("k", server.Endpoint), new AzureOpenAiClient(server.Endpoint, "k"),
            };
            try
            {
                foreach (ProviderClientBase client in clients)
                {
                    InvalidOperationException? ex = null;
                    try { await client.ChatAsync("x").ConfigureAwait(false); } catch (InvalidOperationException e) { ex = e; }
                    SharedAssert.NotNull(ex, client.GetType().Name + " has no default completion model.");
                    SharedAssert.True(ex!.Message.Contains("Defaults.Completion.Model", StringComparison.Ordinal), "Guidance: " + ex.Message);
                }
                foreach (ProviderClientBase client in clients.Where(c => c is not BedrockClient))
                {
                    InvalidOperationException? ex = null;
                    try { await client.EmbedAsync("x").ConfigureAwait(false); } catch (InvalidOperationException e) { ex = e; }
                    SharedAssert.True(ex!.Message.Contains("Defaults.Embedding.Model", StringComparison.Ordinal), client.GetType().Name + " embedding guidance: " + ex.Message);
                }
                SharedAssert.Equal(0, server.RequestPaths.Count, "Nothing is sent without a model.");
            }
            finally { foreach (ProviderClientBase c in clients) c.Dispose(); }
        }

        #endregion
    }
}
