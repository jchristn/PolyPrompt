namespace PolyPrompt.Clients
{
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Text;
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using SyslogLogging;

    /// <summary>
    /// Client for the Anthropic (Claude) Messages API supporting chat completions, tool calling, and text
    /// generation. Anthropic has no embeddings API and no legacy completions endpoint; embeddings throw
    /// <see cref="NotSupportedException"/> and text generation is mapped onto the Messages API as a
    /// single-user-turn request.
    /// </summary>
    public class AnthropicClient : CompletionClientBase
    {
        #region Private-Members

        // Anthropic wire literals, centralized so each is defined once rather than inlined.
        private const string ApiKeyHeader = "x-api-key";
        private const string VersionHeader = "anthropic-version";
        private const string WorkspaceHeader = "anthropic-workspace-id";
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

        private string _AnthropicVersion = "2023-06-01";
        private string? _WorkspaceId = null;
        private int _ModelsPageLimit = 1000;

        #endregion

        #region Public-Members

        /// <summary>
        /// Value sent in the anthropic-version request header. Default: 2023-06-01. Must be non-empty;
        /// changing it updates the header on subsequent requests.
        /// </summary>
        public string AnthropicVersion
        {
            get { return _AnthropicVersion; }
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    throw new ArgumentNullException(nameof(AnthropicVersion));

                _AnthropicVersion = value;
                if (_HttpClient.DefaultRequestHeaders.Contains(VersionHeader))
                    _HttpClient.DefaultRequestHeaders.Remove(VersionHeader);
                _HttpClient.DefaultRequestHeaders.Add(VersionHeader, value);
            }
        }

        /// <summary>
        /// Workspace identifier sent in the anthropic-workspace-id request header. Default: null (no header
        /// is sent). Identity-linked Anthropic API keys reject requests without it; standard workspace API
        /// keys do not require it. Set to null to remove the header.
        /// </summary>
        public string? WorkspaceId
        {
            get { return _WorkspaceId; }
            set
            {
                _WorkspaceId = string.IsNullOrWhiteSpace(value) ? null : value;
                if (_HttpClient.DefaultRequestHeaders.Contains(WorkspaceHeader))
                    _HttpClient.DefaultRequestHeaders.Remove(WorkspaceHeader);
                if (_WorkspaceId != null)
                    _HttpClient.DefaultRequestHeaders.Add(WorkspaceHeader, _WorkspaceId);
            }
        }

        /// <summary>
        /// Page size requested from the models list endpoint. Clamped to 1..1,000 (the API maximum).
        /// Default: 1,000. ListModelsAsync follows pagination until the provider reports no more pages, so
        /// this affects request count, not result completeness.
        /// </summary>
        public int ModelsPageLimit
        {
            get { return _ModelsPageLimit; }
            set { _ModelsPageLimit = Math.Clamp(value, 1, 1000); }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new AnthropicClient.
        /// </summary>
        /// <param name="endpoint">Anthropic API endpoint URL. Default: https://api.anthropic.com.</param>
        /// <param name="apiKey">Anthropic API key (required). Sent as the x-api-key header; Anthropic does not use bearer authorization. Default: null.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        /// <param name="httpClient">Optional HTTP client. When supplied, the caller owns and disposes it; use this to configure the transport (custom handler, TLS, proxy). Default: null (an internally owned client is created).</param>
        public AnthropicClient(
            string endpoint = "https://api.anthropic.com",
            string? apiKey = null,
            LoggingModule? logging = null,
            HttpClient? httpClient = null)
            : base(endpoint, apiKey, logging ?? new LoggingModule(), httpClient)
        {
            _Header = "[Anthropic] ";
            Model = "claude-opus-4-8";

            if (!string.IsNullOrEmpty(apiKey))
            {
                _HttpClient.DefaultRequestHeaders.Add(ApiKeyHeader, apiKey);
            }

            _HttpClient.DefaultRequestHeaders.Add(VersionHeader, _AnthropicVersion);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override async Task<ChatResponse> ChatAsync(
            string prompt,
            ChatCompletionOptions? options = null,
            CancellationToken token = default)
        {
            ResolveOptions(options, out int maxTokens, out double? temperature, out double? topP, out string? systemPrompt);

            ChatResponse chatResponse = new ChatResponse();
            chatResponse.Model = Model;

            Stopwatch sw = Stopwatch.StartNew();

            string url = BuildMessagesUrl();

            Dictionary<string, object> requestBody = BuildSinglePromptRequestBody(
                Model, prompt, maxTokens, systemPrompt, temperature, topP,
                (options as AnthropicChatCompletionOptions)?.TopK,
                (options as AnthropicChatCompletionOptions)?.StopSequences,
                false);

            string json = _Serializer.SerializeJson(requestBody, false);
            StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

            _Logging.Debug(_Header + "POST " + url);

            try
            {
                CompletionHttpResult result = await PostAndRecordAsync(url, content, json, token).ConfigureAwait(false);
                string responseBody = result.ResponseBody;

                chatResponse.StatusCode = result.StatusCode;

                if (!result.IsSuccessStatusCode)
                {
                    _Logging.Warn(_Header + "chat request failed with status " + result.StatusCode + ": " + responseBody);
                    chatResponse.Success = false;
                    chatResponse.Error = "HTTP " + result.StatusCode + ": " + responseBody;
                    return chatResponse;
                }

                chatResponse.Text = ExtractTextFromResponse(responseBody);
                chatResponse.Reasoning = ExtractReasoningFromResponse(responseBody);
                chatResponse.Success = true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                chatResponse.Success = false;
                chatResponse.Error = ex.Message;
            }
            finally
            {
                sw.Stop();
                chatResponse.OverallRuntimeMs = sw.ElapsedMilliseconds;
            }

            return chatResponse;
        }

        /// <inheritdoc />
        public override async Task<ChatStreamingResponse> ChatStreamingAsync(
            string prompt,
            ChatCompletionOptions? options = null,
            CancellationToken token = default)
        {
            ResolveOptions(options, out int maxTokens, out double? temperature, out double? topP, out string? systemPrompt);

            string url = BuildMessagesUrl();

            Dictionary<string, object> requestBody = BuildSinglePromptRequestBody(
                Model, prompt, maxTokens, systemPrompt, temperature, topP,
                (options as AnthropicChatCompletionOptions)?.TopK,
                (options as AnthropicChatCompletionOptions)?.StopSequences,
                true);

            string json = _Serializer.SerializeJson(requestBody, false);
            StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

            _Logging.Debug(_Header + "POST (streaming) " + url);

            Stopwatch sw = Stopwatch.StartNew();

            ChatStreamingResponse streamingResponse = new ChatStreamingResponse();
            streamingResponse.Model = Model;

            try
            {
                StreamingHttpResult streamingResult = await PostStreamingAsync(url, content, token).ConfigureAwait(false);
                HttpResponseMessage response = streamingResult.Response;
                streamingResponse.StatusCode = (int)response.StatusCode;

                if (!response.IsSuccessStatusCode)
                {
                    using (streamingResult)
                    {
                        string errorBody = await response.Content.ReadAsStringAsync(streamingResult.Token).ConfigureAwait(false);
                        _Logging.Warn(_Header + "streaming chat request failed with status " + (int)response.StatusCode + ": " + errorBody);
                        streamingResponse.Success = false;
                        streamingResponse.Error = "HTTP " + (int)response.StatusCode + ": " + errorBody;
                    }
                    return streamingResponse;
                }

                streamingResponse.Success = true;
                streamingResponse.Chunks = WrapChunksWithTiming(streamingResponse, ReadAnthropicChatChunks(response, streamingResult.Token), sw, streamingResult.Token, streamingResult);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                streamingResponse.Success = false;
                streamingResponse.Error = ex.Message;
            }

            return streamingResponse;
        }

        /// <inheritdoc />
        public override async Task<ToolChatResponse> ToolChatAsync(
            ToolChatRequest request,
            CancellationToken token = default)
        {
            ResolveToolChatRequest(request, out string model, out int maxTokens, out double? temperature, out double? topP, out ReasoningEffort? reasoningEffort);

            ToolChatResponse toolResponse = new ToolChatResponse();
            toolResponse.Model = model;

            Stopwatch sw = Stopwatch.StartNew();

            string url = BuildMessagesUrl();

            Dictionary<string, object> requestBody = BuildToolChatRequestBody(request, model, maxTokens, temperature, topP, reasoningEffort, false);

            string json = _Serializer.SerializeJson(requestBody, false);
            StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

            _Logging.Debug(_Header + "POST " + url);

            try
            {
                CompletionHttpResult result = await PostAndRecordAsync(url, content, json, token).ConfigureAwait(false);
                string responseBody = result.ResponseBody;

                toolResponse.StatusCode = result.StatusCode;

                if (!result.IsSuccessStatusCode)
                {
                    _Logging.Warn(_Header + "tool chat request failed with status " + result.StatusCode + ": " + responseBody);
                    toolResponse.Success = false;
                    toolResponse.Error = "HTTP " + result.StatusCode + ": " + responseBody;
                    return toolResponse;
                }

                PopulateAnthropicToolChatResponse(responseBody, toolResponse);
                toolResponse.Success = string.IsNullOrEmpty(toolResponse.Error);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                toolResponse.Success = false;
                toolResponse.Error = ex.Message;
            }
            finally
            {
                sw.Stop();
                toolResponse.OverallRuntimeMs = sw.ElapsedMilliseconds;
            }

            return toolResponse;
        }

        /// <inheritdoc />
        public override async Task<ToolChatStreamingResponse> ToolChatStreamingAsync(
            ToolChatRequest request,
            CancellationToken token = default)
        {
            ResolveToolChatRequest(request, out string model, out int maxTokens, out double? temperature, out double? topP, out ReasoningEffort? reasoningEffort);

            string url = BuildMessagesUrl();

            Dictionary<string, object> requestBody = BuildToolChatRequestBody(request, model, maxTokens, temperature, topP, reasoningEffort, true);

            string json = _Serializer.SerializeJson(requestBody, false);
            StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

            _Logging.Debug(_Header + "POST (streaming tool chat) " + url);

            Stopwatch sw = Stopwatch.StartNew();

            ToolChatStreamingResponse streamingResponse = new ToolChatStreamingResponse();
            streamingResponse.Model = model;

            try
            {
                StreamingHttpResult streamingResult = await PostStreamingAsync(url, content, token).ConfigureAwait(false);
                HttpResponseMessage response = streamingResult.Response;
                streamingResponse.StatusCode = (int)response.StatusCode;

                if (!response.IsSuccessStatusCode)
                {
                    using (streamingResult)
                    {
                        string errorBody = await response.Content.ReadAsStringAsync(streamingResult.Token).ConfigureAwait(false);
                        _Logging.Warn(_Header + "streaming tool chat request failed with status " + (int)response.StatusCode + ": " + errorBody);
                        streamingResponse.Success = false;
                        streamingResponse.Error = "HTTP " + (int)response.StatusCode + ": " + errorBody;
                    }
                    return streamingResponse;
                }

                streamingResponse.Success = true;
                streamingResponse.Chunks = WrapToolChatChunksWithTiming(streamingResponse, ReadAnthropicToolChatChunks(response, streamingResult.Token), sw, streamingResult.Token, streamingResult);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                streamingResponse.Success = false;
                streamingResponse.Error = ex.Message;
            }

            return streamingResponse;
        }

        /// <summary>
        /// Not supported. Anthropic does not provide an embeddings API.
        /// </summary>
        /// <param name="input">The text to embed.</param>
        /// <param name="options">Optional per-call overrides.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Never returns; always throws.</returns>
        /// <exception cref="NotSupportedException">Always thrown; Anthropic has no embeddings endpoint.</exception>
        public override Task<EmbeddingResponse> EmbedAsync(
            string input,
            EmbeddingOptions? options = null,
            CancellationToken token = default)
        {
            throw new NotSupportedException("Anthropic does not provide an embeddings API. Use a different provider (for example Ollama or OpenAI) for embeddings.");
        }

        /// <summary>
        /// Not supported. Anthropic does not provide an embeddings API.
        /// </summary>
        /// <param name="inputs">The list of texts to embed.</param>
        /// <param name="options">Optional per-call overrides.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Never returns; always throws.</returns>
        /// <exception cref="NotSupportedException">Always thrown; Anthropic has no embeddings endpoint.</exception>
        public override Task<EmbeddingResponse> EmbedAsync(
            List<string> inputs,
            EmbeddingOptions? options = null,
            CancellationToken token = default)
        {
            throw new NotSupportedException("Anthropic does not provide an embeddings API. Use a different provider (for example Ollama or OpenAI) for embeddings.");
        }

        /// <inheritdoc />
        public override async Task<GenerationResponse> GenerateAsync(
            string prompt,
            GenerationOptions? options = null,
            CancellationToken token = default)
        {
            ResolveGenerationOptions(options, out string model, out int maxTokens, out double? temperature, out double? topP);

            GenerationResponse genResponse = new GenerationResponse();
            genResponse.Model = model;

            Stopwatch sw = Stopwatch.StartNew();

            string url = BuildMessagesUrl();

            Dictionary<string, object> requestBody = BuildSinglePromptRequestBody(
                model, prompt, maxTokens, null, temperature, topP,
                (options as AnthropicGenerationOptions)?.TopK,
                (options as AnthropicGenerationOptions)?.StopSequences,
                false);

            string json = _Serializer.SerializeJson(requestBody, false);
            StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

            _Logging.Debug(_Header + "POST " + url);

            try
            {
                CompletionHttpResult result = await PostAndRecordAsync(url, content, json, token).ConfigureAwait(false);
                string responseBody = result.ResponseBody;

                genResponse.StatusCode = result.StatusCode;

                if (!result.IsSuccessStatusCode)
                {
                    _Logging.Warn(_Header + "generate request failed with status " + result.StatusCode + ": " + responseBody);
                    genResponse.Success = false;
                    genResponse.Error = "HTTP " + result.StatusCode + ": " + responseBody;
                    return genResponse;
                }

                genResponse.Text = ExtractTextFromResponse(responseBody);
                genResponse.Success = true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                genResponse.Success = false;
                genResponse.Error = ex.Message;
            }
            finally
            {
                sw.Stop();
                genResponse.OverallRuntimeMs = sw.ElapsedMilliseconds;
            }

            return genResponse;
        }

        /// <inheritdoc />
        public override async Task<GenerationStreamingResponse> GenerateStreamingAsync(
            string prompt,
            GenerationOptions? options = null,
            CancellationToken token = default)
        {
            ResolveGenerationOptions(options, out string model, out int maxTokens, out double? temperature, out double? topP);

            string url = BuildMessagesUrl();

            Dictionary<string, object> requestBody = BuildSinglePromptRequestBody(
                model, prompt, maxTokens, null, temperature, topP,
                (options as AnthropicGenerationOptions)?.TopK,
                (options as AnthropicGenerationOptions)?.StopSequences,
                true);

            string json = _Serializer.SerializeJson(requestBody, false);
            StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

            _Logging.Debug(_Header + "POST (streaming) " + url);

            Stopwatch sw = Stopwatch.StartNew();

            GenerationStreamingResponse streamingResponse = new GenerationStreamingResponse();
            streamingResponse.Model = model;

            try
            {
                StreamingHttpResult streamingResult = await PostStreamingAsync(url, content, token).ConfigureAwait(false);
                HttpResponseMessage response = streamingResult.Response;
                streamingResponse.StatusCode = (int)response.StatusCode;

                if (!response.IsSuccessStatusCode)
                {
                    using (streamingResult)
                    {
                        string errorBody = await response.Content.ReadAsStringAsync(streamingResult.Token).ConfigureAwait(false);
                        _Logging.Warn(_Header + "streaming generate request failed with status " + (int)response.StatusCode + ": " + errorBody);
                        streamingResponse.Success = false;
                        streamingResponse.Error = "HTTP " + (int)response.StatusCode + ": " + errorBody;
                    }
                    return streamingResponse;
                }

                streamingResponse.Success = true;
                streamingResponse.Chunks = WrapGenerationChunksWithTiming(streamingResponse, ReadAnthropicGenerateChunks(response, streamingResult.Token), sw, streamingResult.Token, streamingResult);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                streamingResponse.Success = false;
                streamingResponse.Error = ex.Message;
            }

            return streamingResponse;
        }

        /// <inheritdoc />
        public override async IAsyncEnumerable<ModelInformation> ListModelsAsync(
            [EnumeratorCancellation] CancellationToken token = default)
        {
            string? afterId = null;
            bool hasMore = true;

            while (hasMore)
            {
                token.ThrowIfCancellationRequested();

                string url = _Endpoint.TrimEnd('/') + "/v1/models?limit=" + _ModelsPageLimit;
                if (!string.IsNullOrEmpty(afterId))
                {
                    url += "&after_id=" + Uri.EscapeDataString(afterId);
                }

                _Logging.Debug(_Header + "GET " + url);

                CompletionHttpResult result = await GetAndRecordAsync(url, token).ConfigureAwait(false);

                if (!result.IsSuccessStatusCode)
                {
                    _Logging.Warn(_Header + "list models failed with status " + result.StatusCode);
                    yield break;
                }

                Dictionary<string, object>? responseObj = _Serializer.DeserializeJson<Dictionary<string, object>>(result.ResponseBody);
                if (responseObj == null || !responseObj.ContainsKey("data"))
                    yield break;

                string dataJson = _Serializer.SerializeJson(responseObj["data"], false);
                List<Dictionary<string, object>>? dataList = _Serializer.DeserializeJson<List<Dictionary<string, object>>>(dataJson);
                if (dataList == null || dataList.Count == 0)
                    yield break;

                foreach (Dictionary<string, object> modelObj in dataList)
                {
                    token.ThrowIfCancellationRequested();
                    yield return ParseModelInformation(modelObj, string.Empty);
                }

                hasMore = IsTruthy(responseObj, "has_more");
                afterId = responseObj.ContainsKey("last_id") ? responseObj["last_id"]?.ToString() : null;
                if (string.IsNullOrEmpty(afterId)) hasMore = false;
            }
        }

        /// <inheritdoc />
        public override async Task<ModelInformation?> GetModelInformationAsync(string model, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(model))
                throw new ArgumentNullException(nameof(model));

            string url = _Endpoint.TrimEnd('/') + "/v1/models/" + Uri.EscapeDataString(model);
            _Logging.Debug(_Header + "GET " + url);

            try
            {
                CompletionHttpResult result = await GetAndRecordAsync(url, token).ConfigureAwait(false);
                string responseBody = result.ResponseBody;

                if (!result.IsSuccessStatusCode)
                {
                    _Logging.Warn(_Header + "get model failed with status " + result.StatusCode + ": " + responseBody);
                    return null;
                }

                Dictionary<string, object>? responseObj = _Serializer.DeserializeJson<Dictionary<string, object>>(responseBody);
                if (responseObj == null) return null;

                return ParseModelInformation(responseObj, model);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "get model failed: " + ex.Message);
                return null;
            }
        }

        #endregion

        #region Private-Methods

        private string BuildMessagesUrl()
        {
            return _Endpoint.TrimEnd('/') + "/v1/messages";
        }

        private Dictionary<string, object> BuildSinglePromptRequestBody(
            string model, string prompt, int maxTokens, string? systemPrompt,
            double? temperature, double? topP,
            int? topK, List<string>? stopSequences, bool stream)
        {
            List<Dictionary<string, object>> messages = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    { "role", "user" },
                    { "content", prompt }
                }
            };

            Dictionary<string, object> requestBody = new Dictionary<string, object>
            {
                { "model", model },
                { "max_tokens", maxTokens },
                { "messages", messages }
            };

            if (!string.IsNullOrEmpty(systemPrompt)) requestBody["system"] = systemPrompt;
            if (temperature.HasValue) requestBody["temperature"] = temperature.Value;
            if (topP.HasValue) requestBody["top_p"] = topP.Value;
            if (topK.HasValue) requestBody["top_k"] = topK.Value;
            if (stopSequences != null && stopSequences.Count > 0) requestBody["stop_sequences"] = stopSequences;
            if (stream) requestBody["stream"] = true;

            return requestBody;
        }

        private Dictionary<string, object> BuildToolChatRequestBody(
            ToolChatRequest request,
            string model,
            int maxTokens,
            double? temperature,
            double? topP,
            ReasoningEffort? reasoningEffort,
            bool stream)
        {
            Dictionary<string, object> requestBody = new Dictionary<string, object>
            {
                { "model", model },
                { "max_tokens", maxTokens },
                { "messages", BuildAnthropicMessages(request.Messages) }
            };

            string? system = BuildAnthropicSystem(request.Messages);
            if (system != null) requestBody["system"] = system;

            if (temperature.HasValue) requestBody["temperature"] = temperature.Value;
            if (topP.HasValue) requestBody["top_p"] = topP.Value;

            if (request.Tools != null && request.Tools.Count > 0 && !IsToolChoiceNone(request.ToolChoice))
            {
                requestBody["tools"] = BuildAnthropicTools(request.Tools);
                requestBody["tool_choice"] = BuildAnthropicToolChoice(request.ToolChoice);
            }

            if (reasoningEffort != null)
            {
                requestBody["output_config"] = new Dictionary<string, object>
                {
                    { "effort", reasoningEffort.ToAnthropicEffort() }
                };

                if (reasoningEffort.SendsAnthropicThinking())
                {
                    requestBody["thinking"] = new Dictionary<string, object>
                    {
                        { "type", "adaptive" },
                        { "display", "summarized" }
                    };
                }
            }

            if (stream) requestBody["stream"] = true;

            return requestBody;
        }

        private List<Dictionary<string, object>> BuildAnthropicMessages(List<ChatMessage> messages)
        {
            List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();
            List<Dictionary<string, object>>? pendingToolResults = null;
            int syntheticToolCallIndex = 0;

            foreach (ChatMessage message in messages)
            {
                if (string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase))
                    continue;

                bool isToolResult = string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(message.Role, "function", StringComparison.OrdinalIgnoreCase);

                if (isToolResult)
                {
                    // Tool results are user-role tool_result content blocks. Consecutive results are merged
                    // into a single user turn so parallel tool calls resolve in one message.
                    if (pendingToolResults == null) pendingToolResults = new List<Dictionary<string, object>>();
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
                        blocks.Add(new Dictionary<string, object>
                        {
                            { "type", TextBlockType },
                            { "text", message.Content }
                        });
                    }

                    foreach (ToolCall toolCall in message.ToolCalls)
                    {
                        string toolUseId = string.IsNullOrWhiteSpace(toolCall.Id)
                            ? "anthropic-call-" + syntheticToolCallIndex
                            : toolCall.Id;
                        syntheticToolCallIndex++;

                        blocks.Add(new Dictionary<string, object>
                        {
                            { "type", ToolUseBlockType },
                            { "id", toolUseId },
                            { "name", toolCall.Name },
                            { "input", DeserializeDictionaryOrEmpty(toolCall.ArgumentsJson) }
                        });
                    }

                    result.Add(new Dictionary<string, object>
                    {
                        { "role", "assistant" },
                        { "content", blocks }
                    });
                    continue;
                }

                result.Add(new Dictionary<string, object>
                {
                    { "role", NormalizeAnthropicRole(message.Role) },
                    { "content", message.Content ?? string.Empty }
                });
            }

            FlushPendingToolResults(result, ref pendingToolResults);

            return result;
        }

        private static void FlushPendingToolResults(
            List<Dictionary<string, object>> result,
            ref List<Dictionary<string, object>>? pendingToolResults)
        {
            if (pendingToolResults == null || pendingToolResults.Count == 0) return;

            result.Add(new Dictionary<string, object>
            {
                { "role", "user" },
                { "content", pendingToolResults }
            });

            pendingToolResults = null;
        }

        private static string? BuildAnthropicSystem(List<ChatMessage> messages)
        {
            List<string> parts = new List<string>();

            foreach (ChatMessage message in messages)
            {
                if (string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(message.Content))
                {
                    parts.Add(message.Content);
                }
            }

            if (parts.Count == 0) return null;
            return string.Join("\n\n", parts);
        }

        private List<Dictionary<string, object>> BuildAnthropicTools(List<ToolDefinition> tools)
        {
            List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();

            foreach (ToolDefinition tool in tools)
            {
                result.Add(new Dictionary<string, object>
                {
                    { "name", tool.Name },
                    { "description", tool.Description },
                    { "input_schema", tool.Parameters }
                });
            }

            return result;
        }

        private static Dictionary<string, object> BuildAnthropicToolChoice(string? toolChoice)
        {
            if (string.IsNullOrWhiteSpace(toolChoice) || string.Equals(toolChoice, "auto", StringComparison.OrdinalIgnoreCase))
            {
                return new Dictionary<string, object> { { "type", "auto" } };
            }

            if (string.Equals(toolChoice, "required", StringComparison.OrdinalIgnoreCase)
                || string.Equals(toolChoice, "any", StringComparison.OrdinalIgnoreCase))
            {
                return new Dictionary<string, object> { { "type", "any" } };
            }

            // A specific tool name forces that tool.
            return new Dictionary<string, object>
            {
                { "type", "tool" },
                { "name", toolChoice }
            };
        }

        private static string ResolveToolUseId(ChatMessage message)
        {
            if (!string.IsNullOrWhiteSpace(message.ToolCallId)) return message.ToolCallId;
            if (!string.IsNullOrWhiteSpace(message.ToolName)) return message.ToolName;
            return "tool";
        }

        private static string NormalizeAnthropicRole(string? role)
        {
            if (string.Equals(role, "model", StringComparison.OrdinalIgnoreCase)) return "assistant";
            if (string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase)) return "assistant";
            return "user";
        }

        private static bool IsToolChoiceNone(string? toolChoice)
        {
            return string.Equals(toolChoice, "none", StringComparison.OrdinalIgnoreCase);
        }

        private Dictionary<string, object> DeserializeDictionaryOrEmpty(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, object>();

            Dictionary<string, object>? parsed = _Serializer.DeserializeJson<Dictionary<string, object>>(json);
            return parsed ?? new Dictionary<string, object>();
        }

        private void PopulateAnthropicToolChatResponse(string responseBody, ToolChatResponse toolResponse)
        {
            Dictionary<string, object>? responseObj = _Serializer.DeserializeJson<Dictionary<string, object>>(responseBody);
            if (responseObj == null || !responseObj.ContainsKey("content"))
            {
                toolResponse.Success = false;
                toolResponse.Error = "Response missing 'content' field";
                return;
            }

            toolResponse.ResponseId = responseObj.ContainsKey("id") ? responseObj["id"]?.ToString() : null;
            toolResponse.Model = responseObj.ContainsKey("model") ? responseObj["model"]?.ToString() ?? toolResponse.Model : toolResponse.Model;
            toolResponse.FinishReason = responseObj.ContainsKey("stop_reason") ? responseObj["stop_reason"]?.ToString() : null;

            List<Dictionary<string, object>>? blocks = ParseContentBlocks(responseObj);
            if (blocks == null) return;

            foreach (Dictionary<string, object> block in blocks)
            {
                string? blockType = block.ContainsKey("type") ? block["type"]?.ToString() : null;

                if (string.Equals(blockType, TextBlockType, StringComparison.Ordinal) && block.ContainsKey("text"))
                {
                    string? text = block["text"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        toolResponse.Text = string.IsNullOrEmpty(toolResponse.Text)
                            ? text
                            : toolResponse.Text + text;
                    }
                }
                else if (string.Equals(blockType, ThinkingBlockType, StringComparison.Ordinal) && block.ContainsKey(ThinkingBlockType))
                {
                    string? thinking = block[ThinkingBlockType]?.ToString();
                    if (!string.IsNullOrWhiteSpace(thinking))
                    {
                        toolResponse.Reasoning = string.IsNullOrEmpty(toolResponse.Reasoning)
                            ? thinking
                            : toolResponse.Reasoning + thinking;
                    }
                }
                else if (string.Equals(blockType, ToolUseBlockType, StringComparison.Ordinal))
                {
                    ToolCall? toolCall = ParseAnthropicToolUseBlock(block);
                    if (toolCall != null) toolResponse.ToolCalls.Add(toolCall);
                }
            }

            toolResponse.Reasoning = NormalizeReasoning(toolResponse.Reasoning);
        }

        private ToolCall? ParseAnthropicToolUseBlock(Dictionary<string, object> block)
        {
            if (!block.ContainsKey("name")) return null;

            ToolCall toolCall = new ToolCall();
            toolCall.Id = block.ContainsKey("id") ? block["id"]?.ToString() : null;
            toolCall.Name = block["name"]?.ToString() ?? string.Empty;
            toolCall.ArgumentsJson = block.ContainsKey("input") && block["input"] != null
                ? _Serializer.SerializeJson(block["input"], false)
                : "{}";
            return toolCall;
        }

        private string? ExtractTextFromResponse(string responseBody)
        {
            List<Dictionary<string, object>>? blocks = ExtractResponseBlocks(responseBody, warnOnMissing: true);
            if (blocks == null) return null;

            string combined = string.Empty;
            foreach (Dictionary<string, object> block in blocks)
            {
                string? blockType = block.ContainsKey("type") ? block["type"]?.ToString() : null;
                if (!string.Equals(blockType, TextBlockType, StringComparison.Ordinal)) continue;
                if (!block.ContainsKey("text")) continue;

                string? text = block["text"]?.ToString();
                if (!string.IsNullOrEmpty(text)) combined += text;
            }

            return string.IsNullOrWhiteSpace(combined) ? null : combined;
        }

        private string? ExtractReasoningFromResponse(string responseBody)
        {
            List<Dictionary<string, object>>? blocks = ExtractResponseBlocks(responseBody, warnOnMissing: false);
            if (blocks == null) return null;

            string combined = string.Empty;
            foreach (Dictionary<string, object> block in blocks)
            {
                string? blockType = block.ContainsKey("type") ? block["type"]?.ToString() : null;
                if (!string.Equals(blockType, ThinkingBlockType, StringComparison.Ordinal)) continue;
                if (!block.ContainsKey(ThinkingBlockType)) continue;

                string? thinking = block[ThinkingBlockType]?.ToString();
                if (!string.IsNullOrEmpty(thinking)) combined += thinking;
            }

            return NormalizeReasoning(combined);
        }

        private List<Dictionary<string, object>>? ExtractResponseBlocks(string responseBody, bool warnOnMissing)
        {
            Dictionary<string, object>? responseObj = _Serializer.DeserializeJson<Dictionary<string, object>>(responseBody);
            if (responseObj == null || !responseObj.ContainsKey("content"))
            {
                if (warnOnMissing) _Logging.Warn(_Header + "response missing 'content' field");
                return null;
            }

            return ParseContentBlocks(responseObj);
        }

        private List<Dictionary<string, object>>? ParseContentBlocks(Dictionary<string, object> responseObj)
        {
            if (!responseObj.ContainsKey("content") || responseObj["content"] == null) return null;

            string contentJson = _Serializer.SerializeJson(responseObj["content"], false);
            List<Dictionary<string, object>>? blocks = _Serializer.DeserializeJson<List<Dictionary<string, object>>>(contentJson);

            return blocks != null && blocks.Count > 0 ? blocks : null;
        }

        private ModelInformation ParseModelInformation(Dictionary<string, object> modelObj, string fallbackName)
        {
            ModelInformation info = new ModelInformation();
            info.Name = modelObj.ContainsKey("id") ? modelObj["id"]?.ToString() ?? fallbackName : fallbackName;
            info.DisplayName = modelObj.ContainsKey("display_name") ? modelObj["display_name"]?.ToString() : null;

            if (modelObj.ContainsKey("created_at"))
            {
                string? createdStr = modelObj["created_at"]?.ToString();
                if (!string.IsNullOrEmpty(createdStr)
                    && DateTime.TryParse(createdStr, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime created))
                {
                    info.CreatedUtc = created;
                }
            }

            if (modelObj.ContainsKey("type"))
            {
                info.Metadata["type"] = modelObj["type"]?.ToString();
            }

            return info;
        }

        private async IAsyncEnumerable<ChatStreamingChunk> ReadAnthropicChatChunks(
            HttpResponseMessage response,
            [EnumeratorCancellation] CancellationToken token)
        {
            using Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using StreamReader reader = new StreamReader(stream);

            int? promptTokens = null;

            string? line;
            while ((line = await reader.ReadLineAsync(token).ConfigureAwait(false)) != null)
            {
                token.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (!line.StartsWith("data: ")) continue;

                string data = line.Substring(6);

                Dictionary<string, object>? evt = _Serializer.DeserializeJson<Dictionary<string, object>>(data);
                if (evt == null) continue;

                string? eventType = evt.ContainsKey("type") ? evt["type"]?.ToString() : null;
                if (eventType == null) continue;

                if (string.Equals(eventType, MessageStartEvent, StringComparison.Ordinal))
                {
                    ChatStreamingChunk startChunk = new ChatStreamingChunk();
                    startChunk.CreatedUtc = DateTime.UtcNow;

                    Dictionary<string, object>? message = ParseNestedObject(evt, "message");
                    if (message != null)
                    {
                        startChunk.ResponseId = message.ContainsKey("id") ? message["id"]?.ToString() : null;
                        startChunk.Model = message.ContainsKey("model") ? message["model"]?.ToString() : null;

                        Dictionary<string, object>? usageObj = ParseNestedObject(message, "usage");
                        if (usageObj != null) promptTokens = TryGetInt(usageObj, "input_tokens");
                    }

                    yield return startChunk;
                    continue;
                }

                if (string.Equals(eventType, ContentBlockDeltaEvent, StringComparison.Ordinal))
                {
                    Dictionary<string, object>? delta = ParseNestedObject(evt, "delta");
                    if (delta == null) continue;

                    string? deltaType = delta.ContainsKey("type") ? delta["type"]?.ToString() : null;

                    ChatStreamingChunk deltaChunk = new ChatStreamingChunk();
                    deltaChunk.CreatedUtc = DateTime.UtcNow;

                    if (string.Equals(deltaType, TextDeltaType, StringComparison.Ordinal) && delta.ContainsKey("text"))
                    {
                        deltaChunk.Text = delta["text"]?.ToString();
                    }
                    else if (string.Equals(deltaType, ThinkingDeltaType, StringComparison.Ordinal) && delta.ContainsKey(ThinkingBlockType))
                    {
                        deltaChunk.ReasoningText = delta[ThinkingBlockType]?.ToString();
                    }

                    yield return deltaChunk;
                    continue;
                }

                if (string.Equals(eventType, MessageDeltaEvent, StringComparison.Ordinal))
                {
                    ChatStreamingChunk finalChunk = new ChatStreamingChunk();
                    finalChunk.CreatedUtc = DateTime.UtcNow;

                    Dictionary<string, object>? delta = ParseNestedObject(evt, "delta");
                    if (delta != null && delta.ContainsKey("stop_reason") && delta["stop_reason"] != null)
                    {
                        finalChunk.FinishReason = delta["stop_reason"]?.ToString();
                        finalChunk.Done = true;
                    }

                    finalChunk.Usage = ParseAnthropicStreamUsage(evt, promptTokens);

                    yield return finalChunk;
                    continue;
                }

                if (string.Equals(eventType, MessageStopEvent, StringComparison.Ordinal))
                {
                    ChatStreamingChunk doneChunk = new ChatStreamingChunk();
                    doneChunk.Done = true;
                    yield return doneChunk;
                    break;
                }
            }
        }

        private async IAsyncEnumerable<ToolChatStreamingChunk> ReadAnthropicToolChatChunks(
            HttpResponseMessage response,
            [EnumeratorCancellation] CancellationToken token)
        {
            using Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using StreamReader reader = new StreamReader(stream);

            int? promptTokens = null;

            string? line;
            while ((line = await reader.ReadLineAsync(token).ConfigureAwait(false)) != null)
            {
                token.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (!line.StartsWith("data: ")) continue;

                string data = line.Substring(6);

                Dictionary<string, object>? evt = _Serializer.DeserializeJson<Dictionary<string, object>>(data);
                if (evt == null) continue;

                string? eventType = evt.ContainsKey("type") ? evt["type"]?.ToString() : null;
                if (eventType == null) continue;

                if (string.Equals(eventType, MessageStartEvent, StringComparison.Ordinal))
                {
                    ToolChatStreamingChunk startChunk = new ToolChatStreamingChunk();
                    startChunk.CreatedUtc = DateTime.UtcNow;

                    Dictionary<string, object>? message = ParseNestedObject(evt, "message");
                    if (message != null)
                    {
                        startChunk.ResponseId = message.ContainsKey("id") ? message["id"]?.ToString() : null;
                        startChunk.Model = message.ContainsKey("model") ? message["model"]?.ToString() : null;

                        Dictionary<string, object>? usageObj = ParseNestedObject(message, "usage");
                        if (usageObj != null) promptTokens = TryGetInt(usageObj, "input_tokens");
                    }

                    yield return startChunk;
                    continue;
                }

                if (string.Equals(eventType, ContentBlockStartEvent, StringComparison.Ordinal))
                {
                    Dictionary<string, object>? contentBlock = ParseNestedObject(evt, "content_block");
                    if (contentBlock == null) continue;

                    string? blockType = contentBlock.ContainsKey("type") ? contentBlock["type"]?.ToString() : null;
                    if (!string.Equals(blockType, ToolUseBlockType, StringComparison.Ordinal)) continue;

                    int index = TryGetInt(evt, "index") ?? 0;

                    ToolCallDelta startDelta = new ToolCallDelta();
                    startDelta.Index = index;
                    startDelta.Id = contentBlock.ContainsKey("id") ? contentBlock["id"]?.ToString() : null;
                    startDelta.Type = "function";
                    startDelta.Name = contentBlock.ContainsKey("name") ? contentBlock["name"]?.ToString() : null;

                    ToolChatStreamingChunk toolStartChunk = new ToolChatStreamingChunk();
                    toolStartChunk.CreatedUtc = DateTime.UtcNow;
                    toolStartChunk.ToolCallDeltas.Add(startDelta);

                    yield return toolStartChunk;
                    continue;
                }

                if (string.Equals(eventType, ContentBlockDeltaEvent, StringComparison.Ordinal))
                {
                    Dictionary<string, object>? delta = ParseNestedObject(evt, "delta");
                    if (delta == null) continue;

                    string? deltaType = delta.ContainsKey("type") ? delta["type"]?.ToString() : null;

                    ToolChatStreamingChunk deltaChunk = new ToolChatStreamingChunk();
                    deltaChunk.CreatedUtc = DateTime.UtcNow;

                    if (string.Equals(deltaType, TextDeltaType, StringComparison.Ordinal) && delta.ContainsKey("text"))
                    {
                        deltaChunk.Text = delta["text"]?.ToString();
                    }
                    else if (string.Equals(deltaType, ThinkingDeltaType, StringComparison.Ordinal) && delta.ContainsKey(ThinkingBlockType))
                    {
                        deltaChunk.ReasoningText = delta[ThinkingBlockType]?.ToString();
                    }
                    else if (string.Equals(deltaType, InputJsonDeltaType, StringComparison.Ordinal) && delta.ContainsKey("partial_json"))
                    {
                        ToolCallDelta argsDelta = new ToolCallDelta();
                        argsDelta.Index = TryGetInt(evt, "index") ?? 0;
                        argsDelta.ArgumentsJsonDelta = delta["partial_json"]?.ToString();
                        deltaChunk.ToolCallDeltas.Add(argsDelta);
                    }

                    yield return deltaChunk;
                    continue;
                }

                if (string.Equals(eventType, MessageDeltaEvent, StringComparison.Ordinal))
                {
                    ToolChatStreamingChunk finalChunk = new ToolChatStreamingChunk();
                    finalChunk.CreatedUtc = DateTime.UtcNow;

                    Dictionary<string, object>? delta = ParseNestedObject(evt, "delta");
                    if (delta != null && delta.ContainsKey("stop_reason") && delta["stop_reason"] != null)
                    {
                        finalChunk.FinishReason = delta["stop_reason"]?.ToString();
                        finalChunk.Done = true;
                    }

                    finalChunk.Usage = ParseAnthropicStreamUsage(evt, promptTokens);

                    yield return finalChunk;
                    continue;
                }

                if (string.Equals(eventType, MessageStopEvent, StringComparison.Ordinal))
                {
                    ToolChatStreamingChunk doneChunk = new ToolChatStreamingChunk();
                    doneChunk.Done = true;
                    yield return doneChunk;
                    break;
                }
            }
        }

        private async IAsyncEnumerable<GenerationStreamingChunk> ReadAnthropicGenerateChunks(
            HttpResponseMessage response,
            [EnumeratorCancellation] CancellationToken token)
        {
            using Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using StreamReader reader = new StreamReader(stream);

            string? line;
            while ((line = await reader.ReadLineAsync(token).ConfigureAwait(false)) != null)
            {
                token.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (!line.StartsWith("data: ")) continue;

                string data = line.Substring(6);

                Dictionary<string, object>? evt = _Serializer.DeserializeJson<Dictionary<string, object>>(data);
                if (evt == null) continue;

                string? eventType = evt.ContainsKey("type") ? evt["type"]?.ToString() : null;
                if (eventType == null) continue;

                if (string.Equals(eventType, MessageStartEvent, StringComparison.Ordinal))
                {
                    GenerationStreamingChunk startChunk = new GenerationStreamingChunk();
                    Dictionary<string, object>? message = ParseNestedObject(evt, "message");
                    if (message != null)
                    {
                        startChunk.Model = message.ContainsKey("model") ? message["model"]?.ToString() : null;
                    }

                    yield return startChunk;
                    continue;
                }

                if (string.Equals(eventType, ContentBlockDeltaEvent, StringComparison.Ordinal))
                {
                    Dictionary<string, object>? delta = ParseNestedObject(evt, "delta");
                    if (delta == null) continue;

                    string? deltaType = delta.ContainsKey("type") ? delta["type"]?.ToString() : null;
                    if (!string.Equals(deltaType, TextDeltaType, StringComparison.Ordinal)) continue;
                    if (!delta.ContainsKey("text")) continue;

                    GenerationStreamingChunk deltaChunk = new GenerationStreamingChunk();
                    deltaChunk.Text = delta["text"]?.ToString();

                    yield return deltaChunk;
                    continue;
                }

                if (string.Equals(eventType, MessageStopEvent, StringComparison.Ordinal))
                {
                    GenerationStreamingChunk doneChunk = new GenerationStreamingChunk();
                    doneChunk.Done = true;
                    yield return doneChunk;
                    break;
                }
            }
        }

        private Dictionary<string, object>? ParseNestedObject(Dictionary<string, object> obj, string key)
        {
            if (!obj.ContainsKey(key) || obj[key] == null) return null;

            string nestedJson = _Serializer.SerializeJson(obj[key], false);
            return _Serializer.DeserializeJson<Dictionary<string, object>>(nestedJson);
        }

        private ChatStreamingUsage? ParseAnthropicStreamUsage(Dictionary<string, object> evt, int? promptTokens)
        {
            Dictionary<string, object>? usageObj = ParseNestedObject(evt, "usage");
            if (usageObj == null && promptTokens == null) return null;

            ChatStreamingUsage usage = new ChatStreamingUsage();
            usage.PromptTokens = promptTokens;

            if (usageObj != null)
            {
                int? inputTokens = TryGetInt(usageObj, "input_tokens");
                if (inputTokens.HasValue) usage.PromptTokens = inputTokens;

                usage.CompletionTokens = TryGetInt(usageObj, "output_tokens");
            }

            if (usage.PromptTokens.HasValue || usage.CompletionTokens.HasValue)
            {
                usage.TotalTokens = (usage.PromptTokens ?? 0) + (usage.CompletionTokens ?? 0);
            }

            return usage;
        }

        #endregion
    }
}
