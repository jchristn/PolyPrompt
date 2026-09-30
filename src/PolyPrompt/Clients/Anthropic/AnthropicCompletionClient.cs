namespace PolyPrompt.Clients
{
    using System.IO;
    using System.Runtime.CompilerServices;
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using SyslogLogging;

    /// <summary>
    /// Anthropic (Claude) completion client using the Messages API (<c>/v1/messages</c>) for chat, tool chat, and
    /// generation (sent as a single user turn), each streaming (typed SSE events) or not. Tool results are sent as
    /// user-role <c>tool_result</c> blocks, and consecutive results merge into one user turn.
    /// </summary>
    public class AnthropicCompletionClient : CompletionClientBase
    {
        #region Private-Members

        private const string TextBlockType = "text";
        private const string ThinkingBlockType = "thinking";
        private const string ToolUseBlockType = "tool_use";
        private const string ToolResultBlockType = "tool_result";
        private const string MessageStartEvent = "message_start";
        private const string ContentBlockStartEvent = "content_block_start";
        private const string ContentBlockDeltaEvent = "content_block_delta";
        private const string MessageDeltaEvent = "message_delta";
        private const string MessageStopEvent = "message_stop";
        private const string TextDeltaType = "text_delta";
        private const string ThinkingDeltaType = "thinking_delta";
        private const string InputJsonDeltaType = "input_json_delta";

        private string _AnthropicVersion = AnthropicProtocol.DefaultVersion;
        private string? _WorkspaceId = null;

        #endregion

        #region Public-Members

        /// <summary>
        /// Client-wide default settings, including Anthropic settings such as <see cref="AnthropicCompletionOptions.TopK"/>.
        /// Default model: claude-opus-4-8.
        /// </summary>
        public override AnthropicCompletionOptions Defaults { get; } = new AnthropicCompletionOptions { Model = "claude-opus-4-8" };

        /// <summary>
        /// Value sent in the <c>anthropic-version</c> header. Default: 2023-06-01.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null, empty, or whitespace.</exception>
        public string AnthropicVersion
        {
            get { return _AnthropicVersion; }
            set { _AnthropicVersion = AnthropicProtocol.ValidateVersion(value, nameof(AnthropicVersion)); }
        }

        /// <summary>
        /// Workspace identifier sent in the <c>anthropic-workspace-id</c> header. Identity-linked API keys reject requests
        /// without it; standard workspace keys do not need it. Null, empty, or whitespace sends no header. Default: null.
        /// </summary>
        public string? WorkspaceId
        {
            get { return _WorkspaceId; }
            set { _WorkspaceId = AnthropicProtocol.NormalizeWorkspace(value); }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new Anthropic completion client.
        /// </summary>
        /// <param name="endpoint">Anthropic API endpoint URL. Default: https://api.anthropic.com.</param>
        /// <param name="apiKey">Anthropic API key, sent as the <c>x-api-key</c> header. Default: null.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        /// <param name="httpClient">Optional HTTP client; see <see cref="ClientBase"/>. Default: null.</param>
        public AnthropicCompletionClient(
            string endpoint = AnthropicProtocol.DefaultEndpoint,
            string? apiKey = null,
            LoggingModule? logging = null,
            HttpClient? httpClient = null)
            : base(endpoint, apiKey, logging ?? new LoggingModule(), httpClient)
        {
            _Header = AnthropicProtocol.Header;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override Task<bool> ValidateConnectivityAsync(CancellationToken token = default)
        {
            return ProbeAsync(BuildUrl("/v1/models?limit=1"), token);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
        {
            AnthropicProtocol.ApplyHeaders(request, _ApiKey, _AnthropicVersion, _WorkspaceId);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override Task<ChatResponse> ChatCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token)
        {
            ChatResponse response = new ChatResponse { Model = settings.Model };
            return ExecutePostAsync(response, "chat", BuildUrl("/v1/messages"), BuildPromptBody(prompt, settings, true, false), ParseChat, token);
        }

        /// <inheritdoc />
        protected override Task<ChatStreamingResponse> ChatStreamingCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token)
        {
            ChatStreamingResponse response = new ChatStreamingResponse { Model = settings.Model };
            return ExecuteStreamingAsync(response, "chat", BuildUrl("/v1/messages"), BuildPromptBody(prompt, settings, true, true),
                (stream, sw) => response.Chunks = WrapChunksWithTiming(response, ReadChatChunks(stream.Response, stream.Token), sw, stream.Token, stream),
                token);
        }

        /// <inheritdoc />
        protected override Task<ToolChatResponse> ToolChatCoreAsync(ToolChatRequest request, List<ChatMessage> messages, ResolvedCompletion settings, CancellationToken token)
        {
            ToolChatResponse response = new ToolChatResponse { Model = settings.Model };
            return ExecutePostAsync(response, "tool chat", BuildUrl("/v1/messages"), BuildToolChatBody(request, messages, settings, false), ParseToolChat, token);
        }

        /// <inheritdoc />
        protected override Task<ToolChatStreamingResponse> ToolChatStreamingCoreAsync(ToolChatRequest request, List<ChatMessage> messages, ResolvedCompletion settings, CancellationToken token)
        {
            ToolChatStreamingResponse response = new ToolChatStreamingResponse { Model = settings.Model };
            return ExecuteStreamingAsync(response, "tool chat", BuildUrl("/v1/messages"), BuildToolChatBody(request, messages, settings, true),
                (stream, sw) => response.Chunks = WrapToolChatChunksWithTiming(response, ReadToolChatChunks(stream.Response, stream.Token), sw, stream.Token, stream),
                token);
        }

        /// <inheritdoc />
        protected override Task<GenerationResponse> GenerateCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token)
        {
            GenerationResponse response = new GenerationResponse { Model = settings.Model };
            return ExecutePostAsync(response, "generate", BuildUrl("/v1/messages"), BuildPromptBody(prompt, settings, false, false), ParseGenerate, token);
        }

        /// <inheritdoc />
        protected override Task<GenerationStreamingResponse> GenerateStreamingCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token)
        {
            GenerationStreamingResponse response = new GenerationStreamingResponse { Model = settings.Model };
            return ExecuteStreamingAsync(response, "generate", BuildUrl("/v1/messages"), BuildPromptBody(prompt, settings, false, true),
                (stream, sw) => response.Chunks = WrapGenerationChunksWithTiming(response, ReadGenerateChunks(stream.Response, stream.Token), sw, stream.Token, stream),
                token);
        }

        #endregion

        #region Private-Methods

        private void ApplySampling(Dictionary<string, object> body, ResolvedCompletion settings)
        {
            if (settings.Temperature.HasValue) body["temperature"] = settings.Temperature.Value;
            if (settings.TopP.HasValue) body["top_p"] = settings.TopP.Value;

            int? topK = Pick<AnthropicCompletionOptions, int>(settings.Call, Defaults, o => o.TopK);
            List<string>? stopSequences = PickRef<AnthropicCompletionOptions, List<string>>(settings.Call, Defaults, o => o.StopSequences);

            if (topK.HasValue) body["top_k"] = topK.Value;
            if (stopSequences != null && stopSequences.Count > 0) body["stop_sequences"] = stopSequences;
        }

        /// <summary>
        /// Adds <c>output_config.effort</c> and, above Minimal, adaptive <c>thinking</c> for a reasoning effort;
        /// null leaves the request unchanged.
        /// </summary>
        private static void ApplyReasoning(Dictionary<string, object> body, ReasoningEffort? reasoningEffort)
        {
            if (reasoningEffort == null) return;
            body["output_config"] = new Dictionary<string, object> { { "effort", reasoningEffort.ToAnthropicEffort() } };

            if (reasoningEffort.SendsAnthropicThinking())
            {
                body["thinking"] = new Dictionary<string, object>
                {
                    { "type", "adaptive" },
                    { "display", "summarized" }
                };
            }
        }

        private Dictionary<string, object> BuildPromptBody(string prompt, ResolvedCompletion settings, bool chat, bool stream)
        {
            Dictionary<string, object> body = new Dictionary<string, object>
            {
                { "model", settings.Model },
                { "max_tokens", settings.MaxTokens },
                { "messages", new List<Dictionary<string, object>> { new Dictionary<string, object> { { "role", "user" }, { "content", prompt } } } }
            };

            if (chat && !string.IsNullOrEmpty(settings.SystemPrompt)) body["system"] = settings.SystemPrompt;
            ApplySampling(body, settings);
            if (chat) ApplyReasoning(body, settings.ReasoningEffort);
            if (stream) body["stream"] = true;
            return body;
        }

        private Dictionary<string, object> BuildToolChatBody(ToolChatRequest request, List<ChatMessage> messages, ResolvedCompletion settings, bool stream)
        {
            Dictionary<string, object> body = new Dictionary<string, object>
            {
                { "model", settings.Model },
                { "max_tokens", settings.MaxTokens },
                { "messages", BuildMessages(messages) }
            };

            string? system = BuildSystem(messages);
            if (system != null) body["system"] = system;

            ApplySampling(body, settings);

            if (request.Tools != null && request.Tools.Count > 0 && !IsToolChoiceNone(request.ToolChoice))
            {
                body["tools"] = request.Tools.Select(tool => new Dictionary<string, object>
                {
                    { "name", tool.Name },
                    { "description", tool.Description },
                    { "input_schema", tool.Parameters }
                }).ToList();
                body["tool_choice"] = BuildToolChoice(request.ToolChoice);
            }

            ApplyReasoning(body, settings.ReasoningEffort);
            if (stream) body["stream"] = true;
            return body;
        }

        private List<Dictionary<string, object>> BuildMessages(List<ChatMessage> messages)
        {
            List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();
            List<Dictionary<string, object>>? pendingToolResults = null;
            int syntheticToolCallIndex = 0;

            foreach (ChatMessage message in messages)
            {
                if (string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase)) continue;

                bool isToolResult = string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(message.Role, "function", StringComparison.OrdinalIgnoreCase);

                if (isToolResult)
                {
                    // Tool results are user-role tool_result content blocks. Consecutive results are merged
                    // into a single user turn so parallel tool calls resolve in one message.
                    pendingToolResults ??= new List<Dictionary<string, object>>();
                    pendingToolResults.Add(new Dictionary<string, object>
                    {
                        { "type", ToolResultBlockType },
                        { "tool_use_id", ResolveToolUseId(message) },
                        { "content", message.Content ?? string.Empty }
                    });
                    continue;
                }

                FlushPendingToolResults(result, ref pendingToolResults);

                if (message.ToolCalls != null && message.ToolCalls.Count > 0)
                {
                    List<Dictionary<string, object>> blocks = new List<Dictionary<string, object>>();

                    if (!string.IsNullOrEmpty(message.Content))
                    {
                        blocks.Add(new Dictionary<string, object> { { "type", TextBlockType }, { "text", message.Content } });
                    }

                    foreach (ToolCall toolCall in message.ToolCalls)
                    {
                        string toolUseId = string.IsNullOrWhiteSpace(toolCall.Id) ? "anthropic-call-" + syntheticToolCallIndex : toolCall.Id;
                        syntheticToolCallIndex++;

                        blocks.Add(new Dictionary<string, object>
                        {
                            { "type", ToolUseBlockType },
                            { "id", toolUseId },
                            { "name", toolCall.Name },
                            { "input", DeserializeDictionaryOrEmpty(toolCall.ArgumentsJson) }
                        });
                    }

                    result.Add(new Dictionary<string, object> { { "role", "assistant" }, { "content", blocks } });
                    continue;
                }

                result.Add(new Dictionary<string, object>
                {
                    { "role", NormalizeRole(message.Role) },
                    { "content", message.Content ?? string.Empty }
                });
            }

            FlushPendingToolResults(result, ref pendingToolResults);
            return result;
        }

        private static void FlushPendingToolResults(List<Dictionary<string, object>> result, ref List<Dictionary<string, object>>? pendingToolResults)
        {
            if (pendingToolResults == null || pendingToolResults.Count == 0) return;
            result.Add(new Dictionary<string, object> { { "role", "user" }, { "content", pendingToolResults } });
            pendingToolResults = null;
        }

        private static string? BuildSystem(List<ChatMessage> messages)
        {
            List<string> parts = messages
                .Where(m => string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(m.Content))
                .Select(m => m.Content!)
                .ToList();

            return parts.Count == 0 ? null : string.Join("\n\n", parts);
        }

        private static Dictionary<string, object> BuildToolChoice(string? toolChoice)
        {
            if (string.IsNullOrWhiteSpace(toolChoice) || string.Equals(toolChoice, "auto", StringComparison.OrdinalIgnoreCase))
                return new Dictionary<string, object> { { "type", "auto" } };

            if (string.Equals(toolChoice, "required", StringComparison.OrdinalIgnoreCase)
                || string.Equals(toolChoice, "any", StringComparison.OrdinalIgnoreCase))
                return new Dictionary<string, object> { { "type", "any" } };

            // A specific tool name forces that tool.
            return new Dictionary<string, object> { { "type", "tool" }, { "name", toolChoice } };
        }

        private static string ResolveToolUseId(ChatMessage message)
        {
            if (!string.IsNullOrWhiteSpace(message.ToolCallId)) return message.ToolCallId;
            if (!string.IsNullOrWhiteSpace(message.ToolName)) return message.ToolName;
            return "tool";
        }

        private static string NormalizeRole(string? role)
        {
            if (string.Equals(role, "model", StringComparison.OrdinalIgnoreCase)) return "assistant";
            if (string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase)) return "assistant";
            return "user";
        }

        private List<Dictionary<string, object>>? ReadBlocks(Dictionary<string, object>? responseObj, ResponseBase response)
        {
            if (responseObj == null || !responseObj.ContainsKey("content"))
            {
                response.Error = "Response missing 'content' field";
                return null;
            }

            return ParseNestedList(responseObj, "content") ?? new List<Dictionary<string, object>>();
        }

        private static string? BlockType(Dictionary<string, object> block)
        {
            return block.ContainsKey("type") ? block["type"]?.ToString() : null;
        }

        private static void SplitBlocks(List<Dictionary<string, object>> blocks, out string? text, out string? reasoning)
        {
            string combinedText = string.Empty;
            string combinedReasoning = string.Empty;

            foreach (Dictionary<string, object> block in blocks)
            {
                string? type = BlockType(block);
                if (string.Equals(type, TextBlockType, StringComparison.Ordinal) && block.ContainsKey("text"))
                    combinedText += block["text"]?.ToString();
                else if (string.Equals(type, ThinkingBlockType, StringComparison.Ordinal) && block.ContainsKey(ThinkingBlockType))
                    combinedReasoning += block[ThinkingBlockType]?.ToString();
            }

            text = string.IsNullOrWhiteSpace(combinedText) ? null : combinedText;
            reasoning = NormalizeReasoning(combinedReasoning);
        }

        private void ParseChat(string responseBody, ChatResponse response)
        {
            Dictionary<string, object>? responseObj = TryDeserializeObject(responseBody);
            List<Dictionary<string, object>>? blocks = ReadBlocks(responseObj, response);
            if (blocks == null) return;

            SplitBlocks(blocks, out string? text, out string? reasoning);
            response.Text = text;
            response.Reasoning = reasoning;
            response.Usage = ParseUsage(responseObj!);
            if (responseObj!.ContainsKey("model")) response.Model = responseObj["model"]?.ToString() ?? response.Model;
        }

        private void ParseGenerate(string responseBody, GenerationResponse response)
        {
            Dictionary<string, object>? responseObj = TryDeserializeObject(responseBody);
            List<Dictionary<string, object>>? blocks = ReadBlocks(responseObj, response);
            if (blocks == null) return;

            SplitBlocks(blocks, out string? text, out _);
            response.Text = text;
        }

        private void ParseToolChat(string responseBody, ToolChatResponse response)
        {
            Dictionary<string, object>? responseObj = TryDeserializeObject(responseBody);
            List<Dictionary<string, object>>? blocks = ReadBlocks(responseObj, response);
            if (blocks == null) return;

            response.ResponseId = responseObj!.ContainsKey("id") ? responseObj["id"]?.ToString() : null;
            if (responseObj.ContainsKey("model")) response.Model = responseObj["model"]?.ToString() ?? response.Model;
            response.FinishReason = responseObj.ContainsKey("stop_reason") ? responseObj["stop_reason"]?.ToString() : null;
            response.Usage = ParseUsage(responseObj);

            SplitBlocks(blocks, out string? text, out string? reasoning);
            response.Text = text;
            response.Reasoning = reasoning;

            foreach (Dictionary<string, object> block in blocks)
            {
                if (!string.Equals(BlockType(block), ToolUseBlockType, StringComparison.Ordinal) || !block.ContainsKey("name")) continue;

                response.ToolCalls.Add(new ToolCall
                {
                    Id = block.ContainsKey("id") ? block["id"]?.ToString() : null,
                    Name = block["name"]?.ToString() ?? string.Empty,
                    ArgumentsJson = block.ContainsKey("input") && block["input"] != null ? _Serializer.SerializeJson(block["input"], false) : "{}"
                });
            }
        }

        // PromptTokens is input_tokens (the uncached input); the cache buckets are reported separately and are
        // additional to it. TotalTokens is input + output.
        private TokenUsage? ParseUsage(Dictionary<string, object> responseObj)
        {
            Dictionary<string, object>? usageObj = ParseNestedObject(responseObj, "usage");
            if (usageObj == null) return null;

            TokenUsage usage = new TokenUsage();
            usage.PromptTokens = TryGetInt(usageObj, "input_tokens");
            usage.CompletionTokens = TryGetInt(usageObj, "output_tokens");
            usage.CachedPromptTokens = TryGetInt(usageObj, "cache_read_input_tokens");
            usage.CacheCreationTokens = TryGetInt(usageObj, "cache_creation_input_tokens");

            if (usage.PromptTokens.HasValue || usage.CompletionTokens.HasValue)
                usage.TotalTokens = (usage.PromptTokens ?? 0) + (usage.CompletionTokens ?? 0);

            return usage;
        }

        private TokenUsage? ParseStreamUsage(Dictionary<string, object> evt, StreamState state)
        {
            Dictionary<string, object>? usageObj = ParseNestedObject(evt, "usage");
            if (usageObj == null && state.PromptTokens == null && state.CacheReadTokens == null && state.CacheCreationTokens == null) return null;

            TokenUsage usage = new TokenUsage();
            usage.PromptTokens = state.PromptTokens;
            usage.CachedPromptTokens = state.CacheReadTokens;
            usage.CacheCreationTokens = state.CacheCreationTokens;

            if (usageObj != null)
            {
                usage.PromptTokens = TryGetInt(usageObj, "input_tokens") ?? usage.PromptTokens;
                usage.CompletionTokens = TryGetInt(usageObj, "output_tokens");

                // message_delta sometimes echoes the cache buckets; prefer an echoed value when present.
                usage.CachedPromptTokens = TryGetInt(usageObj, "cache_read_input_tokens") ?? usage.CachedPromptTokens;
                usage.CacheCreationTokens = TryGetInt(usageObj, "cache_creation_input_tokens") ?? usage.CacheCreationTokens;
            }

            if (usage.PromptTokens.HasValue || usage.CompletionTokens.HasValue)
                usage.TotalTokens = (usage.PromptTokens ?? 0) + (usage.CompletionTokens ?? 0);

            return usage;
        }

        private sealed class StreamState
        {
            public int? PromptTokens;
            public int? CacheReadTokens;
            public int? CacheCreationTokens;
        }

        private async IAsyncEnumerable<(string Type, Dictionary<string, object> Event)> ReadEvents(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken token)
        {
            using Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using StreamReader reader = new StreamReader(stream);

            string? line;
            while ((line = await reader.ReadLineAsync(token).ConfigureAwait(false)) != null)
            {
                token.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

                Dictionary<string, object>? evt = TryDeserializeObject(line.Substring(5).TrimStart());
                string? type = evt != null && evt.ContainsKey("type") ? evt["type"]?.ToString() : null;
                if (evt == null || type == null) continue;

                yield return (type, evt);
                if (string.Equals(type, MessageStopEvent, StringComparison.Ordinal)) yield break;
            }
        }

        private (string? Id, string? Model) ReadMessageStart(Dictionary<string, object> evt, StreamState state)
        {
            Dictionary<string, object>? message = ParseNestedObject(evt, "message");
            if (message == null) return (null, null);

            // message_start carries input_tokens and the cache buckets; message_delta carries only output_tokens
            // (and sometimes echoes the cache buckets). Capture them here.
            Dictionary<string, object>? usageObj = ParseNestedObject(message, "usage");
            if (usageObj != null)
            {
                state.PromptTokens = TryGetInt(usageObj, "input_tokens");
                state.CacheReadTokens = TryGetInt(usageObj, "cache_read_input_tokens");
                state.CacheCreationTokens = TryGetInt(usageObj, "cache_creation_input_tokens");
            }

            return (message.ContainsKey("id") ? message["id"]?.ToString() : null, message.ContainsKey("model") ? message["model"]?.ToString() : null);
        }

        private string? ReadStopReason(Dictionary<string, object> evt)
        {
            Dictionary<string, object>? delta = ParseNestedObject(evt, "delta");
            return delta != null && delta.ContainsKey("stop_reason") && delta["stop_reason"] != null ? delta["stop_reason"]?.ToString() : null;
        }

        private async IAsyncEnumerable<ChatStreamingChunk> ReadChatChunks(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken token)
        {
            StreamState state = new StreamState();

            await foreach ((string type, Dictionary<string, object> evt) in ReadEvents(response, token).ConfigureAwait(false))
            {
                ChatStreamingChunk chunk = new ChatStreamingChunk { CreatedUtc = DateTime.UtcNow };

                if (type == MessageStartEvent)
                {
                    (chunk.ResponseId, chunk.Model) = ReadMessageStart(evt, state);
                }
                else if (type == ContentBlockDeltaEvent)
                {
                    Dictionary<string, object>? delta = ParseNestedObject(evt, "delta");
                    string? deltaType = delta != null && delta.ContainsKey("type") ? delta["type"]?.ToString() : null;
                    if (deltaType == TextDeltaType && delta!.ContainsKey("text")) chunk.Text = delta["text"]?.ToString();
                    else if (deltaType == ThinkingDeltaType && delta!.ContainsKey(ThinkingBlockType)) chunk.ReasoningText = delta[ThinkingBlockType]?.ToString();
                }
                else if (type == MessageDeltaEvent)
                {
                    chunk.FinishReason = ReadStopReason(evt);
                    chunk.Done = chunk.FinishReason != null;
                    chunk.Usage = ParseStreamUsage(evt, state);
                }
                else if (type == MessageStopEvent)
                {
                    chunk.Done = true;
                }
                else
                {
                    continue;
                }

                yield return chunk;
            }
        }

        private async IAsyncEnumerable<ToolChatStreamingChunk> ReadToolChatChunks(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken token)
        {
            StreamState state = new StreamState();

            await foreach ((string type, Dictionary<string, object> evt) in ReadEvents(response, token).ConfigureAwait(false))
            {
                ToolChatStreamingChunk chunk = new ToolChatStreamingChunk { CreatedUtc = DateTime.UtcNow };

                if (type == MessageStartEvent)
                {
                    (chunk.ResponseId, chunk.Model) = ReadMessageStart(evt, state);
                }
                else if (type == ContentBlockStartEvent)
                {
                    Dictionary<string, object>? block = ParseNestedObject(evt, "content_block");
                    if (block == null || BlockType(block) != ToolUseBlockType) continue;

                    chunk.ToolCallDeltas.Add(new ToolCallDelta
                    {
                        Index = TryGetInt(evt, "index") ?? 0,
                        Id = block.ContainsKey("id") ? block["id"]?.ToString() : null,
                        Type = "function",
                        Name = block.ContainsKey("name") ? block["name"]?.ToString() : null
                    });
                }
                else if (type == ContentBlockDeltaEvent)
                {
                    Dictionary<string, object>? delta = ParseNestedObject(evt, "delta");
                    string? deltaType = delta != null && delta.ContainsKey("type") ? delta["type"]?.ToString() : null;

                    if (deltaType == TextDeltaType && delta!.ContainsKey("text"))
                    {
                        chunk.Text = delta["text"]?.ToString();
                    }
                    else if (deltaType == ThinkingDeltaType && delta!.ContainsKey(ThinkingBlockType))
                    {
                        chunk.ReasoningText = delta[ThinkingBlockType]?.ToString();
                    }
                    else if (deltaType == InputJsonDeltaType && delta!.ContainsKey("partial_json"))
                    {
                        chunk.ToolCallDeltas.Add(new ToolCallDelta
                        {
                            Index = TryGetInt(evt, "index") ?? 0,
                            ArgumentsJsonDelta = delta["partial_json"]?.ToString()
                        });
                    }
                }
                else if (type == MessageDeltaEvent)
                {
                    chunk.FinishReason = ReadStopReason(evt);
                    chunk.Done = chunk.FinishReason != null;
                    chunk.Usage = ParseStreamUsage(evt, state);
                }
                else if (type == MessageStopEvent)
                {
                    chunk.Done = true;
                }
                else
                {
                    continue;
                }

                yield return chunk;
            }
        }

        private async IAsyncEnumerable<GenerationStreamingChunk> ReadGenerateChunks(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken token)
        {
            await foreach ((string type, Dictionary<string, object> evt) in ReadEvents(response, token).ConfigureAwait(false))
            {
                if (type == MessageStartEvent)
                {
                    Dictionary<string, object>? message = ParseNestedObject(evt, "message");
                    yield return new GenerationStreamingChunk { Model = message != null && message.ContainsKey("model") ? message["model"]?.ToString() : null };
                }
                else if (type == ContentBlockDeltaEvent)
                {
                    Dictionary<string, object>? delta = ParseNestedObject(evt, "delta");
                    string? deltaType = delta != null && delta.ContainsKey("type") ? delta["type"]?.ToString() : null;
                    if (deltaType == TextDeltaType && delta!.ContainsKey("text"))
                        yield return new GenerationStreamingChunk { Text = delta["text"]?.ToString() };
                }
                else if (type == MessageStopEvent)
                {
                    yield return new GenerationStreamingChunk { Done = true };
                }
            }
        }

        #endregion
    }
}
