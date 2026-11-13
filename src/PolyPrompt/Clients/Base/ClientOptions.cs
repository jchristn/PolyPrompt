namespace PolyPrompt.Clients
{
    using PolyPrompt.Models;
    using SyslogLogging;

    /// <summary>
    /// Construction settings shared by every client. Every field is optional; null keeps the client's default.
    /// </summary>
    public class ClientOptions
    {
        /// <summary>
        /// Logging module. Default: a new <see cref="LoggingModule"/>.
        /// </summary>
        public LoggingModule? Logging { get; init; }

        /// <summary>
        /// HTTP client used for every request this client makes. When supplied, the caller keeps ownership and the client
        /// never disposes it; several clients may share one. Credentials are attached per request, so sharing is safe
        /// across providers. Its timeout is set to infinite when possible (see <see cref="ClientBase.TimeoutMs"/>).
        /// Ignored by agent harness clients. Default: a client-owned instance.
        /// </summary>
        public HttpClient? HttpClient { get; init; }

        /// <summary>
        /// Call timeout in milliseconds (<see cref="ClientBase.TimeoutMs"/>). Default: 120000.
        /// </summary>
        public int? TimeoutMs { get; init; }

        /// <summary>
        /// How much of each upstream call is recorded (<see cref="ClientBase.CallDetailCapture"/>). Default:
        /// <see cref="Models.CallDetailCapture.Metadata"/>.
        /// </summary>
        public CallDetailCapture? CallDetailCapture { get; init; }

        /// <summary>
        /// Whether credential headers are redacted in recorded call details (<see cref="ClientBase.RedactCredentials"/>).
        /// Default: true.
        /// </summary>
        public bool? RedactCredentials { get; init; }
    }
}
