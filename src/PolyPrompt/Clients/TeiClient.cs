namespace PolyPrompt.Clients
{
    using System.Diagnostics;
    using System.Runtime.CompilerServices;
    using System.Text;
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using SyslogLogging;

    /// <summary>
    /// Client for Hugging Face Text Embeddings Inference (TEI). A TEI server hosts exactly one model, whose
    /// type (embedding, reranker, or classifier) is fixed when the server starts and is reported by
    /// <c>GET /info</c>. Supported operations: dense embeddings (<c>/embed</c>), sparse embeddings
    /// (<c>/embed_sparse</c>), reranking (<c>/rerank</c>), classification (<c>/predict</c>), model listing and
    /// lookup (from <c>/info</c>), and connectivity validation (<c>/health</c>). Which of these succeed depends
    /// on the model the server hosts; calling an operation the model does not support returns an
    /// unsuccessful response carrying TEI's error (typically HTTP 424). Chat, tool chat, and text generation
    /// are served by Hugging Face Text Generation Inference, not TEI, and throw
    /// <see cref="NotSupportedException"/>. Because the server hosts a single model, the
    /// <see cref="CompletionClientBase.Model"/> property and per-request model overrides are informational
    /// only and are not sent on the wire.
    /// </summary>
    public class TeiClient : CompletionClientBase
    {
        #region Private-Members

        private const string UnsupportedChat = "Text Embeddings Inference does not support chat completions. Use Text Generation Inference through OpenAiClient for chat.";
        private const string UnsupportedToolChat = "Text Embeddings Inference does not support tool calling.";
        private const string UnsupportedGeneration = "Text Embeddings Inference does not support text generation.";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new TeiClient.
        /// </summary>
        /// <param name="endpoint">TEI server URL. Default: http://localhost:8080.</param>
        /// <param name="apiKey">Optional API key, required only when the server was started with --api-key. When non-empty an Authorization: Bearer header is added. Default: null.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        /// <param name="httpClient">Optional HTTP client. When supplied, the caller owns and disposes it; use this to configure the transport (custom handler, TLS, proxy). Default: null (an internally owned client is created).</param>
        public TeiClient(
            string endpoint = "http://localhost:8080",
            string? apiKey = null,
            LoggingModule? logging = null,
            HttpClient? httpClient = null)
            : base(endpoint, apiKey, logging ?? new LoggingModule(), httpClient)
        {
            _Header = "[TEI] ";
            Model = "tei";

            if (!string.IsNullOrEmpty(apiKey))
            {
                _HttpClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + apiKey);
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Not supported. Text Embeddings Inference has no chat API.
        /// </summary>
        /// <param name="prompt">User message.</param>
        /// <param name="options">Optional per-call overrides.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Never returns; always throws.</returns>
        /// <exception cref="NotSupportedException">Always thrown.</exception>
        public override Task<ChatResponse> ChatAsync(
            string prompt,
            ChatCompletionOptions? options = null,
            CancellationToken token = default)
        {
            throw new NotSupportedException(UnsupportedChat);
        }

        /// <summary>
        /// Not supported. Text Embeddings Inference has no chat API.
        /// </summary>
        /// <param name="prompt">User message.</param>
        /// <param name="options">Optional per-call overrides.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Never returns; always throws.</returns>
        /// <exception cref="NotSupportedException">Always thrown.</exception>
        public override Task<ChatStreamingResponse> ChatStreamingAsync(
            string prompt,
            ChatCompletionOptions? options = null,
            CancellationToken token = default)
        {
            throw new NotSupportedException(UnsupportedChat);
        }

        /// <summary>
        /// Not supported. Text Embeddings Inference has no tool calling API.
        /// </summary>
        /// <param name="request">Tool chat request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Never returns; always throws.</returns>
        /// <exception cref="NotSupportedException">Always thrown.</exception>
        public override Task<ToolChatResponse> ToolChatAsync(
            ToolChatRequest request,
            CancellationToken token = default)
        {
            throw new NotSupportedException(UnsupportedToolChat);
        }

        /// <summary>
        /// Not supported. Text Embeddings Inference has no tool calling API.
        /// </summary>
        /// <param name="request">Tool chat request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Never returns; always throws.</returns>
        /// <exception cref="NotSupportedException">Always thrown.</exception>
        public override Task<ToolChatStreamingResponse> ToolChatStreamingAsync(
            ToolChatRequest request,
            CancellationToken token = default)
        {
            throw new NotSupportedException(UnsupportedToolChat);
        }

        /// <summary>
        /// Not supported. Text Embeddings Inference has no text generation API.
        /// </summary>
        /// <param name="prompt">The prompt text.</param>
        /// <param name="options">Optional per-call overrides.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Never returns; always throws.</returns>
        /// <exception cref="NotSupportedException">Always thrown.</exception>
        public override Task<GenerationResponse> GenerateAsync(
            string prompt,
            GenerationOptions? options = null,
            CancellationToken token = default)
        {
            throw new NotSupportedException(UnsupportedGeneration);
        }

        /// <summary>
        /// Not supported. Text Embeddings Inference has no text generation API.
        /// </summary>
        /// <param name="prompt">The prompt text.</param>
        /// <param name="options">Optional per-call overrides.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Never returns; always throws.</returns>
        /// <exception cref="NotSupportedException">Always thrown.</exception>
        public override Task<GenerationStreamingResponse> GenerateStreamingAsync(
            string prompt,
            GenerationOptions? options = null,
            CancellationToken token = default)
        {
            throw new NotSupportedException(UnsupportedGeneration);
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
        /// Generate dense embeddings for a batch of inputs through <c>POST /embed</c>. The request fails with a
        /// TEI error (HTTP 413) when the batch exceeds the server's max_client_batch_size.
        /// </summary>
        /// <param name="inputs">The texts to embed.</param>
        /// <param name="options">Optional per-call overrides; pass <see cref="TeiEmbeddingOptions"/> for TEI-specific fields.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An EmbeddingResponse with one vector per input, indexed by input position.</returns>
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
            embedResponse.Model = options?.Model ?? Model;

            Dictionary<string, object> requestBody = new Dictionary<string, object>
            {
                { "inputs", inputs }
            };

            TeiEmbeddingOptions? teiOptions = options as TeiEmbeddingOptions;
            if (teiOptions != null)
            {
                if (teiOptions.Normalize.HasValue) requestBody["normalize"] = teiOptions.Normalize.Value;
                if (teiOptions.Truncate.HasValue) requestBody["truncate"] = teiOptions.Truncate.Value;
                if (teiOptions.TruncationDirection != null) requestBody["truncation_direction"] = teiOptions.TruncationDirection;
                if (teiOptions.PromptName != null) requestBody["prompt_name"] = teiOptions.PromptName;
                if (teiOptions.Dimensions.HasValue) requestBody["dimensions"] = teiOptions.Dimensions.Value;
            }

            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                CompletionHttpResult result = await PostJsonAsync("/embed", requestBody, token).ConfigureAwait(false);
                embedResponse.StatusCode = result.StatusCode;

                if (!result.IsSuccessStatusCode)
                {
                    _Logging.Warn(_Header + "embed request failed with status " + result.StatusCode + ": " + result.ResponseBody);
                    embedResponse.Success = false;
                    embedResponse.Error = "HTTP " + result.StatusCode + ": " + result.ResponseBody;
                    return embedResponse;
                }

                List<object>? vectors = _Serializer.DeserializeJson<List<object>>(result.ResponseBody);
                if (vectors == null)
                {
                    embedResponse.Success = false;
                    embedResponse.Error = "Response is not a JSON array of embeddings";
                    return embedResponse;
                }

                for (int i = 0; i < vectors.Count; i++)
                {
                    EmbeddingResult embResult = new EmbeddingResult();
                    embResult.Index = i;
                    embResult.Embedding = ParseFloatArray(_Serializer.SerializeJson(vectors[i], false));
                    embedResponse.Embeddings.Add(embResult);
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
        /// Score documents against a query through <c>POST /rerank</c>. Requires the server to host a reranker
        /// (cross-encoder) model; otherwise TEI returns HTTP 424 and the response is unsuccessful. TEI has no
        /// native top-N parameter, so every document is scored and the results are trimmed client-side.
        /// </summary>
        /// <param name="query">The query. Cannot be null, empty, or whitespace.</param>
        /// <param name="documents">The documents to score. Cannot be null or empty, and no element can be null.</param>
        /// <param name="options">Optional per-call overrides; pass <see cref="TeiRerankOptions"/> for TEI-specific fields.</param>
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
            rerankResponse.Model = options?.Model ?? Model;

            Dictionary<string, object> requestBody = new Dictionary<string, object>
            {
                { "query", query },
                { "texts", documents }
            };

            TeiRerankOptions? teiOptions = options as TeiRerankOptions;
            if (teiOptions != null)
            {
                if (teiOptions.RawScores.HasValue) requestBody["raw_scores"] = teiOptions.RawScores.Value;
                if (teiOptions.Truncate.HasValue) requestBody["truncate"] = teiOptions.Truncate.Value;
                if (teiOptions.TruncationDirection != null) requestBody["truncation_direction"] = teiOptions.TruncationDirection;
            }

            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                CompletionHttpResult result = await PostJsonAsync("/rerank", requestBody, token).ConfigureAwait(false);
                rerankResponse.StatusCode = result.StatusCode;

                if (!result.IsSuccessStatusCode)
                {
                    _Logging.Warn(_Header + "rerank request failed with status " + result.StatusCode + ": " + result.ResponseBody);
                    rerankResponse.Success = false;
                    rerankResponse.Error = "HTTP " + result.StatusCode + ": " + result.ResponseBody;
                    return rerankResponse;
                }

                List<Dictionary<string, object>>? ranks = _Serializer.DeserializeJson<List<Dictionary<string, object>>>(result.ResponseBody);
                if (ranks == null)
                {
                    rerankResponse.Success = false;
                    rerankResponse.Error = "Response is not a JSON array of ranks";
                    return rerankResponse;
                }

                foreach (Dictionary<string, object> rank in ranks)
                {
                    int? index = TryGetInt(rank, "index");
                    double? score = TryGetDouble(rank, "score");
                    if (!index.HasValue || !score.HasValue) continue;

                    rerankResponse.Results.Add(new RerankResult { Index = index.Value, Score = score.Value });
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
        /// Classify a batch of inputs through <c>POST /predict</c>. Requires the server to host a sequence
        /// classification model. Inputs are always sent in TEI's batch form (each input wrapped in its own
        /// array) so that two inputs are never mistaken for a single text pair.
        /// </summary>
        /// <param name="inputs">The texts to classify. Cannot be null or empty, and no element can be null.</param>
        /// <param name="options">Optional per-call overrides; pass <see cref="TeiClassificationOptions"/> for TEI-specific fields.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A ClassificationResponse with one result per input, labels sorted by score.</returns>
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
            classifyResponse.Model = options?.Model ?? Model;

            List<List<string>> batch = inputs.Select(i => new List<string> { i }).ToList();
            Dictionary<string, object> requestBody = new Dictionary<string, object>
            {
                { "inputs", batch }
            };

            TeiClassificationOptions? teiOptions = options as TeiClassificationOptions;
            if (teiOptions != null)
            {
                if (teiOptions.RawScores.HasValue) requestBody["raw_scores"] = teiOptions.RawScores.Value;
                if (teiOptions.Truncate.HasValue) requestBody["truncate"] = teiOptions.Truncate.Value;
                if (teiOptions.TruncationDirection != null) requestBody["truncation_direction"] = teiOptions.TruncationDirection;
            }

            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                CompletionHttpResult result = await PostJsonAsync("/predict", requestBody, token).ConfigureAwait(false);
                classifyResponse.StatusCode = result.StatusCode;

                if (!result.IsSuccessStatusCode)
                {
                    _Logging.Warn(_Header + "predict request failed with status " + result.StatusCode + ": " + result.ResponseBody);
                    classifyResponse.Success = false;
                    classifyResponse.Error = "HTTP " + result.StatusCode + ": " + result.ResponseBody;
                    return classifyResponse;
                }

                List<object>? outer = _Serializer.DeserializeJson<List<object>>(result.ResponseBody);
                if (outer == null)
                {
                    classifyResponse.Success = false;
                    classifyResponse.Error = "Response is not a JSON array of predictions";
                    return classifyResponse;
                }

                // A batch returns one prediction list per input. Tolerate a flat list (single input form).
                List<List<Dictionary<string, object>>> perInput = new List<List<Dictionary<string, object>>>();
                string outerJson = _Serializer.SerializeJson(outer, false);
                string firstJson = outer.Count > 0 ? _Serializer.SerializeJson(outer[0], false).TrimStart() : string.Empty;
                if (firstJson.StartsWith("{", StringComparison.Ordinal))
                {
                    perInput.Add(_Serializer.DeserializeJson<List<Dictionary<string, object>>>(outerJson) ?? new List<Dictionary<string, object>>());
                }
                else
                {
                    perInput = _Serializer.DeserializeJson<List<List<Dictionary<string, object>>>>(outerJson) ?? perInput;
                }

                for (int i = 0; i < perInput.Count; i++)
                {
                    ClassificationResult classification = new ClassificationResult();
                    classification.Index = i;
                    classification.Input = i < inputs.Count ? inputs[i] : string.Empty;

                    foreach (Dictionary<string, object> prediction in perInput[i])
                    {
                        string? label = prediction.ContainsKey("label") ? prediction["label"]?.ToString() : null;
                        double? score = TryGetDouble(prediction, "score");
                        if (label == null || !score.HasValue) continue;
                        classification.Labels.Add(new ClassificationLabel { Label = label, Score = score.Value });
                    }

                    FinalizeClassification(classification);
                    classifyResponse.Classifications.Add(classification);
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
        /// Generate sparse embeddings for a batch of inputs through <c>POST /embed_sparse</c>. Requires the
        /// server to host a SPLADE-style model.
        /// </summary>
        /// <param name="inputs">The texts to embed. Cannot be null or empty, and no element can be null.</param>
        /// <param name="options">Optional per-call overrides; pass <see cref="TeiSparseEmbeddingOptions"/> for TEI-specific fields.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A SparseEmbeddingResponse with one sparse vector per input.</returns>
        /// <exception cref="ArgumentNullException">Thrown when inputs is null.</exception>
        /// <exception cref="ArgumentException">Thrown when inputs is empty or contains a null element.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the token is cancelled.</exception>
        public override async Task<SparseEmbeddingResponse> EmbedSparseAsync(
            List<string> inputs,
            SparseEmbeddingOptions? options = null,
            CancellationToken token = default)
        {
            ValidateInputList(inputs, nameof(inputs), "Sparse embedding requests require at least one input.");

            SparseEmbeddingResponse sparseResponse = new SparseEmbeddingResponse();
            sparseResponse.Model = options?.Model ?? Model;

            Dictionary<string, object> requestBody = new Dictionary<string, object>
            {
                { "inputs", inputs }
            };

            TeiSparseEmbeddingOptions? teiOptions = options as TeiSparseEmbeddingOptions;
            if (teiOptions != null)
            {
                if (teiOptions.Truncate.HasValue) requestBody["truncate"] = teiOptions.Truncate.Value;
                if (teiOptions.TruncationDirection != null) requestBody["truncation_direction"] = teiOptions.TruncationDirection;
                if (teiOptions.PromptName != null) requestBody["prompt_name"] = teiOptions.PromptName;
            }

            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                CompletionHttpResult result = await PostJsonAsync("/embed_sparse", requestBody, token).ConfigureAwait(false);
                sparseResponse.StatusCode = result.StatusCode;

                if (!result.IsSuccessStatusCode)
                {
                    _Logging.Warn(_Header + "sparse embed request failed with status " + result.StatusCode + ": " + result.ResponseBody);
                    sparseResponse.Success = false;
                    sparseResponse.Error = "HTTP " + result.StatusCode + ": " + result.ResponseBody;
                    return sparseResponse;
                }

                List<List<Dictionary<string, object>>>? vectors =
                    _Serializer.DeserializeJson<List<List<Dictionary<string, object>>>>(result.ResponseBody);
                if (vectors == null)
                {
                    sparseResponse.Success = false;
                    sparseResponse.Error = "Response is not a JSON array of sparse embeddings";
                    return sparseResponse;
                }

                for (int i = 0; i < vectors.Count; i++)
                {
                    SparseEmbeddingResult sparse = new SparseEmbeddingResult();
                    sparse.Index = i;

                    foreach (Dictionary<string, object> entry in vectors[i])
                    {
                        int? index = TryGetInt(entry, "index");
                        double? value = TryGetDouble(entry, "value");
                        if (!index.HasValue || !value.HasValue) continue;
                        sparse.Values.Add(new SparseValue { Index = index.Value, Value = (float)value.Value });
                    }

                    sparseResponse.Embeddings.Add(sparse);
                }

                sparseResponse.Success = true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                sparseResponse.Success = false;
                sparseResponse.Error = ex.Message;
            }
            finally
            {
                sw.Stop();
                sparseResponse.OverallRuntimeMs = sw.ElapsedMilliseconds;
            }

            return sparseResponse;
        }

        /// <summary>
        /// List the single model hosted by the TEI server, read from <c>GET /info</c>. Yields nothing when the
        /// server is unreachable or returns an error. The returned model's Metadata carries <c>model_type</c>
        /// ("embedding", "reranker", or "classifier"), <c>model_dtype</c>, <c>max_client_batch_size</c>,
        /// <c>max_batch_tokens</c>, <c>version</c>, and <c>served_model_name</c> when reported.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An async enumerable with zero or one ModelInformation.</returns>
        /// <exception cref="OperationCanceledException">Thrown when the token is cancelled.</exception>
        public override async IAsyncEnumerable<ModelInformation> ListModelsAsync(
            [EnumeratorCancellation] CancellationToken token = default)
        {
            ModelInformation? info = await GetInfoAsync(token).ConfigureAwait(false);
            if (info != null) yield return info;
        }

        /// <summary>
        /// Return the hosted model's information when <paramref name="model"/> matches its model_id or
        /// served_model_name (case-insensitive); otherwise null.
        /// </summary>
        /// <param name="model">The model name to look up.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The hosted model's information, or null when the name does not match or the server is unreachable.</returns>
        /// <exception cref="ArgumentNullException">Thrown when model is null or empty.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the token is cancelled.</exception>
        public override async Task<ModelInformation?> GetModelInformationAsync(string model, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(model))
                throw new ArgumentNullException(nameof(model));

            ModelInformation? info = await GetInfoAsync(token).ConfigureAwait(false);
            if (info == null) return null;

            if (string.Equals(info.Name, model, StringComparison.OrdinalIgnoreCase)) return info;
            if (info.DisplayName != null && string.Equals(info.DisplayName, model, StringComparison.OrdinalIgnoreCase)) return info;

            return null;
        }

        /// <summary>
        /// Validate connectivity by calling <c>GET /health</c>, which TEI answers with 200 once the model is
        /// loaded and 503 while it is unhealthy. An unreachable server or a request that exceeds
        /// <see cref="CompletionClientBase.TimeoutMs"/> returns false; only cancellation of
        /// <paramref name="token"/> propagates.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the server reports healthy, false otherwise.</returns>
        /// <exception cref="OperationCanceledException">Thrown when the token is cancelled.</exception>
        public override async Task<bool> ValidateConnectivityAsync(CancellationToken token = default)
        {
            try
            {
                CompletionHttpResult result = await GetAndRecordAsync(BuildUrl("/health"), token).ConfigureAwait(false);
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

        #endregion

        #region Private-Methods

        private string BuildUrl(string path)
        {
            return _Endpoint.TrimEnd('/') + path;
        }

        private async Task<CompletionHttpResult> PostJsonAsync(string path, Dictionary<string, object> body, CancellationToken token)
        {
            string url = BuildUrl(path);
            string json = _Serializer.SerializeJson(body, false);
            StringContent content = new StringContent(json, Encoding.UTF8, "application/json");
            _Logging.Debug(_Header + "POST " + url);
            return await PostAndRecordAsync(url, content, json, token).ConfigureAwait(false);
        }

        private async Task<ModelInformation?> GetInfoAsync(CancellationToken token)
        {
            string url = BuildUrl("/info");
            _Logging.Debug(_Header + "GET " + url);

            try
            {
                CompletionHttpResult result = await GetAndRecordAsync(url, token).ConfigureAwait(false);
                if (!result.IsSuccessStatusCode)
                {
                    _Logging.Warn(_Header + "info request failed with status " + result.StatusCode + ": " + result.ResponseBody);
                    return null;
                }

                Dictionary<string, object>? infoObj = _Serializer.DeserializeJson<Dictionary<string, object>>(result.ResponseBody);
                if (infoObj == null || !infoObj.ContainsKey("model_id")) return null;

                ModelInformation info = new ModelInformation();
                info.Name = infoObj["model_id"]?.ToString() ?? string.Empty;
                info.DisplayName = infoObj.ContainsKey("served_model_name") ? infoObj["served_model_name"]?.ToString() : null;
                info.OwnedBy = "text-embeddings-inference";
                info.InputTokenLimit = TryGetInt(infoObj, "max_input_length");

                Dictionary<string, object>? modelType = ParseNestedObject(infoObj, "model_type");
                if (modelType != null && modelType.Count > 0)
                {
                    info.Metadata["model_type"] = modelType.Keys.First();
                }

                CopyMetadata(infoObj, info, "model_dtype");
                CopyMetadata(infoObj, info, "max_client_batch_size");
                CopyMetadata(infoObj, info, "max_batch_tokens");
                CopyMetadata(infoObj, info, "version");
                CopyMetadata(infoObj, info, "served_model_name");

                return info;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "info request failed: " + ex.Message);
                return null;
            }
        }

        private static void CopyMetadata(Dictionary<string, object> source, ModelInformation info, string key)
        {
            if (source.ContainsKey(key) && source[key] != null)
            {
                info.Metadata[key] = source[key]?.ToString();
            }
        }

        private static void FinalizeClassification(ClassificationResult classification)
        {
            classification.Labels = classification.Labels
                .OrderByDescending(l => l.Score)
                .ToList();

            ClassificationLabel? top = classification.Labels.FirstOrDefault();
            classification.Label = top?.Label;
            classification.Score = top?.Score;
        }

        #endregion
    }
}
