namespace PolyPrompt.Clients
{
    using System.Diagnostics;
    using System.Globalization;
    using System.Text;
    using System.Text.Json;
    using PolyPrompt.Helpers;
    using PolyPrompt.Models;
    using PolyPrompt.Telemetry;
    using SyslogLogging;

    /// <summary>
    /// Base class for every HTTP API client. It owns the HTTP transport (endpoint, per-request credentials, timeouts,
    /// cancellation), recording of HTTP calls into <see cref="ClientBase.CallDetails"/>, and the HTTP connectivity probe.
    /// It declares no model operations: each capability (completions, embeddings, reranking, and so on) has its own base
    /// class that derives from this one, and each provider supplies one client per capability.
    /// </summary>
    public abstract class HttpClientBase : ClientBase
    {
        #region Private-Members

        /// <summary>
        /// Whether this instance created (and therefore owns and disposes) <see cref="_HttpClient"/>.
        /// False when the client was supplied by the caller, in which case the caller retains ownership.
        /// </summary>
        private readonly bool _OwnsHttpClient;

        #endregion

        #region Protected-Members

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

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new client.
        /// </summary>
        /// <param name="endpoint">Endpoint URL.</param>
        /// <param name="apiKey">API key (nullable).</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="httpClient">
        /// Optional HTTP client to use for all requests. When supplied, the caller retains ownership and is
        /// responsible for disposing it; <see cref="ClientBase.Dispose"/> will not dispose an injected client. This lets
        /// callers configure the transport (for example a custom <see cref="System.Net.Http.HttpClientHandler"/>
        /// that relaxes TLS certificate validation, or a proxy), and lets several clients share one connection
        /// pool. When null, an internally owned client is created. The client's <see cref="HttpClient.Timeout"/>
        /// is set to <see cref="Timeout.InfiniteTimeSpan"/> (per-request timeouts are enforced via
        /// <see cref="ClientBase.TimeoutMs"/>); if an injected client has already sent a request its timeout cannot be
        /// changed and is left as configured, so callers sharing a client should give it an infinite timeout.
        /// Clients never modify <see cref="HttpClient.DefaultRequestHeaders"/>; credentials and provider headers
        /// are attached to each request, so a shared client is safe to use across providers.
        /// </param>
        /// <exception cref="ArgumentNullException">Thrown when endpoint or logging is null.</exception>
        protected HttpClientBase(string endpoint, string? apiKey, LoggingModule logging, HttpClient? httpClient = null)
            : base(logging)
        {
            _Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            _ApiKey = apiKey;
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
        /// Verify that the provider is reachable and accepts this client's credentials. Returns false on an HTTP
        /// error status, an unreachable server, or a request timeout; rethrows only cancellation requested through
        /// <paramref name="token"/>.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the provider responded successfully, false otherwise.</returns>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="token"/> is cancelled.</exception>
        public abstract Task<bool> ValidateConnectivityAsync(CancellationToken token = default);


        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose the HTTP client when this instance created it. An HTTP client supplied by the caller is not disposed.
        /// </summary>
        protected override void DisposeResources()
        {
            if (_OwnsHttpClient)
            {
                _HttpClient?.Dispose();
            }
        }

        /// <summary>
        /// Build a full URL from the endpoint and a path (with optional query string).
        /// </summary>
        /// <param name="pathAndQuery">Path beginning with a slash, for example <c>/v1/models</c>.</param>
        /// <returns>The absolute URL.</returns>
        protected string BuildUrl(string pathAndQuery)
        {
            return _Endpoint.TrimEnd('/') + pathAndQuery;
        }

        /// <summary>
        /// Send a GET to <paramref name="url"/> and report whether it succeeded. Used to implement
        /// <see cref="ValidateConnectivityAsync"/>. Returns false on an HTTP error status, a transport failure, or a
        /// timeout; rethrows only cancellation requested through <paramref name="token"/>.
        /// </summary>
        /// <param name="url">URL to probe.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the response has a success status code.</returns>
        protected async Task<bool> ProbeAsync(string url, CancellationToken token)
        {
            OperationScope? scope = OperationScope.Start(this, PolyPromptTelemetryNames.OperationValidateConnectivity, null, token);

            try
            {
                HttpCallResult result = await GetAndRecordAsync(url, token).ConfigureAwait(false);
                scope?.CompleteBool(result.IsSuccessStatusCode);
                return result.IsSuccessStatusCode;
            }
            catch (OperationCanceledException ex) when (token.IsCancellationRequested)
            {
                scope?.Fail(ex);
                throw;
            }
            catch (Exception ex)
            {
                _Logging.Debug(_Header + "connectivity probe failed: " + ex.Message);
                scope?.CompleteBool(false);
                return false;
            }
        }

        /// <summary>
        /// POST <paramref name="body"/> as JSON and fill <paramref name="response"/>: the status code, then either the
        /// HTTP error (status and body) or the result of <paramref name="parse"/>, and the elapsed time. A parser reports
        /// a malformed provider response by setting <see cref="ResponseBase.Error"/>; <see cref="ResponseBase.Success"/>
        /// is true when the status is successful and no error was set. Transport failures are reported on the response;
        /// cancellation and timeouts (<see cref="OperationCanceledException"/>) propagate.
        /// </summary>
        /// <typeparam name="TResponse">Response type.</typeparam>
        /// <param name="response">The response to fill (with its model already set).</param>
        /// <param name="operation">Operation name for log messages, for example "chat".</param>
        /// <param name="url">Full URL.</param>
        /// <param name="body">Request body.</param>
        /// <param name="parse">Parses a successful response body into the response.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The filled response.</returns>
        protected Task<TResponse> ExecutePostAsync<TResponse>(
            TResponse response,
            string operation,
            string url,
            object body,
            Action<string, TResponse> parse,
            CancellationToken token)
            where TResponse : ResponseBase
        {
            return ExecuteAsync(response, operation, "POST", url, t => PostJsonAsync(url, body, t), parse, token);
        }

        /// <summary>
        /// GET <paramref name="url"/> and fill <paramref name="response"/>. See
        /// <see cref="ExecutePostAsync{TResponse}"/>.
        /// </summary>
        /// <typeparam name="TResponse">Response type.</typeparam>
        /// <param name="response">The response to fill.</param>
        /// <param name="operation">Operation name for log messages.</param>
        /// <param name="url">Full URL.</param>
        /// <param name="parse">Parses a successful response body into the response.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The filled response.</returns>
        protected Task<TResponse> ExecuteGetAsync<TResponse>(
            TResponse response,
            string operation,
            string url,
            Action<string, TResponse> parse,
            CancellationToken token)
            where TResponse : ResponseBase
        {
            return ExecuteAsync(response, operation, "GET", url, t => GetAndRecordAsync(url, t), parse, token);
        }

        /// <summary>
        /// POST <paramref name="body"/> as JSON for a streaming response. On a successful status,
        /// <paramref name="attach"/> is called with the open stream and a stopwatch started before the request, and
        /// must set the response's chunk enumerable; the stream resources are then owned by that enumerable. On an
        /// HTTP error the body is read into <see cref="ResponseBase.Error"/> and the resources are released. Transport
        /// failures are reported on the response; cancellation and timeouts propagate.
        /// </summary>
        /// <typeparam name="TResponse">Streaming response type.</typeparam>
        /// <param name="response">The response to fill.</param>
        /// <param name="operation">Operation name for log messages.</param>
        /// <param name="url">Full URL.</param>
        /// <param name="body">Request body.</param>
        /// <param name="attach">Attaches the chunk enumerable to the response.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response, ready to enumerate when successful.</returns>
        protected async Task<TResponse> ExecuteStreamingAsync<TResponse>(
            TResponse response,
            string operation,
            string url,
            object body,
            Action<StreamingHttpResult, Stopwatch> attach,
            CancellationToken token)
            where TResponse : ResponseBase
        {
            _Logging.Debug(_Header + "POST (streaming " + operation + ") " + StripQuery(url));
            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                StreamingHttpResult streamingResult = await PostStreamingJsonAsync(url, body, token).ConfigureAwait(false);
                HttpResponseMessage httpResponse = streamingResult.Response;
                response.StatusCode = (int)httpResponse.StatusCode;

                if (!httpResponse.IsSuccessStatusCode)
                {
                    using (streamingResult)
                    {
                        string errorBody = await httpResponse.Content.ReadAsStringAsync(streamingResult.Token).ConfigureAwait(false);
                        _Logging.Warn(_Header + "streaming " + operation + " request failed with status " + (int)httpResponse.StatusCode + ": " + errorBody);
                        response.Success = false;
                        response.Error = "HTTP " + (int)httpResponse.StatusCode + ": " + errorBody;
                    }

                    sw.Stop();
                    response.OverallRuntimeMs = sw.ElapsedMilliseconds;
                    return response;
                }

                response.Success = true;
                attach(streamingResult, sw);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                OperationScope.Current?.ReportError(ex.GetType().FullName ?? ex.GetType().Name);
                _Logging.Warn(_Header + "streaming " + operation + " request failed: " + ex.Message);
                response.Success = false;
                response.Error = ex.Message;
                sw.Stop();
                response.OverallRuntimeMs = sw.ElapsedMilliseconds;
            }

            return response;
        }

        /// <summary>
        /// Send a GET to an upstream endpoint and record the call details.
        /// </summary>
        /// <param name="url">Full URL to call.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An HttpCallResult containing the response and body.</returns>
        protected async Task<HttpCallResult> GetAndRecordAsync(string url, CancellationToken token)
        {
            CallDetail detail = new CallDetail();
            detail.Url = url;
            detail.Method = "GET";
            detail.TimestampUtc = DateTime.UtcNow;

            Stopwatch sw = Stopwatch.StartNew();
            HttpCallScope? http = null;

            try
            {
                using CancellationTokenSource timeoutCts = new CancellationTokenSource(TimeoutMs);
                using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

                // Build an explicit request so subclasses can attach per-request credentials (a fresh bearer
                // token or a SigV4 signature) via PrepareRequestAsync. A GET carries no body, so the hook
                // signs the hash of an empty payload.
                using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
                await PrepareRequestAsync(request, Array.Empty<byte>(), linkedCts.Token).ConfigureAwait(false);
                detail.RequestHeaders = CaptureRequestHeaders(request);
                http = HttpCallScope.Start(TelemetryProvider, "GET", url, 0);

                using HttpResponseMessage response = await _HttpClient.SendAsync(request, linkedCts.Token).ConfigureAwait(false);
                string responseBody = await response.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);

                sw.Stop();
                http?.Complete((int)response.StatusCode, response.Content.Headers.ContentLength ?? Encoding.UTF8.GetByteCount(responseBody));
                return CompleteCall(detail, response, responseBody, sw);
            }
            catch (Exception ex)
            {
                http?.Fail(ex, token);
                sw.Stop();
                detail.ResponseTimeMs = sw.ElapsedMilliseconds;
                detail.Success = false;
                detail.Error = ex.Message;
                RecordCallDetail(detail);
                throw;
            }
        }

        /// <summary>
        /// Send an HTTP DELETE with an optional JSON body and record the call details.
        /// </summary>
        /// <param name="url">Full URL to call.</param>
        /// <param name="requestBodyJson">JSON body, or null for none.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An HttpCallResult containing the response and body.</returns>
        protected async Task<HttpCallResult> DeleteAndRecordAsync(string url, string? requestBodyJson, CancellationToken token)
        {
            CallDetail detail = new CallDetail();
            detail.Url = url;
            detail.Method = "DELETE";
            detail.RequestBody = requestBodyJson;
            detail.TimestampUtc = DateTime.UtcNow;

            Stopwatch sw = Stopwatch.StartNew();
            HttpCallScope? http = null;

            try
            {
                using CancellationTokenSource timeoutCts = new CancellationTokenSource(TimeoutMs);
                using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

                using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Delete, url);
                byte[] body = Array.Empty<byte>();
                if (requestBodyJson != null)
                {
                    request.Content = new StringContent(requestBodyJson, Encoding.UTF8, "application/json");
                    body = Encoding.UTF8.GetBytes(requestBodyJson);
                }

                await PrepareRequestAsync(request, body, linkedCts.Token).ConfigureAwait(false);
                detail.RequestHeaders = CaptureRequestHeaders(request);
                http = HttpCallScope.Start(TelemetryProvider, "DELETE", url, body.Length);

                using HttpResponseMessage response = await _HttpClient.SendAsync(request, linkedCts.Token).ConfigureAwait(false);
                string responseBody = await response.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);

                sw.Stop();
                http?.Complete((int)response.StatusCode, response.Content.Headers.ContentLength ?? Encoding.UTF8.GetByteCount(responseBody));
                return CompleteCall(detail, response, responseBody, sw);
            }
            catch (Exception ex)
            {
                http?.Fail(ex, token);
                sw.Stop();
                detail.ResponseTimeMs = sw.ElapsedMilliseconds;
                detail.Success = false;
                detail.Error = ex.Message;
                RecordCallDetail(detail);
                throw;
            }
        }

        /// <summary>
        /// Send an HTTP POST to an upstream endpoint and record the call details.
        /// </summary>
        /// <param name="url">Full URL to call.</param>
        /// <param name="content">HTTP content to send.</param>
        /// <param name="requestBodyJson">Request body as a JSON string (for recording and signing).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An HttpCallResult containing the response and body.</returns>
        protected async Task<HttpCallResult> PostAndRecordAsync(
            string url, StringContent content, string requestBodyJson, CancellationToken token)
        {
            CallDetail detail = new CallDetail();
            detail.Url = url;
            detail.Method = "POST";
            detail.RequestBody = requestBodyJson;
            detail.TimestampUtc = DateTime.UtcNow;

            Stopwatch sw = Stopwatch.StartNew();
            HttpCallScope? http = null;

            try
            {
                using CancellationTokenSource timeoutCts = new CancellationTokenSource(TimeoutMs);
                using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

                // Build an explicit request so subclasses can attach per-request credentials (a fresh bearer
                // token or a SigV4 signature computed over the body) via PrepareRequestAsync.
                using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Content = content;
                byte[] body = Encoding.UTF8.GetBytes(requestBodyJson ?? string.Empty);
                await PrepareRequestAsync(request, body, linkedCts.Token).ConfigureAwait(false);
                detail.RequestHeaders = CaptureRequestHeaders(request);
                http = HttpCallScope.Start(TelemetryProvider, "POST", url, body.Length);

                using HttpResponseMessage response = await _HttpClient.SendAsync(request, linkedCts.Token).ConfigureAwait(false);
                string responseBody = await response.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);

                sw.Stop();
                http?.Complete((int)response.StatusCode, response.Content.Headers.ContentLength ?? Encoding.UTF8.GetByteCount(responseBody));
                return CompleteCall(detail, response, responseBody, sw);
            }
            catch (Exception ex)
            {
                http?.Fail(ex, token);
                sw.Stop();
                detail.ResponseTimeMs = sw.ElapsedMilliseconds;
                detail.Success = false;
                detail.Error = ex.Message;
                RecordCallDetail(detail);
                throw;
            }
        }

        /// <summary>
        /// Serialize <paramref name="body"/> and POST it as JSON, recording the call details.
        /// </summary>
        /// <param name="url">Full URL to call.</param>
        /// <param name="body">Request body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An HttpCallResult containing the response and body.</returns>
        protected Task<HttpCallResult> PostJsonAsync(string url, object body, CancellationToken token)
        {
            string json = _Serializer.SerializeJson(body, false);
            StringContent content = new StringContent(json, Encoding.UTF8, "application/json");
            return PostAndRecordAsync(url, content, json, token);
        }

        /// <summary>
        /// Send an HTTP POST for streaming and return the response with its body unread. The timeout covers the
        /// whole response: <see cref="StreamingHttpResult.Token"/> is linked to both the caller token and
        /// <see cref="TimeoutMs"/>, and must be used while reading the body. Streaming calls are not recorded in
        /// <see cref="CallDetails"/>.
        /// </summary>
        /// <param name="url">Full URL to call.</param>
        /// <param name="content">HTTP content to send.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The HTTP response message with stream content, and the resources that must be disposed with it.</returns>
        protected async Task<StreamingHttpResult> PostStreamingAsync(
            string url, StringContent content, CancellationToken token)
        {
            CancellationTokenSource timeoutCts = new CancellationTokenSource(TimeoutMs);
            CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = content;
            HttpCallScope? http = null;

            try
            {
                // Attach per-request credentials (fresh bearer token or SigV4 signature over the body)
                // before the request is sent, mirroring the non-streaming path.
                byte[] body = await content.ReadAsByteArrayAsync(linkedCts.Token).ConfigureAwait(false);
                await PrepareRequestAsync(request, body, linkedCts.Token).ConfigureAwait(false);
                http = HttpCallScope.Start(TelemetryProvider, "POST", url, body.Length);

                HttpResponseMessage response = await _HttpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    linkedCts.Token).ConfigureAwait(false);

                http?.SetStatus((int)response.StatusCode);
                return new StreamingHttpResult(response, timeoutCts, linkedCts, request, http);
            }
            catch (Exception ex)
            {
                http?.Fail(ex, token);
                request.Dispose();
                linkedCts.Dispose();
                timeoutCts.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Serialize <paramref name="body"/> and POST it as JSON for streaming. See <see cref="PostStreamingAsync"/>.
        /// </summary>
        /// <param name="url">Full URL to call.</param>
        /// <param name="body">Request body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The streaming result.</returns>
        protected Task<StreamingHttpResult> PostStreamingJsonAsync(string url, object body, CancellationToken token)
        {
            string json = _Serializer.SerializeJson(body, false);
            StringContent content = new StringContent(json, Encoding.UTF8, "application/json");
            return PostStreamingAsync(url, content, token);
        }

        /// <summary>
        /// Hook invoked after an outbound <see cref="HttpRequestMessage"/> is built and its body serialized,
        /// immediately before the request is sent. Clients override this to attach credentials and provider
        /// headers to the request: a static API key header, a freshly refreshed OAuth bearer token, or an AWS
        /// SigV4 signature computed over <paramref name="body"/>. For a GET (or any request without a body)
        /// <paramref name="body"/> is an empty array, which for SigV4 hashes to the well-known empty-payload digest.
        /// The default implementation does nothing.
        /// </summary>
        /// <param name="request">The request about to be sent. Add or replace headers on it as needed.</param>
        /// <param name="body">The exact request body bytes (empty for bodyless requests).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task that completes once the request has been prepared.</returns>
        protected virtual Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Captures the effective request headers: the HTTP client's default headers overlaid with any headers
        /// (and content headers) attached to this specific request, so credentials added by
        /// <see cref="PrepareRequestAsync"/> are reflected in recorded <see cref="CallDetail"/>s.
        /// </summary>
        /// <param name="request">The outbound request.</param>
        /// <returns>The merged request headers.</returns>
        protected Dictionary<string, string> CaptureRequestHeaders(HttpRequestMessage request)
        {
            Dictionary<string, string> reqHeaders = new Dictionary<string, string>();

            foreach (KeyValuePair<string, IEnumerable<string>> header in _HttpClient.DefaultRequestHeaders)
            {
                reqHeaders[header.Key] = string.Join(", ", header.Value);
            }

            foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
            {
                reqHeaders[header.Key] = string.Join(", ", header.Value);
            }

            if (request.Content != null)
            {
                foreach (KeyValuePair<string, IEnumerable<string>> header in request.Content.Headers)
                {
                    reqHeaders[header.Key] = string.Join(", ", header.Value);
                }
            }

            return reqHeaders;
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
        /// Remove the query string from a URL, for logging URLs that carry an API key.
        /// </summary>
        /// <param name="url">URL.</param>
        /// <returns>The URL without its query string.</returns>
        protected static string StripQuery(string url)
        {
            int index = url.IndexOf('?');
            return index >= 0 ? url.Substring(0, index) : url;
        }

        /// <summary>
        /// Build an unsuccessful-response error message from an HTTP result.
        /// </summary>
        /// <param name="result">HTTP result.</param>
        /// <returns>A message that includes the status code and response body.</returns>
        protected static string DescribeHttpError(HttpCallResult result)
        {
            return "HTTP " + result.StatusCode + ": " + result.ResponseBody;
        }

        private protected Dictionary<string, object> ParseStreamingObject(string json, string provider)
        {
            Dictionary<string, object>? record = TryDeserializeObject(json);
            if (record == null) throw new InvalidDataException(provider + " stream contains malformed or non-object JSON.");
            if (record.TryGetValue("error", out object? error))
            {
                string diagnostic = error?.ToString() ?? "Unspecified provider error";
                if (!string.IsNullOrEmpty(_ApiKey)) diagnostic = diagnostic.Replace(_ApiKey, "[redacted]", StringComparison.Ordinal);
                diagnostic = diagnostic.Replace("\r", " ").Replace("\n", " ");
                if (diagnostic.Length > 1024) diagnostic = diagnostic.Substring(0, 1024);
                throw new InvalidDataException(provider + " provider error: " + diagnostic);
            }
            return record;
        }

        #endregion

        #region Private-Methods

        private async Task<TResponse> ExecuteAsync<TResponse>(
            TResponse response,
            string operation,
            string method,
            string url,
            Func<CancellationToken, Task<HttpCallResult>> send,
            Action<string, TResponse> parse,
            CancellationToken token)
            where TResponse : ResponseBase
        {
            _Logging.Debug(_Header + method + " " + StripQuery(url));
            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                HttpCallResult result = await send(token).ConfigureAwait(false);
                response.StatusCode = result.StatusCode;

                if (!result.IsSuccessStatusCode)
                {
                    _Logging.Warn(_Header + operation + " request failed with status " + result.StatusCode + ": " + result.ResponseBody);
                    response.Success = false;
                    response.Error = DescribeHttpError(result);
                    return response;
                }

                response.Error = null;
                parse(result.ResponseBody, response);
                response.Success = response.Error == null;
                if (!response.Success) _Logging.Warn(_Header + operation + " response could not be parsed: " + response.Error);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                OperationScope.Current?.ReportError(ex.GetType().FullName ?? ex.GetType().Name);
                _Logging.Warn(_Header + operation + " request failed: " + ex.Message);
                response.Success = false;
                response.Error = ex.Message;
            }
            finally
            {
                sw.Stop();
                response.OverallRuntimeMs = sw.ElapsedMilliseconds;
            }

            return response;
        }

        private HttpCallResult CompleteCall(CallDetail detail, HttpResponseMessage response, string responseBody, Stopwatch sw)
        {
            Dictionary<string, string> respHeaders = CaptureResponseHeaders(response);

            detail.StatusCode = (int)response.StatusCode;
            detail.ResponseTimeMs = sw.ElapsedMilliseconds;
            detail.ResponseBody = responseBody;
            detail.Success = response.IsSuccessStatusCode;
            detail.ResponseHeaders = respHeaders;

            RecordCallDetail(detail);

            HttpCallResult result = new HttpCallResult();
            result.Response = response;
            result.StatusCode = (int)response.StatusCode;
            result.IsSuccessStatusCode = response.IsSuccessStatusCode;
            result.ResponseHeaders = respHeaders;
            result.ResponseBody = responseBody;
            return result;
        }

        #endregion

        #region Protected-Classes

        /// <summary>
        /// Holds the HTTP response and timeout resources for a streaming request.
        /// </summary>
        protected sealed class StreamingHttpResult : IDisposable
        {
            private readonly CancellationTokenSource _TimeoutCts;
            private readonly CancellationTokenSource _LinkedCts;
            private readonly HttpRequestMessage _Request;
            private readonly HttpCallScope? _Telemetry;
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
                : this(response, timeoutCts, linkedCts, request, null)
            {
            }

            internal StreamingHttpResult(
                HttpResponseMessage response,
                CancellationTokenSource timeoutCts,
                CancellationTokenSource linkedCts,
                HttpRequestMessage request,
                HttpCallScope? telemetry)
            {
                Response = response;
                _TimeoutCts = timeoutCts;
                _LinkedCts = linkedCts;
                _Request = request;
                _Telemetry = telemetry;
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
                _Telemetry?.End();
            }
        }

        #endregion
    }
}
