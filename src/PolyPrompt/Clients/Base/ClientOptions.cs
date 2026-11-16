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

        /// <summary>
        /// Whether inline media in recorded request bodies is replaced with a placeholder
        /// (<see cref="HttpClientBase.RedactMediaInCallDetails"/>). Default: true.
        /// </summary>
        public bool? RedactMediaInCallDetails { get; init; }

        /// <summary>
        /// Inline media at or below this many decoded bytes is recorded in full
        /// (<see cref="HttpClientBase.MediaRedactionThresholdBytes"/>). Default: 1024.
        /// </summary>
        public int? MediaRedactionThresholdBytes { get; init; }

        /// <summary>
        /// Whether inline media is written with the normal JSON string escaping instead of raw
        /// (<see cref="HttpClientBase.EscapeMediaInJson"/>). Default: false.
        /// </summary>
        public bool? EscapeMediaInJson { get; init; }

        /// <summary>
        /// The most inline media one chat request may carry, in bytes as encoded on the wire
        /// (<see cref="ProviderClientBase.MaxInlineMediaBytes"/>). Default: the provider's limit.
        /// </summary>
        public long? MaxInlineMediaBytes { get; init; }
    }
}
