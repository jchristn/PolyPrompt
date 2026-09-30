namespace Test.Shared
{
    using System.Net;
    using System.Text;
    using System.Text.Json;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using Touchstone.Core;

    /// <summary>
    /// Deterministic cases for provider details added or fixed in 3.x: recovering Gemini tool result names from earlier
    /// tool calls, OpenAI base64 embeddings, and the live-test configuration for Azure OpenAI, Vertex AI, Bedrock, and
    /// TypeSafe. Registered into the local behavior suite.
    /// </summary>
    internal static class LocalProviderDetailCases
    {
        public static IEnumerable<TestCaseDescriptor> Create(string suiteId)
        {
            return new List<TestCaseDescriptor>
            {
                Case(suiteId, "gemini_tool_name_recovery", "Gemini recovers a tool result's name from the earlier tool call with the same id, and an explicit ToolName wins", RunToolNameRecoveryAsync),
                Case(suiteId, "gemini_tool_name_unrecoverable", "Gemini rejects a tool result whose name cannot be recovered, before sending, naming the message and id", RunToolNameUnrecoverableAsync),
                Case(suiteId, "tool_result_without_name_other_providers", "Providers that do not need the function name accept a tool result without ToolName", RunToolResultWithoutNameAsync),
                Case(suiteId, "openai_base64_embeddings", "OpenAI embeddings decode base64 vectors and send encoding_format only when set", RunBase64EmbeddingsAsync),
                Case(suiteId, "live_config_cloud_providers", "Live configuration for Azure OpenAI, Vertex AI, Bedrock, and TypeSafe applies defaults, environment groups, and validation", RunCloudConfigurationAsync),
                Case(suiteId, "live_provider_capabilities", "The live-test factory builds exactly the capability clients each provider supports", RunLiveProviderCapabilitiesAsync),
            };
        }

        private static TestCaseDescriptor Case(string suiteId, string caseId, string displayName, Func<CancellationToken, Task> executeAsync)
        {
            return new TestCaseDescriptor(suiteId, caseId, displayName, executeAsync, new[] { "local" });
        }

        #region Gemini-Tool-Names

        private static ToolChatRequest ParallelCallsWithIdOnlyResults()
        {
            ToolChatRequest request = new ToolChatRequest();
            request.Messages.Add(ChatMessage.User("Weather and time in Seattle?"));
            request.Messages.Add(ChatMessage.AssistantToolCalls(new[]
            {
                new ToolCall { Id = "call-1", Name = "get_weather", ArgumentsJson = "{\"city\":\"Seattle\"}" },
                new ToolCall { Id = "call-2", Name = "get_time", ArgumentsJson = "{\"city\":\"Seattle\"}" },
            }));

            // Results arrive out of order and carry only the call id, as Mux sent them.
            request.Messages.Add(new ChatMessage { Role = "tool", ToolCallId = "call-2", Content = "{\"time\":\"09:00\"}" });
            request.Messages.Add(new ChatMessage { Role = "tool", ToolCallId = "call-1", Content = "{\"temperature\":72}" });
            return request;
        }

        private static async Task RunToolNameRecoveryAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using GeminiCompletionClient gemini = new GeminiCompletionClient(server.Endpoint, "test-key") { Model = "test-model", TimeoutMs = 3000 };
            using VertexAiCompletionClient vertex = new VertexAiCompletionClient("test-project", "us-central1", new PolyPrompt.Auth.StaticTokenCredential("t"), server.Endpoint) { Model = "test-model", TimeoutMs = 3000 };

            ToolChatRequest sent = ParallelCallsWithIdOnlyResults();
            ToolChatResponse chat = await gemini.ToolChatAsync(sent, token).ConfigureAwait(false);
            SharedAssert.True(chat.Success, "Gemini tool chat with id-only results should succeed. " + chat.Error);

            ToolChatStreamingResponse stream = await gemini.ToolChatStreamingAsync(ParallelCallsWithIdOnlyResults(), token).ConfigureAwait(false);
            await foreach (ToolChatStreamingChunk _ in stream.Chunks.WithCancellation(token).ConfigureAwait(false)) { }
            SharedAssert.True(stream.Success, "Streaming Gemini tool chat with id-only results should succeed. " + stream.Error);

            ToolChatResponse vertexChat = await vertex.ToolChatAsync(ParallelCallsWithIdOnlyResults(), token).ConfigureAwait(false);
            SharedAssert.True(vertexChat.Success, "Vertex AI tool chat with id-only results should succeed. " + vertexChat.Error);

            ToolChatRequest explicitName = ParallelCallsWithIdOnlyResults();
            explicitName.Messages[3].ToolName = "explicit_name";
            ToolChatResponse explicitChat = await gemini.ToolChatAsync(explicitName, token).ConfigureAwait(false);
            SharedAssert.True(explicitChat.Success, "Gemini tool chat with an explicit name should succeed. " + explicitChat.Error);

            List<string> bodies = server.RequestBodies;
            SharedAssert.Equal(4, bodies.Count, "Four requests should have been sent.");
            for (int i = 0; i < 3; i++)
            {
                SharedAssert.Equal("get_time,get_weather", string.Join(",", FunctionResponseNames(bodies[i])), "Request " + i + " should name each result after the call with the same id, in result order.");
            }

            SharedAssert.Equal("get_time,explicit_name", string.Join(",", FunctionResponseNames(bodies[3])), "An explicit ToolName should be used as given.");
            SharedAssert.True(sent.Messages.Where(m => m.Role == "tool").All(m => m.ToolName == null), "Recovery should not write ToolName back into the caller's messages.");
        }

        private static async Task RunToolNameUnrecoverableAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using GeminiCompletionClient gemini = new GeminiCompletionClient(server.Endpoint, "test-key") { Model = "test-model", TimeoutMs = 3000 };

            ToolChatRequest unknownId = ParallelCallsWithIdOnlyResults();
            unknownId.Messages[2].ToolCallId = "call-unknown";
            await ExpectArgumentAsync(() => gemini.ToolChatAsync(unknownId, token), "'call-unknown'", "a result whose id matches no earlier call").ConfigureAwait(false);
            await ExpectArgumentAsync(() => gemini.ToolChatStreamingAsync(unknownId, token), "'call-unknown'", "a streamed result whose id matches no earlier call").ConfigureAwait(false);

            ToolChatRequest noIdentity = ParallelCallsWithIdOnlyResults();
            noIdentity.Messages[3].ToolCallId = null;
            await ExpectArgumentAsync(() => gemini.ToolChatAsync(noIdentity, token), "message 3", "a result with neither a name nor an id").ConfigureAwait(false);

            // The call must come before its result: an id that only appears later is not used.
            ToolChatRequest callAfterResult = new ToolChatRequest();
            callAfterResult.Messages.Add(ChatMessage.User("hi"));
            callAfterResult.Messages.Add(new ChatMessage { Role = "tool", ToolCallId = "call-9", Content = "{}" });
            callAfterResult.Messages.Add(ChatMessage.AssistantToolCalls(new[] { new ToolCall { Id = "call-9", Name = "late" } }));
            await ExpectArgumentAsync(() => gemini.ToolChatAsync(callAfterResult, token), "'call-9'", "a result before its call").ConfigureAwait(false);

            SharedAssert.Equal(0, server.RequestPaths.Count, "No request with an unrecoverable tool name should be sent.");
        }

        private static async Task RunToolResultWithoutNameAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiCompletionClient openAi = new OpenAiCompletionClient(server.Endpoint, "test-key") { Model = "test-model", TimeoutMs = 3000 };

            ToolChatRequest request = ParallelCallsWithIdOnlyResults();
            request.Messages[2].ToolCallId = "call-unknown";
            ToolChatResponse response = await openAi.ToolChatAsync(request, token).ConfigureAwait(false);
            SharedAssert.True(response.Success, "OpenAI identifies tool results by id alone and should accept a result without a name. " + response.Error);

            using JsonDocument body = JsonDocument.Parse(server.RequestBodies[0]);
            List<string?> ids = body.RootElement.GetProperty("messages").EnumerateArray()
                .Where(m => m.GetProperty("role").GetString() == "tool")
                .Select(m => m.GetProperty("tool_call_id").GetString())
                .ToList();
            SharedAssert.Equal("call-unknown,call-1", string.Join(",", ids), "OpenAI should send the tool call ids as given.");
        }

        private static List<string> FunctionResponseNames(string body)
        {
            List<string> names = new List<string>();
            using JsonDocument document = JsonDocument.Parse(body);
            foreach (JsonElement content in document.RootElement.GetProperty("contents").EnumerateArray())
            {
                foreach (JsonElement part in content.GetProperty("parts").EnumerateArray())
                {
                    if (part.TryGetProperty("functionResponse", out JsonElement response))
                        names.Add(response.GetProperty("name").GetString() ?? string.Empty);
                }
            }

            return names;
        }

        private static async Task ExpectArgumentAsync(Func<Task> action, string fragment, string label)
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (ArgumentException ex)
            {
                SharedAssert.True(ex.Message.Contains(fragment, StringComparison.Ordinal), "The error for " + label + " should mention " + fragment + ": " + ex.Message);
                SharedAssert.True(ex.Message.Contains("ToolName", StringComparison.Ordinal), "The error for " + label + " should tell the caller to set ToolName: " + ex.Message);
                return;
            }

            throw new TestFailureException("Gemini should reject " + label + ".");
        }

        #endregion

        #region OpenAI-Base64

        private static async Task RunBase64EmbeddingsAsync(CancellationToken token)
        {
            float[] first = new[] { 0.5f, -1.25f, 3.0f };
            float[] second = new[] { 1e-3f, 0f, -7.5f };
            List<string> bodies = new List<string>();

            LocalFakeHandler handler = new LocalFakeHandler(async (request, index) =>
            {
                string body = await request.Content!.ReadAsStringAsync().ConfigureAwait(false);
                lock (bodies) bodies.Add(body);
                string data = body.Contains("\"base64\"", StringComparison.Ordinal)
                    ? "[{\"index\":0,\"embedding\":\"" + ToBase64(first) + "\"},{\"index\":1,\"embedding\":\"" + ToBase64(second) + "\"}]"
                    : "[{\"index\":0,\"embedding\":[0.5,-1.25,3.0]}]";
                return LocalFakeHandler.Json(HttpStatusCode.OK, "{\"model\":\"text-embedding-3-small\",\"data\":" + data + ",\"usage\":{\"prompt_tokens\":4,\"total_tokens\":4}}");
            });

            using HttpClient http = new HttpClient(handler);
            using OpenAiEmbeddingClient client = new OpenAiEmbeddingClient("http://polyprompt.test", "k", httpClient: http);

            EmbeddingResponse plain = await client.EmbedAsync("a", null, token).ConfigureAwait(false);
            SharedAssert.True(plain.Success, "Float embeddings should parse. " + plain.Error);
            SharedAssert.Equal("0.5,-1.25,3", string.Join(",", plain.Embeddings[0].Embedding), "Float vectors should be kept.");

            client.Defaults.EncodingFormat = "base64";
            EmbeddingResponse encoded = await client.EmbedAsync(new List<string> { "a", "b" }, null, token).ConfigureAwait(false);
            SharedAssert.True(encoded.Success, "Base64 embeddings should parse. " + encoded.Error);
            SharedAssert.Equal(string.Join(",", first), string.Join(",", encoded.Embeddings[0].Embedding), "The first base64 vector should decode to its floats.");
            SharedAssert.Equal(string.Join(",", second), string.Join(",", encoded.Embeddings[1].Embedding), "The second base64 vector should decode to its floats.");

            EmbeddingResponse perCall = await client.EmbedAsync("a", new OpenAiEmbeddingOptions { EncodingFormat = "float" }, token).ConfigureAwait(false);
            SharedAssert.True(perCall.Success, "A per-call float format should parse. " + perCall.Error);

            using JsonDocument b0 = JsonDocument.Parse(bodies[0]);
            using JsonDocument b1 = JsonDocument.Parse(bodies[1]);
            using JsonDocument b2 = JsonDocument.Parse(bodies[2]);
            SharedAssert.False(b0.RootElement.TryGetProperty("encoding_format", out _), "encoding_format should be omitted when unset.");
            SharedAssert.Equal("base64", b1.RootElement.GetProperty("encoding_format").GetString(), "The default encoding format should be sent.");
            SharedAssert.Equal("float", b2.RootElement.GetProperty("encoding_format").GetString(), "A per-call encoding format should override the default.");
        }

        private static string ToBase64(float[] values)
        {
            byte[] bytes = new byte[values.Length * 4];
            Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            return Convert.ToBase64String(bytes);
        }

        #endregion

        #region Live-Configuration

        private static async Task RunCloudConfigurationAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            await ThrowsAsync(() => ProviderTestConfiguration.CreateWithDefaults("azure"), "Azure OpenAI has no default endpoint.").ConfigureAwait(false);

            ProviderTestConfiguration vertex = ProviderTestConfiguration.CreateWithDefaults("vertex");
            SharedAssert.Equal(string.Empty, vertex.Endpoint, "Vertex AI should derive a regional endpoint.");
            SharedAssert.Equal("us-central1", vertex.Region, "Vertex AI should default the region.");
            SharedAssert.Equal("text-embedding-005", vertex.EmbeddingModel, "Vertex AI should default the embedding model.");
            await ThrowsAsync(vertex.Validate, "Vertex AI requires a project.").ConfigureAwait(false);

            ProviderTestConfiguration bedrock = ProviderTestConfiguration.CreateWithDefaults("bedrock");
            SharedAssert.Equal("us-east-1", bedrock.Region, "Bedrock should default the region.");
            SharedAssert.Equal("cohere.rerank-v3-5:0", bedrock.RerankModel, "Bedrock should default the rerank model.");
            bedrock.Validate();
            bedrock.AwsAccessKeyId = "AKID";
            await ThrowsAsync(bedrock.Validate, "Bedrock requires both halves of a static key.").ConfigureAwait(false);

            ProviderTestConfiguration typeSafe = ProviderTestConfiguration.CreateWithDefaults("TypeSafe", apiKey: "ts");
            SharedAssert.Equal(ProviderTestConfiguration.DefaultTypeSafeEndpoint, typeSafe.Endpoint, "TypeSafe should default to the hosted API.");
            SharedAssert.Equal(string.Empty, typeSafe.EmbeddingModel, "TypeSafe has no embedding model.");

            Dictionary<string, string?> saved = ProviderTestConfiguration.EnvironmentVariableNames.ToDictionary(n => n, Environment.GetEnvironmentVariable);
            try
            {
                Clear();
                Set("POLYPROMPT_TEST_AZURE_ENDPOINT", "https://res.openai.azure.com");
                Set("POLYPROMPT_TEST_AZURE_API_KEY", "az");
                await ThrowsAsync(() => ProviderTestConfiguration.FromEnvironment(), "An Azure group without a chat deployment should be rejected.").ConfigureAwait(false);
                Set("POLYPROMPT_TEST_AZURE_MODEL", "gpt-deploy");
                Set("POLYPROMPT_TEST_AZURE_EMBEDDING_MODEL", "embed-deploy");
                Set("POLYPROMPT_TEST_AZURE_API_VERSION", "2024-10-21");
                ProviderTestConfiguration azure = ProviderTestConfiguration.FromEnvironment()!;
                SharedAssert.Equal("azure", azure.ProviderType, "The Azure group should select Azure.");
                SharedAssert.Equal("gpt-deploy", azure.InferenceModel, "The chat deployment should be kept.");
                SharedAssert.Equal("embed-deploy", azure.EmbeddingModel, "The embedding deployment should be kept.");
                SharedAssert.Equal("2024-10-21", azure.ApiVersion, "The API version should be kept.");

                Clear();
                Set("POLYPROMPT_TEST_VERTEX_PROJECT", "proj");
                Set("POLYPROMPT_TEST_VERTEX_REGION", "europe-west4");
                Set("POLYPROMPT_TEST_VERTEX_CREDENTIALS", "/path/sa.json");
                ProviderTestConfiguration vertexEnv = ProviderTestConfiguration.FromEnvironment()!;
                SharedAssert.Equal("vertex", vertexEnv.ProviderType, "The Vertex group should select Vertex AI.");
                SharedAssert.Equal("proj", vertexEnv.Project, "The project should be kept.");
                SharedAssert.Equal("europe-west4", vertexEnv.Region, "The region should be kept.");
                SharedAssert.Equal("/path/sa.json", vertexEnv.CredentialsPath, "The credentials path should be kept.");
                SharedAssert.True(vertexEnv.ApiKey == null, "Vertex AI uses no API key.");

                Clear();
                Set("POLYPROMPT_TEST_BEDROCK_REGION", "us-west-2");
                Set("POLYPROMPT_TEST_BEDROCK_ACCESS_KEY_ID", "AKID");
                Set("POLYPROMPT_TEST_BEDROCK_SECRET_ACCESS_KEY", "secret");
                Set("POLYPROMPT_TEST_BEDROCK_SESSION_TOKEN", "session");
                Set("POLYPROMPT_TEST_BEDROCK_RERANK_MODEL", "amazon.rerank-v1:0");
                ProviderTestConfiguration bedrockEnv = ProviderTestConfiguration.FromEnvironment()!;
                SharedAssert.Equal("bedrock", bedrockEnv.ProviderType, "The Bedrock group should select Bedrock.");
                SharedAssert.Equal("us-west-2", bedrockEnv.Region, "The region should be kept.");
                SharedAssert.Equal("session", bedrockEnv.AwsSessionToken, "The session token should be kept.");
                SharedAssert.Equal("amazon.rerank-v1:0", bedrockEnv.RerankModel, "The rerank model should be kept.");

                Clear();
                Set("POLYPROMPT_TEST_TYPESAFE_API_KEY", "ts");
                Set("POLYPROMPT_TEST_TYPESAFE_MODEL", "jev-2");
                ProviderTestConfiguration typeSafeEnv = ProviderTestConfiguration.FromEnvironment()!;
                SharedAssert.Equal("typesafe", typeSafeEnv.ProviderType, "The TypeSafe group should select TypeSafe.");
                SharedAssert.Equal("jev-2", typeSafeEnv.InferenceModel, "The decision model should be kept.");

                Clear();
                Set("POLYPROMPT_TEST_PROVIDER", "vertex");
                Set("POLYPROMPT_TEST_PROJECT", "generic-proj");
                Set("POLYPROMPT_TEST_REGION", "asia-northeast1");
                ProviderTestConfiguration generic = ProviderTestConfiguration.FromEnvironment()!;
                SharedAssert.Equal("generic-proj", generic.Project, "The generic project variable should be read.");
                SharedAssert.Equal("asia-northeast1", generic.Region, "The generic region variable should be read.");

                Clear();
                Set("POLYPROMPT_TEST_BEDROCK_REGION", "us-east-1");
                Set("POLYPROMPT_TEST_TYPESAFE_API_KEY", "ts");
                await ThrowsAsync(() => ProviderTestConfiguration.FromEnvironment(), "Two provider groups at once should be rejected.").ConfigureAwait(false);
            }
            finally
            {
                foreach (KeyValuePair<string, string?> entry in saved) Environment.SetEnvironmentVariable(entry.Key, entry.Value);
            }
        }

        private static Task RunLiveProviderCapabilitiesAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            foreach (string providerType in ProviderTestConfiguration.ProviderTypes)
            {
                ProviderTestConfiguration configuration = ProviderTestConfiguration.Create(providerType, "http://127.0.0.1:1", "k", "some-model", "some-embed");
                configuration.Project = "proj";
                configuration.Region ??= "us-east-1";
                configuration.AwsAccessKeyId = "AKID";
                configuration.AwsSecretAccessKey = "secret";
                configuration.CredentialsPath = null;

                using LiveProvider provider = LiveProvider.Create(configuration);
                Dictionary<string, ClientBase?> clients = new Dictionary<string, ClientBase?>
                {
                    { "completion", provider.Completion }, { "embedding", provider.Embedding }, { "sparse", provider.SparseEmbedding },
                    { "rerank", provider.Rerank }, { "classification", provider.Classification }, { "decision", provider.Decision }, { "models", provider.Models },
                };

                foreach (KeyValuePair<string, ClientBase?> entry in clients)
                {
                    SharedAssert.Equal(ProviderLiveSuite.Supports(providerType, entry.Key), entry.Value != null, providerType + " " + entry.Key + " client presence should match the live suite's capability table.");
                }

                if (provider.Completion != null) SharedAssert.Equal("some-model", provider.Completion.Model, providerType + " completion should use the configured model.");
                if (provider.Embedding != null && providerType != "tei") SharedAssert.Equal("some-embed", provider.Embedding.Model, providerType + " embedding should use the configured embedding model.");
                if (provider.Decision != null) SharedAssert.Equal("some-model", provider.Decision.Model, providerType + " decision should use the configured model.");
                SharedAssert.True(provider.All.All(c => c.TimeoutMs == 120000), providerType + " live clients should use the live timeout.");
            }

            return Task.CompletedTask;
        }

        private static void Clear()
        {
            foreach (string name in ProviderTestConfiguration.EnvironmentVariableNames) Environment.SetEnvironmentVariable(name, null);
        }

        private static void Set(string name, string value)
        {
            Environment.SetEnvironmentVariable(name, value);
        }

        private static async Task ThrowsAsync(Action action, string message)
        {
            await SharedAssert.ThrowsAsync<ArgumentException>(() => { action(); return Task.CompletedTask; }, message).ConfigureAwait(false);
        }

        private static Task ThrowsAsync(Func<object?> action, string message)
        {
            return ThrowsAsync(() => { action(); }, message);
        }

        #endregion
    }
}
