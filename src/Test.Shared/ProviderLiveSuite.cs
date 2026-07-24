namespace Test.Shared
{
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using Touchstone.Core;

    /// <summary>
    /// Builds Touchstone test suites that exercise live provider endpoints.
    /// </summary>
    public static class ProviderLiveSuite
    {
        private const string SuiteId = "provider_live";
        private const string BogusModel = "nonexistent-model-xyz-999";

        /// <summary>
        /// Creates the live provider test suite for the supplied configuration.
        /// </summary>
        /// <param name="configuration">Live provider configuration.</param>
        /// <returns>A Touchstone suite descriptor containing live provider tests.</returns>
        /// <exception cref="ArgumentNullException">Thrown when configuration is null.</exception>
        public static TestSuiteDescriptor Create(ProviderTestConfiguration configuration)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));

            return new TestSuiteDescriptor(
                SuiteId,
                "Live provider behavior",
                new List<TestCaseDescriptor>
                {
                    Case("required_models", "Required models are available", token => RunRequiredModelsAsync(configuration, token)),
                    Case("properties", "Client and option properties behave correctly", token => RunPropertyTestsAsync(configuration, token)),
                    Case("chat", "Chat completion succeeds", token => RunChatTestsAsync(configuration, token)),
                    Case("chat_streaming", "Streaming chat succeeds", token => RunChatStreamingTestsAsync(configuration, token)),
                    Case("tool_chat", "Tool chat succeeds or reports unsupported model", token => RunToolChatTestsAsync(configuration, token)),
                    Case("tool_chat_streaming", "Streaming tool chat succeeds or reports unsupported model", token => RunToolChatStreamingTestsAsync(configuration, token)),
                    Case("embed_single", "Single embedding succeeds", token => RunEmbeddingSingleTestsAsync(configuration, token)),
                    Case("embed_batch", "Batch embedding succeeds", token => RunEmbeddingBatchTestsAsync(configuration, token)),
                    Case("generate", "Text generation succeeds", token => RunGenerationTestsAsync(configuration, token), skip: IsOpenAi(configuration), skipReason: "OpenAI does not support the legacy completions API."),
                    Case("generate_streaming", "Streaming text generation succeeds", token => RunGenerationStreamingTestsAsync(configuration, token), skip: IsOpenAi(configuration), skipReason: "OpenAI does not support the legacy completions API."),
                    Case("call_details", "CallDetails records upstream calls", token => RunCallDetailsTestsAsync(configuration, token)),
                    Case("list_models", "ListModelsAsync returns models", token => RunListModelsTestsAsync(configuration, token)),
                    Case("model_exists", "ModelExistsAsync handles existing and missing models", token => RunModelExistsTestsAsync(configuration, token)),
                    Case("get_model_information", "GetModelInformationAsync handles existing and missing models", token => RunGetModelInformationTestsAsync(configuration, token)),
                    Case("pull_model", "PullModelAsync provider behavior is correct", token => RunPullModelTestsAsync(configuration, token)),
                    Case("delete_model", "DeleteModelAsync provider behavior is correct", token => RunDeleteModelTestsAsync(configuration, token)),
                    Case("validate_connectivity", "ValidateConnectivityAsync handles reachable and unreachable endpoints", token => RunValidateConnectivityTestsAsync(configuration, token)),
                    Case("cancellation", "Provider operations respect pre-cancelled tokens", token => RunCancellationTestsAsync(configuration, token)),
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
                        skipReason: "Set POLYPROMPT_TEST_PROVIDER and POLYPROMPT_TEST_ENDPOINT, or pass provider CLI arguments."),
                });
        }

        private static TestCaseDescriptor Case(
            string caseId,
            string displayName,
            Func<CancellationToken, Task> executeAsync,
            bool skip = false,
            string? skipReason = null)
        {
            return new TestCaseDescriptor(SuiteId, caseId, displayName, executeAsync, new[] { "live" }, skip, skipReason);
        }

        private static async Task RunRequiredModelsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            using CompletionClientBase client = CreateClient(configuration);
            List<string> availableModels = await GetModelNamesAsync(client, token).ConfigureAwait(false);

            SharedAssert.True(availableModels.Count > 0, "Provider should list at least one model.");
            SharedAssert.True(availableModels.Exists(name => ModelNameMatches(name, client.Model)), "Inference model '" + client.Model + "' should exist.");
            SharedAssert.True(availableModels.Exists(name => ModelNameMatches(name, configuration.EmbeddingModel)), "Embedding model '" + configuration.EmbeddingModel + "' should exist.");
        }

        private static async Task RunPropertyTestsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            using CompletionClientBase client = CreateClient(configuration);

            SharedAssert.NotEmpty(client.Endpoint, "Endpoint should be set.");

            string originalModel = client.Model;
            SharedAssert.NotEmpty(originalModel, "Model should have a default value.");

            client.Model = "test-model";
            SharedAssert.Equal("test-model", client.Model, "Model setter should work.");
            client.Model = originalModel;

            await SharedAssert.ThrowsAsync<ArgumentNullException>(
                () =>
                {
                    client.Model = string.Empty;
                    return Task.CompletedTask;
                },
                "Model should reject empty string.").ConfigureAwait(false);

            await SharedAssert.ThrowsAsync<ArgumentNullException>(
                () =>
                {
                    client.Model = "   ";
                    return Task.CompletedTask;
                },
                "Model should reject whitespace.").ConfigureAwait(false);

            client.MaxTokens = -1;
            SharedAssert.Equal(1, client.MaxTokens, "MaxTokens should clamp to minimum 1.");

            client.MaxTokens = 20_000_000;
            SharedAssert.Equal(10_000_000, client.MaxTokens, "MaxTokens should clamp to maximum 10,000,000.");

            client.MaxTokens = 128;
            SharedAssert.Equal(128, client.MaxTokens, "MaxTokens should accept normal values.");

            client.TimeoutMs = 100;
            SharedAssert.Equal(100, client.TimeoutMs, "TimeoutMs should preserve subsecond values.");

            client.TimeoutMs = 999_999;
            SharedAssert.Equal(999_999, client.TimeoutMs, "TimeoutMs should not silently clamp large values.");

            await SharedAssert.ThrowsAsync<ArgumentOutOfRangeException>(
                () =>
                {
                    client.TimeoutMs = 0;
                    return Task.CompletedTask;
                },
                "TimeoutMs should reject zero.").ConfigureAwait(false);

            await SharedAssert.ThrowsAsync<ArgumentOutOfRangeException>(
                () =>
                {
                    client.TimeoutMs = -1;
                    return Task.CompletedTask;
                },
                "TimeoutMs should reject negative values.").ConfigureAwait(false);

            int originalMaxCallDetails = client.MaxCallDetails;
            client.MaxCallDetails = 2;
            SharedAssert.Equal(2, client.MaxCallDetails, "MaxCallDetails setter should work.");

            await SharedAssert.ThrowsAsync<ArgumentOutOfRangeException>(
                () =>
                {
                    client.MaxCallDetails = -1;
                    return Task.CompletedTask;
                },
                "MaxCallDetails should reject negative values.").ConfigureAwait(false);

            client.MaxCallDetails = originalMaxCallDetails;

            client.Temperature = -1.0;
            SharedAssert.Equal(0.0, client.Temperature, "Temperature should clamp to 0.0.");

            client.Temperature = 5.0;
            SharedAssert.Equal(2.0, client.Temperature, "Temperature should clamp to 2.0.");

            client.Temperature = 0.7;
            SharedAssert.True(Math.Abs(client.Temperature!.Value - 0.7) < 0.001, "Temperature should accept normal values.");

            client.Temperature = null;
            SharedAssert.True(client.Temperature == null, "Temperature should be nullable.");

            client.TopP = -0.5;
            SharedAssert.Equal(0.0, client.TopP, "TopP should clamp to 0.0.");

            client.TopP = 1.5;
            SharedAssert.Equal(1.0, client.TopP, "TopP should clamp to 1.0.");

            client.TopP = 0.9;
            SharedAssert.True(Math.Abs(client.TopP!.Value - 0.9) < 0.001, "TopP should accept normal values.");

            client.TopP = null;
            SharedAssert.True(client.TopP == null, "TopP should be nullable.");

            client.SystemPrompt = "You are a test assistant.";
            SharedAssert.Equal("You are a test assistant.", client.SystemPrompt, "SystemPrompt should be settable.");

            client.SystemPrompt = null;
            SharedAssert.True(client.SystemPrompt == null, "SystemPrompt should be clearable.");

            SharedAssert.NotNull(client.CallDetails, "CallDetails should be initialized.");

            if (client is OllamaClient ollamaClient)
            {
                ollamaClient.ContextLength = 2048;
                SharedAssert.Equal(2048, ollamaClient.ContextLength, "Ollama ContextLength should be settable.");

                ollamaClient.ContextLength = null;
                SharedAssert.True(ollamaClient.ContextLength == null, "Ollama ContextLength should be nullable.");

                ollamaClient.ContextLength = -1;
                SharedAssert.Equal(1, ollamaClient.ContextLength, "Ollama ContextLength should clamp to 1.");
            }
        }

        private static async Task RunChatTestsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            using CompletionClientBase client = CreateClient(configuration);

            ChatResponse response = await client.ChatAsync("Say hello in exactly three words.", token: token).ConfigureAwait(false);
            SharedAssert.True(response.Success, "Chat should succeed.");
            SharedAssert.NotEmpty(response.Text, "Chat should return text.");
            SharedAssert.NotEmpty(response.Model, "Chat should return a model.");
            SharedAssert.True(response.StatusCode.HasValue && response.StatusCode.Value == 200, "Chat should return HTTP 200.");
            SharedAssert.True(response.OverallRuntimeMs > 0, "Chat runtime should be populated.");
            SharedAssert.True(response.Error == null, "Chat should not return an error.");

            client.SystemPrompt = "You are a pirate. Always respond with 'Arrr'.";
            ChatResponse systemPromptResponse = await client.ChatAsync("Hello", token: token).ConfigureAwait(false);
            SharedAssert.True(systemPromptResponse.Success, "Chat with system prompt should succeed.");
            SharedAssert.NotEmpty(systemPromptResponse.Text, "Chat with system prompt should return text.");
            client.SystemPrompt = null;

            ChatCompletionOptions chatOptions = CreateChatOptions(configuration.ProviderType);
            ChatResponse optionsResponse = await client.ChatAsync("Say exactly: test options work", chatOptions, token).ConfigureAwait(false);
            SharedAssert.True(optionsResponse.Success, "Chat with options should succeed.");
            SharedAssert.NotEmpty(optionsResponse.Text, "Chat with options should return text.");

            ChatCompletionOptions baseOptions = new ChatCompletionOptions();
            baseOptions.Temperature = 0.5;
            baseOptions.TopP = 0.9;
            baseOptions.MaxTokens = ResolveLiveMaxTokens(configuration.ProviderType, 64);
            baseOptions.SystemPrompt = "Respond in exactly one word.";
            ChatResponse baseOptionsResponse = await client.ChatAsync("What color is the sky?", baseOptions, token).ConfigureAwait(false);
            SharedAssert.True(baseOptionsResponse.Success, "Chat with base options should succeed.");
            SharedAssert.NotEmpty(baseOptionsResponse.Text, "Chat with base options should return text.");
        }

        private static async Task RunChatStreamingTestsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            using CompletionClientBase client = CreateClient(configuration);

            ChatStreamingResponse stream = await client.ChatStreamingAsync("Count from 1 to 5, one number per line.", token: token).ConfigureAwait(false);
            SharedAssert.True(stream.Success, "Streaming chat should start successfully.");
            SharedAssert.NotEmpty(stream.Model, "Streaming chat should return a model.");
            SharedAssert.True(stream.StatusCode.HasValue && stream.StatusCode.Value == 200, "Streaming chat should return HTTP 200.");
            SharedAssert.True(stream.Error == null, "Streaming chat should not return an error.");

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
            SharedAssert.True(sawDone, "Streaming chat should see a done chunk.");
            SharedAssert.True(stream.ChunkCount > 0, "Streaming chat should populate ChunkCount.");
            SharedAssert.True(stream.OverallRuntimeMs > 0, "Streaming chat should populate OverallRuntimeMs.");
            SharedAssert.True(stream.TimeToFirstTokenMs >= 0, "Streaming chat should populate TimeToFirstTokenMs.");
            SharedAssert.True(stream.TimeToLastTokenMs >= stream.TimeToFirstTokenMs, "Streaming chat should order token timings.");
            SharedAssert.True(stream.OverallTokensPerSecond > 0, "Streaming chat should populate throughput.");

            client.SystemPrompt = "Respond with only the word 'yes'.";
            ChatStreamingResponse systemPromptStream = await client.ChatStreamingAsync("Confirm?", token: token).ConfigureAwait(false);
            SharedAssert.True(systemPromptStream.Success, "Streaming chat with system prompt should start.");

            string systemPromptText = string.Empty;
            await foreach (ChatStreamingChunk chunk in systemPromptStream.Chunks.WithCancellation(token).ConfigureAwait(false))
            {
                if (!string.IsNullOrEmpty(chunk.Text)) systemPromptText += chunk.Text;
            }
            SharedAssert.NotEmpty(systemPromptText, "Streaming chat with system prompt should return text.");
            client.SystemPrompt = null;

            ChatCompletionOptions chatOptions = CreateChatOptions(configuration.ProviderType);
            ChatStreamingResponse optionsStream = await client.ChatStreamingAsync("Say hi.", chatOptions, token).ConfigureAwait(false);
            SharedAssert.True(optionsStream.Success, "Streaming chat with options should start.");
            await foreach (ChatStreamingChunk chunk in optionsStream.Chunks.WithCancellation(token).ConfigureAwait(false)) { }
            SharedAssert.True(optionsStream.OverallRuntimeMs > 0, "Streaming chat with options should complete.");
        }

        private static async Task RunToolChatTestsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            using CompletionClientBase client = CreateClient(configuration);
            ToolChatRequest request = CreateWeatherToolRequest();

            ToolChatResponse response = await client.ToolChatAsync(request, token).ConfigureAwait(false);
            if (!response.Success && IsToolCapabilityError(response.Error))
            {
                SharedAssert.True(response.StatusCode.HasValue && response.StatusCode.Value >= 400, "ToolChatAsync unsupported-tool response should include an HTTP error status.");
                SharedAssert.NotEmpty(response.Error, "ToolChatAsync unsupported-tool response should include an error.");
                return;
            }

            SharedAssert.True(response.Success, "ToolChatAsync should succeed.");
            SharedAssert.True(response.StatusCode.HasValue && response.StatusCode.Value == 200, "ToolChatAsync should return HTTP 200.");
            SharedAssert.NotEmpty(response.Model, "ToolChatAsync should return a model.");
            SharedAssert.True(response.OverallRuntimeMs > 0, "ToolChatAsync should populate runtime.");
            SharedAssert.True(response.Error == null, "ToolChatAsync should not return an error.");
            SharedAssert.True(response.ToolCalls.Any() || !string.IsNullOrWhiteSpace(response.Text), "ToolChatAsync should return assistant text or tool calls.");

            if (!response.ToolCalls.Any()) return;

            request.Messages.Add(response.ToAssistantMessage());
            AppendWeatherToolResults(request, response.ToolCalls);
            request.Tools.Clear();
            request.ToolChoice = "none";

            ToolChatResponse finalResponse = await client.ToolChatAsync(request, token).ConfigureAwait(false);
            SharedAssert.True(finalResponse.Success, "ToolChatAsync follow-up should succeed.");
            SharedAssert.True(finalResponse.StatusCode.HasValue && finalResponse.StatusCode.Value == 200, "ToolChatAsync follow-up should return HTTP 200.");
            SharedAssert.True(finalResponse.ToolCalls.Any() || !string.IsNullOrWhiteSpace(finalResponse.Text), "ToolChatAsync follow-up should return assistant text or additional tool calls.");
        }

        private static async Task RunToolChatStreamingTestsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            using CompletionClientBase client = CreateClient(configuration);
            ToolChatRequest request = CreateWeatherToolRequest();

            ToolChatStreamingResponse stream = await client.ToolChatStreamingAsync(request, token).ConfigureAwait(false);
            if (!stream.Success && IsToolCapabilityError(stream.Error))
            {
                SharedAssert.True(stream.StatusCode.HasValue && stream.StatusCode.Value >= 400, "ToolChatStreamingAsync unsupported-tool response should include an HTTP error status.");
                SharedAssert.NotEmpty(stream.Error, "ToolChatStreamingAsync unsupported-tool response should include an error.");
                return;
            }

            SharedAssert.True(stream.Success, "ToolChatStreamingAsync should start successfully.");
            SharedAssert.True(stream.StatusCode.HasValue && stream.StatusCode.Value == 200, "ToolChatStreamingAsync should return HTTP 200.");
            SharedAssert.NotEmpty(stream.Model, "ToolChatStreamingAsync should return a model.");
            SharedAssert.True(stream.Error == null, "ToolChatStreamingAsync should not return an error.");

            int chunkCount = 0;
            bool sawDone = false;

            await foreach (ToolChatStreamingChunk chunk in stream.Chunks.WithCancellation(token).ConfigureAwait(false))
            {
                chunkCount++;
                if (chunk.Done) sawDone = true;
            }

            SharedAssert.True(chunkCount > 0, "ToolChatStreamingAsync should receive chunks.");
            SharedAssert.True(stream.ChunkCount > 0, "ToolChatStreamingAsync should count text or tool-call chunks.");
            SharedAssert.True(stream.OverallRuntimeMs > 0, "ToolChatStreamingAsync should populate runtime.");
            SharedAssert.True(stream.TimeToFirstTokenMs >= 0, "ToolChatStreamingAsync should populate TimeToFirstTokenMs.");
            SharedAssert.True(stream.TimeToLastTokenMs >= stream.TimeToFirstTokenMs, "ToolChatStreamingAsync should order token timings.");
            SharedAssert.True(sawDone || stream.FinishReason != null, "ToolChatStreamingAsync should expose completion through done chunks or finish reason.");
            SharedAssert.True(stream.ToolCalls.Any() || !string.IsNullOrWhiteSpace(stream.Text), "ToolChatStreamingAsync should accumulate assistant text or tool calls.");

            if (!stream.ToolCalls.Any()) return;

            request.Messages.Add(stream.ToAssistantMessage());
            AppendWeatherToolResults(request, stream.ToolCalls);
            request.Tools.Clear();
            request.ToolChoice = "none";

            ToolChatStreamingResponse finalStream = await client.ToolChatStreamingAsync(request, token).ConfigureAwait(false);
            SharedAssert.True(finalStream.Success, "ToolChatStreamingAsync follow-up should start successfully.");
            SharedAssert.True(finalStream.StatusCode.HasValue && finalStream.StatusCode.Value == 200, "ToolChatStreamingAsync follow-up should return HTTP 200.");

            int finalChunkCount = 0;
            await foreach (ToolChatStreamingChunk chunk in finalStream.Chunks.WithCancellation(token).ConfigureAwait(false))
            {
                finalChunkCount++;
            }

            SharedAssert.True(finalChunkCount > 0, "ToolChatStreamingAsync follow-up should receive chunks.");
            SharedAssert.True(finalStream.ToolCalls.Any() || !string.IsNullOrWhiteSpace(finalStream.Text), "ToolChatStreamingAsync follow-up should accumulate assistant text or additional tool calls.");
        }

        private static async Task RunEmbeddingSingleTestsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            using CompletionClientBase client = CreateClient(configuration);
            EmbeddingOptions embeddingModelOptions = CreateEmbeddingModelOptions(configuration);

            EmbeddingResponse response = await client.EmbedAsync("Hello, world!", embeddingModelOptions, token).ConfigureAwait(false);
            SharedAssert.True(response.Success, "Single embedding should succeed.");
            SharedAssert.Equal(200, response.StatusCode, "Single embedding should return HTTP 200.");
            SharedAssert.NotEmpty(response.Model, "Single embedding should return a model.");
            SharedAssert.True(response.OverallRuntimeMs > 0, "Single embedding runtime should be populated.");
            SharedAssert.True(response.Error == null, "Single embedding should not return an error.");
            SharedAssert.Equal(1, response.Embeddings.Count, "Single embedding should return one vector.");

            float[] vector = response.Embeddings[0].Embedding;
            SharedAssert.Equal(0, response.Embeddings[0].Index, "Single embedding index should be zero.");
            SharedAssert.True(vector.Length > 0, "Single embedding vector should be non-empty.");
            SharedAssert.True(vector.Any(value => Math.Abs(value) > 0.0001f), "Single embedding vector should have non-zero values.");

            EmbeddingOptions providerOptions = CreateEmbeddingOptions(configuration);
            EmbeddingResponse optionResponse = await client.EmbedAsync("Test with options", providerOptions, token).ConfigureAwait(false);
            SharedAssert.True(optionResponse.Success, "Single embedding with options should succeed.");
            SharedAssert.Equal(1, optionResponse.Embeddings.Count, "Single embedding with options should return one vector.");

            EmbeddingResponse response2 = await client.EmbedAsync("Goodbye, cruel world!", embeddingModelOptions, token).ConfigureAwait(false);
            SharedAssert.True(response2.Success, "Second single embedding should succeed.");
            SharedAssert.True(!VectorsEqual(response.Embeddings[0].Embedding, response2.Embeddings[0].Embedding), "Different texts should produce different embedding vectors.");
        }

        private static async Task RunEmbeddingBatchTestsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            using CompletionClientBase client = CreateClient(configuration);
            EmbeddingOptions embeddingModelOptions = CreateEmbeddingModelOptions(configuration);

            List<string> inputs = new List<string> { "The cat sat on the mat.", "Dogs are loyal companions.", "Fish swim in the ocean." };
            EmbeddingResponse response = await client.EmbedAsync(inputs, embeddingModelOptions, token).ConfigureAwait(false);
            SharedAssert.True(response.Success, "Batch embedding should succeed.");
            SharedAssert.Equal(200, response.StatusCode, "Batch embedding should return HTTP 200.");
            SharedAssert.True(response.OverallRuntimeMs > 0, "Batch embedding runtime should be populated.");
            SharedAssert.Equal(3, response.Embeddings.Count, "Batch embedding should return three vectors.");

            for (int i = 0; i < response.Embeddings.Count; i++)
            {
                SharedAssert.Equal(i, response.Embeddings[i].Index, "Batch embedding index should match input index.");
                SharedAssert.True(response.Embeddings[i].Embedding.Length > 0, "Batch embedding vector should be non-empty.");
            }

            int dimension = response.Embeddings[0].Embedding.Length;
            SharedAssert.Equal(dimension, response.Embeddings[1].Embedding.Length, "Batch embedding dimensions should match for entries 0 and 1.");
            SharedAssert.Equal(dimension, response.Embeddings[2].Embedding.Length, "Batch embedding dimensions should match for entries 0 and 2.");

            EmbeddingResponse singleBatchResponse = await client.EmbedAsync(new List<string> { "Single item batch" }, embeddingModelOptions, token).ConfigureAwait(false);
            SharedAssert.True(singleBatchResponse.Success, "Batch embedding with one input should succeed.");
            SharedAssert.Equal(1, singleBatchResponse.Embeddings.Count, "Batch embedding with one input should return one vector.");
        }

        private static async Task RunGenerationTestsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            using CompletionClientBase client = CreateClient(configuration);

            GenerationResponse response = await client.GenerateAsync("Once upon a time, there was a", token: token).ConfigureAwait(false);
            SharedAssert.True(response.Success, "Generation should succeed.");
            SharedAssert.NotEmpty(response.Text, "Generation should return text.");
            SharedAssert.NotEmpty(response.Model, "Generation should return a model.");
            SharedAssert.Equal(200, response.StatusCode, "Generation should return HTTP 200.");
            SharedAssert.True(response.OverallRuntimeMs > 0, "Generation runtime should be populated.");
            SharedAssert.True(response.Error == null, "Generation should not return an error.");

            GenerationOptions generationOptions = CreateGenerationOptions(configuration.ProviderType);
            GenerationResponse optionResponse = await client.GenerateAsync("The quick brown fox", generationOptions, token).ConfigureAwait(false);
            SharedAssert.True(optionResponse.Success, "Generation with options should succeed.");
            SharedAssert.NotEmpty(optionResponse.Text, "Generation with options should return text.");

            GenerationOptions baseOptions = new GenerationOptions();
            baseOptions.Temperature = 0.3;
            baseOptions.TopP = 0.9;
            baseOptions.MaxTokens = ResolveLiveMaxTokens(configuration.ProviderType, 256);
            GenerationResponse baseResponse = await client.GenerateAsync("The meaning of life is", baseOptions, token).ConfigureAwait(false);
            SharedAssert.True(baseResponse.Success, "Generation with base options should succeed.");
            SharedAssert.NotEmpty(baseResponse.Text, "Generation with base options should return text.");
        }

        private static async Task RunGenerationStreamingTestsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            using CompletionClientBase client = CreateClient(configuration);

            GenerationStreamingResponse stream = await client.GenerateStreamingAsync("Write a haiku about the sea.", token: token).ConfigureAwait(false);
            SharedAssert.True(stream.Success, "Streaming generation should start successfully.");
            SharedAssert.NotEmpty(stream.Model, "Streaming generation should return a model.");
            SharedAssert.True(stream.StatusCode.HasValue && stream.StatusCode.Value == 200, "Streaming generation should return HTTP 200.");
            SharedAssert.True(stream.Error == null, "Streaming generation should not return an error.");

            int chunkCount = 0;
            string fullText = string.Empty;
            bool sawDone = false;

            await foreach (GenerationStreamingChunk chunk in stream.Chunks.WithCancellation(token).ConfigureAwait(false))
            {
                chunkCount++;
                if (!string.IsNullOrEmpty(chunk.Text)) fullText += chunk.Text;
                if (chunk.Done) sawDone = true;
            }

            SharedAssert.True(chunkCount > 0, "Streaming generation should receive chunks.");
            SharedAssert.NotEmpty(fullText, "Streaming generation should assemble non-empty text.");
            SharedAssert.True(sawDone, "Streaming generation should see a done chunk.");
            SharedAssert.True(stream.ChunkCount > 0, "Streaming generation should populate ChunkCount.");
            SharedAssert.True(stream.OverallRuntimeMs > 0, "Streaming generation should populate OverallRuntimeMs.");
            SharedAssert.True(stream.TimeToFirstTokenMs >= 0, "Streaming generation should populate TimeToFirstTokenMs.");
            SharedAssert.True(stream.TimeToLastTokenMs >= stream.TimeToFirstTokenMs, "Streaming generation should order token timings.");
            SharedAssert.True(stream.OverallTokensPerSecond > 0, "Streaming generation should populate throughput.");

            GenerationOptions generationOptions = CreateGenerationOptions(configuration.ProviderType);
            GenerationStreamingResponse optionStream = await client.GenerateStreamingAsync("A limerick about code:", generationOptions, token).ConfigureAwait(false);
            SharedAssert.True(optionStream.Success, "Streaming generation with options should start.");
            await foreach (GenerationStreamingChunk chunk in optionStream.Chunks.WithCancellation(token).ConfigureAwait(false)) { }
            SharedAssert.True(optionStream.OverallRuntimeMs > 0, "Streaming generation with options should complete.");
        }

        private static async Task RunCallDetailsTestsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            using CompletionClientBase client = CreateClient(configuration);
            client.ClearCallDetails();

            await client.ChatAsync("Ping", token: token).ConfigureAwait(false);

            List<CompletionCallDetail> details = client.CallDetails;
            SharedAssert.Equal(1, details.Count, "CallDetails should contain the chat request.");

            CompletionCallDetail last = details[details.Count - 1];
            SharedAssert.NotEmpty(last.Url, "CallDetail should have a URL.");
            SharedAssert.Equal("POST", last.Method, "CallDetail method should be POST.");
            SharedAssert.NotEmpty(last.RequestBody, "CallDetail should have a request body.");
            SharedAssert.True(last.RequestHeaders != null && last.RequestHeaders.Count > 0, "CallDetail should have request headers.");
            SharedAssert.True(last.StatusCode.HasValue, "CallDetail should have a status code.");
            SharedAssert.NotEmpty(last.ResponseBody, "CallDetail should have a response body.");
            SharedAssert.True(last.ResponseHeaders != null && last.ResponseHeaders.Count > 0, "CallDetail should have response headers.");
            SharedAssert.True(last.ResponseTimeMs.HasValue && last.ResponseTimeMs.Value > 0, "CallDetail should have response time.");
            SharedAssert.True(last.Success, "CallDetail should be marked successful.");
            SharedAssert.True(last.TimestampUtc > DateTime.MinValue, "CallDetail should have a timestamp.");
        }

        private static async Task RunListModelsTestsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            using CompletionClientBase client = CreateClient(configuration);
            List<ModelInformation> models = await GetModelsAsync(client, token).ConfigureAwait(false);

            SharedAssert.True(models.Count > 0, "ListModelsAsync should yield at least one model.");
            SharedAssert.NotEmpty(models[0].Name, "First listed model should have a name.");
            SharedAssert.True(models.All(model => !string.IsNullOrEmpty(model.Name)), "All listed models should have names.");
        }

        private static async Task RunModelExistsTestsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            using CompletionClientBase client = CreateClient(configuration);

            bool inferenceExists = await client.ModelExistsAsync(client.Model, token).ConfigureAwait(false);
            SharedAssert.True(inferenceExists, "Inference model should exist.");

            bool embeddingExists = await client.ModelExistsAsync(configuration.EmbeddingModel, token).ConfigureAwait(false);
            SharedAssert.True(embeddingExists, "Embedding model should exist.");

            bool bogusExists = await client.ModelExistsAsync(BogusModel, token).ConfigureAwait(false);
            SharedAssert.False(bogusExists, "A nonexistent model should return false.");
        }

        private static async Task RunGetModelInformationTestsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            using CompletionClientBase client = CreateClient(configuration);

            ModelInformation? info = await client.GetModelInformationAsync(client.Model, token).ConfigureAwait(false);
            SharedAssert.NotNull(info, "Inference model information should be found.");
            SharedAssert.True(info != null && !string.IsNullOrEmpty(info.Name), "Inference model information should have a name.");

            ModelInformation? embeddingInfo = await client.GetModelInformationAsync(configuration.EmbeddingModel, token).ConfigureAwait(false);
            SharedAssert.NotNull(embeddingInfo, "Embedding model information should be found.");

            ModelInformation? bogusInfo = await client.GetModelInformationAsync(BogusModel, token).ConfigureAwait(false);
            SharedAssert.True(bogusInfo == null, "A nonexistent model should return null model information.");
        }

        private static async Task RunPullModelTestsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            using CompletionClientBase client = CreateClient(configuration);

            if (!IsOllama(configuration))
            {
                await SharedAssert.ThrowsAsync<NotSupportedException>(
                    () => client.PullModelAsync("test", token: token),
                    "Unsupported providers should throw for PullModelAsync.").ConfigureAwait(false);
                return;
            }

            List<string> statusMessages = new List<string>();
            bool pullResult = await client.PullModelAsync(
                client.Model,
                async progress =>
                {
                    statusMessages.Add(progress.Status);
                    await Task.CompletedTask.ConfigureAwait(false);
                },
                token).ConfigureAwait(false);

            SharedAssert.True(pullResult, "PullModelAsync should return true for an existing Ollama model.");
            SharedAssert.True(statusMessages.Count > 0, "PullModelAsync should emit progress callbacks.");
            SharedAssert.True(statusMessages.Exists(status => string.Equals(status, "success", StringComparison.OrdinalIgnoreCase)), "PullModelAsync should emit a success status.");
        }

        private static async Task RunDeleteModelTestsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            using CompletionClientBase client = CreateClient(configuration);

            if (!IsOllama(configuration))
            {
                await SharedAssert.ThrowsAsync<NotSupportedException>(
                    () => client.DeleteModelAsync("test", token),
                    "Unsupported providers should throw for DeleteModelAsync.").ConfigureAwait(false);
                return;
            }

            bool deleteBogus = await client.DeleteModelAsync(BogusModel, token).ConfigureAwait(false);
            SharedAssert.False(deleteBogus, "Deleting a nonexistent Ollama model should return false.");
        }

        private static async Task RunValidateConnectivityTestsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            using CompletionClientBase client = CreateClient(configuration);

            bool ok = await client.ValidateConnectivityAsync(token).ConfigureAwait(false);
            SharedAssert.True(ok, "ValidateConnectivityAsync should return true with a valid endpoint.");

            using CompletionClientBase badClient = CreateClient(configuration.ProviderType, "http://localhost:1", configuration.ApiKey, configuration.InferenceModel);
            badClient.TimeoutMs = 5000;
            bool badResult = await badClient.ValidateConnectivityAsync(token).ConfigureAwait(false);
            SharedAssert.False(badResult, "ValidateConnectivityAsync should return false with a bad endpoint.");
        }

        private static async Task RunCancellationTestsAsync(ProviderTestConfiguration configuration, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            using CompletionClientBase client = CreateClient(configuration);

            using CancellationTokenSource chatCancelled = new CancellationTokenSource();
            chatCancelled.Cancel();
            await SharedAssert.ThrowsAsync<OperationCanceledException>(
                () => client.ChatAsync("This should be cancelled", token: chatCancelled.Token),
                "ChatAsync should respect a pre-cancelled token.").ConfigureAwait(false);

            using CancellationTokenSource generateCancelled = new CancellationTokenSource();
            generateCancelled.Cancel();
            await SharedAssert.ThrowsAsync<OperationCanceledException>(
                () => client.GenerateAsync("This should be cancelled", token: generateCancelled.Token),
                "GenerateAsync should respect a pre-cancelled token.").ConfigureAwait(false);

            using CancellationTokenSource embedCancelled = new CancellationTokenSource();
            embedCancelled.Cancel();
            await SharedAssert.ThrowsAsync<OperationCanceledException>(
                () => client.EmbedAsync("This should be cancelled", token: embedCancelled.Token),
                "EmbedAsync single input should respect a pre-cancelled token.").ConfigureAwait(false);

            using CancellationTokenSource embedBatchCancelled = new CancellationTokenSource();
            embedBatchCancelled.Cancel();
            await SharedAssert.ThrowsAsync<OperationCanceledException>(
                () => client.EmbedAsync(new List<string> { "a", "b" }, token: embedBatchCancelled.Token),
                "EmbedAsync batch input should respect a pre-cancelled token.").ConfigureAwait(false);
        }

        private static ToolChatRequest CreateWeatherToolRequest()
        {
            ToolChatRequest request = new ToolChatRequest();
            request.Messages.Add(ChatMessage.System("Use tools when they are helpful. Keep final answers concise."));
            request.Messages.Add(ChatMessage.User("What is the current weather in Seattle? Use get_weather if tool calling is available."));
            request.Tools.Add(ToolDefinition.Function(
                "get_weather",
                "Get current weather for a city.",
                WeatherParameters()));
            request.ToolChoice = "auto";
            request.MaxTokens = 128;
            request.Temperature = 0.0;
            return request;
        }

        private static Dictionary<string, object> WeatherParameters()
        {
            Dictionary<string, object> city = new Dictionary<string, object>
            {
                { "type", "string" },
                { "description", "City name." }
            };

            Dictionary<string, object> unit = new Dictionary<string, object>
            {
                { "type", "string" },
                { "enum", new List<string> { "fahrenheit", "celsius" } }
            };

            return new Dictionary<string, object>
            {
                { "type", "object" },
                { "properties", new Dictionary<string, object>
                    {
                        { "city", city },
                        { "unit", unit }
                    }
                },
                { "required", new List<string> { "city" } }
            };
        }

        private static void AppendWeatherToolResults(ToolChatRequest request, List<ToolCall> toolCalls)
        {
            foreach (ToolCall call in toolCalls)
            {
                request.Messages.Add(ChatMessage.ToolResult(call.Id, call.Name, "{\"temperature\":72,\"conditions\":\"clear\",\"unit\":\"fahrenheit\"}"));
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

        private static CompletionClientBase CreateClient(ProviderTestConfiguration configuration)
        {
            return CreateClient(configuration.ProviderType, configuration.Endpoint, configuration.ApiKey, configuration.InferenceModel);
        }

        private static CompletionClientBase CreateClient(string providerType, string endpoint, string? apiKey, string? inferenceModel)
        {
            CompletionClientBase client = providerType switch
            {
                "ollama" => new OllamaClient(endpoint, apiKey) { TimeoutMs = 120000 },
                "openai" => new OpenAiClient(endpoint, apiKey) { TimeoutMs = 60000 },
                "gemini" => new GeminiClient(endpoint, apiKey) { TimeoutMs = 60000 },
                _ => throw new ArgumentException("Unknown provider: " + providerType, nameof(providerType)),
            };

            client.MaxTokens = ResolveLiveMaxTokens(providerType, 128);

            if (!string.IsNullOrEmpty(inferenceModel))
                client.Model = inferenceModel;

            return client;
        }

        private static async Task<List<ModelInformation>> GetModelsAsync(CompletionClientBase client, CancellationToken token)
        {
            List<ModelInformation> models = new List<ModelInformation>();
            await foreach (ModelInformation model in client.ListModelsAsync(token).ConfigureAwait(false))
            {
                if (!string.IsNullOrEmpty(model.Name))
                    models.Add(model);
            }
            return models;
        }

        private static async Task<List<string>> GetModelNamesAsync(CompletionClientBase client, CancellationToken token)
        {
            List<ModelInformation> models = await GetModelsAsync(client, token).ConfigureAwait(false);
            return models.Select(model => model.Name).ToList();
        }

        private static ChatCompletionOptions CreateChatOptions(string providerType)
        {
            switch (providerType)
            {
                case "ollama":
                    return new OllamaChatCompletionOptions
                    {
                        Temperature = 0.5,
                        TopP = 0.9,
                        MaxTokens = ResolveLiveMaxTokens(providerType, 64),
                        TopK = 40,
                        RepeatPenalty = 1.1,
                        Seed = 42,
                    };

                case "openai":
                    return new OpenAiChatCompletionOptions
                    {
                        Temperature = 0.5,
                        TopP = 0.9,
                        MaxTokens = ResolveLiveMaxTokens(providerType, 64),
                        FrequencyPenalty = 0.0,
                        PresencePenalty = 0.0,
                        Seed = 42,
                    };

                case "gemini":
                    return new GeminiChatCompletionOptions
                    {
                        Temperature = 0.5,
                        TopP = 0.9,
                        MaxTokens = ResolveLiveMaxTokens(providerType, 64),
                        TopK = 40,
                    };

                default:
                    return new ChatCompletionOptions();
            }
        }

        private static EmbeddingOptions CreateEmbeddingModelOptions(ProviderTestConfiguration configuration)
        {
            return new EmbeddingOptions { Model = configuration.EmbeddingModel };
        }

        private static EmbeddingOptions CreateEmbeddingOptions(ProviderTestConfiguration configuration)
        {
            switch (configuration.ProviderType)
            {
                case "ollama":
                    return new OllamaEmbeddingOptions
                    {
                        Model = configuration.EmbeddingModel,
                        ContextLength = 2048,
                    };

                case "openai":
                    return new OpenAiEmbeddingOptions
                    {
                        Model = configuration.EmbeddingModel,
                        Dimensions = 256,
                    };

                case "gemini":
                    return new GeminiEmbeddingOptions
                    {
                        Model = configuration.EmbeddingModel,
                        TaskType = "RETRIEVAL_DOCUMENT",
                    };

                default:
                    return new EmbeddingOptions { Model = configuration.EmbeddingModel };
            }
        }

        private static GenerationOptions CreateGenerationOptions(string providerType)
        {
            switch (providerType)
            {
                case "ollama":
                    return new OllamaGenerationOptions
                    {
                        Temperature = 0.5,
                        TopP = 0.9,
                        MaxTokens = ResolveLiveMaxTokens(providerType, 64),
                        TopK = 40,
                        RepeatPenalty = 1.1,
                        Seed = 42,
                    };

                case "openai":
                    return new OpenAiGenerationOptions
                    {
                        Temperature = 0.5,
                        TopP = 0.9,
                        MaxTokens = ResolveLiveMaxTokens(providerType, 64),
                        FrequencyPenalty = 0.0,
                        PresencePenalty = 0.0,
                    };

                case "gemini":
                    return new GeminiGenerationOptions
                    {
                        Temperature = 0.5,
                        TopP = 0.9,
                        MaxTokens = ResolveLiveMaxTokens(providerType, 64),
                        TopK = 40,
                    };

                default:
                    return new GenerationOptions();
            }
        }

        private static int ResolveLiveMaxTokens(string providerType, int defaultMaxTokens)
        {
            if (string.Equals(providerType, "ollama", StringComparison.OrdinalIgnoreCase))
                return 1024;

            return defaultMaxTokens;
        }

        private static bool ModelNameMatches(string available, string requested)
        {
            if (string.Equals(available, requested, StringComparison.OrdinalIgnoreCase))
                return true;

            int colonIndex = available.IndexOf(':');
            if (colonIndex > 0)
            {
                string baseName = available.Substring(0, colonIndex);
                if (string.Equals(baseName, requested, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            colonIndex = requested.IndexOf(':');
            if (colonIndex > 0)
            {
                string baseName = requested.Substring(0, colonIndex);
                if (string.Equals(available, baseName, StringComparison.OrdinalIgnoreCase))
                    return true;

                int availableColonIndex = available.IndexOf(':');
                if (availableColonIndex > 0)
                {
                    string availableBaseName = available.Substring(0, availableColonIndex);
                    if (string.Equals(availableBaseName, baseName, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
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

        private static bool IsOpenAi(ProviderTestConfiguration configuration)
        {
            return string.Equals(configuration.ProviderType, "openai", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsOllama(ProviderTestConfiguration configuration)
        {
            return string.Equals(configuration.ProviderType, "ollama", StringComparison.OrdinalIgnoreCase);
        }
    }
}
