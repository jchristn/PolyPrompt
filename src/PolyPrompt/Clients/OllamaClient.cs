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
    /// Client for the Ollama API supporting chat completions, embeddings, and text generation.
    /// </summary>
    public class OllamaClient : CompletionClientBase
    {
        #region Private-Members

        private int? _ContextLength = null;

        #endregion

        #region Public-Members

        /// <summary>
        /// Context window size (num_ctx). Clamped to 1..2,097,152. Null uses model default.
        /// </summary>
        public int? ContextLength
        {
            get { return _ContextLength; }
            set
            {
                if (value.HasValue)
                    _ContextLength = Math.Clamp(value.Value, 1, 2_097_152);
                else
                    _ContextLength = null;
            }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new OllamaClient.
        /// </summary>
        /// <param name="endpoint">Ollama server endpoint URL. Default: http://localhost:11434.</param>
        /// <param name="apiKey">API key (nullable, typically not needed for Ollama). Default: null.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        public OllamaClient(
            string endpoint = "http://localhost:11434",
            string? apiKey = null,
            LoggingModule? logging = null)
            : base(endpoint, apiKey, logging ?? new LoggingModule())
        {
            _Header = "[Ollama] ";
            Model = "gemma3:4b";

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

            string url = _Endpoint.TrimEnd('/') + "/api/chat";

            Dictionary<string, object> requestBody = BuildChatRequestBody(prompt, maxTokens, systemPrompt, temperature, topP, options as OllamaChatCompletionOptions, false);

            string json = _Serializer.SerializeJson(requestBody, false);
            StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

            _Logging.Debug(_Header + "POST " + url);

            try
            {
                CompletionHttpResult result = await PostAndRecordAsync(url, content, json, token).ConfigureAwait(false);
                HttpResponseMessage response = result.Response;
                string responseBody = result.ResponseBody;

                chatResponse.StatusCode = (int)response.StatusCode;

                if (!response.IsSuccessStatusCode)
                {
                    _Logging.Warn(_Header + "chat request failed with status " + (int)response.StatusCode + ": " + responseBody);
                    chatResponse.Success = false;
                    chatResponse.Error = "HTTP " + (int)response.StatusCode + ": " + responseBody;
                    return chatResponse;
                }

                Dictionary<string, object>? responseObj = _Serializer.DeserializeJson<Dictionary<string, object>>(responseBody);
                if (responseObj == null || !responseObj.ContainsKey("message"))
                {
                    _Logging.Warn(_Header + "chat response missing 'message' field");
                    chatResponse.Success = false;
                    chatResponse.Error = "Response missing 'message' field";
                    return chatResponse;
                }

                string messageJson = _Serializer.SerializeJson(responseObj["message"], false);
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

            string url = _Endpoint.TrimEnd('/') + "/api/chat";

            Dictionary<string, object> requestBody = BuildChatRequestBody(prompt, maxTokens, systemPrompt, temperature, topP, options as OllamaChatCompletionOptions, true);

            string json = _Serializer.SerializeJson(requestBody, false);
            StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

            _Logging.Debug(_Header + "POST (streaming) " + url);

            Stopwatch sw = Stopwatch.StartNew();

            ChatStreamingResponse streamingResponse = new ChatStreamingResponse();
            streamingResponse.Model = Model;

            try
            {
                HttpResponseMessage response = await PostStreamingAsync(url, content, token).ConfigureAwait(false);
                streamingResponse.StatusCode = (int)response.StatusCode;

                if (!response.IsSuccessStatusCode)
                {
                    string errorBody = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                    _Logging.Warn(_Header + "streaming chat request failed with status " + (int)response.StatusCode + ": " + errorBody);
                    streamingResponse.Success = false;
                    streamingResponse.Error = "HTTP " + (int)response.StatusCode + ": " + errorBody;
                    return streamingResponse;
                }

                streamingResponse.Success = true;
                streamingResponse.Chunks = WrapChunksWithTiming(streamingResponse, ReadOllamaChatChunks(response, token), sw, token);
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

            string url = _Endpoint.TrimEnd('/') + "/api/embed";

            Dictionary<string, object> requestBody = new Dictionary<string, object>
            {
                { "model", model },
                { "input", inputs }
            };

            OllamaEmbeddingOptions? ollamaOptions = options as OllamaEmbeddingOptions;
            if (ollamaOptions != null)
            {
                if (ollamaOptions.Truncate.HasValue) requestBody["truncate"] = ollamaOptions.Truncate.Value;
                Dictionary<string, object> modelOptions = new Dictionary<string, object>();
                if (ollamaOptions.ContextLength.HasValue) modelOptions["num_ctx"] = ollamaOptions.ContextLength.Value;
                if (modelOptions.Count > 0) requestBody["options"] = modelOptions;
            }

            string json = _Serializer.SerializeJson(requestBody, false);
            StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

            _Logging.Debug(_Header + "POST " + url);

            try
            {
                CompletionHttpResult result = await PostAndRecordAsync(url, content, json, token).ConfigureAwait(false);
                HttpResponseMessage response = result.Response;
                string responseBody = result.ResponseBody;

                embedResponse.StatusCode = (int)response.StatusCode;

                if (!response.IsSuccessStatusCode)
                {
                    _Logging.Warn(_Header + "embed request failed with status " + (int)response.StatusCode + ": " + responseBody);
                    embedResponse.Success = false;
                    embedResponse.Error = "HTTP " + (int)response.StatusCode + ": " + responseBody;
                    return embedResponse;
                }

                Dictionary<string, object>? responseObj = _Serializer.DeserializeJson<Dictionary<string, object>>(responseBody);
                if (responseObj == null || !responseObj.ContainsKey("embeddings"))
                {
                    _Logging.Warn(_Header + "embed response missing 'embeddings' field");
                    embedResponse.Success = false;
                    embedResponse.Error = "Response missing 'embeddings' field";
                    return embedResponse;
                }

                string embeddingsJson = _Serializer.SerializeJson(responseObj["embeddings"], false);
                List<object>? embeddingsList = _Serializer.DeserializeJson<List<object>>(embeddingsJson);

                if (embeddingsList != null)
                {
                    for (int i = 0; i < embeddingsList.Count; i++)
                    {
                        string vectorJson = _Serializer.SerializeJson(embeddingsList[i], false);
                        float[] vector = ParseFloatArray(vectorJson);

                        EmbeddingResult embResult = new EmbeddingResult();
                        embResult.Index = i;
                        embResult.Embedding = vector;
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

            string url = _Endpoint.TrimEnd('/') + "/api/generate";

            Dictionary<string, object> requestBody = BuildGenerateRequestBody(prompt, model, maxTokens, temperature, topP, options as OllamaGenerationOptions, false);

            string json = _Serializer.SerializeJson(requestBody, false);
            StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

            _Logging.Debug(_Header + "POST " + url);

            try
            {
                CompletionHttpResult result = await PostAndRecordAsync(url, content, json, token).ConfigureAwait(false);
                HttpResponseMessage response = result.Response;
                string responseBody = result.ResponseBody;

                genResponse.StatusCode = (int)response.StatusCode;

                if (!response.IsSuccessStatusCode)
                {
                    _Logging.Warn(_Header + "generate request failed with status " + (int)response.StatusCode + ": " + responseBody);
                    genResponse.Success = false;
                    genResponse.Error = "HTTP " + (int)response.StatusCode + ": " + responseBody;
                    return genResponse;
                }

                Dictionary<string, object>? responseObj = _Serializer.DeserializeJson<Dictionary<string, object>>(responseBody);
                if (responseObj != null && responseObj.ContainsKey("response"))
                {
                    string? text = responseObj["response"]?.ToString();
                    genResponse.Text = string.IsNullOrWhiteSpace(text) ? null : text;
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

            string url = _Endpoint.TrimEnd('/') + "/api/generate";

            Dictionary<string, object> requestBody = BuildGenerateRequestBody(prompt, model, maxTokens, temperature, topP, options as OllamaGenerationOptions, true);

            string json = _Serializer.SerializeJson(requestBody, false);
            StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

            _Logging.Debug(_Header + "POST (streaming) " + url);

            Stopwatch sw = Stopwatch.StartNew();

            GenerationStreamingResponse streamingResponse = new GenerationStreamingResponse();
            streamingResponse.Model = model;

            try
            {
                HttpResponseMessage response = await PostStreamingAsync(url, content, token).ConfigureAwait(false);
                streamingResponse.StatusCode = (int)response.StatusCode;

                if (!response.IsSuccessStatusCode)
                {
                    string errorBody = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                    _Logging.Warn(_Header + "streaming generate request failed with status " + (int)response.StatusCode + ": " + errorBody);
                    streamingResponse.Success = false;
                    streamingResponse.Error = "HTTP " + (int)response.StatusCode + ": " + errorBody;
                    return streamingResponse;
                }

                streamingResponse.Success = true;
                streamingResponse.Chunks = WrapGenerationChunksWithTiming(streamingResponse, ReadOllamaGenerateChunks(response, token), sw, token);
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
            string url = _Endpoint.TrimEnd('/') + "/api/tags";

            _Logging.Debug(_Header + "GET " + url);

            CompletionHttpResult result = await GetAndRecordAsync(url, token).ConfigureAwait(false);

            if (!result.Response.IsSuccessStatusCode)
            {
                _Logging.Warn(_Header + "list models failed with status " + (int)result.Response.StatusCode);
                yield break;
            }

            Dictionary<string, object>? responseObj = _Serializer.DeserializeJson<Dictionary<string, object>>(result.ResponseBody);
            if (responseObj == null || !responseObj.ContainsKey("models"))
                yield break;

            string modelsJson = _Serializer.SerializeJson(responseObj["models"], false);
            List<Dictionary<string, object>>? modelsList = _Serializer.DeserializeJson<List<Dictionary<string, object>>>(modelsJson);
            if (modelsList == null)
                yield break;

            foreach (Dictionary<string, object> modelObj in modelsList)
            {
                token.ThrowIfCancellationRequested();

                ModelInformation info = new ModelInformation();
                info.Name = modelObj.ContainsKey("model") ? modelObj["model"]?.ToString() ?? "" : "";
                info.DisplayName = modelObj.ContainsKey("name") ? modelObj["name"]?.ToString() : null;

                if (modelObj.ContainsKey("modified_at"))
                {
                    string? modifiedAt = modelObj["modified_at"]?.ToString();
                    if (!string.IsNullOrEmpty(modifiedAt) && DateTime.TryParse(modifiedAt, out DateTime parsed))
                    {
                        info.ModifiedUtc = parsed.ToUniversalTime();
                    }
                }

                if (modelObj.ContainsKey("size"))
                {
                    string? sizeStr = modelObj["size"]?.ToString();
                    if (long.TryParse(sizeStr, out long size))
                    {
                        info.SizeBytes = size;
                    }
                }

                if (modelObj.ContainsKey("digest"))
                {
                    info.Metadata["digest"] = modelObj["digest"]?.ToString();
                }

                if (modelObj.ContainsKey("details"))
                {
                    string detailsJson = _Serializer.SerializeJson(modelObj["details"], false);
                    Dictionary<string, object>? details = _Serializer.DeserializeJson<Dictionary<string, object>>(detailsJson);
                    if (details != null)
                    {
                        if (details.ContainsKey("parameter_size")) info.Metadata["parameter_size"] = details["parameter_size"]?.ToString();
                        if (details.ContainsKey("quantization_level")) info.Metadata["quantization_level"] = details["quantization_level"]?.ToString();
                        if (details.ContainsKey("family")) info.Metadata["family"] = details["family"]?.ToString();
                        if (details.ContainsKey("format")) info.Metadata["format"] = details["format"]?.ToString();
                    }
                }

                yield return info;
            }
        }

        /// <inheritdoc />
        public override async Task<ModelInformation?> GetModelInformationAsync(string model, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(model))
                throw new ArgumentNullException(nameof(model));

            string url = _Endpoint.TrimEnd('/') + "/api/show";

            Dictionary<string, object> requestBody = new Dictionary<string, object>
            {
                { "model", model }
            };

            string json = _Serializer.SerializeJson(requestBody, false);
            StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

            _Logging.Debug(_Header + "POST " + url + " (show " + model + ")");

            try
            {
                CompletionHttpResult result = await PostAndRecordAsync(url, content, json, token).ConfigureAwait(false);
                HttpResponseMessage response = result.Response;
                string responseBody = result.ResponseBody;

                if (!response.IsSuccessStatusCode)
                {
                    _Logging.Warn(_Header + "show request failed with status " + (int)response.StatusCode + ": " + responseBody);
                    return null;
                }

                Dictionary<string, object>? responseObj = _Serializer.DeserializeJson<Dictionary<string, object>>(responseBody);
                if (responseObj == null) return null;

                ModelInformation info = new ModelInformation();
                info.Name = model;

                if (responseObj.ContainsKey("modified_at"))
                {
                    string? modifiedStr = responseObj["modified_at"]?.ToString();
                    if (!string.IsNullOrEmpty(modifiedStr) && DateTime.TryParse(modifiedStr, out DateTime modified))
                    {
                        info.ModifiedUtc = modified.ToUniversalTime();
                    }
                }

                if (responseObj.ContainsKey("details"))
                {
                    string detailsJson = _Serializer.SerializeJson(responseObj["details"], false);
                    Dictionary<string, object>? details = _Serializer.DeserializeJson<Dictionary<string, object>>(detailsJson);
                    if (details != null)
                    {
                        if (details.ContainsKey("parameter_size"))
                            info.Metadata["parameter_size"] = details["parameter_size"]?.ToString();
                        if (details.ContainsKey("quantization_level"))
                            info.Metadata["quantization_level"] = details["quantization_level"]?.ToString();
                        if (details.ContainsKey("family"))
                            info.Metadata["family"] = details["family"]?.ToString();
                        if (details.ContainsKey("format"))
                            info.Metadata["format"] = details["format"]?.ToString();
                        if (details.ContainsKey("families"))
                            info.Metadata["families"] = _Serializer.SerializeJson(details["families"], false);
                        if (details.ContainsKey("parent_model"))
                            info.Metadata["parent_model"] = details["parent_model"]?.ToString();
                    }
                }

                if (responseObj.ContainsKey("license"))
                    info.Metadata["license"] = responseObj["license"]?.ToString();
                if (responseObj.ContainsKey("template"))
                    info.Metadata["template"] = responseObj["template"]?.ToString();
                if (responseObj.ContainsKey("parameters"))
                    info.Metadata["parameters"] = responseObj["parameters"]?.ToString();
                if (responseObj.ContainsKey("capabilities"))
                    info.Metadata["capabilities"] = _Serializer.SerializeJson(responseObj["capabilities"], false);

                return info;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "show request failed: " + ex.Message);
                return null;
            }
        }

        /// <inheritdoc />
        public override async Task<bool> PullModelAsync(
            string model,
            Func<ModelPullProgress, Task>? progress = null,
            CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(model))
                throw new ArgumentNullException(nameof(model));

            string url = _Endpoint.TrimEnd('/') + "/api/pull";

            Dictionary<string, object> requestBody = new Dictionary<string, object>
            {
                { "name", model },
                { "stream", true }
            };

            string json = _Serializer.SerializeJson(requestBody, false);
            StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

            _Logging.Debug(_Header + "POST " + url + " (pull " + model + ")");

            try
            {
                using CancellationTokenSource timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(30));
                using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Content = content;

                HttpResponseMessage response = await _HttpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    linkedCts.Token).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    string errorBody = await response.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);
                    _Logging.Warn(_Header + "pull request failed with status " + (int)response.StatusCode + ": " + errorBody);
                    return false;
                }

                using Stream stream = await response.Content.ReadAsStreamAsync(linkedCts.Token).ConfigureAwait(false);
                using StreamReader reader = new StreamReader(stream);

                bool success = false;
                string? line;

                while ((line = await reader.ReadLineAsync(linkedCts.Token).ConfigureAwait(false)) != null)
                {
                    linkedCts.Token.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    Dictionary<string, object>? chunk = _Serializer.DeserializeJson<Dictionary<string, object>>(line);
                    if (chunk == null) continue;

                    string status = chunk.ContainsKey("status") ? chunk["status"]?.ToString() ?? "" : "";

                    if (string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
                    {
                        success = true;
                    }

                    if (progress != null)
                    {
                        ModelPullProgress pullProgress = new ModelPullProgress();
                        pullProgress.Status = status;
                        pullProgress.Digest = chunk.ContainsKey("digest") ? chunk["digest"]?.ToString() : null;
                        pullProgress.TotalBytes = TryGetLong(chunk, "total");
                        pullProgress.CompletedBytes = TryGetLong(chunk, "completed");

                        await progress(pullProgress).ConfigureAwait(false);
                    }
                }

                return success;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "pull request failed: " + ex.Message);
                return false;
            }
        }

        /// <inheritdoc />
        public override async Task<bool> DeleteModelAsync(string model, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(model))
                throw new ArgumentNullException(nameof(model));

            string url = _Endpoint.TrimEnd('/') + "/api/delete";

            Dictionary<string, object> requestBody = new Dictionary<string, object>
            {
                { "model", model }
            };

            string json = _Serializer.SerializeJson(requestBody, false);

            _Logging.Debug(_Header + "DELETE " + url + " (model " + model + ")");

            try
            {
                using CancellationTokenSource timeoutCts = new CancellationTokenSource(TimeoutMs);
                using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Delete, url);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _HttpClient.SendAsync(request, linkedCts.Token).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    _Logging.Debug(_Header + "model '" + model + "' deleted successfully");
                    return true;
                }

                string errorBody = await response.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);
                _Logging.Warn(_Header + "delete request failed with status " + (int)response.StatusCode + ": " + errorBody);
                return false;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "delete request failed: " + ex.Message);
                return false;
            }
        }

        #endregion

        #region Private-Methods

        private Dictionary<string, object> BuildChatRequestBody(
            string prompt, int maxTokens, string? systemPrompt,
            double? temperature, double? topP,
            OllamaChatCompletionOptions? ollamaOptions, bool stream)
        {
            List<Dictionary<string, string>> messages = new List<Dictionary<string, string>>();
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                messages.Add(new Dictionary<string, string> { { "role", "system" }, { "content", systemPrompt } });
            }
            messages.Add(new Dictionary<string, string> { { "role", "user" }, { "content", prompt } });

            Dictionary<string, object> modelOptions = new Dictionary<string, object>();
            modelOptions["num_predict"] = maxTokens;

            int? contextLength = ollamaOptions?.ContextLength ?? _ContextLength;
            if (contextLength.HasValue) modelOptions["num_ctx"] = contextLength.Value;
            if (temperature.HasValue) modelOptions["temperature"] = temperature.Value;
            if (topP.HasValue) modelOptions["top_p"] = topP.Value;

            if (ollamaOptions != null)
            {
                if (ollamaOptions.TopK.HasValue) modelOptions["top_k"] = ollamaOptions.TopK.Value;
                if (ollamaOptions.RepeatPenalty.HasValue) modelOptions["repeat_penalty"] = ollamaOptions.RepeatPenalty.Value;
                if (ollamaOptions.Seed.HasValue) modelOptions["seed"] = ollamaOptions.Seed.Value;
                if (ollamaOptions.MinP.HasValue) modelOptions["min_p"] = ollamaOptions.MinP.Value;
                if (ollamaOptions.RepeatLastN.HasValue) modelOptions["repeat_last_n"] = ollamaOptions.RepeatLastN.Value;
            }

            Dictionary<string, object> requestBody = new Dictionary<string, object>
            {
                { "model", Model },
                { "messages", messages },
                { "stream", stream },
                { "options", modelOptions }
            };

            return requestBody;
        }

        private Dictionary<string, object> BuildGenerateRequestBody(
            string prompt, string model, int maxTokens,
            double? temperature, double? topP,
            OllamaGenerationOptions? ollamaOptions, bool stream)
        {
            Dictionary<string, object> modelOptions = new Dictionary<string, object>();
            modelOptions["num_predict"] = maxTokens;

            int? contextLength = ollamaOptions?.ContextLength ?? _ContextLength;
            if (contextLength.HasValue) modelOptions["num_ctx"] = contextLength.Value;
            if (temperature.HasValue) modelOptions["temperature"] = temperature.Value;
            if (topP.HasValue) modelOptions["top_p"] = topP.Value;

            if (ollamaOptions != null)
            {
                if (ollamaOptions.TopK.HasValue) modelOptions["top_k"] = ollamaOptions.TopK.Value;
                if (ollamaOptions.RepeatPenalty.HasValue) modelOptions["repeat_penalty"] = ollamaOptions.RepeatPenalty.Value;
                if (ollamaOptions.Seed.HasValue) modelOptions["seed"] = ollamaOptions.Seed.Value;
                if (ollamaOptions.MinP.HasValue) modelOptions["min_p"] = ollamaOptions.MinP.Value;
                if (ollamaOptions.RepeatLastN.HasValue) modelOptions["repeat_last_n"] = ollamaOptions.RepeatLastN.Value;
            }

            Dictionary<string, object> requestBody = new Dictionary<string, object>
            {
                { "model", model },
                { "prompt", prompt },
                { "stream", stream },
                { "options", modelOptions }
            };

            return requestBody;
        }

        private async IAsyncEnumerable<ChatStreamingChunk> ReadOllamaChatChunks(
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

                Dictionary<string, object>? chunk = _Serializer.DeserializeJson<Dictionary<string, object>>(line);
                if (chunk == null) continue;

                ChatStreamingChunk streamChunk = new ChatStreamingChunk();
                streamChunk.Model = chunk.ContainsKey("model") ? chunk["model"]?.ToString() : null;

                if (chunk.ContainsKey("created_at"))
                {
                    string? createdAt = chunk["created_at"]?.ToString();
                    if (!string.IsNullOrEmpty(createdAt) && DateTime.TryParse(createdAt, out DateTime parsed))
                    {
                        streamChunk.CreatedUtc = parsed.ToUniversalTime();
                    }
                }

                if (chunk.ContainsKey("message"))
                {
                    string msgJson = _Serializer.SerializeJson(chunk["message"], false);
                    Dictionary<string, object>? msg = _Serializer.DeserializeJson<Dictionary<string, object>>(msgJson);
                    if (msg != null && msg.ContainsKey("content"))
                    {
                        streamChunk.Text = msg["content"]?.ToString();
                    }
                }

                bool done = IsTruthy(chunk, "done");
                streamChunk.Done = done;

                if (done)
                {
                    streamChunk.FinishReason = chunk.ContainsKey("done_reason") ? chunk["done_reason"]?.ToString() : null;

                    ChatStreamingUsage usage = new ChatStreamingUsage();
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

                    streamChunk.Usage = usage;
                }

                yield return streamChunk;
            }
        }

        private async IAsyncEnumerable<GenerationStreamingChunk> ReadOllamaGenerateChunks(
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

                Dictionary<string, object>? chunk = _Serializer.DeserializeJson<Dictionary<string, object>>(line);
                if (chunk == null) continue;

                GenerationStreamingChunk streamChunk = new GenerationStreamingChunk();
                streamChunk.Model = chunk.ContainsKey("model") ? chunk["model"]?.ToString() : null;
                streamChunk.Text = chunk.ContainsKey("response") ? chunk["response"]?.ToString() : null;

                bool done = IsTruthy(chunk, "done");
                streamChunk.Done = done;

                yield return streamChunk;
            }
        }

        #endregion
    }
}
