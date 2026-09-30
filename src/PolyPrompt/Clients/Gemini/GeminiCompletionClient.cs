namespace PolyPrompt.Clients
{
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Text.Json;
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using SyslogLogging;

    /// <summary>
    /// Gemini (Google AI Studio) completion client using <c>generateContent</c> and <c>streamGenerateContent</c> for chat,
    /// tool chat, and generation. Handles Gemini 3 tool calling: thought signatures are captured into
    /// <see cref="ToolCall.ThoughtSignature"/> and replayed beside each function call; tool results are sent as
    /// <c>functionResponse</c> parts in <c>user</c> turns (parallel results merged into one turn); tool results of any shape
    /// are accepted; and tool schemas are sent as JSON Schema or reduced to the OpenAPI subset
    /// (<see cref="GeminiCompletionOptions.ToolSchemaMode"/>).
    /// </summary>
    public class GeminiCompletionClient : CompletionClientBase
    {
        #region Private-Members

        // Gemini marks reasoning ("thought summary") parts with a truthy "thought" flag.
        private const string ThoughtKey = "thought";

        // Gemini 3 attaches an opaque signature beside each functionCall and requires it back on replay.
        private const string ThoughtSignatureKey = "thoughtSignature";

        // Google's documented placeholder for replayed function calls that have no real signature.
        private const string SkipThoughtSignatureValidator = "skip_thought_signature_validator";

        private const string SyntheticCallIdPrefix = "gemini-call-";

        #endregion

        #region Public-Members

        /// <summary>
        /// Client-wide default settings, including Gemini settings such as <see cref="GeminiCompletionOptions.TopK"/> and
        /// <see cref="GeminiCompletionOptions.ToolSchemaMode"/>. Default model: gemini-2.5-flash.
        /// </summary>
        public override GeminiCompletionOptions Defaults { get; } = new GeminiCompletionOptions { Model = "gemini-2.5-flash" };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new Gemini completion client.
        /// </summary>
        /// <param name="endpoint">Gemini API endpoint URL. Default: https://generativelanguage.googleapis.com.</param>
        /// <param name="apiKey">Google AI Studio API key, sent as the <c>x-goog-api-key</c> header.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        /// <param name="httpClient">Optional HTTP client; see <see cref="ClientBase"/>. Default: null.</param>
        public GeminiCompletionClient(
            string endpoint = GeminiProtocol.DefaultEndpoint,
            string? apiKey = null,
            LoggingModule? logging = null,
            HttpClient? httpClient = null)
            : base(endpoint, apiKey, logging ?? new LoggingModule(), httpClient)
        {
            _Header = GeminiProtocol.Header;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override Task<bool> ValidateConnectivityAsync(CancellationToken token = default)
        {
            return ProbeAsync(_Endpoint.TrimEnd('/') + "/v1beta/models?pageSize=1", token);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Build the model resource URL, to which <c>:generateContent</c> or <c>:streamGenerateContent</c> is appended.
        /// Vertex AI overrides this with its project and region path.
        /// </summary>
        /// <param name="model">Model name.</param>
        /// <returns>The model URL.</returns>
        protected virtual string BuildModelUrl(string model)
        {
            return GeminiProtocol.ModelUrl(_Endpoint, model);
        }

        /// <inheritdoc />
        protected override Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
        {
            GeminiProtocol.ApplyApiKey(request, _ApiKey);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override Task<ChatResponse> ChatCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token)
        {
            ChatResponse response = new ChatResponse { Model = settings.Model };
            return ExecutePostAsync(response, "chat", ContentUrl(settings.Model, false), BuildPromptBody(prompt, settings, true), ParseChat, token);
        }

        /// <inheritdoc />
        protected override Task<ChatStreamingResponse> ChatStreamingCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token)
        {
            ChatStreamingResponse response = new ChatStreamingResponse { Model = settings.Model };
            return ExecuteStreamingAsync(response, "chat", ContentUrl(settings.Model, true), BuildPromptBody(prompt, settings, true),
                (stream, sw) => response.Chunks = WrapChunksWithTiming(response, ReadChatChunks(stream.Response, stream.Token), sw, stream.Token, stream),
                token);
        }

        /// <inheritdoc />
        protected override Task<ToolChatResponse> ToolChatCoreAsync(ToolChatRequest request, List<ChatMessage> messages, ResolvedCompletion settings, CancellationToken token)
        {
            Dictionary<string, object> body = BuildToolChatBody(request, messages, settings);
            ToolChatResponse response = new ToolChatResponse { Model = settings.Model };
            return ExecutePostAsync(response, "tool chat", ContentUrl(settings.Model, false), body, ParseToolChat, token);
        }

        /// <inheritdoc />
        protected override Task<ToolChatStreamingResponse> ToolChatStreamingCoreAsync(ToolChatRequest request, List<ChatMessage> messages, ResolvedCompletion settings, CancellationToken token)
        {
            Dictionary<string, object> body = BuildToolChatBody(request, messages, settings);
            ToolChatStreamingResponse response = new ToolChatStreamingResponse { Model = settings.Model };
            return ExecuteStreamingAsync(response, "tool chat", ContentUrl(settings.Model, true), body,
                (stream, sw) => response.Chunks = WrapToolChatChunksWithTiming(response, ReadToolChatChunks(stream.Response, stream.Token), sw, stream.Token, stream),
                token);
        }

        /// <inheritdoc />
        protected override Task<GenerationResponse> GenerateCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token)
        {
            GenerationResponse response = new GenerationResponse { Model = settings.Model };
            return ExecutePostAsync(response, "generate", ContentUrl(settings.Model, false), BuildPromptBody(prompt, settings, false), ParseGenerate, token);
        }

        /// <inheritdoc />
        protected override Task<GenerationStreamingResponse> GenerateStreamingCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token)
        {
            GenerationStreamingResponse response = new GenerationStreamingResponse { Model = settings.Model };
            return ExecuteStreamingAsync(response, "generate", ContentUrl(settings.Model, true), BuildPromptBody(prompt, settings, false),
                (stream, sw) => response.Chunks = WrapGenerationChunksWithTiming(response, ReadGenerateChunks(stream.Response, stream.Token), sw, stream.Token, stream),
                token);
        }

        #endregion

        #region Private-Methods

        private string ContentUrl(string model, bool streaming)
        {
            return BuildModelUrl(model) + (streaming ? ":streamGenerateContent?alt=sse" : ":generateContent");
        }

        private Dictionary<string, object> BuildGenerationConfig(ResolvedCompletion settings, bool applyReasoning)
        {
            Dictionary<string, object> config = new Dictionary<string, object> { { "maxOutputTokens", settings.MaxTokens } };

            if (settings.Temperature.HasValue) config["temperature"] = settings.Temperature.Value;
            if (settings.TopP.HasValue) config["topP"] = settings.TopP.Value;

            int? topK = Pick<GeminiCompletionOptions, int>(settings.Call, Defaults, o => o.TopK);
            int? candidateCount = Pick<GeminiCompletionOptions, int>(settings.Call, Defaults, o => o.CandidateCount);
            double? presencePenalty = Pick<GeminiCompletionOptions, double>(settings.Call, Defaults, o => o.PresencePenalty);
            double? frequencyPenalty = Pick<GeminiCompletionOptions, double>(settings.Call, Defaults, o => o.FrequencyPenalty);

            if (topK.HasValue) config["topK"] = topK.Value;
            if (candidateCount.HasValue) config["candidateCount"] = candidateCount.Value;
            if (presencePenalty.HasValue) config["presencePenalty"] = presencePenalty.Value;
            if (frequencyPenalty.HasValue) config["frequencyPenalty"] = frequencyPenalty.Value;

            if (applyReasoning && settings.ReasoningEffort != null)
            {
                config["thinkingConfig"] = new Dictionary<string, object>
                {
                    { "thinkingBudget", settings.ReasoningEffort.ToGeminiThinkingBudget() }
                };
            }

            return config;
        }

        private Dictionary<string, object> BuildPromptBody(string prompt, ResolvedCompletion settings, bool chat)
        {
            Dictionary<string, object> body = new Dictionary<string, object>
            {
                { "contents", new List<Dictionary<string, object>> { TextContent("user", prompt) } },
                { "generationConfig", BuildGenerationConfig(settings, applyReasoning: chat) }
            };

            if (chat && !string.IsNullOrEmpty(settings.SystemPrompt))
            {
                body["systemInstruction"] = new Dictionary<string, object>
                {
                    { "parts", new List<Dictionary<string, object>> { new Dictionary<string, object> { { "text", settings.SystemPrompt } } } }
                };
            }

            return body;
        }

        private static Dictionary<string, object> TextContent(string role, string text)
        {
            return new Dictionary<string, object>
            {
                { "role", role },
                { "parts", new List<Dictionary<string, object>> { new Dictionary<string, object> { { "text", text } } } }
            };
        }

        private Dictionary<string, object> BuildToolChatBody(ToolChatRequest request, List<ChatMessage> messages, ResolvedCompletion settings)
        {
            Dictionary<string, object> body = new Dictionary<string, object>
            {
                { "contents", BuildContents(messages) },
                { "generationConfig", BuildGenerationConfig(settings, applyReasoning: true) }
            };

            Dictionary<string, object>? systemInstruction = BuildSystemInstruction(messages);
            if (systemInstruction != null) body["systemInstruction"] = systemInstruction;

            if (request.Tools != null && request.Tools.Count > 0 && !IsToolChoiceNone(request.ToolChoice))
            {
                GeminiToolSchemaMode mode = Pick<GeminiCompletionOptions, GeminiToolSchemaMode>(settings.Call, Defaults, o => o.ToolSchemaMode)
                    ?? GeminiToolSchemaMode.JsonSchema;
                body["tools"] = BuildTools(request.Tools, mode);
                body["toolConfig"] = BuildToolConfig(request.ToolChoice);
            }

            return body;
        }

        private List<Dictionary<string, object>> BuildContents(List<ChatMessage> messages)
        {
            List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();

            // Gemini requires the function name on every function response. Callers sometimes set only the tool call id,
            // so the name is recovered from the assistant function call with the same id earlier in the conversation.
            Dictionary<string, string> callNames = new Dictionary<string, string>(StringComparer.Ordinal);
            bool previousWasToolResult = false;

            for (int i = 0; i < messages.Count; i++)
            {
                ChatMessage message = messages[i];
                if (string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (message.ToolCalls != null)
                {
                    foreach (ToolCall call in message.ToolCalls)
                    {
                        if (!string.IsNullOrEmpty(call.Id) && !string.IsNullOrEmpty(call.Name)) callNames[call.Id] = call.Name;
                    }
                }

                bool isToolResult = IsToolResultMessage(message);
                List<Dictionary<string, object>> parts = BuildParts(message, i, callNames);

                // Results for parallel function calls belong together in one user turn.
                if (isToolResult && previousWasToolResult && result.Count > 0)
                {
                    ((List<Dictionary<string, object>>)result[result.Count - 1]["parts"]).AddRange(parts);
                    continue;
                }

                result.Add(new Dictionary<string, object>
                {
                    { "role", NormalizeRole(message.Role) },
                    { "parts", parts }
                });
                previousWasToolResult = isToolResult;
            }

            return result;
        }

        private static Dictionary<string, object>? BuildSystemInstruction(List<ChatMessage> messages)
        {
            List<Dictionary<string, object>> parts = new List<Dictionary<string, object>>();

            foreach (ChatMessage message in messages)
            {
                if (string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(message.Content))
                {
                    parts.Add(new Dictionary<string, object> { { "text", message.Content } });
                }
            }

            if (parts.Count == 0) return null;
            return new Dictionary<string, object> { { "parts", parts } };
        }

        private List<Dictionary<string, object>> BuildParts(ChatMessage message, int messageIndex, Dictionary<string, string> callNames)
        {
            List<Dictionary<string, object>> parts = new List<Dictionary<string, object>>();

            if (message.ToolCalls != null && message.ToolCalls.Count > 0)
            {
                if (!string.IsNullOrEmpty(message.Content))
                {
                    parts.Add(new Dictionary<string, object> { { "text", message.Content } });
                }

                // Gemini 3 validates the thought signature on replayed function calls. History that did not come
                // from Gemini (another provider, a hand-built transcript) has none, so the first call of such a turn
                // carries Google's documented placeholder instead. Turns with a real signature are replayed as received.
                bool turnHasSignature = message.ToolCalls.Any(call => !string.IsNullOrEmpty(call.ThoughtSignature));
                bool first = true;

                foreach (ToolCall toolCall in message.ToolCalls)
                {
                    Dictionary<string, object> functionCall = new Dictionary<string, object>
                    {
                        { "name", toolCall.Name },
                        { "args", DeserializeDictionaryOrEmpty(toolCall.ArgumentsJson) }
                    };

                    if (IsProviderCallId(toolCall.Id)) functionCall["id"] = toolCall.Id!;

                    Dictionary<string, object> part = new Dictionary<string, object> { { "functionCall", functionCall } };

                    if (!string.IsNullOrEmpty(toolCall.ThoughtSignature))
                        part[ThoughtSignatureKey] = toolCall.ThoughtSignature;
                    else if (first && !turnHasSignature)
                        part[ThoughtSignatureKey] = SkipThoughtSignatureValidator;

                    parts.Add(part);
                    first = false;
                }

                return parts;
            }

            if (IsToolResultMessage(message))
            {
                Dictionary<string, object> functionResponse = new Dictionary<string, object>
                {
                    { "name", ResolveToolResultName(message, messageIndex, callNames) },
                    { "response", ToFunctionResponse(message.Content) }
                };

                if (IsProviderCallId(message.ToolCallId)) functionResponse["id"] = message.ToolCallId!;

                parts.Add(new Dictionary<string, object> { { "functionResponse", functionResponse } });
                return parts;
            }

            parts.Add(new Dictionary<string, object> { { "text", message.Content ?? string.Empty } });
            return parts;
        }

        private static string ResolveToolResultName(ChatMessage message, int messageIndex, Dictionary<string, string> callNames)
        {
            if (!string.IsNullOrWhiteSpace(message.ToolName)) return message.ToolName;

            if (!string.IsNullOrEmpty(message.ToolCallId) && callNames.TryGetValue(message.ToolCallId, out string? name))
                return name;

            throw new ArgumentException(
                "Tool result message " + messageIndex + " has no ToolName"
                + (string.IsNullOrEmpty(message.ToolCallId)
                    ? " and no ToolCallId"
                    : ", and no earlier assistant tool call has id '" + message.ToolCallId + "'")
                + ". Gemini requires the function name on every tool result; set ChatMessage.ToolName (ChatMessage.ToolResult does).",
                "request");
        }

        private Dictionary<string, object> ToFunctionResponse(string? content)
        {
            // functionResponse.response must be a JSON object. Objects pass through; arrays and scalars are wrapped
            // with their structure intact; empty or non-JSON text is wrapped as a string.
            if (!TryParseJson(content, out JsonElement element))
            {
                return new Dictionary<string, object> { { "result", content ?? string.Empty } };
            }

            if (element.ValueKind == JsonValueKind.Object)
            {
                return _Serializer.DeserializeJson<Dictionary<string, object>>(element.GetRawText()) ?? new Dictionary<string, object>();
            }

            return new Dictionary<string, object> { { "result", element } };
        }

        private List<Dictionary<string, object>> BuildTools(List<ToolDefinition> tools, GeminiToolSchemaMode mode)
        {
            List<Dictionary<string, object>> declarations = new List<Dictionary<string, object>>();

            foreach (ToolDefinition tool in tools)
            {
                Dictionary<string, object> declaration = new Dictionary<string, object>
                {
                    { "name", tool.Name },
                    { "description", tool.Description }
                };

                // A declaration without parameters is valid; an empty schema object adds nothing.
                if (tool.Parameters != null && tool.Parameters.Count > 0)
                {
                    List<string> removed = new List<string>();

                    if (mode == GeminiToolSchemaMode.OpenApiSubset)
                    {
                        using JsonDocument document = JsonDocument.Parse(_Serializer.SerializeJson(tool.Parameters, false));
                        declaration["parameters"] = GeminiProtocol.SanitizeOpenApiSchema(document.RootElement.Clone(), "", removed);
                    }
                    else
                    {
                        Dictionary<string, object> schema = new Dictionary<string, object>(tool.Parameters);
                        if (schema.Remove("$schema")) removed.Add("$schema");
                        declaration["parametersJsonSchema"] = schema;
                    }

                    if (removed.Count > 0)
                    {
                        _Logging.Debug(_Header + "tool '" + tool.Name + "' schema: removed unsupported keys " + string.Join(", ", removed));
                    }
                }

                declarations.Add(declaration);
            }

            return new List<Dictionary<string, object>>
            {
                new Dictionary<string, object> { { "functionDeclarations", declarations } }
            };
        }

        private static Dictionary<string, object> BuildToolConfig(string? toolChoice)
        {
            string mode = "AUTO";
            if (string.Equals(toolChoice, "required", StringComparison.OrdinalIgnoreCase)) mode = "ANY";
            else if (string.Equals(toolChoice, "none", StringComparison.OrdinalIgnoreCase)) mode = "NONE";

            return new Dictionary<string, object>
            {
                { "functionCallingConfig", new Dictionary<string, object> { { "mode", mode } } }
            };
        }

        private static bool IsToolResultMessage(ChatMessage message)
        {
            return string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase)
                || string.Equals(message.Role, "function", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsProviderCallId(string? id)
        {
            // Ids synthesized by this client were never issued by Gemini, so they are not sent back.
            return !string.IsNullOrWhiteSpace(id) && !id.StartsWith(SyntheticCallIdPrefix, StringComparison.Ordinal);
        }

        private static string NormalizeRole(string? role)
        {
            // Gemini contents accept only the roles user and model; function responses travel in a user turn.
            if (string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase)) return "model";
            if (string.Equals(role, "model", StringComparison.OrdinalIgnoreCase)) return "model";
            return "user";
        }

        private static string? ReadThoughtSignature(Dictionary<string, object> part)
        {
            if (!part.ContainsKey(ThoughtSignatureKey)) return null;
            string? value = part[ThoughtSignatureKey]?.ToString();
            return string.IsNullOrEmpty(value) ? null : value;
        }

        /// <summary>
        /// Read the first candidate's parts. Returns null and sets the response error when the response has no candidates.
        /// </summary>
        private List<Dictionary<string, object>>? ReadParts(Dictionary<string, object>? responseObj, ResponseBase response, out Dictionary<string, object>? candidate)
        {
            candidate = null;
            if (responseObj == null || !responseObj.ContainsKey("candidates"))
            {
                string? blockReason = null;
                Dictionary<string, object>? feedback = responseObj == null ? null : ParseNestedObject(responseObj, "promptFeedback");
                if (feedback != null && feedback.ContainsKey("blockReason")) blockReason = feedback["blockReason"]?.ToString();
                response.Error = blockReason != null ? "Prompt blocked: " + blockReason : "Response missing 'candidates' field";
                return null;
            }

            List<Dictionary<string, object>>? candidates = ParseNestedList(responseObj, "candidates");
            if (candidates == null || candidates.Count == 0)
            {
                response.Error = "Response has empty candidates array";
                return null;
            }

            candidate = candidates[0];
            Dictionary<string, object>? content = ParseNestedObject(candidate, "content");
            return content == null ? new List<Dictionary<string, object>>() : ParseNestedList(content, "parts") ?? new List<Dictionary<string, object>>();
        }

        /// <summary>
        /// Split parts into answer text and reasoning (thought) text. For a complete response, whitespace-only text is
        /// reported as null; for a streamed chunk, any non-empty text is kept so spacing between tokens survives.
        /// </summary>
        private static void SplitText(List<Dictionary<string, object>> parts, bool streaming, out string? text, out string? reasoning)
        {
            string combinedText = string.Empty;
            string combinedReasoning = string.Empty;

            foreach (Dictionary<string, object> part in parts)
            {
                if (!part.ContainsKey("text")) continue;
                string? value = part["text"]?.ToString();
                if (string.IsNullOrEmpty(value)) continue;

                // A thought part is reasoning, not answer text; it must never appear in Text.
                if (IsTruthy(part, ThoughtKey)) combinedReasoning += value;
                else combinedText += value;
            }

            if (streaming)
            {
                text = combinedText.Length == 0 ? null : combinedText;
                reasoning = combinedReasoning.Length == 0 ? null : combinedReasoning;
            }
            else
            {
                text = string.IsNullOrWhiteSpace(combinedText) ? null : combinedText;
                reasoning = NormalizeReasoning(combinedReasoning);
            }
        }

        private void ParseChat(string responseBody, ChatResponse response)
        {
            Dictionary<string, object>? responseObj = TryDeserializeObject(responseBody);
            List<Dictionary<string, object>>? parts = ReadParts(responseObj, response, out _);
            if (parts == null) return;

            SplitText(parts, false, out string? text, out string? reasoning);
            response.Text = text;
            response.Reasoning = reasoning;
            response.Usage = ParseUsage(responseObj!);
            if (responseObj!.ContainsKey("modelVersion")) response.Model = responseObj["modelVersion"]?.ToString() ?? response.Model;
        }

        private void ParseGenerate(string responseBody, GenerationResponse response)
        {
            Dictionary<string, object>? responseObj = TryDeserializeObject(responseBody);
            List<Dictionary<string, object>>? parts = ReadParts(responseObj, response, out _);
            if (parts == null) return;

            SplitText(parts, false, out string? text, out _);
            response.Text = text;
        }

        private void ParseToolChat(string responseBody, ToolChatResponse response)
        {
            Dictionary<string, object>? responseObj = TryDeserializeObject(responseBody);
            if (responseObj != null)
            {
                response.ResponseId = responseObj.ContainsKey("responseId") ? responseObj["responseId"]?.ToString() : null;
                if (responseObj.ContainsKey("modelVersion")) response.Model = responseObj["modelVersion"]?.ToString() ?? response.Model;
                response.Usage = ParseUsage(responseObj);
            }

            List<Dictionary<string, object>>? parts = ReadParts(responseObj, response, out Dictionary<string, object>? candidate);
            if (parts == null) return;

            response.FinishReason = candidate!.ContainsKey("finishReason") ? candidate["finishReason"]?.ToString() : null;

            SplitText(parts, false, out string? text, out string? reasoning);
            response.Text = text;
            response.Reasoning = reasoning;

            int index = 0;
            foreach (Dictionary<string, object> part in parts)
            {
                if (!part.ContainsKey("functionCall")) continue;
                ToolCall? toolCall = ParseToolCall(part, index);
                if (toolCall != null) response.ToolCalls.Add(toolCall);
                index++;
            }
        }

        private ToolCall? ParseToolCall(Dictionary<string, object> part, int index)
        {
            Dictionary<string, object>? functionCall = ParseNestedObject(part, "functionCall");
            if (functionCall == null || !functionCall.ContainsKey("name")) return null;

            ToolCall toolCall = new ToolCall();
            toolCall.Id = functionCall.ContainsKey("id") ? functionCall["id"]?.ToString() : null;
            if (string.IsNullOrWhiteSpace(toolCall.Id)) toolCall.Id = SyntheticCallIdPrefix + index;
            toolCall.Name = functionCall["name"]?.ToString() ?? string.Empty;
            toolCall.ArgumentsJson = functionCall.ContainsKey("args") && functionCall["args"] != null
                ? _Serializer.SerializeJson(functionCall["args"], false)
                : "{}";
            toolCall.ThoughtSignature = ReadThoughtSignature(part);
            return toolCall;
        }

        private TokenUsage? ParseUsage(Dictionary<string, object> responseObj)
        {
            Dictionary<string, object>? usageObj = ParseNestedObject(responseObj, "usageMetadata");
            if (usageObj == null) return null;

            TokenUsage usage = new TokenUsage();
            usage.PromptTokens = TryGetInt(usageObj, "promptTokenCount");
            usage.CompletionTokens = TryGetInt(usageObj, "candidatesTokenCount");
            usage.TotalTokens = TryGetInt(usageObj, "totalTokenCount");

            // cachedContentTokenCount is a subset of promptTokenCount; thoughtsTokenCount is the separately
            // billed thinking-token count. Both are absent unless the request enabled caching / thinking.
            usage.CachedPromptTokens = TryGetInt(usageObj, "cachedContentTokenCount");
            usage.ReasoningTokens = TryGetInt(usageObj, "thoughtsTokenCount");
            return usage;
        }

        private async IAsyncEnumerable<Dictionary<string, object>> ReadEvents(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken token)
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
                if (data == "[DONE]") yield break;

                Dictionary<string, object>? chunk = TryDeserializeObject(data);
                if (chunk != null) yield return chunk;
            }
        }

        private List<Dictionary<string, object>>? ReadChunkParts(Dictionary<string, object> chunk, out string? finishReason)
        {
            finishReason = null;
            List<Dictionary<string, object>>? candidates = ParseNestedList(chunk, "candidates");
            if (candidates == null || candidates.Count == 0) return null;

            Dictionary<string, object> candidate = candidates[0];
            if (candidate.ContainsKey("finishReason")) finishReason = candidate["finishReason"]?.ToString();

            Dictionary<string, object>? content = ParseNestedObject(candidate, "content");
            return content == null ? null : ParseNestedList(content, "parts");
        }

        private async IAsyncEnumerable<ChatStreamingChunk> ReadChatChunks(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken token)
        {
            await foreach (Dictionary<string, object> chunk in ReadEvents(response, token).ConfigureAwait(false))
            {
                ChatStreamingChunk streamChunk = new ChatStreamingChunk();
                streamChunk.ResponseId = chunk.ContainsKey("responseId") ? chunk["responseId"]?.ToString() : null;
                streamChunk.Model = chunk.ContainsKey("modelVersion") ? chunk["modelVersion"]?.ToString() : null;
                streamChunk.CreatedUtc = DateTime.UtcNow;

                List<Dictionary<string, object>>? parts = ReadChunkParts(chunk, out string? finishReason);
                if (finishReason != null)
                {
                    streamChunk.FinishReason = finishReason;
                    streamChunk.Done = true;
                }

                if (parts != null)
                {
                    SplitText(parts, true, out string? text, out string? reasoning);
                    streamChunk.Text = text;
                    streamChunk.ReasoningText = reasoning;
                }

                streamChunk.Usage = ParseUsage(chunk);
                yield return streamChunk;
            }
        }

        private async IAsyncEnumerable<ToolChatStreamingChunk> ReadToolChatChunks(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken token)
        {
            int nextToolCallIndex = 0;

            await foreach (Dictionary<string, object> chunk in ReadEvents(response, token).ConfigureAwait(false))
            {
                ToolChatStreamingChunk streamChunk = new ToolChatStreamingChunk();
                streamChunk.ResponseId = chunk.ContainsKey("responseId") ? chunk["responseId"]?.ToString() : null;
                streamChunk.Model = chunk.ContainsKey("modelVersion") ? chunk["modelVersion"]?.ToString() : null;
                streamChunk.CreatedUtc = DateTime.UtcNow;

                List<Dictionary<string, object>>? parts = ReadChunkParts(chunk, out string? finishReason);
                if (finishReason != null)
                {
                    streamChunk.FinishReason = finishReason;
                    streamChunk.Done = true;
                }

                if (parts != null)
                {
                    SplitText(parts, true, out string? text, out string? reasoning);
                    streamChunk.Text = text;
                    streamChunk.ReasoningText = reasoning;

                    foreach (Dictionary<string, object> part in parts)
                    {
                        if (!part.ContainsKey("functionCall")) continue;

                        ToolCallDelta? delta = ParseToolCallDelta(part, nextToolCallIndex);
                        if (delta != null)
                        {
                            streamChunk.ToolCallDeltas.Add(delta);
                            nextToolCallIndex = Math.Max(nextToolCallIndex, delta.Index + 1);
                        }
                        else
                        {
                            nextToolCallIndex++;
                        }
                    }
                }

                streamChunk.Usage = ParseUsage(chunk);
                yield return streamChunk;
            }
        }

        private ToolCallDelta? ParseToolCallDelta(Dictionary<string, object> part, int fallbackIndex)
        {
            Dictionary<string, object>? functionCall = ParseNestedObject(part, "functionCall");
            if (functionCall == null || !functionCall.ContainsKey("name")) return null;

            int index = TryGetInt(part, "index") ?? TryGetInt(functionCall, "index") ?? fallbackIndex;

            ToolCallDelta delta = new ToolCallDelta();
            delta.Index = index;
            delta.Id = functionCall.ContainsKey("id") ? functionCall["id"]?.ToString() : null;
            if (string.IsNullOrWhiteSpace(delta.Id)) delta.Id = SyntheticCallIdPrefix + index;
            delta.Type = "function";
            delta.Name = functionCall["name"]?.ToString() ?? string.Empty;
            delta.ThoughtSignature = ReadThoughtSignature(part);

            if (functionCall.ContainsKey("args") && functionCall["args"] != null)
                delta.ArgumentsJson = _Serializer.SerializeJson(functionCall["args"], false);

            return delta;
        }

        private async IAsyncEnumerable<GenerationStreamingChunk> ReadGenerateChunks(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken token)
        {
            await foreach (Dictionary<string, object> chunk in ReadEvents(response, token).ConfigureAwait(false))
            {
                GenerationStreamingChunk streamChunk = new GenerationStreamingChunk();
                streamChunk.Model = chunk.ContainsKey("modelVersion") ? chunk["modelVersion"]?.ToString() : null;

                List<Dictionary<string, object>>? parts = ReadChunkParts(chunk, out string? finishReason);
                if (finishReason != null) streamChunk.Done = true;

                if (parts != null)
                {
                    SplitText(parts, true, out string? text, out _);
                    streamChunk.Text = text;
                }

                yield return streamChunk;
            }
        }

        #endregion
    }
}
