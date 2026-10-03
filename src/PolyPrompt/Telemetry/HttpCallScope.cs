namespace PolyPrompt.Telemetry
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.Threading;

    /// <summary>
    /// Telemetry for one outbound HTTP request to a provider: a client span named after the HTTP method, the request
    /// duration and count metrics, request and response body sizes, and the in-flight gauge. The span is current while
    /// the request is sent, so HttpClient propagates its W3C <c>traceparent</c> to the provider. Errors are also reported
    /// to the enclosing <see cref="OperationScope"/>. Every member is best-effort and never throws.
    /// Thread safety: a scope is owned by one request; ending it is idempotent and safe from any thread.
    /// </summary>
    internal sealed class HttpCallScope
    {
        #region Private-Members

        private readonly long _StartTimestamp;
        private readonly string _Provider;
        private readonly string _Method;
        private readonly string? _Host;
        private readonly int _Port;
        private readonly InFlightEntry _ActiveEntry;
        private readonly OperationScope? _Operation;
        private int? _StatusCode = null;
        private int _Ended = 0;

        #endregion

        #region Public-Members

        /// <summary>
        /// The request's span, or null when no trace listener sampled it.
        /// </summary>
        public Activity? Activity { get; }

        #endregion

        #region Constructors-and-Factories

        private HttpCallScope(string provider, string method, string url, long requestBytes)
        {
            _StartTimestamp = Stopwatch.GetTimestamp();
            _Provider = provider;
            _Method = method;
            _Operation = OperationScope.Current;

            if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            {
                _Host = uri.Host;
                _Port = uri.Port;
            }

            Activity = PolyPromptTelemetry.Source.StartActivity(method, ActivityKind.Client);
            if (Activity != null)
            {
                Activity.SetTag(PolyPromptTelemetryNames.Provider, provider);
                Activity.SetTag(PolyPromptTelemetryNames.HttpMethod, method);
                Activity.SetTag(PolyPromptTelemetryNames.UrlFull, StripQuery(url));
                if (_Host != null)
                {
                    Activity.SetTag(PolyPromptTelemetryNames.ServerAddress, _Host);
                    Activity.SetTag(PolyPromptTelemetryNames.ServerPort, _Port);
                }
            }

            _ActiveEntry = PolyPromptTelemetry.ActiveRequests.Get(PolyPromptTelemetryNames.Provider, provider, PolyPromptTelemetryNames.HttpMethod, method);
            _ActiveEntry.Increment();

            if (requestBytes > 0 && PolyPromptTelemetry.HttpRequestBodySize.Enabled)
            {
                TagList tags = new TagList
                {
                    { PolyPromptTelemetryNames.Provider, provider },
                    { PolyPromptTelemetryNames.HttpMethod, method },
                };
                PolyPromptTelemetry.SafeRecord(PolyPromptTelemetry.HttpRequestBodySize, requestBytes, tags);
            }
        }

        /// <summary>
        /// Start a scope for a request, or return null when nothing is listening or telemetry fails. Call from an async
        /// method so the span does not remain current in the caller's flow.
        /// </summary>
        /// <param name="provider">Provider label.</param>
        /// <param name="method">HTTP method.</param>
        /// <param name="url">Request URL (the query string is removed before it is recorded).</param>
        /// <param name="requestBytes">Request body size in bytes.</param>
        /// <returns>The scope, or null.</returns>
        public static HttpCallScope? Start(string provider, string method, string url, long requestBytes)
        {
            if (!PolyPromptTelemetry.IsEnabled) return null;

            try
            {
                return new HttpCallScope(provider, method, url, requestBytes);
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Record the response status once headers arrive, without ending the scope (used by streaming requests, which
        /// end when the body has been read and the response disposed).
        /// </summary>
        /// <param name="statusCode">HTTP status code.</param>
        public void SetStatus(int statusCode)
        {
            _StatusCode = statusCode;
            if (statusCode < 200 || statusCode > 299) ReportToOperation(statusCode.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// End the scope with the status recorded by <see cref="SetStatus"/>.
        /// </summary>
        public void End()
        {
            if (_StatusCode.HasValue)
                Complete(_StatusCode.Value, null);
            else
                Finish(null, null, null);
        }

        /// <summary>
        /// End the scope with a response status.
        /// </summary>
        /// <param name="statusCode">HTTP status code.</param>
        /// <param name="responseBytes">Response body size in bytes, or null when the body was streamed.</param>
        public void Complete(int statusCode, long? responseBytes)
        {
            bool success = statusCode >= 200 && statusCode <= 299;
            string? errorType = success ? null : statusCode.ToString(CultureInfo.InvariantCulture);
            if (errorType != null) ReportToOperation(errorType);
            Finish(statusCode, errorType, responseBytes);
        }

        /// <summary>
        /// End the scope from an exception thrown while sending or reading the request.
        /// </summary>
        /// <param name="ex">Exception.</param>
        /// <param name="callerToken">Caller cancellation token, used to tell cancellation from timeout.</param>
        public void Fail(Exception ex, CancellationToken callerToken)
        {
            string errorType;
            if (ex is OperationCanceledException)
            {
                if (callerToken.IsCancellationRequested)
                {
                    Finish(null, null, null);
                    return;
                }
                errorType = PolyPromptTelemetryNames.ErrorTimeout;
            }
            else
            {
                errorType = ex.GetType().FullName ?? ex.GetType().Name;
                try
                {
                    PolyPromptTelemetry.RecordException(Activity, ex);
                }
                catch
                {
                }
            }

            ReportToOperation(errorType);
            Finish(null, errorType, null);
        }

        #endregion

        #region Private-Methods

        private void ReportToOperation(string errorType)
        {
            try
            {
                _Operation?.ReportError(errorType);
            }
            catch
            {
            }
        }

        private void Finish(int? statusCode, string? errorType, long? responseBytes)
        {
            if (Interlocked.Exchange(ref _Ended, 1) == 1) return;

            try
            {
                double seconds = Stopwatch.GetElapsedTime(_StartTimestamp).TotalSeconds;

                TagList tags = new TagList
                {
                    { PolyPromptTelemetryNames.Provider, _Provider },
                    { PolyPromptTelemetryNames.HttpMethod, _Method },
                };
                if (statusCode.HasValue) tags.Add(PolyPromptTelemetryNames.HttpStatusCode, statusCode.Value);
                if (errorType != null) tags.Add(PolyPromptTelemetryNames.ErrorType, errorType);
                if (_Host != null)
                {
                    tags.Add(PolyPromptTelemetryNames.ServerAddress, _Host);
                    tags.Add(PolyPromptTelemetryNames.ServerPort, _Port);
                }

                PolyPromptTelemetry.SafeRecord(PolyPromptTelemetry.HttpRequestDuration, seconds, tags);
                PolyPromptTelemetry.SafeAdd(PolyPromptTelemetry.HttpRequests, 1, tags);

                if (responseBytes.HasValue && PolyPromptTelemetry.HttpResponseBodySize.Enabled)
                {
                    TagList sizeTags = new TagList
                    {
                        { PolyPromptTelemetryNames.Provider, _Provider },
                        { PolyPromptTelemetryNames.HttpMethod, _Method },
                    };
                    PolyPromptTelemetry.SafeRecord(PolyPromptTelemetry.HttpResponseBodySize, responseBytes.Value, sizeTags);
                }

            }
            catch
            {
            }
            finally
            {
                _ActiveEntry.Decrement();
                StopActivity(statusCode, errorType);
            }
        }

        private void StopActivity(int? statusCode, string? errorType)
        {
            if (Activity == null) return;

            try
            {
                if (statusCode.HasValue) Activity.SetTag(PolyPromptTelemetryNames.HttpStatusCode, statusCode.Value);
                if (errorType != null)
                {
                    Activity.SetTag(PolyPromptTelemetryNames.ErrorType, errorType);
                    Activity.SetStatus(ActivityStatusCode.Error, statusCode.HasValue ? "HTTP " + statusCode.Value : errorType);
                }
            }
            catch
            {
            }

            PolyPromptTelemetry.StopDetached(Activity);
        }

        private static string StripQuery(string url)
        {
            int index = url.IndexOf('?');
            return index >= 0 ? url.Substring(0, index) : url;
        }

        #endregion
    }
}
