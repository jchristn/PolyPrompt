namespace AnthropicConsole
{
    using GetSomeInput;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;

    public class Program
    {
        #region Private-Members

        private static bool _RunForever = true;
        private static bool _Streaming = true;
        private static AnthropicClient _Client = null!;

        #endregion

        #region Public-Methods

        public static async Task Main(string[] args)
        {
            Console.WriteLine("");
            Console.WriteLine("AnthropicConsole - Anthropic (Claude) Test Harness");
            Console.WriteLine("");

            string endpoint = Inputty.GetString("Endpoint [https://api.anthropic.com]:", "https://api.anthropic.com", false);
            string? apiKey = Inputty.GetString("API key:", null, false);
            string? workspaceId = Inputty.GetString("Workspace ID (identity-linked keys only) [none]:", null, true);
            string model = Inputty.GetString("Model [claude-opus-4-8]:", "claude-opus-4-8", false);
            int maxTokens = Inputty.GetInteger("Max tokens [4096]:", 4096, true, false);
            int timeoutMs = Inputty.GetInteger("Timeout ms [120000]:", 120000, true, false);

            _Client = new AnthropicClient(endpoint, apiKey);
            _Client.WorkspaceId = workspaceId;
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
            Console.WriteLine("  tc/toolchat     Send a tool-capable chat using the sample get_weather tool");
            Console.WriteLine("  em/embed        Generate a single embedding (not supported by Anthropic)");
            Console.WriteLine("  emb/embedbatch  Generate batch embeddings (not supported by Anthropic)");
            Console.WriteLine("  gen/generate    Generate text (non-streaming, via the Messages API)");
            Console.WriteLine("  gens/genstream  Generate text (streaming, via the Messages API)");
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
            Console.WriteLine("  API version   : " + _Client.AnthropicVersion);
            Console.WriteLine("  Workspace ID  : " + (string.IsNullOrEmpty(_Client.WorkspaceId) ? "(none)" : _Client.WorkspaceId));
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
                        if (!string.IsNullOrEmpty(chunk.ReasoningText))
                        {
                            Console.Write(chunk.ReasoningText);
                        }
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
                        if (!string.IsNullOrEmpty(response.Reasoning))
                        {
                            Console.WriteLine("--- Reasoning ---");
                            Console.WriteLine(response.Reasoning);
                            Console.WriteLine("--- Answer ---");
                        }
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

        private static async Task ToolChatCommandAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            string prompt = Inputty.GetString("Prompt:", null, false);
            ToolChatRequest request = BuildWeatherToolRequest(prompt);

            Console.WriteLine("");

            try
            {
                if (_Streaming)
                {
                    await RunStreamingToolChatAsync(request, token).ConfigureAwait(false);
                }
                else
                {
                    await RunToolChatAsync(request, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }

            Console.WriteLine("");
        }

        private static async Task RunToolChatAsync(ToolChatRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            ToolChatResponse response = await _Client.ToolChatAsync(request, token).ConfigureAwait(false);

            if (!response.Success)
            {
                Console.WriteLine("Error: " + response.Error);
                return;
            }

            PrintToolChatText(response.Text);
            PrintToolCalls(response.ToolCalls);
            PrintToolChatMetadata(response);

            if (!response.ToolCalls.Any()) return;

            request.Messages.Add(response.ToAssistantMessage());
            AppendToolResults(request, response.ToolCalls);
            request.Tools.Clear();
            request.ToolChoice = "none";

            Console.WriteLine("");
            Console.WriteLine("--- Final response ---");

            ToolChatResponse finalResponse = await _Client.ToolChatAsync(request, token).ConfigureAwait(false);
            if (!finalResponse.Success)
            {
                Console.WriteLine("Error: " + finalResponse.Error);
                return;
            }

            PrintToolChatText(finalResponse.Text);
            PrintToolCalls(finalResponse.ToolCalls);
            PrintToolChatMetadata(finalResponse);
        }

        private static async Task RunStreamingToolChatAsync(ToolChatRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            ToolChatStreamingResponse response = await _Client.ToolChatStreamingAsync(request, token).ConfigureAwait(false);

            if (!response.Success)
            {
                Console.WriteLine("Error: " + response.Error);
                return;
            }

            await foreach (ToolChatStreamingChunk chunk in response.Chunks.WithCancellation(token).ConfigureAwait(false))
            {
                if (!string.IsNullOrEmpty(chunk.ReasoningText))
                {
                    Console.Write(chunk.ReasoningText);
                }
                if (!string.IsNullOrEmpty(chunk.Text))
                {
                    Console.Write(chunk.Text);
                }
            }

            Console.WriteLine("");
            PrintToolCalls(response.ToolCalls);
            PrintToolChatStreamingMetadata(response);

            if (!response.ToolCalls.Any()) return;

            request.Messages.Add(response.ToAssistantMessage());
            AppendToolResults(request, response.ToolCalls);
            request.Tools.Clear();
            request.ToolChoice = "none";

            Console.WriteLine("");
            Console.WriteLine("--- Final response ---");

            ToolChatStreamingResponse finalResponse = await _Client.ToolChatStreamingAsync(request, token).ConfigureAwait(false);
            if (!finalResponse.Success)
            {
                Console.WriteLine("Error: " + finalResponse.Error);
                return;
            }

            await foreach (ToolChatStreamingChunk chunk in finalResponse.Chunks.WithCancellation(token).ConfigureAwait(false))
            {
                if (!string.IsNullOrEmpty(chunk.Text))
                {
                    Console.Write(chunk.Text);
                }
            }

            Console.WriteLine("");
            PrintToolCalls(finalResponse.ToolCalls);
            PrintToolChatStreamingMetadata(finalResponse);
        }

        private static ToolChatRequest BuildWeatherToolRequest(string prompt)
        {
            ToolChatRequest request = new ToolChatRequest();

            if (!string.IsNullOrWhiteSpace(_Client.SystemPrompt))
            {
                request.Messages.Add(ChatMessage.System(_Client.SystemPrompt));
            }

            request.Messages.Add(ChatMessage.User(prompt));
            request.Tools.Add(ToolDefinition.Function(
                "get_weather",
                "Get current weather for a city.",
                BuildWeatherParameters()));
            request.ToolChoice = "auto";
            return request;
        }

        private static Dictionary<string, object> BuildWeatherParameters()
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

        private static void AppendToolResults(ToolChatRequest request, List<ToolCall> toolCalls)
        {
            foreach (ToolCall call in toolCalls)
            {
                Console.WriteLine("");
                Console.WriteLine("Tool call: " + call.Name);
                if (!string.IsNullOrWhiteSpace(call.Id)) Console.WriteLine("  ID        : " + call.Id);
                if (!string.IsNullOrWhiteSpace(call.ArgumentsJson)) Console.WriteLine("  Arguments : " + call.ArgumentsJson);

                string defaultResult = "{\"temperature\":72,\"conditions\":\"clear\"}";
                string? resultJson = Inputty.GetString("Result JSON [" + defaultResult + "]:", defaultResult, true);
                string content = string.IsNullOrWhiteSpace(resultJson) ? defaultResult : resultJson;
                request.Messages.Add(ChatMessage.ToolResult(call.Id, call.Name, content));
            }
        }

        private static void PrintToolChatText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                Console.WriteLine("(no assistant text)");
            }
            else
            {
                Console.WriteLine(text);
            }
        }

        private static void PrintToolCalls(List<ToolCall> toolCalls)
        {
            if (!toolCalls.Any()) return;

            Console.WriteLine("");
            Console.WriteLine("--- Tool calls ---");

            int index = 0;
            foreach (ToolCall call in toolCalls)
            {
                Console.WriteLine("  [" + index + "] " + call.Name);
                if (!string.IsNullOrWhiteSpace(call.Id)) Console.WriteLine("      ID        : " + call.Id);
                Console.WriteLine("      Arguments : " + call.ArgumentsJson);
                index++;
            }
        }

        private static void PrintToolChatMetadata(ToolChatResponse response)
        {
            Console.WriteLine("");
            Console.WriteLine("--- Metadata ---");
            Console.WriteLine("  Model           : " + response.Model);
            if (response.ResponseId != null) Console.WriteLine("  Response ID     : " + response.ResponseId);
            if (response.FinishReason != null) Console.WriteLine("  Finish reason   : " + response.FinishReason);
            if (response.StatusCode.HasValue) Console.WriteLine("  HTTP status     : " + response.StatusCode.Value);
            Console.WriteLine("--- Timing ---");
            Console.WriteLine("  Runtime         : " + response.OverallRuntimeMs + " ms");
        }

        private static void PrintToolChatStreamingMetadata(ToolChatStreamingResponse response)
        {
            Console.WriteLine("");
            Console.WriteLine("--- Metadata ---");
            Console.WriteLine("  Model           : " + response.Model);
            if (response.ResponseId != null) Console.WriteLine("  Response ID     : " + response.ResponseId);
            if (response.FinishReason != null) Console.WriteLine("  Finish reason   : " + response.FinishReason);
            if (response.StatusCode.HasValue) Console.WriteLine("  HTTP status     : " + response.StatusCode.Value);
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
            Console.WriteLine("  Tool deltas     : " + response.ToolCallDeltaCount);
            if (response.OverallTokensPerSecond > 0) Console.WriteLine("  Overall tok/sec : " + response.OverallTokensPerSecond.ToString("F1"));
            if (response.InterTokenTokensPerSecond > 0) Console.WriteLine("  Stream tok/sec  : " + response.InterTokenTokensPerSecond.ToString("F1"));
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
            catch (NotSupportedException ex)
            {
                Console.WriteLine("Not supported: " + ex.Message);
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
            catch (NotSupportedException ex)
            {
                Console.WriteLine("Not supported: " + ex.Message);
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
