namespace PolyPrompt.Clients
{
    using System.Diagnostics;
    using System.Runtime.CompilerServices;
    using PolyPrompt.Models;
    using SyslogLogging;

    /// <summary>
    /// Abstract base class for chat completion, embedding, and generation API clients.
    /// </summary>
    public abstract class CompletionClientBase : IDisposable
    {
        #region Private-Members

        private string _Model = "default";
        private int _MaxTokens = 4096;
        private int _TimeoutMs = 120000;
        private double? _Temperature = null;
        private double? _TopP = null;
        private ReasoningEffort? _ReasoningEffort = null;
        private string? _SystemPrompt = null;
        private readonly object _CallDetailsLock = new object();
        private readonly List<CompletionCallDetail> _CallDetails = new List<CompletionCallDetail>();
        private int _MaxCallDetails = 1000;

        #endregion

        #region Protected-Members

        /// <summary>
        /// Logging module.
        /// </summary>
        protected readonly LoggingModule _Logging;

        /// <summary>
        /// Endpoint URL.
        /// </summary>
        protected readonly string _Endpoint;

        /// <summary>
        /// API key (nullable).
        /// </summary>
        protected readonly string? _ApiKey;

        /// <summary>
        /// HTTP client for API requests.
        /// </summary>
        protected readonly HttpClient _HttpClient;

        /// <summary>
        /// Whether this instance created (and therefore owns and disposes) <see cref="_HttpClient"/>.
        /// False when the client was supplied by the caller, in which case the caller retains ownership.
        /// </summary>
        private readonly bool _OwnsHttpClient;

        /// <summary>
        /// Serializer instance.
        /// </summary>
        protected readonly SerializationHelper.Serializer _Serializer = new SerializationHelper.Serializer();

        /// <summary>
        /// Header prefix for log messages.
        /// </summary>
        protected string _Header = "[CompletionClient] ";

        #endregion

        #region Public-Members

        /// <summary>
        /// The endpoint URL for this client (read-only, set in constructor).
        /// </summary>
        public string Endpoint
        {
            get { return _Endpoint; }
        }

        /// <summary>
        /// The API key for this client (read-only, set in constructor). Returns null if not set.
        /// </summary>
        public string? ApiKey
        {
            get { return _ApiKey; }
        }

        /// <summary>
        /// Model name to use for completions.
        /// </summary>
        public string Model
        {
            get { return _Model; }
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    throw new ArgumentNullException(nameof(Model));
                _Model = value;
            }
        }

        /// <summary>
        /// Maximum number of tokens to generate. Clamped to 1..10,000,000.
        /// </summary>
        public int MaxTokens
        {
            get { return _MaxTokens; }
            set { _MaxTokens = Math.Clamp(value, 1, 10_000_000); }
        }

        /// <summary>
        /// HTTP request timeout in milliseconds. Must be greater than zero.
        /// </summary>
        public int TimeoutMs
        {
            get { return _TimeoutMs; }
            set
            {
                if (value <= 0) throw new ArgumentOutOfRangeException(nameof(TimeoutMs), "TimeoutMs must be greater than zero.");
                _TimeoutMs = value;
            }
        }

        /// <summary>
        /// Sampling temperature. Clamped to 0.0..2.0. Set to null to use the provider default.
        /// </summary>
        public double? Temperature
        {
            get { return _Temperature; }
            set
            {
                if (value.HasValue)
                    _Temperature = Math.Clamp(value.Value, 0.0, 2.0);
                else
                    _Temperature = null;
            }
        }

        /// <summary>
        /// Nucleus sampling (top-p). Clamped to 0.0..1.0. Set to null to use the provider default.
        /// </summary>
        public double? TopP
        {
            get { return _TopP; }
            set
            {
                if (value.HasValue)
                    _TopP = Math.Clamp(value.Value, 0.0, 1.0);
                else
                    _TopP = null;
            }
        }

        /// <summary>
        /// Default reasoning effort applied to tool chat requests that do not specify their own. Set to
        /// null to send no reasoning field unless a request supplies one.
        /// </summary>
        public ReasoningEffort? ReasoningEffort
        {
            get { return _ReasoningEffort; }
            set { _ReasoningEffort = value; }
        }

        /// <summary>
        /// System prompt to include with every chat completion. Set to null for none.
        /// </summary>
        public string? SystemPrompt
        {
            get { return _SystemPrompt; }
            set { _SystemPrompt = value; }
        }

        /// <summary>
        /// Recorded details of HTTP calls made to upstream completion endpoints.
        /// </summary>
        public List<CompletionCallDetail> CallDetails
        {
            get
            {
                lock (_CallDetailsLock)
                {
                    List<CompletionCallDetail> snapshot = new List<CompletionCallDetail>(_CallDetails.Count);
                    foreach (CompletionCallDetail detail in _CallDetails)
                    {
                        snapshot.Add(CloneCallDetail(detail));
                    }
                    return snapshot;
                }
            }
        }

        /// <summary>
        /// Maximum number of call details retained by this client. Set to zero to disable recording.
        /// </summary>
        public int MaxCallDetails
        {
            get
            {
                lock (_CallDetailsLock)
                {
                    return _MaxCallDetails;
                }
            }
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(MaxCallDetails), "MaxCallDetails cannot be negative.");
                lock (_CallDetailsLock)
                {
                    _MaxCallDetails = value;
                    TrimCallDetailsIfNeeded();
                }
            }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new CompletionClientBase.
        /// </summary>
        /// <param name="endpoint">Endpoint URL.</param>
        /// <param name="apiKey">API key (nullable).</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="httpClient">
        /// Optional HTTP client to use for all requests. When supplied, the caller retains ownership and is
        /// responsible for disposing it; <see cref="Dispose"/> will not dispose an injected client. This lets
        /// callers configure the transport (for example a custom <see cref="System.Net.Http.HttpClientHandler"/>
        /// that relaxes TLS certificate validation, or a proxy). When null, an internally owned client is
        /// created. The client's <see cref="HttpClient.Timeout"/> is set to
        /// <see cref="Timeout.InfiniteTimeSpan"/> (per-request timeouts are enforced via
        /// <see cref="TimeoutMs"/>); if an injected client has already sent a request its timeout cannot be
        /// changed and is left as configured, so callers sharing a client should give it an infinite timeout.
        /// </param>
        protected CompletionClientBase(string endpoint, string? apiKey, LoggingModule logging, HttpClient? httpClient = null)
        {
            _Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            _ApiKey = apiKey;
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _OwnsHttpClient = httpClient is null;
            _HttpClient = httpClient ?? new HttpClient();

            // Per-request timeouts are enforced via TimeoutMs, so the transport's own timeout must be
            // disabled. Setting Timeout throws once a client has already sent a request; for an injected
            // client that has been used (for example, shared across clients) we leave its timeout as the
            // caller configured it.
            if (_HttpClient.Timeout != Timeout.InfiniteTimeSpan)
            {
                try
                {
                    _HttpClient.Timeout = Timeout.InfiniteTimeSpan;
                }
                catch (InvalidOperationException)
                {
                }
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Send a chat completion request and return the full response.
        /// </summary>
        /// <param name="prompt">User message.</param>
        /// <param name="options">Optional per-call overrides.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A ChatResponse containing the completion text, metadata, and timing.</returns>
        public abstract Task<ChatResponse> ChatAsync(
            string prompt,
            ChatCompletionOptions? options = null,
            CancellationToken token = default);

        /// <summary>
        /// Send a chat completion request and stream response chunks as they arrive.
        /// </summary>
        /// <param name="prompt">User message.</param>
        /// <param name="options">Optional per-call overrides.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A ChatStreamingResponse containing metadata and an async enumerable of chunks.</returns>
        public abstract Task<ChatStreamingResponse> ChatStreamingAsync(
            string prompt,
            ChatCompletionOptions? options = null,
            CancellationToken token = default);

        /// <summary>
        /// Send a tool-capable chat completion request and return text and/or tool calls from the model.
        /// Tool execution is owned by the caller; append ToAssistantMessage and tool-result messages to continue.
        /// </summary>
        /// <param name="request">Tool chat request containing messages and tool definitions.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A ToolChatResponse containing assistant text and requested tool calls.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="ArgumentException">Thrown when request.Messages is empty.</exception>
        public abstract Task<ToolChatResponse> ToolChatAsync(
            ToolChatRequest request,
            CancellationToken token = default);

        /// <summary>
        /// Send a tool-capable chat completion request and stream response chunks as they arrive.
        /// Tool execution is owned by the caller; enumerate Chunks, then append ToAssistantMessage and tool-result messages to continue.
        /// </summary>
        /// <param name="request">Tool chat request containing messages and tool definitions.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A ToolChatStreamingResponse containing metadata, streamed chunks, accumulated text, and requested tool calls.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="ArgumentException">Thrown when request.Messages is empty.</exception>
        public abstract Task<ToolChatStreamingResponse> ToolChatStreamingAsync(
            ToolChatRequest request,
            CancellationToken token = default);

        /// <summary>
        /// Generate an embedding for a single input string.
        /// </summary>
        /// <param name="input">The text to embed.</param>
        /// <param name="options">Optional per-call overrides.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An EmbeddingResponse containing the embedding vector.</returns>
        public abstract Task<EmbeddingResponse> EmbedAsync(
            string input,
            EmbeddingOptions? options = null,
            CancellationToken token = default);

        /// <summary>
        /// Generate embeddings for a batch of input strings.
        /// </summary>
        /// <param name="inputs">The list of texts to embed.</param>
        /// <param name="options">Optional per-call overrides.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An EmbeddingResponse containing the embedding vectors.</returns>
        public abstract Task<EmbeddingResponse> EmbedAsync(
            List<string> inputs,
            EmbeddingOptions? options = null,
            CancellationToken token = default);

        /// <summary>
        /// Generate text from a prompt (non-streaming).
        /// </summary>
        /// <param name="prompt">The prompt text.</param>
        /// <param name="options">Optional per-call overrides.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A GenerationResponse containing the generated text.</returns>
        public abstract Task<GenerationResponse> GenerateAsync(
            string prompt,
            GenerationOptions? options = null,
            CancellationToken token = default);

        /// <summary>
        /// Generate text from a prompt with streaming.
        /// </summary>
        /// <param name="prompt">The prompt text.</param>
        /// <param name="options">Optional per-call overrides.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A GenerationStreamingResponse containing an async enumerable of chunks.</returns>
        public abstract Task<GenerationStreamingResponse> GenerateStreamingAsync(
            string prompt,
            GenerationOptions? options = null,
            CancellationToken token = default);

        /// <summary>
        /// List available models from the provider.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An async enumerable of ModelInformation objects.</returns>
        public abstract IAsyncEnumerable<ModelInformation> ListModelsAsync(CancellationToken token = default);

        /// <summary>
        /// Check if a specific model exists on the provider.
        /// </summary>
        /// <param name="model">The model name to search for.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the model exists, false otherwise.</returns>
        /// <exception cref="ArgumentNullException">Thrown when model is null or empty.</exception>
        public virtual async Task<bool> ModelExistsAsync(string model, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(model))
                throw new ArgumentNullException(nameof(model));

            try
            {
                await foreach (ModelInformation info in ListModelsAsync(token).ConfigureAwait(false))
                {
                    token.ThrowIfCancellationRequested();

                    if (string.Equals(info.Name, model, StringComparison.OrdinalIgnoreCase))
                        return true;

                    // Match without tag, e.g. "gemma3" matches "gemma3:latest"
                    int colonIndex = info.Name.IndexOf(':');
                    if (colonIndex > 0)
                    {
                        string baseName = info.Name.Substring(0, colonIndex);
                        if (string.Equals(baseName, model, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }

                return false;
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

        /// <summary>
        /// Validate connectivity to the provider by attempting to list models.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the provider is reachable and responded successfully, false otherwise.</returns>
        public virtual async Task<bool> ValidateConnectivityAsync(CancellationToken token = default)
        {
            try
            {
                await foreach (ModelInformation model in ListModelsAsync(token).ConfigureAwait(false))
                {
                    return true;
                }
                return true;
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

        /// <summary>
        /// Pull (download) a model from the provider. Invokes the progress callback as download progresses.
        /// Not all providers support model pulling; the default implementation throws NotSupportedException.
        /// </summary>
        /// <param name="model">The model name to pull.</param>
        /// <param name="progress">Callback invoked with progress updates. May be null to ignore progress.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the model was pulled successfully, false otherwise.</returns>
        /// <exception cref="NotSupportedException">Thrown when the provider does not support model pulling.</exception>
        /// <exception cref="ArgumentNullException">Thrown when model is null or empty.</exception>
        public virtual Task<bool> PullModelAsync(
            string model,
            Func<ModelPullProgress, Task>? progress = null,
            CancellationToken token = default)
        {
            throw new NotSupportedException("This provider does not support pulling models.");
        }

        /// <summary>
        /// Delete a model from the provider.
        /// Not all providers support model deletion; the default implementation throws NotSupportedException.
        /// </summary>
        /// <param name="model">The model name to delete.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the model was deleted successfully, false otherwise.</returns>
        /// <exception cref="NotSupportedException">Thrown when the provider does not support model deletion.</exception>
        /// <exception cref="ArgumentNullException">Thrown when model is null or empty.</exception>
        public virtual Task<bool> DeleteModelAsync(string model, CancellationToken token = default)
        {
            throw new NotSupportedException("This provider does not support deleting models.");
        }

        /// <summary>
        /// Retrieve detailed information about a specific model.
        /// </summary>
        /// <param name="model">The model name to look up.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A ModelInformation object if found, null otherwise.</returns>
        /// <exception cref="ArgumentNullException">Thrown when model is null or empty.</exception>
        public abstract Task<ModelInformation?> GetModelInformationAsync(string model, CancellationToken token = default);

        /// <summary>
        /// Dispose of HTTP client resources.
        /// </summary>
        public void Dispose()
        {
            if (_OwnsHttpClient)
            {
                _HttpClient?.Dispose();
            }

            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Clear all retained call details.
        /// </summary>
        public void ClearCallDetails()
        {
            lock (_CallDetailsLock)
            {
                _CallDetails.Clear();
            }
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Send an HTTP GET to an upstream endpoint and record the call details.
        /// </summary>
        /// <param name="url">Full URL to call.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A CompletionHttpResult containing the response and body.</returns>
        protected async Task<CompletionHttpResult> GetAndRecordAsync(string url, CancellationToken token)
        {
            CompletionCallDetail detail = new CompletionCallDetail();
            detail.Url = url;
            detail.Method = "GET";
            detail.TimestampUtc = DateTime.UtcNow;

            Dictionary<string, string> reqHeaders = new Dictionary<string, string>();
            foreach (KeyValuePair<string, IEnumerable<string>> header in _HttpClient.DefaultRequestHeaders)
            {
                reqHeaders[header.Key] = string.Join(", ", header.Value);
            }
            detail.RequestHeaders = reqHeaders;

            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                using CancellationTokenSource timeoutCts = new CancellationTokenSource(_TimeoutMs);
                using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

                using HttpResponseMessage response = await _HttpClient.GetAsync(url, linkedCts.Token).ConfigureAwait(false);
                string responseBody = await response.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);

                sw.Stop();

                Dictionary<string, string> respHeaders = CaptureResponseHeaders(response);

                detail.StatusCode = (int)response.StatusCode;
                detail.ResponseTimeMs = sw.ElapsedMilliseconds;
                detail.ResponseBody = responseBody;
                detail.Success = response.IsSuccessStatusCode;
                detail.ResponseHeaders = respHeaders;

                RecordCallDetail(detail);

                CompletionHttpResult result = new CompletionHttpResult();
                result.Response = response;
                result.StatusCode = (int)response.StatusCode;
                result.IsSuccessStatusCode = response.IsSuccessStatusCode;
                result.ResponseHeaders = respHeaders;
                result.ResponseBody = responseBody;
                return result;
            }
            catch (Exception ex)
            {
                sw.Stop();
                detail.ResponseTimeMs = sw.ElapsedMilliseconds;
                detail.Success = false;
                detail.Error = ex.Message;
                RecordCallDetail(detail);
                throw;
            }
        }

        /// <summary>
        /// Resolve effective values from per-call options and instance defaults.
        /// </summary>
        /// <param name="options">Per-call options (nullable).</param>
        /// <param name="maxTokens">Resolved max tokens.</param>
        /// <param name="temperature">Resolved temperature.</param>
        /// <param name="topP">Resolved top-p.</param>
        /// <param name="systemPrompt">Resolved system prompt.</param>
        protected void ResolveOptions(
            ChatCompletionOptions? options,
            out int maxTokens,
            out double? temperature,
            out double? topP,
            out string? systemPrompt)
        {
            maxTokens = options?.MaxTokens ?? _MaxTokens;
            temperature = options?.Temperature ?? _Temperature;
            topP = options?.TopP ?? _TopP;
            systemPrompt = options?.SystemPrompt ?? _SystemPrompt;
        }

        /// <summary>
        /// Resolve effective values from a tool chat request and instance defaults.
        /// </summary>
        /// <param name="request">Tool chat request.</param>
        /// <param name="model">Resolved model.</param>
        /// <param name="maxTokens">Resolved max tokens.</param>
        /// <param name="temperature">Resolved temperature.</param>
        /// <param name="topP">Resolved top-p.</param>
        /// <param name="reasoningEffort">Resolved reasoning effort, or null when none applies.</param>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="ArgumentException">Thrown when request.Messages is empty.</exception>
        protected void ResolveToolChatRequest(
            ToolChatRequest request,
            out string model,
            out int maxTokens,
            out double? temperature,
            out double? topP,
            out ReasoningEffort? reasoningEffort)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (request.Messages == null || request.Messages.Count == 0)
                throw new ArgumentException("Tool chat requests require at least one message.", nameof(request));

            model = request.Model ?? _Model;
            maxTokens = request.MaxTokens ?? _MaxTokens;
            temperature = request.Temperature ?? _Temperature;
            topP = request.TopP ?? _TopP;
            reasoningEffort = request.ReasoningEffort ?? _ReasoningEffort;
        }

        /// <summary>
        /// Resolve effective values from per-call generation options and instance defaults.
        /// </summary>
        /// <param name="options">Per-call options (nullable).</param>
        /// <param name="model">Resolved model name.</param>
        /// <param name="maxTokens">Resolved max tokens.</param>
        /// <param name="temperature">Resolved temperature.</param>
        /// <param name="topP">Resolved top-p.</param>
        protected void ResolveGenerationOptions(
            GenerationOptions? options,
            out string model,
            out int maxTokens,
            out double? temperature,
            out double? topP)
        {
            model = options?.Model ?? _Model;
            maxTokens = options?.MaxTokens ?? _MaxTokens;
            temperature = options?.Temperature ?? _Temperature;
            topP = options?.TopP ?? _TopP;
        }

        /// <summary>
        /// Send an HTTP POST to an upstream endpoint and record the call details.
        /// </summary>
        /// <param name="url">Full URL to call.</param>
        /// <param name="content">HTTP content to send.</param>
        /// <param name="requestBodyJson">Request body as a JSON string (for recording).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A CompletionHttpResult containing the response and body.</returns>
        protected async Task<CompletionHttpResult> PostAndRecordAsync(
            string url, StringContent content, string requestBodyJson, CancellationToken token)
        {
            CompletionCallDetail detail = new CompletionCallDetail();
            detail.Url = url;
            detail.Method = "POST";
            detail.RequestBody = requestBodyJson;
            detail.TimestampUtc = DateTime.UtcNow;

            Dictionary<string, string> reqHeaders = new Dictionary<string, string>();
            foreach (KeyValuePair<string, IEnumerable<string>> header in _HttpClient.DefaultRequestHeaders)
            {
                reqHeaders[header.Key] = string.Join(", ", header.Value);
            }
            if (content.Headers.ContentType != null)
            {
                reqHeaders["Content-Type"] = content.Headers.ContentType.ToString();
            }
            detail.RequestHeaders = reqHeaders;

            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                using CancellationTokenSource timeoutCts = new CancellationTokenSource(_TimeoutMs);
                using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

                using HttpResponseMessage response = await _HttpClient.PostAsync(url, content, linkedCts.Token).ConfigureAwait(false);
                string responseBody = await response.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);

                sw.Stop();

                Dictionary<string, string> respHeaders = CaptureResponseHeaders(response);

                detail.StatusCode = (int)response.StatusCode;
                detail.ResponseTimeMs = sw.ElapsedMilliseconds;
                detail.ResponseBody = responseBody;
                detail.Success = response.IsSuccessStatusCode;
                detail.ResponseHeaders = respHeaders;

                RecordCallDetail(detail);

                CompletionHttpResult result = new CompletionHttpResult();
                result.Response = response;
                result.StatusCode = (int)response.StatusCode;
                result.IsSuccessStatusCode = response.IsSuccessStatusCode;
                result.ResponseHeaders = respHeaders;
                result.ResponseBody = responseBody;
                return result;
            }
            catch (Exception ex)
            {
                sw.Stop();
                detail.ResponseTimeMs = sw.ElapsedMilliseconds;
                detail.Success = false;
                detail.Error = ex.Message;
                RecordCallDetail(detail);
                throw;
            }
        }

        /// <summary>
        /// Wrap a raw chunk enumerable with timing-tracking logic that updates the response object.
        /// </summary>
        /// <param name="response">The streaming response object to update with timing.</param>
        /// <param name="rawChunks">The raw async enumerable of chunks from the provider.</param>
        /// <param name="sw">Stopwatch started before the HTTP request was made.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="owner">Optional disposable owner released when enumeration ends.</param>
        /// <returns>A wrapped async enumerable that updates timing on the response as chunks flow through.</returns>
        protected async IAsyncEnumerable<ChatStreamingChunk> WrapChunksWithTiming(
            ChatStreamingResponse response,
            IAsyncEnumerable<ChatStreamingChunk> rawChunks,
            Stopwatch sw,
            [EnumeratorCancellation] CancellationToken token = default,
            IDisposable? owner = null)
        {
            try
            {
                await foreach (ChatStreamingChunk chunk in rawChunks.WithCancellation(token).ConfigureAwait(false))
                {
                    if (!string.IsNullOrEmpty(chunk.Text))
                    {
                        if (response.ChunkCount == 0)
                        {
                            response.TimeToFirstTokenMs = sw.ElapsedMilliseconds;
                        }
                        response.ChunkCount++;
                        response.TimeToLastTokenMs = sw.ElapsedMilliseconds;
                    }

                    if (chunk.Usage != null) response.Usage = chunk.Usage;
                    if (chunk.FinishReason != null) response.FinishReason = chunk.FinishReason;
                    if (chunk.ResponseId != null) response.ResponseId = chunk.ResponseId;

                    yield return chunk;
                }

                sw.Stop();
                response.OverallRuntimeMs = sw.ElapsedMilliseconds;

                int tokenCount = response.Usage?.CompletionTokens ?? response.ChunkCount;

                if (tokenCount > 0 && response.OverallRuntimeMs > 0)
                {
                    response.OverallTokensPerSecond = tokenCount / (response.OverallRuntimeMs / 1000.0);
                }

                if (tokenCount > 0 && response.TimeToLastTokenMs > response.TimeToFirstTokenMs)
                {
                    double interTokenSec = (response.TimeToLastTokenMs - response.TimeToFirstTokenMs) / 1000.0;
                    response.InterTokenTokensPerSecond = tokenCount / interTokenSec;
                }
            }
            finally
            {
                if (sw.IsRunning)
                {
                    sw.Stop();
                    response.OverallRuntimeMs = sw.ElapsedMilliseconds;
                }
                owner?.Dispose();
            }
        }

        /// <summary>
        /// Wrap a raw tool-chat chunk enumerable with timing and accumulation logic.
        /// </summary>
        /// <param name="response">The streaming response object to update.</param>
        /// <param name="rawChunks">The raw async enumerable of chunks from the provider.</param>
        /// <param name="sw">Stopwatch started before the HTTP request was made.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="owner">Optional disposable owner released when enumeration ends.</param>
        /// <returns>A wrapped async enumerable that updates timing and accumulated output on the response.</returns>
        protected async IAsyncEnumerable<ToolChatStreamingChunk> WrapToolChatChunksWithTiming(
            ToolChatStreamingResponse response,
            IAsyncEnumerable<ToolChatStreamingChunk> rawChunks,
            Stopwatch sw,
            [EnumeratorCancellation] CancellationToken token = default,
            IDisposable? owner = null)
        {
            Dictionary<int, ToolCallAssembly> toolCalls = new Dictionary<int, ToolCallAssembly>();

            try
            {
                await foreach (ToolChatStreamingChunk chunk in rawChunks.WithCancellation(token).ConfigureAwait(false))
                {
                    bool hasText = !string.IsNullOrEmpty(chunk.Text);
                    bool hasToolDeltas = chunk.ToolCallDeltas != null && chunk.ToolCallDeltas.Count > 0;

                    if (hasText || hasToolDeltas)
                    {
                        if (response.ChunkCount == 0)
                        {
                            response.TimeToFirstTokenMs = sw.ElapsedMilliseconds;
                        }
                        response.ChunkCount++;
                        response.TimeToLastTokenMs = sw.ElapsedMilliseconds;
                    }

                    if (hasText)
                    {
                        response.Text = string.IsNullOrEmpty(response.Text)
                            ? chunk.Text
                            : response.Text + chunk.Text;
                    }

                    if (hasToolDeltas)
                    {
                        response.ToolCallDeltaCount += chunk.ToolCallDeltas.Count;

                        foreach (ToolCallDelta delta in chunk.ToolCallDeltas)
                        {
                            if (!toolCalls.TryGetValue(delta.Index, out ToolCallAssembly? assembly))
                            {
                                assembly = new ToolCallAssembly(delta.Index);
                                toolCalls[delta.Index] = assembly;
                            }

                            assembly.Apply(delta);
                        }

                        RefreshToolCalls(response, toolCalls);
                    }

                    if (chunk.Usage != null) response.Usage = chunk.Usage;
                    if (chunk.FinishReason != null) response.FinishReason = chunk.FinishReason;
                    if (chunk.ResponseId != null) response.ResponseId = chunk.ResponseId;
                    if (chunk.Model != null) response.Model = chunk.Model;

                    yield return chunk;
                }

                sw.Stop();
                response.OverallRuntimeMs = sw.ElapsedMilliseconds;

                int tokenCount = response.Usage?.CompletionTokens ?? response.ChunkCount;

                if (tokenCount > 0 && response.OverallRuntimeMs > 0)
                {
                    response.OverallTokensPerSecond = tokenCount / (response.OverallRuntimeMs / 1000.0);
                }

                if (tokenCount > 0 && response.TimeToLastTokenMs > response.TimeToFirstTokenMs)
                {
                    double interTokenSec = (response.TimeToLastTokenMs - response.TimeToFirstTokenMs) / 1000.0;
                    response.InterTokenTokensPerSecond = tokenCount / interTokenSec;
                }
            }
            finally
            {
                if (sw.IsRunning)
                {
                    sw.Stop();
                    response.OverallRuntimeMs = sw.ElapsedMilliseconds;
                }
                owner?.Dispose();
            }
        }

        /// <summary>
        /// Wrap a raw generation chunk enumerable with timing-tracking logic.
        /// </summary>
        /// <param name="response">The streaming response object to update with timing.</param>
        /// <param name="rawChunks">The raw async enumerable of generation chunks.</param>
        /// <param name="sw">Stopwatch started before the HTTP request was made.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="owner">Optional disposable owner released when enumeration ends.</param>
        /// <returns>A wrapped async enumerable that updates timing on the response as chunks flow through.</returns>
        protected async IAsyncEnumerable<GenerationStreamingChunk> WrapGenerationChunksWithTiming(
            GenerationStreamingResponse response,
            IAsyncEnumerable<GenerationStreamingChunk> rawChunks,
            Stopwatch sw,
            [EnumeratorCancellation] CancellationToken token = default,
            IDisposable? owner = null)
        {
            try
            {
                await foreach (GenerationStreamingChunk chunk in rawChunks.WithCancellation(token).ConfigureAwait(false))
                {
                    if (!string.IsNullOrEmpty(chunk.Text))
                    {
                        if (response.ChunkCount == 0)
                        {
                            response.TimeToFirstTokenMs = sw.ElapsedMilliseconds;
                        }
                        response.ChunkCount++;
                        response.TimeToLastTokenMs = sw.ElapsedMilliseconds;
                    }

                    yield return chunk;
                }

                sw.Stop();
                response.OverallRuntimeMs = sw.ElapsedMilliseconds;

                if (response.ChunkCount > 0 && response.OverallRuntimeMs > 0)
                {
                    response.OverallTokensPerSecond = response.ChunkCount / (response.OverallRuntimeMs / 1000.0);
                }

                if (response.ChunkCount > 0 && response.TimeToLastTokenMs > response.TimeToFirstTokenMs)
                {
                    double interTokenSec = (response.TimeToLastTokenMs - response.TimeToFirstTokenMs) / 1000.0;
                    response.InterTokenTokensPerSecond = response.ChunkCount / interTokenSec;
                }
            }
            finally
            {
                if (sw.IsRunning)
                {
                    sw.Stop();
                    response.OverallRuntimeMs = sw.ElapsedMilliseconds;
                }
                owner?.Dispose();
            }
        }

        /// <summary>
        /// Send an HTTP POST for streaming and return the response stream.
        /// </summary>
        /// <param name="url">Full URL to call.</param>
        /// <param name="content">HTTP content to send.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The HTTP response message with stream content.</returns>
        protected async Task<StreamingHttpResult> PostStreamingAsync(
            string url, StringContent content, CancellationToken token)
        {
            CancellationTokenSource timeoutCts = new CancellationTokenSource(_TimeoutMs);
            CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = content;

            try
            {
                HttpResponseMessage response = await _HttpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    linkedCts.Token).ConfigureAwait(false);

                return new StreamingHttpResult(response, timeoutCts, linkedCts, request);
            }
            catch
            {
                request.Dispose();
                linkedCts.Dispose();
                timeoutCts.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Captures response and content headers into a retained dictionary.
        /// </summary>
        /// <param name="response">HTTP response.</param>
        /// <returns>Response headers.</returns>
        protected Dictionary<string, string> CaptureResponseHeaders(HttpResponseMessage response)
        {
            Dictionary<string, string> respHeaders = new Dictionary<string, string>();
            foreach (KeyValuePair<string, IEnumerable<string>> header in response.Headers)
            {
                respHeaders[header.Key] = string.Join(", ", header.Value);
            }
            foreach (KeyValuePair<string, IEnumerable<string>> header in response.Content.Headers)
            {
                respHeaders[header.Key] = string.Join(", ", header.Value);
            }
            return respHeaders;
        }

        /// <summary>
        /// Record a call detail while enforcing the configured retention limit.
        /// </summary>
        /// <param name="detail">Call detail to record.</param>
        protected void RecordCallDetail(CompletionCallDetail detail)
        {
            lock (_CallDetailsLock)
            {
                if (_MaxCallDetails == 0) return;

                _CallDetails.Add(CloneCallDetail(detail));
                TrimCallDetailsIfNeeded();
            }
        }

        /// <summary>
        /// Clone a call detail so callers cannot mutate retained state.
        /// </summary>
        /// <param name="detail">Call detail.</param>
        /// <returns>A detached copy.</returns>
        protected CompletionCallDetail CloneCallDetail(CompletionCallDetail detail)
        {
            CompletionCallDetail clone = new CompletionCallDetail();
            clone.Url = detail.Url;
            clone.Method = detail.Method;
            clone.RequestHeaders = detail.RequestHeaders != null ? new Dictionary<string, string>(detail.RequestHeaders) : new Dictionary<string, string>();
            clone.RequestBody = detail.RequestBody;
            clone.StatusCode = detail.StatusCode;
            clone.ResponseHeaders = detail.ResponseHeaders != null ? new Dictionary<string, string>(detail.ResponseHeaders) : new Dictionary<string, string>();
            clone.ResponseBody = detail.ResponseBody;
            clone.ResponseTimeMs = detail.ResponseTimeMs;
            clone.Success = detail.Success;
            clone.Error = detail.Error;
            clone.TimestampUtc = detail.TimestampUtc;
            return clone;
        }

        private void TrimCallDetailsIfNeeded()
        {
            if (_MaxCallDetails == 0)
            {
                _CallDetails.Clear();
                return;
            }

            int excess = _CallDetails.Count - _MaxCallDetails;
            if (excess > 0)
            {
                _CallDetails.RemoveRange(0, excess);
            }
        }

        private static void RefreshToolCalls(
            ToolChatStreamingResponse response,
            Dictionary<int, ToolCallAssembly> toolCallAssemblies)
        {
            response.ToolCalls.Clear();

            foreach (ToolCallAssembly assembly in toolCallAssemblies.Values.OrderBy(item => item.Index))
            {
                if (!string.IsNullOrWhiteSpace(assembly.Name))
                {
                    response.ToolCalls.Add(assembly.ToToolCall());
                }
            }
        }

        /// <summary>
        /// Holds the HTTP response and timeout resources for a streaming request.
        /// </summary>
        protected sealed class StreamingHttpResult : IDisposable
        {
            private readonly CancellationTokenSource _TimeoutCts;
            private readonly CancellationTokenSource _LinkedCts;
            private readonly HttpRequestMessage _Request;
            private bool _Disposed = false;

            /// <summary>
            /// HTTP response message.
            /// </summary>
            public HttpResponseMessage Response { get; }

            /// <summary>
            /// Token linked to the caller token and the configured timeout.
            /// </summary>
            public CancellationToken Token => _LinkedCts.Token;

            /// <summary>
            /// Initialize a streaming result.
            /// </summary>
            /// <param name="response">HTTP response.</param>
            /// <param name="timeoutCts">Timeout cancellation source.</param>
            /// <param name="linkedCts">Linked cancellation source.</param>
            /// <param name="request">HTTP request.</param>
            public StreamingHttpResult(
                HttpResponseMessage response,
                CancellationTokenSource timeoutCts,
                CancellationTokenSource linkedCts,
                HttpRequestMessage request)
            {
                Response = response;
                _TimeoutCts = timeoutCts;
                _LinkedCts = linkedCts;
                _Request = request;
            }

            /// <summary>
            /// Dispose streaming request resources.
            /// </summary>
            public void Dispose()
            {
                if (_Disposed) return;
                _Disposed = true;

                Response.Dispose();
                _Request.Dispose();
                _LinkedCts.Dispose();
                _TimeoutCts.Dispose();
            }
        }

        /// <summary>
        /// Parse a float array from a JSON array element.
        /// </summary>
        /// <param name="arrayJson">The JSON string representing the array.</param>
        /// <returns>An array of floats.</returns>
        protected float[] ParseFloatArray(string arrayJson)
        {
            List<object>? values = _Serializer.DeserializeJson<List<object>>(arrayJson);
            if (values == null) return Array.Empty<float>();

            float[] result = new float[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                string? val = values[i]?.ToString();
                if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f))
                {
                    result[i] = f;
                }
            }
            return result;
        }

        /// <summary>
        /// Try to parse an integer from a dictionary value.
        /// </summary>
        /// <param name="dict">The dictionary.</param>
        /// <param name="key">The key to look up.</param>
        /// <returns>The integer value or null.</returns>
        protected int? TryGetInt(Dictionary<string, object> dict, string key)
        {
            if (!dict.ContainsKey(key)) return null;
            string? val = dict[key]?.ToString();
            if (int.TryParse(val, out int result)) return result;
            return null;
        }

        /// <summary>
        /// Try to parse a long from a dictionary value.
        /// </summary>
        /// <param name="dict">The dictionary.</param>
        /// <param name="key">The key to look up.</param>
        /// <returns>The long value or null.</returns>
        protected long? TryGetLong(Dictionary<string, object> dict, string key)
        {
            if (!dict.ContainsKey(key)) return null;
            string? val = dict[key]?.ToString();
            if (long.TryParse(val, out long result)) return result;
            return null;
        }

        /// <summary>
        /// Check if a dictionary value represents a truthy boolean.
        /// Handles bool objects, strings ("true"/"True"), and numeric values (non-zero).
        /// </summary>
        /// <param name="dict">The dictionary.</param>
        /// <param name="key">The key to look up.</param>
        /// <returns>True if the value is truthy, false otherwise.</returns>
        protected bool IsTruthy(Dictionary<string, object> dict, string key)
        {
            if (!dict.ContainsKey(key)) return false;
            object? value = dict[key];
            if (value == null) return false;
            if (value is bool b) return b;
            string? str = value.ToString();
            if (string.IsNullOrEmpty(str)) return false;
            if (string.Equals(str, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(str, "True", StringComparison.Ordinal)) return true;
            if (long.TryParse(str, out long num)) return num != 0;
            return false;
        }

        #endregion
    }
}
