namespace VoyageAIConsole
{
    using GetSomeInput;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using PolyPrompt.Options;

    public class Program
    {
        #region Private-Members

        private static bool _RunForever = true;
        private static VoyageAiClient _Client = null!;

        #endregion

        #region Public-Methods

        public static async Task Main(string[] args)
        {
            Console.WriteLine("");
            Console.WriteLine("VoyageAIConsole - VoyageAI Embeddings Test Harness");
            Console.WriteLine("");

            string endpoint = Inputty.GetString("Endpoint [https://api.voyageai.com]:", "https://api.voyageai.com", false);
            string? apiKey = Inputty.GetString("API key:", null, false);
            string model = Inputty.GetString("Model [voyage-3.5]:", "voyage-3.5", false);
            int timeoutMs = Inputty.GetInteger("Timeout ms [120000]:", 120000, true, false);

            _Client = new VoyageAiClient(endpoint, apiKey);
            _Client.Model = model;
            _Client.TimeoutMs = timeoutMs;

            Console.WriteLine("");
            Console.WriteLine("Client initialized. VoyageAI is embeddings-only. Type ? for help.");
            Console.WriteLine("");

            while (_RunForever)
            {
                string userInput = Inputty.GetString("Command [?/help]:", null, false);
                await ProcessCommand(userInput).ConfigureAwait(false);
            }
        }

        #endregion

        #region Private-Methods

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
                    await EmbedAsync().ConfigureAwait(false);
                    break;

                case "emb":
                case "embedbatch":
                    await EmbedBatchAsync().ConfigureAwait(false);
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
            Console.WriteLine("  ?/help          Show this help menu");
            Console.WriteLine("  c/cls           Clear the screen");
            Console.WriteLine("  em/embed        Generate a single embedding");
            Console.WriteLine("  emb/embedbatch  Generate batch embeddings");
            Console.WriteLine("  settings        Show current settings");
            Console.WriteLine("  val/validate    Validate provider connectivity (sends a minimal embedding request)");
            Console.WriteLine("  q/quit/exit     Exit the application");
            Console.WriteLine("");
            Console.WriteLine("VoyageAI is an embeddings-only provider; chat, tool calling, generation, and model");
            Console.WriteLine("management are not available.");
            Console.WriteLine("");
        }

        private static void PrintSettings()
        {
            Console.WriteLine("");
            Console.WriteLine("  Endpoint   : " + _Client.Endpoint);
            Console.WriteLine("  API key    : " + (string.IsNullOrEmpty(_Client.ApiKey) ? "(none)" : "(set)"));
            Console.WriteLine("  Model      : " + _Client.Model);
            Console.WriteLine("  Timeout ms : " + _Client.TimeoutMs);
            Console.WriteLine("");
        }

        private static VoyageAiEmbeddingOptions? PromptForOptions()
        {
            string? inputType = Inputty.GetString("Input type (query/document) [none]:", null, true);
            string? dimensionInput = Inputty.GetString("Output dimension (256/512/1024/2048) [model default]:", null, true);

            bool hasInputType = !string.IsNullOrWhiteSpace(inputType);
            bool hasDimension = int.TryParse(dimensionInput, out int dimension);

            if (!hasInputType && !hasDimension) return null;

            VoyageAiEmbeddingOptions options = new VoyageAiEmbeddingOptions();
            if (hasInputType) options.InputType = inputType;
            if (hasDimension) options.OutputDimension = dimension;
            return options;
        }

        private static void PrintEmbeddings(EmbeddingResponse response)
        {
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

            Console.WriteLine("");
            Console.WriteLine("--- Timing ---");
            Console.WriteLine("  Runtime : " + response.OverallRuntimeMs + " ms");
        }

        private static async Task EmbedAsync()
        {
            string input = Inputty.GetString("Text to embed:", null, false);
            VoyageAiEmbeddingOptions? options = PromptForOptions();

            Console.WriteLine("");

            try
            {
                EmbeddingResponse response = await _Client.EmbedAsync(input, options).ConfigureAwait(false);
                PrintEmbeddings(response);
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

            VoyageAiEmbeddingOptions? options = PromptForOptions();

            Console.WriteLine("");

            try
            {
                EmbeddingResponse response = await _Client.EmbedAsync(inputs, options).ConfigureAwait(false);
                PrintEmbeddings(response);
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
