namespace Test.Shared
{
    using System.Diagnostics;
    using System.Text;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using Touchstone.Core;

    public static class LocalBehaviorSuite
    {
        private const string SuiteId = "local_behavior";

        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Local behavior",
                new List<TestCaseDescriptor>
                {
                    Case("chat_and_call_details", "Chat and CallDetails behavior", RunChatAndCallDetailsAsync),
                    Case("client_options_and_guards", "Client options and guard clauses", RunClientOptionsAndGuardsAsync),
                    Case("provider_specific_options_clamping", "Provider-specific options clamp values", RunProviderSpecificOptionsClampingAsync),
                    Case("provider_chat_request_translation", "Provider chat request translation", RunProviderChatRequestTranslationAsync),
                    Case("tool_chat_models_and_validation", "Tool chat models and validation", RunToolChatModelsAndValidationAsync),
                    Case("openai_tool_chat", "OpenAI-compatible tool chat flow", RunOpenAiToolChatAsync),
                    Case("ollama_tool_chat", "Ollama tool chat flow", RunOllamaToolChatAsync),
                    Case("gemini_tool_chat", "Gemini tool chat flow", RunGeminiToolChatAsync),
                    Case("openai_embedding_generation_models", "OpenAI-compatible embeddings, generation, and models", RunOpenAiEmbeddingGenerationModelsAsync),
                    Case("ollama_embedding_generation_models", "Ollama embeddings, generation, and models", RunOllamaEmbeddingGenerationModelsAsync),
                    Case("gemini_embedding_generation_models", "Gemini embeddings, generation, and models", RunGeminiEmbeddingGenerationModelsAsync),
                    Case("unsupported_provider_model_management", "Unsupported provider model management throws", RunUnsupportedProviderModelManagementAsync),
                    Case("http_error_handling", "HTTP error responses are surfaced", RunHttpErrorHandlingAsync),
                    Case("timeout_validation", "TimeoutMs validation preserves positive values", RunTimeoutValidationAsync),
                    Case("validate_connectivity_cancellation", "ValidateConnectivityAsync propagates cancellation", RunValidateConnectivityCancellationAsync),
                    Case("streaming_body_timeout", "Streaming timeout covers the response body", RunStreamingBodyTimeoutAsync),
                    Case("post_and_record_disposes_response", "PostAndRecordAsync disposes non-streaming responses", RunPostAndRecordDisposesResponseAsync),
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

            ChatStreamingResponse streaming = await client.ChatStreamingAsync("stream", token: token).ConfigureAwait(false);
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

        private static LocalOpenAiChatRequest DeserializeRecordedOpenAiRequest(string requestBody)
        {
            LocalOpenAiChatRequest? request = LocalRequestParser.DeserializeOpenAiChatRequest(requestBody);
            if (request == null)
                throw new TestFailureException("Recorded request body could not be deserialized as LocalOpenAiChatRequest.");

            return request;
        }

        private static LocalGeminiRequest DeserializeRecordedGeminiRequest(string requestBody)
        {
            LocalGeminiRequest? request = LocalRequestParser.DeserializeGeminiRequest(requestBody);
            if (request == null)
                throw new TestFailureException("Recorded request body could not be deserialized as LocalGeminiRequest.");

            return request;
        }

        private sealed class ProbeOpenAiClient : OpenAiClient
        {
            public ProbeOpenAiClient(string endpoint, string apiKey) : base(endpoint, apiKey)
            {
                Model = "test-model";
            }

            public async Task<CompletionHttpResult> PostProbeAsync(CancellationToken token)
            {
                string url = Endpoint.TrimEnd('/') + "/v1/chat/completions";
                string json = "{\"model\":\"test-model\",\"messages\":[{\"role\":\"user\",\"content\":\"probe\"}],\"max_tokens\":1}";
                using StringContent content = new StringContent(json, Encoding.UTF8, "application/json");
                return await PostAndRecordAsync(url, content, json, token).ConfigureAwait(false);
            }
        }
    }
}
