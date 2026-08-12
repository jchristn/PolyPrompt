namespace Test.Shared
{
    using System.Diagnostics;
    using System.Text;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using Touchstone.Core;

    /// <summary>
    /// Builds Touchstone test suites that exercise deterministic local provider behavior.
    /// </summary>
    public static class LocalBehaviorSuite
    {
        private const string SuiteId = "local_behavior";
        private static readonly string[] _ProviderEnvironmentVariables = new string[]
        {
            "POLYPROMPT_TEST_PROVIDER",
            "POLYPROMPT_TEST_ENDPOINT",
            "POLYPROMPT_TEST_API_KEY",
            "POLYPROMPT_TEST_MODEL",
            "POLYPROMPT_TEST_EMBEDDING_MODEL",
            "POLYPROMPT_TEST_OPENAI_API_KEY",
            "POLYPROMPT_TEST_OPENAI_ENDPOINT",
            "POLYPROMPT_TEST_OPENAI_MODEL",
            "POLYPROMPT_TEST_OPENAI_EMBEDDING_MODEL",
            "POLYPROMPT_TEST_OLLAMA_API_KEY",
            "POLYPROMPT_TEST_OLLAMA_ENDPOINT",
            "POLYPROMPT_TEST_OLLAMA_MODEL",
            "POLYPROMPT_TEST_OLLAMA_EMBEDDING_MODEL",
            "POLYPROMPT_TEST_GEMINI_API_KEY",
            "POLYPROMPT_TEST_GEMINI_ENDPOINT",
            "POLYPROMPT_TEST_GEMINI_MODEL",
            "POLYPROMPT_TEST_GEMINI_EMBEDDING_MODEL",
        };

        /// <summary>
        /// Creates the deterministic local behavior suite.
        /// </summary>
        /// <returns>A Touchstone suite descriptor containing local provider protocol and behavior tests.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Local behavior",
                new List<TestCaseDescriptor>
                {
                    Case("chat_and_call_details", "Chat and CallDetails behavior", RunChatAndCallDetailsAsync),
                    Case("client_options_and_guards", "Client options and guard clauses", RunClientOptionsAndGuardsAsync),
                    Case("provider_test_configuration", "Provider test configuration defaults and environment groups", RunProviderTestConfigurationAsync),
                    Case("provider_specific_options_clamping", "Provider-specific options clamp values", RunProviderSpecificOptionsClampingAsync),
                    Case("provider_chat_request_translation", "Provider chat request translation", RunProviderChatRequestTranslationAsync),
                    Case("openai_versioned_endpoint_base", "OpenAI-compatible client accepts versioned endpoint base", RunOpenAiVersionedEndpointBaseAsync),
                    Case("tool_chat_models_and_validation", "Tool chat models and validation", RunToolChatModelsAndValidationAsync),
                    Case("tool_chat_streaming_models_and_validation", "Tool chat streaming models and validation", RunToolChatStreamingModelsAndValidationAsync),
                    Case("openai_chat_streaming", "OpenAI-compatible streaming chat flow", RunOpenAiChatStreamingAsync),
                    Case("ollama_chat_streaming", "Ollama streaming chat flow", RunOllamaChatStreamingAsync),
                    Case("gemini_chat_streaming", "Gemini streaming chat flow", RunGeminiChatStreamingAsync),
                    Case("openai_generation_streaming", "OpenAI-compatible streaming generation flow", RunOpenAiGenerationStreamingAsync),
                    Case("ollama_generation_streaming", "Ollama streaming generation flow", RunOllamaGenerationStreamingAsync),
                    Case("gemini_generation_streaming", "Gemini streaming generation flow", RunGeminiGenerationStreamingAsync),
                    Case("tool_choice_translation", "Tool choice directives are translated consistently", RunToolChoiceTranslationAsync),
                    Case("request_model_overrides", "Per-request model overrides are sent to providers", RunRequestModelOverridesAsync),
                    Case("streaming_http_error_handling", "Streaming HTTP startup errors are surfaced", RunStreamingHttpErrorHandlingAsync),
                    Case("openai_tool_chat", "OpenAI-compatible tool chat flow", RunOpenAiToolChatAsync),
                    Case("openai_tool_chat_streaming", "OpenAI-compatible streaming tool chat flow", RunOpenAiToolChatStreamingAsync),
                    Case("ollama_tool_chat", "Ollama tool chat flow", RunOllamaToolChatAsync),
                    Case("ollama_tool_chat_streaming", "Ollama streaming tool chat flow", RunOllamaToolChatStreamingAsync),
                    Case("gemini_tool_chat", "Gemini tool chat flow", RunGeminiToolChatAsync),
                    Case("gemini_tool_chat_streaming", "Gemini streaming tool chat flow", RunGeminiToolChatStreamingAsync),
                    Case("reasoning_effort_openai_toolchat", "Reasoning effort maps to OpenAI reasoning_effort", RunReasoningEffortOpenAiToolChatAsync),
                    Case("reasoning_effort_openai_toolchat_streaming", "Reasoning effort maps to OpenAI reasoning_effort (streaming)", RunReasoningEffortOpenAiToolChatStreamingAsync),
                    Case("reasoning_effort_instance_default", "Reasoning effort instance default is applied", RunReasoningEffortInstanceDefaultAsync),
                    Case("reasoning_effort_request_overrides_instance", "Reasoning effort request overrides instance default", RunReasoningEffortRequestOverridesInstanceAsync),
                    Case("reasoning_effort_gemini_toolchat", "Reasoning effort maps to Gemini thinkingConfig", RunReasoningEffortGeminiToolChatAsync),
                    Case("reasoning_effort_ollama_toolchat", "Reasoning effort maps to Ollama think", RunReasoningEffortOllamaToolChatAsync),
                    Case("reasoning_effort_projection_defaults", "Reasoning effort level projections return defaults", RunReasoningEffortProjectionDefaultsAsync),
                    Case("reasoning_effort_presets_and_implicit", "Reasoning effort presets and implicit conversion", RunReasoningEffortPresetsAndImplicitAsync),
                    Case("reasoning_effort_overrides_win", "Reasoning effort per-provider overrides win", RunReasoningEffortOverridesWinAsync),
                    Case("reasoning_effort_override_clamping", "Reasoning effort override setters clamp/validate", RunReasoningEffortOverrideClampingAsync),
                    Case("reasoning_effort_absent_by_default", "Reasoning effort is omitted when unset", RunReasoningEffortAbsentByDefaultAsync),
                    Case("reasoning_effort_undefined_level_throws", "Reasoning effort undefined level throws", RunReasoningEffortUndefinedLevelThrowsAsync),
                    Case("reasoning_openai_chat", "OpenAI non-streaming chat surfaces reasoning", RunReasoningOpenAiChatAsync),
                    Case("reasoning_openai_chat_streaming", "OpenAI streaming chat surfaces reasoning", RunReasoningOpenAiChatStreamingAsync),
                    Case("reasoning_openai_toolchat_streaming", "OpenAI streaming tool chat surfaces reasoning without leaking into text", RunReasoningOpenAiToolChatStreamingAsync),
                    Case("reasoning_ollama_toolchat", "Ollama non-streaming tool chat surfaces reasoning", RunReasoningOllamaToolChatAsync),
                    Case("reasoning_ollama_chat_streaming", "Ollama streaming chat surfaces reasoning", RunReasoningOllamaChatStreamingAsync),
                    Case("reasoning_gemini_chat", "Gemini non-streaming chat splits thought and text", RunReasoningGeminiChatAsync),
                    Case("reasoning_gemini_chat_streaming", "Gemini streaming chat splits thought and text", RunReasoningGeminiChatStreamingAsync),
                    Case("reasoning_absent_by_default", "No reasoning leaves the fields null", RunReasoningAbsentByDefaultAsync),
                    Case("reasoning_empty_is_null", "Empty reasoning normalizes to null", RunReasoningEmptyIsNullAsync),
                    Case("reasoning_not_resent", "Reasoning is not carried into a follow-up message", RunReasoningNotResentAsync),
                    Case("reasoning_openai_toolchat", "OpenAI non-streaming tool chat surfaces reasoning", RunReasoningOpenAiToolChatAsync),
                    Case("reasoning_openai_chat_fallback", "OpenAI reasoning fallback field is captured", RunReasoningOpenAiChatFallbackAsync),
                    Case("reasoning_openai_malformed_tolerated", "A malformed reasoning field does not break the response", RunReasoningOpenAiMalformedAsync),
                    Case("reasoning_ollama_chat", "Ollama non-streaming chat surfaces reasoning", RunReasoningOllamaChatAsync),
                    Case("reasoning_ollama_toolchat_streaming", "Ollama streaming tool chat surfaces reasoning", RunReasoningOllamaToolChatStreamingAsync),
                    Case("reasoning_gemini_toolchat", "Gemini non-streaming tool chat splits thought and function call", RunReasoningGeminiToolChatAsync),
                    Case("reasoning_gemini_toolchat_streaming", "Gemini streaming tool chat splits thought and function call", RunReasoningGeminiToolChatStreamingAsync),
                    Case("reasoning_empty_ollama_gemini", "Empty reasoning normalizes to null for Ollama and Gemini", RunReasoningEmptyOllamaGeminiAsync),
                    Case("reasoning_only_chunks_count_metrics", "Reasoning-only chunks count toward streaming metrics", RunReasoningOnlyChunksCountMetricsAsync),
                    Case("gemini_multipart_text_concatenates", "Gemini concatenates multiple non-thought text parts", RunGeminiMultipartTextAsync),
                    Case("reasoning_effort_and_capture_together", "Effort is sent and reasoning is returned in one turn", RunReasoningEffortAndCaptureTogetherAsync),
                    Case("openai_embedding_generation_models", "OpenAI-compatible embeddings, generation, and models", RunOpenAiEmbeddingGenerationModelsAsync),
                    Case("ollama_embedding_generation_models", "Ollama embeddings, generation, and models", RunOllamaEmbeddingGenerationModelsAsync),
                    Case("gemini_embedding_generation_models", "Gemini embeddings, generation, and models", RunGeminiEmbeddingGenerationModelsAsync),
                    Case("unsupported_provider_model_management", "Unsupported provider model management throws", RunUnsupportedProviderModelManagementAsync),
                    Case("http_error_handling", "HTTP error responses are surfaced", RunHttpErrorHandlingAsync),
                    Case("timeout_validation", "TimeoutMs validation preserves positive values", RunTimeoutValidationAsync),
                    Case("validate_connectivity_cancellation", "ValidateConnectivityAsync propagates cancellation", RunValidateConnectivityCancellationAsync),
                    Case("streaming_body_timeout", "Streaming timeout covers the response body", RunStreamingBodyTimeoutAsync),
                    Case("tool_chat_streaming_body_timeout", "Streaming tool chat timeout covers the response body", RunToolChatStreamingBodyTimeoutAsync),
                    Case("post_and_record_disposes_response", "PostAndRecordAsync disposes non-streaming responses", RunPostAndRecordDisposesResponseAsync),
                    Case("injected_http_client", "Injected HttpClient is used and not disposed by the client", RunInjectedHttpClientAsync),
                });
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<CancellationToken, Task> executeAsync)
        {
            return new TestCaseDescriptor(SuiteId, caseId, displayName, executeAsync, new[] { "local" });
        }

        private static async Task RunChatAndCallDetailsAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiClient client = CreateClient(server);
            client.MaxCallDetails = 2;

            ChatResponse first = await client.ChatAsync("first", token: token).ConfigureAwait(false);
            SharedAssert.True(first.Success && first.Text == "pong", "Local ChatAsync should succeed.");

            List<CompletionCallDetail> snapshot = client.CallDetails;
            SharedAssert.Equal(1, snapshot.Count, "CallDetails snapshot should contain the first call.");

            string? originalUrl = snapshot[0].Url;
            snapshot[0].Url = "mutated";
            SharedAssert.Equal(originalUrl, client.CallDetails[0].Url, "CallDetails snapshot should be detached from retained state.");

            await client.ChatAsync("second", token: token).ConfigureAwait(false);
            await client.ChatAsync("third", token: token).ConfigureAwait(false);
            SharedAssert.Equal(2, client.CallDetails.Count, "CallDetails should honor max retention.");

            client.MaxCallDetails = 0;
            await client.ChatAsync("disabled", token: token).ConfigureAwait(false);
            SharedAssert.Equal(0, client.CallDetails.Count, "CallDetails should be disabled when MaxCallDetails is zero.");

            client.MaxCallDetails = 1000;
            await client.ChatAsync("enabled", token: token).ConfigureAwait(false);
            SharedAssert.Equal(1, client.CallDetails.Count, "CallDetails should be re-enabled after MaxCallDetails is raised.");

            client.ClearCallDetails();
            SharedAssert.Equal(0, client.CallDetails.Count, "ClearCallDetails should clear retained entries.");
        }

        private static async Task RunInjectedHttpClientAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();

            // A counting handler proves the injected transport is the one actually used for requests.
            CountingHandler handler = new CountingHandler(new HttpClientHandler());
            HttpClient shared = new HttpClient(handler);

            using (OpenAiClient client = new OpenAiClient(server.Endpoint, apiKey: null, logging: null, httpClient: shared))
            {
                client.Model = "test-model";
                client.TimeoutMs = 1000;

                ChatResponse response = await client.ChatAsync("hello", token: token).ConfigureAwait(false);
                SharedAssert.True(response.Success, "Chat via an injected HttpClient should succeed.");
                SharedAssert.Equal("pong", response.Text, "The injected HttpClient should carry the request to the local server.");
            }

            SharedAssert.True(handler.Count >= 1, "The injected HttpClient's handler should have processed at least one request.");

            // Disposing the PolyPrompt client must NOT dispose an injected HttpClient: reuse it in a
            // second client and confirm it still works (a disposed HttpClient would throw here).
            int countAfterFirst = handler.Count;
            using (OpenAiClient reuse = new OpenAiClient(server.Endpoint, apiKey: null, logging: null, httpClient: shared))
            {
                reuse.Model = "test-model";
                reuse.TimeoutMs = 1000;

                ChatResponse second = await reuse.ChatAsync("again", token: token).ConfigureAwait(false);
                SharedAssert.True(second.Success, "An injected HttpClient should remain usable after the first client is disposed.");
            }

            SharedAssert.True(handler.Count > countAfterFirst, "Reusing the injected HttpClient should route another request through it.");

            // The caller still owns the injected client and is responsible for disposing it.
            shared.Dispose();
        }

        private sealed class CountingHandler : DelegatingHandler
        {
            private int _Count;

            public CountingHandler(HttpMessageHandler innerHandler)
                : base(innerHandler)
            {
            }

            public int Count => Volatile.Read(ref _Count);

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _Count);
                return base.SendAsync(request, cancellationToken);
            }
        }

        private static async Task RunClientOptionsAndGuardsAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiClient client = CreateClient(server);

            client.MaxTokens = -1;
            SharedAssert.Equal(1, client.MaxTokens, "Client MaxTokens should clamp to minimum.");

            client.Temperature = 99;
            SharedAssert.Equal(2.0, client.Temperature, "Client Temperature should clamp to maximum.");

            client.TopP = -99;
            SharedAssert.Equal(0.0, client.TopP, "Client TopP should clamp to minimum.");

            await SharedAssert.ThrowsAsync<ArgumentNullException>(
                () =>
                {
                    client.Model = "";
                    return Task.CompletedTask;
                },
                "Client Model should reject empty values.").ConfigureAwait(false);

            await SharedAssert.ThrowsAsync<ArgumentOutOfRangeException>(
                () =>
                {
                    client.MaxCallDetails = -1;
                    return Task.CompletedTask;
                },
                "MaxCallDetails should reject negative values.").ConfigureAwait(false);

            ChatCompletionOptions chatOptions = new ChatCompletionOptions();
            chatOptions.MaxTokens = 0;
            chatOptions.Temperature = -1;
            chatOptions.TopP = 2;
            SharedAssert.Equal(1, chatOptions.MaxTokens, "ChatCompletionOptions MaxTokens should clamp.");
            SharedAssert.Equal(0.0, chatOptions.Temperature, "ChatCompletionOptions Temperature should clamp.");
            SharedAssert.Equal(1.0, chatOptions.TopP, "ChatCompletionOptions TopP should clamp.");

            GenerationOptions generationOptions = new GenerationOptions();
            generationOptions.MaxTokens = 20_000_000;
            generationOptions.Temperature = 3;
            generationOptions.TopP = -3;
            SharedAssert.Equal(10_000_000, generationOptions.MaxTokens, "GenerationOptions MaxTokens should clamp.");
            SharedAssert.Equal(2.0, generationOptions.Temperature, "GenerationOptions Temperature should clamp.");
            SharedAssert.Equal(0.0, generationOptions.TopP, "GenerationOptions TopP should clamp.");
        }

        private static async Task RunProviderTestConfigurationAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            ProviderTestConfiguration openAi = ProviderTestConfiguration.CreateWithDefaults("OPENAI", apiKey: "test-key", inferenceModel: "gpt-test");
            SharedAssert.Equal("openai", openAi.ProviderType, "OpenAI provider type should normalize.");
            SharedAssert.Equal(ProviderTestConfiguration.DefaultOpenAiEndpoint, openAi.Endpoint, "OpenAI should use its default endpoint when none is supplied.");
            SharedAssert.Equal("test-key", openAi.ApiKey, "OpenAI API key should be retained.");
            SharedAssert.Equal("gpt-test", openAi.InferenceModel, "OpenAI inference model should be retained.");
            SharedAssert.Equal("text-embedding-3-small", openAi.EmbeddingModel, "OpenAI should use its default embedding model.");

            ProviderTestConfiguration gemini = ProviderTestConfiguration.CreateWithDefaults("gemini", apiKey: "gemini-key", inferenceModel: "gemini-test");
            SharedAssert.Equal(ProviderTestConfiguration.DefaultGeminiEndpoint, gemini.Endpoint, "Gemini should use its default endpoint when none is supplied.");
            SharedAssert.Equal("text-embedding-004", gemini.EmbeddingModel, "Gemini should use its default embedding model.");

            ProviderTestConfiguration ollama = ProviderTestConfiguration.CreateWithDefaults("ollama", endpoint: "http://localhost:11434", inferenceModel: "gemma3:4b", embeddingModel: "all-minilm");
            SharedAssert.Equal("ollama", ollama.ProviderType, "Ollama provider type should be retained.");
            SharedAssert.Equal("http://localhost:11434", ollama.Endpoint, "Ollama endpoint should be retained.");
            SharedAssert.True(ollama.ApiKey == null, "Ollama API key should remain null when omitted.");
            SharedAssert.Equal("gemma3:4b", ollama.InferenceModel, "Ollama inference model should be retained.");
            SharedAssert.Equal("all-minilm", ollama.EmbeddingModel, "Ollama embedding model override should be retained.");

            await SharedAssert.ThrowsAsync<ArgumentException>(
                () =>
                {
                    ProviderTestConfiguration.CreateWithDefaults("unknown");
                    return Task.CompletedTask;
                },
                "Unknown provider configuration should throw.").ConfigureAwait(false);

            Dictionary<string, string?> originalEnvironment = CaptureProviderEnvironment();
            try
            {
                ClearProviderEnvironment();
                Environment.SetEnvironmentVariable("POLYPROMPT_TEST_OPENAI_API_KEY", "openai-key");
                Environment.SetEnvironmentVariable("POLYPROMPT_TEST_OPENAI_MODEL", "gpt-live");
                ProviderTestConfiguration? openAiFromEnvironment = ProviderTestConfiguration.FromEnvironment();
                ProviderTestConfiguration openAiEnvironment = openAiFromEnvironment ?? throw new TestFailureException("OpenAI environment configuration should be created.");
                SharedAssert.Equal("openai", openAiEnvironment.ProviderType, "OpenAI environment provider should be selected.");
                SharedAssert.Equal(ProviderTestConfiguration.DefaultOpenAiEndpoint, openAiEnvironment.Endpoint, "OpenAI environment configuration should default endpoint.");
                SharedAssert.Equal("openai-key", openAiEnvironment.ApiKey, "OpenAI environment API key should be retained.");
                SharedAssert.Equal("gpt-live", openAiEnvironment.InferenceModel, "OpenAI environment model should be retained.");

                ClearProviderEnvironment();
                Environment.SetEnvironmentVariable("POLYPROMPT_TEST_PROVIDER", "gemini");
                Environment.SetEnvironmentVariable("POLYPROMPT_TEST_API_KEY", "gemini-key");
                Environment.SetEnvironmentVariable("POLYPROMPT_TEST_MODEL", "gemini-live");
                ProviderTestConfiguration? genericFromEnvironment = ProviderTestConfiguration.FromEnvironment();
                ProviderTestConfiguration genericEnvironment = genericFromEnvironment ?? throw new TestFailureException("Generic environment configuration should be created.");
                SharedAssert.Equal("gemini", genericEnvironment.ProviderType, "Generic environment provider should be selected.");
                SharedAssert.Equal(ProviderTestConfiguration.DefaultGeminiEndpoint, genericEnvironment.Endpoint, "Generic Gemini environment configuration should default endpoint.");
                SharedAssert.Equal("gemini-key", genericEnvironment.ApiKey, "Generic environment API key should be retained.");
                SharedAssert.Equal("gemini-live", genericEnvironment.InferenceModel, "Generic environment model should be retained.");

                ClearProviderEnvironment();
                Environment.SetEnvironmentVariable("POLYPROMPT_TEST_OPENAI_API_KEY", "openai-key");
                Environment.SetEnvironmentVariable("POLYPROMPT_TEST_GEMINI_API_KEY", "gemini-key");
                await SharedAssert.ThrowsAsync<ArgumentException>(
                    () =>
                    {
                        ProviderTestConfiguration.FromEnvironment();
                        return Task.CompletedTask;
                    },
                    "Conflicting provider-specific environment groups should throw.").ConfigureAwait(false);
            }
            finally
            {
                RestoreProviderEnvironment(originalEnvironment);
            }
        }

        private static async Task RunProviderSpecificOptionsClampingAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            OpenAiChatCompletionOptions openAiChat = new OpenAiChatCompletionOptions();
            openAiChat.FrequencyPenalty = -99;
            openAiChat.PresencePenalty = 99;
            SharedAssert.Equal(-2.0, openAiChat.FrequencyPenalty, "OpenAI frequency penalty should clamp.");
            SharedAssert.Equal(2.0, openAiChat.PresencePenalty, "OpenAI presence penalty should clamp.");

            OllamaChatCompletionOptions ollamaChat = new OllamaChatCompletionOptions();
            ollamaChat.ContextLength = -1;
            ollamaChat.TopK = 5000;
            ollamaChat.RepeatPenalty = 99;
            ollamaChat.MinP = -1;
            ollamaChat.RepeatLastN = 9000;
            SharedAssert.Equal(1, ollamaChat.ContextLength, "Ollama context length should clamp.");
            SharedAssert.Equal(1000, ollamaChat.TopK, "Ollama top-k should clamp.");
            SharedAssert.Equal(10.0, ollamaChat.RepeatPenalty, "Ollama repeat penalty should clamp.");
            SharedAssert.Equal(0.0, ollamaChat.MinP, "Ollama min-p should clamp.");
            SharedAssert.Equal(4096, ollamaChat.RepeatLastN, "Ollama repeat-last-n should clamp.");

            OllamaEmbeddingOptions ollamaEmbedding = new OllamaEmbeddingOptions();
            ollamaEmbedding.ContextLength = -1;
            SharedAssert.Equal(1, ollamaEmbedding.ContextLength, "Ollama embedding context length should clamp.");

            GeminiChatCompletionOptions geminiChat = new GeminiChatCompletionOptions();
            geminiChat.TopK = -10;
            geminiChat.CandidateCount = 100;
            geminiChat.PresencePenalty = 99;
            geminiChat.FrequencyPenalty = -99;
            SharedAssert.Equal(1, geminiChat.TopK, "Gemini top-k should clamp.");
            SharedAssert.Equal(8, geminiChat.CandidateCount, "Gemini candidate count should clamp.");
            SharedAssert.Equal(2.0, geminiChat.PresencePenalty, "Gemini presence penalty should clamp.");
            SharedAssert.Equal(-2.0, geminiChat.FrequencyPenalty, "Gemini frequency penalty should clamp.");
        }

        private static async Task RunProviderChatRequestTranslationAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();

            using OpenAiClient openAiClient = CreateClient(server);
            OpenAiChatCompletionOptions openAiOptions = new OpenAiChatCompletionOptions();
            openAiOptions.MaxTokens = 123;
            openAiOptions.Temperature = 0.25;
            openAiOptions.TopP = 0.75;
            openAiOptions.SystemPrompt = "system instructions";
            ChatResponse openAiResponse = await openAiClient.ChatAsync("hello openai", openAiOptions, token).ConfigureAwait(false);
            SharedAssert.True(openAiResponse.Success, "OpenAI-compatible chat should succeed.");

            using OllamaClient ollamaClient = new OllamaClient(server.Endpoint, "test-key");
            ollamaClient.Model = "test-model";
            ollamaClient.TimeoutMs = 1000;
            OllamaChatCompletionOptions ollamaOptions = new OllamaChatCompletionOptions();
            ollamaOptions.ContextLength = 2048;
            ollamaOptions.MaxTokens = 321;
            ollamaOptions.SystemPrompt = "system instructions";
            ChatResponse ollamaResponse = await ollamaClient.ChatAsync("hello ollama", ollamaOptions, token).ConfigureAwait(false);
            SharedAssert.True(ollamaResponse.Success, "Ollama chat should succeed.");

            using GeminiClient geminiClient = new GeminiClient(server.Endpoint, "test-key");
            geminiClient.Model = "test-model";
            geminiClient.TimeoutMs = 1000;
            GeminiChatCompletionOptions geminiOptions = new GeminiChatCompletionOptions();
            geminiOptions.MaxTokens = 456;
            geminiOptions.SystemPrompt = "system instructions";
            ChatResponse geminiResponse = await geminiClient.ChatAsync("hello gemini", geminiOptions, token).ConfigureAwait(false);
            SharedAssert.True(geminiResponse.Success, "Gemini chat should succeed.");

            List<string> bodies = server.RequestBodies;
            LocalOpenAiChatRequest openAiRequest = DeserializeRecordedOpenAiRequest(bodies[0]);
            LocalOpenAiChatRequest ollamaRequest = DeserializeRecordedOpenAiRequest(bodies[1]);
            LocalGeminiRequest geminiRequest = DeserializeRecordedGeminiRequest(bodies[2]);

            SharedAssert.True(openAiRequest.Messages != null && openAiRequest.Messages.Count == 2, "OpenAI-compatible request should include system and user messages.");
            SharedAssert.True(ollamaRequest.Messages != null && ollamaRequest.Messages.Count == 2, "Ollama request should include system and user messages.");
            SharedAssert.True(ollamaRequest.Stream == false, "Ollama non-streaming chat should send stream false.");
            SharedAssert.True(geminiRequest.GenerationConfig != null && geminiRequest.GenerationConfig.MaxOutputTokens == 456, "Gemini chat should map max tokens.");
            SharedAssert.True(geminiRequest.Contents != null && geminiRequest.Contents.Count >= 1, "Gemini chat should include content messages.");
        }

        private static async Task RunOpenAiVersionedEndpointBaseAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiClient client = new OpenAiClient(server.Endpoint + "/v1", "test-key");
            client.Model = "test-model";
            client.TimeoutMs = 1000;

            ChatResponse chat = await client.ChatAsync("hello", token: token).ConfigureAwait(false);
            SharedAssert.True(chat.Success, "OpenAI-compatible ChatAsync should accept a /v1 endpoint base.");
            SharedAssert.Equal("pong", chat.Text, "OpenAI-compatible ChatAsync should receive the local response.");

            ToolChatStreamingResponse stream = await client.ToolChatStreamingAsync(CreateWeatherToolRequest(), token).ConfigureAwait(false);
            SharedAssert.True(stream.Success, "OpenAI-compatible ToolChatStreamingAsync should accept a /v1 endpoint base.");

            int toolDeltaCount = 0;
            await foreach (ToolChatStreamingChunk chunk in stream.Chunks.WithCancellation(token).ConfigureAwait(false))
            {
                toolDeltaCount += chunk.ToolCallDeltas.Count;
            }

            SharedAssert.True(toolDeltaCount > 0, "OpenAI-compatible versioned endpoint stream should emit tool-call deltas.");
            SharedAssert.True(stream.ToolCalls.Any(), "OpenAI-compatible versioned endpoint stream should accumulate tool calls.");

            List<string> paths = server.RequestPaths;
            SharedAssert.Equal("/v1/chat/completions", paths[0], "OpenAI-compatible ChatAsync should not double-prefix /v1.");
            SharedAssert.Equal("/v1/chat/completions", paths[1], "OpenAI-compatible ToolChatStreamingAsync should not double-prefix /v1.");
            SharedAssert.False(paths.Any(path => path.Contains("/v1/v1/", StringComparison.OrdinalIgnoreCase)), "OpenAI-compatible versioned endpoint requests should never contain /v1/v1/.");
        }

        private static async Task RunToolChatModelsAndValidationAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            ToolChatRequest request = new ToolChatRequest();
            request.MaxTokens = -10;
            request.Temperature = 9;
            request.TopP = -1;

            SharedAssert.Equal(1, request.MaxTokens, "ToolChatRequest MaxTokens should clamp to minimum.");
            SharedAssert.Equal(2.0, request.Temperature, "ToolChatRequest Temperature should clamp to maximum.");
            SharedAssert.Equal(0.0, request.TopP, "ToolChatRequest TopP should clamp to minimum.");

            await SharedAssert.ThrowsAsync<ArgumentNullException>(
                () =>
                {
                    ChatMessage.User(null!);
                    return Task.CompletedTask;
                },
                "ChatMessage.User should reject null content.").ConfigureAwait(false);

            await SharedAssert.ThrowsAsync<ArgumentNullException>(
                () =>
                {
                    ToolDefinition.Function(null!, "description", WeatherParameters());
                    return Task.CompletedTask;
                },
                "ToolDefinition.Function should reject null names.").ConfigureAwait(false);

            ToolCall toolCall = new ToolCall
            {
                Id = "call-1",
                Name = "get_weather",
                ArgumentsJson = "{\"city\":\"Seattle\",\"unit\":\"fahrenheit\"}"
            };

            Dictionary<string, string>? args = toolCall.DeserializeArguments<Dictionary<string, string>>();
            SharedAssert.NotNull(args, "ToolCall arguments should deserialize.");
            SharedAssert.Equal("Seattle", args!["city"], "ToolCall argument city should deserialize.");

            ToolChatResponse response = new ToolChatResponse();
            response.ToolCalls.Add(toolCall);
            ChatMessage assistant = response.ToAssistantMessage();
            SharedAssert.Equal("assistant", assistant.Role, "Tool response should convert to assistant message.");
            SharedAssert.Equal(1, assistant.ToolCalls.Count, "Assistant message should include tool calls.");

            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiClient client = CreateClient(server);

            await SharedAssert.ThrowsAsync<ArgumentException>(
                () => client.ToolChatAsync(new ToolChatRequest(), token),
                "ToolChatAsync should reject empty message lists.").ConfigureAwait(false);
        }

        private static async Task RunToolChatStreamingModelsAndValidationAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            ToolCall toolCall = new ToolCall
            {
                Id = "call-1",
                Name = "get_weather",
                ArgumentsJson = "{\"city\":\"Seattle\",\"unit\":\"fahrenheit\"}"
            };

            ToolChatStreamingResponse response = new ToolChatStreamingResponse();
            response.ToolCalls.Add(toolCall);
            ChatMessage assistant = response.ToAssistantMessage();
            SharedAssert.Equal("assistant", assistant.Role, "Streaming tool response should convert to assistant message.");
            SharedAssert.Equal(1, assistant.ToolCalls.Count, "Streaming assistant message should include tool calls.");

            ToolChatStreamingResponse textResponse = new ToolChatStreamingResponse();
            textResponse.Text = "hello";
            ChatMessage textAssistant = textResponse.ToAssistantMessage();
            SharedAssert.Equal("hello", textAssistant.Content, "Streaming text response should convert to assistant text message.");

            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiClient client = CreateClient(server);

            await SharedAssert.ThrowsAsync<ArgumentException>(
                () => client.ToolChatStreamingAsync(new ToolChatRequest(), token),
                "ToolChatStreamingAsync should reject empty message lists.").ConfigureAwait(false);

            using OpenAiClient missingClient = new OpenAiClient(server.Endpoint + "/missing", "test-key");
            missingClient.Model = "test-model";
            missingClient.TimeoutMs = 500;

            ToolChatStreamingResponse failed = await missingClient.ToolChatStreamingAsync(CreateWeatherToolRequest(), token).ConfigureAwait(false);
            SharedAssert.False(failed.Success, "Streaming tool chat HTTP errors should produce unsuccessful responses.");
            SharedAssert.True(failed.StatusCode == 404, "Streaming tool chat HTTP errors should preserve status code.");
            SharedAssert.NotEmpty(failed.Error, "Streaming tool chat HTTP errors should surface an error message.");
        }

        private static async Task RunOpenAiChatStreamingAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiClient client = CreateClient(server);

            ChatStreamingResponse stream = await client.ChatStreamingAsync("normal stream", token: token).ConfigureAwait(false);
            List<ChatStreamingChunk> chunks = await ConsumeChatStreamAsync(stream, token).ConfigureAwait(false);
            string text = CombineChatText(chunks);

            SharedAssert.True(stream.Success, "OpenAI-compatible streaming chat should start.");
            SharedAssert.Equal(200, stream.StatusCode, "OpenAI-compatible streaming chat should return HTTP 200.");
            SharedAssert.Equal("hello world", text, "OpenAI-compatible streaming chat should assemble text.");
            SharedAssert.Equal("stop", stream.FinishReason, "OpenAI-compatible streaming chat should expose finish reason.");
            SharedAssert.Equal("chatcmpl-stream-local", stream.ResponseId, "OpenAI-compatible streaming chat should expose response id.");
            SharedAssert.NotNull(stream.Usage, "OpenAI-compatible streaming chat should expose usage.");
            SharedAssert.Equal(2, stream.Usage!.CompletionTokens, "OpenAI-compatible streaming chat should parse completion tokens.");
            SharedAssert.True(stream.ChunkCount >= 2, "OpenAI-compatible streaming chat should count text chunks.");

            LocalOpenAiChatRequest recordedRequest = DeserializeRecordedOpenAiRequest(server.RequestBodies[0]);
            SharedAssert.True(recordedRequest.Stream == true, "OpenAI-compatible streaming chat request should set stream true.");
        }

        private static async Task RunOllamaChatStreamingAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OllamaClient client = new OllamaClient(server.Endpoint, "test-key");
            client.Model = "test-model";
            client.TimeoutMs = 1000;

            ChatStreamingResponse stream = await client.ChatStreamingAsync("normal stream", token: token).ConfigureAwait(false);
            List<ChatStreamingChunk> chunks = await ConsumeChatStreamAsync(stream, token).ConfigureAwait(false);
            string text = CombineChatText(chunks);

            SharedAssert.True(stream.Success, "Ollama streaming chat should start.");
            SharedAssert.Equal(200, stream.StatusCode, "Ollama streaming chat should return HTTP 200.");
            SharedAssert.Equal("hello world", text, "Ollama streaming chat should assemble text.");
            SharedAssert.Equal("stop", stream.FinishReason, "Ollama streaming chat should expose finish reason.");
            SharedAssert.NotNull(stream.Usage, "Ollama streaming chat should expose usage.");
            SharedAssert.Equal(2, stream.Usage!.CompletionTokens, "Ollama streaming chat should parse eval count.");
            SharedAssert.True(stream.ChunkCount >= 2, "Ollama streaming chat should count text chunks.");

            LocalOpenAiChatRequest recordedRequest = DeserializeRecordedOpenAiRequest(server.RequestBodies[0]);
            SharedAssert.True(recordedRequest.Stream == true, "Ollama streaming chat request should set stream true.");
        }

        private static async Task RunGeminiChatStreamingAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using GeminiClient client = new GeminiClient(server.Endpoint, "test-key");
            client.Model = "test-model";
            client.TimeoutMs = 1000;

            ChatStreamingResponse stream = await client.ChatStreamingAsync("normal stream", token: token).ConfigureAwait(false);
            List<ChatStreamingChunk> chunks = await ConsumeChatStreamAsync(stream, token).ConfigureAwait(false);
            string text = CombineChatText(chunks);

            SharedAssert.True(stream.Success, "Gemini streaming chat should start.");
            SharedAssert.Equal(200, stream.StatusCode, "Gemini streaming chat should return HTTP 200.");
            SharedAssert.Equal("pong", text, "Gemini streaming chat should assemble text.");
            SharedAssert.Equal("STOP", stream.FinishReason, "Gemini streaming chat should expose finish reason.");
            SharedAssert.Equal("gemini-chat-stream-local", stream.ResponseId, "Gemini streaming chat should expose response id.");
            SharedAssert.True(stream.ChunkCount >= 1, "Gemini streaming chat should count text chunks.");

            LocalGeminiRequest recordedRequest = DeserializeRecordedGeminiRequest(server.RequestBodies[0]);
            SharedAssert.True(recordedRequest.Contents != null && recordedRequest.Contents.Any(), "Gemini streaming chat request should include contents.");
        }

        private static async Task RunOpenAiGenerationStreamingAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiClient client = CreateClient(server);

            GenerationStreamingResponse stream = await client.GenerateStreamingAsync("generate stream", token: token).ConfigureAwait(false);
            List<GenerationStreamingChunk> chunks = await ConsumeGenerationStreamAsync(stream, token).ConfigureAwait(false);
            string text = CombineGenerationText(chunks);

            SharedAssert.True(stream.Success, "OpenAI-compatible streaming generation should start.");
            SharedAssert.Equal(200, stream.StatusCode, "OpenAI-compatible streaming generation should return HTTP 200.");
            SharedAssert.Equal("generated text", text, "OpenAI-compatible streaming generation should assemble text.");
            SharedAssert.True(stream.ChunkCount >= 2, "OpenAI-compatible streaming generation should count text chunks.");

            LocalGenerateRequest recordedRequest = DeserializeRecordedGenerateRequest(server.RequestBodies[0]);
            SharedAssert.True(recordedRequest.Stream == true, "OpenAI-compatible streaming generation request should set stream true.");
        }

        private static async Task RunOllamaGenerationStreamingAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OllamaClient client = new OllamaClient(server.Endpoint, "test-key");
            client.Model = "test-model";
            client.TimeoutMs = 1000;

            GenerationStreamingResponse stream = await client.GenerateStreamingAsync("generate stream", token: token).ConfigureAwait(false);
            List<GenerationStreamingChunk> chunks = await ConsumeGenerationStreamAsync(stream, token).ConfigureAwait(false);
            string text = CombineGenerationText(chunks);

            SharedAssert.True(stream.Success, "Ollama streaming generation should start.");
            SharedAssert.Equal(200, stream.StatusCode, "Ollama streaming generation should return HTTP 200.");
            SharedAssert.Equal("generated text", text, "Ollama streaming generation should assemble text.");
            SharedAssert.True(stream.ChunkCount >= 2, "Ollama streaming generation should count text chunks.");

            LocalGenerateRequest recordedRequest = DeserializeRecordedGenerateRequest(server.RequestBodies[0]);
            SharedAssert.True(recordedRequest.Stream == true, "Ollama streaming generation request should set stream true.");
        }

        private static async Task RunGeminiGenerationStreamingAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using GeminiClient client = new GeminiClient(server.Endpoint, "test-key");
            client.Model = "test-model";
            client.TimeoutMs = 1000;

            GenerationStreamingResponse stream = await client.GenerateStreamingAsync("generate stream", token: token).ConfigureAwait(false);
            List<GenerationStreamingChunk> chunks = await ConsumeGenerationStreamAsync(stream, token).ConfigureAwait(false);
            string text = CombineGenerationText(chunks);

            SharedAssert.True(stream.Success, "Gemini streaming generation should start.");
            SharedAssert.Equal(200, stream.StatusCode, "Gemini streaming generation should return HTTP 200.");
            SharedAssert.Equal("pong", text, "Gemini streaming generation should assemble text from streamGenerateContent.");
            SharedAssert.True(stream.ChunkCount >= 1, "Gemini streaming generation should count text chunks.");

            LocalGeminiRequest recordedRequest = DeserializeRecordedGeminiRequest(server.RequestBodies[0]);
            SharedAssert.True(recordedRequest.GenerationConfig != null, "Gemini streaming generation request should include generationConfig.");
        }

        private static async Task RunToolChoiceTranslationAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();

            using OpenAiClient openAiClient = CreateClient(server);
            ToolChatRequest openAiRequired = CreateWeatherToolRequest();
            openAiRequired.ToolChoice = "required";
            ToolChatResponse openAiRequiredResponse = await openAiClient.ToolChatAsync(openAiRequired, token).ConfigureAwait(false);
            SharedAssert.True(openAiRequiredResponse.Success, "OpenAI-compatible required tool-choice request should succeed.");

            ToolChatRequest openAiNone = CreateWeatherToolRequest();
            openAiNone.ToolChoice = "none";
            ToolChatResponse openAiNoneResponse = await openAiClient.ToolChatAsync(openAiNone, token).ConfigureAwait(false);
            SharedAssert.True(openAiNoneResponse.Success, "OpenAI-compatible none tool-choice request should succeed.");

            using OllamaClient ollamaClient = new OllamaClient(server.Endpoint, "test-key");
            ollamaClient.Model = "test-model";
            ollamaClient.TimeoutMs = 1000;
            ToolChatRequest ollamaNone = CreateWeatherToolRequest();
            ollamaNone.ToolChoice = "none";
            ToolChatResponse ollamaNoneResponse = await ollamaClient.ToolChatAsync(ollamaNone, token).ConfigureAwait(false);
            SharedAssert.True(ollamaNoneResponse.Success, "Ollama none tool-choice request should succeed.");

            using GeminiClient geminiClient = new GeminiClient(server.Endpoint, "test-key");
            geminiClient.Model = "test-model";
            geminiClient.TimeoutMs = 1000;
            ToolChatRequest geminiRequired = CreateWeatherToolRequest();
            geminiRequired.ToolChoice = "required";
            ToolChatResponse geminiRequiredResponse = await geminiClient.ToolChatAsync(geminiRequired, token).ConfigureAwait(false);
            SharedAssert.True(geminiRequiredResponse.Success, "Gemini required tool-choice request should succeed.");

            List<string> bodies = server.RequestBodies;
            LocalOpenAiChatRequest openAiRequiredRequest = DeserializeRecordedOpenAiRequest(bodies[0]);
            LocalOpenAiChatRequest openAiNoneRequest = DeserializeRecordedOpenAiRequest(bodies[1]);
            LocalOpenAiChatRequest ollamaNoneRequest = DeserializeRecordedOpenAiRequest(bodies[2]);
            LocalGeminiRequest geminiRequiredRequest = DeserializeRecordedGeminiRequest(bodies[3]);

            SharedAssert.Equal("required", openAiRequiredRequest.ToolChoice, "OpenAI-compatible required tool-choice should be sent.");
            SharedAssert.True(openAiRequiredRequest.Tools != null && openAiRequiredRequest.Tools.Any(), "OpenAI-compatible required tool-choice should include tools.");
            SharedAssert.Equal("none", openAiNoneRequest.ToolChoice, "OpenAI-compatible none tool-choice should be sent.");
            SharedAssert.True(openAiNoneRequest.Tools == null || !openAiNoneRequest.Tools.Any(), "OpenAI-compatible none tool-choice should omit tools.");
            SharedAssert.True(ollamaNoneRequest.Tools == null || !ollamaNoneRequest.Tools.Any(), "Ollama none tool-choice should omit tools.");
            SharedAssert.True(geminiRequiredRequest.Tools != null && geminiRequiredRequest.Tools.Any(), "Gemini required tool-choice should include tools.");
        }

        private static async Task RunRequestModelOverridesAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();

            using OpenAiClient openAiClient = CreateClient(server);
            ToolChatRequest openAiRequest = CreateWeatherToolRequest();
            openAiRequest.Model = "override-model";
            ToolChatResponse openAiResponse = await openAiClient.ToolChatAsync(openAiRequest, token).ConfigureAwait(false);
            SharedAssert.True(openAiResponse.Success, "OpenAI-compatible tool chat model override should succeed.");

            using OllamaClient ollamaClient = new OllamaClient(server.Endpoint, "test-key");
            ollamaClient.Model = "test-model";
            ollamaClient.TimeoutMs = 1000;
            ToolChatRequest ollamaRequest = CreateWeatherToolRequest();
            ollamaRequest.Model = "override-model";
            ToolChatResponse ollamaResponse = await ollamaClient.ToolChatAsync(ollamaRequest, token).ConfigureAwait(false);
            SharedAssert.True(ollamaResponse.Success, "Ollama tool chat model override should succeed.");

            using GeminiClient geminiClient = new GeminiClient(server.Endpoint, "test-key");
            geminiClient.Model = "test-model";
            geminiClient.TimeoutMs = 1000;
            ToolChatRequest geminiRequest = CreateWeatherToolRequest();
            geminiRequest.Model = "override-model";
            ToolChatResponse geminiResponse = await geminiClient.ToolChatAsync(geminiRequest, token).ConfigureAwait(false);
            SharedAssert.True(geminiResponse.Success, "Gemini tool chat model override should succeed.");

            LocalOpenAiChatRequest openAiRecorded = DeserializeRecordedOpenAiRequest(server.RequestBodies[0]);
            LocalOpenAiChatRequest ollamaRecorded = DeserializeRecordedOpenAiRequest(server.RequestBodies[1]);
            SharedAssert.Equal("override-model", openAiRecorded.Model, "OpenAI-compatible tool chat should send request model override.");
            SharedAssert.Equal("override-model", ollamaRecorded.Model, "Ollama tool chat should send request model override.");
            SharedAssert.True(server.RequestPaths[2].Contains("/v1beta/models/override-model:generateContent", StringComparison.Ordinal), "Gemini tool chat should route request model override in the URL.");
        }

        private static async Task RunStreamingHttpErrorHandlingAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();

            using OpenAiClient openAiClient = new OpenAiClient(server.Endpoint + "/missing", "test-key");
            openAiClient.Model = "test-model";
            openAiClient.TimeoutMs = 1000;
            ChatStreamingResponse openAiChat = await openAiClient.ChatStreamingAsync("fail", token: token).ConfigureAwait(false);
            GenerationStreamingResponse openAiGeneration = await openAiClient.GenerateStreamingAsync("fail", token: token).ConfigureAwait(false);
            ToolChatStreamingResponse openAiTool = await openAiClient.ToolChatStreamingAsync(CreateWeatherToolRequest(), token).ConfigureAwait(false);

            SharedAssert.False(openAiChat.Success, "OpenAI-compatible streaming chat should surface HTTP startup errors.");
            SharedAssert.False(openAiGeneration.Success, "OpenAI-compatible streaming generation should surface HTTP startup errors.");
            SharedAssert.False(openAiTool.Success, "OpenAI-compatible streaming tool chat should surface HTTP startup errors.");
            SharedAssert.Equal(404, openAiChat.StatusCode, "OpenAI-compatible streaming chat should preserve HTTP status.");
            SharedAssert.Equal(404, openAiGeneration.StatusCode, "OpenAI-compatible streaming generation should preserve HTTP status.");
            SharedAssert.Equal(404, openAiTool.StatusCode, "OpenAI-compatible streaming tool chat should preserve HTTP status.");

            using OllamaClient ollamaClient = new OllamaClient(server.Endpoint + "/missing", "test-key");
            ollamaClient.Model = "test-model";
            ollamaClient.TimeoutMs = 1000;
            ChatStreamingResponse ollamaChat = await ollamaClient.ChatStreamingAsync("fail", token: token).ConfigureAwait(false);
            GenerationStreamingResponse ollamaGeneration = await ollamaClient.GenerateStreamingAsync("fail", token: token).ConfigureAwait(false);
            ToolChatStreamingResponse ollamaTool = await ollamaClient.ToolChatStreamingAsync(CreateWeatherToolRequest(), token).ConfigureAwait(false);

            SharedAssert.False(ollamaChat.Success, "Ollama streaming chat should surface HTTP startup errors.");
            SharedAssert.False(ollamaGeneration.Success, "Ollama streaming generation should surface HTTP startup errors.");
            SharedAssert.False(ollamaTool.Success, "Ollama streaming tool chat should surface HTTP startup errors.");
            SharedAssert.Equal(404, ollamaChat.StatusCode, "Ollama streaming chat should preserve HTTP status.");
            SharedAssert.Equal(404, ollamaGeneration.StatusCode, "Ollama streaming generation should preserve HTTP status.");
            SharedAssert.Equal(404, ollamaTool.StatusCode, "Ollama streaming tool chat should preserve HTTP status.");

            using GeminiClient geminiClient = new GeminiClient(server.Endpoint + "/missing", "test-key");
            geminiClient.Model = "test-model";
            geminiClient.TimeoutMs = 1000;
            ChatStreamingResponse geminiChat = await geminiClient.ChatStreamingAsync("fail", token: token).ConfigureAwait(false);
            GenerationStreamingResponse geminiGeneration = await geminiClient.GenerateStreamingAsync("fail", token: token).ConfigureAwait(false);
            ToolChatStreamingResponse geminiTool = await geminiClient.ToolChatStreamingAsync(CreateWeatherToolRequest(), token).ConfigureAwait(false);

            SharedAssert.False(geminiChat.Success, "Gemini streaming chat should surface HTTP startup errors.");
            SharedAssert.False(geminiGeneration.Success, "Gemini streaming generation should surface HTTP startup errors.");
            SharedAssert.False(geminiTool.Success, "Gemini streaming tool chat should surface HTTP startup errors.");
            SharedAssert.Equal(404, geminiChat.StatusCode, "Gemini streaming chat should preserve HTTP status.");
            SharedAssert.Equal(404, geminiGeneration.StatusCode, "Gemini streaming generation should preserve HTTP status.");
            SharedAssert.Equal(404, geminiTool.StatusCode, "Gemini streaming tool chat should preserve HTTP status.");
        }

        private static async Task RunOpenAiToolChatAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiClient client = CreateClient(server);

            ToolChatRequest request = CreateWeatherToolRequest();
            ToolChatResponse first = await client.ToolChatAsync(request, token).ConfigureAwait(false);

            SharedAssert.True(first.Success, "OpenAI-compatible ToolChatAsync should succeed.");
            SharedAssert.Equal("tool_calls", first.FinishReason, "OpenAI-compatible tool response should expose finish reason.");
            SharedAssert.Equal(1, first.ToolCalls.Count, "OpenAI-compatible response should contain one tool call.");
            SharedAssert.Equal("get_weather", first.ToolCalls[0].Name, "OpenAI-compatible response should parse tool call name.");
            Dictionary<string, string> openAiArguments = LocalRequestParser.DeserializeStringDictionary(first.ToolCalls[0].ArgumentsJson);
            SharedAssert.Equal("Seattle", openAiArguments["city"], "OpenAI-compatible response should parse tool call arguments.");

            request.Messages.Add(first.ToAssistantMessage());
            request.Tools.Clear();
            request.ToolChoice = "none";
            request.Messages.Add(ChatMessage.ToolResult(first.ToolCalls[0].Id, first.ToolCalls[0].Name, "{\"temperature\":72,\"conditions\":\"clear\"}"));

            ToolChatResponse final = await client.ToolChatAsync(request, token).ConfigureAwait(false);

            SharedAssert.True(final.Success, "OpenAI-compatible final ToolChatAsync should succeed.");
            SharedAssert.Equal("Seattle is 72 F and clear.", final.Text, "OpenAI-compatible final response should parse text.");

            List<string> bodies = server.RequestBodies;
            LocalOpenAiChatRequest initialRequest = DeserializeRecordedOpenAiRequest(bodies[0]);
            LocalOpenAiChatRequest followupRequest = DeserializeRecordedOpenAiRequest(bodies[1]);

            SharedAssert.True(initialRequest.Tools != null && initialRequest.Tools.Count == 1, "OpenAI-compatible request should include one tool.");
            SharedAssert.Equal("auto", initialRequest.ToolChoice, "OpenAI-compatible request should include tool_choice.");
            SharedAssert.True(
                followupRequest.Messages != null
                    && followupRequest.Messages.Any(message => string.Equals(message.ToolCallId, "call-weather-1", StringComparison.Ordinal)),
                "OpenAI-compatible follow-up should include tool call id.");
        }

        private static async Task RunOpenAiToolChatStreamingAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiClient client = CreateClient(server);

            ToolChatRequest request = CreateWeatherToolRequest();
            ToolChatStreamingResponse first = await client.ToolChatStreamingAsync(request, token).ConfigureAwait(false);

            SharedAssert.True(first.Success, "OpenAI-compatible ToolChatStreamingAsync should start.");
            SharedAssert.Equal(200, first.StatusCode, "OpenAI-compatible streaming tool chat should return HTTP 200.");

            List<ToolChatStreamingChunk> firstChunks = await ConsumeToolChatStreamAsync(first, token).ConfigureAwait(false);

            SharedAssert.True(firstChunks.Count > 0, "OpenAI-compatible streaming tool chat should receive chunks.");
            SharedAssert.True(firstChunks.Any(chunk => !string.IsNullOrEmpty(chunk.Text)), "OpenAI-compatible streaming tool chat should emit text chunks.");
            SharedAssert.True(firstChunks.Any(chunk => chunk.ToolCallDeltas.Count > 0), "OpenAI-compatible streaming tool chat should emit tool-call deltas.");
            SharedAssert.Equal("Checking weather. ", first.Text, "OpenAI-compatible streaming tool chat should accumulate text.");
            SharedAssert.Equal("tool_calls", first.FinishReason, "OpenAI-compatible streaming tool chat should expose finish reason.");
            SharedAssert.Equal("chatcmpl-tool-stream-local", first.ResponseId, "OpenAI-compatible streaming tool chat should expose response id.");
            SharedAssert.Equal(2, first.ToolCalls.Count, "OpenAI-compatible streaming tool chat should accumulate multiple tool calls.");
            SharedAssert.True(first.ToolCallDeltaCount >= 4, "OpenAI-compatible streaming tool chat should count tool-call deltas.");
            SharedAssert.True(first.ChunkCount >= 4, "OpenAI-compatible streaming tool chat should count content chunks.");
            SharedAssert.True(first.TimeToFirstTokenMs >= 0, "OpenAI-compatible streaming tool chat should populate first-token timing.");
            SharedAssert.True(first.TimeToLastTokenMs >= first.TimeToFirstTokenMs, "OpenAI-compatible streaming tool chat should order token timings.");
            SharedAssert.True(first.OverallRuntimeMs > 0, "OpenAI-compatible streaming tool chat should populate runtime.");
            SharedAssert.True(first.OverallTokensPerSecond > 0, "OpenAI-compatible streaming tool chat should populate throughput.");
            SharedAssert.NotNull(first.Usage, "OpenAI-compatible streaming tool chat should expose usage.");
            SharedAssert.Equal(7, first.Usage!.CompletionTokens, "OpenAI-compatible streaming tool chat should parse completion tokens.");

            SharedAssert.Equal("call-weather-1", first.ToolCalls[0].Id, "OpenAI-compatible streaming tool chat should preserve first tool call id.");
            SharedAssert.Equal("get_weather", first.ToolCalls[0].Name, "OpenAI-compatible streaming tool chat should parse first tool call name.");
            Dictionary<string, string> firstArguments = LocalRequestParser.DeserializeStringDictionary(first.ToolCalls[0].ArgumentsJson);
            SharedAssert.Equal("Seattle", firstArguments["city"], "OpenAI-compatible streaming tool chat should accumulate split arguments.");

            SharedAssert.Equal("call-weather-2", first.ToolCalls[1].Id, "OpenAI-compatible streaming tool chat should preserve second tool call id.");
            Dictionary<string, string> secondArguments = LocalRequestParser.DeserializeStringDictionary(first.ToolCalls[1].ArgumentsJson);
            SharedAssert.Equal("Portland", secondArguments["city"], "OpenAI-compatible streaming tool chat should parse second tool call arguments.");

            request.Messages.Add(first.ToAssistantMessage());
            request.Tools.Clear();
            request.ToolChoice = "none";
            foreach (ToolCall call in first.ToolCalls)
            {
                request.Messages.Add(ChatMessage.ToolResult(call.Id, call.Name, "{\"temperature\":72,\"conditions\":\"clear\"}"));
            }

            ToolChatStreamingResponse final = await client.ToolChatStreamingAsync(request, token).ConfigureAwait(false);
            List<ToolChatStreamingChunk> finalChunks = await ConsumeToolChatStreamAsync(final, token).ConfigureAwait(false);

            SharedAssert.True(final.Success, "OpenAI-compatible final ToolChatStreamingAsync should start.");
            SharedAssert.True(finalChunks.Count > 0, "OpenAI-compatible final streaming tool chat should receive chunks.");
            SharedAssert.Equal("Seattle is 72 F and clear.", final.Text, "OpenAI-compatible final streaming response should accumulate text.");
            SharedAssert.Equal(0, final.ToolCalls.Count, "OpenAI-compatible final streaming response should not accumulate tool calls.");
            SharedAssert.Equal("stop", final.FinishReason, "OpenAI-compatible final streaming response should expose stop reason.");

            List<string> bodies = server.RequestBodies;
            LocalOpenAiChatRequest initialRequest = DeserializeRecordedOpenAiRequest(bodies[0]);
            LocalOpenAiChatRequest followupRequest = DeserializeRecordedOpenAiRequest(bodies[1]);

            SharedAssert.True(initialRequest.Stream == true, "OpenAI-compatible streaming tool request should send stream true.");
            SharedAssert.True(initialRequest.Tools != null && initialRequest.Tools.Count == 1, "OpenAI-compatible streaming tool request should include one tool.");
            SharedAssert.Equal("auto", initialRequest.ToolChoice, "OpenAI-compatible streaming tool request should include tool_choice.");
            SharedAssert.True(
                followupRequest.Messages != null
                    && followupRequest.Messages.Any(message => message.ToolCalls != null && message.ToolCalls.Count == 2),
                "OpenAI-compatible streaming follow-up should include assistant tool calls.");
            SharedAssert.True(
                followupRequest.Messages != null
                    && followupRequest.Messages.Count(message => string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase)) == 2,
                "OpenAI-compatible streaming follow-up should include both tool results.");
            SharedAssert.True(followupRequest.Stream == true, "OpenAI-compatible streaming follow-up should send stream true.");
            SharedAssert.True(followupRequest.Tools == null || followupRequest.Tools.Count == 0, "OpenAI-compatible streaming follow-up should omit tools when ToolChoice is none.");
        }

        private static async Task RunOllamaToolChatAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OllamaClient client = new OllamaClient(server.Endpoint, "test-key");
            client.Model = "test-model";
            client.TimeoutMs = 1000;

            ToolChatRequest request = CreateWeatherToolRequest();
            ToolChatResponse first = await client.ToolChatAsync(request, token).ConfigureAwait(false);

            SharedAssert.True(first.Success, "Ollama ToolChatAsync should succeed.");
            SharedAssert.Equal("tool_calls", first.FinishReason, "Ollama tool response should expose done reason.");
            SharedAssert.Equal(1, first.ToolCalls.Count, "Ollama response should contain one tool call.");
            SharedAssert.Equal("get_weather", first.ToolCalls[0].Name, "Ollama response should parse tool call name.");
            Dictionary<string, string> ollamaArguments = LocalRequestParser.DeserializeStringDictionary(first.ToolCalls[0].ArgumentsJson);
            SharedAssert.Equal("Seattle", ollamaArguments["city"], "Ollama response should parse object arguments.");

            request.Messages.Add(first.ToAssistantMessage());
            request.Tools.Clear();
            request.ToolChoice = "none";
            request.Messages.Add(ChatMessage.ToolResult(first.ToolCalls[0].Id, first.ToolCalls[0].Name, "{\"temperature\":72,\"conditions\":\"clear\"}"));

            ToolChatResponse final = await client.ToolChatAsync(request, token).ConfigureAwait(false);

            SharedAssert.True(final.Success, "Ollama final ToolChatAsync should succeed.");
            SharedAssert.Equal("Seattle is 72 F and clear.", final.Text, "Ollama final response should parse text.");

            List<string> bodies = server.RequestBodies;
            LocalOpenAiChatRequest initialRequest = DeserializeRecordedOpenAiRequest(bodies[0]);
            LocalOpenAiChatRequest followupRequest = DeserializeRecordedOpenAiRequest(bodies[1]);

            SharedAssert.True(initialRequest.Tools != null && initialRequest.Tools.Count == 1, "Ollama request should include one tool.");
            SharedAssert.False(initialRequest.Stream == true, "Ollama request should be non-streaming.");
            SharedAssert.True(followupRequest.Tools == null || followupRequest.Tools.Count == 0, "Ollama follow-up should omit tools when ToolChoice is none.");
        }

        private static async Task RunOllamaToolChatStreamingAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OllamaClient client = new OllamaClient(server.Endpoint, "test-key");
            client.Model = "test-model";
            client.TimeoutMs = 1000;

            ToolChatRequest request = CreateWeatherToolRequest();
            ToolChatStreamingResponse first = await client.ToolChatStreamingAsync(request, token).ConfigureAwait(false);

            SharedAssert.True(first.Success, "Ollama ToolChatStreamingAsync should start.");
            SharedAssert.Equal(200, first.StatusCode, "Ollama streaming tool chat should return HTTP 200.");

            List<ToolChatStreamingChunk> firstChunks = await ConsumeToolChatStreamAsync(first, token).ConfigureAwait(false);

            SharedAssert.True(firstChunks.Count > 0, "Ollama streaming tool chat should receive chunks.");
            SharedAssert.True(firstChunks.Any(chunk => !string.IsNullOrEmpty(chunk.Text)), "Ollama streaming tool chat should emit text chunks.");
            SharedAssert.True(firstChunks.Any(chunk => chunk.ToolCallDeltas.Count > 0), "Ollama streaming tool chat should emit tool-call deltas.");
            SharedAssert.Equal("Checking weather. ", first.Text, "Ollama streaming tool chat should accumulate text.");
            SharedAssert.Equal("tool_calls", first.FinishReason, "Ollama streaming tool chat should expose done reason.");
            SharedAssert.Equal(2, first.ToolCalls.Count, "Ollama streaming tool chat should accumulate multiple tool calls.");
            SharedAssert.True(first.ToolCallDeltaCount >= 3, "Ollama streaming tool chat should count tool-call deltas.");
            SharedAssert.True(first.ChunkCount >= 4, "Ollama streaming tool chat should count content chunks.");
            SharedAssert.True(first.TimeToFirstTokenMs >= 0, "Ollama streaming tool chat should populate first-token timing.");
            SharedAssert.True(first.TimeToLastTokenMs >= first.TimeToFirstTokenMs, "Ollama streaming tool chat should order token timings.");
            SharedAssert.True(first.OverallRuntimeMs > 0, "Ollama streaming tool chat should populate runtime.");
            SharedAssert.True(first.OverallTokensPerSecond > 0, "Ollama streaming tool chat should populate throughput.");
            SharedAssert.NotNull(first.Usage, "Ollama streaming tool chat should expose usage.");
            SharedAssert.Equal(7, first.Usage!.CompletionTokens, "Ollama streaming tool chat should parse eval count.");

            SharedAssert.Equal("ollama-call-0", first.ToolCalls[0].Id, "Ollama streaming tool chat should synthesize first tool call id.");
            SharedAssert.Equal("get_weather", first.ToolCalls[0].Name, "Ollama streaming tool chat should parse first tool call name.");
            Dictionary<string, string> firstArguments = LocalRequestParser.DeserializeStringDictionary(first.ToolCalls[0].ArgumentsJson);
            SharedAssert.Equal("Seattle", firstArguments["city"], "Ollama streaming tool chat should replace partial object arguments with final arguments.");

            SharedAssert.Equal("ollama-call-1", first.ToolCalls[1].Id, "Ollama streaming tool chat should synthesize second tool call id.");
            Dictionary<string, string> secondArguments = LocalRequestParser.DeserializeStringDictionary(first.ToolCalls[1].ArgumentsJson);
            SharedAssert.Equal("Portland", secondArguments["city"], "Ollama streaming tool chat should parse second tool call arguments.");

            request.Messages.Add(first.ToAssistantMessage());
            request.Tools.Clear();
            request.ToolChoice = "none";
            foreach (ToolCall call in first.ToolCalls)
            {
                request.Messages.Add(ChatMessage.ToolResult(call.Id, call.Name, "{\"temperature\":72,\"conditions\":\"clear\"}"));
            }

            ToolChatStreamingResponse final = await client.ToolChatStreamingAsync(request, token).ConfigureAwait(false);
            List<ToolChatStreamingChunk> finalChunks = await ConsumeToolChatStreamAsync(final, token).ConfigureAwait(false);

            SharedAssert.True(final.Success, "Ollama final ToolChatStreamingAsync should start.");
            SharedAssert.True(finalChunks.Count > 0, "Ollama final streaming tool chat should receive chunks.");
            SharedAssert.Equal("Seattle is 72 F and clear.", final.Text, "Ollama final streaming response should accumulate text.");
            SharedAssert.Equal(0, final.ToolCalls.Count, "Ollama final streaming response should not accumulate tool calls.");
            SharedAssert.Equal("stop", final.FinishReason, "Ollama final streaming response should expose stop reason.");

            List<string> bodies = server.RequestBodies;
            LocalOpenAiChatRequest initialRequest = DeserializeRecordedOpenAiRequest(bodies[0]);
            LocalOpenAiChatRequest followupRequest = DeserializeRecordedOpenAiRequest(bodies[1]);

            SharedAssert.True(initialRequest.Stream == true, "Ollama streaming tool request should send stream true.");
            SharedAssert.True(initialRequest.Tools != null && initialRequest.Tools.Count == 1, "Ollama streaming tool request should include one tool.");
            SharedAssert.True(
                followupRequest.Messages != null
                    && followupRequest.Messages.Any(message => message.ToolCalls != null && message.ToolCalls.Count == 2),
                "Ollama streaming follow-up should include assistant tool calls.");
            SharedAssert.True(
                followupRequest.Messages != null
                    && followupRequest.Messages.Count(message => string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase)) == 2,
                "Ollama streaming follow-up should include both tool results.");
            SharedAssert.True(followupRequest.Stream == true, "Ollama streaming follow-up should send stream true.");
            SharedAssert.True(followupRequest.Tools == null || followupRequest.Tools.Count == 0, "Ollama streaming follow-up should omit tools when ToolChoice is none.");
        }

        private static async Task RunGeminiToolChatAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using GeminiClient client = new GeminiClient(server.Endpoint, "test-key");
            client.Model = "test-model";
            client.TimeoutMs = 1000;

            ToolChatRequest request = CreateWeatherToolRequest();
            ToolChatResponse first = await client.ToolChatAsync(request, token).ConfigureAwait(false);

            SharedAssert.True(first.Success, "Gemini ToolChatAsync should succeed.");
            SharedAssert.Equal(1, first.ToolCalls.Count, "Gemini response should contain one tool call.");
            SharedAssert.Equal("get_weather", first.ToolCalls[0].Name, "Gemini response should parse function call name.");
            Dictionary<string, string> geminiArguments = LocalRequestParser.DeserializeStringDictionary(first.ToolCalls[0].ArgumentsJson);
            SharedAssert.Equal("Seattle", geminiArguments["city"], "Gemini response should parse function call args.");

            request.Messages.Add(first.ToAssistantMessage());
            request.Tools.Clear();
            request.ToolChoice = "none";
            request.Messages.Add(ChatMessage.ToolResult(first.ToolCalls[0].Id, first.ToolCalls[0].Name, "{\"temperature\":72,\"conditions\":\"clear\"}"));

            ToolChatResponse final = await client.ToolChatAsync(request, token).ConfigureAwait(false);

            SharedAssert.True(final.Success, "Gemini final ToolChatAsync should succeed.");
            SharedAssert.Equal("Seattle is 72 F and clear.", final.Text, "Gemini final response should parse text.");

            List<string> bodies = server.RequestBodies;
            LocalGeminiRequest initialRequest = DeserializeRecordedGeminiRequest(bodies[0]);
            LocalGeminiRequest followupRequest = DeserializeRecordedGeminiRequest(bodies[1]);

            SharedAssert.True(initialRequest.SystemInstruction?.Parts != null && initialRequest.SystemInstruction.Parts.Count == 1, "Gemini request should map system messages to systemInstruction.");
            SharedAssert.True(
                initialRequest.Tools != null
                    && initialRequest.Tools.Any(tool => tool.FunctionDeclarations != null
                        && tool.FunctionDeclarations.Any(declaration => string.Equals(declaration.Name, "get_weather", StringComparison.Ordinal))),
                "Gemini request should include function declarations.");
            SharedAssert.True(
                followupRequest.Contents != null
                    && followupRequest.Contents.Any(content => content.Parts != null
                        && content.Parts.Any(part => part.FunctionCall != null
                            && string.Equals(part.FunctionCall.Name, "get_weather", StringComparison.Ordinal))),
                "Gemini follow-up should include assistant function call history.");
            SharedAssert.True(
                followupRequest.Contents != null
                    && followupRequest.Contents.Any(content => content.Parts != null
                        && content.Parts.Any(part => part.FunctionResponse != null
                            && string.Equals(part.FunctionResponse.Name, "get_weather", StringComparison.Ordinal))),
                "Gemini follow-up should include function response.");
        }

        private static async Task RunGeminiToolChatStreamingAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using GeminiClient client = new GeminiClient(server.Endpoint, "test-key");
            client.Model = "test-model";
            client.TimeoutMs = 1000;

            ToolChatRequest request = CreateWeatherToolRequest();
            ToolChatStreamingResponse first = await client.ToolChatStreamingAsync(request, token).ConfigureAwait(false);

            SharedAssert.True(first.Success, "Gemini ToolChatStreamingAsync should start.");
            SharedAssert.Equal(200, first.StatusCode, "Gemini streaming tool chat should return HTTP 200.");

            List<ToolChatStreamingChunk> firstChunks = await ConsumeToolChatStreamAsync(first, token).ConfigureAwait(false);

            SharedAssert.True(firstChunks.Any(), "Gemini streaming tool chat should receive chunks.");
            SharedAssert.True(firstChunks.Any(chunk => !string.IsNullOrEmpty(chunk.Text)), "Gemini streaming tool chat should emit text chunks.");
            SharedAssert.True(firstChunks.Any(chunk => chunk.ToolCallDeltas.Any()), "Gemini streaming tool chat should emit tool-call deltas.");
            SharedAssert.Equal("Checking weather. ", first.Text, "Gemini streaming tool chat should accumulate text.");
            SharedAssert.Equal("STOP", first.FinishReason, "Gemini streaming tool chat should expose finish reason.");
            SharedAssert.Equal("gemini-tool-stream-local", first.ResponseId, "Gemini streaming tool chat should expose response id.");
            SharedAssert.Equal(2, first.ToolCalls.Count, "Gemini streaming tool chat should accumulate multiple tool calls.");
            SharedAssert.True(first.ToolCallDeltaCount >= 2, "Gemini streaming tool chat should count tool-call deltas.");
            SharedAssert.True(first.ChunkCount >= 3, "Gemini streaming tool chat should count content chunks.");
            SharedAssert.True(first.TimeToFirstTokenMs >= 0, "Gemini streaming tool chat should populate first-token timing.");
            SharedAssert.True(first.TimeToLastTokenMs >= first.TimeToFirstTokenMs, "Gemini streaming tool chat should order token timings.");
            SharedAssert.True(first.OverallRuntimeMs > 0, "Gemini streaming tool chat should populate runtime.");
            SharedAssert.True(first.OverallTokensPerSecond > 0, "Gemini streaming tool chat should populate throughput.");
            SharedAssert.NotNull(first.Usage, "Gemini streaming tool chat should expose usage.");
            SharedAssert.Equal(7, first.Usage!.CompletionTokens, "Gemini streaming tool chat should parse candidates token count.");

            SharedAssert.Equal("gemini-call-0", first.ToolCalls[0].Id, "Gemini streaming tool chat should synthesize first tool call id.");
            SharedAssert.Equal("get_weather", first.ToolCalls[0].Name, "Gemini streaming tool chat should parse first tool call name.");
            Dictionary<string, string> firstArguments = LocalRequestParser.DeserializeStringDictionary(first.ToolCalls[0].ArgumentsJson);
            SharedAssert.Equal("Seattle", firstArguments["city"], "Gemini streaming tool chat should parse first tool call args.");

            SharedAssert.Equal("gemini-call-1", first.ToolCalls[1].Id, "Gemini streaming tool chat should synthesize second tool call id.");
            Dictionary<string, string> secondArguments = LocalRequestParser.DeserializeStringDictionary(first.ToolCalls[1].ArgumentsJson);
            SharedAssert.Equal("Portland", secondArguments["city"], "Gemini streaming tool chat should parse second tool call args.");

            request.Messages.Add(first.ToAssistantMessage());
            request.Tools.Clear();
            request.ToolChoice = "none";
            foreach (ToolCall call in first.ToolCalls)
            {
                request.Messages.Add(ChatMessage.ToolResult(call.Id, call.Name, "{\"temperature\":72,\"conditions\":\"clear\"}"));
            }

            ToolChatStreamingResponse final = await client.ToolChatStreamingAsync(request, token).ConfigureAwait(false);
            List<ToolChatStreamingChunk> finalChunks = await ConsumeToolChatStreamAsync(final, token).ConfigureAwait(false);

            SharedAssert.True(final.Success, "Gemini final ToolChatStreamingAsync should start.");
            SharedAssert.True(finalChunks.Any(), "Gemini final streaming tool chat should receive chunks.");
            SharedAssert.Equal("Seattle is 72 F and clear.", final.Text, "Gemini final streaming response should accumulate text.");
            SharedAssert.Equal(0, final.ToolCalls.Count, "Gemini final streaming response should not accumulate tool calls.");
            SharedAssert.Equal("STOP", final.FinishReason, "Gemini final streaming response should expose stop reason.");
            SharedAssert.Equal("gemini-final-stream-local", final.ResponseId, "Gemini final streaming response should expose response id.");
            SharedAssert.NotNull(final.Usage, "Gemini final streaming response should expose usage.");
            SharedAssert.Equal(5, final.Usage!.CompletionTokens, "Gemini final streaming response should parse candidates token count.");

            List<string> bodies = server.RequestBodies;
            LocalGeminiRequest initialRequest = DeserializeRecordedGeminiRequest(bodies[0]);
            LocalGeminiRequest followupRequest = DeserializeRecordedGeminiRequest(bodies[1]);

            int followupFunctionCallCount = 0;
            int followupFunctionResponseCount = 0;
            if (followupRequest.Contents != null)
            {
                foreach (LocalGeminiContent content in followupRequest.Contents)
                {
                    if (content.Parts == null) continue;

                    foreach (LocalGeminiPart part in content.Parts)
                    {
                        if (part.FunctionCall != null
                            && string.Equals(part.FunctionCall.Name, "get_weather", StringComparison.Ordinal))
                        {
                            followupFunctionCallCount++;
                        }

                        if (part.FunctionResponse != null
                            && string.Equals(part.FunctionResponse.Name, "get_weather", StringComparison.Ordinal))
                        {
                            followupFunctionResponseCount++;
                        }
                    }
                }
            }

            SharedAssert.True(initialRequest.SystemInstruction?.Parts != null && initialRequest.SystemInstruction.Parts.Count == 1, "Gemini streaming request should map system messages to systemInstruction.");
            SharedAssert.True(
                initialRequest.Tools != null
                    && initialRequest.Tools.Any(tool => tool.FunctionDeclarations != null
                        && tool.FunctionDeclarations.Any(declaration => string.Equals(declaration.Name, "get_weather", StringComparison.Ordinal))),
                "Gemini streaming request should include function declarations.");
            SharedAssert.Equal(2, followupFunctionCallCount, "Gemini streaming follow-up should include assistant function call history.");
            SharedAssert.Equal(2, followupFunctionResponseCount, "Gemini streaming follow-up should include both function responses.");
            SharedAssert.True(followupRequest.Tools == null || !followupRequest.Tools.Any(), "Gemini streaming follow-up should omit tools when ToolChoice is none.");
        }

        private static async Task RunReasoningEffortOpenAiToolChatAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiClient client = CreateClient(server);

            ReasoningEffortLevel[] levels =
            {
                ReasoningEffortLevel.Minimal,
                ReasoningEffortLevel.Low,
                ReasoningEffortLevel.Medium,
                ReasoningEffortLevel.High
            };
            string[] wire = { "minimal", "low", "medium", "high" };

            for (int i = 0; i < levels.Length; i++)
            {
                ToolChatRequest request = CreateWeatherToolRequest();
                request.ReasoningEffort = levels[i];   // exercises the implicit ReasoningEffortLevel -> ReasoningEffort conversion
                ToolChatResponse response = await client.ToolChatAsync(request, token).ConfigureAwait(false);
                SharedAssert.True(response.Success, "OpenAI-compatible tool chat with reasoning effort should succeed.");
            }

            List<string> bodies = server.RequestBodies;
            for (int i = 0; i < levels.Length; i++)
            {
                LocalOpenAiChatRequest recorded = DeserializeRecordedOpenAiRequest(bodies[i]);
                SharedAssert.Equal(wire[i], recorded.ReasoningEffort, "OpenAI-compatible tool chat should send reasoning_effort for each level.");
            }
        }

        private static async Task RunReasoningEffortOpenAiToolChatStreamingAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiClient client = CreateClient(server);

            ToolChatRequest request = CreateWeatherToolRequest();
            request.ReasoningEffort = ReasoningEffort.High;

            ToolChatStreamingResponse stream = await client.ToolChatStreamingAsync(request, token).ConfigureAwait(false);
            await ConsumeToolChatStreamAsync(stream, token).ConfigureAwait(false);

            LocalOpenAiChatRequest recorded = DeserializeRecordedOpenAiRequest(server.RequestBodies[0]);
            SharedAssert.Equal("high", recorded.ReasoningEffort, "OpenAI-compatible streaming tool chat should send reasoning_effort.");
            SharedAssert.True(recorded.Stream == true, "OpenAI-compatible streaming tool chat should still set stream true.");
        }

        private static async Task RunReasoningEffortInstanceDefaultAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiClient client = CreateClient(server);
            client.ReasoningEffort = ReasoningEffort.High;

            ToolChatRequest request = CreateWeatherToolRequest();   // no per-request effort
            ToolChatResponse response = await client.ToolChatAsync(request, token).ConfigureAwait(false);
            SharedAssert.True(response.Success, "OpenAI-compatible tool chat with instance-default effort should succeed.");

            LocalOpenAiChatRequest recorded = DeserializeRecordedOpenAiRequest(server.RequestBodies[0]);
            SharedAssert.Equal("high", recorded.ReasoningEffort, "Instance-default reasoning effort should be applied when the request has none.");
        }

        private static async Task RunReasoningEffortRequestOverridesInstanceAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiClient client = CreateClient(server);
            client.ReasoningEffort = ReasoningEffort.Low;

            ToolChatRequest request = CreateWeatherToolRequest();
            request.ReasoningEffort = ReasoningEffort.High;
            ToolChatResponse response = await client.ToolChatAsync(request, token).ConfigureAwait(false);
            SharedAssert.True(response.Success, "OpenAI-compatible tool chat overriding the instance default should succeed.");

            LocalOpenAiChatRequest recorded = DeserializeRecordedOpenAiRequest(server.RequestBodies[0]);
            SharedAssert.Equal("high", recorded.ReasoningEffort, "Per-request reasoning effort should win over the instance default.");
        }

        private static async Task RunReasoningEffortGeminiToolChatAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using GeminiClient client = new GeminiClient(server.Endpoint, "test-key");
            client.Model = "test-model";
            client.TimeoutMs = 1000;

            ToolChatRequest high = CreateWeatherToolRequest();
            high.ReasoningEffort = ReasoningEffort.High;
            ToolChatResponse highResponse = await client.ToolChatAsync(high, token).ConfigureAwait(false);
            SharedAssert.True(highResponse.Success, "Gemini tool chat with high reasoning effort should succeed.");

            ToolChatRequest minimal = CreateWeatherToolRequest();
            minimal.ReasoningEffort = ReasoningEffort.Minimal;
            ToolChatResponse minimalResponse = await client.ToolChatAsync(minimal, token).ConfigureAwait(false);
            SharedAssert.True(minimalResponse.Success, "Gemini tool chat with minimal reasoning effort should succeed.");

            List<string> bodies = server.RequestBodies;
            LocalGeminiRequest highRequest = DeserializeRecordedGeminiRequest(bodies[0]);
            LocalGeminiRequest minimalRequest = DeserializeRecordedGeminiRequest(bodies[1]);

            SharedAssert.Equal(-1, highRequest.GenerationConfig!.ThinkingBudget, "Gemini high effort should map to a dynamic thinking budget (-1).");
            SharedAssert.Equal(0, minimalRequest.GenerationConfig!.ThinkingBudget, "Gemini minimal effort should disable thinking (0).");
        }

        private static async Task RunReasoningEffortOllamaToolChatAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OllamaClient client = new OllamaClient(server.Endpoint, "test-key");
            client.Model = "test-model";
            client.TimeoutMs = 1000;

            ToolChatRequest medium = CreateWeatherToolRequest();
            medium.ReasoningEffort = ReasoningEffort.Medium;
            ToolChatResponse mediumResponse = await client.ToolChatAsync(medium, token).ConfigureAwait(false);
            SharedAssert.True(mediumResponse.Success, "Ollama tool chat with medium reasoning effort should succeed.");

            ToolChatRequest minimal = CreateWeatherToolRequest();
            minimal.ReasoningEffort = ReasoningEffort.Minimal;
            ToolChatResponse minimalResponse = await client.ToolChatAsync(minimal, token).ConfigureAwait(false);
            SharedAssert.True(minimalResponse.Success, "Ollama tool chat with minimal reasoning effort should succeed.");

            List<string> bodies = server.RequestBodies;
            LocalOpenAiChatRequest mediumRequest = DeserializeRecordedOpenAiRequest(bodies[0]);
            LocalOpenAiChatRequest minimalRequest = DeserializeRecordedOpenAiRequest(bodies[1]);

            SharedAssert.Equal("medium", mediumRequest.Think, "Ollama medium effort should send think as the level string.");
            SharedAssert.True(minimalRequest.ThinkBool == false, "Ollama minimal effort should send think:false.");
        }

        private static async Task RunReasoningEffortProjectionDefaultsAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            SharedAssert.Equal("minimal", new ReasoningEffort(ReasoningEffortLevel.Minimal).ToOpenAiWireValue(), "Minimal maps to OpenAI 'minimal'.");
            SharedAssert.Equal("low", new ReasoningEffort(ReasoningEffortLevel.Low).ToOpenAiWireValue(), "Low maps to OpenAI 'low'.");
            SharedAssert.Equal("medium", new ReasoningEffort(ReasoningEffortLevel.Medium).ToOpenAiWireValue(), "Medium maps to OpenAI 'medium'.");
            SharedAssert.Equal("high", new ReasoningEffort(ReasoningEffortLevel.High).ToOpenAiWireValue(), "High maps to OpenAI 'high'.");

            SharedAssert.Equal(0, new ReasoningEffort(ReasoningEffortLevel.Minimal).ToGeminiThinkingBudget(), "Minimal maps to Gemini budget 0.");
            SharedAssert.Equal(1024, new ReasoningEffort(ReasoningEffortLevel.Low).ToGeminiThinkingBudget(), "Low maps to Gemini budget 1024.");
            SharedAssert.Equal(8192, new ReasoningEffort(ReasoningEffortLevel.Medium).ToGeminiThinkingBudget(), "Medium maps to Gemini budget 8192.");
            SharedAssert.Equal(-1, new ReasoningEffort(ReasoningEffortLevel.High).ToGeminiThinkingBudget(), "High maps to Gemini budget -1.");

            SharedAssert.True(new ReasoningEffort(ReasoningEffortLevel.Minimal).ToOllamaThink() is false, "Minimal maps to Ollama think:false.");
            SharedAssert.Equal("low", (string)new ReasoningEffort(ReasoningEffortLevel.Low).ToOllamaThink(), "Low maps to Ollama think 'low'.");
            SharedAssert.Equal("medium", (string)new ReasoningEffort(ReasoningEffortLevel.Medium).ToOllamaThink(), "Medium maps to Ollama think 'medium'.");
            SharedAssert.Equal("high", (string)new ReasoningEffort(ReasoningEffortLevel.High).ToOllamaThink(), "High maps to Ollama think 'high'.");

            await Task.CompletedTask.ConfigureAwait(false);
        }

        private static async Task RunReasoningEffortPresetsAndImplicitAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            SharedAssert.Equal(ReasoningEffortLevel.Minimal, ReasoningEffort.Minimal.Level, "Minimal preset should carry the Minimal level.");
            SharedAssert.Equal(ReasoningEffortLevel.Low, ReasoningEffort.Low.Level, "Low preset should carry the Low level.");
            SharedAssert.Equal(ReasoningEffortLevel.Medium, ReasoningEffort.Medium.Level, "Medium preset should carry the Medium level.");
            SharedAssert.Equal(ReasoningEffortLevel.High, ReasoningEffort.High.Level, "High preset should carry the High level.");

            ReasoningEffort implicitEffort = ReasoningEffortLevel.Low;
            SharedAssert.Equal(ReasoningEffortLevel.Low, implicitEffort.Level, "Implicit conversion should carry the level.");
            SharedAssert.True(
                implicitEffort.OpenAiValue == null && implicitEffort.GeminiThinkingBudget == null && implicitEffort.OllamaThink == null,
                "Implicit conversion should leave all overrides unset.");

            await Task.CompletedTask.ConfigureAwait(false);
        }

        private static async Task RunReasoningEffortOverridesWinAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();

            using GeminiClient gemini = new GeminiClient(server.Endpoint, "test-key");
            gemini.Model = "test-model";
            gemini.TimeoutMs = 1000;
            ToolChatRequest geminiRequest = CreateWeatherToolRequest();
            geminiRequest.ReasoningEffort = new ReasoningEffort(ReasoningEffortLevel.High) { GeminiThinkingBudget = 16000 };
            ToolChatResponse geminiResponse = await gemini.ToolChatAsync(geminiRequest, token).ConfigureAwait(false);
            SharedAssert.True(geminiResponse.Success, "Gemini tool chat with an overridden budget should succeed.");

            using OpenAiClient openAi = CreateClient(server);
            ToolChatRequest openAiRequest = CreateWeatherToolRequest();
            openAiRequest.ReasoningEffort = new ReasoningEffort(ReasoningEffortLevel.High) { GeminiThinkingBudget = 16000 };
            ToolChatResponse openAiResponse = await openAi.ToolChatAsync(openAiRequest, token).ConfigureAwait(false);
            SharedAssert.True(openAiResponse.Success, "OpenAI-compatible tool chat with a Gemini-only override should succeed.");

            using OllamaClient ollama = new OllamaClient(server.Endpoint, "test-key");
            ollama.Model = "test-model";
            ollama.TimeoutMs = 1000;
            ToolChatRequest ollamaRequest = CreateWeatherToolRequest();
            ollamaRequest.ReasoningEffort = new ReasoningEffort(ReasoningEffortLevel.Minimal) { OllamaThink = "true" };
            ToolChatResponse ollamaResponse = await ollama.ToolChatAsync(ollamaRequest, token).ConfigureAwait(false);
            SharedAssert.True(ollamaResponse.Success, "Ollama tool chat with a think override should succeed.");

            List<string> bodies = server.RequestBodies;
            LocalGeminiRequest geminiRecorded = DeserializeRecordedGeminiRequest(bodies[0]);
            LocalOpenAiChatRequest openAiRecorded = DeserializeRecordedOpenAiRequest(bodies[1]);
            LocalOpenAiChatRequest ollamaRecorded = DeserializeRecordedOpenAiRequest(bodies[2]);

            SharedAssert.Equal(16000, geminiRecorded.GenerationConfig!.ThinkingBudget, "Gemini override budget should win over the level default.");
            SharedAssert.Equal("high", openAiRecorded.ReasoningEffort, "OpenAI mapping should still derive 'high' from the level when only the Gemini value is overridden.");
            SharedAssert.True(ollamaRecorded.ThinkBool == true, "Ollama think override 'true' should be emitted as a JSON boolean.");
        }

        private static async Task RunReasoningEffortOverrideClampingAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            ReasoningEffort effort = new ReasoningEffort(ReasoningEffortLevel.Medium);

            effort.GeminiThinkingBudget = -5;
            SharedAssert.Equal(-1, effort.GeminiThinkingBudget, "Gemini budget should clamp to the -1 floor.");
            effort.GeminiThinkingBudget = 999999;
            SharedAssert.Equal(32768, effort.GeminiThinkingBudget, "Gemini budget should clamp to the 32768 ceiling.");
            effort.GeminiThinkingBudget = null;
            SharedAssert.True(effort.GeminiThinkingBudget == null, "Gemini budget should accept null to revert to the level default.");

            effort.OpenAiValue = " HIGH ";
            SharedAssert.Equal("high", effort.OpenAiValue, "OpenAI override should normalize case and whitespace.");
            effort.OpenAiValue = "banana";
            SharedAssert.True(effort.OpenAiValue == null, "Unrecognized OpenAI override should revert to null.");
            SharedAssert.Equal("medium", effort.ToOpenAiWireValue(), "A reverted OpenAI override should fall back to the level default.");

            effort.OllamaThink = "MEDIUM";
            SharedAssert.Equal("medium", effort.OllamaThink, "Ollama override should normalize case.");
            effort.OllamaThink = "maybe";
            SharedAssert.True(effort.OllamaThink == null, "Unrecognized Ollama override should revert to null.");

            await Task.CompletedTask.ConfigureAwait(false);
        }

        private static async Task RunReasoningEffortAbsentByDefaultAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();

            using OpenAiClient openAi = CreateClient(server);
            ToolChatResponse openAiResponse = await openAi.ToolChatAsync(CreateWeatherToolRequest(), token).ConfigureAwait(false);
            SharedAssert.True(openAiResponse.Success, "OpenAI-compatible tool chat without reasoning effort should succeed.");

            using OllamaClient ollama = new OllamaClient(server.Endpoint, "test-key");
            ollama.Model = "test-model";
            ollama.TimeoutMs = 1000;
            ToolChatResponse ollamaResponse = await ollama.ToolChatAsync(CreateWeatherToolRequest(), token).ConfigureAwait(false);
            SharedAssert.True(ollamaResponse.Success, "Ollama tool chat without reasoning effort should succeed.");

            using GeminiClient gemini = new GeminiClient(server.Endpoint, "test-key");
            gemini.Model = "test-model";
            gemini.TimeoutMs = 1000;
            ToolChatResponse geminiResponse = await gemini.ToolChatAsync(CreateWeatherToolRequest(), token).ConfigureAwait(false);
            SharedAssert.True(geminiResponse.Success, "Gemini tool chat without reasoning effort should succeed.");

            List<string> bodies = server.RequestBodies;
            LocalOpenAiChatRequest openAiRecorded = DeserializeRecordedOpenAiRequest(bodies[0]);
            LocalOpenAiChatRequest ollamaRecorded = DeserializeRecordedOpenAiRequest(bodies[1]);
            LocalGeminiRequest geminiRecorded = DeserializeRecordedGeminiRequest(bodies[2]);

            SharedAssert.True(openAiRecorded.ReasoningEffort == null, "OpenAI-compatible request should omit reasoning_effort by default.");
            SharedAssert.True(ollamaRecorded.Think == null && ollamaRecorded.ThinkBool == null, "Ollama request should omit think by default.");
            SharedAssert.True(geminiRecorded.GenerationConfig != null && geminiRecorded.GenerationConfig.ThinkingBudget == null, "Gemini request should omit thinkingConfig by default.");
        }

        private static async Task RunReasoningEffortUndefinedLevelThrowsAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            ReasoningEffort effort = new ReasoningEffort { Level = (ReasoningEffortLevel)999 };

            await SharedAssert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => { effort.ToOpenAiWireValue(); return Task.CompletedTask; },
                "An undefined level should throw from ToOpenAiWireValue.").ConfigureAwait(false);

            await SharedAssert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => { effort.ToGeminiThinkingBudget(); return Task.CompletedTask; },
                "An undefined level should throw from ToGeminiThinkingBudget.").ConfigureAwait(false);

            await SharedAssert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => { effort.ToOllamaThink(); return Task.CompletedTask; },
                "An undefined level should throw from ToOllamaThink.").ConfigureAwait(false);
        }

        private static async Task RunReasoningOpenAiChatAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiClient client = CreateClient(server);

            ChatResponse response = await client.ChatAsync("reasoncapture please", token: token).ConfigureAwait(false);
            SharedAssert.True(response.Success, "OpenAI reasoning chat should succeed.");
            SharedAssert.Equal("pong", response.Text, "OpenAI reasoning chat should return the answer text.");
            SharedAssert.Equal("Let me think.", response.Reasoning, "OpenAI reasoning chat should surface reasoning.");
        }

        private static async Task RunReasoningOpenAiChatStreamingAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiClient client = CreateClient(server);

            ChatStreamingResponse stream = await client.ChatStreamingAsync("reasoncapture stream", token: token).ConfigureAwait(false);
            List<ChatStreamingChunk> chunks = await ConsumeChatStreamAsync(stream, token).ConfigureAwait(false);

            SharedAssert.True(stream.Success, "OpenAI reasoning streaming chat should start.");
            SharedAssert.Equal("pong", CombineChatText(chunks), "OpenAI reasoning streaming chat should assemble the answer.");
            SharedAssert.Equal("Let me think.", stream.Reasoning, "OpenAI reasoning streaming chat should accumulate reasoning.");
            SharedAssert.True(chunks.Any(c => !string.IsNullOrEmpty(c.ReasoningText)), "reasoning deltas should be present on chunks.");
            SharedAssert.False(CombineChatText(chunks).Contains("think", StringComparison.Ordinal), "reasoning must not leak into text.");
        }

        private static async Task RunReasoningOpenAiToolChatStreamingAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiClient client = CreateClient(server);

            ToolChatStreamingResponse stream = await client.ToolChatStreamingAsync(CreateReasoningToolRequest(), token).ConfigureAwait(false);
            List<ToolChatStreamingChunk> chunks = await ConsumeToolChatStreamAsync(stream, token).ConfigureAwait(false);

            SharedAssert.True(stream.Success, "OpenAI reasoning tool chat should start.");
            SharedAssert.Equal("Planning the call.", stream.Reasoning, "OpenAI reasoning tool chat should accumulate reasoning.");
            SharedAssert.Equal("Checking. ", stream.Text, "OpenAI reasoning tool chat should accumulate answer text separately.");
            SharedAssert.True(stream.ToolCalls.Count == 1, "OpenAI reasoning tool chat should still surface the tool call.");
            SharedAssert.True(chunks.Any(c => !string.IsNullOrEmpty(c.ReasoningText)), "reasoning deltas should be present on chunks.");
            SharedAssert.False((stream.Text ?? string.Empty).Contains("Planning", StringComparison.Ordinal), "reasoning must not leak into text.");
        }

        private static async Task RunReasoningOllamaToolChatAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OllamaClient client = new OllamaClient(server.Endpoint, "test-key");
            client.Model = "test-model";
            client.TimeoutMs = 1000;

            ToolChatResponse response = await client.ToolChatAsync(CreateReasoningToolRequest(), token).ConfigureAwait(false);
            SharedAssert.True(response.Success, "Ollama reasoning tool chat should succeed.");
            SharedAssert.Equal("Let me think.", response.Reasoning, "Ollama reasoning tool chat should surface thinking.");
            SharedAssert.True(response.ToolCalls.Count == 1, "Ollama reasoning tool chat should surface the tool call.");
        }

        private static async Task RunReasoningOllamaChatStreamingAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OllamaClient client = new OllamaClient(server.Endpoint, "test-key");
            client.Model = "test-model";
            client.TimeoutMs = 1000;

            ChatStreamingResponse stream = await client.ChatStreamingAsync("reasoncapture stream", token: token).ConfigureAwait(false);
            List<ChatStreamingChunk> chunks = await ConsumeChatStreamAsync(stream, token).ConfigureAwait(false);

            SharedAssert.True(stream.Success, "Ollama reasoning streaming chat should start.");
            SharedAssert.Equal("pong", CombineChatText(chunks), "Ollama reasoning streaming chat should assemble the answer.");
            SharedAssert.Equal("Let me think.", stream.Reasoning, "Ollama reasoning streaming chat should accumulate thinking.");
        }

        private static async Task RunReasoningGeminiChatAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using GeminiClient client = new GeminiClient(server.Endpoint, "test-key");
            client.Model = "test-model";
            client.TimeoutMs = 1000;

            ChatResponse response = await client.ChatAsync("reasoncapture please", token: token).ConfigureAwait(false);
            SharedAssert.True(response.Success, "Gemini reasoning chat should succeed.");
            SharedAssert.Equal("pong", response.Text, "Gemini reasoning chat should return the answer text.");
            SharedAssert.Equal("Let me think.", response.Reasoning, "Gemini reasoning chat should surface the thought part.");
        }

        private static async Task RunReasoningGeminiChatStreamingAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using GeminiClient client = new GeminiClient(server.Endpoint, "test-key");
            client.Model = "test-model";
            client.TimeoutMs = 1000;

            ChatStreamingResponse stream = await client.ChatStreamingAsync("reasoncapture stream", token: token).ConfigureAwait(false);
            List<ChatStreamingChunk> chunks = await ConsumeChatStreamAsync(stream, token).ConfigureAwait(false);

            SharedAssert.True(stream.Success, "Gemini reasoning streaming chat should start.");
            SharedAssert.Equal("pong", CombineChatText(chunks), "Gemini reasoning streaming chat should assemble the answer.");
            SharedAssert.Equal("Let me think.", stream.Reasoning, "Gemini reasoning streaming chat should accumulate the thought.");
            SharedAssert.False(CombineChatText(chunks).Contains("Let me", StringComparison.Ordinal), "the thought must not leak into text.");
        }

        private static async Task RunReasoningAbsentByDefaultAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();

            using OpenAiClient openAi = CreateClient(server);
            ChatResponse openAiResponse = await openAi.ChatAsync("hello", token: token).ConfigureAwait(false);
            SharedAssert.True(openAiResponse.Reasoning == null, "OpenAI chat without reasoning should leave Reasoning null.");

            using OllamaClient ollama = new OllamaClient(server.Endpoint, "test-key");
            ollama.Model = "test-model";
            ollama.TimeoutMs = 1000;
            ChatResponse ollamaResponse = await ollama.ChatAsync("hello", token: token).ConfigureAwait(false);
            SharedAssert.True(ollamaResponse.Reasoning == null, "Ollama chat without reasoning should leave Reasoning null.");

            using GeminiClient gemini = new GeminiClient(server.Endpoint, "test-key");
            gemini.Model = "test-model";
            gemini.TimeoutMs = 1000;
            ChatResponse geminiResponse = await gemini.ChatAsync("hello", token: token).ConfigureAwait(false);
            SharedAssert.True(geminiResponse.Reasoning == null, "Gemini chat without reasoning should leave Reasoning null.");
        }

        private static async Task RunReasoningEmptyIsNullAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiClient client = CreateClient(server);

            ChatResponse response = await client.ChatAsync("reasonempty please", token: token).ConfigureAwait(false);
            SharedAssert.True(response.Success, "empty-reasoning response should succeed.");
            SharedAssert.Equal("pong", response.Text, "empty-reasoning response should still return text.");
            SharedAssert.True(response.Reasoning == null, "empty reasoning should normalize to null.");
        }

        private static async Task RunReasoningNotResentAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiClient client = CreateClient(server);

            ToolChatStreamingResponse stream = await client.ToolChatStreamingAsync(CreateReasoningToolRequest(), token).ConfigureAwait(false);
            await ConsumeToolChatStreamAsync(stream, token).ConfigureAwait(false);
            SharedAssert.NotEmpty(stream.Reasoning, "precondition: the turn produced reasoning.");

            ChatMessage assistant = stream.ToAssistantMessage();
            SharedAssert.False((assistant.Content ?? string.Empty).Contains("Planning", StringComparison.Ordinal), "reasoning must not be carried into the follow-up message content.");
            foreach (ToolCall call in assistant.ToolCalls)
            {
                SharedAssert.False((call.ArgumentsJson ?? string.Empty).Contains("Planning", StringComparison.Ordinal), "reasoning must not appear in tool call arguments.");
            }
        }

        private static ToolChatRequest CreateReasoningToolRequest()
        {
            ToolChatRequest request = new ToolChatRequest();
            request.Messages.Add(ChatMessage.User("reasoncapture: what is the weather in Seattle?"));
            request.Tools.Add(ToolDefinition.Function(
                "get_weather",
                "Get current weather for a city.",
                WeatherParameters()));
            return request;
        }

        private static async Task RunReasoningEffortAndCaptureTogetherAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiClient client = CreateClient(server);

            ToolChatRequest request = CreateReasoningToolRequest();
            request.ReasoningEffort = ReasoningEffort.High;

            ToolChatStreamingResponse stream = await client.ToolChatStreamingAsync(request, token).ConfigureAwait(false);
            await ConsumeToolChatStreamAsync(stream, token).ConfigureAwait(false);

            // Outbound: the effort control is sent on the request.
            LocalOpenAiChatRequest recorded = DeserializeRecordedOpenAiRequest(server.RequestBodies[0]);
            SharedAssert.Equal("high", recorded.ReasoningEffort, "the request should carry reasoning_effort.");

            // Inbound: the model's reasoning is returned to the caller.
            SharedAssert.Equal("Planning the call.", stream.Reasoning, "the response should surface the model's reasoning.");
            SharedAssert.True(stream.ToolCalls.Count == 1, "the tool call should still be surfaced.");
        }

        private static async Task RunReasoningOpenAiToolChatAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiClient client = CreateClient(server);

            ToolChatResponse response = await client.ToolChatAsync(CreateReasoningToolRequest(), token).ConfigureAwait(false);
            SharedAssert.True(response.Success, "OpenAI non-streaming reasoning tool chat should succeed.");
            SharedAssert.Equal("Let me think.", response.Reasoning, "OpenAI non-streaming tool chat should surface reasoning.");
            SharedAssert.True(response.ToolCalls.Count == 1, "OpenAI non-streaming reasoning tool chat should surface the tool call.");
        }

        private static async Task RunReasoningOpenAiChatFallbackAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiClient client = CreateClient(server);

            ChatResponse response = await client.ChatAsync("reasonfallback please", token: token).ConfigureAwait(false);
            SharedAssert.True(response.Success, "OpenAI fallback reasoning chat should succeed.");
            SharedAssert.Equal("Fallback thinking.", response.Reasoning, "OpenAI should read the 'reasoning' fallback field.");
        }

        private static async Task RunReasoningOpenAiMalformedAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiClient client = CreateClient(server);

            ChatResponse response = await client.ChatAsync("reasonmalformed please", token: token).ConfigureAwait(false);
            SharedAssert.True(response.Success, "a malformed reasoning field should not fail the response.");
            SharedAssert.Equal("pong", response.Text, "a malformed reasoning field should not affect the answer text.");
        }

        private static async Task RunReasoningOllamaChatAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OllamaClient client = new OllamaClient(server.Endpoint, "test-key");
            client.Model = "test-model";
            client.TimeoutMs = 1000;

            ChatResponse response = await client.ChatAsync("reasoncapture please", token: token).ConfigureAwait(false);
            SharedAssert.True(response.Success, "Ollama non-streaming reasoning chat should succeed.");
            SharedAssert.Equal("pong", response.Text, "Ollama non-streaming reasoning chat should return the answer.");
            SharedAssert.Equal("Let me think.", response.Reasoning, "Ollama non-streaming chat should surface thinking.");
        }

        private static async Task RunReasoningOllamaToolChatStreamingAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OllamaClient client = new OllamaClient(server.Endpoint, "test-key");
            client.Model = "test-model";
            client.TimeoutMs = 1000;

            ToolChatStreamingResponse stream = await client.ToolChatStreamingAsync(CreateReasoningToolRequest(), token).ConfigureAwait(false);
            List<ToolChatStreamingChunk> chunks = await ConsumeToolChatStreamAsync(stream, token).ConfigureAwait(false);

            SharedAssert.True(stream.Success, "Ollama streaming reasoning tool chat should start.");
            SharedAssert.Equal("Let me think.", stream.Reasoning, "Ollama streaming tool chat should accumulate thinking.");
            SharedAssert.True(stream.ToolCalls.Count == 1, "Ollama streaming reasoning tool chat should surface the tool call.");
            SharedAssert.True(chunks.Any(c => !string.IsNullOrEmpty(c.ReasoningText)), "reasoning deltas should be present on chunks.");
        }

        private static async Task RunReasoningGeminiToolChatAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using GeminiClient client = new GeminiClient(server.Endpoint, "test-key");
            client.Model = "test-model";
            client.TimeoutMs = 1000;

            ToolChatResponse response = await client.ToolChatAsync(CreateReasoningToolRequest(), token).ConfigureAwait(false);
            SharedAssert.True(response.Success, "Gemini non-streaming reasoning tool chat should succeed.");
            SharedAssert.Equal("Let me think.", response.Reasoning, "Gemini non-streaming tool chat should surface the thought part.");
            SharedAssert.True(response.ToolCalls.Count == 1, "Gemini non-streaming reasoning tool chat should surface the function call.");
        }

        private static async Task RunReasoningGeminiToolChatStreamingAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using GeminiClient client = new GeminiClient(server.Endpoint, "test-key");
            client.Model = "test-model";
            client.TimeoutMs = 1000;

            ToolChatStreamingResponse stream = await client.ToolChatStreamingAsync(CreateReasoningToolRequest(), token).ConfigureAwait(false);
            List<ToolChatStreamingChunk> chunks = await ConsumeToolChatStreamAsync(stream, token).ConfigureAwait(false);

            SharedAssert.True(stream.Success, "Gemini streaming reasoning tool chat should start.");
            SharedAssert.Equal("Let me think.", stream.Reasoning, "Gemini streaming tool chat should accumulate the thought.");
            SharedAssert.True(stream.ToolCalls.Count == 1, "Gemini streaming reasoning tool chat should accumulate the function call.");
            SharedAssert.True(chunks.Any(c => !string.IsNullOrEmpty(c.ReasoningText)), "reasoning deltas should be present on chunks.");
        }

        private static async Task RunReasoningEmptyOllamaGeminiAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();

            using OllamaClient ollama = new OllamaClient(server.Endpoint, "test-key");
            ollama.Model = "test-model";
            ollama.TimeoutMs = 1000;
            ChatResponse ollamaResponse = await ollama.ChatAsync("reasonempty please", token: token).ConfigureAwait(false);
            SharedAssert.Equal("pong", ollamaResponse.Text, "Ollama empty-thinking response should still return text.");
            SharedAssert.True(ollamaResponse.Reasoning == null, "Ollama empty thinking should normalize to null.");

            using GeminiClient gemini = new GeminiClient(server.Endpoint, "test-key");
            gemini.Model = "test-model";
            gemini.TimeoutMs = 1000;
            ChatResponse geminiResponse = await gemini.ChatAsync("reasonempty please", token: token).ConfigureAwait(false);
            SharedAssert.Equal("pong", geminiResponse.Text, "Gemini empty-thought response should still return text.");
            SharedAssert.True(geminiResponse.Reasoning == null, "Gemini empty thought should normalize to null.");
        }

        private static async Task RunReasoningOnlyChunksCountMetricsAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiClient client = CreateClient(server);

            ChatStreamingResponse stream = await client.ChatStreamingAsync("reasoncapture stream", token: token).ConfigureAwait(false);
            await ConsumeChatStreamAsync(stream, token).ConfigureAwait(false);

            // The fixture emits two reasoning-only deltas then one text delta; all three count.
            SharedAssert.True(stream.Success, "reasoning-only metrics stream should start.");
            SharedAssert.True(stream.ChunkCount >= 3, "reasoning-only chunks should count toward ChunkCount.");
            SharedAssert.True(stream.TimeToFirstTokenMs >= 0, "the first reasoning delta should set time-to-first-token.");
        }

        private static async Task RunGeminiMultipartTextAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using GeminiClient client = new GeminiClient(server.Endpoint, "test-key");
            client.Model = "test-model";
            client.TimeoutMs = 1000;

            ChatResponse response = await client.ChatAsync("multipart please", token: token).ConfigureAwait(false);
            SharedAssert.True(response.Success, "Gemini multi-part chat should succeed.");
            SharedAssert.Equal("Hello world", response.Text, "Gemini should concatenate multiple non-thought text parts.");
            SharedAssert.True(response.Reasoning == null, "a multi-part text response has no reasoning.");
        }

        private static async Task RunOpenAiEmbeddingGenerationModelsAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiClient client = CreateClient(server);

            OpenAiEmbeddingOptions embeddingOptions = new OpenAiEmbeddingOptions();
            embeddingOptions.Model = "embedding-model";
            embeddingOptions.EncodingFormat = "float";
            embeddingOptions.Dimensions = 3;

            EmbeddingResponse embeddings = await client.EmbedAsync(
                new List<string> { "first", "second" },
                embeddingOptions,
                token).ConfigureAwait(false);

            SharedAssert.True(embeddings.Success, "OpenAI-compatible embedding request should succeed.");
            SharedAssert.Equal(2, embeddings.Embeddings.Count, "OpenAI-compatible embedding response should parse both vectors.");
            SharedAssert.Equal(3, embeddings.Embeddings[0].Embedding.Length, "OpenAI-compatible embedding vector length should parse.");

            OpenAiGenerationOptions generationOptions = new OpenAiGenerationOptions();
            generationOptions.Model = "generation-model";
            generationOptions.MaxTokens = 17;
            generationOptions.Temperature = 0.4;
            generationOptions.TopP = 0.8;

            GenerationResponse generation = await client.GenerateAsync("generate me", generationOptions, token).ConfigureAwait(false);
            SharedAssert.True(generation.Success, "OpenAI-compatible generation should succeed.");
            SharedAssert.Equal("generated text", generation.Text, "OpenAI-compatible generation text should parse.");

            List<ModelInformation> models = await GetModelsAsync(client, token).ConfigureAwait(false);
            SharedAssert.Equal(1, models.Count, "OpenAI-compatible ListModelsAsync should parse one model.");
            SharedAssert.Equal("test-model", models[0].Name, "OpenAI-compatible model name should parse.");
            SharedAssert.True(await client.ModelExistsAsync("test-model", token).ConfigureAwait(false), "OpenAI-compatible ModelExistsAsync should find exact model.");

            ModelInformation? modelInfo = await client.GetModelInformationAsync("test-model", token).ConfigureAwait(false);
            SharedAssert.NotNull(modelInfo, "OpenAI-compatible GetModelInformationAsync should parse model info.");
            SharedAssert.Equal("local", modelInfo!.OwnedBy, "OpenAI-compatible model owner should parse.");

            List<string> bodies = server.RequestBodies;
            LocalEmbeddingRequest embeddingRequest = LocalRequestParser.DeserializeEmbeddingRequest(bodies[0]) ?? new LocalEmbeddingRequest();
            LocalGenerateRequest generationRequest = LocalRequestParser.DeserializeGenerateRequest(bodies[1]) ?? new LocalGenerateRequest();
            SharedAssert.Equal("embedding-model", embeddingRequest.Model, "OpenAI-compatible embedding request should use option model.");
            SharedAssert.Equal(3, embeddingRequest.Dimensions, "OpenAI-compatible embedding request should include dimensions.");
            SharedAssert.Equal("generation-model", generationRequest.Model, "OpenAI-compatible generation request should use option model.");
            SharedAssert.Equal(17, generationRequest.MaxTokens, "OpenAI-compatible generation request should include max tokens.");
        }

        private static async Task RunOllamaEmbeddingGenerationModelsAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OllamaClient client = new OllamaClient(server.Endpoint, "test-key");
            client.Model = "test-model";
            client.TimeoutMs = 1000;

            OllamaEmbeddingOptions embeddingOptions = new OllamaEmbeddingOptions();
            embeddingOptions.Model = "embedding-model";
            embeddingOptions.Truncate = 128;
            embeddingOptions.ContextLength = 2048;

            EmbeddingResponse embeddings = await client.EmbedAsync(
                new List<string> { "first", "second" },
                embeddingOptions,
                token).ConfigureAwait(false);

            SharedAssert.True(embeddings.Success, "Ollama embedding request should succeed.");
            SharedAssert.Equal(2, embeddings.Embeddings.Count, "Ollama embedding response should parse both vectors.");
            SharedAssert.Equal(1, embeddings.Embeddings[0].Embedding[0], "Ollama embedding vector values should parse.");

            OllamaGenerationOptions generationOptions = new OllamaGenerationOptions();
            generationOptions.Model = "generation-model";
            generationOptions.MaxTokens = 19;
            generationOptions.Temperature = 0.3;
            generationOptions.TopP = 0.7;
            generationOptions.ContextLength = 4096;

            GenerationResponse generation = await client.GenerateAsync("generate me", generationOptions, token).ConfigureAwait(false);
            SharedAssert.True(generation.Success, "Ollama generation should succeed.");
            SharedAssert.Equal("generated text", generation.Text, "Ollama generation text should parse.");

            List<ModelInformation> models = await GetModelsAsync(client, token).ConfigureAwait(false);
            SharedAssert.Equal(1, models.Count, "Ollama ListModelsAsync should parse one model.");
            SharedAssert.Equal("test-model:latest", models[0].Name, "Ollama model name should parse.");
            SharedAssert.True(await client.ModelExistsAsync("test-model", token).ConfigureAwait(false), "Ollama ModelExistsAsync should match model without tag.");

            ModelInformation? modelInfo = await client.GetModelInformationAsync("test-model", token).ConfigureAwait(false);
            SharedAssert.NotNull(modelInfo, "Ollama GetModelInformationAsync should parse model info.");
            SharedAssert.Equal("1B", modelInfo!.Metadata["parameter_size"], "Ollama model details should parse parameter size.");

            List<ModelPullProgress> progressEvents = new List<ModelPullProgress>();
            bool pullResult = await client.PullModelAsync(
                "test-model",
                progress =>
                {
                    progressEvents.Add(progress);
                    return Task.CompletedTask;
                },
                token).ConfigureAwait(false);

            SharedAssert.True(pullResult, "Ollama PullModelAsync should return success.");
            SharedAssert.True(progressEvents.Count >= 1, "Ollama PullModelAsync should report progress.");
            SharedAssert.True(await client.DeleteModelAsync("test-model", token).ConfigureAwait(false), "Ollama DeleteModelAsync should return success.");

            List<string> bodies = server.RequestBodies;
            LocalEmbeddingRequest embeddingRequest = LocalRequestParser.DeserializeEmbeddingRequest(bodies[0]) ?? new LocalEmbeddingRequest();
            LocalGenerateRequest generationRequest = LocalRequestParser.DeserializeGenerateRequest(bodies[1]) ?? new LocalGenerateRequest();
            SharedAssert.Equal("embedding-model", embeddingRequest.Model, "Ollama embedding request should use option model.");
            SharedAssert.Equal(128, embeddingRequest.Truncate, "Ollama embedding request should include truncate.");
            SharedAssert.Equal("generation-model", generationRequest.Model, "Ollama generation request should use option model.");
            SharedAssert.Equal(19, generationRequest.MaxTokens, "Ollama generation request should include num_predict.");
        }

        private static async Task RunGeminiEmbeddingGenerationModelsAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using GeminiClient client = new GeminiClient(server.Endpoint, "test-key");
            client.Model = "test-model";
            client.TimeoutMs = 1000;

            GeminiEmbeddingOptions embeddingOptions = new GeminiEmbeddingOptions();
            embeddingOptions.Model = "embedding-model";
            embeddingOptions.TaskType = "RETRIEVAL_DOCUMENT";
            embeddingOptions.Title = "Document title";

            EmbeddingResponse singleEmbedding = await client.EmbedAsync("first", embeddingOptions, token).ConfigureAwait(false);
            SharedAssert.True(singleEmbedding.Success, "Gemini single embedding request should succeed.");
            SharedAssert.Equal(1, singleEmbedding.Embeddings.Count, "Gemini single embedding response should parse one vector.");

            EmbeddingResponse batchEmbedding = await client.EmbedAsync(
                new List<string> { "first", "second" },
                embeddingOptions,
                token).ConfigureAwait(false);
            SharedAssert.True(batchEmbedding.Success, "Gemini batch embedding request should succeed.");
            SharedAssert.Equal(2, batchEmbedding.Embeddings.Count, "Gemini batch embedding response should parse two vectors.");

            GeminiGenerationOptions generationOptions = new GeminiGenerationOptions();
            generationOptions.Model = "generation-model";
            generationOptions.MaxTokens = 23;
            generationOptions.Temperature = 0.2;
            generationOptions.TopP = 0.6;

            GenerationResponse generation = await client.GenerateAsync("generate me", generationOptions, token).ConfigureAwait(false);
            SharedAssert.True(generation.Success, "Gemini generation should succeed.");
            SharedAssert.Equal("pong", generation.Text, "Gemini generation text should parse.");

            List<ModelInformation> models = await GetModelsAsync(client, token).ConfigureAwait(false);
            SharedAssert.Equal(1, models.Count, "Gemini ListModelsAsync should parse one model.");
            SharedAssert.Equal("test-model", models[0].Name, "Gemini model name should strip models prefix.");

            ModelInformation? modelInfo = await client.GetModelInformationAsync("test-model", token).ConfigureAwait(false);
            SharedAssert.NotNull(modelInfo, "Gemini GetModelInformationAsync should parse model info.");
            SharedAssert.Equal("Test Model", modelInfo!.DisplayName, "Gemini model display name should parse.");

            List<string> bodies = server.RequestBodies;
            LocalGeminiRequest generationRequest = DeserializeRecordedGeminiRequest(bodies[2]);
            SharedAssert.True(generationRequest.GenerationConfig != null && generationRequest.GenerationConfig.MaxOutputTokens == 23, "Gemini generation request should include max output tokens.");
        }

        private static async Task RunUnsupportedProviderModelManagementAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiClient openAiClient = CreateClient(server);
            using GeminiClient geminiClient = new GeminiClient(server.Endpoint, "test-key");

            await SharedAssert.ThrowsAsync<NotSupportedException>(
                () => openAiClient.PullModelAsync("test-model", token: token),
                "OpenAI-compatible PullModelAsync should be unsupported.").ConfigureAwait(false);

            await SharedAssert.ThrowsAsync<NotSupportedException>(
                () => openAiClient.DeleteModelAsync("test-model", token),
                "OpenAI-compatible DeleteModelAsync should be unsupported.").ConfigureAwait(false);

            await SharedAssert.ThrowsAsync<NotSupportedException>(
                () => geminiClient.PullModelAsync("test-model", token: token),
                "Gemini PullModelAsync should be unsupported.").ConfigureAwait(false);

            await SharedAssert.ThrowsAsync<NotSupportedException>(
                () => geminiClient.DeleteModelAsync("test-model", token),
                "Gemini DeleteModelAsync should be unsupported.").ConfigureAwait(false);
        }

        private static async Task RunHttpErrorHandlingAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiClient client = new OpenAiClient(server.Endpoint + "/missing", "test-key");
            client.Model = "test-model";
            client.TimeoutMs = 500;

            ChatResponse response = await client.ChatAsync("hello", token: token).ConfigureAwait(false);
            SharedAssert.False(response.Success, "HTTP error responses should produce unsuccessful chat responses.");
            SharedAssert.True(response.StatusCode == 404, "HTTP error responses should preserve status code.");
            SharedAssert.NotEmpty(response.Error, "HTTP error responses should surface an error message.");
        }

        private static async Task RunTimeoutValidationAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiClient client = CreateClient(server);

            client.TimeoutMs = 1;
            SharedAssert.Equal(1, client.TimeoutMs, "TimeoutMs should preserve 1ms values.");

            client.TimeoutMs = 999999;
            SharedAssert.Equal(999999, client.TimeoutMs, "TimeoutMs should preserve large positive values.");

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
        }

        private static async Task RunValidateConnectivityCancellationAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiClient client = CreateClient(server);
            using CancellationTokenSource preCancelled = new CancellationTokenSource();
            preCancelled.Cancel();

            await SharedAssert.ThrowsAsync<OperationCanceledException>(
                () => client.ValidateConnectivityAsync(preCancelled.Token),
                "ValidateConnectivityAsync should propagate cancellation.").ConfigureAwait(false);
        }

        private static async Task RunStreamingBodyTimeoutAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiClient client = CreateClient(server);
            client.TimeoutMs = 100;

            ChatStreamingResponse streaming = await client.ChatStreamingAsync("hang stream", token: token).ConfigureAwait(false);
            SharedAssert.True(streaming.Success, "Local streaming request should start.");

            Stopwatch streamWatch = Stopwatch.StartNew();
            bool streamTimedOut = false;
            int chunks = 0;

            try
            {
                await foreach (ChatStreamingChunk chunk in streaming.Chunks.ConfigureAwait(false))
                {
                    if (!string.IsNullOrEmpty(chunk.Text)) chunks++;
                }
            }
            catch (OperationCanceledException)
            {
                streamTimedOut = true;
            }

            streamWatch.Stop();

            SharedAssert.True(streamTimedOut, "Streaming body enumeration should time out.");
            SharedAssert.True(chunks > 0, "Streaming body should yield the initial chunk before timing out.");
            SharedAssert.True(streamWatch.ElapsedMilliseconds < 3000, "Streaming timeout should use the subsecond TimeoutMs value.");
        }

        private static async Task RunToolChatStreamingBodyTimeoutAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiClient client = CreateClient(server);
            client.TimeoutMs = 100;

            ToolChatRequest request = CreateWeatherToolRequest();
            request.Messages.Clear();
            request.Messages.Add(ChatMessage.User("hang tool stream"));

            ToolChatStreamingResponse streaming = await client.ToolChatStreamingAsync(request, token).ConfigureAwait(false);
            SharedAssert.True(streaming.Success, "Local streaming tool chat request should start.");

            Stopwatch streamWatch = Stopwatch.StartNew();
            bool streamTimedOut = false;
            int toolDeltas = 0;

            try
            {
                await foreach (ToolChatStreamingChunk chunk in streaming.Chunks.ConfigureAwait(false))
                {
                    toolDeltas += chunk.ToolCallDeltas.Count;
                }
            }
            catch (OperationCanceledException)
            {
                streamTimedOut = true;
            }

            streamWatch.Stop();

            SharedAssert.True(streamTimedOut, "Streaming tool chat body enumeration should time out.");
            SharedAssert.True(toolDeltas > 0, "Streaming tool chat body should yield the initial tool-call delta before timing out.");
            SharedAssert.True(streaming.ToolCalls.Count == 1, "Streaming tool chat should accumulate partial tool call metadata before timing out.");
            SharedAssert.True(streamWatch.ElapsedMilliseconds < 3000, "Streaming tool chat timeout should use the subsecond TimeoutMs value.");
        }

        private static async Task RunPostAndRecordDisposesResponseAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using ProbeOpenAiClient probe = new ProbeOpenAiClient(server.Endpoint, "test-key");
            probe.TimeoutMs = 1000;

            CompletionHttpResult result = await probe.PostProbeAsync(token).ConfigureAwait(false);
            SharedAssert.True(result.IsSuccessStatusCode && result.StatusCode == 200, "Probe PostAndRecordAsync should succeed.");

            bool responseDisposed = result.Response == null;
            if (result.Response != null)
            {
                try
                {
                    await result.Response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    responseDisposed = true;
                }
            }

            SharedAssert.True(responseDisposed, "PostAndRecordAsync should dispose the retained response object.");
        }

        private static Dictionary<string, string?> CaptureProviderEnvironment()
        {
            Dictionary<string, string?> values = new Dictionary<string, string?>();

            foreach (string name in _ProviderEnvironmentVariables)
            {
                values[name] = Environment.GetEnvironmentVariable(name);
            }

            return values;
        }

        private static void ClearProviderEnvironment()
        {
            foreach (string name in _ProviderEnvironmentVariables)
            {
                Environment.SetEnvironmentVariable(name, null);
            }
        }

        private static void RestoreProviderEnvironment(Dictionary<string, string?> values)
        {
            foreach (string name in _ProviderEnvironmentVariables)
            {
                string? value = null;
                if (values.TryGetValue(name, out string? capturedValue))
                {
                    value = capturedValue;
                }

                Environment.SetEnvironmentVariable(name, value);
            }
        }

        private static OpenAiClient CreateClient(LocalOpenAiTestServer server)
        {
            OpenAiClient client = new OpenAiClient(server.Endpoint, "test-key");
            client.Model = "test-model";
            client.TimeoutMs = 1000;
            return client;
        }

        private static ToolChatRequest CreateWeatherToolRequest()
        {
            ToolChatRequest request = new ToolChatRequest();
            request.Messages.Add(ChatMessage.System("Answer with weather guidance."));
            request.Messages.Add(ChatMessage.User("What is the weather in Seattle?"));
            request.Tools.Add(ToolDefinition.Function(
                "get_weather",
                "Get current weather for a city.",
                WeatherParameters()));
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

        private static async Task<List<ModelInformation>> GetModelsAsync(CompletionClientBase client, CancellationToken token)
        {
            List<ModelInformation> models = new List<ModelInformation>();

            await foreach (ModelInformation model in client.ListModelsAsync(token).ConfigureAwait(false))
            {
                models.Add(model);
            }

            return models;
        }

        private static async Task<List<ToolChatStreamingChunk>> ConsumeToolChatStreamAsync(
            ToolChatStreamingResponse stream,
            CancellationToken token)
        {
            List<ToolChatStreamingChunk> chunks = new List<ToolChatStreamingChunk>();

            await foreach (ToolChatStreamingChunk chunk in stream.Chunks.WithCancellation(token).ConfigureAwait(false))
            {
                chunks.Add(chunk);
            }

            return chunks;
        }

        private static async Task<List<ChatStreamingChunk>> ConsumeChatStreamAsync(
            ChatStreamingResponse stream,
            CancellationToken token)
        {
            List<ChatStreamingChunk> chunks = new List<ChatStreamingChunk>();

            await foreach (ChatStreamingChunk chunk in stream.Chunks.WithCancellation(token).ConfigureAwait(false))
            {
                chunks.Add(chunk);
            }

            return chunks;
        }

        private static async Task<List<GenerationStreamingChunk>> ConsumeGenerationStreamAsync(
            GenerationStreamingResponse stream,
            CancellationToken token)
        {
            List<GenerationStreamingChunk> chunks = new List<GenerationStreamingChunk>();

            await foreach (GenerationStreamingChunk chunk in stream.Chunks.WithCancellation(token).ConfigureAwait(false))
            {
                chunks.Add(chunk);
            }

            return chunks;
        }

        private static string CombineChatText(List<ChatStreamingChunk> chunks)
        {
            StringBuilder builder = new StringBuilder();

            foreach (ChatStreamingChunk chunk in chunks)
            {
                if (!string.IsNullOrEmpty(chunk.Text))
                {
                    builder.Append(chunk.Text);
                }
            }

            return builder.ToString();
        }

        private static string CombineGenerationText(List<GenerationStreamingChunk> chunks)
        {
            StringBuilder builder = new StringBuilder();

            foreach (GenerationStreamingChunk chunk in chunks)
            {
                if (!string.IsNullOrEmpty(chunk.Text))
                {
                    builder.Append(chunk.Text);
                }
            }

            return builder.ToString();
        }

        private static LocalOpenAiChatRequest DeserializeRecordedOpenAiRequest(string requestBody)
        {
            LocalOpenAiChatRequest? request = LocalRequestParser.DeserializeOpenAiChatRequest(requestBody);
            if (request == null)
                throw new TestFailureException("Recorded request body could not be deserialized as LocalOpenAiChatRequest.");

            return request;
        }

        private static LocalGenerateRequest DeserializeRecordedGenerateRequest(string requestBody)
        {
            LocalGenerateRequest? request = LocalRequestParser.DeserializeGenerateRequest(requestBody);
            if (request == null)
                throw new TestFailureException("Recorded request body could not be deserialized as LocalGenerateRequest.");

            return request;
        }

        private static LocalGeminiRequest DeserializeRecordedGeminiRequest(string requestBody)
        {
            LocalGeminiRequest? request = LocalRequestParser.DeserializeGeminiRequest(requestBody);
            if (request == null)
                throw new TestFailureException("Recorded request body could not be deserialized as LocalGeminiRequest.");

            return request;
        }

    }
}
