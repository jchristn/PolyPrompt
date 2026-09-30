namespace PolyPrompt.Clients
{
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Text.Json;
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using SyslogLogging;

    /// <summary>
    /// OpenAI completion client: chat and tool chat through <c>/v1/chat/completions</c>, and generation through the legacy
    /// <c>/v1/completions</c>, each streaming (SSE) or not. Works with any OpenAI-compatible server, including Ollama's
    /// <c>/v1</c> API, vLLM, and Gemini's OpenAI-compatible endpoint (whose tool-call thought signatures are read from and
    /// replayed in <c>extra_content.google.thought_signature</c>).
    /// </summary>
    public class OpenAiCompletionClient : CompletionClientBase
    {
        #region Private-Members

        private const string ReasoningContentKey = "reasoning_content";
        private const string ReasoningKey = "reasoning";

        #endregion

        #region Public-Members

        /// <summary>
        /// Client-wide default settings, including OpenAI settings such as penalties and seed. Default model: gpt-4o-mini.
        /// </summary>
        public override OpenAiCompletionOptions Defaults { get; } = new OpenAiCompletionOptions { Model = "gpt-4o-mini" };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new OpenAI completion client.
        /// </summary>
        /// <param name="endpoint">API endpoint URL; <c>/v1</c> is appended unless present. Default: https://api.openai.com.</param>
        /// <param name="apiKey">API key, sent as a bearer token. Default: null.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        /// <param name="httpClient">Optional HTTP client; see <see cref="ClientBase"/>. Default: null.</param>
        public OpenAiCompletionClient(
            string endpoint = OpenAiProtocol.DefaultEndpoint,
            string? apiKey = null,
            LoggingModule? logging = null,
            HttpClient? httpClient = null)
            : base(endpoint, apiKey, logging ?? new LoggingModule(), httpClient)
        {
            _Header = OpenAiProtocol.Header;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override Task<bool> ValidateConnectivityAsync(CancellationToken token = default)
        {
            return ProbeAsync(BuildApiUrl("models", null), token);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Build the URL for an operation. Azure OpenAI overrides this to route by deployment.
        /// </summary>
        /// <param name="path">Operation path, for example <c>chat/completions</c> or <c>models</c>.</param>
        /// <param name="model">The model for this call, or null for operations that are not model-scoped.</param>
        /// <returns>The absolute URL.</returns>
        protected virtual string BuildApiUrl(string path, string? model)
        {
            return OpenAiProtocol.BuildApiUrl(_Endpoint, path);
        }

        /// <inheritdoc />
        protected override Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
        {
            OpenAiProtocol.ApplyAuth(request, _ApiKey);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override Task<ChatResponse> ChatCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token)
        {
            ChatResponse response = new ChatResponse { Model = settings.Model };
            return ExecutePostAsync(response, "chat", BuildApiUrl("chat/completions", settings.Model), BuildChatBody(prompt, settings, false), ParseChat, token);
        }

        /// <inheritdoc />
        protected override Task<ChatStreamingResponse> ChatStreamingCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token)
        {
            ChatStreamingResponse response = new ChatStreamingResponse { Model = settings.Model };
            return ExecuteStreamingAsync(response, "chat", BuildApiUrl("chat/completions", settings.Model), BuildChatBody(prompt, settings, true),
                (stream, sw) => response.Chunks = WrapChunksWithTiming(response, ReadChatChunks(stream.Response, stream.Token), sw, stream.Token, stream),
                token);
        }

        /// <inheritdoc />
        protected override Task<ToolChatResponse> ToolChatCoreAsync(ToolChatRequest request, List<ChatMessage> messages, ResolvedCompletion settings, CancellationToken token)
        {
            ToolChatResponse response = new ToolChatResponse { Model = settings.Model };
            return ExecutePostAsync(response, "tool chat", BuildApiUrl("chat/completions", settings.Model), BuildToolChatBody(request, messages, settings, false), ParseToolChat, token);
        }

        /// <inheritdoc />
        protected override Task<ToolChatStreamingResponse> ToolChatStreamingCoreAsync(ToolChatRequest request, List<ChatMessage> messages, ResolvedCompletion settings, CancellationToken token)
        {
            ToolChatStreamingResponse response = new ToolChatStreamingResponse { Model = settings.Model };
            return ExecuteStreamingAsync(response, "tool chat", BuildApiUrl("chat/completions", settings.Model), BuildToolChatBody(request, messages, settings, true),
                (stream, sw) => response.Chunks = WrapToolChatChunksWithTiming(response, ReadToolChatChunks(stream.Response, stream.Token), sw, stream.Token, stream),
                token);
        }

        /// <inheritdoc />
        protected override Task<GenerationResponse> GenerateCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token)
        {
            GenerationResponse response = new GenerationResponse { Model = settings.Model };
            return ExecutePostAsync(response, "generate", BuildApiUrl("completions", settings.Model), BuildGenerateBody(prompt, settings, false), ParseGenerate, token);
        }

        /// <inheritdoc />
        protected override Task<GenerationStreamingResponse> GenerateStreamingCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token)
        {
            GenerationStreamingResponse response = new GenerationStreamingResponse { Model = settings.Model };
            return ExecuteStreamingAsync(response, "generate", BuildApiUrl("completions", settings.Model), BuildGenerateBody(prompt, settings, true),
                (stream, sw) => response.Chunks = WrapGenerationChunksWithTiming(response, ReadGenerateChunks(stream.Response, stream.Token), sw, stream.Token, stream),
                token);
        }

        #endregion

        #region Private-Methods

        private void ApplySampling(Dictionary<string, object> body, ResolvedCompletion settings)
        {
            if (settings.Temperature.HasValue) body["temperature"] = settings.Temperature.Value;
            if (settings.TopP.HasValue) body["top_p"] = settings.TopP.Value;

            double? frequencyPenalty = Pick<OpenAiCompletionOptions, double>(settings.Call, Defaults, o => o.FrequencyPenalty);
            double? presencePenalty = Pick<OpenAiCompletionOptions, double>(settings.Call, Defaults, o => o.PresencePenalty);
            int? seed = Pick<OpenAiCompletionOptions, int>(settings.Call, Defaults, o => o.Seed);

            if (frequencyPenalty.HasValue) body["frequency_penalty"] = frequencyPenalty.Value;
            if (presencePenalty.HasValue) body["presence_penalty"] = presencePenalty.Value;
            if (seed.HasValue) body["seed"] = seed.Value;
        }

        private static void ApplyStreaming(Dictionary<string, object> body, bool stream, bool includeUsage)
        {
            if (!stream) return;
            body["stream"] = true;
            if (includeUsage) body["stream_options"] = new Dictionary<string, object> { { "include_usage", true } };
        }

        /// <summary>
        /// Adds the OpenAI <c>reasoning_effort</c> field for a reasoning effort; null leaves the request unchanged.
        /// </summary>
        private static void ApplyReasoning(Dictionary<string, object> body, ReasoningEffort? reasoningEffort)
        {
            if (reasoningEffort == null) return;
            body["reasoning_effort"] = reasoningEffort.ToOpenAiWireValue();
        }

        private Dictionary<string, object> BuildChatBody(string prompt, ResolvedCompletion settings, bool stream)
        {
            List<Dictionary<string, string>> messages = new List<Dictionary<string, string>>();
            if (!string.IsNullOrEmpty(settings.SystemPrompt))
            {
                messages.Add(new Dictionary<string, string> { { "role", "system" }, { "content", settings.SystemPrompt } });
            }
            messages.Add(new Dictionary<string, string> { { "role", "user" }, { "content", prompt } });

            Dictionary<string, object> body = new Dictionary<string, object>
            {
                { "model", settings.Model },
                { "messages", messages },
                { "max_tokens", settings.MaxTokens }
            };

            ApplySampling(body, settings);
            ApplyReasoning(body, settings.ReasoningEffort);
            ApplyStreaming(body, stream, true);
            return body;
        }

        private Dictionary<string, object> BuildToolChatBody(ToolChatRequest request, List<ChatMessage> messages, ResolvedCompletion settings, bool stream)
        {
            Dictionary<string, object> body = new Dictionary<string, object>
            {
                { "model", settings.Model },
                { "messages", BuildMessages(messages) },
                { "max_tokens", settings.MaxTokens }
            };

            ApplySampling(body, settings);

            if (request.Tools != null && request.Tools.Count > 0 && !IsToolChoiceNone(request.ToolChoice))
            {
                body["tools"] = BuildTools(request.Tools);
            }

            if (!string.IsNullOrWhiteSpace(request.ToolChoice))
            {
                body["tool_choice"] = request.ToolChoice;
            }

            ApplyReasoning(body, settings.ReasoningEffort);
            ApplyStreaming(body, stream, true);
            return body;
        }

        private Dictionary<string, object> BuildGenerateBody(string prompt, ResolvedCompletion settings, bool stream)
        {
            Dictionary<string, object> body = new Dictionary<string, object>
            {
                { "model", settings.Model },
                { "prompt", prompt },
                { "max_tokens", settings.MaxTokens }
            };

            ApplySampling(body, settings);

            bool? echo = Pick<OpenAiCompletionOptions, bool>(settings.Call, Defaults, o => o.Echo);
            string? suffix = PickRef<OpenAiCompletionOptions, string>(settings.Call, Defaults, o => o.Suffix);
            int? logprobs = Pick<OpenAiCompletionOptions, int>(settings.Call, Defaults, o => o.Logprobs);

            if (echo.HasValue) body["echo"] = echo.Value;
            if (!string.IsNullOrEmpty(suffix)) body["suffix"] = suffix;
            if (logprobs.HasValue) body["logprobs"] = logprobs.Value;

            ApplyStreaming(body, stream, false);
            return body;
        }

        private List<Dictionary<string, object>> BuildMessages(List<ChatMessage> messages)
        {
            List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();

            foreach (ChatMessage message in messages)
            {
                Dictionary<string, object> item = new Dictionary<string, object>
                {
                    { "role", NormalizeRole(message.Role) },
                    { "content", message.Content ?? string.Empty }
                };

                if (!string.IsNullOrEmpty(message.ToolCallId))
                {
                    item["tool_call_id"] = message.ToolCallId;
                }

                if (message.ToolCalls != null && message.ToolCalls.Count > 0)
                {
                    item["tool_calls"] = BuildToolCalls(message.ToolCalls);
                }

                result.Add(item);
            }

            return result;
        }

        private static List<Dictionary<string, object>> BuildTools(List<ToolDefinition> tools)
        {
            List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();

            foreach (ToolDefinition tool in tools)
            {
                Dictionary<string, object> function = new Dictionary<string, object>
                {
                    { "name", tool.Name },
                    { "description", tool.Description },
                    { "parameters", tool.Parameters }
                };

                result.Add(new Dictionary<string, object>
                {
                    { "type", string.IsNullOrWhiteSpace(tool.Type) ? "function" : tool.Type },
                    { "function", function }
                });
            }

            return result;
        }

        private static List<Dictionary<string, object>> BuildToolCalls(List<ToolCall> toolCalls)
        {
            List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();

            foreach (ToolCall toolCall in toolCalls)
            {
                Dictionary<string, object> function = new Dictionary<string, object>
                {
                    { "name", toolCall.Name },
                    { "arguments", string.IsNullOrWhiteSpace(toolCall.ArgumentsJson) ? "{}" : toolCall.ArgumentsJson }
                };

                Dictionary<string, object> item = new Dictionary<string, object>
                {
                    { "id", toolCall.Id ?? string.Empty },
                    { "type", "function" },
                    { "function", function }
                };

                // Gemini's OpenAI-compatible endpoint carries its thought signature in a vendor extension. It is
                // only emitted when present, so requests to other OpenAI-compatible servers are unchanged.
                if (!string.IsNullOrEmpty(toolCall.ThoughtSignature))
                {
                    item["extra_content"] = new Dictionary<string, object>
                    {
                        { "google", new Dictionary<string, object> { { "thought_signature", toolCall.ThoughtSignature } } }
                    };
                }

                result.Add(item);
            }

            return result;
        }

        private static string NormalizeRole(string? role)
        {
            if (string.Equals(role, "model", StringComparison.OrdinalIgnoreCase)) return "assistant";
            if (string.Equals(role, "function", StringComparison.OrdinalIgnoreCase)) return "tool";
            return string.IsNullOrWhiteSpace(role) ? "user" : role.ToLowerInvariant();
        }

        private Dictionary<string, object>? FirstChoice(Dictionary<string, object> responseObj, ResponseBase response)
        {
            if (!responseObj.ContainsKey("choices"))
            {
                response.Error = "Response missing 'choices' field";
                return null;
            }

            List<Dictionary<string, object>>? choices = ParseNestedList(responseObj, "choices");
            if (choices == null || choices.Count == 0)
            {
                response.Error = "Response has empty choices array";
                return null;
            }

            return choices[0];
        }

        private void ParseChat(string responseBody, ChatResponse response)
        {
            Dictionary<string, object>? responseObj = TryDeserializeObject(responseBody);
            if (responseObj == null)
            {
                response.Error = "Response missing 'choices' field";
                return;
            }

            Dictionary<string, object>? choice = FirstChoice(responseObj, response);
            if (choice == null) return;

            Dictionary<string, object>? message = ParseNestedObject(choice, "message");
            if (message == null)
            {
                response.Error = "Response choice missing 'message' field";
                return;
            }

            if (!message.ContainsKey("content"))
            {
                response.Error = "Response message missing 'content' field";
                return;
            }

            string? text = message["content"]?.ToString();
            response.Text = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            response.Reasoning = ReadReasoning(message);
            response.Usage = ParseUsage(ParseNestedObject(responseObj, "usage"));
            if (responseObj.ContainsKey("model")) response.Model = responseObj["model"]?.ToString() ?? response.Model;
        }

        private void ParseToolChat(string responseBody, ToolChatResponse response)
        {
            Dictionary<string, object>? responseObj = TryDeserializeObject(responseBody);
            if (responseObj == null)
            {
                response.Error = "Response missing 'choices' field";
                return;
            }

            response.ResponseId = responseObj.ContainsKey("id") ? responseObj["id"]?.ToString() : null;
            response.Usage = ParseUsage(ParseNestedObject(responseObj, "usage"));
            if (responseObj.ContainsKey("model")) response.Model = responseObj["model"]?.ToString() ?? response.Model;

            Dictionary<string, object>? choice = FirstChoice(responseObj, response);
            if (choice == null) return;

            response.FinishReason = choice.ContainsKey("finish_reason") ? choice["finish_reason"]?.ToString() : null;

            if (!choice.ContainsKey("message"))
            {
                response.Error = "Response choice missing 'message' field";
                return;
            }

            Dictionary<string, object>? message = ParseNestedObject(choice, "message");
            if (message == null)
            {
                response.Error = "Response message could not be parsed";
                return;
            }

            if (message.ContainsKey("content"))
            {
                string? text = message["content"]?.ToString();
                response.Text = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            }

            response.Reasoning = ReadReasoning(message);

            List<Dictionary<string, object>>? toolCalls = ParseNestedList(message, "tool_calls");
            if (toolCalls != null)
            {
                foreach (Dictionary<string, object> toolCallObj in toolCalls)
                {
                    ToolCall? toolCall = ParseToolCall(toolCallObj);
                    if (toolCall != null) response.ToolCalls.Add(toolCall);
                }
            }
        }

        private void ParseGenerate(string responseBody, GenerationResponse response)
        {
            Dictionary<string, object>? responseObj = TryDeserializeObject(responseBody);
            List<Dictionary<string, object>>? choices = responseObj == null ? null : ParseNestedList(responseObj, "choices");
            if (choices != null && choices.Count > 0 && choices[0].ContainsKey("text"))
            {
                string? text = choices[0]["text"]?.ToString();
                response.Text = string.IsNullOrWhiteSpace(text) ? null : text;
            }
        }

        private ToolCall? ParseToolCall(Dictionary<string, object> toolCallObj)
        {
            Dictionary<string, object>? function = ParseNestedObject(toolCallObj, "function");
            if (function == null || !function.ContainsKey("name")) return null;

            ToolCall toolCall = new ToolCall();
            toolCall.Id = toolCallObj.ContainsKey("id") ? toolCallObj["id"]?.ToString() : null;
            toolCall.Name = function["name"]?.ToString() ?? string.Empty;
            toolCall.ArgumentsJson = function.ContainsKey("arguments") && function["arguments"] != null
                ? function["arguments"]?.ToString() ?? "{}"
                : "{}";
            toolCall.ThoughtSignature = ReadGoogleThoughtSignature(toolCallObj);
            return toolCall;
        }

        private static string? ReadGoogleThoughtSignature(Dictionary<string, object> toolCallObj)
        {
            // extra_content.google.thought_signature, emitted by Gemini's OpenAI-compatible endpoint. Any other
            // shape is ignored rather than failing the parse.
            if (!toolCallObj.TryGetValue("extra_content", out object? extraContent)
                || extraContent is not JsonElement extra
                || extra.ValueKind != JsonValueKind.Object
                || !extra.TryGetProperty("google", out JsonElement google)
                || google.ValueKind != JsonValueKind.Object
                || !google.TryGetProperty("thought_signature", out JsonElement signature)
                || signature.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            string? value = signature.GetString();
            return string.IsNullOrEmpty(value) ? null : value;
        }

        private static string? ReadReasoning(Dictionary<string, object> obj)
        {
            // Reasoning models on the OpenAI-compatible surface use "reasoning_content"; some servers use
            // "reasoning". Read either and normalize an empty value to null.
            string? value = ReadString(obj, ReasoningContentKey) ?? ReadString(obj, ReasoningKey);
            return NormalizeReasoning(value);
        }

        private static string? ReadString(Dictionary<string, object> obj, string key)
        {
            if (!obj.ContainsKey(key) || obj[key] == null) return null;
            return obj[key].ToString();
        }

        // Parse an OpenAI-shape usage object. Shared by the streaming chunk readers and the non-streaming
        // response parsers so telemetry is identical across both paths. cached_tokens and reasoning_tokens
        // live in nested detail objects and are absent unless the model reports them.
        private TokenUsage? ParseUsage(Dictionary<string, object>? usageObj)
        {
            if (usageObj == null) return null;

            TokenUsage usage = new TokenUsage();
            usage.PromptTokens = TryGetInt(usageObj, "prompt_tokens");
            usage.CompletionTokens = TryGetInt(usageObj, "completion_tokens");
            usage.TotalTokens = TryGetInt(usageObj, "total_tokens");

            Dictionary<string, object>? promptDetails = ParseNestedObject(usageObj, "prompt_tokens_details");
            if (promptDetails != null) usage.CachedPromptTokens = TryGetInt(promptDetails, "cached_tokens");

            Dictionary<string, object>? completionDetails = ParseNestedObject(usageObj, "completion_tokens_details");
            if (completionDetails != null) usage.ReasoningTokens = TryGetInt(completionDetails, "reasoning_tokens");

            return usage;
        }

        private async IAsyncEnumerable<Dictionary<string, object>?> ReadEvents(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken token)
        {
            using Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using StreamReader reader = new StreamReader(stream);

            string? line;
            while ((line = await reader.ReadLineAsync(token).ConfigureAwait(false)) != null)
            {
                token.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

                string data = line.Substring(5).TrimStart();
                if (data == "[DONE]")
                {
                    yield return null;
                    yield break;
                }

                Dictionary<string, object>? chunk = TryDeserializeObject(data);
                if (chunk != null) yield return chunk;
            }
        }

        private static DateTime? ParseCreated(Dictionary<string, object> chunk)
        {
            long? created = TryGetLong(chunk, "created");
            return created.HasValue ? DateTimeOffset.FromUnixTimeSeconds(created.Value).UtcDateTime : null;
        }

        private async IAsyncEnumerable<ChatStreamingChunk> ReadChatChunks(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken token)
        {
            await foreach (Dictionary<string, object>? chunk in ReadEvents(response, token).ConfigureAwait(false))
            {
                if (chunk == null)
                {
                    yield return new ChatStreamingChunk { Done = true };
                    yield break;
                }

                ChatStreamingChunk streamChunk = new ChatStreamingChunk();
                streamChunk.ResponseId = chunk.ContainsKey("id") ? chunk["id"]?.ToString() : null;
                streamChunk.Model = chunk.ContainsKey("model") ? chunk["model"]?.ToString() : null;
                DateTime? created = ParseCreated(chunk);
                if (created.HasValue) streamChunk.CreatedUtc = created.Value;

                List<Dictionary<string, object>>? choices = ParseNestedList(chunk, "choices");
                if (choices != null && choices.Count > 0)
                {
                    Dictionary<string, object> choice = choices[0];
                    if (choice.ContainsKey("finish_reason") && choice["finish_reason"] != null)
                    {
                        streamChunk.FinishReason = choice["finish_reason"].ToString();
                        streamChunk.Done = true;
                    }

                    Dictionary<string, object>? delta = ParseNestedObject(choice, "delta");
                    if (delta != null)
                    {
                        if (delta.ContainsKey("content")) streamChunk.Text = delta["content"]?.ToString();
                        streamChunk.ReasoningText = ReadReasoning(delta);
                    }
                }

                if (chunk.ContainsKey("usage") && chunk["usage"] != null)
                {
                    streamChunk.Usage = ParseUsage(ParseNestedObject(chunk, "usage"));
                }

                yield return streamChunk;
            }
        }

        private async IAsyncEnumerable<ToolChatStreamingChunk> ReadToolChatChunks(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken token)
        {
            await foreach (Dictionary<string, object>? chunk in ReadEvents(response, token).ConfigureAwait(false))
            {
                if (chunk == null)
                {
                    yield return new ToolChatStreamingChunk { Done = true };
                    yield break;
                }

                ToolChatStreamingChunk streamChunk = new ToolChatStreamingChunk();
                streamChunk.ResponseId = chunk.ContainsKey("id") ? chunk["id"]?.ToString() : null;
                streamChunk.Model = chunk.ContainsKey("model") ? chunk["model"]?.ToString() : null;
                DateTime? created = ParseCreated(chunk);
                if (created.HasValue) streamChunk.CreatedUtc = created.Value;

                List<Dictionary<string, object>>? choices = ParseNestedList(chunk, "choices");
                if (choices != null && choices.Count > 0)
                {
                    Dictionary<string, object> choice = choices[0];
                    if (choice.ContainsKey("finish_reason") && choice["finish_reason"] != null)
                    {
                        streamChunk.FinishReason = choice["finish_reason"]?.ToString();
                        streamChunk.Done = true;
                    }

                    Dictionary<string, object>? delta = ParseNestedObject(choice, "delta");
                    if (delta != null)
                    {
                        if (delta.ContainsKey("content")) streamChunk.Text = delta["content"]?.ToString();
                        streamChunk.ReasoningText = ReadReasoning(delta);

                        List<Dictionary<string, object>>? toolCalls = ParseNestedList(delta, "tool_calls");
                        if (toolCalls != null) streamChunk.ToolCallDeltas.AddRange(ParseToolCallDeltas(toolCalls));
                    }
                }

                if (chunk.ContainsKey("usage") && chunk["usage"] != null)
                {
                    streamChunk.Usage = ParseUsage(ParseNestedObject(chunk, "usage"));
                }

                yield return streamChunk;
            }
        }

        private List<ToolCallDelta> ParseToolCallDeltas(List<Dictionary<string, object>> toolCalls)
        {
            List<ToolCallDelta> result = new List<ToolCallDelta>();

            int fallbackIndex = 0;
            foreach (Dictionary<string, object> toolCallObj in toolCalls)
            {
                ToolCallDelta delta = new ToolCallDelta();
                delta.Index = TryGetInt(toolCallObj, "index") ?? fallbackIndex;
                delta.Id = toolCallObj.ContainsKey("id") ? toolCallObj["id"]?.ToString() : null;
                delta.Type = toolCallObj.ContainsKey("type") ? toolCallObj["type"]?.ToString() : null;
                delta.ThoughtSignature = ReadGoogleThoughtSignature(toolCallObj);

                Dictionary<string, object>? function = ParseNestedObject(toolCallObj, "function");
                if (function != null)
                {
                    delta.Name = function.ContainsKey("name") ? function["name"]?.ToString() : null;
                    if (function.ContainsKey("arguments")) delta.ArgumentsJsonDelta = function["arguments"]?.ToString();
                }

                result.Add(delta);
                fallbackIndex++;
            }

            return result;
        }

        private async IAsyncEnumerable<GenerationStreamingChunk> ReadGenerateChunks(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken token)
        {
            await foreach (Dictionary<string, object>? chunk in ReadEvents(response, token).ConfigureAwait(false))
            {
                if (chunk == null)
                {
                    yield return new GenerationStreamingChunk { Done = true };
                    yield break;
                }

                GenerationStreamingChunk streamChunk = new GenerationStreamingChunk();
                streamChunk.Model = chunk.ContainsKey("model") ? chunk["model"]?.ToString() : null;

                List<Dictionary<string, object>>? choices = ParseNestedList(chunk, "choices");
                if (choices != null && choices.Count > 0)
                {
                    Dictionary<string, object> choice = choices[0];
                    if (choice.ContainsKey("text")) streamChunk.Text = choice["text"]?.ToString();
                    if (choice.ContainsKey("finish_reason") && choice["finish_reason"] != null) streamChunk.Done = true;
                }

                yield return streamChunk;
            }
        }

        #endregion
    }
}
