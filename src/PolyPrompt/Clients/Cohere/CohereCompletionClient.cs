namespace PolyPrompt.Clients
{
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Text;
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using SyslogLogging;

    /// <summary>
    /// Cohere completion client using the v2 Chat API (<c>/v2/chat</c>) for chat, tool chat, and generation (sent as a
    /// single user turn, since Cohere retired its generate endpoint), each streaming (typed SSE events) or not. The model's
    /// tool plan and thinking are surfaced as <c>Reasoning</c> and never sent back. Cohere's <c>tool_choice</c> accepts only
    /// <c>REQUIRED</c> and <c>NONE</c>, so "auto" omits it and a specific tool name sends only that tool with <c>REQUIRED</c>.
    /// </summary>
    public class CohereCompletionClient : CompletionClientBase
    {
        #region Private-Members

        private const string MessageStartEvent = "message-start";
        private const string ContentDeltaEvent = "content-delta";
        private const string ToolPlanDeltaEvent = "tool-plan-delta";
        private const string ToolCallStartEvent = "tool-call-start";
        private const string ToolCallDeltaEvent = "tool-call-delta";
        private const string MessageEndEvent = "message-end";
        private const string TextContentType = "text";
        private const string ThinkingContentType = "thinking";

        #endregion

        #region Public-Members

        /// <summary>
        /// Client-wide default settings, including Cohere settings. Default model: command-a-03-2025.
        /// </summary>
        public override CohereCompletionOptions Defaults { get; } = new CohereCompletionOptions { Model = "command-a-03-2025" };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new Cohere completion client.
        /// </summary>
        /// <param name="endpoint">Cohere API endpoint URL. Default: https://api.cohere.com.</param>
        /// <param name="apiKey">Cohere API key, sent as a bearer token. Default: null.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        /// <param name="httpClient">Optional HTTP client; see <see cref="ClientBase"/>. Default: null.</param>
        public CohereCompletionClient(
            string endpoint = CohereProtocol.DefaultEndpoint,
            string? apiKey = null,
            LoggingModule? logging = null,
            HttpClient? httpClient = null)
            : base(endpoint, apiKey, logging ?? new LoggingModule(), httpClient)
        {
            _Header = CohereProtocol.Header;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override Task<bool> ValidateConnectivityAsync(CancellationToken token = default)
        {
            return ProbeAsync(BuildUrl(CohereProtocol.ProbePath), token);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
        {
            CohereProtocol.ApplyAuth(request, _ApiKey);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override Task<ChatResponse> ChatCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token)
        {
            ChatResponse response = new ChatResponse { Model = settings.Model };
            return ExecutePostAsync(response, "chat", BuildUrl("/v2/chat"), BuildPromptBody(prompt, settings, true, false), (text, r) =>
            {
                ChatResult parsed = ParseChat(text, r);
                r.Text = parsed.Text;
                r.Reasoning = NormalizeReasoning(parsed.Reasoning);
                r.Usage = parsed.Usage;
            }, token);
        }

        /// <inheritdoc />
        protected override Task<ChatStreamingResponse> ChatStreamingCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token)
        {
            ChatStreamingResponse response = new ChatStreamingResponse { Model = settings.Model };
            return ExecuteStreamingAsync(response, "chat", BuildUrl("/v2/chat"), BuildPromptBody(prompt, settings, true, true),
                (stream, sw) => response.Chunks = WrapChunksWithTiming(response, ReadChatChunks(stream.Response, stream.Token), sw, stream.Token, stream),
                token);
        }

        /// <inheritdoc />
        protected override Task<ToolChatResponse> ToolChatCoreAsync(ToolChatRequest request, List<ChatMessage> messages, ResolvedCompletion settings, CancellationToken token)
        {
            ToolChatResponse response = new ToolChatResponse { Model = settings.Model };
            return ExecutePostAsync(response, "tool chat", BuildUrl("/v2/chat"), BuildToolChatBody(request, messages, settings, false), (text, r) =>
            {
                ChatResult parsed = ParseChat(text, r);
                r.Text = parsed.Text;
                r.Reasoning = NormalizeReasoning(parsed.Reasoning);
                r.ToolCalls = parsed.ToolCalls;
                r.ResponseId = parsed.ResponseId;
                r.FinishReason = parsed.FinishReason;
                r.Usage = parsed.Usage;
            }, token);
        }

        /// <inheritdoc />
        protected override Task<ToolChatStreamingResponse> ToolChatStreamingCoreAsync(ToolChatRequest request, List<ChatMessage> messages, ResolvedCompletion settings, CancellationToken token)
        {
            ToolChatStreamingResponse response = new ToolChatStreamingResponse { Model = settings.Model };
            return ExecuteStreamingAsync(response, "tool chat", BuildUrl("/v2/chat"), BuildToolChatBody(request, messages, settings, true),
                (stream, sw) => response.Chunks = WrapToolChatChunksWithTiming(response, ReadToolChatChunks(stream.Response, stream.Token), sw, stream.Token, stream),
                token);
        }

        /// <inheritdoc />
        protected override Task<GenerationResponse> GenerateCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token)
        {
            GenerationResponse response = new GenerationResponse { Model = settings.Model };
            return ExecutePostAsync(response, "generate", BuildUrl("/v2/chat"), BuildPromptBody(prompt, settings, false, false), (text, r) => r.Text = ParseChat(text, r).Text, token);
        }

        /// <inheritdoc />
        protected override Task<GenerationStreamingResponse> GenerateStreamingCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token)
        {
            GenerationStreamingResponse response = new GenerationStreamingResponse { Model = settings.Model };
            return ExecuteStreamingAsync(response, "generate", BuildUrl("/v2/chat"), BuildPromptBody(prompt, settings, false, true),
                (stream, sw) => response.Chunks = WrapGenerationChunksWithTiming(response, ReadGenerationChunks(stream.Response, stream.Token), sw, stream.Token, stream),
                token);
        }

        #endregion

        #region Private-Methods

        private void ApplySampling(Dictionary<string, object> body, ResolvedCompletion settings)
        {
            if (settings.Temperature.HasValue) body["temperature"] = settings.Temperature.Value;

            // Cohere's p must be within 0.01..0.99; the shared TopP range is 0.0..1.0.
            if (settings.TopP.HasValue) body["p"] = Math.Clamp(settings.TopP.Value, 0.01, 0.99);

            int? topK = Pick<CohereCompletionOptions, int>(settings.Call, Defaults, o => o.TopK);
            int? seed = Pick<CohereCompletionOptions, int>(settings.Call, Defaults, o => o.Seed);
            double? frequencyPenalty = Pick<CohereCompletionOptions, double>(settings.Call, Defaults, o => o.FrequencyPenalty);
            double? presencePenalty = Pick<CohereCompletionOptions, double>(settings.Call, Defaults, o => o.PresencePenalty);
            List<string>? stopSequences = PickRef<CohereCompletionOptions, List<string>>(settings.Call, Defaults, o => o.StopSequences);

            if (topK.HasValue) body["k"] = topK.Value;
            if (seed.HasValue) body["seed"] = seed.Value;
            if (frequencyPenalty.HasValue) body["frequency_penalty"] = frequencyPenalty.Value;
            if (presencePenalty.HasValue) body["presence_penalty"] = presencePenalty.Value;
            if (stopSequences != null && stopSequences.Count > 0) body["stop_sequences"] = stopSequences;
        }

        /// <summary>
        /// Adds the Cohere <c>thinking</c> field for a reasoning effort (disabled at a budget of 0); null leaves the
        /// request unchanged.
        /// </summary>
        private static void ApplyReasoning(Dictionary<string, object> body, ReasoningEffort? reasoningEffort)
        {
            if (reasoningEffort == null) return;
            int budget = reasoningEffort.ToCohereThinkingBudget();
            body["thinking"] = budget > 0
                ? new Dictionary<string, object> { { "type", "enabled" }, { "token_budget", budget } }
                : new Dictionary<string, object> { { "type", "disabled" } };
        }

        private Dictionary<string, object> BuildPromptBody(string prompt, ResolvedCompletion settings, bool chat, bool stream)
        {
            List<Dictionary<string, object>> messages = new List<Dictionary<string, object>>();
            if (chat && !string.IsNullOrEmpty(settings.SystemPrompt))
            {
                messages.Add(new Dictionary<string, object> { { "role", "system" }, { "content", settings.SystemPrompt } });
            }
            messages.Add(new Dictionary<string, object> { { "role", "user" }, { "content", prompt } });

            Dictionary<string, object> body = new Dictionary<string, object>
            {
                { "model", settings.Model },
                { "messages", messages },
                { "max_tokens", settings.MaxTokens },
                { "stream", stream }
            };

            ApplySampling(body, settings);
            if (chat) ApplyReasoning(body, settings.ReasoningEffort);
            return body;
        }

        private Dictionary<string, object> BuildToolChatBody(ToolChatRequest request, List<ChatMessage> messages, ResolvedCompletion settings, bool stream)
        {
            Dictionary<string, object> body = new Dictionary<string, object>
            {
                { "model", settings.Model },
                { "messages", BuildMessages(messages) },
                { "max_tokens", settings.MaxTokens },
                { "stream", stream }
            };

            ApplySampling(body, settings);

            if (request.Tools != null && request.Tools.Count > 0)
            {
                List<ToolDefinition> tools = request.Tools;
                string? toolChoice = request.ToolChoice?.Trim();

                if (string.IsNullOrEmpty(toolChoice) || string.Equals(toolChoice, "auto", StringComparison.OrdinalIgnoreCase))
                {
                    // Cohere's default behavior is auto; omit tool_choice.
                }
                else if (string.Equals(toolChoice, "none", StringComparison.OrdinalIgnoreCase))
                {
                    body["tool_choice"] = "NONE";
                }
                else if (string.Equals(toolChoice, "required", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(toolChoice, "any", StringComparison.OrdinalIgnoreCase))
                {
                    body["tool_choice"] = "REQUIRED";
                }
                else
                {
                    // Cohere cannot name a tool in tool_choice, so a forced tool is expressed by sending only
                    // that tool and requiring a call.
                    List<ToolDefinition> named = tools.Where(t => string.Equals(t.Name, toolChoice, StringComparison.Ordinal)).ToList();
                    if (named.Count > 0) tools = named;
                    body["tool_choice"] = "REQUIRED";
                }

                body["tools"] = tools.Select(tool => new Dictionary<string, object>
                {
                    { "type", "function" },
                    { "function", new Dictionary<string, object>
                        {
                            { "name", tool.Name },
                            { "description", tool.Description },
                            { "parameters", tool.Parameters }
                        }
                    }
                }).ToList();
            }

            ApplyReasoning(body, settings.ReasoningEffort);
            return body;
        }

        private static List<Dictionary<string, object>> BuildMessages(List<ChatMessage> messages)
        {
            List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();
            int syntheticToolCallIndex = 0;

            foreach (ChatMessage message in messages)
            {
                string role = message.Role ?? "user";

                if (string.Equals(role, "tool", StringComparison.OrdinalIgnoreCase) || string.Equals(role, "function", StringComparison.OrdinalIgnoreCase))
                {
                    string toolCallId = !string.IsNullOrWhiteSpace(message.ToolCallId) ? message.ToolCallId
                        : !string.IsNullOrWhiteSpace(message.ToolName) ? message.ToolName
                        : "tool";

                    result.Add(new Dictionary<string, object>
                    {
                        { "role", "tool" },
                        { "tool_call_id", toolCallId },
                        { "content", message.Content ?? string.Empty }
                    });
                    continue;
                }

                if (message.ToolCalls != null && message.ToolCalls.Count > 0)
                {
                    List<Dictionary<string, object>> toolCalls = new List<Dictionary<string, object>>();
                    foreach (ToolCall toolCall in message.ToolCalls)
                    {
                        string id = string.IsNullOrWhiteSpace(toolCall.Id) ? "cohere-call-" + syntheticToolCallIndex : toolCall.Id;
                        syntheticToolCallIndex++;

                        toolCalls.Add(new Dictionary<string, object>
                        {
                            { "id", id },
                            { "type", "function" },
                            { "function", new Dictionary<string, object>
                                {
                                    { "name", toolCall.Name },
                                    { "arguments", string.IsNullOrWhiteSpace(toolCall.ArgumentsJson) ? "{}" : toolCall.ArgumentsJson }
                                }
                            }
                        });
                    }

                    Dictionary<string, object> assistant = new Dictionary<string, object>
                    {
                        { "role", "assistant" },
                        { "tool_calls", toolCalls }
                    };
                    if (!string.IsNullOrEmpty(message.Content)) assistant["content"] = message.Content;

                    result.Add(assistant);
                    continue;
                }

                string normalized = string.Equals(role, "system", StringComparison.OrdinalIgnoreCase) ? "system"
                    : string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase) || string.Equals(role, "model", StringComparison.OrdinalIgnoreCase) ? "assistant"
                    : "user";

                result.Add(new Dictionary<string, object> { { "role", normalized }, { "content", message.Content ?? string.Empty } });
            }

            return result;
        }

        private ChatResult ParseChat(string responseBody, ResponseBase response)
        {
            ChatResult parsed = new ChatResult();

            Dictionary<string, object>? responseObj = TryDeserializeObject(responseBody);
            if (responseObj == null)
            {
                response.Error = "Response is not a JSON object";
                return parsed;
            }

            Dictionary<string, object>? message = ParseNestedObject(responseObj, "message");
            if (message == null)
            {
                response.Error = "Response missing 'message' field";
                return parsed;
            }

            parsed.ResponseId = responseObj.ContainsKey("id") ? responseObj["id"]?.ToString() : null;
            parsed.FinishReason = responseObj.ContainsKey("finish_reason") ? responseObj["finish_reason"]?.ToString() : null;
            parsed.Usage = ParseUsage(ParseNestedObject(responseObj, "usage"));

            StringBuilder text = new StringBuilder();
            StringBuilder reasoning = new StringBuilder();

            if (message.ContainsKey("tool_plan") && message["tool_plan"] != null) reasoning.Append(message["tool_plan"]?.ToString());

            List<Dictionary<string, object>>? blocks = ParseNestedList(message, "content");
            if (blocks != null)
            {
                foreach (Dictionary<string, object> block in blocks)
                {
                    string? type = block.ContainsKey("type") ? block["type"]?.ToString() : null;
                    if (type == TextContentType && block.ContainsKey("text")) text.Append(block["text"]?.ToString());
                    else if (type == ThinkingContentType && block.ContainsKey(ThinkingContentType)) reasoning.Append(block[ThinkingContentType]?.ToString());
                }
            }
            else if (message.ContainsKey("content") && message["content"] != null)
            {
                text.Append(message["content"]?.ToString());
            }

            List<Dictionary<string, object>>? calls = ParseNestedList(message, "tool_calls");
            if (calls != null)
            {
                foreach (Dictionary<string, object> call in calls)
                {
                    Dictionary<string, object>? function = ParseNestedObject(call, "function");
                    if (function == null || !function.ContainsKey("name")) continue;

                    string? arguments = function.ContainsKey("arguments") ? function["arguments"]?.ToString() : null;
                    parsed.ToolCalls.Add(new ToolCall
                    {
                        Id = call.ContainsKey("id") ? call["id"]?.ToString() : null,
                        Name = function["name"]?.ToString() ?? string.Empty,
                        ArgumentsJson = string.IsNullOrWhiteSpace(arguments) ? "{}" : arguments
                    });
                }
            }

            parsed.Text = text.Length > 0 ? text.ToString() : null;
            parsed.Reasoning = reasoning.Length > 0 ? reasoning.ToString() : null;
            return parsed;
        }

        private TokenUsage? ParseUsage(Dictionary<string, object>? usageObj)
        {
            if (usageObj == null) return null;

            Dictionary<string, object>? tokens = ParseNestedObject(usageObj, "tokens");
            Dictionary<string, object>? billed = ParseNestedObject(usageObj, "billed_units");

            // Prefer the raw token counts (what the model processed); fall back to billed units.
            TokenUsage usage = new TokenUsage();
            usage.PromptTokens = (tokens != null ? TryGetRoundedInt(tokens, "input_tokens") : null) ?? (billed != null ? TryGetRoundedInt(billed, "input_tokens") : null);
            usage.CompletionTokens = (tokens != null ? TryGetRoundedInt(tokens, "output_tokens") : null) ?? (billed != null ? TryGetRoundedInt(billed, "output_tokens") : null);
            usage.CachedPromptTokens = TryGetRoundedInt(usageObj, "cached_tokens");

            if (usage.PromptTokens.HasValue || usage.CompletionTokens.HasValue)
                usage.TotalTokens = (usage.PromptTokens ?? 0) + (usage.CompletionTokens ?? 0);

            return usage;
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

                string data = line.Substring(5).Trim();
                if (data.Length == 0) continue;
                if (data == "[DONE]") yield break;

                Dictionary<string, object>? evt = TryDeserializeObject(data);
                string? type = evt != null && evt.ContainsKey("type") ? evt["type"]?.ToString() : null;
                if (evt == null || type == null) continue;

                yield return (type, evt);
                if (type == MessageEndEvent) yield break;
            }
        }

        private Dictionary<string, object>? DeltaMessage(Dictionary<string, object> evt)
        {
            Dictionary<string, object>? delta = ParseNestedObject(evt, "delta");
            return delta != null ? ParseNestedObject(delta, "message") : null;
        }

        private (string? Text, string? Thinking) ReadContentDelta(Dictionary<string, object> evt)
        {
            Dictionary<string, object>? message = DeltaMessage(evt);
            Dictionary<string, object>? content = message != null ? ParseNestedObject(message, "content") : null;
            if (content == null) return (null, null);

            return (content.ContainsKey("text") ? content["text"]?.ToString() : null,
                content.ContainsKey(ThinkingContentType) ? content[ThinkingContentType]?.ToString() : null);
        }

        private string? ReadToolPlanDelta(Dictionary<string, object> evt)
        {
            Dictionary<string, object>? message = DeltaMessage(evt);
            return message != null && message.ContainsKey("tool_plan") ? message["tool_plan"]?.ToString() : null;
        }

        private (string? FinishReason, TokenUsage? Usage) ReadMessageEnd(Dictionary<string, object> evt)
        {
            Dictionary<string, object>? delta = ParseNestedObject(evt, "delta");
            if (delta == null) return (null, null);
            return (delta.ContainsKey("finish_reason") ? delta["finish_reason"]?.ToString() : null, ParseUsage(ParseNestedObject(delta, "usage")));
        }

        private ToolCallDelta? ReadToolCallDelta(Dictionary<string, object> evt, bool isStart)
        {
            Dictionary<string, object>? message = DeltaMessage(evt);
            Dictionary<string, object>? toolCall = message != null ? ParseNestedObject(message, "tool_calls") : null;
            if (toolCall == null) return null;

            Dictionary<string, object>? function = ParseNestedObject(toolCall, "function");

            ToolCallDelta delta = new ToolCallDelta { Index = TryGetInt(evt, "index") ?? 0 };
            if (isStart)
            {
                delta.Id = toolCall.ContainsKey("id") ? toolCall["id"]?.ToString() : null;
                delta.Type = "function";
                delta.Name = function != null && function.ContainsKey("name") ? function["name"]?.ToString() : null;
            }

            string? arguments = function != null && function.ContainsKey("arguments") ? function["arguments"]?.ToString() : null;
            if (!string.IsNullOrEmpty(arguments)) delta.ArgumentsJsonDelta = arguments;
            return delta;
        }

        private async IAsyncEnumerable<ChatStreamingChunk> ReadChatChunks(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken token)
        {
            await foreach ((string type, Dictionary<string, object> evt) in ReadEvents(response, token).ConfigureAwait(false))
            {
                ChatStreamingChunk chunk = new ChatStreamingChunk { CreatedUtc = DateTime.UtcNow };

                if (type == MessageStartEvent) chunk.ResponseId = evt.ContainsKey("id") ? evt["id"]?.ToString() : null;
                else if (type == ContentDeltaEvent) (chunk.Text, chunk.ReasoningText) = ReadContentDelta(evt);
                else if (type == ToolPlanDeltaEvent) chunk.ReasoningText = ReadToolPlanDelta(evt);
                else if (type == MessageEndEvent)
                {
                    (chunk.FinishReason, chunk.Usage) = ReadMessageEnd(evt);
                    chunk.Done = true;
                }
                else continue;

                yield return chunk;
            }
        }

        private async IAsyncEnumerable<ToolChatStreamingChunk> ReadToolChatChunks(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken token)
        {
            await foreach ((string type, Dictionary<string, object> evt) in ReadEvents(response, token).ConfigureAwait(false))
            {
                ToolChatStreamingChunk chunk = new ToolChatStreamingChunk { CreatedUtc = DateTime.UtcNow };

                if (type == MessageStartEvent) chunk.ResponseId = evt.ContainsKey("id") ? evt["id"]?.ToString() : null;
                else if (type == ContentDeltaEvent) (chunk.Text, chunk.ReasoningText) = ReadContentDelta(evt);
                else if (type == ToolPlanDeltaEvent) chunk.ReasoningText = ReadToolPlanDelta(evt);
                else if (type == ToolCallStartEvent || type == ToolCallDeltaEvent)
                {
                    ToolCallDelta? delta = ReadToolCallDelta(evt, type == ToolCallStartEvent);
                    if (delta == null) continue;
                    chunk.ToolCallDeltas.Add(delta);
                }
                else if (type == MessageEndEvent)
                {
                    (chunk.FinishReason, chunk.Usage) = ReadMessageEnd(evt);
                    chunk.Done = true;
                }
                else continue;

                yield return chunk;
            }
        }

        private async IAsyncEnumerable<GenerationStreamingChunk> ReadGenerationChunks(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken token)
        {
            await foreach ((string type, Dictionary<string, object> evt) in ReadEvents(response, token).ConfigureAwait(false))
            {
                if (type == ContentDeltaEvent)
                {
                    string? text = ReadContentDelta(evt).Text;
                    if (!string.IsNullOrEmpty(text)) yield return new GenerationStreamingChunk { Text = text };
                }
                else if (type == MessageEndEvent)
                {
                    yield return new GenerationStreamingChunk { Done = true };
                }
            }
        }

        private sealed class ChatResult
        {
            public string? Text { get; set; }
            public string? Reasoning { get; set; }
            public List<ToolCall> ToolCalls { get; set; } = new List<ToolCall>();
            public string? ResponseId { get; set; }
            public string? FinishReason { get; set; }
            public TokenUsage? Usage { get; set; }
        }

        #endregion
    }
}
