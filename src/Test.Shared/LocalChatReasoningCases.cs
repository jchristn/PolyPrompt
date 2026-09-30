namespace Test.Shared
{
    using System.Text.Json;
    using PolyPrompt.Auth;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using Touchstone.Core;

    /// <summary>
    /// Deterministic local cases for <see cref="CompletionOptions.ReasoningEffort"/> on plain (non-tool) chat:
    /// each provider sends its reasoning field only when the option is set, including turning thinking off at
    /// <see cref="ReasoningEffortLevel.Minimal"/>. Registered into the local behavior suite.
    /// </summary>
    internal static class LocalChatReasoningCases
    {
        public static IEnumerable<TestCaseDescriptor> Create(string suiteId)
        {
            return new List<TestCaseDescriptor>
            {
                Case(suiteId, "chat_reasoning_absent_by_default", "Plain chat sends no reasoning field unless set; the client default applies and a per-call value overrides it", RunAbsentByDefaultAsync),
                Case(suiteId, "chat_reasoning_ollama", "Plain chat reasoning effort maps to Ollama think (off at Minimal), chat and streaming", RunOllamaAsync),
                Case(suiteId, "chat_reasoning_openai", "Plain chat reasoning effort maps to OpenAI reasoning_effort, chat and streaming", RunOpenAiAsync),
                Case(suiteId, "chat_reasoning_gemini", "Plain chat reasoning effort maps to Gemini thinkingConfig beside the sampling settings", RunGeminiAsync),
                Case(suiteId, "chat_reasoning_anthropic", "Plain chat reasoning effort maps to Anthropic effort and thinking", RunAnthropicAsync),
                Case(suiteId, "chat_reasoning_cohere", "Plain chat reasoning effort maps to Cohere thinking (disabled at Minimal)", RunCohereAsync),
                Case(suiteId, "chat_reasoning_bedrock", "Plain chat reasoning effort maps to a Bedrock thinking budget", RunBedrockAsync)
            };
        }

        private static TestCaseDescriptor Case(string suiteId, string caseId, string displayName, Func<CancellationToken, Task> executeAsync)
        {
            return new TestCaseDescriptor(suiteId, caseId, displayName, executeAsync, new[] { "local" });
        }

        private static async Task RunAbsentByDefaultAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();

            using (OpenAiCompletionClient openAi = new OpenAiCompletionClient(server.Endpoint, "test-key") { Model = "test-model", TimeoutMs = 1000 })
            {
                SharedAssert.True((await openAi.ChatAsync("hello", token: token).ConfigureAwait(false)).Success, "OpenAI chat should succeed.");
                openAi.Defaults.ReasoningEffort = ReasoningEffort.High;
                SharedAssert.True((await openAi.ChatAsync("hello", new CompletionOptions { Temperature = 0 }, token).ConfigureAwait(false)).Success, "OpenAI chat should succeed.");
                SharedAssert.True((await openAi.ChatAsync("hello", new CompletionOptions { ReasoningEffort = ReasoningEffort.Low }, token).ConfigureAwait(false)).Success, "OpenAI chat should succeed.");
            }

            using (OllamaCompletionClient ollama = new OllamaCompletionClient(server.Endpoint, "test-key") { Model = "test-model", TimeoutMs = 1000 })
            {
                await ollama.ChatAsync("hello", token: token).ConfigureAwait(false);
            }

            using (GeminiCompletionClient gemini = new GeminiCompletionClient(server.Endpoint, "test-key") { Model = "test-model", TimeoutMs = 1000 })
            {
                await gemini.ChatAsync("hello", token: token).ConfigureAwait(false);
            }

            using (AnthropicCompletionClient anthropic = new AnthropicCompletionClient(server.Endpoint, "test-key") { Model = "test-model", TimeoutMs = 1000 })
            {
                await anthropic.ChatAsync("hello", token: token).ConfigureAwait(false);
            }

            List<string> bodies = server.RequestBodies;
            SharedAssert.Equal(6, bodies.Count, "Each call should have sent one request.");
            SharedAssert.False(Has(bodies[0], "reasoning_effort"), "OpenAI chat with no default and no per-call value should not send reasoning_effort.");
            SharedAssert.Equal("high", Get(bodies[1], "reasoning_effort").GetString(), "The client default should apply to plain chat when the per-call options leave it unset.");
            SharedAssert.Equal("low", Get(bodies[2], "reasoning_effort").GetString(), "A per-call reasoning effort should override the client default.");
            SharedAssert.False(Has(bodies[3], "think"), "Ollama chat without the option should not send think.");
            SharedAssert.False(Has(bodies[4], "generationConfig", "thinkingConfig"), "Gemini chat without the option should not send thinkingConfig.");
            SharedAssert.False(Has(bodies[5], "output_config") || Has(bodies[5], "thinking"), "Anthropic chat without the option should not send effort or thinking.");
        }

        private static async Task RunOllamaAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OllamaCompletionClient client = new OllamaCompletionClient(server.Endpoint, "test-key") { Model = "test-model", TimeoutMs = 1000 };

            ChatResponse off = await client.ChatAsync("hello", new CompletionOptions { ReasoningEffort = ReasoningEffort.Minimal }, token).ConfigureAwait(false);
            SharedAssert.True(off.Success, "Ollama chat with thinking off should succeed.");

            ChatStreamingResponse stream = await client.ChatStreamingAsync("normal stream", new CompletionOptions { ReasoningEffort = ReasoningEffortLevel.Medium }, token).ConfigureAwait(false);
            await ConsumeAsync(stream, token).ConfigureAwait(false);
            SharedAssert.True(stream.Success, "Ollama streaming chat with reasoning effort should succeed.");

            List<string> bodies = server.RequestBodies;
            SharedAssert.True(Get(bodies[0], "think").ValueKind == JsonValueKind.False, "Minimal should send think:false.");
            SharedAssert.Equal("medium", Get(bodies[1], "think").GetString(), "Medium should send think as the level string when streaming.");
            SharedAssert.True(Get(bodies[1], "stream").ValueKind == JsonValueKind.True, "The streaming request should still stream.");
        }

        private static async Task RunOpenAiAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiCompletionClient client = new OpenAiCompletionClient(server.Endpoint, "test-key") { Model = "test-model", TimeoutMs = 1000 };

            ChatResponse high = await client.ChatAsync("hello", new CompletionOptions { ReasoningEffort = ReasoningEffort.High }, token).ConfigureAwait(false);
            SharedAssert.True(high.Success, "OpenAI chat with reasoning effort should succeed.");

            ChatStreamingResponse stream = await client.ChatStreamingAsync("normal stream", new CompletionOptions { ReasoningEffort = ReasoningEffort.Low }, token).ConfigureAwait(false);
            await ConsumeAsync(stream, token).ConfigureAwait(false);
            SharedAssert.True(stream.Success, "OpenAI streaming chat with reasoning effort should succeed.");

            List<string> bodies = server.RequestBodies;
            SharedAssert.Equal("high", Get(bodies[0], "reasoning_effort").GetString(), "High should send reasoning_effort high.");
            SharedAssert.Equal("low", Get(bodies[1], "reasoning_effort").GetString(), "Low should send reasoning_effort low when streaming.");
        }

        private static async Task RunGeminiAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using GeminiCompletionClient client = new GeminiCompletionClient(server.Endpoint, "test-key") { Model = "test-model", TimeoutMs = 1000 };

            ChatResponse off = await client.ChatAsync("hello", new CompletionOptions { Temperature = 0.3, ReasoningEffort = ReasoningEffort.Minimal }, token).ConfigureAwait(false);
            SharedAssert.True(off.Success, "Gemini chat with thinking off should succeed.");

            ChatStreamingResponse stream = await client.ChatStreamingAsync("normal stream", new CompletionOptions { ReasoningEffort = ReasoningEffort.High }, token).ConfigureAwait(false);
            await ConsumeAsync(stream, token).ConfigureAwait(false);

            List<string> bodies = server.RequestBodies;
            SharedAssert.Equal(0, Get(bodies[0], "generationConfig", "thinkingConfig", "thinkingBudget").GetInt32(), "Minimal should send a thinking budget of 0.");
            SharedAssert.Equal(0.3, Get(bodies[0], "generationConfig", "temperature").GetDouble(), "The sampling settings should stay beside thinkingConfig.");
            SharedAssert.Equal(-1, Get(bodies[1], "generationConfig", "thinkingConfig", "thinkingBudget").GetInt32(), "High should send the dynamic budget when streaming.");
        }

        private static async Task RunAnthropicAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using AnthropicCompletionClient client = new AnthropicCompletionClient(server.Endpoint, "test-key") { Model = "test-model", TimeoutMs = 1000 };

            await client.ChatAsync("hello", new CompletionOptions { ReasoningEffort = ReasoningEffort.Minimal }, token).ConfigureAwait(false);
            await client.ChatAsync("hello", new CompletionOptions { ReasoningEffort = ReasoningEffort.High }, token).ConfigureAwait(false);

            List<string> bodies = server.RequestBodies;
            SharedAssert.Equal("low", Get(bodies[0], "output_config", "effort").GetString(), "Minimal should send effort low.");
            SharedAssert.False(Has(bodies[0], "thinking"), "Minimal should omit the thinking field.");
            SharedAssert.Equal("high", Get(bodies[1], "output_config", "effort").GetString(), "High should send effort high.");
            SharedAssert.Equal("adaptive", Get(bodies[1], "thinking", "type").GetString(), "High should send adaptive thinking.");
        }

        private static async Task RunCohereAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using CohereCompletionClient client = new CohereCompletionClient(server.Endpoint, LocalExtendedRoutes.CohereTestKey) { Model = "cohere-test-model", TimeoutMs = 2000 };

            await client.ChatAsync("hello", new CompletionOptions { ReasoningEffort = ReasoningEffort.Minimal }, token).ConfigureAwait(false);
            await client.ChatAsync("hello", new CompletionOptions { ReasoningEffort = ReasoningEffort.Medium }, token).ConfigureAwait(false);

            List<string> bodies = server.RequestBodies;
            SharedAssert.Equal("disabled", Get(bodies[0], "thinking", "type").GetString(), "Minimal should disable Cohere thinking.");
            SharedAssert.Equal("enabled", Get(bodies[1], "thinking", "type").GetString(), "Medium should enable Cohere thinking.");
            SharedAssert.Equal(4096, Get(bodies[1], "thinking", "token_budget").GetInt32(), "Medium should send a 4096-token budget.");
        }

        private static async Task RunBedrockAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using BedrockCompletionClient client = new BedrockCompletionClient(
                new StaticAwsCredential("AKIDTESTEXAMPLE", "wJalrXUtnFEMI/K7MDENG+bPxRfiCYEXAMPLEKEY", "us-east-1"),
                "us-east-1",
                endpoint: server.Endpoint);
            client.TimeoutMs = 1000;

            ChatResponse plain = await client.ChatAsync("hello", token: token).ConfigureAwait(false);
            SharedAssert.True(plain.Success, "Bedrock chat should succeed.");
            ChatResponse reasoned = await client.ChatAsync("hello", new CompletionOptions { ReasoningEffort = ReasoningEffort.Medium }, token).ConfigureAwait(false);
            SharedAssert.True(reasoned.Success, "Bedrock chat with reasoning effort should succeed.");

            List<string> bodies = server.RequestBodies;
            SharedAssert.False(bodies[0].Contains("budget_tokens", StringComparison.Ordinal), "Bedrock chat without the option should not send a thinking budget.");
            SharedAssert.True(bodies[1].Contains("budget_tokens", StringComparison.Ordinal), "Bedrock chat with reasoning effort should send a thinking budget.");
        }

        private static async Task ConsumeAsync(ChatStreamingResponse stream, CancellationToken token)
        {
            if (stream.Chunks == null) return;
            await foreach (ChatStreamingChunk chunk in stream.Chunks.WithCancellation(token).ConfigureAwait(false))
            {
            }
        }

        private static bool Has(string body, params string[] path)
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement current = document.RootElement;
            foreach (string name in path)
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out current)) return false;
            }

            return true;
        }

        private static JsonElement Get(string body, params string[] path)
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement current = document.RootElement;
            foreach (string name in path)
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out current))
                    throw new TestFailureException("The request body has no '" + string.Join(".", path) + "': " + body);
            }

            return current.Clone();
        }
    }
}
