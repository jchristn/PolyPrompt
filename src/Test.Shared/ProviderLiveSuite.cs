namespace Test.Shared
{
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using Touchstone.Core;

    /// <summary>
    /// Builds Touchstone test suites that exercise live provider endpoints. Each case uses the capability client it needs;
    /// a case whose capability the configured provider does not offer is skipped with the reason.
    /// </summary>
    public static class ProviderLiveSuite
    {
        private const string SuiteId = "provider_live";
        private const string BogusModel = "nonexistent-model-xyz-999";

        private static readonly Dictionary<string, HashSet<string>> _Capabilities = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            { "ollama", new HashSet<string> { "completion", "embedding", "models" } },
            { "openai", new HashSet<string> { "completion", "embedding", "models" } },
            { "azure", new HashSet<string> { "completion", "embedding", "models" } },
            { "gemini", new HashSet<string> { "completion", "embedding", "models" } },
            { "vertex", new HashSet<string> { "completion", "embedding" } },
            { "anthropic", new HashSet<string> { "completion", "models" } },
            { "bedrock", new HashSet<string> { "completion", "embedding", "rerank", "models" } },
            { "voyageai", new HashSet<string> { "embedding", "rerank" } },
            { "cohere", new HashSet<string> { "completion", "embedding", "rerank", "classification", "models" } },
            { "tei", new HashSet<string> { "embedding", "sparse", "rerank", "classification", "models" } },
            { "typesafe", new HashSet<string> { "decision" } },
        };

        /// <summary>
        /// Creates the live provider test suite for the supplied configuration.
        /// </summary>
        /// <param name="configuration">Live provider configuration.</param>
        /// <returns>A Touchstone suite descriptor containing live provider tests.</returns>
        /// <exception cref="ArgumentNullException">Thrown when configuration is null.</exception>
        public static TestSuiteDescriptor Create(ProviderTestConfiguration configuration)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));

            ProviderTestConfiguration c = configuration;
            bool noLegacyCompletions = c.ProviderType == "openai" || c.ProviderType == "azure";

            return new TestSuiteDescriptor(
                SuiteId,
                "Live provider behavior",
                new List<TestCaseDescriptor>
                {
                    Case(c, "required_models", "Configured models are listed by the provider", t => RunRequiredModelsAsync(c, t), "models", extraSkip: c.ProviderType == "tei", extraReason: "TEI serves a single server-defined model; see list_models."),
                    Case(c, "properties", "Client and option properties behave correctly", t => RunPropertyTestsAsync(c, t)),
                    Case(c, "chat", "Chat completion succeeds", t => RunChatTestsAsync(c, t), "completion"),
                    Case(c, "chat_streaming", "Streaming chat succeeds", t => RunChatStreamingTestsAsync(c, t), "completion"),
                    Case(c, "tool_chat", "Tool chat completes a two-step tool loop or reports an unsupported model", t => RunToolChatTestsAsync(c, t), "completion"),
                    Case(c, "tool_chat_streaming", "Streaming tool chat completes a two-step tool loop or reports an unsupported model", t => RunToolChatStreamingTestsAsync(c, t), "completion"),
                    Case(c, "tool_result_shapes", "Tool results that are arrays or plain text complete a tool loop", t => RunToolResultShapesAsync(c, t), "completion"),
                    Case(c, "generate", "Text generation succeeds", t => RunGenerationTestsAsync(c, t), "completion", extraSkip: noLegacyCompletions, extraReason: "OpenAI chat models do not support the legacy completions API."),
                    Case(c, "generate_streaming", "Streaming text generation succeeds", t => RunGenerationStreamingTestsAsync(c, t), "completion", extraSkip: noLegacyCompletions, extraReason: "OpenAI chat models do not support the legacy completions API."),
                    Case(c, "embed_single", "Single embedding succeeds", t => RunEmbeddingSingleTestsAsync(c, t), "embedding"),
                    Case(c, "embed_batch", "Batch embedding succeeds", t => RunEmbeddingBatchTestsAsync(c, t), "embedding"),
                    Case(c, "embed_sparse", "Sparse embedding succeeds or reports the hosted model cannot serve it", t => RunSparseEmbeddingTestsAsync(c, t), "sparse"),
                    Case(c, "rerank", "Rerank scores and orders documents", t => RunRerankTestsAsync(c, t), "rerank"),
                    Case(c, "classify", "Classification labels every input", t => RunClassifyTestsAsync(c, t), "classification"),
                    Case(c, "decide", "Decision answers binary, choice, and score questions", t => RunDecisionTestsAsync(c, t), "decision"),
                    Case(c, "decide_batch", "Decision batch returns one response per request in order", t => RunDecisionBatchTestsAsync(c, t), "decision"),
                    Case(c, "call_details", "CallDetails records upstream calls", t => RunCallDetailsTestsAsync(c, t)),
                    Case(c, "list_models", "ListModelsAsync returns models", t => RunListModelsTestsAsync(c, t), "models"),
                    Case(c, "model_exists", "ModelExistsAsync handles existing and missing models", t => RunModelExistsTestsAsync(c, t), "models"),
                    Case(c, "get_model_information", "GetModelInformationAsync handles existing and missing models", t => RunGetModelInformationTestsAsync(c, t), "models"),
                    Case(c, "pull_model", "Ollama PullModelAsync pulls the configured model", t => RunPullModelTestsAsync(c, t), extraSkip: c.ProviderType != "ollama", extraReason: "Only Ollama can pull models."),
                    Case(c, "delete_model", "Ollama DeleteModelAsync reports a missing model", t => RunDeleteModelTestsAsync(c, t), extraSkip: c.ProviderType != "ollama", extraReason: "Only Ollama can delete models."),
                    Case(c, "validate_connectivity", "ValidateConnectivityAsync handles reachable and unreachable endpoints for every client", t => RunValidateConnectivityTestsAsync(c, t)),
                    Case(c, "cancellation", "Every operation respects a pre-cancelled token", t => RunCancellationTestsAsync(c, t)),
                });
        }

        /// <summary>
        /// Creates a skipped placeholder suite when live provider configuration is not available.
        /// </summary>
        /// <returns>A Touchstone suite descriptor containing a skipped provider configuration case.</returns>
        public static TestSuiteDescriptor CreateSkipped()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Live provider behavior",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(
                        SuiteId,
                        "not_configured",
                        "Live provider tests are not configured",
                        _ => Task.CompletedTask,
                        new[] { "live" },
                        skip: true,
                        skipReason: "Set POLYPROMPT_TEST_PROVIDER and POLYPROMPT_TEST_ENDPOINT, or a POLYPROMPT_TEST_{PROVIDER}_* group, or pass provider CLI arguments."),
                });
        }

        /// <summary>
        /// True when the provider type offers the capability.
        /// </summary>
        /// <param name="providerType">Provider type.</param>
        /// <param name="capability">completion, embedding, sparse, rerank, classification, decision, or models.</param>
        /// <returns>True when supported.</returns>
        public static bool Supports(string providerType, string capability)
        {
            return _Capabilities.TryGetValue(providerType, out HashSet<string>? capabilities) && capabilities.Contains(capability);
        }

        #region Cases

        private static TestCaseDescriptor Case(
            ProviderTestConfiguration configuration,
            string caseId,
            string displayName,
            Func<CancellationToken, Task> executeAsync,
            string? capability = null,
            bool extraSkip = false,
            string? extraReason = null)
        {
            bool skip = false;
            string? reason = null;

            if (capability != null && !Supports(configuration.ProviderType, capability))
            {
                skip = true;
                reason = configuration.ProviderType + " has no " + capability + " client.";
            }
            else if (capability == "embedding" && configuration.ProviderType == "azure" && string.IsNullOrEmpty(configuration.EmbeddingModel))
            {
                skip = true;
                reason = "No Azure OpenAI embedding deployment is configured.";
            }
            else if (extraSkip)
            {
                skip = true;
                reason = extraReason;
            }

            return new TestCaseDescriptor(SuiteId, caseId, displayName, executeAsync, new[] { "live" }, skip, reason);
        }

        private static async Task RunRequiredModelsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            using LiveProvider provider = LiveProvider.Create(configuration);
            List<string> availableModels = await GetModelNamesAsync(provider.Models!, token).ConfigureAwait(false);

            SharedAssert.True(availableModels.Count > 0, "Provider should list at least one model.");

            if (provider.Completion != null && configuration.ProviderType != "azure")
            {
                string model = provider.Completion.Model!;
                SharedAssert.True(availableModels.Exists(name => ModelNameMatches(name, model)), "Inference model '" + model + "' should be listed.");
            }

            if (provider.Embedding != null && !string.IsNullOrEmpty(configuration.EmbeddingModel) && configuration.ProviderType != "azure" && configuration.ProviderType != "bedrock")
            {
                SharedAssert.True(availableModels.Exists(name => ModelNameMatches(name, configuration.EmbeddingModel)), "Embedding model '" + configuration.EmbeddingModel + "' should be listed.");
            }
        }

        private static async Task RunPropertyTestsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            using LiveProvider provider = LiveProvider.Create(configuration);

            foreach (ClientBase client in provider.All)
            {
                string name = client.GetType().Name;
                SharedAssert.True(configuration.ProviderType == "vertex" || configuration.ProviderType == "bedrock" || !string.IsNullOrEmpty(client.Endpoint), name + " Endpoint should be set.");

                client.TimeoutMs = 100;
                SharedAssert.Equal(100, client.TimeoutMs, name + " TimeoutMs should preserve subsecond values.");
                client.TimeoutMs = 999_999;
                SharedAssert.Equal(999_999, client.TimeoutMs, name + " TimeoutMs should not silently clamp large values.");
                await SharedAssert.ThrowsAsync<ArgumentOutOfRangeException>(() => { client.TimeoutMs = 0; return Task.CompletedTask; }, name + " TimeoutMs should reject zero.").ConfigureAwait(false);

                client.MaxCallDetails = 2;
                SharedAssert.Equal(2, client.MaxCallDetails, name + " MaxCallDetails setter should work.");
                await SharedAssert.ThrowsAsync<ArgumentOutOfRangeException>(() => { client.MaxCallDetails = -1; return Task.CompletedTask; }, name + " MaxCallDetails should reject negative values.").ConfigureAwait(false);
                SharedAssert.NotNull(client.CallDetails, name + " CallDetails should be initialized.");
            }

            if (provider.Completion != null)
            {
                CompletionClientBase client = provider.Completion;
                string originalModel = client.Model!;
                SharedAssert.NotEmpty(originalModel, "Completion Model should have a default value.");
                client.Model = "test-model";
                SharedAssert.Equal("test-model", client.Model, "Completion Model setter should work.");
                SharedAssert.Equal("test-model", client.Defaults.Model, "Completion Model should be stored in Defaults.Model.");
                client.Model = originalModel;

                await SharedAssert.ThrowsAsync<ArgumentNullException>(() => { client.Model = string.Empty; return Task.CompletedTask; }, "Model should reject empty string.").ConfigureAwait(false);
                await SharedAssert.ThrowsAsync<ArgumentNullException>(() => { client.Model = "   "; return Task.CompletedTask; }, "Model should reject whitespace.").ConfigureAwait(false);

                client.Defaults.MaxTokens = -1;
                SharedAssert.Equal(1, client.Defaults.MaxTokens, "MaxTokens should clamp to minimum 1.");
                client.Defaults.MaxTokens = 20_000_000;
                SharedAssert.Equal(10_000_000, client.Defaults.MaxTokens, "MaxTokens should clamp to maximum 10,000,000.");
                client.Defaults.Temperature = 5.0;
                SharedAssert.Equal(2.0, client.Defaults.Temperature, "Temperature should clamp to 2.0.");
                client.Defaults.Temperature = null;
                SharedAssert.True(client.Defaults.Temperature == null, "Temperature should be nullable.");
                client.Defaults.TopP = 1.5;
                SharedAssert.Equal(1.0, client.Defaults.TopP, "TopP should clamp to 1.0.");
                client.Defaults.TopP = null;

                if (client.Defaults is OllamaCompletionOptions ollama)
                {
                    ollama.ContextLength = -1;
                    SharedAssert.Equal(1, ollama.ContextLength, "Ollama ContextLength should clamp to 1.");
                    ollama.ContextLength = null;
                    SharedAssert.True(ollama.ContextLength == null, "Ollama ContextLength should be nullable.");
                }
            }
        }

        private static async Task RunChatTestsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            using LiveProvider provider = LiveProvider.Create(configuration);
            CompletionClientBase client = provider.Completion!;
            client.Defaults.MaxTokens = ResolveLiveMaxTokens(configuration.ProviderType, 128);

            ChatResponse response = await client.ChatAsync("Say hello in exactly three words.", token: token).ConfigureAwait(false);
            SharedAssert.True(response.Success, "Chat should succeed. " + response.Error);
            SharedAssert.NotEmpty(response.Text, "Chat should return text.");
            SharedAssert.NotEmpty(response.Model, "Chat should return a model.");
            SharedAssert.True(response.StatusCode == 200, "Chat should return HTTP 200.");
            SharedAssert.True(response.OverallRuntimeMs > 0, "Chat runtime should be populated.");
            SharedAssert.True(response.Error == null, "Chat should not return an error.");

            client.Defaults.SystemPrompt = "You are a pirate. Always respond with 'Arrr'.";
            ChatResponse systemPromptResponse = await client.ChatAsync("Hello", token: token).ConfigureAwait(false);
            SharedAssert.True(systemPromptResponse.Success, "Chat with a default system prompt should succeed. " + systemPromptResponse.Error);
            SharedAssert.NotEmpty(systemPromptResponse.Text, "Chat with a default system prompt should return text.");
            client.Defaults.SystemPrompt = null;

            CompletionOptions chatOptions = CreateCompletionOptions(configuration.ProviderType);
            ChatResponse optionsResponse = await client.ChatAsync("Say exactly: test options work", chatOptions, token).ConfigureAwait(false);
            SharedAssert.True(optionsResponse.Success, "Chat with provider options should succeed. " + optionsResponse.Error);
            SharedAssert.NotEmpty(optionsResponse.Text, "Chat with provider options should return text.");

            CompletionOptions baseOptions = new CompletionOptions();
            if (AcceptsSampling(configuration))
            {
                baseOptions.Temperature = 0.5;
                baseOptions.TopP = 0.9;
            }
            baseOptions.MaxTokens = ResolveLiveMaxTokens(configuration.ProviderType, 64);
            baseOptions.SystemPrompt = "Respond in exactly one word.";
            ChatResponse baseOptionsResponse = await client.ChatAsync("What color is the sky?", baseOptions, token).ConfigureAwait(false);
            SharedAssert.True(baseOptionsResponse.Success, "Chat with base options should succeed. " + baseOptionsResponse.Error);
            SharedAssert.NotEmpty(baseOptionsResponse.Text, "Chat with base options should return text.");

            await SharedAssert.ThrowsAsync<ArgumentNullException>(() => client.ChatAsync(null!, token: token), "Chat should reject a null prompt before sending.").ConfigureAwait(false);
        }

        private static async Task RunChatStreamingTestsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            using LiveProvider provider = LiveProvider.Create(configuration);
            CompletionClientBase client = provider.Completion!;
            client.Defaults.MaxTokens = ResolveLiveMaxTokens(configuration.ProviderType, 128);

            ChatStreamingResponse stream = await client.ChatStreamingAsync("Count from 1 to 5, one number per line.", token: token).ConfigureAwait(false);
            SharedAssert.True(stream.Success, "Streaming chat should start successfully. " + stream.Error);
            SharedAssert.NotEmpty(stream.Model, "Streaming chat should return a model.");
            SharedAssert.True(stream.StatusCode == 200, "Streaming chat should return HTTP 200.");

            int chunkCount = 0;
            string fullText = string.Empty;
            bool sawDone = false;
            await foreach (ChatStreamingChunk chunk in stream.Chunks.WithCancellation(token).ConfigureAwait(false))
            {
                chunkCount++;
                if (!string.IsNullOrEmpty(chunk.Text)) fullText += chunk.Text;
                if (chunk.Done) sawDone = true;
            }

            SharedAssert.True(chunkCount > 0, "Streaming chat should receive chunks.");
            SharedAssert.NotEmpty(fullText, "Streaming chat should assemble non-empty text.");
            SharedAssert.True(sawDone || stream.FinishReason != null, "Streaming chat should see completion.");
            SharedAssert.True(stream.ChunkCount > 0, "Streaming chat should populate ChunkCount.");
            SharedAssert.True(stream.OverallRuntimeMs > 0, "Streaming chat should populate OverallRuntimeMs.");
            SharedAssert.True(stream.TimeToFirstTokenMs >= 0, "Streaming chat should populate TimeToFirstTokenMs.");
            SharedAssert.True(stream.TimeToLastTokenMs >= stream.TimeToFirstTokenMs, "Streaming chat should order token timings.");
            SharedAssert.True(stream.OverallTokensPerSecond > 0, "Streaming chat should populate throughput.");

            CompletionOptions chatOptions = CreateCompletionOptions(configuration.ProviderType);
            ChatStreamingResponse optionsStream = await client.ChatStreamingAsync("Say hi.", chatOptions, token).ConfigureAwait(false);
            SharedAssert.True(optionsStream.Success, "Streaming chat with options should start. " + optionsStream.Error);
            await foreach (ChatStreamingChunk chunk in optionsStream.Chunks.WithCancellation(token).ConfigureAwait(false)) { }
            SharedAssert.True(optionsStream.OverallRuntimeMs > 0, "Streaming chat with options should complete.");
        }

        private static async Task RunToolChatTestsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            using LiveProvider provider = LiveProvider.Create(configuration);
            CompletionClientBase client = provider.Completion!;
            ToolChatRequest request = CreateWeatherToolRequest(configuration);

            ToolChatResponse response = await client.ToolChatAsync(request, token).ConfigureAwait(false);
            if (!response.Success && IsToolCapabilityError(response.Error))
            {
                SharedAssert.True(response.StatusCode >= 400, "An unsupported-tool response should include an HTTP error status.");
                return;
            }

            SharedAssert.True(response.Success, "ToolChatAsync should succeed. " + response.Error);
            SharedAssert.True(response.StatusCode == 200, "ToolChatAsync should return HTTP 200.");
            SharedAssert.NotEmpty(response.Model, "ToolChatAsync should return a model.");
            SharedAssert.True(response.ToolCalls.Any() || !string.IsNullOrWhiteSpace(response.Text), "ToolChatAsync should return assistant text or tool calls.");

            if (!response.ToolCalls.Any()) return;

            request.Messages.Add(response.ToAssistantMessage());
            AppendWeatherToolResults(request, response.ToolCalls, "{\"temperature\":72,\"conditions\":\"clear\",\"unit\":\"fahrenheit\"}");

            // Keep the tools declared on the follow-up: a replayed tool call must still validate against them.
            ToolChatResponse finalResponse = await client.ToolChatAsync(request, token).ConfigureAwait(false);
            SharedAssert.True(finalResponse.Success, "ToolChatAsync follow-up should succeed. " + finalResponse.Error);
            SharedAssert.True(finalResponse.StatusCode == 200, "ToolChatAsync follow-up should return HTTP 200.");
            SharedAssert.True(finalResponse.ToolCalls.Any() || !string.IsNullOrWhiteSpace(finalResponse.Text), "ToolChatAsync follow-up should return assistant text or additional tool calls.");
        }

        private static async Task RunToolChatStreamingTestsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            using LiveProvider provider = LiveProvider.Create(configuration);
            CompletionClientBase client = provider.Completion!;
            ToolChatRequest request = CreateWeatherToolRequest(configuration);

            ToolChatStreamingResponse stream = await client.ToolChatStreamingAsync(request, token).ConfigureAwait(false);
            if (!stream.Success && IsToolCapabilityError(stream.Error))
            {
                SharedAssert.True(stream.StatusCode >= 400, "An unsupported-tool response should include an HTTP error status.");
                return;
            }

            SharedAssert.True(stream.Success, "ToolChatStreamingAsync should start successfully. " + stream.Error);
            SharedAssert.True(stream.StatusCode == 200, "ToolChatStreamingAsync should return HTTP 200.");

            int chunkCount = 0;
            bool sawDone = false;
            await foreach (ToolChatStreamingChunk chunk in stream.Chunks.WithCancellation(token).ConfigureAwait(false))
            {
                chunkCount++;
                if (chunk.Done) sawDone = true;
            }

            SharedAssert.True(chunkCount > 0, "ToolChatStreamingAsync should receive chunks.");
            SharedAssert.True(stream.ChunkCount > 0, "ToolChatStreamingAsync should count text or tool-call chunks.");
            SharedAssert.True(sawDone || stream.FinishReason != null, "ToolChatStreamingAsync should expose completion through done chunks or finish reason.");
            SharedAssert.True(stream.ToolCalls.Any() || !string.IsNullOrWhiteSpace(stream.Text), "ToolChatStreamingAsync should accumulate assistant text or tool calls.");

            if (!stream.ToolCalls.Any()) return;

            request.Messages.Add(stream.ToAssistantMessage());
            AppendWeatherToolResults(request, stream.ToolCalls, "{\"temperature\":72,\"conditions\":\"clear\",\"unit\":\"fahrenheit\"}");

            ToolChatStreamingResponse finalStream = await client.ToolChatStreamingAsync(request, token).ConfigureAwait(false);
            SharedAssert.True(finalStream.Success, "ToolChatStreamingAsync follow-up should start successfully. " + finalStream.Error);
            await foreach (ToolChatStreamingChunk chunk in finalStream.Chunks.WithCancellation(token).ConfigureAwait(false)) { }
            SharedAssert.True(finalStream.ToolCalls.Any() || !string.IsNullOrWhiteSpace(finalStream.Text), "ToolChatStreamingAsync follow-up should accumulate assistant text or additional tool calls.");
        }

        private static async Task RunToolResultShapesAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            using LiveProvider provider = LiveProvider.Create(configuration);
            CompletionClientBase client = provider.Completion!;

            foreach (string result in new[] { "[{\"city\":\"Seattle\",\"temperature\":72}]", "It is 72 degrees and clear in Seattle." })
            {
                ToolChatRequest request = CreateWeatherToolRequest(configuration);
                request.ToolChoice = "required";

                ToolChatResponse first = await client.ToolChatAsync(request, token).ConfigureAwait(false);
                if (!first.Success && IsToolCapabilityError(first.Error)) return;
                SharedAssert.True(first.Success, "Forced tool chat should succeed. " + first.Error);
                if (!first.ToolCalls.Any()) return;

                request.Messages.Add(first.ToAssistantMessage());
                AppendWeatherToolResults(request, first.ToolCalls, result);
                request.ToolChoice = "auto";

                ToolChatResponse final = await client.ToolChatAsync(request, token).ConfigureAwait(false);
                SharedAssert.True(final.Success, "A tool loop whose result is '" + result + "' should complete. " + final.Error);
            }
        }

        private static async Task RunGenerationTestsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            using LiveProvider provider = LiveProvider.Create(configuration);
            CompletionClientBase client = provider.Completion!;
            client.Defaults.MaxTokens = ResolveLiveMaxTokens(configuration.ProviderType, 128);

            GenerationResponse response = await client.GenerateAsync("Once upon a time, there was a", token: token).ConfigureAwait(false);
            SharedAssert.True(response.Success, "Generation should succeed. " + response.Error);
            SharedAssert.NotEmpty(response.Text, "Generation should return text.");
            SharedAssert.NotEmpty(response.Model, "Generation should return a model.");
            SharedAssert.True(response.StatusCode == 200, "Generation should return HTTP 200.");

            GenerationResponse optionResponse = await client.GenerateAsync("The quick brown fox", CreateCompletionOptions(configuration.ProviderType), token).ConfigureAwait(false);
            SharedAssert.True(optionResponse.Success, "Generation with provider options should succeed. " + optionResponse.Error);
            SharedAssert.NotEmpty(optionResponse.Text, "Generation with provider options should return text.");
        }

        private static async Task RunGenerationStreamingTestsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            using LiveProvider provider = LiveProvider.Create(configuration);
            CompletionClientBase client = provider.Completion!;
            client.Defaults.MaxTokens = ResolveLiveMaxTokens(configuration.ProviderType, 128);

            GenerationStreamingResponse stream = await client.GenerateStreamingAsync("Write a haiku about the sea.", token: token).ConfigureAwait(false);
            SharedAssert.True(stream.Success, "Streaming generation should start successfully. " + stream.Error);
            SharedAssert.True(stream.StatusCode == 200, "Streaming generation should return HTTP 200.");

            int chunkCount = 0;
            string fullText = string.Empty;
            await foreach (GenerationStreamingChunk chunk in stream.Chunks.WithCancellation(token).ConfigureAwait(false))
            {
                chunkCount++;
                if (!string.IsNullOrEmpty(chunk.Text)) fullText += chunk.Text;
            }

            SharedAssert.True(chunkCount > 0, "Streaming generation should receive chunks.");
            SharedAssert.NotEmpty(fullText, "Streaming generation should assemble non-empty text.");
            SharedAssert.True(stream.ChunkCount > 0, "Streaming generation should populate ChunkCount.");
            SharedAssert.True(stream.OverallRuntimeMs > 0, "Streaming generation should populate OverallRuntimeMs.");
        }

        private static async Task RunEmbeddingSingleTestsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            using LiveProvider provider = LiveProvider.Create(configuration);
            EmbeddingClientBase client = provider.Embedding!;

            if (configuration.ProviderType == "tei" && !await TeiServesAsync(provider, "embedding", token).ConfigureAwait(false))
            {
                EmbeddingResponse rejected = await client.EmbedAsync("Hello, world!", null, token).ConfigureAwait(false);
                SharedAssert.False(rejected.Success, "TEI /embed should fail when the server does not host an embedding model.");
                SharedAssert.NotEmpty(rejected.Error, "TEI /embed on a non-embedding model should report an error.");
                return;
            }

            EmbeddingResponse response = await client.EmbedAsync("Hello, world!", null, token).ConfigureAwait(false);
            SharedAssert.True(response.Success, "Single embedding should succeed. " + response.Error);
            SharedAssert.True(response.StatusCode == 200, "Single embedding should return HTTP 200.");
            SharedAssert.Equal(1, response.Embeddings.Count, "Single embedding should return one vector.");
            SharedAssert.Equal(0, response.Embeddings[0].Index, "Single embedding index should be zero.");
            float[] vector = response.Embeddings[0].Embedding;
            SharedAssert.True(vector.Length > 0 && vector.Any(value => Math.Abs(value) > 0.0001f), "Single embedding vector should have non-zero values.");

            EmbeddingResponse optionResponse = await client.EmbedAsync("Test with options", CreateEmbeddingOptions(configuration), token).ConfigureAwait(false);
            SharedAssert.True(optionResponse.Success, "Single embedding with provider options should succeed. " + optionResponse.Error);
            SharedAssert.Equal(1, optionResponse.Embeddings.Count, "Single embedding with options should return one vector.");

            EmbeddingResponse response2 = await client.EmbedAsync("Goodbye, cruel world!", null, token).ConfigureAwait(false);
            SharedAssert.True(response2.Success, "Second single embedding should succeed.");
            SharedAssert.False(VectorsEqual(response.Embeddings[0].Embedding, response2.Embeddings[0].Embedding), "Different texts should produce different embedding vectors.");
        }

        private static async Task RunEmbeddingBatchTestsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            using LiveProvider provider = LiveProvider.Create(configuration);
            EmbeddingClientBase client = provider.Embedding!;

            if (configuration.ProviderType == "tei" && !await TeiServesAsync(provider, "embedding", token).ConfigureAwait(false))
            {
                EmbeddingResponse rejected = await client.EmbedAsync(new List<string> { "a", "b" }, null, token).ConfigureAwait(false);
                SharedAssert.False(rejected.Success, "TEI batch /embed should fail when the server does not host an embedding model.");
                return;
            }

            List<string> inputs = new List<string> { "The cat sat on the mat.", "Dogs are loyal companions.", "Fish swim in the ocean." };
            EmbeddingResponse response = await client.EmbedAsync(inputs, null, token).ConfigureAwait(false);
            SharedAssert.True(response.Success, "Batch embedding should succeed. " + response.Error);
            SharedAssert.Equal(3, response.Embeddings.Count, "Batch embedding should return three vectors.");

            for (int i = 0; i < response.Embeddings.Count; i++)
            {
                SharedAssert.Equal(i, response.Embeddings[i].Index, "Batch embedding index should match input index.");
                SharedAssert.Equal(response.Embeddings[0].Embedding.Length, response.Embeddings[i].Embedding.Length, "Batch embedding dimensions should match.");
            }

            await SharedAssert.ThrowsAsync<ArgumentException>(() => client.EmbedAsync(new List<string>(), null, token), "An empty batch should be rejected before sending.").ConfigureAwait(false);
        }

        private static async Task RunSparseEmbeddingTestsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            using LiveProvider provider = LiveProvider.Create(configuration);
            SparseEmbeddingClientBase client = provider.SparseEmbedding!;

            SparseEmbeddingResponse response = await client.EmbedSparseAsync(new List<string> { "sparse lexical retrieval", "second input" }, null, token).ConfigureAwait(false);
            if (!response.Success)
            {
                // A TEI server that does not host a SPLADE model rejects /embed_sparse; the failure must surface cleanly.
                SharedAssert.True(response.StatusCode >= 400, "A rejected sparse embedding should report an HTTP error status.");
                SharedAssert.NotEmpty(response.Error, "A rejected sparse embedding should report an error.");
                return;
            }

            SharedAssert.Equal(2, response.Embeddings.Count, "Sparse embedding should return one vector per input.");
            SharedAssert.True(response.Embeddings.All(e => e.Values.Count > 0), "Every sparse vector should have entries.");
        }

        private static async Task RunRerankTestsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            using LiveProvider provider = LiveProvider.Create(configuration);
            RerankClientBase client = provider.Rerank!;

            List<string> documents = new List<string>
            {
                "The Great Wall of China is thousands of kilometers long.",
                "Photosynthesis converts light into chemical energy in plants.",
                "Paris is the capital and largest city of France.",
                "Bananas are rich in potassium.",
            };

            if (configuration.ProviderType == "tei" && !await TeiServesAsync(provider, "reranker", token).ConfigureAwait(false))
            {
                RerankResponse rejected = await client.RerankAsync("What is the capital of France?", documents, null, token).ConfigureAwait(false);
                SharedAssert.False(rejected.Success, "TEI /rerank should fail when the server does not host a reranker.");
                SharedAssert.NotEmpty(rejected.Error, "TEI /rerank on a non-reranker should report an error.");
                return;
            }

            RerankResponse response = await client.RerankAsync("What is the capital of France?", documents, null, token).ConfigureAwait(false);
            SharedAssert.True(response.Success, "Rerank should succeed. " + response.Error);
            SharedAssert.Equal(documents.Count, response.Results.Count, "Rerank should score every document.");
            SharedAssert.Equal(2, response.Results[0].Index, "Rerank should rank the passage about the capital of France first.");
            for (int i = 1; i < response.Results.Count; i++)
            {
                SharedAssert.True(response.Results[i - 1].Score >= response.Results[i].Score, "Rerank results should be sorted by score.");
            }

            RerankResponse top = await client.RerankAsync("What is the capital of France?", documents, new RerankOptions { TopN = 2, ReturnDocuments = true }, token).ConfigureAwait(false);
            SharedAssert.True(top.Success, "Rerank with TopN should succeed.");
            SharedAssert.Equal(2, top.Results.Count, "Rerank with TopN should return TopN results.");
            SharedAssert.Equal(documents[top.Results[0].Index], top.Results[0].Document, "Rerank should attach document text on request.");

            await SharedAssert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => client.RerankAsync("q", documents, new RerankOptions { TopN = documents.Count + 1 }, token),
                "Rerank should reject a per-call TopN greater than the document count.").ConfigureAwait(false);
        }

        private static async Task RunClassifyTestsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            using LiveProvider provider = LiveProvider.Create(configuration);
            ClassificationClientBase client = provider.Classification!;
            List<string> inputs = new List<string> { "I absolutely love this product, it is wonderful!", "This is the worst purchase I have ever made." };

            // TEI /predict works for classifier models and for rerankers (single-label cross-encoders), and is rejected
            // (HTTP 424) for embedding models.
            if (configuration.ProviderType == "tei"
                && !await TeiServesAsync(provider, "classifier", token).ConfigureAwait(false)
                && !await TeiServesAsync(provider, "reranker", token).ConfigureAwait(false))
            {
                ClassificationResponse rejected = await client.ClassifyAsync(inputs, null, token).ConfigureAwait(false);
                SharedAssert.False(rejected.Success, "TEI /predict should fail when the server hosts an embedding model.");
                SharedAssert.True(rejected.StatusCode == 424, "TEI /predict on an embedding model should return HTTP 424.");
                return;
            }

            ClassificationOptions? options = null;
            if (configuration.ProviderType == "cohere")
            {
                CohereClassificationOptions cohereOptions = new CohereClassificationOptions();
                cohereOptions.Examples.Add(new ClassificationExample("I love it", "positive"));
                cohereOptions.Examples.Add(new ClassificationExample("This is fantastic", "positive"));
                cohereOptions.Examples.Add(new ClassificationExample("I hate it", "negative"));
                cohereOptions.Examples.Add(new ClassificationExample("This is awful", "negative"));
                options = cohereOptions;
            }

            ClassificationResponse response = await client.ClassifyAsync(inputs, options, token).ConfigureAwait(false);
            SharedAssert.True(response.Success, "Classification should succeed. " + response.Error);
            SharedAssert.Equal(2, response.Classifications.Count, "Classification should return one result per input.");
            SharedAssert.True(response.Classifications.All(c => !string.IsNullOrEmpty(c.Label) && c.Labels.Count > 0), "Every classification should have a top label and a label list.");
        }

        private static async Task RunDecisionTestsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            using LiveProvider provider = LiveProvider.Create(configuration);
            DecisionClientBase client = provider.Decision!;

            DecisionRequest request = CreateTicketDecision("I was charged twice for my order last week and I need my money back today or I am cancelling my account.");
            DecisionResponse response = await client.DecideAsync(request, null, token).ConfigureAwait(false);

            SharedAssert.True(response.Success, "Decision should succeed. " + response.Error);
            SharedAssert.True(response.StatusCode == 200, "Decision should return HTTP 200.");
            SharedAssert.Equal(3, response.Answers.Count, "Decision should answer every question.");

            ChoiceAnswer intent = response.Choice("intent");
            SharedAssert.True(intent.Value == "refund" || intent.Value == "billing", "The intent should be refund or billing, got '" + intent.Value + "'.");
            SharedAssert.True(intent.Confidence == null || (intent.Confidence >= 0 && intent.Confidence <= 1), "Choice confidence should be between 0 and 1.");

            BinaryAnswer urgent = response.Binary("urgent");
            SharedAssert.True(urgent.Probability >= 0 && urgent.Probability <= 1, "Binary probability should be between 0 and 1.");

            ScoreAnswer tone = response.Score("tone");
            SharedAssert.True(tone.Value >= 0 && tone.Value <= 3, "Score should be on the rubric's scale.");

            await SharedAssert.ThrowsAsync<ArgumentException>(
                () => client.DecideAsync(new DecisionRequest { State = "x", Questions = { DecisionQuestion.Choice("only", "Pick", "one") } }, null, token),
                "A choice question with one option should be rejected before sending.").ConfigureAwait(false);
        }

        private static async Task RunDecisionBatchTestsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            using LiveProvider provider = LiveProvider.Create(configuration);
            DecisionClientBase client = provider.Decision!;

            List<DecisionRequest> requests = new List<DecisionRequest>
            {
                CreateTicketDecision("Please refund the duplicate charge on my card."),
                CreateTicketDecision("How do I change my billing address?"),
                CreateTicketDecision("Your product is great, thanks for the help!"),
            };

            List<DecisionResponse> responses = await client.DecideAsync(requests, null, token).ConfigureAwait(false);
            SharedAssert.Equal(3, responses.Count, "A decision batch should return one response per request.");
            SharedAssert.True(responses.All(r => r.Success), "Every batch decision should succeed. " + string.Join("; ", responses.Where(r => !r.Success).Select(r => r.Error)));
            SharedAssert.True(responses.All(r => r.Answers.Count == 3), "Every batch decision should answer every question.");
        }

        private static async Task RunCallDetailsTestsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            using LiveProvider provider = LiveProvider.Create(configuration);
            ClientBase recorded;

            if (provider.Completion != null)
            {
                recorded = provider.Completion;
                await provider.Completion.ChatAsync("Ping", token: token).ConfigureAwait(false);
            }
            else if (provider.Decision != null)
            {
                recorded = provider.Decision;
                await provider.Decision.DecideAsync(CreateTicketDecision("Ping"), null, token).ConfigureAwait(false);
            }
            else if (configuration.ProviderType == "tei")
            {
                bool embedding = await TeiServesAsync(provider, "embedding", token).ConfigureAwait(false);
                bool reranker = await TeiServesAsync(provider, "reranker", token).ConfigureAwait(false);
                if (embedding) { recorded = provider.Embedding!; await provider.Embedding!.EmbedAsync("Ping", null, token).ConfigureAwait(false); }
                else if (reranker) { recorded = provider.Rerank!; await provider.Rerank!.RerankAsync("Ping", new List<string> { "Pong" }, null, token).ConfigureAwait(false); }
                else { recorded = provider.Classification!; await provider.Classification!.ClassifyAsync("Ping", null, token).ConfigureAwait(false); }
            }
            else
            {
                recorded = provider.Embedding!;
                await provider.Embedding!.EmbedAsync("Ping", null, token).ConfigureAwait(false);
            }

            List<CallDetail> details = recorded.CallDetails;
            SharedAssert.Equal(1, details.Count, "CallDetails should contain the request.");

            CallDetail last = details[0];
            SharedAssert.NotEmpty(last.Url, "CallDetail should have a URL.");
            SharedAssert.False(last.Url!.Contains("key=", StringComparison.OrdinalIgnoreCase), "CallDetail URLs should never contain an API key.");
            SharedAssert.Equal("POST", last.Method, "CallDetail method should be POST.");
            SharedAssert.NotEmpty(last.RequestBody, "CallDetail should have a request body.");
            SharedAssert.True(last.RequestHeaders != null && last.RequestHeaders.Count > 0, "CallDetail should have request headers.");
            SharedAssert.True(last.StatusCode.HasValue, "CallDetail should have a status code.");
            SharedAssert.NotEmpty(last.ResponseBody, "CallDetail should have a response body.");
            SharedAssert.True(last.ResponseTimeMs.HasValue && last.ResponseTimeMs.Value > 0, "CallDetail should have response time.");
            SharedAssert.True(last.Success, "CallDetail should be marked successful.");

            recorded.ClearCallDetails();
            SharedAssert.Equal(0, recorded.CallDetails.Count, "ClearCallDetails should empty the list.");
        }

        private static async Task RunListModelsTestsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            using LiveProvider provider = LiveProvider.Create(configuration);
            List<ModelInformation> models = await GetModelsAsync(provider.Models!, token).ConfigureAwait(false);

            SharedAssert.True(models.Count > 0, "ListModelsAsync should yield at least one model.");
            SharedAssert.True(models.All(model => !string.IsNullOrEmpty(model.Name)), "All listed models should have names.");
        }

        private static async Task RunModelExistsTestsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            using LiveProvider provider = LiveProvider.Create(configuration);
            ModelClientBase models = provider.Models!;

            string existing = configuration.ProviderType == "tei" || configuration.ProviderType == "azure"
                ? (await GetModelsAsync(models, token).ConfigureAwait(false))[0].Name
                : provider.Completion?.Model ?? provider.Embedding!.Model!;

            SharedAssert.True(await models.ModelExistsAsync(existing, token).ConfigureAwait(false), "Model '" + existing + "' should exist.");
            SharedAssert.False(await models.ModelExistsAsync(BogusModel, token).ConfigureAwait(false), "A nonexistent model should return false.");
            await SharedAssert.ThrowsAsync<ArgumentNullException>(() => models.ModelExistsAsync(" ", token), "A blank model name should be rejected.").ConfigureAwait(false);
        }

        private static async Task RunGetModelInformationTestsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            using LiveProvider provider = LiveProvider.Create(configuration);
            ModelClientBase models = provider.Models!;

            string existing = configuration.ProviderType == "tei" || configuration.ProviderType == "azure"
                ? (await GetModelsAsync(models, token).ConfigureAwait(false))[0].Name
                : provider.Completion?.Model ?? provider.Embedding!.Model!;

            ModelInformation? info = await models.GetModelInformationAsync(existing, token).ConfigureAwait(false);
            SharedAssert.NotNull(info, "Model information for '" + existing + "' should be found.");
            SharedAssert.NotEmpty(info!.Name, "Model information should have a name.");
            if (configuration.ProviderType == "tei") SharedAssert.True(info.Metadata.ContainsKey("model_type"), "TEI model information should report the model type.");

            SharedAssert.True(await models.GetModelInformationAsync(BogusModel, token).ConfigureAwait(false) == null, "A nonexistent model should return null model information.");
        }

        private static async Task RunPullModelTestsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            using LiveProvider provider = LiveProvider.Create(configuration);
            OllamaModelClient models = (OllamaModelClient)provider.Models!;

            List<string> statusMessages = new List<string>();
            bool pullResult = await models.PullModelAsync(
                provider.Completion!.Model!,
                progress =>
                {
                    statusMessages.Add(progress.Status);
                    return Task.CompletedTask;
                },
                token).ConfigureAwait(false);

            SharedAssert.True(pullResult, "PullModelAsync should return true for an existing Ollama model.");
            SharedAssert.True(statusMessages.Exists(status => string.Equals(status, "success", StringComparison.OrdinalIgnoreCase)), "PullModelAsync should emit a success status.");
        }

        private static async Task RunDeleteModelTestsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            using LiveProvider provider = LiveProvider.Create(configuration);
            OllamaModelClient models = (OllamaModelClient)provider.Models!;

            SharedAssert.False(await models.DeleteModelAsync(BogusModel, token).ConfigureAwait(false), "Deleting a nonexistent Ollama model should return false.");
            SharedAssert.True(models.CallDetails.Any(d => d.Method == "DELETE"), "The delete request should be recorded.");
        }

        private static async Task RunValidateConnectivityTestsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            using LiveProvider provider = LiveProvider.Create(configuration);
            foreach (ClientBase client in provider.All)
            {
                SharedAssert.True(await client.ValidateConnectivityAsync(token).ConfigureAwait(false), client.GetType().Name + " ValidateConnectivityAsync should return true with a valid endpoint.");
            }

            using LiveProvider bad = LiveProvider.Create(configuration, "http://localhost:1");
            foreach (ClientBase client in bad.All)
            {
                client.TimeoutMs = 5000;
                SharedAssert.False(await client.ValidateConnectivityAsync(token).ConfigureAwait(false), client.GetType().Name + " ValidateConnectivityAsync should return false with an unreachable endpoint.");
            }
        }

        private static async Task RunCancellationTestsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            using LiveProvider provider = LiveProvider.Create(configuration);
            using CancellationTokenSource cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            CancellationToken c = cancelled.Token;

            if (provider.Completion != null)
            {
                await SharedAssert.ThrowsAsync<OperationCanceledException>(() => provider.Completion.ChatAsync("x", token: c), "ChatAsync should respect a pre-cancelled token.").ConfigureAwait(false);
                await SharedAssert.ThrowsAsync<OperationCanceledException>(() => provider.Completion.ChatStreamingAsync("x", token: c), "ChatStreamingAsync should respect a pre-cancelled token.").ConfigureAwait(false);
                await SharedAssert.ThrowsAsync<OperationCanceledException>(() => provider.Completion.ToolChatAsync(CreateWeatherToolRequest(configuration), c), "ToolChatAsync should respect a pre-cancelled token.").ConfigureAwait(false);
                await SharedAssert.ThrowsAsync<OperationCanceledException>(() => provider.Completion.GenerateAsync("x", token: c), "GenerateAsync should respect a pre-cancelled token.").ConfigureAwait(false);
            }

            if (provider.Embedding != null)
            {
                await SharedAssert.ThrowsAsync<OperationCanceledException>(() => provider.Embedding.EmbedAsync("x", null, c), "EmbedAsync should respect a pre-cancelled token.").ConfigureAwait(false);
                await SharedAssert.ThrowsAsync<OperationCanceledException>(() => provider.Embedding.EmbedAsync(new List<string> { "a", "b" }, null, c), "Batch EmbedAsync should respect a pre-cancelled token.").ConfigureAwait(false);
            }

            if (provider.SparseEmbedding != null)
                await SharedAssert.ThrowsAsync<OperationCanceledException>(() => provider.SparseEmbedding.EmbedSparseAsync("x", null, c), "EmbedSparseAsync should respect a pre-cancelled token.").ConfigureAwait(false);

            if (provider.Rerank != null)
                await SharedAssert.ThrowsAsync<OperationCanceledException>(() => provider.Rerank.RerankAsync("q", new List<string> { "d" }, null, c), "RerankAsync should respect a pre-cancelled token.").ConfigureAwait(false);

            if (provider.Classification != null)
                await SharedAssert.ThrowsAsync<OperationCanceledException>(() => provider.Classification.ClassifyAsync("x", null, c), "ClassifyAsync should respect a pre-cancelled token.").ConfigureAwait(false);

            if (provider.Decision != null)
                await SharedAssert.ThrowsAsync<OperationCanceledException>(() => provider.Decision.DecideAsync(CreateTicketDecision("x"), null, c), "DecideAsync should respect a pre-cancelled token.").ConfigureAwait(false);

            if (provider.Models != null)
                await SharedAssert.ThrowsAsync<OperationCanceledException>(() => provider.Models.GetModelInformationAsync("x", c), "GetModelInformationAsync should respect a pre-cancelled token.").ConfigureAwait(false);

            foreach (ClientBase client in provider.All)
            {
                await SharedAssert.ThrowsAsync<OperationCanceledException>(() => client.ValidateConnectivityAsync(c), client.GetType().Name + " ValidateConnectivityAsync should respect a pre-cancelled token.").ConfigureAwait(false);
            }
        }

        #endregion

        #region Helpers

        private static DecisionRequest CreateTicketDecision(string ticket)
        {
            return new DecisionRequest
            {
                State = ticket,
                Questions =
                {
                    DecisionQuestion.Choice("intent", "What does the customer want?",
                        DecisionOption.Of("refund", "Wants money back"),
                        DecisionOption.Of("billing", "A question about a charge or an invoice"),
                        DecisionOption.Of("praise", "Positive feedback"),
                        DecisionOption.Of("other")),
                    DecisionQuestion.Binary("urgent", "Does the customer need a response today?"),
                    DecisionQuestion.Score("tone", "How upset is the customer?", "calm", "annoyed", "angry", "furious")
                }
            };
        }

        private static ToolChatRequest CreateWeatherToolRequest(ProviderTestConfiguration configuration)
        {
            ToolChatRequest request = new ToolChatRequest();
            request.Messages.Add(ChatMessage.System("Use tools when they are helpful. Keep final answers concise."));
            request.Messages.Add(ChatMessage.User("What is the current weather in Seattle? Use get_weather if tool calling is available."));
            request.Tools.Add(ToolDefinition.Function("get_weather", "Get current weather for a city.", WeatherParameters()));
            request.ToolChoice = "auto";
            request.Options = new CompletionOptions { MaxTokens = ResolveLiveMaxTokens(configuration.ProviderType, 256) };
            if (AcceptsSampling(configuration)) request.Options.Temperature = 0.0;
            return request;
        }

        private static Dictionary<string, object> WeatherParameters()
        {
            return new Dictionary<string, object>
            {
                { "type", "object" },
                { "properties", new Dictionary<string, object>
                    {
                        { "city", new Dictionary<string, object> { { "type", "string" }, { "description", "City name." } } },
                        { "unit", new Dictionary<string, object> { { "type", "string" }, { "enum", new List<string> { "fahrenheit", "celsius" } } } }
                    }
                },
                { "required", new List<string> { "city" } }
            };
        }

        private static void AppendWeatherToolResults(ToolChatRequest request, List<ToolCall> toolCalls, string result)
        {
            foreach (ToolCall call in toolCalls)
            {
                request.Messages.Add(ChatMessage.ToolResult(call.Id, call.Name, result));
            }
        }

        private static bool IsToolCapabilityError(string? error)
        {
            if (string.IsNullOrWhiteSpace(error)) return false;

            return error.Contains("does not support tools", StringComparison.OrdinalIgnoreCase)
                || error.Contains("doesn't support tools", StringComparison.OrdinalIgnoreCase)
                || error.Contains("does not support tool", StringComparison.OrdinalIgnoreCase)
                || error.Contains("function calling is not supported", StringComparison.OrdinalIgnoreCase)
                || error.Contains("does not support function calling", StringComparison.OrdinalIgnoreCase);
        }

        private static bool AcceptsSampling(ProviderTestConfiguration configuration)
        {
            // Current Claude models (directly and through Bedrock) reject sampling parameters.
            return configuration.ProviderType != "anthropic" && configuration.ProviderType != "bedrock";
        }

        private static async Task<List<ModelInformation>> GetModelsAsync(ModelClientBase client, CancellationToken token)
        {
            List<ModelInformation> models = new List<ModelInformation>();
            await foreach (ModelInformation model in client.ListModelsAsync(token).ConfigureAwait(false))
            {
                if (!string.IsNullOrEmpty(model.Name)) models.Add(model);
            }
            return models;
        }

        private static async Task<List<string>> GetModelNamesAsync(ModelClientBase client, CancellationToken token)
        {
            return (await GetModelsAsync(client, token).ConfigureAwait(false)).Select(model => model.Name).ToList();
        }

        private static CompletionOptions CreateCompletionOptions(string providerType)
        {
            int maxTokens = ResolveLiveMaxTokens(providerType, 64);
            switch (providerType)
            {
                case "ollama":
                    return new OllamaCompletionOptions { Temperature = 0.5, TopP = 0.9, MaxTokens = maxTokens, TopK = 40, RepeatPenalty = 1.1, Seed = 42 };
                case "openai":
                case "azure":
                    return new OpenAiCompletionOptions { Temperature = 0.5, TopP = 0.9, MaxTokens = maxTokens, FrequencyPenalty = 0.0, PresencePenalty = 0.0, Seed = 42 };
                case "gemini":
                case "vertex":
                    return new GeminiCompletionOptions { Temperature = 0.5, TopP = 0.9, MaxTokens = maxTokens, TopK = 40 };
                case "anthropic":
                    return new AnthropicCompletionOptions { MaxTokens = maxTokens };
                case "cohere":
                    return new CohereCompletionOptions { Temperature = 0.5, TopP = 0.9, MaxTokens = maxTokens, TopK = 40, Seed = 42 };
                default:
                    return new CompletionOptions { MaxTokens = maxTokens };
            }
        }

        private static EmbeddingOptions CreateEmbeddingOptions(ProviderTestConfiguration configuration)
        {
            switch (configuration.ProviderType)
            {
                case "ollama": return new OllamaEmbeddingOptions { ContextLength = 2048 };
                case "openai": return new OpenAiEmbeddingOptions { Dimensions = 256 };
                case "azure": return new OpenAiEmbeddingOptions();
                case "gemini": return new GeminiEmbeddingOptions { TaskType = "RETRIEVAL_DOCUMENT" };
                case "vertex": return new VertexAiEmbeddingOptions { TaskType = "RETRIEVAL_DOCUMENT" };
                case "bedrock": return new BedrockEmbeddingOptions { Normalize = true };
                case "voyageai": return new VoyageAiEmbeddingOptions { InputType = "document" };
                case "cohere": return new CohereEmbeddingOptions { InputType = "search_document" };
                case "tei": return new TeiEmbeddingOptions { Normalize = true, Truncate = true };
                default: return new EmbeddingOptions();
            }
        }

        private static int ResolveLiveMaxTokens(string providerType, int defaultMaxTokens)
        {
            // Reasoning models spend tokens thinking before they answer.
            if (providerType == "ollama" || providerType == "openai" || providerType == "azure" || providerType == "anthropic"
                || providerType == "gemini" || providerType == "vertex" || providerType == "bedrock")
                return Math.Max(defaultMaxTokens, 1024);

            return defaultMaxTokens;
        }

        private static bool ModelNameMatches(string available, string requested)
        {
            if (string.Equals(available, requested, StringComparison.OrdinalIgnoreCase)) return true;

            string availableBase = available.Contains(':') ? available.Substring(0, available.IndexOf(':')) : available;
            string requestedBase = requested.Contains(':') ? requested.Substring(0, requested.IndexOf(':')) : requested;
            return string.Equals(availableBase, requestedBase, StringComparison.OrdinalIgnoreCase);
        }

        private static bool VectorsEqual(float[] a, float[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (Math.Abs(a[i] - b[i]) > 0.00001f) return false;
            }
            return true;
        }

        private static async Task<bool> TeiServesAsync(LiveProvider provider, string modelType, CancellationToken token)
        {
            List<ModelInformation> models = await GetModelsAsync(provider.Models!, token).ConfigureAwait(false);
            if (models.Count == 0) throw new TestFailureException("TEI /info did not return the hosted model.");

            return models[0].Metadata.TryGetValue("model_type", out string? actual)
                && string.Equals(actual, modelType, StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
}
