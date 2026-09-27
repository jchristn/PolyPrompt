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
    /// Client for the Cohere API. Chat, tool chat, and text generation use the v2 Chat API
    /// (<c>/v2/chat</c>, text generation is mapped onto a single-turn chat because Cohere retired its legacy
    /// generate endpoint); embeddings use <c>/v2/embed</c>; reranking uses <c>/v2/rerank</c>; classification
    /// uses <c>/v1/classify</c>; and model listing and lookup use <c>/v1/models</c>. Chat, embeddings, reranking,
    /// and classification each have their own default model (<see cref="CompletionClientBase.Model"/>,
    /// <see cref="EmbeddingModel"/>, <see cref="RerankModel"/>, <see cref="ClassificationModel"/>) because
    /// Cohere serves each operation from a different model family. Model pull and delete throw
    /// <see cref="NotSupportedException"/>.
    /// </summary>
    public class CohereClient : CompletionClientBase
    {
        #region Private-Members

        // Cohere v2 streaming event types.
        private const string MessageStartEvent = "message-start";
        private const string ContentDeltaEvent = "content-delta";
        private const string ToolPlanDeltaEvent = "tool-plan-delta";
        private const string ToolCallStartEvent = "tool-call-start";
        private const string ToolCallDeltaEvent = "tool-call-delta";
        private const string MessageEndEvent = "message-end";
        private const string TextContentType = "text";
        private const string ThinkingContentType = "thinking";

        private string _EmbeddingModel = "embed-v4.0";
        private string _RerankModel = "rerank-v3.5";
        private string? _ClassificationModel = null;
        private int _ModelsPageSize = 1000;

        #endregion

        #region Public-Members

        /// <summary>
        /// Model used by EmbedAsync when the request does not override it. Default: embed-v4.0. Cannot be
        /// null, empty, or whitespace.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null, empty, or whitespace.</exception>
        public string EmbeddingModel
        {
            get { return _EmbeddingModel; }
            set
            {
                if (string.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(EmbeddingModel));
                _EmbeddingModel = value;
            }
        }

        /// <summary>
        /// Model used by RerankAsync when the request does not override it. Default: rerank-v3.5. Cannot be
        /// null, empty, or whitespace.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null, empty, or whitespace.</exception>
        public string RerankModel
        {
            get { return _RerankModel; }
            set
            {
                if (string.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(RerankModel));
                _RerankModel = value;
            }
        }

        /// <summary>
        /// Model used by ClassifyAsync when the request does not override it, typically a fine-tuned
        /// classification model. Default: null, which omits the model field so Cohere uses its default model
        /// with the request's few-shot examples. Whitespace is treated as null.
        /// </summary>
        public string? ClassificationModel
        {
            get { return _ClassificationModel; }
            set { _ClassificationModel = string.IsNullOrWhiteSpace(value) ? null : value; }
        }

        /// <summary>
        /// Page size requested from the models list endpoint. Clamped to 1..1,000. Default: 1,000.
        /// ListModelsAsync follows pagination until no page token is returned, so this affects request count,
        /// not result completeness.
        /// </summary>
        public int ModelsPageSize
        {
            get { return _ModelsPageSize; }
            set { _ModelsPageSize = Math.Clamp(value, 1, 1000); }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new CohereClient.
        /// </summary>
        /// <param name="endpoint">Cohere API endpoint URL. Default: https://api.cohere.com.</param>
        /// <param name="apiKey">Cohere API key (required); when non-empty an Authorization: Bearer header is added. Default: null.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        /// <param name="httpClient">Optional HTTP client. When supplied, the caller owns and disposes it; use this to configure the transport (custom handler, TLS, proxy). Default: null (an internally owned client is created).</param>
        public CohereClient(
            string endpoint = "https://api.cohere.com",
            string? apiKey = null,
            LoggingModule? logging = null,
            HttpClient? httpClient = null)
            : base(endpoint, apiKey, logging ?? new LoggingModule(), httpClient)
        {
            _Header = "[Cohere] ";
            Model = "command-a-03-2025";

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

            CohereChatCompletionOptions? cohereOptions = options as CohereChatCompletionOptions;
            Dictionary<string, object> requestBody = BuildSinglePromptRequestBody(
                Model, prompt, systemPrompt, maxTokens, temperature, topP,
                cohereOptions?.TopK, cohereOptions?.Seed, cohereOptions?.FrequencyPenalty,
                cohereOptions?.PresencePenalty, cohereOptions?.StopSequences, false);
            ApplyReasoning(requestBody, options?.ReasoningEffort);

            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                CompletionHttpResult result = await PostJsonAsync(BuildChatUrl(), requestBody, token).ConfigureAwait(false);
                chatResponse.StatusCode = result.StatusCode;

                if (!result.IsSuccessStatusCode)
                {
                    _Logging.Warn(_Header + "chat request failed with status " + result.StatusCode + ": " + result.ResponseBody);
                    chatResponse.Success = false;
                    chatResponse.Error = "HTTP " + result.StatusCode + ": " + result.ResponseBody;
                    return chatResponse;
                }

                CohereChatParse parsed = ParseChatResponse(result.ResponseBody);
                if (parsed.Error != null)
                {
                    chatResponse.Success = false;
                    chatResponse.Error = parsed.Error;
                    return chatResponse;
                }

                chatResponse.Text = parsed.Text;
                chatResponse.Reasoning = NormalizeReasoning(parsed.Reasoning);
                chatResponse.Usage = parsed.Usage;
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

            CohereChatCompletionOptions? cohereOptions = options as CohereChatCompletionOptions;
            Dictionary<string, object> requestBody = BuildSinglePromptRequestBody(
                Model, prompt, systemPrompt, maxTokens, temperature, topP,
                cohereOptions?.TopK, cohereOptions?.Seed, cohereOptions?.FrequencyPenalty,
                cohereOptions?.PresencePenalty, cohereOptions?.StopSequences, true);
            ApplyReasoning(requestBody, options?.ReasoningEffort);

            Stopwatch sw = Stopwatch.StartNew();

            ChatStreamingResponse streamingResponse = new ChatStreamingResponse();
            streamingResponse.Model = Model;

            try
            {
                StreamingHttpResult streamingResult = await PostStreamingJsonAsync(BuildChatUrl(), requestBody, token).ConfigureAwait(false);
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
                streamingResponse.Chunks = WrapChunksWithTiming(streamingResponse, ReadChatChunks(response, streamingResult.Token), sw, streamingResult.Token, streamingResult);
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
        /// Send a tool-capable chat request to Cohere's v2 Chat API. Cohere's tool_choice accepts only
        /// "REQUIRED" and "NONE": "auto" (or null) omits the field, "required"/"any" sends "REQUIRED", "none"
        /// sends "NONE", and a specific tool name sends only that tool with "REQUIRED" so the model must call
        /// it. The model's tool plan and thinking are surfaced as <see cref="ToolChatResponse.Reasoning"/> and
        /// are never sent back on follow-up turns.
        /// </summary>
        /// <param name="request">Tool chat request containing messages and tool definitions.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A ToolChatResponse containing assistant text and requested tool calls.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="ArgumentException">Thrown when request.Messages is empty.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the token is cancelled.</exception>
        public override async Task<ToolChatResponse> ToolChatAsync(
            ToolChatRequest request,
            CancellationToken token = default)
        {
            ResolveToolChatRequest(request, out string model, out int maxTokens, out double? temperature, out double? topP, out ReasoningEffort? reasoningEffort);

            ToolChatResponse toolResponse = new ToolChatResponse();
            toolResponse.Model = model;

            Dictionary<string, object> requestBody = BuildToolChatRequestBody(request, model, maxTokens, temperature, topP, reasoningEffort, false);

            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                CompletionHttpResult result = await PostJsonAsync(BuildChatUrl(), requestBody, token).ConfigureAwait(false);
                toolResponse.StatusCode = result.StatusCode;

                if (!result.IsSuccessStatusCode)
                {
                    _Logging.Warn(_Header + "tool chat request failed with status " + result.StatusCode + ": " + result.ResponseBody);
                    toolResponse.Success = false;
                    toolResponse.Error = "HTTP " + result.StatusCode + ": " + result.ResponseBody;
                    return toolResponse;
                }

                CohereChatParse parsed = ParseChatResponse(result.ResponseBody);
                if (parsed.Error != null)
                {
                    toolResponse.Success = false;
                    toolResponse.Error = parsed.Error;
                    return toolResponse;
                }

                toolResponse.Text = parsed.Text;
                toolResponse.Reasoning = NormalizeReasoning(parsed.Reasoning);
                toolResponse.ToolCalls = parsed.ToolCalls;
                toolResponse.ResponseId = parsed.ResponseId;
                toolResponse.FinishReason = parsed.FinishReason;
                toolResponse.Usage = parsed.Usage;
                toolResponse.Success = true;
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

        /// <summary>
        /// Streaming variant of <see cref="ToolChatAsync"/>. Tool-call arguments arriving across multiple
        /// tool-call-delta events are reassembled per call index, and tool-plan deltas are surfaced as
        /// reasoning text rather than answer text.
        /// </summary>
        /// <param name="request">Tool chat request containing messages and tool definitions.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A ToolChatStreamingResponse containing streamed chunks, accumulated text, and tool calls.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="ArgumentException">Thrown when request.Messages is empty.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the token is cancelled.</exception>
        public override async Task<ToolChatStreamingResponse> ToolChatStreamingAsync(
            ToolChatRequest request,
            CancellationToken token = default)
        {
            ResolveToolChatRequest(request, out string model, out int maxTokens, out double? temperature, out double? topP, out ReasoningEffort? reasoningEffort);

            Dictionary<string, object> requestBody = BuildToolChatRequestBody(request, model, maxTokens, temperature, topP, reasoningEffort, true);

            Stopwatch sw = Stopwatch.StartNew();

            ToolChatStreamingResponse streamingResponse = new ToolChatStreamingResponse();
            streamingResponse.Model = model;

            try
            {
                StreamingHttpResult streamingResult = await PostStreamingJsonAsync(BuildChatUrl(), requestBody, token).ConfigureAwait(false);
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
                streamingResponse.Chunks = WrapToolChatChunksWithTiming(streamingResponse, ReadToolChatChunks(response, streamingResult.Token), sw, streamingResult.Token, streamingResult);
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

        /// <summary>
        /// Generate embeddings through <c>POST /v2/embed</c>, using <see cref="EmbeddingModel"/> unless the
        /// request overrides the model. Cohere requires input_type for v3 and later models; when
        /// <see cref="CohereEmbeddingOptions.InputType"/> is not set, "search_document" is sent.
        /// </summary>
        /// <param name="inputs">The texts to embed (at most 96 per request).</param>
        /// <param name="options">Optional per-call overrides; pass <see cref="CohereEmbeddingOptions"/> for Cohere-specific fields.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An EmbeddingResponse with one vector per input.</returns>
        /// <exception cref="ArgumentNullException">Thrown when inputs is null.</exception>
        /// <exception cref="ArgumentException">Thrown when inputs is empty or contains a null element.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the token is cancelled.</exception>
        public override async Task<EmbeddingResponse> EmbedAsync(
            List<string> inputs,
            EmbeddingOptions? options = null,
            CancellationToken token = default)
        {
            ValidateInputList(inputs, nameof(inputs), "Embedding requests require at least one input.");

            EmbeddingResponse embedResponse = new EmbeddingResponse();
            string model = options?.Model ?? _EmbeddingModel;
            embedResponse.Model = model;

            CohereEmbeddingOptions? cohereOptions = options as CohereEmbeddingOptions;
            string embeddingType = cohereOptions?.EmbeddingType ?? "float";

            Dictionary<string, object> requestBody = new Dictionary<string, object>
            {
                { "model", model },
                { "texts", inputs },
                { "input_type", cohereOptions?.InputType ?? "search_document" },
                { "embedding_types", new List<string> { embeddingType } }
            };

            if (cohereOptions?.OutputDimension != null) requestBody["output_dimension"] = cohereOptions.OutputDimension.Value;
            if (cohereOptions?.Truncate != null) requestBody["truncate"] = cohereOptions.Truncate;

            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                CompletionHttpResult result = await PostJsonAsync(BuildUrl("/v2/embed"), requestBody, token).ConfigureAwait(false);
                embedResponse.StatusCode = result.StatusCode;

                if (!result.IsSuccessStatusCode)
                {
                    _Logging.Warn(_Header + "embed request failed with status " + result.StatusCode + ": " + result.ResponseBody);
                    embedResponse.Success = false;
                    embedResponse.Error = "HTTP " + result.StatusCode + ": " + result.ResponseBody;
                    return embedResponse;
                }

                Dictionary<string, object>? responseObj = _Serializer.DeserializeJson<Dictionary<string, object>>(result.ResponseBody);
                Dictionary<string, object>? embeddings = responseObj != null ? ParseNestedObject(responseObj, "embeddings") : null;
                if (embeddings == null || !embeddings.ContainsKey(embeddingType) || embeddings[embeddingType] == null)
                {
                    embedResponse.Success = false;
                    embedResponse.Error = "Response missing 'embeddings." + embeddingType + "' field";
                    return embedResponse;
                }

                List<object>? vectors = _Serializer.DeserializeJson<List<object>>(_Serializer.SerializeJson(embeddings[embeddingType], false));
                if (vectors != null)
                {
                    for (int i = 0; i < vectors.Count; i++)
                    {
                        EmbeddingResult embResult = new EmbeddingResult();
                        embResult.Index = i;
                        embResult.Embedding = ParseFloatArray(_Serializer.SerializeJson(vectors[i], false));
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

        /// <summary>
        /// Score documents against a query through <c>POST /v2/rerank</c>, using <see cref="RerankModel"/>
        /// unless the request overrides the model. Relevance scores are normalized to 0..1.
        /// </summary>
        /// <param name="query">The query. Cannot be null, empty, or whitespace.</param>
        /// <param name="documents">The documents to score. Cannot be null or empty, and no element can be null.</param>
        /// <param name="options">Optional per-call overrides; pass <see cref="CohereRerankOptions"/> for Cohere-specific fields.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A RerankResponse whose Results are sorted by score, highest first.</returns>
        /// <exception cref="ArgumentNullException">Thrown when query or documents is null.</exception>
        /// <exception cref="ArgumentException">Thrown when query is empty or whitespace, documents is empty, or a document is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when TopN exceeds the number of documents.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the token is cancelled.</exception>
        public override async Task<RerankResponse> RerankAsync(
            string query,
            List<string> documents,
            RerankOptions? options = null,
            CancellationToken token = default)
        {
            ValidateRerankArguments(query, documents, options);

            RerankResponse rerankResponse = new RerankResponse();
            string model = options?.Model ?? _RerankModel;
            rerankResponse.Model = model;

            Dictionary<string, object> requestBody = new Dictionary<string, object>
            {
                { "model", model },
                { "query", query },
                { "documents", documents }
            };

            if (options?.TopN != null) requestBody["top_n"] = options.TopN.Value;

            CohereRerankOptions? cohereOptions = options as CohereRerankOptions;
            if (cohereOptions?.MaxTokensPerDoc != null) requestBody["max_tokens_per_doc"] = cohereOptions.MaxTokensPerDoc.Value;

            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                CompletionHttpResult result = await PostJsonAsync(BuildUrl("/v2/rerank"), requestBody, token).ConfigureAwait(false);
                rerankResponse.StatusCode = result.StatusCode;

                if (!result.IsSuccessStatusCode)
                {
                    _Logging.Warn(_Header + "rerank request failed with status " + result.StatusCode + ": " + result.ResponseBody);
                    rerankResponse.Success = false;
                    rerankResponse.Error = "HTTP " + result.StatusCode + ": " + result.ResponseBody;
                    return rerankResponse;
                }

                Dictionary<string, object>? responseObj = _Serializer.DeserializeJson<Dictionary<string, object>>(result.ResponseBody);
                if (responseObj == null || !responseObj.ContainsKey("results"))
                {
                    rerankResponse.Success = false;
                    rerankResponse.Error = "Response missing 'results' field";
                    return rerankResponse;
                }

                rerankResponse.ResponseId = responseObj.ContainsKey("id") ? responseObj["id"]?.ToString() : null;

                List<Dictionary<string, object>>? results = _Serializer.DeserializeJson<List<Dictionary<string, object>>>(
                    _Serializer.SerializeJson(responseObj["results"], false));
                if (results != null)
                {
                    foreach (Dictionary<string, object> item in results)
                    {
                        int? index = TryGetInt(item, "index");
                        double? score = TryGetDouble(item, "relevance_score");
                        if (!index.HasValue || !score.HasValue) continue;
                        rerankResponse.Results.Add(new RerankResult { Index = index.Value, Score = score.Value });
                    }
                }

                Dictionary<string, object>? meta = ParseNestedObject(responseObj, "meta");
                if (meta != null)
                {
                    Dictionary<string, object>? billed = ParseNestedObject(meta, "billed_units");
                    if (billed != null) rerankResponse.SearchUnits = TryGetRoundedInt(billed, "search_units");

                    Dictionary<string, object>? tokens = ParseNestedObject(meta, "tokens");
                    if (tokens != null) rerankResponse.TotalTokens = TryGetRoundedInt(tokens, "input_tokens");
                }

                FinalizeRerankResults(rerankResponse, documents, options);
                rerankResponse.Success = true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                rerankResponse.Success = false;
                rerankResponse.Error = ex.Message;
            }
            finally
            {
                sw.Stop();
                rerankResponse.OverallRuntimeMs = sw.ElapsedMilliseconds;
            }

            return rerankResponse;
        }

        /// <summary>
        /// Classify inputs through <c>POST /v1/classify</c>, using <see cref="ClassificationModel"/> unless the
        /// request overrides the model. Without a fine-tuned model, pass labeled few-shot examples through
        /// <see cref="CohereClassificationOptions.Examples"/> (at least 2 per label).
        /// </summary>
        /// <param name="inputs">The texts to classify (at most 96 per request). Cannot be null or empty, and no element can be null.</param>
        /// <param name="options">Optional per-call overrides; pass <see cref="CohereClassificationOptions"/> for examples and truncation.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A ClassificationResponse with one result per input, labels sorted by confidence.</returns>
        /// <exception cref="ArgumentNullException">Thrown when inputs is null.</exception>
        /// <exception cref="ArgumentException">Thrown when inputs is empty or contains a null element.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the token is cancelled.</exception>
        public override async Task<ClassificationResponse> ClassifyAsync(
            List<string> inputs,
            ClassificationOptions? options = null,
            CancellationToken token = default)
        {
            ValidateInputList(inputs, nameof(inputs), "Classification requests require at least one input.");

            ClassificationResponse classifyResponse = new ClassificationResponse();
            string? model = options?.Model ?? _ClassificationModel;
            classifyResponse.Model = model;

            Dictionary<string, object> requestBody = new Dictionary<string, object>
            {
                { "inputs", inputs }
            };

            if (model != null) requestBody["model"] = model;

            CohereClassificationOptions? cohereOptions = options as CohereClassificationOptions;
            if (cohereOptions != null)
            {
                if (cohereOptions.Examples.Count > 0)
                {
                    requestBody["examples"] = cohereOptions.Examples
                        .Select(e => new Dictionary<string, object> { { "text", e.Text }, { "label", e.Label } })
                        .ToList();
                }

                if (cohereOptions.Truncate != null) requestBody["truncate"] = cohereOptions.Truncate;
            }

            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                CompletionHttpResult result = await PostJsonAsync(BuildUrl("/v1/classify"), requestBody, token).ConfigureAwait(false);
                classifyResponse.StatusCode = result.StatusCode;

                if (!result.IsSuccessStatusCode)
                {
                    _Logging.Warn(_Header + "classify request failed with status " + result.StatusCode + ": " + result.ResponseBody);
                    classifyResponse.Success = false;
                    classifyResponse.Error = "HTTP " + result.StatusCode + ": " + result.ResponseBody;
                    return classifyResponse;
                }

                Dictionary<string, object>? responseObj = _Serializer.DeserializeJson<Dictionary<string, object>>(result.ResponseBody);
                if (responseObj == null || !responseObj.ContainsKey("classifications"))
                {
                    classifyResponse.Success = false;
                    classifyResponse.Error = "Response missing 'classifications' field";
                    return classifyResponse;
                }

                List<Dictionary<string, object>>? classifications = _Serializer.DeserializeJson<List<Dictionary<string, object>>>(
                    _Serializer.SerializeJson(responseObj["classifications"], false));
                if (classifications != null)
                {
                    for (int i = 0; i < classifications.Count; i++)
                    {
                        classifyResponse.Classifications.Add(ParseClassification(classifications[i], i, inputs));
                    }
                }

                classifyResponse.Success = true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                classifyResponse.Success = false;
                classifyResponse.Error = ex.Message;
            }
            finally
            {
                sw.Stop();
                classifyResponse.OverallRuntimeMs = sw.ElapsedMilliseconds;
            }

            return classifyResponse;
        }

        /// <summary>
        /// Generate text from a prompt. Cohere retired its legacy generate endpoint, so this is sent as a
        /// single-user-turn <c>/v2/chat</c> request.
        /// </summary>
        /// <param name="prompt">The prompt text.</param>
        /// <param name="options">Optional per-call overrides; pass <see cref="CohereGenerationOptions"/> for Cohere-specific fields.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A GenerationResponse containing the generated text.</returns>
        /// <exception cref="OperationCanceledException">Thrown when the token is cancelled.</exception>
        public override async Task<GenerationResponse> GenerateAsync(
            string prompt,
            GenerationOptions? options = null,
            CancellationToken token = default)
        {
            ResolveGenerationOptions(options, out string model, out int maxTokens, out double? temperature, out double? topP);

            GenerationResponse genResponse = new GenerationResponse();
            genResponse.Model = model;

            CohereGenerationOptions? cohereOptions = options as CohereGenerationOptions;
            Dictionary<string, object> requestBody = BuildSinglePromptRequestBody(
                model, prompt, null, maxTokens, temperature, topP,
                cohereOptions?.TopK, cohereOptions?.Seed, cohereOptions?.FrequencyPenalty,
                cohereOptions?.PresencePenalty, cohereOptions?.StopSequences, false);

            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                CompletionHttpResult result = await PostJsonAsync(BuildChatUrl(), requestBody, token).ConfigureAwait(false);
                genResponse.StatusCode = result.StatusCode;

                if (!result.IsSuccessStatusCode)
                {
                    _Logging.Warn(_Header + "generate request failed with status " + result.StatusCode + ": " + result.ResponseBody);
                    genResponse.Success = false;
                    genResponse.Error = "HTTP " + result.StatusCode + ": " + result.ResponseBody;
                    return genResponse;
                }

                CohereChatParse parsed = ParseChatResponse(result.ResponseBody);
                if (parsed.Error != null)
                {
                    genResponse.Success = false;
                    genResponse.Error = parsed.Error;
                    return genResponse;
                }

                genResponse.Text = parsed.Text;
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

        /// <summary>
        /// Streaming variant of <see cref="GenerateAsync"/>, sent as a single-user-turn streaming
        /// <c>/v2/chat</c> request. Only answer text is streamed; thinking is omitted.
        /// </summary>
        /// <param name="prompt">The prompt text.</param>
        /// <param name="options">Optional per-call overrides; pass <see cref="CohereGenerationOptions"/> for Cohere-specific fields.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A GenerationStreamingResponse containing an async enumerable of chunks.</returns>
        /// <exception cref="OperationCanceledException">Thrown when the token is cancelled.</exception>
        public override async Task<GenerationStreamingResponse> GenerateStreamingAsync(
            string prompt,
            GenerationOptions? options = null,
            CancellationToken token = default)
        {
            ResolveGenerationOptions(options, out string model, out int maxTokens, out double? temperature, out double? topP);

            CohereGenerationOptions? cohereOptions = options as CohereGenerationOptions;
            Dictionary<string, object> requestBody = BuildSinglePromptRequestBody(
                model, prompt, null, maxTokens, temperature, topP,
                cohereOptions?.TopK, cohereOptions?.Seed, cohereOptions?.FrequencyPenalty,
                cohereOptions?.PresencePenalty, cohereOptions?.StopSequences, true);

            Stopwatch sw = Stopwatch.StartNew();

            GenerationStreamingResponse streamingResponse = new GenerationStreamingResponse();
            streamingResponse.Model = model;

            try
            {
                StreamingHttpResult streamingResult = await PostStreamingJsonAsync(BuildChatUrl(), requestBody, token).ConfigureAwait(false);
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
                streamingResponse.Chunks = WrapGenerationChunksWithTiming(streamingResponse, ReadGenerationChunks(response, streamingResult.Token), sw, streamingResult.Token, streamingResult);
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
        /// List models through <c>GET /v1/models</c>, following <c>next_page_token</c> pagination. Each model's
        /// Metadata carries <c>endpoints</c> (comma-separated, for example "chat,embed"), <c>finetuned</c>, and
        /// <c>features</c> when reported, and InputTokenLimit carries the context length.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An async enumerable of ModelInformation objects.</returns>
        /// <exception cref="OperationCanceledException">Thrown when the token is cancelled.</exception>
        public override async IAsyncEnumerable<ModelInformation> ListModelsAsync(
            [EnumeratorCancellation] CancellationToken token = default)
        {
            string? pageToken = null;
            HashSet<string> seenTokens = new HashSet<string>(StringComparer.Ordinal);

            while (true)
            {
                token.ThrowIfCancellationRequested();

                string url = BuildUrl("/v1/models?page_size=" + _ModelsPageSize);
                if (!string.IsNullOrEmpty(pageToken)) url += "&page_token=" + Uri.EscapeDataString(pageToken);

                _Logging.Debug(_Header + "GET " + url);

                CompletionHttpResult result = await GetAndRecordAsync(url, token).ConfigureAwait(false);
                if (!result.IsSuccessStatusCode)
                {
                    _Logging.Warn(_Header + "list models failed with status " + result.StatusCode);
                    yield break;
                }

                Dictionary<string, object>? responseObj = _Serializer.DeserializeJson<Dictionary<string, object>>(result.ResponseBody);
                if (responseObj == null || !responseObj.ContainsKey("models")) yield break;

                List<Dictionary<string, object>>? models = _Serializer.DeserializeJson<List<Dictionary<string, object>>>(
                    _Serializer.SerializeJson(responseObj["models"], false));
                if (models == null) yield break;

                foreach (Dictionary<string, object> modelObj in models)
                {
                    token.ThrowIfCancellationRequested();
                    yield return ParseModelInformation(modelObj, string.Empty);
                }

                pageToken = responseObj.ContainsKey("next_page_token") ? responseObj["next_page_token"]?.ToString() : null;

                // Stop when there is no next page, or defensively when a token repeats.
                if (string.IsNullOrEmpty(pageToken) || !seenTokens.Add(pageToken)) yield break;
            }
        }

        /// <summary>
        /// Validate connectivity and credentials by requesting a single-entry page from <c>GET /v1/models</c>.
        /// Returns false on any non-success status (for example an invalid API key), an unreachable server,
        /// or a request that exceeds <see cref="CompletionClientBase.TimeoutMs"/>; only cancellation of
        /// <paramref name="token"/> propagates.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the models endpoint answered successfully, false otherwise.</returns>
        /// <exception cref="OperationCanceledException">Thrown when the token is cancelled.</exception>
        public override async Task<bool> ValidateConnectivityAsync(CancellationToken token = default)
        {
            try
            {
                CompletionHttpResult result = await GetAndRecordAsync(BuildUrl("/v1/models?page_size=1"), token).ConfigureAwait(false);
                return result.IsSuccessStatusCode;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return false;
            }
        }

        /// <inheritdoc />
        public override async Task<ModelInformation?> GetModelInformationAsync(string model, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(model))
                throw new ArgumentNullException(nameof(model));

            string url = BuildUrl("/v1/models/" + Uri.EscapeDataString(model));
            _Logging.Debug(_Header + "GET " + url);

            try
            {
                CompletionHttpResult result = await GetAndRecordAsync(url, token).ConfigureAwait(false);
                if (!result.IsSuccessStatusCode)
                {
                    _Logging.Warn(_Header + "get model failed with status " + result.StatusCode + ": " + result.ResponseBody);
                    return null;
                }

                Dictionary<string, object>? responseObj = _Serializer.DeserializeJson<Dictionary<string, object>>(result.ResponseBody);
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

        private string BuildUrl(string pathAndQuery)
        {
            return _Endpoint.TrimEnd('/') + pathAndQuery;
        }

        private string BuildChatUrl()
        {
            return BuildUrl("/v2/chat");
        }

        private async Task<CompletionHttpResult> PostJsonAsync(string url, Dictionary<string, object> body, CancellationToken token)
        {
            string json = _Serializer.SerializeJson(body, false);
            StringContent content = new StringContent(json, Encoding.UTF8, "application/json");
            _Logging.Debug(_Header + "POST " + url);
            return await PostAndRecordAsync(url, content, json, token).ConfigureAwait(false);
        }

        private async Task<StreamingHttpResult> PostStreamingJsonAsync(string url, Dictionary<string, object> body, CancellationToken token)
        {
            string json = _Serializer.SerializeJson(body, false);
            StringContent content = new StringContent(json, Encoding.UTF8, "application/json");
            _Logging.Debug(_Header + "POST (streaming) " + url);
            return await PostStreamingAsync(url, content, token).ConfigureAwait(false);
        }

        private Dictionary<string, object> BuildSinglePromptRequestBody(
            string model, string prompt, string? systemPrompt, int maxTokens,
            double? temperature, double? topP, int? topK, int? seed,
            double? frequencyPenalty, double? presencePenalty, List<string>? stopSequences, bool stream)
        {
            List<Dictionary<string, object>> messages = new List<Dictionary<string, object>>();
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                messages.Add(new Dictionary<string, object> { { "role", "system" }, { "content", systemPrompt } });
            }
            messages.Add(new Dictionary<string, object> { { "role", "user" }, { "content", prompt ?? string.Empty } });

            Dictionary<string, object> requestBody = new Dictionary<string, object>
            {
                { "model", model },
                { "messages", messages },
                { "max_tokens", maxTokens },
                { "stream", stream }
            };

            ApplySampling(requestBody, temperature, topP);
            if (topK.HasValue) requestBody["k"] = topK.Value;
            if (seed.HasValue) requestBody["seed"] = seed.Value;
            if (frequencyPenalty.HasValue) requestBody["frequency_penalty"] = frequencyPenalty.Value;
            if (presencePenalty.HasValue) requestBody["presence_penalty"] = presencePenalty.Value;
            if (stopSequences != null && stopSequences.Count > 0) requestBody["stop_sequences"] = stopSequences;

            return requestBody;
        }

        private static void ApplySampling(Dictionary<string, object> requestBody, double? temperature, double? topP)
        {
            if (temperature.HasValue) requestBody["temperature"] = temperature.Value;

            // Cohere's p must be within 0.01..0.99; the base TopP range is 0.0..1.0.
            if (topP.HasValue) requestBody["p"] = Math.Clamp(topP.Value, 0.01, 0.99);
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
                { "messages", BuildCohereMessages(request.Messages) },
                { "max_tokens", maxTokens },
                { "stream", stream }
            };

            ApplySampling(requestBody, temperature, topP);

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
                    requestBody["tool_choice"] = "NONE";
                }
                else if (string.Equals(toolChoice, "required", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(toolChoice, "any", StringComparison.OrdinalIgnoreCase))
                {
                    requestBody["tool_choice"] = "REQUIRED";
                }
                else
                {
                    // Cohere cannot name a tool in tool_choice, so a forced tool is expressed by sending only
                    // that tool and requiring a call.
                    List<ToolDefinition> named = tools
                        .Where(t => string.Equals(t.Name, toolChoice, StringComparison.Ordinal))
                        .ToList();
                    if (named.Count > 0) tools = named;
                    requestBody["tool_choice"] = "REQUIRED";
                }

                requestBody["tools"] = tools.Select(BuildCohereTool).ToList();
            }

            ApplyReasoning(requestBody, reasoningEffort);

            return requestBody;
        }

        /// <summary>
        /// Adds the Cohere <c>thinking</c> field for a reasoning effort (disabled at a budget of 0); null leaves the
        /// request unchanged.
        /// </summary>
        private static void ApplyReasoning(Dictionary<string, object> requestBody, ReasoningEffort? reasoningEffort)
        {
            if (reasoningEffort == null) return;
            int budget = reasoningEffort.ToCohereThinkingBudget();
            requestBody["thinking"] = budget > 0
                ? new Dictionary<string, object> { { "type", "enabled" }, { "token_budget", budget } }
                : new Dictionary<string, object> { { "type", "disabled" } };
        }

        private static Dictionary<string, object> BuildCohereTool(ToolDefinition tool)
        {
            return new Dictionary<string, object>
            {
                { "type", "function" },
                {
                    "function", new Dictionary<string, object>
                    {
                        { "name", tool.Name },
                        { "description", tool.Description },
                        { "parameters", tool.Parameters }
                    }
                }
            };
        }

        private static List<Dictionary<string, object>> BuildCohereMessages(List<ChatMessage> messages)
        {
            List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();
            int syntheticToolCallIndex = 0;

            foreach (ChatMessage message in messages)
            {
                string role = message.Role ?? "user";

                if (string.Equals(role, "tool", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(role, "function", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(new Dictionary<string, object>
                    {
                        { "role", "tool" },
                        { "tool_call_id", ResolveToolCallId(message) },
                        { "content", message.Content ?? string.Empty }
                    });
                    continue;
                }

                if (message.ToolCalls != null && message.ToolCalls.Count > 0)
                {
                    List<Dictionary<string, object>> toolCalls = new List<Dictionary<string, object>>();
                    foreach (ToolCall toolCall in message.ToolCalls)
                    {
                        string id = string.IsNullOrWhiteSpace(toolCall.Id)
                            ? "cohere-call-" + syntheticToolCallIndex
                            : toolCall.Id;
                        syntheticToolCallIndex++;

                        toolCalls.Add(new Dictionary<string, object>
                        {
                            { "id", id },
                            { "type", "function" },
                            {
                                "function", new Dictionary<string, object>
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

                result.Add(new Dictionary<string, object>
                {
                    { "role", NormalizeRole(role) },
                    { "content", message.Content ?? string.Empty }
                });
            }

            return result;
        }

        private static string NormalizeRole(string role)
        {
            if (string.Equals(role, "system", StringComparison.OrdinalIgnoreCase)) return "system";
            if (string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase)) return "assistant";
            if (string.Equals(role, "model", StringComparison.OrdinalIgnoreCase)) return "assistant";
            return "user";
        }

        private static string ResolveToolCallId(ChatMessage message)
        {
            if (!string.IsNullOrWhiteSpace(message.ToolCallId)) return message.ToolCallId;
            if (!string.IsNullOrWhiteSpace(message.ToolName)) return message.ToolName;
            return "tool";
        }

        private CohereChatParse ParseChatResponse(string responseBody)
        {
            CohereChatParse parsed = new CohereChatParse();

            Dictionary<string, object>? responseObj = _Serializer.DeserializeJson<Dictionary<string, object>>(responseBody);
            if (responseObj == null)
            {
                parsed.Error = "Response is not a JSON object";
                return parsed;
            }

            Dictionary<string, object>? message = ParseNestedObject(responseObj, "message");
            if (message == null)
            {
                parsed.Error = "Response missing 'message' field";
                return parsed;
            }

            parsed.ResponseId = responseObj.ContainsKey("id") ? responseObj["id"]?.ToString() : null;
            parsed.FinishReason = responseObj.ContainsKey("finish_reason") ? responseObj["finish_reason"]?.ToString() : null;
            parsed.Usage = ParseUsage(ParseNestedObject(responseObj, "usage"));

            StringBuilder text = new StringBuilder();
            StringBuilder reasoning = new StringBuilder();

            if (message.ContainsKey("tool_plan") && message["tool_plan"] != null)
            {
                reasoning.Append(message["tool_plan"]?.ToString());
            }

            if (message.ContainsKey("content") && message["content"] != null)
            {
                string contentJson = _Serializer.SerializeJson(message["content"], false);
                if (contentJson.TrimStart().StartsWith("[", StringComparison.Ordinal))
                {
                    List<Dictionary<string, object>>? blocks = _Serializer.DeserializeJson<List<Dictionary<string, object>>>(contentJson);
                    if (blocks != null)
                    {
                        foreach (Dictionary<string, object> block in blocks)
                        {
                            string? type = block.ContainsKey("type") ? block["type"]?.ToString() : null;
                            if (string.Equals(type, TextContentType, StringComparison.Ordinal) && block.ContainsKey("text"))
                            {
                                text.Append(block["text"]?.ToString());
                            }
                            else if (string.Equals(type, ThinkingContentType, StringComparison.Ordinal) && block.ContainsKey(ThinkingContentType))
                            {
                                reasoning.Append(block[ThinkingContentType]?.ToString());
                            }
                        }
                    }
                }
                else
                {
                    text.Append(message["content"]?.ToString());
                }
            }

            if (message.ContainsKey("tool_calls") && message["tool_calls"] != null)
            {
                List<Dictionary<string, object>>? calls = _Serializer.DeserializeJson<List<Dictionary<string, object>>>(
                    _Serializer.SerializeJson(message["tool_calls"], false));
                if (calls != null)
                {
                    foreach (Dictionary<string, object> call in calls)
                    {
                        Dictionary<string, object>? function = ParseNestedObject(call, "function");
                        if (function == null || !function.ContainsKey("name")) continue;

                        ToolCall toolCall = new ToolCall();
                        toolCall.Id = call.ContainsKey("id") ? call["id"]?.ToString() : null;
                        toolCall.Name = function["name"]?.ToString() ?? string.Empty;

                        string? arguments = function.ContainsKey("arguments") ? function["arguments"]?.ToString() : null;
                        toolCall.ArgumentsJson = string.IsNullOrWhiteSpace(arguments) ? "{}" : arguments;
                        parsed.ToolCalls.Add(toolCall);
                    }
                }
            }

            parsed.Text = text.Length > 0 ? text.ToString() : null;
            parsed.Reasoning = reasoning.Length > 0 ? reasoning.ToString() : null;
            return parsed;
        }

        private ChatStreamingUsage? ParseUsage(Dictionary<string, object>? usageObj)
        {
            if (usageObj == null) return null;

            Dictionary<string, object>? tokens = ParseNestedObject(usageObj, "tokens");
            Dictionary<string, object>? billed = ParseNestedObject(usageObj, "billed_units");

            ChatStreamingUsage usage = new ChatStreamingUsage();

            // Prefer the raw token counts (what the model processed); fall back to billed units.
            usage.PromptTokens = (tokens != null ? TryGetRoundedInt(tokens, "input_tokens") : null)
                ?? (billed != null ? TryGetRoundedInt(billed, "input_tokens") : null);
            usage.CompletionTokens = (tokens != null ? TryGetRoundedInt(tokens, "output_tokens") : null)
                ?? (billed != null ? TryGetRoundedInt(billed, "output_tokens") : null);
            usage.CachedPromptTokens = TryGetRoundedInt(usageObj, "cached_tokens");

            if (usage.PromptTokens.HasValue || usage.CompletionTokens.HasValue)
            {
                usage.TotalTokens = (usage.PromptTokens ?? 0) + (usage.CompletionTokens ?? 0);
            }

            return usage;
        }

        private ClassificationResult ParseClassification(Dictionary<string, object> classificationObj, int index, List<string> inputs)
        {
            ClassificationResult classification = new ClassificationResult();
            classification.Index = index;
            classification.Input = classificationObj.ContainsKey("input") && classificationObj["input"] != null
                ? classificationObj["input"]?.ToString() ?? string.Empty
                : (index < inputs.Count ? inputs[index] : string.Empty);

            Dictionary<string, object>? labels = ParseNestedObject(classificationObj, "labels");
            if (labels != null)
            {
                foreach (KeyValuePair<string, object> entry in labels)
                {
                    Dictionary<string, object>? labelObj = ParseNestedObject(labels, entry.Key);
                    double? confidence = labelObj != null ? TryGetDouble(labelObj, "confidence") : null;
                    if (!confidence.HasValue) continue;
                    classification.Labels.Add(new ClassificationLabel { Label = entry.Key, Score = confidence.Value });
                }
            }

            classification.Labels = classification.Labels.OrderByDescending(l => l.Score).ToList();

            ClassificationLabel? top = classification.Labels.FirstOrDefault();
            if (top != null)
            {
                classification.Label = top.Label;
                classification.Score = top.Score;
            }
            else
            {
                // Fall back to the deprecated single-label fields when no label map was returned.
                classification.Label = classificationObj.ContainsKey("prediction") ? classificationObj["prediction"]?.ToString() : null;
                classification.Score = TryGetDouble(classificationObj, "confidence");
            }

            return classification;
        }

        private ModelInformation ParseModelInformation(Dictionary<string, object> modelObj, string fallbackName)
        {
            ModelInformation info = new ModelInformation();
            info.Name = modelObj.ContainsKey("name") ? modelObj["name"]?.ToString() ?? fallbackName : fallbackName;
            info.OwnedBy = "cohere";
            info.InputTokenLimit = TryGetRoundedInt(modelObj, "context_length");

            string? endpoints = JoinStringArray(modelObj, "endpoints");
            if (endpoints != null) info.Metadata["endpoints"] = endpoints;

            string? features = JoinStringArray(modelObj, "features");
            if (features != null) info.Metadata["features"] = features;

            if (modelObj.ContainsKey("finetuned") && modelObj["finetuned"] != null)
            {
                info.Metadata["finetuned"] = IsTruthy(modelObj, "finetuned") ? "true" : "false";
            }

            return info;
        }

        private string? JoinStringArray(Dictionary<string, object> obj, string key)
        {
            if (!obj.ContainsKey(key) || obj[key] == null) return null;
            List<string>? values = _Serializer.DeserializeJson<List<string>>(_Serializer.SerializeJson(obj[key], false));
            if (values == null || values.Count == 0) return null;
            return string.Join(",", values);
        }

        private async IAsyncEnumerable<Dictionary<string, object>> ReadEvents(
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
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

                string data = line.Substring(5).Trim();
                if (data.Length == 0) continue;
                if (string.Equals(data, "[DONE]", StringComparison.Ordinal)) yield break;

                Dictionary<string, object>? evt = _Serializer.DeserializeJson<Dictionary<string, object>>(data);
                if (evt == null || !evt.ContainsKey("type")) continue;

                yield return evt;

                if (string.Equals(evt["type"]?.ToString(), MessageEndEvent, StringComparison.Ordinal)) yield break;
            }
        }

        private Dictionary<string, object>? ParseDeltaMessage(Dictionary<string, object> evt)
        {
            Dictionary<string, object>? delta = ParseNestedObject(evt, "delta");
            return delta != null ? ParseNestedObject(delta, "message") : null;
        }

        private void ReadContentDelta(Dictionary<string, object> evt, out string? text, out string? thinking)
        {
            text = null;
            thinking = null;

            Dictionary<string, object>? message = ParseDeltaMessage(evt);
            Dictionary<string, object>? content = message != null ? ParseNestedObject(message, "content") : null;
            if (content == null) return;

            if (content.ContainsKey("text")) text = content["text"]?.ToString();
            if (content.ContainsKey(ThinkingContentType)) thinking = content[ThinkingContentType]?.ToString();
        }

        private string? ReadToolPlanDelta(Dictionary<string, object> evt)
        {
            Dictionary<string, object>? message = ParseDeltaMessage(evt);
            if (message == null || !message.ContainsKey("tool_plan")) return null;
            return message["tool_plan"]?.ToString();
        }

        private void ReadMessageEnd(Dictionary<string, object> evt, out string? finishReason, out ChatStreamingUsage? usage)
        {
            finishReason = null;
            usage = null;

            Dictionary<string, object>? delta = ParseNestedObject(evt, "delta");
            if (delta == null) return;

            finishReason = delta.ContainsKey("finish_reason") ? delta["finish_reason"]?.ToString() : null;
            usage = ParseUsage(ParseNestedObject(delta, "usage"));
        }

        private async IAsyncEnumerable<ChatStreamingChunk> ReadChatChunks(
            HttpResponseMessage response,
            [EnumeratorCancellation] CancellationToken token)
        {
            await foreach (Dictionary<string, object> evt in ReadEvents(response, token).ConfigureAwait(false))
            {
                string? eventType = evt["type"]?.ToString();
                ChatStreamingChunk chunk = new ChatStreamingChunk();
                chunk.CreatedUtc = DateTime.UtcNow;

                if (string.Equals(eventType, MessageStartEvent, StringComparison.Ordinal))
                {
                    chunk.ResponseId = evt.ContainsKey("id") ? evt["id"]?.ToString() : null;
                }
                else if (string.Equals(eventType, ContentDeltaEvent, StringComparison.Ordinal))
                {
                    ReadContentDelta(evt, out string? text, out string? thinking);
                    chunk.Text = text;
                    chunk.ReasoningText = thinking;
                }
                else if (string.Equals(eventType, ToolPlanDeltaEvent, StringComparison.Ordinal))
                {
                    chunk.ReasoningText = ReadToolPlanDelta(evt);
                }
                else if (string.Equals(eventType, MessageEndEvent, StringComparison.Ordinal))
                {
                    ReadMessageEnd(evt, out string? finishReason, out ChatStreamingUsage? usage);
                    chunk.FinishReason = finishReason;
                    chunk.Usage = usage;
                    chunk.Done = true;
                }
                else
                {
                    continue;
                }

                yield return chunk;
            }
        }

        private async IAsyncEnumerable<ToolChatStreamingChunk> ReadToolChatChunks(
            HttpResponseMessage response,
            [EnumeratorCancellation] CancellationToken token)
        {
            await foreach (Dictionary<string, object> evt in ReadEvents(response, token).ConfigureAwait(false))
            {
                string? eventType = evt["type"]?.ToString();
                ToolChatStreamingChunk chunk = new ToolChatStreamingChunk();
                chunk.CreatedUtc = DateTime.UtcNow;

                if (string.Equals(eventType, MessageStartEvent, StringComparison.Ordinal))
                {
                    chunk.ResponseId = evt.ContainsKey("id") ? evt["id"]?.ToString() : null;
                }
                else if (string.Equals(eventType, ContentDeltaEvent, StringComparison.Ordinal))
                {
                    ReadContentDelta(evt, out string? text, out string? thinking);
                    chunk.Text = text;
                    chunk.ReasoningText = thinking;
                }
                else if (string.Equals(eventType, ToolPlanDeltaEvent, StringComparison.Ordinal))
                {
                    chunk.ReasoningText = ReadToolPlanDelta(evt);
                }
                else if (string.Equals(eventType, ToolCallStartEvent, StringComparison.Ordinal)
                    || string.Equals(eventType, ToolCallDeltaEvent, StringComparison.Ordinal))
                {
                    ToolCallDelta? delta = ReadToolCallDelta(evt, string.Equals(eventType, ToolCallStartEvent, StringComparison.Ordinal));
                    if (delta == null) continue;
                    chunk.ToolCallDeltas.Add(delta);
                }
                else if (string.Equals(eventType, MessageEndEvent, StringComparison.Ordinal))
                {
                    ReadMessageEnd(evt, out string? finishReason, out ChatStreamingUsage? usage);
                    chunk.FinishReason = finishReason;
                    chunk.Usage = usage;
                    chunk.Done = true;
                }
                else
                {
                    continue;
                }

                yield return chunk;
            }
        }

        private ToolCallDelta? ReadToolCallDelta(Dictionary<string, object> evt, bool isStart)
        {
            Dictionary<string, object>? message = ParseDeltaMessage(evt);
            Dictionary<string, object>? toolCall = message != null ? ParseNestedObject(message, "tool_calls") : null;
            if (toolCall == null) return null;

            Dictionary<string, object>? function = ParseNestedObject(toolCall, "function");

            ToolCallDelta delta = new ToolCallDelta();
            delta.Index = TryGetInt(evt, "index") ?? 0;

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

        private async IAsyncEnumerable<GenerationStreamingChunk> ReadGenerationChunks(
            HttpResponseMessage response,
            [EnumeratorCancellation] CancellationToken token)
        {
            await foreach (Dictionary<string, object> evt in ReadEvents(response, token).ConfigureAwait(false))
            {
                string? eventType = evt["type"]?.ToString();

                if (string.Equals(eventType, ContentDeltaEvent, StringComparison.Ordinal))
                {
                    ReadContentDelta(evt, out string? text, out string? _);
                    if (string.IsNullOrEmpty(text)) continue;
                    yield return new GenerationStreamingChunk { Text = text };
                }
                else if (string.Equals(eventType, MessageEndEvent, StringComparison.Ordinal))
                {
                    yield return new GenerationStreamingChunk { Done = true };
                }
            }
        }

        private sealed class CohereChatParse
        {
            public string? Text { get; set; }
            public string? Reasoning { get; set; }
            public List<ToolCall> ToolCalls { get; set; } = new List<ToolCall>();
            public string? ResponseId { get; set; }
            public string? FinishReason { get; set; }
            public ChatStreamingUsage? Usage { get; set; }
            public string? Error { get; set; }
        }

        #endregion
    }
}
