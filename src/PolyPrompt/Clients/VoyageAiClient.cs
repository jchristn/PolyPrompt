namespace PolyPrompt.Clients
{
    using System.Diagnostics;
    using System.Text;
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using SyslogLogging;

    /// <summary>
    /// Client for the VoyageAI API. VoyageAI is an embeddings-only provider: single and batch embeddings
    /// are fully supported, while chat, tool chat, text generation, and model management have no VoyageAI
    /// API and throw <see cref="NotSupportedException"/>. Connectivity validation is implemented with a
    /// minimal embeddings request because VoyageAI has no model listing endpoint.
    /// </summary>
    public class VoyageAiClient : CompletionClientBase
    {
        #region Private-Members

        // The prompt used by ValidateConnectivityAsync; a single short word keeps the probe cost negligible.
        private const string ConnectivityProbeInput = "ping";

        private const string UnsupportedChat = "VoyageAI is an embeddings-only provider and does not support chat completions.";
        private const string UnsupportedToolChat = "VoyageAI is an embeddings-only provider and does not support tool calling.";
        private const string UnsupportedGeneration = "VoyageAI is an embeddings-only provider and does not support text generation.";
        private const string UnsupportedModelManagement = "VoyageAI does not provide a model management API.";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new VoyageAiClient.
        /// </summary>
        /// <param name="endpoint">VoyageAI API endpoint URL. Default: https://api.voyageai.com.</param>
        /// <param name="apiKey">VoyageAI API key (required); when non-empty an Authorization: Bearer header is added. Default: null.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        /// <param name="httpClient">Optional HTTP client. When supplied, the caller owns and disposes it; use this to configure the transport (custom handler, TLS, proxy). Default: null (an internally owned client is created).</param>
        public VoyageAiClient(
            string endpoint = "https://api.voyageai.com",
            string? apiKey = null,
            LoggingModule? logging = null,
            HttpClient? httpClient = null)
            : base(endpoint, apiKey, logging ?? new LoggingModule(), httpClient)
        {
            _Header = "[VoyageAI] ";
            Model = "voyage-3.5";

            if (!string.IsNullOrEmpty(apiKey))
            {
                _HttpClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + apiKey);
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Not supported. VoyageAI is an embeddings-only provider.
        /// </summary>
        /// <param name="prompt">User message.</param>
        /// <param name="options">Optional per-call overrides.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Never returns; always throws.</returns>
        /// <exception cref="NotSupportedException">Always thrown; VoyageAI has no chat API.</exception>
        public override Task<ChatResponse> ChatAsync(
            string prompt,
            ChatCompletionOptions? options = null,
            CancellationToken token = default)
        {
            throw new NotSupportedException(UnsupportedChat);
        }

        /// <summary>
        /// Not supported. VoyageAI is an embeddings-only provider.
        /// </summary>
        /// <param name="prompt">User message.</param>
        /// <param name="options">Optional per-call overrides.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Never returns; always throws.</returns>
        /// <exception cref="NotSupportedException">Always thrown; VoyageAI has no chat API.</exception>
        public override Task<ChatStreamingResponse> ChatStreamingAsync(
            string prompt,
            ChatCompletionOptions? options = null,
            CancellationToken token = default)
        {
            throw new NotSupportedException(UnsupportedChat);
        }

        /// <summary>
        /// Not supported. VoyageAI is an embeddings-only provider.
        /// </summary>
        /// <param name="request">Tool chat request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Never returns; always throws.</returns>
        /// <exception cref="NotSupportedException">Always thrown; VoyageAI has no tool calling API.</exception>
        public override Task<ToolChatResponse> ToolChatAsync(
            ToolChatRequest request,
            CancellationToken token = default)
        {
            throw new NotSupportedException(UnsupportedToolChat);
        }

        /// <summary>
        /// Not supported. VoyageAI is an embeddings-only provider.
        /// </summary>
        /// <param name="request">Tool chat request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Never returns; always throws.</returns>
        /// <exception cref="NotSupportedException">Always thrown; VoyageAI has no tool calling API.</exception>
        public override Task<ToolChatStreamingResponse> ToolChatStreamingAsync(
            ToolChatRequest request,
            CancellationToken token = default)
        {
            throw new NotSupportedException(UnsupportedToolChat);
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

            string url = _Endpoint.TrimEnd('/') + "/v1/embeddings";

            Dictionary<string, object> requestBody = new Dictionary<string, object>
            {
                { "model", model },
                { "input", inputs }
            };

            VoyageAiEmbeddingOptions? voyageOptions = options as VoyageAiEmbeddingOptions;
            if (voyageOptions != null)
            {
                if (!string.IsNullOrEmpty(voyageOptions.InputType)) requestBody["input_type"] = voyageOptions.InputType;
                if (voyageOptions.Truncation.HasValue) requestBody["truncation"] = voyageOptions.Truncation.Value;
                if (voyageOptions.OutputDimension.HasValue) requestBody["output_dimension"] = voyageOptions.OutputDimension.Value;
                if (!string.IsNullOrEmpty(voyageOptions.OutputDtype)) requestBody["output_dtype"] = voyageOptions.OutputDtype;
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

        /// <summary>
        /// Not supported. VoyageAI is an embeddings-only provider.
        /// </summary>
        /// <param name="prompt">The prompt text.</param>
        /// <param name="options">Optional per-call overrides.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Never returns; always throws.</returns>
        /// <exception cref="NotSupportedException">Always thrown; VoyageAI has no text generation API.</exception>
        public override Task<GenerationResponse> GenerateAsync(
            string prompt,
            GenerationOptions? options = null,
            CancellationToken token = default)
        {
            throw new NotSupportedException(UnsupportedGeneration);
        }

        /// <summary>
        /// Not supported. VoyageAI is an embeddings-only provider.
        /// </summary>
        /// <param name="prompt">The prompt text.</param>
        /// <param name="options">Optional per-call overrides.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Never returns; always throws.</returns>
        /// <exception cref="NotSupportedException">Always thrown; VoyageAI has no text generation API.</exception>
        public override Task<GenerationStreamingResponse> GenerateStreamingAsync(
            string prompt,
            GenerationOptions? options = null,
            CancellationToken token = default)
        {
            throw new NotSupportedException(UnsupportedGeneration);
        }

        /// <summary>
        /// Not supported. VoyageAI does not provide a model management API. Thrown when the method is
        /// called, not when the returned sequence is enumerated.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Never returns; always throws.</returns>
        /// <exception cref="NotSupportedException">Always thrown; VoyageAI has no model listing endpoint.</exception>
        public override IAsyncEnumerable<ModelInformation> ListModelsAsync(CancellationToken token = default)
        {
            throw new NotSupportedException(UnsupportedModelManagement);
        }

        /// <summary>
        /// Not supported. VoyageAI does not provide a model management API. This overrides the base
        /// implementation, which would otherwise swallow the ListModelsAsync exception and silently
        /// return false.
        /// </summary>
        /// <param name="model">The model name to search for.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Never returns; always throws.</returns>
        /// <exception cref="NotSupportedException">Always thrown; VoyageAI has no model listing endpoint.</exception>
        public override Task<bool> ModelExistsAsync(string model, CancellationToken token = default)
        {
            throw new NotSupportedException(UnsupportedModelManagement);
        }

        /// <summary>
        /// Not supported. VoyageAI does not provide a model management API.
        /// </summary>
        /// <param name="model">The model name to look up.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Never returns; always throws.</returns>
        /// <exception cref="NotSupportedException">Always thrown; VoyageAI has no model information endpoint.</exception>
        public override Task<ModelInformation?> GetModelInformationAsync(string model, CancellationToken token = default)
        {
            throw new NotSupportedException(UnsupportedModelManagement);
        }

        /// <summary>
        /// Validate connectivity by sending a minimal single-word embeddings request, because VoyageAI has
        /// no model listing endpoint to probe. The probe uses the configured <see cref="CompletionClientBase.Model"/>
        /// and consumes a negligible number of tokens.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the provider accepted the embeddings request, false otherwise.</returns>
        /// <exception cref="OperationCanceledException">Thrown when the token is cancelled.</exception>
        public override async Task<bool> ValidateConnectivityAsync(CancellationToken token = default)
        {
            try
            {
                EmbeddingResponse probe = await EmbedAsync(ConnectivityProbeInput, null, token).ConfigureAwait(false);
                return probe.Success;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return false;
            }
        }

        #endregion
    }
}
