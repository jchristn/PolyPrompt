namespace PolyPrompt.Clients
{
    using System.Diagnostics;
    using System.IO;
    using System.Runtime.CompilerServices;
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using SyslogLogging;

    /// <summary>
    /// Ollama completion client: chat and tool chat through <c>/api/chat</c>, and generation through <c>/api/generate</c>,
    /// each streaming (newline-delimited JSON) or not. Ollama settings (<see cref="OllamaCompletionOptions"/>) map to the
    /// request's <c>options</c> object for every operation.
    /// </summary>
    public class OllamaCompletionClient : CompletionClientBase
    {
        #region Private-Members

        private const string ThinkingKey = "thinking";

        #endregion

        #region Public-Members

        /// <summary>
        /// Client-wide default settings, including Ollama settings such as <see cref="OllamaCompletionOptions.ContextLength"/>.
        /// Default model: gemma3:4b.
        /// </summary>
        public override OllamaCompletionOptions Defaults { get; } = new OllamaCompletionOptions { Model = "gemma3:4b" };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new Ollama completion client.
        /// </summary>
        /// <param name="endpoint">Ollama server endpoint URL. Default: http://localhost:11434.</param>
        /// <param name="apiKey">Optional bearer token, for Ollama servers behind an authenticating proxy. Default: null.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        /// <param name="httpClient">Optional HTTP client; see <see cref="ClientBase"/>. Default: null.</param>
        public OllamaCompletionClient(
            string endpoint = OllamaProtocol.DefaultEndpoint,
            string? apiKey = null,
            LoggingModule? logging = null,
            HttpClient? httpClient = null)
            : base(endpoint, apiKey, logging ?? new LoggingModule(), httpClient)
        {
            _Header = OllamaProtocol.Header;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override Task<bool> ValidateConnectivityAsync(CancellationToken token = default)
        {
            return ProbeAsync(BuildUrl(OllamaProtocol.TagsPath), token);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
        {
            OllamaProtocol.ApplyAuth(request, _ApiKey);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override Task<ChatResponse> ChatCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token)
        {
            ChatResponse response = new ChatResponse { Model = settings.Model };
            return ExecutePostAsync(response, "chat", BuildUrl("/api/chat"), BuildChatBody(prompt, settings, false), ParseChat, token);
        }

        /// <inheritdoc />
        protected override Task<ChatStreamingResponse> ChatStreamingCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token)
        {
            ChatStreamingResponse response = new ChatStreamingResponse { Model = settings.Model };
            return ExecuteStreamingAsync(response, "chat", BuildUrl("/api/chat"), BuildChatBody(prompt, settings, true),
                (stream, sw) => response.Chunks = WrapChunksWithTiming(response, ReadChatChunks(stream.Response, stream.Token), sw, stream.Token, stream),
                token);
        }

        /// <inheritdoc />
        protected override Task<ToolChatResponse> ToolChatCoreAsync(ToolChatRequest request, List<ChatMessage> messages, ResolvedCompletion settings, CancellationToken token)
        {
            ToolChatResponse response = new ToolChatResponse { Model = settings.Model };
            return ExecutePostAsync(response, "tool chat", BuildUrl("/api/chat"), BuildToolChatBody(request, messages, settings, false), ParseToolChat, token);
        }

        /// <inheritdoc />
        protected override Task<ToolChatStreamingResponse> ToolChatStreamingCoreAsync(ToolChatRequest request, List<ChatMessage> messages, ResolvedCompletion settings, CancellationToken token)
        {
            ToolChatStreamingResponse response = new ToolChatStreamingResponse { Model = settings.Model };
            return ExecuteStreamingAsync(response, "tool chat", BuildUrl("/api/chat"), BuildToolChatBody(request, messages, settings, true),
                (stream, sw) => response.Chunks = WrapToolChatChunksWithTiming(response, ReadToolChatChunks(stream.Response, stream.Token), sw, stream.Token, stream),
                token);
        }

        /// <inheritdoc />
        protected override Task<GenerationResponse> GenerateCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token)
        {
            GenerationResponse response = new GenerationResponse { Model = settings.Model };
            return ExecutePostAsync(response, "generate", BuildUrl("/api/generate"), BuildGenerateBody(prompt, settings, false), ParseGenerate, token);
        }

        /// <inheritdoc />
        protected override Task<GenerationStreamingResponse> GenerateStreamingCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token)
        {
            GenerationStreamingResponse response = new GenerationStreamingResponse { Model = settings.Model };
            return ExecuteStreamingAsync(response, "generate", BuildUrl("/api/generate"), BuildGenerateBody(prompt, settings, true),
                (stream, sw) => response.Chunks = WrapGenerationChunksWithTiming(response, ReadGenerateChunks(stream.Response, stream.Token), sw, stream.Token, stream),
                token);
        }

        #endregion

        #region Private-Methods

        private Dictionary<string, object> BuildModelOptions(ResolvedCompletion settings)
        {
            Dictionary<string, object> modelOptions = new Dictionary<string, object>();
            modelOptions["num_predict"] = settings.MaxTokens;

            int? contextLength = Pick<OllamaCompletionOptions, int>(settings.Call, Defaults, o => o.ContextLength);
            int? topK = Pick<OllamaCompletionOptions, int>(settings.Call, Defaults, o => o.TopK);
            double? repeatPenalty = Pick<OllamaCompletionOptions, double>(settings.Call, Defaults, o => o.RepeatPenalty);
            int? seed = Pick<OllamaCompletionOptions, int>(settings.Call, Defaults, o => o.Seed);
            double? minP = Pick<OllamaCompletionOptions, double>(settings.Call, Defaults, o => o.MinP);
            int? repeatLastN = Pick<OllamaCompletionOptions, int>(settings.Call, Defaults, o => o.RepeatLastN);

            if (contextLength.HasValue) modelOptions["num_ctx"] = contextLength.Value;
            if (settings.Temperature.HasValue) modelOptions["temperature"] = settings.Temperature.Value;
            if (settings.TopP.HasValue) modelOptions["top_p"] = settings.TopP.Value;
            if (topK.HasValue) modelOptions["top_k"] = topK.Value;
            if (repeatPenalty.HasValue) modelOptions["repeat_penalty"] = repeatPenalty.Value;
            if (seed.HasValue) modelOptions["seed"] = seed.Value;
            if (minP.HasValue) modelOptions["min_p"] = minP.Value;
            if (repeatLastN.HasValue) modelOptions["repeat_last_n"] = repeatLastN.Value;

            return modelOptions;
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
                { "stream", stream },
                { "options", BuildModelOptions(settings) }
            };

            ApplyReasoning(body, settings.ReasoningEffort);
            return body;
        }

        private Dictionary<string, object> BuildToolChatBody(ToolChatRequest request, List<ChatMessage> messages, ResolvedCompletion settings, bool stream)
        {
            Dictionary<string, object> body = new Dictionary<string, object>
            {
                { "model", settings.Model },
                { "messages", BuildMessages(messages) },
                { "stream", stream },
                { "options", BuildModelOptions(settings) }
            };

            ApplyReasoning(body, settings.ReasoningEffort);

            if (request.Tools != null && request.Tools.Count > 0 && !IsToolChoiceNone(request.ToolChoice))
            {
                body["tools"] = BuildTools(request.Tools);
            }

            return body;
        }

        private Dictionary<string, object> BuildGenerateBody(string prompt, ResolvedCompletion settings, bool stream)
        {
            return new Dictionary<string, object>
            {
                { "model", settings.Model },
                { "prompt", prompt },
                { "stream", stream },
                { "options", BuildModelOptions(settings) }
            };
        }

        /// <summary>
        /// Adds the Ollama <c>think</c> field for a reasoning effort; null leaves the request unchanged.
        /// </summary>
        private static void ApplyReasoning(Dictionary<string, object> body, ReasoningEffort? reasoningEffort)
        {
            if (reasoningEffort == null) return;
            body["think"] = reasoningEffort.ToOllamaThink();
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

        private List<Dictionary<string, object>> BuildToolCalls(List<ToolCall> toolCalls)
        {
            List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();

            foreach (ToolCall toolCall in toolCalls)
            {
                Dictionary<string, object> function = new Dictionary<string, object>
                {
                    { "name", toolCall.Name },
                    { "arguments", DeserializeDictionaryOrEmpty(toolCall.ArgumentsJson) }
                };

                result.Add(new Dictionary<string, object> { { "function", function } });
            }

            return result;
        }

        private static string NormalizeRole(string? role)
        {
            if (string.Equals(role, "model", StringComparison.OrdinalIgnoreCase)) return "assistant";
            if (string.Equals(role, "function", StringComparison.OrdinalIgnoreCase)) return "tool";
            return string.IsNullOrWhiteSpace(role) ? "user" : role.ToLowerInvariant();
        }

        private void ParseChat(string responseBody, ChatResponse response)
        {
            Dictionary<string, object>? responseObj = TryDeserializeObject(responseBody);
            Dictionary<string, object>? message = responseObj == null ? null : ParseNestedObject(responseObj, "message");
            if (responseObj == null || message == null)
            {
                response.Error = "Response missing 'message' field";
                return;
            }

            if (!message.ContainsKey("content"))
            {
                response.Error = "Response message missing 'content' field";
                return;
            }

            string? completionText = message["content"]?.ToString();
            response.Text = string.IsNullOrWhiteSpace(completionText) ? null : completionText.Trim();
            response.Reasoning = message.ContainsKey(ThinkingKey) ? NormalizeReasoning(message[ThinkingKey]?.ToString()) : null;
            response.Usage = ParseUsage(responseObj);
        }

        private void ParseToolChat(string responseBody, ToolChatResponse response)
        {
            Dictionary<string, object>? responseObj = TryDeserializeObject(responseBody);
            if (responseObj == null || !responseObj.ContainsKey("message"))
            {
                response.Error = "Response missing 'message' field";
                return;
            }

            if (responseObj.ContainsKey("model")) response.Model = responseObj["model"]?.ToString() ?? response.Model;
            response.FinishReason = responseObj.ContainsKey("done_reason") ? responseObj["done_reason"]?.ToString() : null;
            response.Usage = ParseUsage(responseObj);

            Dictionary<string, object>? message = ParseNestedObject(responseObj, "message");
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

            if (message.ContainsKey(ThinkingKey))
            {
                response.Reasoning = NormalizeReasoning(message[ThinkingKey]?.ToString());
            }

            List<Dictionary<string, object>>? toolCalls = ParseNestedList(message, "tool_calls");
            if (toolCalls != null)
            {
                int index = 0;
                foreach (Dictionary<string, object> toolCallObj in toolCalls)
                {
                    ToolCall? toolCall = ParseToolCall(toolCallObj, index);
                    if (toolCall != null) response.ToolCalls.Add(toolCall);
                    index++;
                }
            }
        }

        private void ParseGenerate(string responseBody, GenerationResponse response)
        {
            Dictionary<string, object>? responseObj = TryDeserializeObject(responseBody);
            if (responseObj != null && responseObj.ContainsKey("response"))
            {
                string? text = responseObj["response"]?.ToString();
                response.Text = string.IsNullOrWhiteSpace(text) ? null : text;
            }
        }

        // Ollama reports neither prompt-cache nor a distinct reasoning-token count, so CachedPromptTokens,
        // CacheCreationTokens, and ReasoningTokens stay null; its thinking is surfaced as text only.
        private static TokenUsage ParseUsage(Dictionary<string, object> chunk)
        {
            TokenUsage usage = new TokenUsage();
            usage.PromptTokens = TryGetInt(chunk, "prompt_eval_count");
            usage.CompletionTokens = TryGetInt(chunk, "eval_count");
            usage.TotalDurationNs = TryGetLong(chunk, "total_duration");
            usage.LoadDurationNs = TryGetLong(chunk, "load_duration");
            usage.PromptEvalDurationNs = TryGetLong(chunk, "prompt_eval_duration");
            usage.EvalDurationNs = TryGetLong(chunk, "eval_duration");

            if (usage.PromptTokens.HasValue && usage.CompletionTokens.HasValue)
            {
                usage.TotalTokens = usage.PromptTokens.Value + usage.CompletionTokens.Value;
            }

            return usage;
        }

        private ToolCall? ParseToolCall(Dictionary<string, object> toolCallObj, int index)
        {
            Dictionary<string, object>? function = ParseNestedObject(toolCallObj, "function");
            if (function == null || !function.ContainsKey("name")) return null;

            ToolCall toolCall = new ToolCall();
            toolCall.Id = toolCallObj.ContainsKey("id") ? toolCallObj["id"]?.ToString() : "ollama-call-" + index;
            toolCall.Name = function["name"]?.ToString() ?? string.Empty;
            toolCall.ArgumentsJson = function.ContainsKey("arguments") && function["arguments"] != null
                ? _Serializer.SerializeJson(function["arguments"], false)
                : "{}";
            return toolCall;
        }

        private static DateTime? ParseCreated(Dictionary<string, object> chunk)
        {
            if (!chunk.ContainsKey("created_at")) return null;
            string? createdAt = chunk["created_at"]?.ToString();
            if (!string.IsNullOrEmpty(createdAt) && DateTime.TryParse(createdAt, out DateTime parsed))
                return parsed.ToUniversalTime();
            return null;
        }

        private async IAsyncEnumerable<Dictionary<string, object>> ReadLines(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken token)
        {
            using Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using StreamReader reader = new StreamReader(stream);

            string? line;
            while ((line = await reader.ReadLineAsync(token).ConfigureAwait(false)) != null)
            {
                token.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(line)) continue;

                Dictionary<string, object>? chunk = TryDeserializeObject(line);
                if (chunk != null) yield return chunk;
            }
        }

        private async IAsyncEnumerable<ChatStreamingChunk> ReadChatChunks(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken token)
        {
            await foreach (Dictionary<string, object> chunk in ReadLines(response, token).ConfigureAwait(false))
            {
                ChatStreamingChunk streamChunk = new ChatStreamingChunk();
                streamChunk.Model = chunk.ContainsKey("model") ? chunk["model"]?.ToString() : null;
                DateTime? created = ParseCreated(chunk);
                if (created.HasValue) streamChunk.CreatedUtc = created.Value;

                Dictionary<string, object>? msg = ParseNestedObject(chunk, "message");
                if (msg != null)
                {
                    if (msg.ContainsKey("content")) streamChunk.Text = msg["content"]?.ToString();
                    if (msg.ContainsKey(ThinkingKey)) streamChunk.ReasoningText = NormalizeReasoning(msg[ThinkingKey]?.ToString());
                }

                streamChunk.Done = IsTruthy(chunk, "done");
                if (streamChunk.Done)
                {
                    streamChunk.FinishReason = chunk.ContainsKey("done_reason") ? chunk["done_reason"]?.ToString() : null;
                    streamChunk.Usage = ParseUsage(chunk);
                }

                yield return streamChunk;
            }
        }

        private async IAsyncEnumerable<ToolChatStreamingChunk> ReadToolChatChunks(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken token)
        {
            await foreach (Dictionary<string, object> chunk in ReadLines(response, token).ConfigureAwait(false))
            {
                ToolChatStreamingChunk streamChunk = new ToolChatStreamingChunk();
                streamChunk.Model = chunk.ContainsKey("model") ? chunk["model"]?.ToString() : null;
                DateTime? created = ParseCreated(chunk);
                if (created.HasValue) streamChunk.CreatedUtc = created.Value;

                Dictionary<string, object>? msg = ParseNestedObject(chunk, "message");
                if (msg != null)
                {
                    if (msg.ContainsKey("content")) streamChunk.Text = msg["content"]?.ToString();
                    if (msg.ContainsKey(ThinkingKey)) streamChunk.ReasoningText = NormalizeReasoning(msg[ThinkingKey]?.ToString());

                    List<Dictionary<string, object>>? toolCalls = ParseNestedList(msg, "tool_calls");
                    if (toolCalls != null) streamChunk.ToolCallDeltas.AddRange(ParseToolCallDeltas(toolCalls));
                }

                streamChunk.Done = IsTruthy(chunk, "done");
                if (streamChunk.Done)
                {
                    streamChunk.FinishReason = chunk.ContainsKey("done_reason") ? chunk["done_reason"]?.ToString() : null;
                    streamChunk.Usage = ParseUsage(chunk);
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
                int index = TryGetInt(toolCallObj, "index") ?? fallbackIndex;
                ToolCallDelta delta = new ToolCallDelta();
                delta.Type = toolCallObj.ContainsKey("type") ? toolCallObj["type"]?.ToString() : "function";

                Dictionary<string, object>? function = ParseNestedObject(toolCallObj, "function");
                if (function != null)
                {
                    index = TryGetInt(function, "index") ?? index;
                    delta.Name = function.ContainsKey("name") ? function["name"]?.ToString() : null;

                    if (function.ContainsKey("arguments") && function["arguments"] != null)
                    {
                        if (function["arguments"] is System.Text.Json.JsonElement args && args.ValueKind == System.Text.Json.JsonValueKind.String)
                            delta.ArgumentsJsonDelta = args.GetString();
                        else
                            delta.ArgumentsJson = _Serializer.SerializeJson(function["arguments"], false);
                    }
                }

                delta.Index = index;
                delta.Id = toolCallObj.ContainsKey("id") ? toolCallObj["id"]?.ToString() : "ollama-call-" + index;
                result.Add(delta);
                fallbackIndex++;
            }

            return result;
        }

        private async IAsyncEnumerable<GenerationStreamingChunk> ReadGenerateChunks(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken token)
        {
            await foreach (Dictionary<string, object> chunk in ReadLines(response, token).ConfigureAwait(false))
            {
                GenerationStreamingChunk streamChunk = new GenerationStreamingChunk();
                streamChunk.Model = chunk.ContainsKey("model") ? chunk["model"]?.ToString() : null;
                streamChunk.Text = chunk.ContainsKey("response") ? chunk["response"]?.ToString() : null;
                streamChunk.Done = IsTruthy(chunk, "done");
                yield return streamChunk;
            }
        }

        #endregion
    }
}
