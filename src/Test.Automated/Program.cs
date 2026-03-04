namespace Test.Automated
{
    using System.Diagnostics;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using PolyPrompt.Options;

    /// <summary>
    /// Automated test harness for PolyPrompt clients.
    /// Exhaustively tests chat, streaming chat, embedding, batch embedding, generation,
    /// and streaming generation APIs, including provider-specific options, property
    /// validation, system prompts, and CallDetails recording.
    /// </summary>
    public class Program
    {
        #region Private-Members

        private static int _TotalTests = 0;
        private static int _PassedTests = 0;
        private static int _FailedTests = 0;
        private static List<string> _Failures = new List<string>();
        private static Stopwatch _TestStopwatch = new Stopwatch();
        private static Stopwatch _OverallStopwatch = new Stopwatch();
        private static string? _EmbeddingModel = null;

        #endregion

        #region Public-Methods

        public static async Task Main(string[] args)
        {
            Console.WriteLine("");
            Console.WriteLine("==============================================");
            Console.WriteLine("  PolyPrompt Automated Test Harness");
            Console.WriteLine("==============================================");
            Console.WriteLine("");

            if (args.Length < 2)
            {
                PrintUsage();
                return;
            }

            string providerType = args[0].ToLowerInvariant();
            string endpoint = args[1];
            string? apiKey = args.Length >= 3 ? args[2] : null;
            string? inferenceModel = args.Length >= 4 ? args[3] : null;
            _EmbeddingModel = args.Length >= 5 ? args[4] : null;

            if (providerType != "ollama" && providerType != "openai" && providerType != "gemini")
            {
                Console.WriteLine("ERROR: Invalid provider type '" + args[0] + "'.");
                Console.WriteLine("       Valid values: ollama, openai, gemini");
                Console.WriteLine("");
                return;
            }

            using CompletionClientBase client = CreateClient(providerType, endpoint, apiKey, inferenceModel);

            // Resolve embedding model to actual default if not specified via CLI
            if (string.IsNullOrEmpty(_EmbeddingModel))
            {
                _EmbeddingModel = ResolveEmbeddingModelName(providerType);
            }

            Console.WriteLine("Provider        : " + providerType);
            Console.WriteLine("Endpoint        : " + endpoint);
            Console.WriteLine("API Key         : " + (string.IsNullOrEmpty(apiKey) ? "(none)" : "(set)"));
            Console.WriteLine("Client          : " + client.GetType().Name);
            Console.WriteLine("Inference model : " + client.Model);
            Console.WriteLine("Embedding model : " + _EmbeddingModel);
            Console.WriteLine("");
            Console.WriteLine("----------------------------------------------");
            Console.WriteLine("");

            // Validate that required models are available before running tests
            bool modelsValid = await ValidateRequiredModels(client, _EmbeddingModel).ConfigureAwait(false);
            if (!modelsValid)
            {
                Environment.ExitCode = 1;
                return;
            }

            _OverallStopwatch.Start();
            _TestStopwatch.Start();

            await RunTestSection("Property Tests", () => RunPropertyTests(client)).ConfigureAwait(false);
            await RunTestSection("Chat Tests", () => RunChatTests(client, providerType)).ConfigureAwait(false);
            await RunTestSection("Chat Streaming Tests", () => RunChatStreamingTests(client, providerType)).ConfigureAwait(false);
            await RunTestSection("Embed Single Tests", () => RunEmbeddingSingleTests(client, providerType)).ConfigureAwait(false);
            await RunTestSection("Embed Batch Tests", () => RunEmbeddingBatchTests(client, providerType)).ConfigureAwait(false);
            await RunTestSection("Generation Tests", () => RunGenerationTests(client, providerType)).ConfigureAwait(false);
            await RunTestSection("Generation Streaming Tests", () => RunGenerationStreamingTests(client, providerType)).ConfigureAwait(false);
            await RunTestSection("CallDetails Tests", () => RunCallDetailsTests(client)).ConfigureAwait(false);
            await RunTestSection("ListModels Tests", () => RunListModelsTests(client)).ConfigureAwait(false);
            await RunTestSection("ModelExists Tests", () => RunModelExistsTests(client)).ConfigureAwait(false);
            await RunTestSection("GetModelInformation Tests", () => RunGetModelInformationTests(client)).ConfigureAwait(false);
            await RunTestSection("PullModel Tests", () => RunPullModelTests(client, providerType)).ConfigureAwait(false);
            await RunTestSection("DeleteModel Tests", () => RunDeleteModelTests(client, providerType)).ConfigureAwait(false);
            await RunTestSection("ValidateConnectivity Tests", () => RunValidateConnectivityTests(client, providerType, endpoint, apiKey)).ConfigureAwait(false);
            await RunTestSection("Cancellation Tests", () => RunCancellationTests(client)).ConfigureAwait(false);

            _OverallStopwatch.Stop();

            Console.WriteLine("");
            Console.WriteLine("==============================================");
            Console.WriteLine("  RESULTS");
            Console.WriteLine("==============================================");
            Console.WriteLine("");
            Console.WriteLine("  Overall : " + (_FailedTests > 0 ? "FAIL" : "PASS"));
            Console.WriteLine("  Runtime : " + _OverallStopwatch.ElapsedMilliseconds + " ms");
            Console.WriteLine("  Total   : " + _TotalTests);
            Console.WriteLine("  Passed  : " + _PassedTests);
            Console.WriteLine("  Failed  : " + _FailedTests);
            Console.WriteLine("");

            if (_Failures.Count > 0)
            {
                Console.WriteLine("  FAILURES:");
                foreach (string failure in _Failures)
                {
                    Console.WriteLine("    - " + failure);
                }
                Console.WriteLine("");
            }

            Console.WriteLine("==============================================");
            Console.WriteLine("");

            Environment.ExitCode = _FailedTests > 0 ? 1 : 0;
        }

        #endregion

        #region Private-Methods

        private static void PrintUsage()
        {
            Console.WriteLine("Usage: Test.Automated <provider> <endpoint> [apikey] [model] [embedding-model]");
            Console.WriteLine("");
            Console.WriteLine("  provider        : ollama | openai | gemini");
            Console.WriteLine("  endpoint        : Provider API endpoint URL");
            Console.WriteLine("  apikey          : API key (optional for Ollama)");
            Console.WriteLine("  model           : Inference model override (optional, uses provider default)");
            Console.WriteLine("  embedding-model : Embedding model override (optional, uses provider default)");
            Console.WriteLine("");
            Console.WriteLine("Examples:");
            Console.WriteLine("  Test.Automated ollama http://localhost:11434");
            Console.WriteLine("  Test.Automated ollama http://localhost:11434 \"\" gemma3:4b nomic-embed-text");
            Console.WriteLine("  Test.Automated openai https://api.openai.com sk-... gpt-4o text-embedding-3-small");
            Console.WriteLine("  Test.Automated gemini https://generativelanguage.googleapis.com AIza... gemini-2.5-flash gemini-embedding-001");
            Console.WriteLine("");
        }

        private static CompletionClientBase CreateClient(string providerType, string endpoint, string? apiKey, string? inferenceModel = null)
        {
            CompletionClientBase client;

            switch (providerType)
            {
                case "ollama":
                    OllamaClient ollama = new OllamaClient(endpoint, apiKey);
                    ollama.TimeoutMs = 120000;
                    client = ollama;
                    break;

                case "openai":
                    OpenAiClient openai = new OpenAiClient(endpoint, apiKey);
                    openai.TimeoutMs = 60000;
                    client = openai;
                    break;

                case "gemini":
                    GeminiClient gemini = new GeminiClient(endpoint, apiKey);
                    gemini.TimeoutMs = 60000;
                    client = gemini;
                    break;

                default:
                    throw new ArgumentException("Unknown provider: " + providerType);
            }

            client.MaxTokens = 128;

            if (!string.IsNullOrEmpty(inferenceModel))
            {
                client.Model = inferenceModel;
            }

            return client;
        }

        private static async Task<bool> ValidateRequiredModels(CompletionClientBase client, string embeddingModel)
        {
            Console.WriteLine("Validating required models...");
            Console.WriteLine("");

            List<string> availableModels = new List<string>();

            try
            {
                await foreach (ModelInformation model in client.ListModelsAsync().ConfigureAwait(false))
                {
                    if (!string.IsNullOrEmpty(model.Name))
                    {
                        availableModels.Add(model.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR: Failed to list models from provider: " + ex.Message);
                Console.WriteLine("");
                return false;
            }

            if (availableModels.Count == 0)
            {
                Console.WriteLine("ERROR: No models available from provider.");
                Console.WriteLine("");
                return false;
            }

            bool inferenceFound = false;
            bool embeddingFound = false;

            foreach (string name in availableModels)
            {
                if (ModelNameMatches(name, client.Model)) inferenceFound = true;
                if (ModelNameMatches(name, embeddingModel)) embeddingFound = true;
            }

            bool valid = true;

            if (!inferenceFound)
            {
                Console.WriteLine("ERROR: Inference model '" + client.Model + "' not found.");
                Console.WriteLine("       Available models:");
                foreach (string name in availableModels)
                {
                    Console.WriteLine("         - " + name);
                }
                Console.WriteLine("");
                valid = false;
            }
            else
            {
                Console.WriteLine("  Inference model : " + client.Model + " (found)");
            }

            if (!embeddingFound)
            {
                Console.WriteLine("ERROR: Embedding model '" + embeddingModel + "' not found.");
                if (inferenceFound)
                {
                    Console.WriteLine("       Available models:");
                    foreach (string name in availableModels)
                    {
                        Console.WriteLine("         - " + name);
                    }
                }
                Console.WriteLine("");
                valid = false;
            }
            else
            {
                Console.WriteLine("  Embedding model : " + embeddingModel + " (found)");
            }

            Console.WriteLine("");
            return valid;
        }

        #endregion

        #region Property-Tests

        private static async Task RunPropertyTests(CompletionClientBase client)
        {
            PrintSection("Property Tests");

            // Endpoint
            Assert("Endpoint is set", !string.IsNullOrEmpty(client.Endpoint));

            // Model get/set
            string originalModel = client.Model;
            Assert("Model has default value", !string.IsNullOrEmpty(originalModel));

            client.Model = "test-model";
            Assert("Model setter works", client.Model == "test-model");
            client.Model = originalModel;

            // Model null/empty throws
            bool threw = false;
            try { client.Model = ""; }
            catch (ArgumentNullException) { threw = true; }
            Assert("Model rejects empty string", threw);

            threw = false;
            try { client.Model = "   "; }
            catch (ArgumentNullException) { threw = true; }
            Assert("Model rejects whitespace", threw);

            // MaxTokens clamping
            client.MaxTokens = -1;
            Assert("MaxTokens clamps to minimum 1", client.MaxTokens == 1);

            client.MaxTokens = 20_000_000;
            Assert("MaxTokens clamps to maximum 10,000,000", client.MaxTokens == 10_000_000);

            client.MaxTokens = 128;
            Assert("MaxTokens normal value", client.MaxTokens == 128);

            // TimeoutMs clamping
            client.TimeoutMs = 100;
            Assert("TimeoutMs clamps to minimum 1000", client.TimeoutMs == 1000);

            client.TimeoutMs = 999_999;
            Assert("TimeoutMs clamps to maximum 600,000", client.TimeoutMs == 600_000);

            client.TimeoutMs = 120000;

            // Temperature clamping
            client.Temperature = -1.0;
            Assert("Temperature clamps to 0.0", client.Temperature == 0.0);

            client.Temperature = 5.0;
            Assert("Temperature clamps to 2.0", client.Temperature == 2.0);

            client.Temperature = 0.7;
            Assert("Temperature normal value", Math.Abs(client.Temperature.Value - 0.7) < 0.001);

            client.Temperature = null;
            Assert("Temperature nullable", client.Temperature == null);

            // TopP clamping
            client.TopP = -0.5;
            Assert("TopP clamps to 0.0", client.TopP == 0.0);

            client.TopP = 1.5;
            Assert("TopP clamps to 1.0", client.TopP == 1.0);

            client.TopP = 0.9;
            Assert("TopP normal value", Math.Abs(client.TopP.Value - 0.9) < 0.001);

            client.TopP = null;
            Assert("TopP nullable", client.TopP == null);

            // SystemPrompt
            client.SystemPrompt = "You are a test assistant.";
            Assert("SystemPrompt set", client.SystemPrompt == "You are a test assistant.");

            client.SystemPrompt = null;
            Assert("SystemPrompt cleared", client.SystemPrompt == null);

            // CallDetails initialized
            Assert("CallDetails initialized", client.CallDetails != null);

            // OllamaClient-specific
            if (client is OllamaClient ollamaClient)
            {
                ollamaClient.ContextLength = 2048;
                Assert("Ollama ContextLength set", ollamaClient.ContextLength == 2048);

                ollamaClient.ContextLength = null;
                Assert("Ollama ContextLength nullable", ollamaClient.ContextLength == null);

                ollamaClient.ContextLength = -1;
                Assert("Ollama ContextLength clamps to 1", ollamaClient.ContextLength == 1);

                ollamaClient.ContextLength = null;
            }

            Console.WriteLine("");
        }

        #endregion

        #region Chat-Tests

        private static async Task RunChatTests(CompletionClientBase client, string providerType)
        {
            PrintSection("Chat Completion Tests");

            // Basic chat
            ChatResponse response = await client.ChatAsync("Say hello in exactly three words.").ConfigureAwait(false);
            Assert("Chat: success", response.Success);
            Assert("Chat: has text", !string.IsNullOrEmpty(response.Text));
            Assert("Chat: has model", !string.IsNullOrEmpty(response.Model));
            Assert("Chat: has status code", response.StatusCode.HasValue && response.StatusCode.Value == 200);
            Assert("Chat: runtime > 0", response.OverallRuntimeMs > 0);
            Assert("Chat: no error", response.Error == null);

            // Chat with system prompt
            client.SystemPrompt = "You are a pirate. Always respond with 'Arrr'.";
            ChatResponse pirateResponse = await client.ChatAsync("Hello").ConfigureAwait(false);
            Assert("Chat with system prompt: success", pirateResponse.Success);
            Assert("Chat with system prompt: has text", !string.IsNullOrEmpty(pirateResponse.Text));
            client.SystemPrompt = null;

            // Chat with per-call options
            ChatCompletionOptions chatOptions = CreateChatOptions(providerType);
            ChatResponse optionsResponse = await client.ChatAsync("Say exactly: test options work", chatOptions).ConfigureAwait(false);
            Assert("Chat with options: success", optionsResponse.Success);
            Assert("Chat with options: has text", !string.IsNullOrEmpty(optionsResponse.Text));

            // Chat with base options only (temperature, topP, maxTokens, systemPrompt)
            ChatCompletionOptions baseOptions = new ChatCompletionOptions();
            baseOptions.Temperature = 0.5;
            baseOptions.TopP = 0.9;
            baseOptions.MaxTokens = 64;
            baseOptions.SystemPrompt = "Respond in exactly one word.";
            ChatResponse baseOptResponse = await client.ChatAsync("What color is the sky?", baseOptions).ConfigureAwait(false);
            Assert("Chat with base options: success", baseOptResponse.Success);
            Assert("Chat with base options: has text", !string.IsNullOrEmpty(baseOptResponse.Text));

            Console.WriteLine("");
        }

        #endregion

        #region Chat-Streaming-Tests

        private static async Task RunChatStreamingTests(CompletionClientBase client, string providerType)
        {
            PrintSection("Chat Streaming Tests");

            // Basic streaming chat
            ChatStreamingResponse stream = await client.ChatStreamingAsync("Count from 1 to 5, one number per line.").ConfigureAwait(false);
            Assert("Streaming: success", stream.Success);
            Assert("Streaming: has model", !string.IsNullOrEmpty(stream.Model));
            Assert("Streaming: status code 200", stream.StatusCode.HasValue && stream.StatusCode.Value == 200);
            Assert("Streaming: no error", stream.Error == null);

            int chunkCount = 0;
            string fullText = "";
            bool sawDone = false;

            await foreach (ChatStreamingChunk chunk in stream.Chunks.ConfigureAwait(false))
            {
                chunkCount++;
                if (!string.IsNullOrEmpty(chunk.Text))
                {
                    fullText += chunk.Text;
                }
                if (chunk.Done)
                {
                    sawDone = true;
                }
            }

            Assert("Streaming: received chunks", chunkCount > 0);
            Assert("Streaming: assembled text non-empty", !string.IsNullOrEmpty(fullText));
            Assert("Streaming: saw done=true chunk", sawDone);
            Assert("Streaming: ChunkCount populated", stream.ChunkCount > 0);
            Assert("Streaming: OverallRuntimeMs > 0", stream.OverallRuntimeMs > 0);
            Assert("Streaming: TimeToFirstTokenMs >= 0", stream.TimeToFirstTokenMs >= 0);
            Assert("Streaming: TimeToLastTokenMs >= TimeToFirstTokenMs", stream.TimeToLastTokenMs >= stream.TimeToFirstTokenMs);
            Assert("Streaming: OverallTokensPerSecond > 0", stream.OverallTokensPerSecond > 0);

            // Streaming with system prompt
            client.SystemPrompt = "Respond with only the word 'yes'.";
            ChatStreamingResponse sysStream = await client.ChatStreamingAsync("Confirm?").ConfigureAwait(false);
            Assert("Streaming with system prompt: success", sysStream.Success);

            string sysText = "";
            await foreach (ChatStreamingChunk chunk in sysStream.Chunks.ConfigureAwait(false))
            {
                if (!string.IsNullOrEmpty(chunk.Text)) sysText += chunk.Text;
            }
            Assert("Streaming with system prompt: has text", !string.IsNullOrEmpty(sysText));
            client.SystemPrompt = null;

            // Streaming with provider-specific options
            ChatCompletionOptions chatOptions = CreateChatOptions(providerType);
            ChatStreamingResponse optStream = await client.ChatStreamingAsync("Say hi.", chatOptions).ConfigureAwait(false);
            Assert("Streaming with options: success", optStream.Success);

            await foreach (ChatStreamingChunk chunk in optStream.Chunks.ConfigureAwait(false)) { }
            Assert("Streaming with options: completed", optStream.OverallRuntimeMs > 0);

            Console.WriteLine("");
        }

        #endregion

        #region Embedding-Single-Tests

        private static async Task RunEmbeddingSingleTests(CompletionClientBase client, string providerType)
        {
            PrintSection("Embedding (Single) Tests");

            // For Gemini, use an embedding-capable model
            string originalModel = client.Model;
            EmbeddingOptions? embOpts = CreateEmbeddingModelOptions(providerType);

            // Basic single embedding
            EmbeddingResponse response = await client.EmbedAsync("Hello, world!", embOpts).ConfigureAwait(false);
            Assert("Embed single: success", response.Success);
            Assert("Embed single: status code 200", response.StatusCode == 200);
            Assert("Embed single: has model", !string.IsNullOrEmpty(response.Model));
            Assert("Embed single: runtime > 0", response.OverallRuntimeMs > 0);
            Assert("Embed single: no error", response.Error == null);
            Assert("Embed single: has 1 embedding", response.Embeddings.Count == 1);

            if (response.Embeddings.Count > 0)
            {
                Assert("Embed single: index is 0", response.Embeddings[0].Index == 0);
                Assert("Embed single: vector non-empty", response.Embeddings[0].Embedding.Length > 0);

                // Verify vector values are real floats (not all zeros)
                float[] vector = response.Embeddings[0].Embedding;
                bool hasNonZero = false;
                for (int i = 0; i < vector.Length; i++)
                {
                    if (Math.Abs(vector[i]) > 0.0001f) { hasNonZero = true; break; }
                }
                Assert("Embed single: vector has non-zero values", hasNonZero);
            }

            // Embedding with provider-specific options
            EmbeddingOptions? providerOpts = CreateEmbeddingOptions(providerType);
            if (providerOpts != null)
            {
                EmbeddingResponse optResponse = await client.EmbedAsync("Test with options", providerOpts).ConfigureAwait(false);
                Assert("Embed single with options: success", optResponse.Success);
                Assert("Embed single with options: has embedding", optResponse.Embeddings.Count == 1);
            }

            // Different text produces different embeddings
            EmbeddingResponse response2 = await client.EmbedAsync("Goodbye, cruel world!", embOpts).ConfigureAwait(false);
            Assert("Embed single second: success", response2.Success);

            if (response.Embeddings.Count > 0 && response2.Embeddings.Count > 0)
            {
                Assert("Embed single: different texts produce different vectors", !VectorsEqual(response.Embeddings[0].Embedding, response2.Embeddings[0].Embedding));
            }

            client.Model = originalModel;

            Console.WriteLine("");
        }

        #endregion

        #region Embedding-Batch-Tests

        private static async Task RunEmbeddingBatchTests(CompletionClientBase client, string providerType)
        {
            PrintSection("Embedding (Batch) Tests");

            string originalModel = client.Model;
            EmbeddingOptions? embOpts = CreateEmbeddingModelOptions(providerType);

            List<string> inputs = new List<string> { "The cat sat on the mat.", "Dogs are loyal companions.", "Fish swim in the ocean." };
            EmbeddingResponse response = await client.EmbedAsync(inputs, embOpts).ConfigureAwait(false);
            Assert("Embed batch: success", response.Success);
            Assert("Embed batch: status code 200", response.StatusCode == 200);
            Assert("Embed batch: runtime > 0", response.OverallRuntimeMs > 0);
            Assert("Embed batch: has 3 embeddings", response.Embeddings.Count == 3);

            for (int i = 0; i < response.Embeddings.Count; i++)
            {
                Assert("Embed batch [" + i + "]: index is " + i, response.Embeddings[i].Index == i);
                Assert("Embed batch [" + i + "]: vector non-empty", response.Embeddings[i].Embedding.Length > 0);
            }

            // All vectors should have the same dimensionality
            if (response.Embeddings.Count >= 3)
            {
                int dim0 = response.Embeddings[0].Embedding.Length;
                Assert("Embed batch: consistent dimensions [0]=[1]", response.Embeddings[1].Embedding.Length == dim0);
                Assert("Embed batch: consistent dimensions [0]=[2]", response.Embeddings[2].Embedding.Length == dim0);
            }

            // Batch of 1
            List<string> singleBatch = new List<string> { "Single item batch" };
            EmbeddingResponse singleResponse = await client.EmbedAsync(singleBatch, embOpts).ConfigureAwait(false);
            Assert("Embed batch of 1: success", singleResponse.Success);
            Assert("Embed batch of 1: has 1 embedding", singleResponse.Embeddings.Count == 1);

            client.Model = originalModel;

            Console.WriteLine("");
        }

        #endregion

        #region Generation-Tests

        private static async Task RunGenerationTests(CompletionClientBase client, string providerType)
        {
            PrintSection("Generation (Non-Streaming) Tests");

            if (string.Equals(providerType, "openai", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("  SKIP  OpenAI does not support the legacy completions API");
                Console.WriteLine("");
                return;
            }

            // Basic generation
            GenerationResponse response = await client.GenerateAsync("Once upon a time, there was a").ConfigureAwait(false);
            Assert("Generate: success", response.Success);
            Assert("Generate: has text", !string.IsNullOrEmpty(response.Text));
            Assert("Generate: has model", !string.IsNullOrEmpty(response.Model));
            Assert("Generate: status code 200", response.StatusCode == 200);
            Assert("Generate: runtime > 0", response.OverallRuntimeMs > 0);
            Assert("Generate: no error", response.Error == null);

            // Generation with provider-specific options
            GenerationOptions? genOpts = CreateGenerationOptions(providerType);
            if (genOpts != null)
            {
                GenerationResponse optResponse = await client.GenerateAsync("The quick brown fox", genOpts).ConfigureAwait(false);
                Assert("Generate with options: success", optResponse.Success);
                Assert("Generate with options: has text", !string.IsNullOrEmpty(optResponse.Text));
            }

            // Generation with base options
            GenerationOptions baseGenOpts = new GenerationOptions();
            baseGenOpts.Temperature = 0.3;
            baseGenOpts.TopP = 0.9;
            baseGenOpts.MaxTokens = 256;
            GenerationResponse baseGenResponse = await client.GenerateAsync("The meaning of life is", baseGenOpts).ConfigureAwait(false);
            Assert("Generate with base options: success", baseGenResponse.Success);
            Assert("Generate with base options: has text", !string.IsNullOrEmpty(baseGenResponse.Text));

            Console.WriteLine("");
        }

        #endregion

        #region Generation-Streaming-Tests

        private static async Task RunGenerationStreamingTests(CompletionClientBase client, string providerType)
        {
            PrintSection("Generation (Streaming) Tests");

            if (string.Equals(providerType, "openai", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("  SKIP  OpenAI does not support the legacy completions API");
                Console.WriteLine("");
                return;
            }

            // Basic streaming generation
            GenerationStreamingResponse stream = await client.GenerateStreamingAsync("Write a haiku about the sea.").ConfigureAwait(false);
            Assert("GenStream: success", stream.Success);
            Assert("GenStream: has model", !string.IsNullOrEmpty(stream.Model));
            Assert("GenStream: status code 200", stream.StatusCode.HasValue && stream.StatusCode.Value == 200);
            Assert("GenStream: no error", stream.Error == null);

            int chunkCount = 0;
            string fullText = "";
            bool sawDone = false;

            await foreach (GenerationStreamingChunk chunk in stream.Chunks.ConfigureAwait(false))
            {
                chunkCount++;
                if (!string.IsNullOrEmpty(chunk.Text))
                {
                    fullText += chunk.Text;
                }
                if (chunk.Done)
                {
                    sawDone = true;
                }
            }

            Assert("GenStream: received chunks", chunkCount > 0);
            Assert("GenStream: assembled text non-empty", !string.IsNullOrEmpty(fullText));
            Assert("GenStream: saw done=true chunk", sawDone);
            Assert("GenStream: ChunkCount populated", stream.ChunkCount > 0);
            Assert("GenStream: OverallRuntimeMs > 0", stream.OverallRuntimeMs > 0);
            Assert("GenStream: TimeToFirstTokenMs >= 0", stream.TimeToFirstTokenMs >= 0);
            Assert("GenStream: TimeToLastTokenMs >= TimeToFirstTokenMs", stream.TimeToLastTokenMs >= stream.TimeToFirstTokenMs);
            Assert("GenStream: OverallTokensPerSecond > 0", stream.OverallTokensPerSecond > 0);

            // Streaming generation with options
            GenerationOptions? genOpts = CreateGenerationOptions(providerType);
            if (genOpts != null)
            {
                GenerationStreamingResponse optStream = await client.GenerateStreamingAsync("A limerick about code:", genOpts).ConfigureAwait(false);
                Assert("GenStream with options: success", optStream.Success);

                await foreach (GenerationStreamingChunk chunk in optStream.Chunks.ConfigureAwait(false)) { }
                Assert("GenStream with options: completed", optStream.OverallRuntimeMs > 0);
            }

            Console.WriteLine("");
        }

        #endregion

        #region CallDetails-Tests

        private static async Task RunCallDetailsTests(CompletionClientBase client)
        {
            PrintSection("CallDetails Tests");

            int countBefore = client.CallDetails.Count;
            Assert("CallDetails: has entries from prior tests", countBefore > 0);

            // Make one more call and verify count increases
            await client.ChatAsync("Ping").ConfigureAwait(false);
            Assert("CallDetails: count incremented", client.CallDetails.Count > countBefore);

            // Inspect the last call detail
            CompletionCallDetail last = client.CallDetails[client.CallDetails.Count - 1];
            Assert("CallDetail: has URL", !string.IsNullOrEmpty(last.Url));
            Assert("CallDetail: method is POST", last.Method == "POST");
            Assert("CallDetail: has request body", !string.IsNullOrEmpty(last.RequestBody));
            Assert("CallDetail: has request headers", last.RequestHeaders != null && last.RequestHeaders.Count > 0);
            Assert("CallDetail: has status code", last.StatusCode.HasValue);
            Assert("CallDetail: has response body", !string.IsNullOrEmpty(last.ResponseBody));
            Assert("CallDetail: has response headers", last.ResponseHeaders != null && last.ResponseHeaders.Count > 0);
            Assert("CallDetail: has response time", last.ResponseTimeMs.HasValue && last.ResponseTimeMs.Value > 0);
            Assert("CallDetail: success is true", last.Success);
            Assert("CallDetail: has timestamp", last.TimestampUtc > DateTime.MinValue);

            Console.WriteLine("");
        }

        #endregion

        #region ListModels-Tests

        private static async Task RunListModelsTests(CompletionClientBase client)
        {
            PrintSection("ListModels Tests");

            List<ModelInformation> models = new List<ModelInformation>();
            await foreach (ModelInformation model in client.ListModelsAsync().ConfigureAwait(false))
            {
                models.Add(model);
            }

            Assert("ListModels: yields at least one model", models.Count > 0);

            if (models.Count > 0)
            {
                Assert("ListModels: first model has non-null/non-empty Name", !string.IsNullOrEmpty(models[0].Name));
            }

            bool allHaveName = true;
            foreach (ModelInformation model in models)
            {
                if (string.IsNullOrEmpty(model.Name))
                {
                    allHaveName = false;
                    break;
                }
            }
            Assert("ListModels: all models have non-null Name", allHaveName);

            Console.WriteLine("");
        }

        #endregion

        #region ModelExists-Tests

        private static async Task RunModelExistsTests(CompletionClientBase client)
        {
            PrintSection("ModelExists Tests");

            bool inferenceExists = await client.ModelExistsAsync(client.Model).ConfigureAwait(false);
            Assert("ModelExists: inference model exists", inferenceExists);

            bool embeddingExists = await client.ModelExistsAsync(_EmbeddingModel).ConfigureAwait(false);
            Assert("ModelExists: embedding model exists", embeddingExists);

            bool bogusExists = await client.ModelExistsAsync("nonexistent-model-xyz-999").ConfigureAwait(false);
            Assert("ModelExists: nonexistent model returns false", !bogusExists);

            Console.WriteLine("");
        }

        #endregion

        #region GetModelInformation-Tests

        private static async Task RunGetModelInformationTests(CompletionClientBase client)
        {
            PrintSection("GetModelInformation Tests");

            ModelInformation? info = await client.GetModelInformationAsync(client.Model).ConfigureAwait(false);
            Assert("GetModelInfo: inference model found", info != null);
            Assert("GetModelInfo: Name is non-empty", info != null && !string.IsNullOrEmpty(info.Name));

            ModelInformation? embeddingInfo = await client.GetModelInformationAsync(_EmbeddingModel).ConfigureAwait(false);
            Assert("GetModelInfo: embedding model found", embeddingInfo != null);

            ModelInformation? bogusInfo = await client.GetModelInformationAsync("nonexistent-model-xyz-999").ConfigureAwait(false);
            Assert("GetModelInfo: nonexistent model returns null", bogusInfo == null);

            Console.WriteLine("");
        }

        #endregion

        #region PullModel-Tests

        private static async Task RunPullModelTests(CompletionClientBase client, string providerType)
        {
            PrintSection("PullModel Tests");

            if (!string.Equals(providerType, "ollama", StringComparison.OrdinalIgnoreCase))
            {
                bool threwNotSupported = false;
                try
                {
                    await client.PullModelAsync("test").ConfigureAwait(false);
                }
                catch (NotSupportedException)
                {
                    threwNotSupported = true;
                }
                Assert("PullModel: throws NotSupportedException for unsupported provider", threwNotSupported);
                Console.WriteLine("");
                return;
            }

            // Pull the inference model (should already exist, so this is a fast no-op)
            List<string> statusMessages = new List<string>();

            bool pullResult = await client.PullModelAsync(
                client.Model,
                async (ModelPullProgress p) =>
                {
                    statusMessages.Add(p.Status);
                    await Task.CompletedTask.ConfigureAwait(false);
                }).ConfigureAwait(false);

            Assert("PullModel: returns true for existing model", pullResult);
            Assert("PullModel: received progress callbacks", statusMessages.Count > 0);
            Assert("PullModel: saw success status", statusMessages.Exists(s => string.Equals(s, "success", StringComparison.OrdinalIgnoreCase)));

            // Pull nonexistent model should return false
            bool pullBogus = await client.PullModelAsync("nonexistent-model-xyz-999").ConfigureAwait(false);
            Assert("PullModel: returns false for nonexistent model", !pullBogus);

            Console.WriteLine("");
        }

        #endregion

        #region DeleteModel-Tests

        private static async Task RunDeleteModelTests(CompletionClientBase client, string providerType)
        {
            PrintSection("DeleteModel Tests");

            if (!string.Equals(providerType, "ollama", StringComparison.OrdinalIgnoreCase))
            {
                bool threwNotSupported = false;
                try
                {
                    await client.DeleteModelAsync("test").ConfigureAwait(false);
                }
                catch (NotSupportedException)
                {
                    threwNotSupported = true;
                }
                Assert("DeleteModel: throws NotSupportedException for unsupported provider", threwNotSupported);
                Console.WriteLine("");
                return;
            }

            // Delete nonexistent model should return false
            bool deleteBogus = await client.DeleteModelAsync("nonexistent-model-xyz-999").ConfigureAwait(false);
            Assert("DeleteModel: returns false for nonexistent model", !deleteBogus);

            Console.WriteLine("");
        }

        #endregion

        #region ValidateConnectivity-Tests

        private static async Task RunValidateConnectivityTests(CompletionClientBase client, string providerType, string endpoint, string? apiKey)
        {
            PrintSection("ValidateConnectivity Tests");

            bool ok = await client.ValidateConnectivityAsync().ConfigureAwait(false);
            Assert("ValidateConnectivity: returns true with valid endpoint", ok);

            // Create a client with an unreachable endpoint
            bool badResult;
            using (CompletionClientBase badClient = CreateClient(providerType, "http://localhost:1", apiKey))
            {
                badClient.TimeoutMs = 5000;
                badResult = await badClient.ValidateConnectivityAsync().ConfigureAwait(false);
            }
            Assert("ValidateConnectivity: returns false with bad endpoint", !badResult);

            Console.WriteLine("");
        }

        #endregion

        #region Cancellation-Tests

        private static async Task RunCancellationTests(CompletionClientBase client)
        {
            PrintSection("Cancellation Tests");

            // Chat with pre-cancelled token
            using CancellationTokenSource preCancelled = new CancellationTokenSource();
            preCancelled.Cancel();

            bool chatCancelled = false;
            try
            {
                await client.ChatAsync("This should be cancelled", token: preCancelled.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                chatCancelled = true;
            }
            Assert("Cancellation: ChatAsync respects cancelled token", chatCancelled);

            // Generate with pre-cancelled token
            using CancellationTokenSource preCancelled2 = new CancellationTokenSource();
            preCancelled2.Cancel();

            bool genCancelled = false;
            try
            {
                await client.GenerateAsync("This should be cancelled", token: preCancelled2.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                genCancelled = true;
            }
            Assert("Cancellation: GenerateAsync respects cancelled token", genCancelled);

            // EmbedAsync (single) with pre-cancelled token
            using CancellationTokenSource preCancelled3 = new CancellationTokenSource();
            preCancelled3.Cancel();

            bool embedCancelled = false;
            try
            {
                await client.EmbedAsync("This should be cancelled", token: preCancelled3.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                embedCancelled = true;
            }
            Assert("Cancellation: EmbedAsync (single) respects cancelled token", embedCancelled);

            // EmbedAsync (batch) with pre-cancelled token
            using CancellationTokenSource preCancelled4 = new CancellationTokenSource();
            preCancelled4.Cancel();

            bool embedBatchCancelled = false;
            try
            {
                await client.EmbedAsync(new List<string> { "a", "b" }, token: preCancelled4.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                embedBatchCancelled = true;
            }
            Assert("Cancellation: EmbedAsync (batch) respects cancelled token", embedBatchCancelled);

            Console.WriteLine("");
        }

        #endregion

        #region Option-Factories

        private static ChatCompletionOptions CreateChatOptions(string providerType)
        {
            switch (providerType)
            {
                case "ollama":
                    OllamaChatCompletionOptions ollamaOpts = new OllamaChatCompletionOptions();
                    ollamaOpts.Temperature = 0.5;
                    ollamaOpts.TopP = 0.9;
                    ollamaOpts.MaxTokens = 64;
                    ollamaOpts.TopK = 40;
                    ollamaOpts.RepeatPenalty = 1.1;
                    ollamaOpts.Seed = 42;
                    return ollamaOpts;

                case "openai":
                    OpenAiChatCompletionOptions openaiOpts = new OpenAiChatCompletionOptions();
                    openaiOpts.Temperature = 0.5;
                    openaiOpts.TopP = 0.9;
                    openaiOpts.MaxTokens = 64;
                    openaiOpts.FrequencyPenalty = 0.0;
                    openaiOpts.PresencePenalty = 0.0;
                    openaiOpts.Seed = 42;
                    return openaiOpts;

                case "gemini":
                    GeminiChatCompletionOptions geminiOpts = new GeminiChatCompletionOptions();
                    geminiOpts.Temperature = 0.5;
                    geminiOpts.TopP = 0.9;
                    geminiOpts.MaxTokens = 64;
                    geminiOpts.TopK = 40;
                    return geminiOpts;

                default:
                    return new ChatCompletionOptions();
            }
        }

        private static EmbeddingOptions? CreateEmbeddingModelOptions(string providerType)
        {
            string embeddingModel;

            if (!string.IsNullOrEmpty(_EmbeddingModel))
            {
                embeddingModel = _EmbeddingModel;
            }
            else
            {
                switch (providerType)
                {
                    case "ollama": embeddingModel = "all-minilm"; break;
                    case "openai": embeddingModel = "text-embedding-3-small"; break;
                    case "gemini": embeddingModel = "text-embedding-004"; break;
                    default: return null;
                }
            }

            EmbeddingOptions opts = new EmbeddingOptions();
            opts.Model = embeddingModel;
            return opts;
        }

        private static EmbeddingOptions? CreateEmbeddingOptions(string providerType)
        {
            switch (providerType)
            {
                case "ollama":
                    OllamaEmbeddingOptions ollamaOpts = new OllamaEmbeddingOptions();
                    ollamaOpts.Model = !string.IsNullOrEmpty(_EmbeddingModel) ? _EmbeddingModel : "all-minilm";
                    ollamaOpts.ContextLength = 2048;
                    return ollamaOpts;

                case "openai":
                    OpenAiEmbeddingOptions openaiOpts = new OpenAiEmbeddingOptions();
                    openaiOpts.Model = !string.IsNullOrEmpty(_EmbeddingModel) ? _EmbeddingModel : "text-embedding-3-small";
                    openaiOpts.Dimensions = 256;
                    return openaiOpts;

                case "gemini":
                    GeminiEmbeddingOptions geminiOpts = new GeminiEmbeddingOptions();
                    geminiOpts.Model = !string.IsNullOrEmpty(_EmbeddingModel) ? _EmbeddingModel : "text-embedding-004";
                    geminiOpts.TaskType = "RETRIEVAL_DOCUMENT";
                    return geminiOpts;

                default:
                    return null;
            }
        }

        private static GenerationOptions? CreateGenerationOptions(string providerType)
        {
            switch (providerType)
            {
                case "ollama":
                    OllamaGenerationOptions ollamaOpts = new OllamaGenerationOptions();
                    ollamaOpts.Temperature = 0.5;
                    ollamaOpts.TopP = 0.9;
                    ollamaOpts.MaxTokens = 64;
                    ollamaOpts.TopK = 40;
                    ollamaOpts.RepeatPenalty = 1.1;
                    ollamaOpts.Seed = 42;
                    return ollamaOpts;

                case "openai":
                    OpenAiGenerationOptions openaiOpts = new OpenAiGenerationOptions();
                    openaiOpts.Temperature = 0.5;
                    openaiOpts.TopP = 0.9;
                    openaiOpts.MaxTokens = 64;
                    openaiOpts.FrequencyPenalty = 0.0;
                    openaiOpts.PresencePenalty = 0.0;
                    return openaiOpts;

                case "gemini":
                    GeminiGenerationOptions geminiOpts = new GeminiGenerationOptions();
                    geminiOpts.Temperature = 0.5;
                    geminiOpts.TopP = 0.9;
                    geminiOpts.MaxTokens = 64;
                    geminiOpts.TopK = 40;
                    return geminiOpts;

                default:
                    return null;
            }
        }

        #endregion

        #region Helpers

        private static async Task RunTestSection(string sectionName, Func<Task> testFunc)
        {
            try
            {
                await testFunc().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                string failName = sectionName + ": unhandled exception";
                _TotalTests++;
                _FailedTests++;
                _Failures.Add(failName);
                Console.WriteLine("  FAIL  " + failName + " (" + ex.GetType().Name + ": " + ex.Message + ")");
                Console.WriteLine("");
            }
        }

        private static void PrintSection(string title)
        {
            _TestStopwatch.Restart();
            Console.WriteLine("[" + title + "]");
            Console.WriteLine("");
        }

        private static void Assert(string testName, bool condition)
        {
            long elapsedMs = _TestStopwatch.ElapsedMilliseconds;
            _TestStopwatch.Restart();

            _TotalTests++;

            string timing = " (" + elapsedMs + " ms)";

            if (condition)
            {
                _PassedTests++;
                Console.WriteLine("  PASS  " + testName + timing);
            }
            else
            {
                _FailedTests++;
                _Failures.Add(testName);
                Console.WriteLine("  FAIL  " + testName + timing);
            }
        }

        private static bool ModelNameMatches(string available, string requested)
        {
            if (string.Equals(available, requested, StringComparison.OrdinalIgnoreCase))
                return true;

            // Strip tag from available name: "all-minilm:latest" -> "all-minilm"
            int colonIndex = available.IndexOf(':');
            if (colonIndex > 0)
            {
                string baseName = available.Substring(0, colonIndex);
                if (string.Equals(baseName, requested, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // Strip tag from requested name: user may pass "all-minilm:latest" while list returns "all-minilm:latest"
            colonIndex = requested.IndexOf(':');
            if (colonIndex > 0)
            {
                string baseName = requested.Substring(0, colonIndex);
                if (string.Equals(available, baseName, StringComparison.OrdinalIgnoreCase))
                    return true;

                // Also compare both base names
                int availColonIndex = available.IndexOf(':');
                if (availColonIndex > 0)
                {
                    string availBase = available.Substring(0, availColonIndex);
                    if (string.Equals(availBase, baseName, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }

        private static string ResolveEmbeddingModelName(string providerType)
        {
            if (!string.IsNullOrEmpty(_EmbeddingModel))
                return _EmbeddingModel;

            switch (providerType)
            {
                case "ollama": return "all-minilm";
                case "openai": return "text-embedding-3-small";
                case "gemini": return "text-embedding-004";
                default: return "(unknown)";
            }
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

        #endregion
    }
}
