namespace PolyPrompt.Clients
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Net.Http;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using PolyPrompt.Auth;
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using PolyPrompt.Wire;
    using SyslogLogging;

    /// <summary>
    /// Client for AWS Bedrock. Inference uses the unified <c>Converse</c> / <c>ConverseStream</c> API, which
    /// normalizes messages, tools, reasoning, and usage across the model families Bedrock hosts (Anthropic,
    /// Amazon, Meta, Cohere, Mistral), so PolyPrompt needs almost no per-family branching. Embeddings use
    /// <c>InvokeModel</c> with family-specific bodies (Amazon Titan and Cohere). Every request is authenticated
    /// with AWS Signature Version 4 (see <see cref="SigV4Signer"/>) via the per-request
    /// <see cref="CompletionClientBase.PrepareRequestAsync"/> hook. <c>ConverseStream</c> responses are the
    /// AWS binary event-stream format, decoded by <see cref="EventStreamDecoder"/>.
    /// </summary>
    public class BedrockClient : CompletionClientBase
    {
        #region Private-Members

        private const string ServiceName = "bedrock";
        private const string DefaultModel = "anthropic.claude-3-5-sonnet-20240620-v1:0";

        private readonly IAwsCredentialProvider _CredentialProvider;
        private readonly string _Region;
        private readonly string _ControlPlaneEndpoint;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new Bedrock client.
        /// </summary>
        /// <param name="credentialProvider">Resolves AWS credentials to sign each request.</param>
        /// <param name="region">AWS region, e.g. <c>us-east-1</c>. Selects the endpoint host and signing region.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        /// <param name="httpClient">Optional HTTP client (caller-owned when supplied).</param>
        /// <param name="endpoint">Optional endpoint override; defaults to <c>https://bedrock-runtime.{region}.amazonaws.com</c>.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null or empty.</exception>
        public BedrockClient(
            IAwsCredentialProvider credentialProvider,
            string region,
            LoggingModule? logging = null,
            HttpClient? httpClient = null,
            string? endpoint = null)
            : base(ResolveEndpoint(endpoint, region), apiKey: null, logging ?? new LoggingModule(), httpClient)
        {
            if (string.IsNullOrWhiteSpace(region)) throw new ArgumentNullException(nameof(region));

            _Header = "[Bedrock] ";
            _CredentialProvider = credentialProvider ?? throw new ArgumentNullException(nameof(credentialProvider));
            _Region = region;
            Model = DefaultModel;

            // Model management lives on the control-plane host. When the runtime endpoint is overridden (for
            // example in tests), route control-plane calls to the same override so both are addressable.
            _ControlPlaneEndpoint = !string.IsNullOrEmpty(endpoint)
                ? endpoint!.TrimEnd('/')
                : "https://bedrock." + region + ".amazonaws.com";
        }

        private static string ResolveEndpoint(string? endpoint, string region)
        {
            if (!string.IsNullOrEmpty(endpoint)) return endpoint;
            if (string.IsNullOrWhiteSpace(region)) throw new ArgumentNullException(nameof(region));
            return "https://bedrock-runtime." + region + ".amazonaws.com";
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

            List<Dictionary<string, object>> messages = new List<Dictionary<string, object>>
            {
                SimpleUserMessage(prompt)
            };
            Dictionary<string, object> body = BuildConverseBody(
                Model, messages, SystemBlocks(systemPrompt), maxTokens, temperature, topP, tools: null, toolChoice: null, reasoningEffort: null);

            string url = BuildConverseUrl(Model, streaming: false);
            string json = _Serializer.SerializeJson(body, false);
            StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

            _Logging.Debug(_Header + "POST " + url);

            try
            {
                CompletionHttpResult result = await PostAndRecordAsync(url, content, json, token).ConfigureAwait(false);
                chatResponse.StatusCode = result.StatusCode;

                if (!result.IsSuccessStatusCode)
                {
                    _Logging.Warn(_Header + "chat request failed with status " + result.StatusCode + ": " + result.ResponseBody);
                    chatResponse.Success = false;
                    chatResponse.Error = "HTTP " + result.StatusCode + ": " + result.ResponseBody;
                    return chatResponse;
                }

                ConverseParseResult parsed = ParseConverseResponse(result.ResponseBody);
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

            List<Dictionary<string, object>> messages = new List<Dictionary<string, object>> { SimpleUserMessage(prompt) };
            Dictionary<string, object> body = BuildConverseBody(
                Model, messages, SystemBlocks(systemPrompt), maxTokens, temperature, topP, tools: null, toolChoice: null, reasoningEffort: null);

            string url = BuildConverseUrl(Model, streaming: true);
            string json = _Serializer.SerializeJson(body, false);
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
                streamingResponse.Chunks = WrapChunksWithTiming(streamingResponse, ReadBedrockChatChunks(response, streamingResult.Token), sw, streamingResult.Token, streamingResult);
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

            Dictionary<string, object> body = BuildConverseBody(
                model,
                BuildConverseMessages(request.Messages),
                BuildConverseSystem(request.Messages),
                maxTokens, temperature, topP,
                BuildConverseTools(request.Tools, request.ToolChoice),
                request.ToolChoice,
                reasoningEffort);

            string url = BuildConverseUrl(model, streaming: false);
            string json = _Serializer.SerializeJson(body, false);
            StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

            _Logging.Debug(_Header + "POST " + url);

            try
            {
                CompletionHttpResult result = await PostAndRecordAsync(url, content, json, token).ConfigureAwait(false);
                toolResponse.StatusCode = result.StatusCode;

                if (!result.IsSuccessStatusCode)
                {
                    _Logging.Warn(_Header + "tool chat request failed with status " + result.StatusCode + ": " + result.ResponseBody);
                    toolResponse.Success = false;
                    toolResponse.Error = "HTTP " + result.StatusCode + ": " + result.ResponseBody;
                    return toolResponse;
                }

                ConverseParseResult parsed = ParseConverseResponse(result.ResponseBody);
                toolResponse.Text = parsed.Text;
                toolResponse.Reasoning = NormalizeReasoning(parsed.Reasoning);
                toolResponse.FinishReason = parsed.StopReason;
                toolResponse.Usage = parsed.Usage;
                foreach (ToolCall call in parsed.ToolCalls) toolResponse.ToolCalls.Add(call);
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

        /// <inheritdoc />
        public override async Task<ToolChatStreamingResponse> ToolChatStreamingAsync(
            ToolChatRequest request,
            CancellationToken token = default)
        {
            ResolveToolChatRequest(request, out string model, out int maxTokens, out double? temperature, out double? topP, out ReasoningEffort? reasoningEffort);

            Dictionary<string, object> body = BuildConverseBody(
                model,
                BuildConverseMessages(request.Messages),
                BuildConverseSystem(request.Messages),
                maxTokens, temperature, topP,
                BuildConverseTools(request.Tools, request.ToolChoice),
                request.ToolChoice,
                reasoningEffort);

            string url = BuildConverseUrl(model, streaming: true);
            string json = _Serializer.SerializeJson(body, false);
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
                streamingResponse.Chunks = WrapToolChatChunksWithTiming(streamingResponse, ReadBedrockToolChatChunks(response, streamingResult.Token), sw, streamingResult.Token, streamingResult);
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

            try
            {
                BedrockEmbeddingOptions? bedrockOptions = options as BedrockEmbeddingOptions;

                if (model.StartsWith("cohere.", StringComparison.OrdinalIgnoreCase))
                {
                    await EmbedCohereAsync(model, inputs, bedrockOptions, embedResponse, token).ConfigureAwait(false);
                }
                else
                {
                    // Amazon Titan (and any single-input family): one InvokeModel per input.
                    await EmbedTitanAsync(model, inputs, bedrockOptions, embedResponse, token).ConfigureAwait(false);
                }
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

            List<Dictionary<string, object>> messages = new List<Dictionary<string, object>> { SimpleUserMessage(prompt) };
            Dictionary<string, object> body = BuildConverseBody(
                model, messages, system: null, maxTokens, temperature, topP, tools: null, toolChoice: null, reasoningEffort: null);

            string url = BuildConverseUrl(model, streaming: false);
            string json = _Serializer.SerializeJson(body, false);
            StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

            _Logging.Debug(_Header + "POST " + url);

            try
            {
                CompletionHttpResult result = await PostAndRecordAsync(url, content, json, token).ConfigureAwait(false);
                genResponse.StatusCode = result.StatusCode;

                if (!result.IsSuccessStatusCode)
                {
                    _Logging.Warn(_Header + "generate request failed with status " + result.StatusCode + ": " + result.ResponseBody);
                    genResponse.Success = false;
                    genResponse.Error = "HTTP " + result.StatusCode + ": " + result.ResponseBody;
                    return genResponse;
                }

                ConverseParseResult parsed = ParseConverseResponse(result.ResponseBody);
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

        /// <inheritdoc />
        public override async Task<GenerationStreamingResponse> GenerateStreamingAsync(
            string prompt,
            GenerationOptions? options = null,
            CancellationToken token = default)
        {
            ResolveGenerationOptions(options, out string model, out int maxTokens, out double? temperature, out double? topP);

            List<Dictionary<string, object>> messages = new List<Dictionary<string, object>> { SimpleUserMessage(prompt) };
            Dictionary<string, object> body = BuildConverseBody(
                model, messages, system: null, maxTokens, temperature, topP, tools: null, toolChoice: null, reasoningEffort: null);

            string url = BuildConverseUrl(model, streaming: true);
            string json = _Serializer.SerializeJson(body, false);
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
                streamingResponse.Chunks = WrapGenerationChunksWithTiming(streamingResponse, ReadBedrockGenerateChunks(response, streamingResult.Token), sw, streamingResult.Token, streamingResult);
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
            // Model listing lives on the Bedrock control-plane host, not the runtime host, but signs the same.
            string url = _ControlPlaneEndpoint + "/foundation-models";
            _Logging.Debug(_Header + "GET " + url);

            CompletionHttpResult result = await GetAndRecordAsync(url, token).ConfigureAwait(false);
            if (!result.IsSuccessStatusCode)
            {
                _Logging.Warn(_Header + "list models failed with status " + result.StatusCode);
                yield break;
            }

            Dictionary<string, object>? responseObj = _Serializer.DeserializeJson<Dictionary<string, object>>(result.ResponseBody);
            if (responseObj == null || !responseObj.ContainsKey("modelSummaries"))
                yield break;

            string summariesJson = _Serializer.SerializeJson(responseObj["modelSummaries"], false);
            List<Dictionary<string, object>>? summaries = _Serializer.DeserializeJson<List<Dictionary<string, object>>>(summariesJson);
            if (summaries == null)
                yield break;

            foreach (Dictionary<string, object> summary in summaries)
            {
                token.ThrowIfCancellationRequested();

                ModelInformation info = new ModelInformation();
                info.Name = summary.ContainsKey("modelId") ? summary["modelId"]?.ToString() ?? "" : "";
                info.DisplayName = summary.ContainsKey("modelName") ? summary["modelName"]?.ToString() : null;
                info.OwnedBy = summary.ContainsKey("providerName") ? summary["providerName"]?.ToString() : null;
                yield return info;
            }
        }

        /// <inheritdoc />
        public override async Task<ModelInformation?> GetModelInformationAsync(string model, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(model)) throw new ArgumentNullException(nameof(model));

            string url = _ControlPlaneEndpoint + "/foundation-models/" + Uri.EscapeDataString(model);
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

                Dictionary<string, object> details = responseObj;
                if (responseObj.ContainsKey("modelDetails"))
                {
                    string detailsJson = _Serializer.SerializeJson(responseObj["modelDetails"], false);
                    details = _Serializer.DeserializeJson<Dictionary<string, object>>(detailsJson) ?? responseObj;
                }

                ModelInformation info = new ModelInformation();
                info.Name = details.ContainsKey("modelId") ? details["modelId"]?.ToString() ?? model : model;
                info.DisplayName = details.ContainsKey("modelName") ? details["modelName"]?.ToString() : null;
                info.OwnedBy = details.ContainsKey("providerName") ? details["providerName"]?.ToString() : null;
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

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
        {
            AwsCredentials resolved = _CredentialProvider.Resolve();

            // The client's region is authoritative for both the endpoint host and the signature, so a
            // credential provider configured with a different region cannot desynchronize the two.
            AwsCredentials signing = new AwsCredentials(resolved.AccessKeyId, resolved.SecretAccessKey, _Region, resolved.SessionToken);
            SigV4Signer.Sign(request, body, signing, ServiceName, DateTimeOffset.UtcNow);
            return Task.CompletedTask;
        }

        #endregion

        #region Private-Methods

        private string BuildConverseUrl(string model, bool streaming)
        {
            string op = streaming ? "converse-stream" : "converse";
            return _Endpoint.TrimEnd('/') + "/model/" + model + "/" + op;
        }

        private static Dictionary<string, object> SimpleUserMessage(string text)
        {
            return new Dictionary<string, object>
            {
                { "role", "user" },
                { "content", new List<object> { new Dictionary<string, object> { { "text", text ?? string.Empty } } } }
            };
        }

        private static List<Dictionary<string, object>>? SystemBlocks(string? systemPrompt)
        {
            if (string.IsNullOrEmpty(systemPrompt)) return null;
            return new List<Dictionary<string, object>> { new Dictionary<string, object> { { "text", systemPrompt } } };
        }

        private Dictionary<string, object> BuildConverseBody(
            string model,
            List<Dictionary<string, object>> messages,
            List<Dictionary<string, object>>? system,
            int maxTokens,
            double? temperature,
            double? topP,
            List<Dictionary<string, object>>? tools,
            string? toolChoice,
            ReasoningEffort? reasoningEffort)
        {
            Dictionary<string, object> inferenceConfig = new Dictionary<string, object>
            {
                { "maxTokens", maxTokens }
            };

            bool thinkingEnabled = reasoningEffort != null
                && model.StartsWith("anthropic.", StringComparison.OrdinalIgnoreCase)
                && reasoningEffort.ToBedrockThinkingBudget() > 0;

            // Anthropic requires temperature/top_p to be unset while extended thinking is enabled.
            if (!thinkingEnabled)
            {
                if (temperature.HasValue) inferenceConfig["temperature"] = temperature.Value;
                if (topP.HasValue) inferenceConfig["topP"] = topP.Value;
            }

            Dictionary<string, object> body = new Dictionary<string, object>
            {
                { "messages", messages },
                { "inferenceConfig", inferenceConfig }
            };

            if (system != null && system.Count > 0) body["system"] = system;

            if (tools != null && tools.Count > 0)
            {
                Dictionary<string, object> toolConfig = new Dictionary<string, object> { { "tools", tools } };
                Dictionary<string, object>? choice = BuildConverseToolChoice(toolChoice);
                if (choice != null) toolConfig["toolChoice"] = choice;
                body["toolConfig"] = toolConfig;
            }

            if (thinkingEnabled)
            {
                body["additionalModelRequestFields"] = new Dictionary<string, object>
                {
                    { "thinking", new Dictionary<string, object>
                        {
                            { "type", "enabled" },
                            { "budget_tokens", reasoningEffort!.ToBedrockThinkingBudget() }
                        }
                    }
                };
            }

            return body;
        }

        private static Dictionary<string, object>? BuildConverseToolChoice(string? toolChoice)
        {
            if (string.IsNullOrWhiteSpace(toolChoice)) return null;
            if (string.Equals(toolChoice, "auto", StringComparison.OrdinalIgnoreCase))
                return new Dictionary<string, object> { { "auto", new Dictionary<string, object>() } };
            if (string.Equals(toolChoice, "required", StringComparison.OrdinalIgnoreCase)
                || string.Equals(toolChoice, "any", StringComparison.OrdinalIgnoreCase))
                return new Dictionary<string, object> { { "any", new Dictionary<string, object>() } };
            if (string.Equals(toolChoice, "none", StringComparison.OrdinalIgnoreCase))
                return null;

            // A specific tool name.
            return new Dictionary<string, object>
            {
                { "tool", new Dictionary<string, object> { { "name", toolChoice } } }
            };
        }

        private List<Dictionary<string, object>>? BuildConverseTools(List<ToolDefinition>? tools, string? toolChoice)
        {
            if (tools == null || tools.Count == 0) return null;
            if (string.Equals(toolChoice, "none", StringComparison.OrdinalIgnoreCase)) return null;

            List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();
            foreach (ToolDefinition tool in tools)
            {
                result.Add(new Dictionary<string, object>
                {
                    { "toolSpec", new Dictionary<string, object>
                        {
                            { "name", tool.Name },
                            { "description", tool.Description ?? string.Empty },
                            { "inputSchema", new Dictionary<string, object> { { "json", tool.Parameters } } }
                        }
                    }
                });
            }

            return result;
        }

        private List<Dictionary<string, object>>? BuildConverseSystem(List<ChatMessage> messages)
        {
            List<Dictionary<string, object>> blocks = new List<Dictionary<string, object>>();
            foreach (ChatMessage message in messages)
            {
                if (string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(message.Content))
                {
                    blocks.Add(new Dictionary<string, object> { { "text", message.Content } });
                }
            }

            return blocks.Count > 0 ? blocks : null;
        }

        private List<Dictionary<string, object>> BuildConverseMessages(List<ChatMessage> messages)
        {
            List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();
            List<object>? pendingToolResults = null;

            foreach (ChatMessage message in messages)
            {
                if (string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase))
                    continue;

                bool isToolResult = string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(message.Role, "function", StringComparison.OrdinalIgnoreCase);

                if (isToolResult)
                {
                    // Consecutive tool results collapse into a single user message (parallel tool calls).
                    pendingToolResults ??= new List<object>();
                    pendingToolResults.Add(new Dictionary<string, object>
                    {
                        { "toolResult", new Dictionary<string, object>
                            {
                                { "toolUseId", ResolveToolUseId(message) },
                                { "content", new List<object> { new Dictionary<string, object> { { "text", message.Content ?? string.Empty } } } }
                            }
                        }
                    });
                    continue;
                }

                FlushToolResults(result, ref pendingToolResults);

                List<object> content = new List<object>();
                if (message.ToolCalls != null && message.ToolCalls.Count > 0)
                {
                    if (!string.IsNullOrEmpty(message.Content))
                        content.Add(new Dictionary<string, object> { { "text", message.Content } });

                    foreach (ToolCall call in message.ToolCalls)
                    {
                        content.Add(new Dictionary<string, object>
                        {
                            { "toolUse", new Dictionary<string, object>
                                {
                                    { "toolUseId", call.Id ?? call.Name },
                                    { "name", call.Name },
                                    { "input", DeserializeDictionaryOrEmpty(call.ArgumentsJson) }
                                }
                            }
                        });
                    }
                }
                else
                {
                    content.Add(new Dictionary<string, object> { { "text", message.Content ?? string.Empty } });
                }

                result.Add(new Dictionary<string, object>
                {
                    { "role", NormalizeConverseRole(message.Role) },
                    { "content", content }
                });
            }

            FlushToolResults(result, ref pendingToolResults);
            return result;
        }

        private static void FlushToolResults(List<Dictionary<string, object>> result, ref List<object>? pendingToolResults)
        {
            if (pendingToolResults == null || pendingToolResults.Count == 0)
            {
                pendingToolResults = null;
                return;
            }

            result.Add(new Dictionary<string, object>
            {
                { "role", "user" },
                { "content", pendingToolResults }
            });
            pendingToolResults = null;
        }

        private static string NormalizeConverseRole(string? role)
        {
            if (string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase)
                || string.Equals(role, "model", StringComparison.OrdinalIgnoreCase))
                return "assistant";
            return "user";
        }

        private static string ResolveToolUseId(ChatMessage message)
        {
            if (!string.IsNullOrWhiteSpace(message.ToolCallId)) return message.ToolCallId!;
            if (!string.IsNullOrWhiteSpace(message.ToolName)) return message.ToolName!;
            return "tool";
        }

        private Dictionary<string, object> DeserializeDictionaryOrEmpty(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, object>();
            Dictionary<string, object>? parsed = _Serializer.DeserializeJson<Dictionary<string, object>>(json);
            return parsed ?? new Dictionary<string, object>();
        }

        private ConverseParseResult ParseConverseResponse(string responseBody)
        {
            ConverseParseResult parseResult = new ConverseParseResult();

            Dictionary<string, object>? responseObj = _Serializer.DeserializeJson<Dictionary<string, object>>(responseBody);
            if (responseObj == null) return parseResult;

            parseResult.Usage = ReadMetadataUsage(responseObj);

            if (responseObj.ContainsKey("stopReason"))
                parseResult.StopReason = responseObj["stopReason"]?.ToString();

            if (!responseObj.ContainsKey("output")) return parseResult;

            string outputJson = _Serializer.SerializeJson(responseObj["output"], false);
            Dictionary<string, object>? output = _Serializer.DeserializeJson<Dictionary<string, object>>(outputJson);
            if (output == null || !output.ContainsKey("message")) return parseResult;

            string messageJson = _Serializer.SerializeJson(output["message"], false);
            Dictionary<string, object>? message = _Serializer.DeserializeJson<Dictionary<string, object>>(messageJson);
            if (message == null || !message.ContainsKey("content")) return parseResult;

            string contentJson = _Serializer.SerializeJson(message["content"], false);
            List<Dictionary<string, object>>? blocks = _Serializer.DeserializeJson<List<Dictionary<string, object>>>(contentJson);
            if (blocks == null) return parseResult;

            StringBuilder text = new StringBuilder();
            StringBuilder reasoning = new StringBuilder();

            foreach (Dictionary<string, object> block in blocks)
            {
                if (block.ContainsKey("text"))
                {
                    text.Append(block["text"]?.ToString());
                }
                else if (block.ContainsKey("reasoningContent"))
                {
                    string reasoningJson = _Serializer.SerializeJson(block["reasoningContent"], false);
                    Dictionary<string, object>? reasoningObj = _Serializer.DeserializeJson<Dictionary<string, object>>(reasoningJson);
                    if (reasoningObj != null && reasoningObj.ContainsKey("reasoningText"))
                    {
                        string reasoningTextJson = _Serializer.SerializeJson(reasoningObj["reasoningText"], false);
                        Dictionary<string, object>? reasoningText = _Serializer.DeserializeJson<Dictionary<string, object>>(reasoningTextJson);
                        if (reasoningText != null && reasoningText.ContainsKey("text"))
                            reasoning.Append(reasoningText["text"]?.ToString());
                    }
                }
                else if (block.ContainsKey("toolUse"))
                {
                    ToolCall? call = ParseConverseToolUse(block["toolUse"]);
                    if (call != null) parseResult.ToolCalls.Add(call);
                }
            }

            parseResult.Text = text.Length > 0 ? text.ToString() : null;
            parseResult.Reasoning = reasoning.Length > 0 ? reasoning.ToString() : null;
            return parseResult;
        }

        private ToolCall? ParseConverseToolUse(object toolUseObj)
        {
            string toolUseJson = _Serializer.SerializeJson(toolUseObj, false);
            Dictionary<string, object>? toolUse = _Serializer.DeserializeJson<Dictionary<string, object>>(toolUseJson);
            if (toolUse == null || !toolUse.ContainsKey("name")) return null;

            ToolCall call = new ToolCall();
            call.Id = toolUse.ContainsKey("toolUseId") ? toolUse["toolUseId"]?.ToString() : null;
            call.Name = toolUse["name"]?.ToString() ?? string.Empty;
            call.ArgumentsJson = toolUse.ContainsKey("input") && toolUse["input"] != null
                ? _Serializer.SerializeJson(toolUse["input"], false)
                : "{}";
            return call;
        }

        // ----- Embeddings (InvokeModel) -----

        private async Task EmbedTitanAsync(
            string model, List<string> inputs, BedrockEmbeddingOptions? options, EmbeddingResponse embedResponse, CancellationToken token)
        {
            for (int i = 0; i < inputs.Count; i++)
            {
                Dictionary<string, object> body = new Dictionary<string, object> { { "inputText", inputs[i] } };
                if (options != null)
                {
                    if (options.Dimensions.HasValue) body["dimensions"] = options.Dimensions.Value;
                    if (options.Normalize.HasValue) body["normalize"] = options.Normalize.Value;
                }

                string json = _Serializer.SerializeJson(body, false);
                CompletionHttpResult result = await InvokeModelAsync(model, json, token).ConfigureAwait(false);
                embedResponse.StatusCode = result.StatusCode;

                if (!result.IsSuccessStatusCode)
                {
                    embedResponse.Success = false;
                    embedResponse.Error = "HTTP " + result.StatusCode + ": " + result.ResponseBody;
                    return;
                }

                Dictionary<string, object>? responseObj = _Serializer.DeserializeJson<Dictionary<string, object>>(result.ResponseBody);
                if (responseObj != null && responseObj.ContainsKey("embedding"))
                {
                    string vectorJson = _Serializer.SerializeJson(responseObj["embedding"], false);
                    EmbeddingResult embResult = new EmbeddingResult();
                    embResult.Index = i;
                    embResult.Embedding = ParseFloatArray(vectorJson);
                    embedResponse.Embeddings.Add(embResult);
                }
            }

            embedResponse.Success = true;
        }

        private async Task EmbedCohereAsync(
            string model, List<string> inputs, BedrockEmbeddingOptions? options, EmbeddingResponse embedResponse, CancellationToken token)
        {
            Dictionary<string, object> body = new Dictionary<string, object>
            {
                { "texts", inputs },
                { "input_type", string.IsNullOrEmpty(options?.InputType) ? "search_document" : options!.InputType! }
            };

            string json = _Serializer.SerializeJson(body, false);
            CompletionHttpResult result = await InvokeModelAsync(model, json, token).ConfigureAwait(false);
            embedResponse.StatusCode = result.StatusCode;

            if (!result.IsSuccessStatusCode)
            {
                embedResponse.Success = false;
                embedResponse.Error = "HTTP " + result.StatusCode + ": " + result.ResponseBody;
                return;
            }

            Dictionary<string, object>? responseObj = _Serializer.DeserializeJson<Dictionary<string, object>>(result.ResponseBody);
            if (responseObj != null && responseObj.ContainsKey("embeddings"))
            {
                string embeddingsJson = _Serializer.SerializeJson(responseObj["embeddings"], false);
                List<object>? vectors = _Serializer.DeserializeJson<List<object>>(embeddingsJson);
                if (vectors != null)
                {
                    for (int i = 0; i < vectors.Count; i++)
                    {
                        string vectorJson = _Serializer.SerializeJson(vectors[i], false);
                        EmbeddingResult embResult = new EmbeddingResult();
                        embResult.Index = i;
                        embResult.Embedding = ParseFloatArray(vectorJson);
                        embedResponse.Embeddings.Add(embResult);
                    }
                }
            }

            embedResponse.Success = true;
        }

        private async Task<CompletionHttpResult> InvokeModelAsync(string model, string json, CancellationToken token)
        {
            string url = _Endpoint.TrimEnd('/') + "/model/" + model + "/invoke";
            StringContent content = new StringContent(json, Encoding.UTF8, "application/json");
            _Logging.Debug(_Header + "POST " + url);
            return await PostAndRecordAsync(url, content, json, token).ConfigureAwait(false);
        }

        // ----- Streaming readers over the AWS event-stream -----

        private async IAsyncEnumerable<ChatStreamingChunk> ReadBedrockChatChunks(
            HttpResponseMessage response,
            [EnumeratorCancellation] CancellationToken token)
        {
            using Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            await foreach (EventStreamMessage message in EventStreamDecoder.DecodeAsync(stream, token).ConfigureAwait(false))
            {
                ChatStreamingChunk chunk = new ChatStreamingChunk();
                Dictionary<string, object>? payload = _Serializer.DeserializeJson<Dictionary<string, object>>(message.PayloadString);
                string? eventType = message.EventType;

                if (eventType == "contentBlockDelta" && payload != null)
                {
                    (string? text, string? reasoning, _) = ReadDeltaBlock(payload);
                    chunk.Text = text;
                    chunk.ReasoningText = reasoning;
                }
                else if (eventType == "messageStop" && payload != null)
                {
                    chunk.FinishReason = payload.ContainsKey("stopReason") ? payload["stopReason"]?.ToString() : null;
                    chunk.Done = true;
                }
                else if (eventType == "metadata" && payload != null)
                {
                    chunk.Usage = ReadMetadataUsage(payload);
                }

                yield return chunk;
            }
        }

        private async IAsyncEnumerable<ToolChatStreamingChunk> ReadBedrockToolChatChunks(
            HttpResponseMessage response,
            [EnumeratorCancellation] CancellationToken token)
        {
            using Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            await foreach (EventStreamMessage message in EventStreamDecoder.DecodeAsync(stream, token).ConfigureAwait(false))
            {
                ToolChatStreamingChunk chunk = new ToolChatStreamingChunk();
                Dictionary<string, object>? payload = _Serializer.DeserializeJson<Dictionary<string, object>>(message.PayloadString);
                string? eventType = message.EventType;

                if (payload != null)
                {
                    if (eventType == "contentBlockStart")
                    {
                        ToolCallDelta? startDelta = ReadToolUseStart(payload);
                        if (startDelta != null) chunk.ToolCallDeltas.Add(startDelta);
                    }
                    else if (eventType == "contentBlockDelta")
                    {
                        (string? text, string? reasoning, ToolCallDelta? toolDelta) = ReadDeltaBlock(payload);
                        chunk.Text = text;
                        chunk.ReasoningText = reasoning;
                        if (toolDelta != null) chunk.ToolCallDeltas.Add(toolDelta);
                    }
                    else if (eventType == "messageStop")
                    {
                        chunk.FinishReason = payload.ContainsKey("stopReason") ? payload["stopReason"]?.ToString() : null;
                        chunk.Done = true;
                    }
                    else if (eventType == "metadata")
                    {
                        chunk.Usage = ReadMetadataUsage(payload);
                    }
                }

                yield return chunk;
            }
        }

        private async IAsyncEnumerable<GenerationStreamingChunk> ReadBedrockGenerateChunks(
            HttpResponseMessage response,
            [EnumeratorCancellation] CancellationToken token)
        {
            using Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            await foreach (EventStreamMessage message in EventStreamDecoder.DecodeAsync(stream, token).ConfigureAwait(false))
            {
                GenerationStreamingChunk chunk = new GenerationStreamingChunk();
                Dictionary<string, object>? payload = _Serializer.DeserializeJson<Dictionary<string, object>>(message.PayloadString);
                string? eventType = message.EventType;

                if (eventType == "contentBlockDelta" && payload != null)
                {
                    (string? text, _, _) = ReadDeltaBlock(payload);
                    chunk.Text = text;
                }
                else if (eventType == "messageStop")
                {
                    chunk.Done = true;
                }

                yield return chunk;
            }
        }

        private (string? text, string? reasoning, ToolCallDelta? toolDelta) ReadDeltaBlock(Dictionary<string, object> payload)
        {
            if (!payload.ContainsKey("delta")) return (null, null, null);

            int index = TryGetInt(payload, "contentBlockIndex") ?? 0;
            string deltaJson = _Serializer.SerializeJson(payload["delta"], false);
            Dictionary<string, object>? delta = _Serializer.DeserializeJson<Dictionary<string, object>>(deltaJson);
            if (delta == null) return (null, null, null);

            if (delta.ContainsKey("text"))
                return (delta["text"]?.ToString(), null, null);

            if (delta.ContainsKey("reasoningContent"))
            {
                string reasoningJson = _Serializer.SerializeJson(delta["reasoningContent"], false);
                Dictionary<string, object>? reasoning = _Serializer.DeserializeJson<Dictionary<string, object>>(reasoningJson);
                if (reasoning != null && reasoning.ContainsKey("text"))
                    return (null, reasoning["text"]?.ToString(), null);
                return (null, null, null);
            }

            if (delta.ContainsKey("toolUse"))
            {
                string toolUseJson = _Serializer.SerializeJson(delta["toolUse"], false);
                Dictionary<string, object>? toolUse = _Serializer.DeserializeJson<Dictionary<string, object>>(toolUseJson);
                if (toolUse != null && toolUse.ContainsKey("input"))
                {
                    ToolCallDelta toolDelta = new ToolCallDelta();
                    toolDelta.Index = index;
                    toolDelta.ArgumentsJsonDelta = toolUse["input"]?.ToString();
                    return (null, null, toolDelta);
                }
            }

            return (null, null, null);
        }

        private ToolCallDelta? ReadToolUseStart(Dictionary<string, object> payload)
        {
            if (!payload.ContainsKey("start")) return null;

            int index = TryGetInt(payload, "contentBlockIndex") ?? 0;
            string startJson = _Serializer.SerializeJson(payload["start"], false);
            Dictionary<string, object>? start = _Serializer.DeserializeJson<Dictionary<string, object>>(startJson);
            if (start == null || !start.ContainsKey("toolUse")) return null;

            string toolUseJson = _Serializer.SerializeJson(start["toolUse"], false);
            Dictionary<string, object>? toolUse = _Serializer.DeserializeJson<Dictionary<string, object>>(toolUseJson);
            if (toolUse == null || !toolUse.ContainsKey("name")) return null;

            ToolCallDelta delta = new ToolCallDelta();
            delta.Index = index;
            delta.Type = "function";
            delta.Id = toolUse.ContainsKey("toolUseId") ? toolUse["toolUseId"]?.ToString() : null;
            delta.Name = toolUse["name"]?.ToString();
            return delta;
        }

        private ChatStreamingUsage? ReadMetadataUsage(Dictionary<string, object> payload)
        {
            if (!payload.ContainsKey("usage")) return null;

            string usageJson = _Serializer.SerializeJson(payload["usage"], false);
            Dictionary<string, object>? usageObj = _Serializer.DeserializeJson<Dictionary<string, object>>(usageJson);
            if (usageObj == null) return null;

            ChatStreamingUsage usage = new ChatStreamingUsage();
            if (usageObj.ContainsKey("inputTokens") && int.TryParse(usageObj["inputTokens"]?.ToString(), out int inTok))
                usage.PromptTokens = inTok;
            if (usageObj.ContainsKey("outputTokens") && int.TryParse(usageObj["outputTokens"]?.ToString(), out int outTok))
                usage.CompletionTokens = outTok;
            if (usageObj.ContainsKey("totalTokens") && int.TryParse(usageObj["totalTokens"]?.ToString(), out int totTok))
                usage.TotalTokens = totTok;

            // Bedrock reports cache reads/writes as separate buckets additional to inputTokens (matching
            // Anthropic's semantic). Absent unless prompt caching was used.
            usage.CachedPromptTokens = TryGetInt(usageObj, "cacheReadInputTokens");
            usage.CacheCreationTokens = TryGetInt(usageObj, "cacheWriteInputTokens");
            return usage;
        }

        private sealed class ConverseParseResult
        {
            public string? Text { get; set; }
            public string? Reasoning { get; set; }
            public string? StopReason { get; set; }
            public ChatStreamingUsage? Usage { get; set; }
            public List<ToolCall> ToolCalls { get; } = new List<ToolCall>();
        }

        #endregion
    }
}
