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
    /// Client for the Google Gemini API supporting chat completions, embeddings, and text generation.
    /// </summary>
    public class GeminiClient : CompletionClientBase
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new GeminiClient.
        /// </summary>
        /// <param name="endpoint">Gemini API endpoint URL. Default: https://generativelanguage.googleapis.com.</param>
        /// <param name="apiKey">Google API key (required). Default: null.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        public GeminiClient(
            string endpoint = "https://generativelanguage.googleapis.com",
            string? apiKey = null,
            LoggingModule? logging = null)
            : base(endpoint, apiKey, logging ?? new LoggingModule())
        {
            _Header = "[Gemini] ";
            Model = "gemini-2.5-flash";
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

            string url = _Endpoint.TrimEnd('/')
                + "/v1beta/models/" + Model + ":generateContent"
                + "?key=" + _ApiKey;

            Dictionary<string, object> requestBody = BuildChatRequestBody(prompt, maxTokens, systemPrompt, temperature, topP, options as GeminiChatCompletionOptions);

            string json = _Serializer.SerializeJson(requestBody, false);
            StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

            _Logging.Debug(_Header + "POST " + _Endpoint.TrimEnd('/') + "/v1beta/models/" + Model + ":generateContent");

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

            string url = _Endpoint.TrimEnd('/')
                + "/v1beta/models/" + Model + ":streamGenerateContent"
                + "?alt=sse&key=" + _ApiKey;

            Dictionary<string, object> requestBody = BuildChatRequestBody(prompt, maxTokens, systemPrompt, temperature, topP, options as GeminiChatCompletionOptions);

            string json = _Serializer.SerializeJson(requestBody, false);
            StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

            _Logging.Debug(_Header + "POST (streaming) " + _Endpoint.TrimEnd('/') + "/v1beta/models/" + Model + ":streamGenerateContent");

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
                streamingResponse.Chunks = WrapChunksWithTiming(streamingResponse, ReadGeminiChunks(response, streamingResult.Token), sw, streamingResult.Token, streamingResult);
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

            string url = _Endpoint.TrimEnd('/')
                + "/v1beta/models/" + model + ":generateContent"
                + "?key=" + _ApiKey;

            Dictionary<string, object> requestBody = BuildToolChatRequestBody(request, maxTokens, temperature, topP);

            string json = _Serializer.SerializeJson(requestBody, false);
            StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

            _Logging.Debug(_Header + "POST " + _Endpoint.TrimEnd('/') + "/v1beta/models/" + model + ":generateContent");

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

                PopulateGeminiToolChatResponse(responseBody, toolResponse);
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
        public override async Task<EmbeddingResponse> EmbedAsync(
            string input,
            EmbeddingOptions? options = null,
            CancellationToken token = default)
        {
            EmbeddingResponse embedResponse = new EmbeddingResponse();
            string model = options?.Model ?? Model;
            embedResponse.Model = model;

            Stopwatch sw = Stopwatch.StartNew();

            string url = _Endpoint.TrimEnd('/')
                + "/v1beta/models/" + model + ":embedContent"
                + "?key=" + _ApiKey;

            Dictionary<string, object> requestBody = new Dictionary<string, object>
            {
                { "model", "models/" + model },
                { "content", new Dictionary<string, object>
                    {
                        { "parts", new List<Dictionary<string, string>>
                            {
                                new Dictionary<string, string> { { "text", input } }
                            }
                        }
                    }
                }
            };

            GeminiEmbeddingOptions? geminiOptions = options as GeminiEmbeddingOptions;
            if (geminiOptions != null)
            {
                if (!string.IsNullOrEmpty(geminiOptions.TaskType)) requestBody["taskType"] = geminiOptions.TaskType;
                if (!string.IsNullOrEmpty(geminiOptions.Title)) requestBody["title"] = geminiOptions.Title;
            }

            string json = _Serializer.SerializeJson(requestBody, false);
            StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

            _Logging.Debug(_Header + "POST " + _Endpoint.TrimEnd('/') + "/v1beta/models/" + model + ":embedContent");

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
                if (responseObj != null && responseObj.ContainsKey("embedding"))
                {
                    string embeddingJson = _Serializer.SerializeJson(responseObj["embedding"], false);
                    Dictionary<string, object>? embeddingObj = _Serializer.DeserializeJson<Dictionary<string, object>>(embeddingJson);

                    if (embeddingObj != null && embeddingObj.ContainsKey("values"))
                    {
                        string valuesJson = _Serializer.SerializeJson(embeddingObj["values"], false);
                        float[] vector = ParseFloatArray(valuesJson);

                        EmbeddingResult embResult = new EmbeddingResult();
                        embResult.Index = 0;
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
        public override async Task<EmbeddingResponse> EmbedAsync(
            List<string> inputs,
            EmbeddingOptions? options = null,
            CancellationToken token = default)
        {
            EmbeddingResponse embedResponse = new EmbeddingResponse();
            string model = options?.Model ?? Model;
            embedResponse.Model = model;

            Stopwatch sw = Stopwatch.StartNew();

            string url = _Endpoint.TrimEnd('/')
                + "/v1beta/models/" + model + ":batchEmbedContents"
                + "?key=" + _ApiKey;

            GeminiEmbeddingOptions? geminiOptions = options as GeminiEmbeddingOptions;

            List<Dictionary<string, object>> requests = new List<Dictionary<string, object>>();
            foreach (string input in inputs)
            {
                Dictionary<string, object> req = new Dictionary<string, object>
                {
                    { "model", "models/" + model },
                    { "content", new Dictionary<string, object>
                        {
                            { "parts", new List<Dictionary<string, string>>
                                {
                                    new Dictionary<string, string> { { "text", input } }
                                }
                            }
                        }
                    }
                };

                if (geminiOptions != null)
                {
                    if (!string.IsNullOrEmpty(geminiOptions.TaskType)) req["taskType"] = geminiOptions.TaskType;
                    if (!string.IsNullOrEmpty(geminiOptions.Title)) req["title"] = geminiOptions.Title;
                }

                requests.Add(req);
            }

            Dictionary<string, object> requestBody = new Dictionary<string, object>
            {
                { "requests", requests }
            };

            string json = _Serializer.SerializeJson(requestBody, false);
            StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

            _Logging.Debug(_Header + "POST " + _Endpoint.TrimEnd('/') + "/v1beta/models/" + model + ":batchEmbedContents");

            try
            {
                CompletionHttpResult result = await PostAndRecordAsync(url, content, json, token).ConfigureAwait(false);
                string responseBody = result.ResponseBody;

                embedResponse.StatusCode = result.StatusCode;

                if (!result.IsSuccessStatusCode)
                {
                    _Logging.Warn(_Header + "batch embed request failed with status " + result.StatusCode + ": " + responseBody);
                    embedResponse.Success = false;
                    embedResponse.Error = "HTTP " + result.StatusCode + ": " + responseBody;
                    return embedResponse;
                }

                Dictionary<string, object>? responseObj = _Serializer.DeserializeJson<Dictionary<string, object>>(responseBody);
                if (responseObj != null && responseObj.ContainsKey("embeddings"))
                {
                    string embeddingsJson = _Serializer.SerializeJson(responseObj["embeddings"], false);
                    List<Dictionary<string, object>>? embeddingsList = _Serializer.DeserializeJson<List<Dictionary<string, object>>>(embeddingsJson);

                    if (embeddingsList != null)
                    {
                        for (int i = 0; i < embeddingsList.Count; i++)
                        {
                            if (embeddingsList[i].ContainsKey("values"))
                            {
                                string valuesJson = _Serializer.SerializeJson(embeddingsList[i]["values"], false);
                                float[] vector = ParseFloatArray(valuesJson);

                                EmbeddingResult embResult = new EmbeddingResult();
                                embResult.Index = i;
                                embResult.Embedding = vector;
                                embedResponse.Embeddings.Add(embResult);
                            }
                        }
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

            string url = _Endpoint.TrimEnd('/')
                + "/v1beta/models/" + model + ":generateContent"
                + "?key=" + _ApiKey;

            Dictionary<string, object> requestBody = BuildGenerateRequestBody(prompt, maxTokens, temperature, topP, options as GeminiGenerationOptions);

            string json = _Serializer.SerializeJson(requestBody, false);
            StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

            _Logging.Debug(_Header + "POST " + _Endpoint.TrimEnd('/') + "/v1beta/models/" + model + ":generateContent");

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

            string url = _Endpoint.TrimEnd('/')
                + "/v1beta/models/" + model + ":streamGenerateContent"
                + "?alt=sse&key=" + _ApiKey;

            Dictionary<string, object> requestBody = BuildGenerateRequestBody(prompt, maxTokens, temperature, topP, options as GeminiGenerationOptions);

            string json = _Serializer.SerializeJson(requestBody, false);
            StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

            _Logging.Debug(_Header + "POST (streaming) " + _Endpoint.TrimEnd('/') + "/v1beta/models/" + model + ":streamGenerateContent");

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
                streamingResponse.Chunks = WrapGenerationChunksWithTiming(streamingResponse, ReadGeminiGenerateChunks(response, streamingResult.Token), sw, streamingResult.Token, streamingResult);
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
            string url = _Endpoint.TrimEnd('/') + "/v1beta/models?key=" + _ApiKey;

            _Logging.Debug(_Header + "GET " + _Endpoint.TrimEnd('/') + "/v1beta/models");

            CompletionHttpResult result = await GetAndRecordAsync(url, token).ConfigureAwait(false);

            if (!result.IsSuccessStatusCode)
            {
                _Logging.Warn(_Header + "list models failed with status " + result.StatusCode);
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

                string rawName = modelObj.ContainsKey("name") ? modelObj["name"]?.ToString() ?? "" : "";
                info.Name = rawName.StartsWith("models/") ? rawName.Substring(7) : rawName;

                info.DisplayName = modelObj.ContainsKey("displayName") ? modelObj["displayName"]?.ToString() : null;
                info.Description = modelObj.ContainsKey("description") ? modelObj["description"]?.ToString() : null;

                if (modelObj.ContainsKey("inputTokenLimit"))
                {
                    string? val = modelObj["inputTokenLimit"]?.ToString();
                    if (int.TryParse(val, out int inputLimit))
                    {
                        info.InputTokenLimit = inputLimit;
                    }
                }

                if (modelObj.ContainsKey("outputTokenLimit"))
                {
                    string? val = modelObj["outputTokenLimit"]?.ToString();
                    if (int.TryParse(val, out int outputLimit))
                    {
                        info.OutputTokenLimit = outputLimit;
                    }
                }

                if (modelObj.ContainsKey("supportedGenerationMethods"))
                {
                    string methodsJson = _Serializer.SerializeJson(modelObj["supportedGenerationMethods"], false);
                    info.Metadata["supportedGenerationMethods"] = methodsJson;
                }

                yield return info;
            }
        }

        /// <inheritdoc />
        public override async Task<ModelInformation?> GetModelInformationAsync(string model, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(model))
                throw new ArgumentNullException(nameof(model));

            string modelPath = model.StartsWith("models/") ? model : "models/" + model;
            string url = _Endpoint.TrimEnd('/') + "/v1beta/" + modelPath + "?key=" + _ApiKey;
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

                string rawName = responseObj.ContainsKey("name") ? responseObj["name"]?.ToString() ?? model : model;
                info.Name = rawName.StartsWith("models/") ? rawName.Substring(7) : rawName;
                info.DisplayName = responseObj.ContainsKey("displayName") ? responseObj["displayName"]?.ToString() : null;
                info.Description = responseObj.ContainsKey("description") ? responseObj["description"]?.ToString() : null;

                long? inputLimit = TryGetLong(responseObj, "inputTokenLimit");
                if (inputLimit.HasValue) info.InputTokenLimit = (int)inputLimit.Value;

                long? outputLimit = TryGetLong(responseObj, "outputTokenLimit");
                if (outputLimit.HasValue) info.OutputTokenLimit = (int)outputLimit.Value;

                if (responseObj.ContainsKey("supportedGenerationMethods"))
                {
                    string methodsJson = _Serializer.SerializeJson(responseObj["supportedGenerationMethods"], false);
                    info.Metadata["supportedGenerationMethods"] = methodsJson;
                }

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

        private Dictionary<string, object> BuildChatRequestBody(
            string prompt, int maxTokens, string? systemPrompt,
            double? temperature, double? topP,
            GeminiChatCompletionOptions? geminiOptions)
        {
            List<Dictionary<string, object>> contents = new List<Dictionary<string, object>>();

            if (!string.IsNullOrEmpty(systemPrompt))
            {
                contents.Add(new Dictionary<string, object>
                {
                    { "role", "user" },
                    { "parts", new List<Dictionary<string, string>>
                        {
                            new Dictionary<string, string> { { "text", systemPrompt } }
                        }
                    }
                });
                contents.Add(new Dictionary<string, object>
                {
                    { "role", "model" },
                    { "parts", new List<Dictionary<string, string>>
                        {
                            new Dictionary<string, string> { { "text", "Understood. I will follow those instructions." } }
                        }
                    }
                });
            }

            contents.Add(new Dictionary<string, object>
            {
                { "role", "user" },
                { "parts", new List<Dictionary<string, string>>
                    {
                        new Dictionary<string, string> { { "text", prompt } }
                    }
                }
            });

            Dictionary<string, object> generationConfig = new Dictionary<string, object>
            {
                { "maxOutputTokens", maxTokens }
            };

            if (temperature.HasValue) generationConfig["temperature"] = temperature.Value;
            if (topP.HasValue) generationConfig["topP"] = topP.Value;

            if (geminiOptions != null)
            {
                if (geminiOptions.TopK.HasValue) generationConfig["topK"] = geminiOptions.TopK.Value;
                if (geminiOptions.CandidateCount.HasValue) generationConfig["candidateCount"] = geminiOptions.CandidateCount.Value;
                if (geminiOptions.PresencePenalty.HasValue) generationConfig["presencePenalty"] = geminiOptions.PresencePenalty.Value;
                if (geminiOptions.FrequencyPenalty.HasValue) generationConfig["frequencyPenalty"] = geminiOptions.FrequencyPenalty.Value;
            }

            Dictionary<string, object> requestBody = new Dictionary<string, object>
            {
                { "contents", contents },
                { "generationConfig", generationConfig }
            };

            return requestBody;
        }

        private Dictionary<string, object> BuildToolChatRequestBody(
            ToolChatRequest request,
            int maxTokens,
            double? temperature,
            double? topP)
        {
            Dictionary<string, object> generationConfig = new Dictionary<string, object>
            {
                { "maxOutputTokens", maxTokens }
            };

            if (temperature.HasValue) generationConfig["temperature"] = temperature.Value;
            if (topP.HasValue) generationConfig["topP"] = topP.Value;

            Dictionary<string, object> requestBody = new Dictionary<string, object>
            {
                { "contents", BuildGeminiContents(request.Messages) },
                { "generationConfig", generationConfig }
            };

            Dictionary<string, object>? systemInstruction = BuildGeminiSystemInstruction(request.Messages);
            if (systemInstruction != null)
            {
                requestBody["systemInstruction"] = systemInstruction;
            }

            if (request.Tools != null && request.Tools.Count > 0 && !IsToolChoiceNone(request.ToolChoice))
            {
                requestBody["tools"] = BuildGeminiTools(request.Tools);
                requestBody["toolConfig"] = BuildGeminiToolConfig(request.ToolChoice);
            }

            return requestBody;
        }

        private List<Dictionary<string, object>> BuildGeminiContents(List<ChatMessage> messages)
        {
            List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();

            foreach (ChatMessage message in messages)
            {
                if (string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase))
                    continue;

                Dictionary<string, object> item = new Dictionary<string, object>
                {
                    { "role", NormalizeGeminiRole(message.Role) },
                    { "parts", BuildGeminiParts(message) }
                };

                result.Add(item);
            }

            return result;
        }

        private Dictionary<string, object>? BuildGeminiSystemInstruction(List<ChatMessage> messages)
        {
            List<Dictionary<string, string>> parts = new List<Dictionary<string, string>>();

            foreach (ChatMessage message in messages)
            {
                if (string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(message.Content))
                {
                    parts.Add(new Dictionary<string, string> { { "text", message.Content } });
                }
            }

            if (parts.Count == 0) return null;

            return new Dictionary<string, object>
            {
                { "parts", parts }
            };
        }

        private List<Dictionary<string, object>> BuildGeminiParts(ChatMessage message)
        {
            List<Dictionary<string, object>> parts = new List<Dictionary<string, object>>();

            if (message.ToolCalls != null && message.ToolCalls.Count > 0)
            {
                foreach (ToolCall toolCall in message.ToolCalls)
                {
                    Dictionary<string, object> functionCall = new Dictionary<string, object>
                    {
                        { "name", toolCall.Name },
                        { "args", DeserializeDictionaryOrEmpty(toolCall.ArgumentsJson) }
                    };

                    parts.Add(new Dictionary<string, object> { { "functionCall", functionCall } });
                }

                return parts;
            }

            if (string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase)
                || string.Equals(message.Role, "function", StringComparison.OrdinalIgnoreCase))
            {
                Dictionary<string, object> functionResponse = new Dictionary<string, object>
                {
                    { "name", ResolveToolResultName(message) },
                    { "response", DeserializeDictionaryOrResult(message.Content) }
                };

                parts.Add(new Dictionary<string, object> { { "functionResponse", functionResponse } });
                return parts;
            }

            parts.Add(new Dictionary<string, object> { { "text", message.Content ?? string.Empty } });
            return parts;
        }

        private List<Dictionary<string, object>> BuildGeminiTools(List<ToolDefinition> tools)
        {
            List<Dictionary<string, object>> functionDeclarations = new List<Dictionary<string, object>>();

            foreach (ToolDefinition tool in tools)
            {
                functionDeclarations.Add(new Dictionary<string, object>
                {
                    { "name", tool.Name },
                    { "description", tool.Description },
                    { "parameters", tool.Parameters }
                });
            }

            return new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    { "functionDeclarations", functionDeclarations }
                }
            };
        }

        private Dictionary<string, object> BuildGeminiToolConfig(string? toolChoice)
        {
            string mode = "AUTO";
            if (string.Equals(toolChoice, "required", StringComparison.OrdinalIgnoreCase))
                mode = "ANY";
            else if (string.Equals(toolChoice, "none", StringComparison.OrdinalIgnoreCase))
                mode = "NONE";

            return new Dictionary<string, object>
            {
                { "functionCallingConfig", new Dictionary<string, object> { { "mode", mode } } }
            };
        }

        private void PopulateGeminiToolChatResponse(string responseBody, ToolChatResponse toolResponse)
        {
            Dictionary<string, object>? responseObj = _Serializer.DeserializeJson<Dictionary<string, object>>(responseBody);
            if (responseObj == null || !responseObj.ContainsKey("candidates"))
            {
                toolResponse.Success = false;
                toolResponse.Error = "Response missing 'candidates' field";
                return;
            }

            toolResponse.ResponseId = responseObj.ContainsKey("responseId") ? responseObj["responseId"]?.ToString() : null;
            toolResponse.Model = responseObj.ContainsKey("modelVersion") ? responseObj["modelVersion"]?.ToString() ?? toolResponse.Model : toolResponse.Model;

            string candidatesJson = _Serializer.SerializeJson(responseObj["candidates"], false);
            List<Dictionary<string, object>>? candidates = _Serializer.DeserializeJson<List<Dictionary<string, object>>>(candidatesJson);
            if (candidates == null || candidates.Count == 0)
            {
                toolResponse.Success = false;
                toolResponse.Error = "Response has empty candidates array";
                return;
            }

            Dictionary<string, object> candidate = candidates[0];
            toolResponse.FinishReason = candidate.ContainsKey("finishReason") ? candidate["finishReason"]?.ToString() : null;

            if (!candidate.ContainsKey("content"))
            {
                toolResponse.Success = false;
                toolResponse.Error = "Response candidate missing 'content' field";
                return;
            }

            string contentJson = _Serializer.SerializeJson(candidate["content"], false);
            Dictionary<string, object>? contentObj = _Serializer.DeserializeJson<Dictionary<string, object>>(contentJson);
            if (contentObj == null || !contentObj.ContainsKey("parts"))
            {
                toolResponse.Success = false;
                toolResponse.Error = "Response content missing 'parts' field";
                return;
            }

            string partsJson = _Serializer.SerializeJson(contentObj["parts"], false);
            List<Dictionary<string, object>>? parts = _Serializer.DeserializeJson<List<Dictionary<string, object>>>(partsJson);
            if (parts == null) return;

            int index = 0;
            foreach (Dictionary<string, object> part in parts)
            {
                if (part.ContainsKey("text"))
                {
                    string? text = part["text"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        toolResponse.Text = string.IsNullOrEmpty(toolResponse.Text)
                            ? text
                            : toolResponse.Text + text;
                    }
                }

                if (part.ContainsKey("functionCall"))
                {
                    ToolCall? toolCall = ParseGeminiToolCall(part["functionCall"], index);
                    if (toolCall != null) toolResponse.ToolCalls.Add(toolCall);
                    index++;
                }
            }
        }

        private ToolCall? ParseGeminiToolCall(object functionCallObj, int index)
        {
            string functionCallJson = _Serializer.SerializeJson(functionCallObj, false);
            Dictionary<string, object>? functionCall = _Serializer.DeserializeJson<Dictionary<string, object>>(functionCallJson);
            if (functionCall == null || !functionCall.ContainsKey("name")) return null;

            ToolCall toolCall = new ToolCall();
            toolCall.Id = "gemini-call-" + index;
            toolCall.Name = functionCall["name"]?.ToString() ?? string.Empty;
            toolCall.ArgumentsJson = functionCall.ContainsKey("args") && functionCall["args"] != null
                ? _Serializer.SerializeJson(functionCall["args"], false)
                : "{}";
            return toolCall;
        }

        private Dictionary<string, object> DeserializeDictionaryOrEmpty(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, object>();

            Dictionary<string, object>? parsed = _Serializer.DeserializeJson<Dictionary<string, object>>(json);
            return parsed ?? new Dictionary<string, object>();
        }

        private Dictionary<string, object> DeserializeDictionaryOrResult(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new Dictionary<string, object> { { "result", string.Empty } };
            }

            Dictionary<string, object>? parsed = _Serializer.DeserializeJson<Dictionary<string, object>>(json);
            if (parsed != null) return parsed;

            return new Dictionary<string, object> { { "result", json } };
        }

        private static string ResolveToolResultName(ChatMessage message)
        {
            if (!string.IsNullOrWhiteSpace(message.ToolName)) return message.ToolName;
            if (!string.IsNullOrWhiteSpace(message.ToolCallId)) return message.ToolCallId;
            return "tool";
        }

        private static string NormalizeGeminiRole(string? role)
        {
            if (string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase)) return "model";
            if (string.Equals(role, "tool", StringComparison.OrdinalIgnoreCase)) return "function";
            return string.IsNullOrWhiteSpace(role) ? "user" : role.ToLowerInvariant();
        }

        private static bool IsToolChoiceNone(string? toolChoice)
        {
            return string.Equals(toolChoice, "none", StringComparison.OrdinalIgnoreCase);
        }

        private Dictionary<string, object> BuildGenerateRequestBody(
            string prompt, int maxTokens,
            double? temperature, double? topP,
            GeminiGenerationOptions? geminiOptions)
        {
            List<Dictionary<string, object>> contents = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    { "role", "user" },
                    { "parts", new List<Dictionary<string, string>>
                        {
                            new Dictionary<string, string> { { "text", prompt } }
                        }
                    }
                }
            };

            Dictionary<string, object> generationConfig = new Dictionary<string, object>
            {
                { "maxOutputTokens", maxTokens }
            };

            if (temperature.HasValue) generationConfig["temperature"] = temperature.Value;
            if (topP.HasValue) generationConfig["topP"] = topP.Value;

            if (geminiOptions != null)
            {
                if (geminiOptions.TopK.HasValue) generationConfig["topK"] = geminiOptions.TopK.Value;
                if (geminiOptions.CandidateCount.HasValue) generationConfig["candidateCount"] = geminiOptions.CandidateCount.Value;
                if (geminiOptions.PresencePenalty.HasValue) generationConfig["presencePenalty"] = geminiOptions.PresencePenalty.Value;
                if (geminiOptions.FrequencyPenalty.HasValue) generationConfig["frequencyPenalty"] = geminiOptions.FrequencyPenalty.Value;
            }

            Dictionary<string, object> requestBody = new Dictionary<string, object>
            {
                { "contents", contents },
                { "generationConfig", generationConfig }
            };

            return requestBody;
        }

        private async IAsyncEnumerable<ChatStreamingChunk> ReadGeminiChunks(
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

                Dictionary<string, object>? chunk = _Serializer.DeserializeJson<Dictionary<string, object>>(data);
                if (chunk == null) continue;

                ChatStreamingChunk streamChunk = new ChatStreamingChunk();
                streamChunk.ResponseId = chunk.ContainsKey("responseId") ? chunk["responseId"]?.ToString() : null;
                streamChunk.Model = chunk.ContainsKey("modelVersion") ? chunk["modelVersion"]?.ToString() : null;
                streamChunk.CreatedUtc = DateTime.UtcNow;

                if (chunk.ContainsKey("candidates"))
                {
                    string candidatesJson = _Serializer.SerializeJson(chunk["candidates"], false);
                    List<Dictionary<string, object>>? candidates = _Serializer.DeserializeJson<List<Dictionary<string, object>>>(candidatesJson);

                    if (candidates != null && candidates.Count > 0)
                    {
                        Dictionary<string, object> candidate = candidates[0];

                        if (candidate.ContainsKey("finishReason"))
                        {
                            streamChunk.FinishReason = candidate["finishReason"]?.ToString();
                            streamChunk.Done = true;
                        }

                        if (candidate.ContainsKey("content"))
                        {
                            string contentJson = _Serializer.SerializeJson(candidate["content"], false);
                            Dictionary<string, object>? contentObj = _Serializer.DeserializeJson<Dictionary<string, object>>(contentJson);

                            if (contentObj != null && contentObj.ContainsKey("parts"))
                            {
                                string partsJson = _Serializer.SerializeJson(contentObj["parts"], false);
                                List<Dictionary<string, object>>? parts = _Serializer.DeserializeJson<List<Dictionary<string, object>>>(partsJson);

                                if (parts != null && parts.Count > 0 && parts[0].ContainsKey("text"))
                                {
                                    streamChunk.Text = parts[0]["text"]?.ToString();
                                }
                            }
                        }
                    }
                }

                if (chunk.ContainsKey("usageMetadata"))
                {
                    string usageJson = _Serializer.SerializeJson(chunk["usageMetadata"], false);
                    Dictionary<string, object>? usageObj = _Serializer.DeserializeJson<Dictionary<string, object>>(usageJson);

                    if (usageObj != null)
                    {
                        ChatStreamingUsage usage = new ChatStreamingUsage();
                        if (usageObj.ContainsKey("promptTokenCount") && int.TryParse(usageObj["promptTokenCount"]?.ToString(), out int pt))
                            usage.PromptTokens = pt;
                        if (usageObj.ContainsKey("candidatesTokenCount") && int.TryParse(usageObj["candidatesTokenCount"]?.ToString(), out int ct))
                            usage.CompletionTokens = ct;
                        if (usageObj.ContainsKey("totalTokenCount") && int.TryParse(usageObj["totalTokenCount"]?.ToString(), out int tt))
                            usage.TotalTokens = tt;
                        streamChunk.Usage = usage;
                    }
                }

                yield return streamChunk;
            }
        }

        private async IAsyncEnumerable<GenerationStreamingChunk> ReadGeminiGenerateChunks(
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

                Dictionary<string, object>? chunk = _Serializer.DeserializeJson<Dictionary<string, object>>(data);
                if (chunk == null) continue;

                GenerationStreamingChunk streamChunk = new GenerationStreamingChunk();
                streamChunk.Model = chunk.ContainsKey("modelVersion") ? chunk["modelVersion"]?.ToString() : null;

                if (chunk.ContainsKey("candidates"))
                {
                    string candidatesJson = _Serializer.SerializeJson(chunk["candidates"], false);
                    List<Dictionary<string, object>>? candidates = _Serializer.DeserializeJson<List<Dictionary<string, object>>>(candidatesJson);

                    if (candidates != null && candidates.Count > 0)
                    {
                        Dictionary<string, object> candidate = candidates[0];

                        if (candidate.ContainsKey("finishReason"))
                        {
                            streamChunk.Done = true;
                        }

                        if (candidate.ContainsKey("content"))
                        {
                            string contentJson = _Serializer.SerializeJson(candidate["content"], false);
                            Dictionary<string, object>? contentObj = _Serializer.DeserializeJson<Dictionary<string, object>>(contentJson);

                            if (contentObj != null && contentObj.ContainsKey("parts"))
                            {
                                string partsJson = _Serializer.SerializeJson(contentObj["parts"], false);
                                List<Dictionary<string, object>>? parts = _Serializer.DeserializeJson<List<Dictionary<string, object>>>(partsJson);

                                if (parts != null && parts.Count > 0 && parts[0].ContainsKey("text"))
                                {
                                    streamChunk.Text = parts[0]["text"]?.ToString();
                                }
                            }
                        }
                    }
                }

                yield return streamChunk;
            }
        }

        private string? ExtractTextFromResponse(string responseBody)
        {
            Dictionary<string, object>? responseObj = _Serializer.DeserializeJson<Dictionary<string, object>>(responseBody);
            if (responseObj == null || !responseObj.ContainsKey("candidates"))
            {
                _Logging.Warn(_Header + "response missing 'candidates' field");
                return null;
            }

            string candidatesJson = _Serializer.SerializeJson(responseObj["candidates"], false);
            List<Dictionary<string, object>>? candidates = _Serializer.DeserializeJson<List<Dictionary<string, object>>>(candidatesJson);

            if (candidates == null || candidates.Count == 0) return null;
            if (!candidates[0].ContainsKey("content")) return null;

            string contentJson = _Serializer.SerializeJson(candidates[0]["content"], false);
            Dictionary<string, object>? contentObj = _Serializer.DeserializeJson<Dictionary<string, object>>(contentJson);

            if (contentObj == null || !contentObj.ContainsKey("parts")) return null;

            string partsJson = _Serializer.SerializeJson(contentObj["parts"], false);
            List<Dictionary<string, object>>? parts = _Serializer.DeserializeJson<List<Dictionary<string, object>>>(partsJson);

            if (parts == null || parts.Count == 0) return null;

            string? completionText = parts[0].ContainsKey("text") ? parts[0]["text"]?.ToString() : null;
            return string.IsNullOrWhiteSpace(completionText) ? null : completionText;
        }

        #endregion
    }
}
