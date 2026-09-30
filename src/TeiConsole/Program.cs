namespace TeiConsole
{
    using GetSomeInput;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using PolyPrompt.Options;

    public class Program
    {
        #region Private-Members

        private static bool _RunForever = true;
        private static TeiEmbeddingClient _EmbeddingClient = null!;
        private static TeiSparseEmbeddingClient _SparseClient = null!;
        private static TeiRerankClient _RerankClient = null!;
        private static TeiClassificationClient _ClassificationClient = null!;
        private static TeiModelClient _ModelClient = null!;

        #endregion

        #region Public-Methods

        public static async Task Main(string[] args)
        {
            Console.WriteLine("");
            Console.WriteLine("TeiConsole - Hugging Face Text Embeddings Inference Test Harness");
            Console.WriteLine("");

            string endpoint = Inputty.GetString("Endpoint [http://localhost:8080]:", "http://localhost:8080", false);
            string? apiKey = Inputty.GetString("API key (only if the server uses --api-key) [none]:", null, true);
            int timeoutMs = Inputty.GetInteger("Timeout ms [120000]:", 120000, true, false);

            string? key = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;

            _EmbeddingClient = new TeiEmbeddingClient(endpoint, key);
            _SparseClient = new TeiSparseEmbeddingClient(endpoint, key);
            _RerankClient = new TeiRerankClient(endpoint, key);
            _ClassificationClient = new TeiClassificationClient(endpoint, key);
            _ModelClient = new TeiModelClient(endpoint, key);

            foreach (ClientBase client in AllClients())
            {
                client.TimeoutMs = timeoutMs;
            }

            Console.WriteLine("");
            Console.WriteLine("Client initialized. A TEI server hosts one model; use 'info' to see its type. Type ? for help.");
            Console.WriteLine("");

            while (_RunForever)
            {
                string userInput = Inputty.GetString("Command [?/help]:", null, false);
                await ProcessCommand(userInput).ConfigureAwait(false);
            }

            foreach (ClientBase client in AllClients())
            {
                client.Dispose();
            }
        }

        #endregion

        #region Private-Methods

        private static List<ClientBase> AllClients()
        {
            return new List<ClientBase> { _EmbeddingClient, _SparseClient, _RerankClient, _ClassificationClient, _ModelClient };
        }

        private static async Task ProcessCommand(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return;

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

                case "em":
                case "embed":
                    await EmbedAsync(false).ConfigureAwait(false);
                    break;

                case "emb":
                case "embedbatch":
                    await EmbedAsync(true).ConfigureAwait(false);
                    break;

                case "sp":
                case "sparse":
                    await EmbedSparseAsync().ConfigureAwait(false);
                    break;

                case "rr":
                case "rerank":
                    await RerankAsync().ConfigureAwait(false);
                    break;

                case "cl":
                case "classify":
                    await ClassifyAsync().ConfigureAwait(false);
                    break;

                case "info":
                case "mod":
                case "models":
                    await InfoAsync().ConfigureAwait(false);
                    break;

                case "settings":
                    PrintSettings();
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
            Console.WriteLine("  ?/help           Show this help menu");
            Console.WriteLine("  c/cls            Clear the screen");
            Console.WriteLine("  em/embed         Generate a single dense embedding (embedding models)");
            Console.WriteLine("  emb/embedbatch   Generate batch dense embeddings (embedding models)");
            Console.WriteLine("  sp/sparse        Generate sparse embeddings (SPLADE models)");
            Console.WriteLine("  rr/rerank        Rerank documents against a query (reranker models)");
            Console.WriteLine("  cl/classify      Classify texts (sequence classification models)");
            Console.WriteLine("  info/models      Show the hosted model from /info");
            Console.WriteLine("  settings         Show current settings");
            Console.WriteLine("  val/validate     Validate connectivity via /health");
            Console.WriteLine("  q/quit/exit      Exit the application");
            Console.WriteLine("");
            Console.WriteLine("TEI serves embeddings, reranking, and classification only; chat, tool calling, and");
            Console.WriteLine("generation are not available. Operations the hosted model does not support return an error.");
            Console.WriteLine("");
        }

        private static void PrintSettings()
        {
            Console.WriteLine("");
            Console.WriteLine("  Endpoint   : " + _EmbeddingClient.Endpoint);
            Console.WriteLine("  API key    : " + (string.IsNullOrEmpty(_EmbeddingClient.ApiKey) ? "(none)" : "(set)"));
            Console.WriteLine("  Timeout ms : " + _EmbeddingClient.TimeoutMs);
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

        private static async Task EmbedAsync(bool batch)
        {
            List<string> inputs = batch
                ? ReadLines("texts to embed")
                : new List<string> { Inputty.GetString("Text to embed:", null, false) };

            if (inputs.Count == 0)
            {
                Console.WriteLine("No inputs provided.");
                return;
            }

            string? promptName = Inputty.GetString("Prompt name (for example query) [none]:", null, true);
            TeiEmbeddingOptions options = new TeiEmbeddingOptions { PromptName = promptName, Truncate = true };

            Console.WriteLine("");

            try
            {
                EmbeddingResponse response = await _EmbeddingClient.EmbedAsync(inputs, options).ConfigureAwait(false);
                if (!response.Success)
                {
                    Console.WriteLine("Error: " + response.Error);
                }
                else
                {
                    Console.WriteLine("Embeddings returned: " + response.Embeddings.Count);
                    foreach (EmbeddingResult emb in response.Embeddings)
                    {
                        Console.WriteLine("  [" + emb.Index + "] dimensions: " + emb.Embedding.Length);
                        if (emb.Embedding.Length > 0)
                        {
                            Console.WriteLine("      First 5 values: " + string.Join(", ", emb.Embedding.Take(5).Select(v => v.ToString("F6"))));
                        }
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

        private static async Task EmbedSparseAsync()
        {
            List<string> inputs = ReadLines("texts to embed");
            if (inputs.Count == 0)
            {
                Console.WriteLine("No inputs provided.");
                return;
            }

            Console.WriteLine("");

            try
            {
                SparseEmbeddingResponse response = await _SparseClient.EmbedSparseAsync(inputs).ConfigureAwait(false);
                if (!response.Success)
                {
                    Console.WriteLine("Error: " + response.Error);
                }
                else
                {
                    foreach (SparseEmbeddingResult sparse in response.Embeddings)
                    {
                        Console.WriteLine("  [" + sparse.Index + "] non-zero entries: " + sparse.Values.Count);
                        Console.WriteLine("      First 5: " + string.Join(", ", sparse.Values.Take(5).Select(v => v.Index + ":" + v.Value.ToString("F4"))));
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
            bool rawScores = Inputty.GetBoolean("Raw scores (logits)?", false);

            TeiRerankOptions options = new TeiRerankOptions { ReturnDocuments = true, RawScores = rawScores };
            if (topN > 0) options.TopN = Math.Min(topN, documents.Count);

            Console.WriteLine("");

            try
            {
                RerankResponse response = await _RerankClient.RerankAsync(query, documents, options).ConfigureAwait(false);
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
                }

                Console.WriteLine("  Runtime : " + response.OverallRuntimeMs + " ms");
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

            Console.WriteLine("");

            try
            {
                ClassificationResponse response = await _ClassificationClient.ClassifyAsync(inputs).ConfigureAwait(false);
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

        private static async Task InfoAsync()
        {
            Console.WriteLine("");

            try
            {
                bool found = false;
                await foreach (ModelInformation model in _ModelClient.ListModelsAsync().ConfigureAwait(false))
                {
                    found = true;
                    Console.WriteLine("  Model id     : " + model.Name);
                    Console.WriteLine("  Served name  : " + (model.DisplayName ?? "(none)"));
                    Console.WriteLine("  Max input    : " + (model.InputTokenLimit?.ToString() ?? "(unknown)"));
                    foreach (KeyValuePair<string, string?> entry in model.Metadata)
                    {
                        Console.WriteLine("  " + entry.Key.PadRight(12) + " : " + entry.Value);
                    }
                }

                if (!found) Console.WriteLine("No model information returned (is the server reachable?).");
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
                foreach (ClientBase client in AllClients())
                {
                    bool ok = await client.ValidateConnectivityAsync().ConfigureAwait(false);
                    Console.WriteLine("Connectivity (" + client.GetType().Name + "): " + (ok ? "OK" : "FAILED"));
                }
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
