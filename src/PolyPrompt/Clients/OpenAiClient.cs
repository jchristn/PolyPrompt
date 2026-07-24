namespace PolyPrompt.Clients
{
    using System.Diagnostics;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Text;
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using SyslogLogging;

    /// <summary>
    /// Client for the OpenAI-compatible API supporting chat completions, embeddings, and text generation.
    /// </summary>
    public class OpenAiClient : CompletionClientBase
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new OpenAiClient.
        /// </summary>
        /// <param name="endpoint">OpenAI API endpoint URL. Default: https://api.openai.com.</param>
        /// <param name="apiKey">API key (required for OpenAI). Default: null.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        public OpenAiClient(
            string endpoint = "https://api.openai.com",
            string? apiKey = null,
            LoggingModule? logging = null)
            : base(endpoint, apiKey, logging ?? new LoggingModule())
        {
            _Header = "[OpenAI] ";
            Model = "gpt-4o-mini";

            if (!string.IsNullOrEmpty(apiKey))
            {
                _HttpClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + apiKey);
            }
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

            string url = BuildApiUrl("chat/completions");

            Dictionary<string, object> requestBody = BuildChatRequestBody(prompt, maxTokens, systemPrompt, temperature, topP, options as OpenAiChatCompletionOptions, false);

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

                Dictionary<string, object>? responseObj = _Serializer.DeserializeJson<Dictionary<string, object>>(responseBody);
                if (responseObj == null || !responseObj.ContainsKey("choices"))
                {
                    _Logging.Warn(_Header + "chat response missing 'choices' field");
                    chatResponse.Success = false;
                    chatResponse.Error = "Response missing 'choices' field";
                    return chatResponse;
                }

                string choicesJson = _Serializer.SerializeJson(responseObj["choices"], false);
                List<Dictionary<string, object>>? choices = _Serializer.DeserializeJson<List<Dictionary<string, object>>>(choicesJson);

                if (choices == null || choices.Count == 0)
                {
                    _Logging.Warn(_Header + "chat response has empty choices array");
                    chatResponse.Success = false;
                    chatResponse.Error = "Response has empty choices array";
                    return chatResponse;
                }

                if (!choices[0].ContainsKey("message"))
                {
                    _Logging.Warn(_Header + "chat response choice missing 'message' field");
                    chatResponse.Success = false;
                    chatResponse.Error = "Response choice missing 'message' field";
                    return chatResponse;
                }

                string messageJson = _Serializer.SerializeJson(choices[0]["message"], false);
                Dictionary<string, object>? message = _Serializer.DeserializeJson<Dictionary<string, object>>(messageJson);

                if (message == null || !message.ContainsKey("content"))
                {
                    _Logging.Warn(_Header + "chat response message missing 'content' field");
                    chatResponse.Success = false;
                    chatResponse.Error = "Response message missing 'content' field";
                    return chatResponse;
                }

                string? completionText = message["content"]?.ToString();
                chatResponse.Text = string.IsNullOrWhiteSpace(completionText) ? null : completionText.Trim();
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

            string url = BuildApiUrl("chat/completions");

            Dictionary<string, object> requestBody = BuildChatRequestBody(prompt, maxTokens, systemPrompt, temperature, topP, options as OpenAiChatCompletionOptions, true);

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
                streamingResponse.Chunks = WrapChunksWithTiming(streamingResponse, ReadOpenAiChatChunks(response, streamingResult.Token), sw, streamingResult.Token, streamingResult);
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
            ResolveToolChatRequest(request, out string model, out int maxTokens, out double? temperature, out double? topP);

            ToolChatResponse toolResponse = new ToolChatResponse();
            toolResponse.Model = model;

            Stopwatch sw = Stopwatch.StartNew();

            string url = BuildApiUrl("chat/completions");
            Dictionary<string, object> requestBody = BuildToolChatRequestBody(request, model, maxTokens, temperature, topP, false);

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

                PopulateOpenAiToolChatResponse(responseBody, toolResponse);
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
            ResolveToolChatRequest(request, out string model, out int maxTokens, out double? temperature, out double? topP);

            string url = BuildApiUrl("chat/completions");
            Dictionary<string, object> requestBody = BuildToolChatRequestBody(request, model, maxTokens, temperature, topP, true);

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
                streamingResponse.Chunks = WrapToolChatChunksWithTiming(streamingResponse, ReadOpenAiToolChatChunks(response, streamingResult.Token), sw, streamingResult.Token, streamingResult);
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
        public override async Task<EmbeddingResponse> EmbedAsync(
            string input,
            EmbeddingOptions? options = null,
            CancellationToken token = default)
        {
            return await EmbedAsync(new List<string> { input }, options, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override async Task<EmbeddingResponse> EmbedAsync(
            List<string> inputs,
            EmbeddingOptions? options = null,
            CancellationToken token = default)
        {
            EmbeddingResponse embedResponse = new EmbeddingResponse();
            string model = options?.Model ?? Model;
            embedResponse.Model = model;

            Stopwatch sw = Stopwatch.StartNew();

            string url = BuildApiUrl("embeddings");

            Dictionary<string, object> requestBody = new Dictionary<string, object>
            {
                { "model", model },
                { "input", inputs }
            };

            OpenAiEmbeddingOptions? openAiOptions = options as OpenAiEmbeddingOptions;
            if (openAiOptions != null)
            {
                if (!string.IsNullOrEmpty(openAiOptions.EncodingFormat)) requestBody["encoding_format"] = openAiOptions.EncodingFormat;
                if (openAiOptions.Dimensions.HasValue) requestBody["dimensions"] = openAiOptions.Dimensions.Value;
            }

            string json = _Serializer.SerializeJson(requestBody, false);
            StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

            _Logging.Debug(_Header + "POST " + url);

            try
            {
                CompletionHttpResult result = await PostAndRecordAsync(url, content, json, token).ConfigureAwait(false);
                string responseBody = result.ResponseBody;

                embedResponse.StatusCode = result.StatusCode;

                if (!result.IsSuccessStatusCode)
                {
                    _Logging.Warn(_Header + "embed request failed with status " + result.StatusCode + ": " + responseBody);
                    embedResponse.Success = false;
                    embedResponse.Error = "HTTP " + result.StatusCode + ": " + responseBody;
                    return embedResponse;
                }

                Dictionary<string, object>? responseObj = _Serializer.DeserializeJson<Dictionary<string, object>>(responseBody);
                if (responseObj == null || !responseObj.ContainsKey("data"))
                {
                    _Logging.Warn(_Header + "embed response missing 'data' field");
                    embedResponse.Success = false;
                    embedResponse.Error = "Response missing 'data' field";
                    return embedResponse;
                }

                string dataJson = _Serializer.SerializeJson(responseObj["data"], false);
                List<Dictionary<string, object>>? dataList = _Serializer.DeserializeJson<List<Dictionary<string, object>>>(dataJson);

                if (dataList != null)
                {
                    foreach (Dictionary<string, object> item in dataList)
                    {
                        EmbeddingResult embResult = new EmbeddingResult();
                        if (item.ContainsKey("index") && int.TryParse(item["index"]?.ToString(), out int idx))
                        {
                            embResult.Index = idx;
                        }
                        if (item.ContainsKey("embedding"))
                        {
                            string vectorJson = _Serializer.SerializeJson(item["embedding"], false);
                            embResult.Embedding = ParseFloatArray(vectorJson);
                        }
                        embedResponse.Embeddings.Add(embResult);
                    }
                }

                embedResponse.Success = true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                embedResponse.Success = false;
                embedResponse.Error = ex.Message;
            }
            finally
            {
                sw.Stop();
                embedResponse.OverallRuntimeMs = sw.ElapsedMilliseconds;
            }

            return embedResponse;
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

            string url = BuildApiUrl("completions");

            Dictionary<string, object> requestBody = BuildGenerateRequestBody(prompt, model, maxTokens, temperature, topP, options as OpenAiGenerationOptions, false);

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

                Dictionary<string, object>? responseObj = _Serializer.DeserializeJson<Dictionary<string, object>>(responseBody);
                if (responseObj != null && responseObj.ContainsKey("choices"))
                {
                    string choicesJson = _Serializer.SerializeJson(responseObj["choices"], false);
                    List<Dictionary<string, object>>? choices = _Serializer.DeserializeJson<List<Dictionary<string, object>>>(choicesJson);

                    if (choices != null && choices.Count > 0 && choices[0].ContainsKey("text"))
                    {
                        string? text = choices[0]["text"]?.ToString();
                        genResponse.Text = string.IsNullOrWhiteSpace(text) ? null : text;
                    }
                }

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

            string url = BuildApiUrl("completions");

            Dictionary<string, object> requestBody = BuildGenerateRequestBody(prompt, model, maxTokens, temperature, topP, options as OpenAiGenerationOptions, true);

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
                streamingResponse.Chunks = WrapGenerationChunksWithTiming(streamingResponse, ReadOpenAiGenerateChunks(response, streamingResult.Token), sw, streamingResult.Token, streamingResult);
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
            string url = BuildApiUrl("models");

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
            if (dataList == null)
                yield break;

            foreach (Dictionary<string, object> modelObj in dataList)
            {
                token.ThrowIfCancellationRequested();

                ModelInformation info = new ModelInformation();
                info.Name = modelObj.ContainsKey("id") ? modelObj["id"]?.ToString() ?? "" : "";
                info.OwnedBy = modelObj.ContainsKey("owned_by") ? modelObj["owned_by"]?.ToString() : null;

                if (modelObj.ContainsKey("created"))
                {
                    string? createdStr = modelObj["created"]?.ToString();
                    if (long.TryParse(createdStr, out long unixSeconds))
                    {
                        info.CreatedUtc = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;
                    }
                }

                if (modelObj.ContainsKey("object"))
                {
                    info.Metadata["object"] = modelObj["object"]?.ToString();
                }

                yield return info;
            }
        }

        /// <inheritdoc />
        public override async Task<ModelInformation?> GetModelInformationAsync(string model, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(model))
                throw new ArgumentNullException(nameof(model));

            string url = BuildApiUrl("models/" + Uri.EscapeDataString(model));
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

                ModelInformation info = new ModelInformation();
                info.Name = responseObj.ContainsKey("id") ? responseObj["id"]?.ToString() ?? model : model;
                info.OwnedBy = responseObj.ContainsKey("owned_by") ? responseObj["owned_by"]?.ToString() : null;

                if (responseObj.ContainsKey("created"))
                {
                    long? created = TryGetLong(responseObj, "created");
                    if (created.HasValue)
                    {
                        info.CreatedUtc = DateTimeOffset.FromUnixTimeSeconds(created.Value).UtcDateTime;
                    }
                }

                if (responseObj.ContainsKey("object"))
                    info.Metadata["object"] = responseObj["object"]?.ToString();

                return info;
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

        private string BuildApiUrl(string path)
        {
            string endpoint = _Endpoint.TrimEnd('/');
            string normalizedPath = path.TrimStart('/');

            if (endpoint.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                return endpoint + "/" + normalizedPath;

            return endpoint + "/v1/" + normalizedPath;
        }

        private Dictionary<string, object> BuildChatRequestBody(
            string prompt, int maxTokens, string? systemPrompt,
            double? temperature, double? topP,
            OpenAiChatCompletionOptions? openAiOptions, bool stream)
        {
            List<Dictionary<string, string>> messages = new List<Dictionary<string, string>>();
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                messages.Add(new Dictionary<string, string> { { "role", "system" }, { "content", systemPrompt } });
            }
            messages.Add(new Dictionary<string, string> { { "role", "user" }, { "content", prompt } });

            Dictionary<string, object> requestBody = new Dictionary<string, object>
            {
                { "model", Model },
                { "messages", messages },
                { "max_tokens", maxTokens }
            };

            if (temperature.HasValue) requestBody["temperature"] = temperature.Value;
            if (topP.HasValue) requestBody["top_p"] = topP.Value;

            if (openAiOptions != null)
            {
                if (openAiOptions.FrequencyPenalty.HasValue) requestBody["frequency_penalty"] = openAiOptions.FrequencyPenalty.Value;
                if (openAiOptions.PresencePenalty.HasValue) requestBody["presence_penalty"] = openAiOptions.PresencePenalty.Value;
                if (openAiOptions.Seed.HasValue) requestBody["seed"] = openAiOptions.Seed.Value;
            }

            if (stream)
            {
                requestBody["stream"] = true;
                requestBody["stream_options"] = new Dictionary<string, object> { { "include_usage", true } };
            }

            return requestBody;
        }

        private Dictionary<string, object> BuildToolChatRequestBody(
            ToolChatRequest request,
            string model,
            int maxTokens,
            double? temperature,
            double? topP,
            bool stream)
        {
            Dictionary<string, object> requestBody = new Dictionary<string, object>
            {
                { "model", model },
                { "messages", BuildOpenAiMessages(request.Messages) },
                { "max_tokens", maxTokens }
            };

            if (temperature.HasValue) requestBody["temperature"] = temperature.Value;
            if (topP.HasValue) requestBody["top_p"] = topP.Value;

            if (request.Tools != null && request.Tools.Count > 0 && !IsToolChoiceNone(request.ToolChoice))
            {
                requestBody["tools"] = BuildOpenAiTools(request.Tools);
            }

            if (!string.IsNullOrWhiteSpace(request.ToolChoice))
            {
                requestBody["tool_choice"] = request.ToolChoice;
            }

            if (stream)
            {
                requestBody["stream"] = true;
                requestBody["stream_options"] = new Dictionary<string, object> { { "include_usage", true } };
            }

            return requestBody;
        }

        private List<Dictionary<string, object>> BuildOpenAiMessages(List<ChatMessage> messages)
        {
            List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();

            foreach (ChatMessage message in messages)
            {
                Dictionary<string, object> item = new Dictionary<string, object>
                {
                    { "role", NormalizeOpenAiRole(message.Role) }
                };

                if (!string.IsNullOrEmpty(message.ToolCallId))
                {
                    item["tool_call_id"] = message.ToolCallId;
                }

                if (message.ToolCalls != null && message.ToolCalls.Count > 0)
                {
                    item["content"] = message.Content ?? string.Empty;
                    item["tool_calls"] = BuildOpenAiToolCalls(message.ToolCalls);
                }
                else
                {
                    item["content"] = message.Content ?? string.Empty;
                }

                result.Add(item);
            }

            return result;
        }

        private List<Dictionary<string, object>> BuildOpenAiTools(List<ToolDefinition> tools)
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

        private List<Dictionary<string, object>> BuildOpenAiToolCalls(List<ToolCall> toolCalls)
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

                result.Add(item);
            }

            return result;
        }

        private void PopulateOpenAiToolChatResponse(string responseBody, ToolChatResponse toolResponse)
        {
            Dictionary<string, object>? responseObj = _Serializer.DeserializeJson<Dictionary<string, object>>(responseBody);
            if (responseObj == null || !responseObj.ContainsKey("choices"))
            {
                toolResponse.Success = false;
                toolResponse.Error = "Response missing 'choices' field";
                return;
            }

            toolResponse.ResponseId = responseObj.ContainsKey("id") ? responseObj["id"]?.ToString() : null;

            string choicesJson = _Serializer.SerializeJson(responseObj["choices"], false);
            List<Dictionary<string, object>>? choices = _Serializer.DeserializeJson<List<Dictionary<string, object>>>(choicesJson);

            if (choices == null || choices.Count == 0)
            {
                toolResponse.Success = false;
                toolResponse.Error = "Response has empty choices array";
                return;
            }

            Dictionary<string, object> choice = choices[0];
            toolResponse.FinishReason = choice.ContainsKey("finish_reason") ? choice["finish_reason"]?.ToString() : null;

            if (!choice.ContainsKey("message"))
            {
                toolResponse.Success = false;
                toolResponse.Error = "Response choice missing 'message' field";
                return;
            }

            string messageJson = _Serializer.SerializeJson(choice["message"], false);
            Dictionary<string, object>? message = _Serializer.DeserializeJson<Dictionary<string, object>>(messageJson);
            if (message == null)
            {
                toolResponse.Success = false;
                toolResponse.Error = "Response message could not be parsed";
                return;
            }

            if (message.ContainsKey("content"))
            {
                string? text = message["content"]?.ToString();
                toolResponse.Text = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            }

            if (message.ContainsKey("tool_calls"))
            {
                string toolCallsJson = _Serializer.SerializeJson(message["tool_calls"], false);
                List<Dictionary<string, object>>? toolCalls = _Serializer.DeserializeJson<List<Dictionary<string, object>>>(toolCallsJson);

                if (toolCalls != null)
                {
                    foreach (Dictionary<string, object> toolCallObj in toolCalls)
                    {
                        ToolCall? toolCall = ParseOpenAiToolCall(toolCallObj);
                        if (toolCall != null) toolResponse.ToolCalls.Add(toolCall);
                    }
                }
            }
        }

        private ToolCall? ParseOpenAiToolCall(Dictionary<string, object> toolCallObj)
        {
            if (!toolCallObj.ContainsKey("function")) return null;

            string functionJson = _Serializer.SerializeJson(toolCallObj["function"], false);
            Dictionary<string, object>? function = _Serializer.DeserializeJson<Dictionary<string, object>>(functionJson);
            if (function == null || !function.ContainsKey("name")) return null;

            ToolCall toolCall = new ToolCall();
            toolCall.Id = toolCallObj.ContainsKey("id") ? toolCallObj["id"]?.ToString() : null;
            toolCall.Name = function["name"]?.ToString() ?? string.Empty;
            toolCall.ArgumentsJson = function.ContainsKey("arguments") && function["arguments"] != null
                ? function["arguments"]?.ToString() ?? "{}"
                : "{}";
            return toolCall;
        }

        private static string NormalizeOpenAiRole(string? role)
        {
            if (string.Equals(role, "model", StringComparison.OrdinalIgnoreCase)) return "assistant";
            if (string.Equals(role, "function", StringComparison.OrdinalIgnoreCase)) return "tool";
            return string.IsNullOrWhiteSpace(role) ? "user" : role.ToLowerInvariant();
        }

        private static bool IsToolChoiceNone(string? toolChoice)
        {
            return string.Equals(toolChoice, "none", StringComparison.OrdinalIgnoreCase);
        }

        private Dictionary<string, object> BuildGenerateRequestBody(
            string prompt, string model, int maxTokens,
            double? temperature, double? topP,
            OpenAiGenerationOptions? openAiOptions, bool stream)
        {
            Dictionary<string, object> requestBody = new Dictionary<string, object>
            {
                { "model", model },
                { "prompt", prompt },
                { "max_tokens", maxTokens }
            };

            if (temperature.HasValue) requestBody["temperature"] = temperature.Value;
            if (topP.HasValue) requestBody["top_p"] = topP.Value;

            if (openAiOptions != null)
            {
                if (openAiOptions.FrequencyPenalty.HasValue) requestBody["frequency_penalty"] = openAiOptions.FrequencyPenalty.Value;
                if (openAiOptions.PresencePenalty.HasValue) requestBody["presence_penalty"] = openAiOptions.PresencePenalty.Value;
                if (openAiOptions.Seed.HasValue) requestBody["seed"] = openAiOptions.Seed.Value;
                if (openAiOptions.Echo.HasValue) requestBody["echo"] = openAiOptions.Echo.Value;
                if (!string.IsNullOrEmpty(openAiOptions.Suffix)) requestBody["suffix"] = openAiOptions.Suffix;
                if (openAiOptions.Logprobs.HasValue) requestBody["logprobs"] = openAiOptions.Logprobs.Value;
            }

            if (stream)
            {
                requestBody["stream"] = true;
            }

            return requestBody;
        }

        private async IAsyncEnumerable<ChatStreamingChunk> ReadOpenAiChatChunks(
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
                if (data == "[DONE]")
                {
                    ChatStreamingChunk doneChunk = new ChatStreamingChunk();
                    doneChunk.Done = true;
                    yield return doneChunk;
                    break;
                }

                Dictionary<string, object>? chunk = _Serializer.DeserializeJson<Dictionary<string, object>>(data);
                if (chunk == null) continue;

                ChatStreamingChunk streamChunk = new ChatStreamingChunk();
                streamChunk.ResponseId = chunk.ContainsKey("id") ? chunk["id"]?.ToString() : null;
                streamChunk.Model = chunk.ContainsKey("model") ? chunk["model"]?.ToString() : null;

                if (chunk.ContainsKey("created"))
                {
                    string? createdStr = chunk["created"]?.ToString();
                    if (long.TryParse(createdStr, out long unixSeconds))
                    {
                        streamChunk.CreatedUtc = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;
                    }
                }

                if (chunk.ContainsKey("choices"))
                {
                    string choicesJson = _Serializer.SerializeJson(chunk["choices"], false);
                    List<Dictionary<string, object>>? choices = _Serializer.DeserializeJson<List<Dictionary<string, object>>>(choicesJson);

                    if (choices != null && choices.Count > 0)
                    {
                        Dictionary<string, object> choice = choices[0];

                        if (choice.ContainsKey("finish_reason") && choice["finish_reason"] != null)
                        {
                            streamChunk.FinishReason = choice["finish_reason"].ToString();
                            streamChunk.Done = true;
                        }

                        if (choice.ContainsKey("delta"))
                        {
                            string deltaJson = _Serializer.SerializeJson(choice["delta"], false);
                            Dictionary<string, object>? delta = _Serializer.DeserializeJson<Dictionary<string, object>>(deltaJson);
                            if (delta != null && delta.ContainsKey("content"))
                            {
                                streamChunk.Text = delta["content"]?.ToString();
                            }
                        }
                    }
                }

                if (chunk.ContainsKey("usage") && chunk["usage"] != null)
                {
                    string usageJson = _Serializer.SerializeJson(chunk["usage"], false);
                    Dictionary<string, object>? usageObj = _Serializer.DeserializeJson<Dictionary<string, object>>(usageJson);

                    if (usageObj != null)
                    {
                        ChatStreamingUsage usage = new ChatStreamingUsage();
                        if (usageObj.ContainsKey("prompt_tokens") && int.TryParse(usageObj["prompt_tokens"]?.ToString(), out int pt))
                            usage.PromptTokens = pt;
                        if (usageObj.ContainsKey("completion_tokens") && int.TryParse(usageObj["completion_tokens"]?.ToString(), out int ct))
                            usage.CompletionTokens = ct;
                        if (usageObj.ContainsKey("total_tokens") && int.TryParse(usageObj["total_tokens"]?.ToString(), out int tt))
                            usage.TotalTokens = tt;
                        streamChunk.Usage = usage;
                    }
                }

                yield return streamChunk;
            }
        }

        private async IAsyncEnumerable<ToolChatStreamingChunk> ReadOpenAiToolChatChunks(
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
                if (data == "[DONE]")
                {
                    ToolChatStreamingChunk doneChunk = new ToolChatStreamingChunk();
                    doneChunk.Done = true;
                    yield return doneChunk;
                    break;
                }

                Dictionary<string, object>? chunk = _Serializer.DeserializeJson<Dictionary<string, object>>(data);
                if (chunk == null) continue;

                ToolChatStreamingChunk streamChunk = new ToolChatStreamingChunk();
                streamChunk.ResponseId = chunk.ContainsKey("id") ? chunk["id"]?.ToString() : null;
                streamChunk.Model = chunk.ContainsKey("model") ? chunk["model"]?.ToString() : null;

                if (chunk.ContainsKey("created"))
                {
                    long? created = TryGetLong(chunk, "created");
                    if (created.HasValue)
                    {
                        streamChunk.CreatedUtc = DateTimeOffset.FromUnixTimeSeconds(created.Value).UtcDateTime;
                    }
                }

                if (chunk.ContainsKey("choices"))
                {
                    string choicesJson = _Serializer.SerializeJson(chunk["choices"], false);
                    List<Dictionary<string, object>>? choices = _Serializer.DeserializeJson<List<Dictionary<string, object>>>(choicesJson);

                    if (choices != null && choices.Count > 0)
                    {
                        Dictionary<string, object> choice = choices[0];

                        if (choice.ContainsKey("finish_reason") && choice["finish_reason"] != null)
                        {
                            streamChunk.FinishReason = choice["finish_reason"]?.ToString();
                            streamChunk.Done = true;
                        }

                        if (choice.ContainsKey("delta"))
                        {
                            string deltaJson = _Serializer.SerializeJson(choice["delta"], false);
                            Dictionary<string, object>? delta = _Serializer.DeserializeJson<Dictionary<string, object>>(deltaJson);
                            if (delta != null)
                            {
                                if (delta.ContainsKey("content"))
                                {
                                    streamChunk.Text = delta["content"]?.ToString();
                                }

                                if (delta.ContainsKey("tool_calls"))
                                {
                                    foreach (ToolCallDelta toolDelta in ParseOpenAiToolCallDeltas(delta["tool_calls"]))
                                    {
                                        streamChunk.ToolCallDeltas.Add(toolDelta);
                                    }
                                }
                            }
                        }
                    }
                }

                if (chunk.ContainsKey("usage") && chunk["usage"] != null)
                {
                    string usageJson = _Serializer.SerializeJson(chunk["usage"], false);
                    Dictionary<string, object>? usageObj = _Serializer.DeserializeJson<Dictionary<string, object>>(usageJson);

                    if (usageObj != null)
                    {
                        ChatStreamingUsage usage = new ChatStreamingUsage();
                        if (usageObj.ContainsKey("prompt_tokens") && int.TryParse(usageObj["prompt_tokens"]?.ToString(), out int pt))
                            usage.PromptTokens = pt;
                        if (usageObj.ContainsKey("completion_tokens") && int.TryParse(usageObj["completion_tokens"]?.ToString(), out int ct))
                            usage.CompletionTokens = ct;
                        if (usageObj.ContainsKey("total_tokens") && int.TryParse(usageObj["total_tokens"]?.ToString(), out int tt))
                            usage.TotalTokens = tt;
                        streamChunk.Usage = usage;
                    }
                }

                yield return streamChunk;
            }
        }

        private List<ToolCallDelta> ParseOpenAiToolCallDeltas(object toolCallsObj)
        {
            List<ToolCallDelta> result = new List<ToolCallDelta>();
            string toolCallsJson = _Serializer.SerializeJson(toolCallsObj, false);
            List<Dictionary<string, object>>? toolCalls = _Serializer.DeserializeJson<List<Dictionary<string, object>>>(toolCallsJson);
            if (toolCalls == null) return result;

            int fallbackIndex = 0;
            foreach (Dictionary<string, object> toolCallObj in toolCalls)
            {
                int index = TryGetInt(toolCallObj, "index") ?? fallbackIndex;
                ToolCallDelta delta = new ToolCallDelta();
                delta.Index = index;
                delta.Id = toolCallObj.ContainsKey("id") ? toolCallObj["id"]?.ToString() : null;
                delta.Type = toolCallObj.ContainsKey("type") ? toolCallObj["type"]?.ToString() : null;

                if (toolCallObj.ContainsKey("function"))
                {
                    string functionJson = _Serializer.SerializeJson(toolCallObj["function"], false);
                    Dictionary<string, object>? function = _Serializer.DeserializeJson<Dictionary<string, object>>(functionJson);
                    if (function != null)
                    {
                        delta.Name = function.ContainsKey("name") ? function["name"]?.ToString() : null;
                        if (function.ContainsKey("arguments"))
                        {
                            delta.ArgumentsJsonDelta = function["arguments"]?.ToString();
                        }
                    }
                }

                result.Add(delta);
                fallbackIndex++;
            }

            return result;
        }

        private async IAsyncEnumerable<GenerationStreamingChunk> ReadOpenAiGenerateChunks(
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
                if (data == "[DONE]")
                {
                    GenerationStreamingChunk doneChunk = new GenerationStreamingChunk();
                    doneChunk.Done = true;
                    yield return doneChunk;
                    break;
                }

                Dictionary<string, object>? chunk = _Serializer.DeserializeJson<Dictionary<string, object>>(data);
                if (chunk == null) continue;

                GenerationStreamingChunk streamChunk = new GenerationStreamingChunk();
                streamChunk.Model = chunk.ContainsKey("model") ? chunk["model"]?.ToString() : null;

                if (chunk.ContainsKey("choices"))
                {
                    string choicesJson = _Serializer.SerializeJson(chunk["choices"], false);
                    List<Dictionary<string, object>>? choices = _Serializer.DeserializeJson<List<Dictionary<string, object>>>(choicesJson);

                    if (choices != null && choices.Count > 0)
                    {
                        Dictionary<string, object> choice = choices[0];
                        if (choice.ContainsKey("text"))
                        {
                            streamChunk.Text = choice["text"]?.ToString();
                        }
                        if (choice.ContainsKey("finish_reason") && choice["finish_reason"] != null)
                        {
                            streamChunk.Done = true;
                        }
                    }
                }

                yield return streamChunk;
            }
        }

        #endregion
    }
}
