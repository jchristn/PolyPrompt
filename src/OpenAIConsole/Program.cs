namespace OpenAIConsole
{
    using GetSomeInput;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;

    public class Program
    {
        #region Private-Members

        private static bool _RunForever = true;
        private static bool _Streaming = true;
        private static OpenAiClient _Client = null!;

        #endregion

        #region Public-Methods

        public static async Task Main(string[] args)
        {
            Console.WriteLine("");
            Console.WriteLine("OpenAIConsole - OpenAI Test Harness");
            Console.WriteLine("");

            string endpoint = Inputty.GetString("Endpoint [https://api.openai.com]:", "https://api.openai.com", false);
            string? apiKey = Inputty.GetString("API key:", null, false);
            string model = Inputty.GetString("Model [gpt-4o-mini]:", "gpt-4o-mini", false);
            int maxTokens = Inputty.GetInteger("Max tokens [4096]:", 4096, true, false);
            int timeoutMs = Inputty.GetInteger("Timeout ms [120000]:", 120000, true, false);

            _Client = new OpenAiClient(endpoint, apiKey);
            _Client.Model = model;
            _Client.MaxTokens = maxTokens;
            _Client.TimeoutMs = timeoutMs;

            Console.WriteLine("");
            Console.WriteLine("Client initialized. Type ? for help.");
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

                case "ch":
                case "chat":
                    await ChatAsync().ConfigureAwait(false);
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
            Console.WriteLine("  ch/chat         Send a chat completion");
            Console.WriteLine("  em/embed        Generate a single embedding");
            Console.WriteLine("  emb/embedbatch  Generate batch embeddings");
            Console.WriteLine("  gen/generate    Generate text (non-streaming)");
            Console.WriteLine("  gens/genstream  Generate text (streaming)");
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
            Console.WriteLine("  Model         : " + _Client.Model);
            Console.WriteLine("  Max tokens    : " + _Client.MaxTokens);
            Console.WriteLine("  Timeout ms    : " + _Client.TimeoutMs);
            Console.WriteLine("  Temperature   : " + (_Client.Temperature.HasValue ? _Client.Temperature.Value.ToString("F1") : "(default)"));
            Console.WriteLine("  Top-P         : " + (_Client.TopP.HasValue ? _Client.TopP.Value.ToString("F2") : "(default)"));
            Console.WriteLine("  Streaming     : " + (_Streaming ? "on" : "off"));
            Console.WriteLine("  System prompt : " + (string.IsNullOrEmpty(_Client.SystemPrompt) ? "(none)" : _Client.SystemPrompt));
            Console.WriteLine("");
        }

        private static void SetSystemPrompt()
        {
            Console.WriteLine("");
            if (!string.IsNullOrEmpty(_Client.SystemPrompt))
            {
                Console.WriteLine("Current system prompt: " + _Client.SystemPrompt);
            }
            string? newPrompt = Inputty.GetString("System prompt [Enter to clear]:", null, true);
            _Client.SystemPrompt = string.IsNullOrWhiteSpace(newPrompt) ? null : newPrompt;
            Console.WriteLine("System prompt " + (string.IsNullOrEmpty(_Client.SystemPrompt) ? "cleared" : "set") + ".");
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

            Console.WriteLine("");

            try
            {
                if (_Streaming)
                {
                    ChatStreamingResponse response = await _Client.ChatStreamingAsync(prompt).ConfigureAwait(false);

                    if (!response.Success)
                    {
                        Console.WriteLine("Error: " + response.Error);
                        Console.WriteLine("");
                        return;
                    }

                    await foreach (ChatStreamingChunk chunk in response.Chunks.ConfigureAwait(false))
                    {
                        if (!string.IsNullOrEmpty(chunk.Text))
                        {
                            Console.Write(chunk.Text);
                        }
                    }

                    Console.WriteLine("");
                    Console.WriteLine("");
                    PrintStreamingMetadata(response);
                }
                else
                {
                    ChatResponse response = await _Client.ChatAsync(prompt).ConfigureAwait(false);

                    if (!response.Success)
                    {
                        Console.WriteLine("Error: " + response.Error);
                    }
                    else if (string.IsNullOrWhiteSpace(response.Text))
                    {
                        Console.WriteLine("(empty response)");
                    }
                    else
                    {
                        Console.WriteLine(response.Text);
                    }

                    Console.WriteLine("");
                    Console.WriteLine("--- Timing ---");
                    Console.WriteLine("  Runtime : " + response.OverallRuntimeMs + " ms");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }

            Console.WriteLine("");
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
                    foreach (EmbeddingResult emb in response.Embeddings)
                    {
                        Console.WriteLine("  [" + emb.Index + "] dimensions: " + emb.Embedding.Length);
                        if (emb.Embedding.Length > 0)
                        {
                            Console.WriteLine("  First 5 values: " + string.Join(", ", emb.Embedding.Take(5).Select(v => v.ToString("F6"))));
                        }
                    }
                }

                Console.WriteLine("");
                Console.WriteLine("--- Timing ---");
                Console.WriteLine("  Runtime : " + response.OverallRuntimeMs + " ms");
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
                    foreach (EmbeddingResult emb in response.Embeddings)
                    {
                        Console.WriteLine("  [" + emb.Index + "] dimensions: " + emb.Embedding.Length);
                    }
                }

                Console.WriteLine("");
                Console.WriteLine("--- Timing ---");
                Console.WriteLine("  Runtime : " + response.OverallRuntimeMs + " ms");
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

            Console.WriteLine("");

            try
            {
                GenerationResponse response = await _Client.GenerateAsync(prompt).ConfigureAwait(false);

                if (!response.Success)
                {
                    Console.WriteLine("Error: " + response.Error);
                }
                else if (string.IsNullOrWhiteSpace(response.Text))
                {
                    Console.WriteLine("(empty response)");
                }
                else
                {
                    Console.WriteLine(response.Text);
                }

                Console.WriteLine("");
                Console.WriteLine("--- Timing ---");
                Console.WriteLine("  Runtime : " + response.OverallRuntimeMs + " ms");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }

            Console.WriteLine("");
        }

        private static async Task GenerateStreamingAsync()
        {
            string prompt = Inputty.GetString("Prompt:", null, false);

            Console.WriteLine("");

            try
            {
                GenerationStreamingResponse response = await _Client.GenerateStreamingAsync(prompt).ConfigureAwait(false);

                if (!response.Success)
                {
                    Console.WriteLine("Error: " + response.Error);
                    Console.WriteLine("");
                    return;
                }

                await foreach (GenerationStreamingChunk chunk in response.Chunks.ConfigureAwait(false))
                {
                    if (!string.IsNullOrEmpty(chunk.Text))
                    {
                        Console.Write(chunk.Text);
                    }
                }

                Console.WriteLine("");
                Console.WriteLine("");
                Console.WriteLine("--- Timing ---");
                Console.WriteLine("  Overall runtime : " + response.OverallRuntimeMs + " ms");
                if (response.TimeToFirstTokenMs >= 0) Console.WriteLine("  First token     : " + response.TimeToFirstTokenMs + " ms");
                if (response.TimeToLastTokenMs >= 0) Console.WriteLine("  Last token      : " + response.TimeToLastTokenMs + " ms");
                Console.WriteLine("  Chunks received : " + response.ChunkCount);
                if (response.OverallTokensPerSecond > 0) Console.WriteLine("  Overall tok/sec : " + response.OverallTokensPerSecond.ToString("F1"));
                if (response.InterTokenTokensPerSecond > 0) Console.WriteLine("  Stream tok/sec  : " + response.InterTokenTokensPerSecond.ToString("F1"));
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }

            Console.WriteLine("");
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
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }

            Console.WriteLine("");
        }

        private static void PrintStreamingMetadata(ChatStreamingResponse response)
        {
            Console.WriteLine("--- Metadata ---");
            Console.WriteLine("  Model           : " + response.Model);
            if (response.ResponseId != null) Console.WriteLine("  Response ID     : " + response.ResponseId);
            if (response.FinishReason != null) Console.WriteLine("  Finish reason   : " + response.FinishReason);
            if (response.Usage != null)
            {
                if (response.Usage.PromptTokens.HasValue) Console.WriteLine("  Prompt tokens   : " + response.Usage.PromptTokens.Value);
                if (response.Usage.CompletionTokens.HasValue) Console.WriteLine("  Compl. tokens   : " + response.Usage.CompletionTokens.Value);
                if (response.Usage.TotalTokens.HasValue) Console.WriteLine("  Total tokens    : " + response.Usage.TotalTokens.Value);
            }
            Console.WriteLine("--- Timing ---");
            Console.WriteLine("  Overall runtime : " + response.OverallRuntimeMs + " ms");
            if (response.TimeToFirstTokenMs >= 0) Console.WriteLine("  First token     : " + response.TimeToFirstTokenMs + " ms");
            if (response.TimeToLastTokenMs >= 0) Console.WriteLine("  Last token      : " + response.TimeToLastTokenMs + " ms");
            Console.WriteLine("  Chunks received : " + response.ChunkCount);
            if (response.OverallTokensPerSecond > 0) Console.WriteLine("  Overall tok/sec : " + response.OverallTokensPerSecond.ToString("F1"));
            if (response.InterTokenTokensPerSecond > 0) Console.WriteLine("  Stream tok/sec  : " + response.InterTokenTokensPerSecond.ToString("F1"));
        }

        #endregion
    }
}
