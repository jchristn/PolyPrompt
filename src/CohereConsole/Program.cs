namespace CohereConsole
{
    using GetSomeInput;
    using PolyPrompt.ConsoleSupport;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using PolyPrompt.Options;

    public class Program : ConsoleApplicationBase
    {
        #region Private-Members

        private static bool _RunForever = true;
        private static bool _Streaming = true;
        private static CohereClient _Client = null!;

        #endregion

        #region Public-Methods

        public static async Task Main(string[] args)
        {
            Console.WriteLine("");
            Console.WriteLine("CohereConsole - Cohere Test Harness");
            Console.WriteLine("");

            string endpoint = Inputty.GetString("Endpoint [https://api.cohere.com]:", "https://api.cohere.com", false);
            string? apiKey = Inputty.GetString("API key:", null, false);
            string model = Inputty.GetString("Chat model [command-a-plus-05-2026]:", "command-a-plus-05-2026", false);
            string embeddingModel = Inputty.GetString("Embedding model [embed-v4.0]:", "embed-v4.0", false);
            string rerankModel = Inputty.GetString("Rerank model [rerank-v5.0-fast]:", "rerank-v5.0-fast", false);
            int maxTokens = Inputty.GetInteger("Max tokens [4096]:", 4096, true, false);
            int timeoutMs = Inputty.GetInteger("Timeout ms [120000]:", 120000, true, false);

            _Client = new CohereClient(apiKey, endpoint, new ClientOptions { HttpClient = CreateConsoleHttpClient() });
            _Client.Defaults.Completion.Model = model;
            _Client.Defaults.Completion.MaxTokens = maxTokens;
            _Client.TimeoutMs = timeoutMs;

            _Client.Defaults.Embedding.Model = embeddingModel;

            _Client.Defaults.Rerank.Model = rerankModel;
            _Client.TimeoutMs = timeoutMs;

            Console.WriteLine("");
            Console.WriteLine("Client initialized. Type ? for help.");
            Console.WriteLine("");

            while (_RunForever)
            {
                string? userInput = ReadCommand();
                if (userInput == null) break;
                try { await ProcessCommand(userInput).ConfigureAwait(false); }
                catch (Exception ex) { Console.WriteLine("Error: " + ex.Message); }
            }

            ShutdownSharedConsole();

            _Client.Dispose();
        }

        #endregion

        #region Private-Methods

        private static async Task ProcessCommand(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return;
            if (await TryHandleSharedCommandAsync(input, _Client)) return;

            string trimmed = input.Trim().ToLowerInvariant();

            switch (trimmed)
            {
                case "?":
                case "help":
                    PrintMenu();
                    break;

                case "c":
                case "cls":
                    Console.Clear();
                    break;

                case "q":
                case "quit":
                case "exit":
                    _RunForever = false;
                    break;

                case "ch":
                case "chat":
                    await ChatAsync().ConfigureAwait(false);
                    break;

                case "tc":
                case "toolchat":
                    await ToolChatCommandAsync(CancellationToken.None).ConfigureAwait(false);
                    break;

                case "em":
                case "embed":
                    await EmbedAsync().ConfigureAwait(false);
                    break;

                case "emb":
                case "embedbatch":
                    await EmbedBatchAsync().ConfigureAwait(false);
                    break;

                case "gen":
                case "generate":
                    await GenerateAsync().ConfigureAwait(false);
                    break;

                case "gens":
                case "genstream":
                    await GenerateStreamingAsync().ConfigureAwait(false);
                    break;

                case "system":
                    SetSystemPrompt();
                    break;

                case "streaming":
                    ToggleStreaming();
                    break;

                case "settings":
                    PrintSettings();
                    break;

                case "mod":
                case "models":
                    await ListModelsAsync().ConfigureAwait(false);
                    break;

                case "rr":
                case "rerank":
                    await RerankAsync().ConfigureAwait(false);
                    break;

                case "cl":
                case "classify":
                    await ClassifyAsync().ConfigureAwait(false);
                    break;

                case "val":
                case "validate":
                    await ValidateConnectivityAsync().ConfigureAwait(false);
                    break;

                default:
                    Console.WriteLine("Unknown command. Type ? for help.");
                    break;
            }
        }

        private static void PrintMenu()
        {
            Console.WriteLine("");
            Console.WriteLine("Available commands:");
            PrintSharedHelp();
            Console.WriteLine("  ?/help          Show this help menu");
            Console.WriteLine("  c/cls           Clear the screen");
            Console.WriteLine("  ch/chat         Chat with automatic Mux tools");
            Console.WriteLine("  tc/toolchat     Chat with automatic Mux tools (alias for chat)");
            Console.WriteLine("  em/embed        Generate a single embedding (embedding model)");
            Console.WriteLine("  emb/embedbatch  Generate batch embeddings (embedding model)");
            Console.WriteLine("  rr/rerank       Rerank documents against a query (rerank model)");
            Console.WriteLine("  cl/classify     Classify texts using few-shot examples");
            Console.WriteLine("  gen/generate    Generate text (non-streaming, via single-turn v2 chat)");
            Console.WriteLine("  gens/genstream  Generate text (streaming, via single-turn v2 chat)");
            Console.WriteLine("  system          Set or clear the system prompt");
            Console.WriteLine("  streaming       Toggle chat streaming on/off (current: " + (_Streaming ? "True" : "False") + ")");
            Console.WriteLine("  settings        Show current settings");
            Console.WriteLine("  mod/models      List available models");
            Console.WriteLine("  val/validate    Validate provider connectivity");
            Console.WriteLine("  q/quit/exit     Exit the application");
            Console.WriteLine("");
        }

        private static void PrintSettings()
        {
            Console.WriteLine("");
            Console.WriteLine("  Endpoint      : " + _Client.Endpoint);
            Console.WriteLine("  API key       : " + (string.IsNullOrEmpty(_Client.ApiKey) ? "(none)" : "(set)"));
            Console.WriteLine("  Chat model    : " + _Client.Defaults.Completion.Model);
            Console.WriteLine("  Embed model   : " + _Client.Defaults.Embedding.Model);
            Console.WriteLine("  Rerank model  : " + _Client.Defaults.Rerank.Model);
            Console.WriteLine("  Max tokens    : " + _Client.Defaults.Completion.MaxTokens);
            Console.WriteLine("  Timeout ms    : " + _Client.TimeoutMs);
            Console.WriteLine("  Temperature   : " + (_Client.Defaults.Completion.Temperature.HasValue ? _Client.Defaults.Completion.Temperature.Value.ToString("F1") : "(default)"));
            Console.WriteLine("  Top-P         : " + (_Client.Defaults.Completion.TopP.HasValue ? _Client.Defaults.Completion.TopP.Value.ToString("F2") : "(default)"));
            Console.WriteLine("  Streaming     : " + (_Streaming ? "on" : "off"));
            Console.WriteLine("  System prompt : " + (string.IsNullOrEmpty(_Client.Defaults.Completion.SystemPrompt) ? "(none)" : _Client.Defaults.Completion.SystemPrompt));
            Console.WriteLine("");
        }

        private static void SetSystemPrompt()
        {
            Console.WriteLine("");
            if (!string.IsNullOrEmpty(_Client.Defaults.Completion.SystemPrompt))
            {
                Console.WriteLine("Current system prompt: " + _Client.Defaults.Completion.SystemPrompt);
            }
            string? newPrompt = Inputty.GetString("System prompt [Enter to clear]:", null, true);
            _Client.Defaults.Completion.SystemPrompt = string.IsNullOrWhiteSpace(newPrompt) ? null : newPrompt;
            Console.WriteLine("System prompt " + (string.IsNullOrEmpty(_Client.Defaults.Completion.SystemPrompt) ? "cleared" : "set") + ".");
            Console.WriteLine("");
        }

        private static void ToggleStreaming()
        {
            _Streaming = !_Streaming;
            Console.WriteLine("Streaming " + (_Streaming ? "enabled" : "disabled") + ".");
        }

        private static async Task ChatAsync()
        {
            string prompt = Inputty.GetString("Prompt:", null, false);
            await RunSharedChatAsync(_Client, prompt, _Streaming);
        }

        private static Task ToolChatCommandAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return ChatAsync();
        }

        private static async Task EmbedAsync()
        {
            string input = Inputty.GetString("Text to embed:", null, false);

            Console.WriteLine("");

            try
            {
                EmbeddingResponse response = await _Client.EmbedAsync(input).ConfigureAwait(false);

                if (!response.Success)
                {
                    Console.WriteLine("Error: " + response.Error);
                }
                else
                {
                    Console.WriteLine("Embeddings returned: " + response.Embeddings.Count);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }

            Console.WriteLine("");
        }

        private static async Task EmbedBatchAsync()
        {
            List<string> inputs = new List<string>();
            Console.WriteLine("Enter texts to embed (empty line to finish):");
            while (true)
            {
                string? line = Inputty.GetString("Text [Enter to finish]:", null, true);
                if (string.IsNullOrWhiteSpace(line)) break;
                inputs.Add(line);
            }

            if (inputs.Count == 0)
            {
                Console.WriteLine("No inputs provided.");
                return;
            }

            Console.WriteLine("");

            try
            {
                EmbeddingResponse response = await _Client.EmbedAsync(inputs).ConfigureAwait(false);

                if (!response.Success)
                {
                    Console.WriteLine("Error: " + response.Error);
                }
                else
                {
                    Console.WriteLine("Embeddings returned: " + response.Embeddings.Count);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }

            Console.WriteLine("");
        }

        private static async Task GenerateAsync()
        {
            string prompt = Inputty.GetString("Prompt:", null, false);
            await RunSharedGenerationAsync(_Client, prompt, false);
        }

        private static async Task GenerateStreamingAsync()
        {
            string prompt = Inputty.GetString("Prompt:", null, false);
            await RunSharedGenerationAsync(_Client, prompt, true);
        }

        private static async Task ListModelsAsync()
        {
            Console.WriteLine("");

            try
            {
                await foreach (ModelInformation model in _Client.ListModelsAsync().ConfigureAwait(false))
                {
                    Console.WriteLine("  " + model.Name + (model.DisplayName != null ? " (" + model.DisplayName + ")" : ""));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }

            Console.WriteLine("");
        }

        private static async Task ValidateConnectivityAsync()
        {
            Console.WriteLine("");

            try
            {
                bool ok = await _Client.ValidateConnectivityAsync().ConfigureAwait(false);
                Console.WriteLine(ok ? "Connectivity: OK" : "Connectivity: FAILED");
                Console.WriteLine("Capabilities: " + _Client.Capabilities);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }

            Console.WriteLine("");
        }

        private static List<string> ReadLines(string label)
        {
            List<string> lines = new List<string>();
            Console.WriteLine("Enter " + label + " (empty line to finish):");
            while (true)
            {
                string? line = Inputty.GetString("Text [Enter to finish]:", null, true);
                if (string.IsNullOrWhiteSpace(line)) break;
                lines.Add(line);
            }
            return lines;
        }

        private static async Task RerankAsync()
        {
            string query = Inputty.GetString("Query:", null, false);
            List<string> documents = ReadLines("documents");
            if (documents.Count == 0)
            {
                Console.WriteLine("No documents provided.");
                return;
            }

            int topN = Inputty.GetInteger("Top N (0 = all):", 0, true, true);

            CohereRerankOptions options = new CohereRerankOptions { ReturnDocuments = true };
            if (topN > 0) options.TopN = Math.Min(topN, documents.Count);

            Console.WriteLine("");

            try
            {
                RerankResponse response = await _Client.RerankAsync(query, documents, options).ConfigureAwait(false);
                if (!response.Success)
                {
                    Console.WriteLine("Error: " + response.Error);
                }
                else
                {
                    foreach (RerankResult result in response.Results)
                    {
                        Console.WriteLine("  [" + result.Index + "] " + result.Score.ToString("F4") + "  " + result.Document);
                    }

                    Console.WriteLine("");
                    Console.WriteLine("  Search units : " + (response.SearchUnits?.ToString() ?? "(n/a)"));
                }

                Console.WriteLine("  Runtime      : " + response.OverallRuntimeMs + " ms");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }

            Console.WriteLine("");
        }

        private static async Task ClassifyAsync()
        {
            List<string> inputs = ReadLines("texts to classify");
            if (inputs.Count == 0)
            {
                Console.WriteLine("No inputs provided.");
                return;
            }

            Console.WriteLine("Enter labeled examples as label|text (at least 2 per label; empty line to finish):");
            CohereClassificationOptions options = new CohereClassificationOptions();
            while (true)
            {
                string? line = Inputty.GetString("Example [Enter to finish]:", null, true);
                if (string.IsNullOrWhiteSpace(line)) break;

                int separator = line.IndexOf('|');
                if (separator <= 0 || separator == line.Length - 1)
                {
                    Console.WriteLine("Use the form label|text.");
                    continue;
                }

                options.Examples.Add(new ClassificationExample(line.Substring(separator + 1).Trim(), line.Substring(0, separator).Trim()));
            }

            Console.WriteLine("");

            try
            {
                ClassificationResponse response = await _Client.ClassifyAsync(inputs, options).ConfigureAwait(false);
                if (!response.Success)
                {
                    Console.WriteLine("Error: " + response.Error);
                }
                else
                {
                    foreach (ClassificationResult result in response.Classifications)
                    {
                        Console.WriteLine("  [" + result.Index + "] " + result.Label + " (" + (result.Score?.ToString("F4") ?? "n/a") + ")  " + result.Input);
                    }
                }

                Console.WriteLine("  Runtime : " + response.OverallRuntimeMs + " ms");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }

            Console.WriteLine("");
        }

        #endregion
    }
}
