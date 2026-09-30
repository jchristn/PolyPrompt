namespace PolyPrompt.Models
{
    /// <summary>
    /// Fields shared by every operation response. Operations report HTTP errors, malformed provider responses, and
    /// transport failures through <see cref="Success"/> and <see cref="Error"/> instead of throwing; they throw only for
    /// invalid arguments and cancellation.
    /// </summary>
    public abstract class ResponseBase
    {
        /// <summary>
        /// Whether the request succeeded. For streaming responses, whether the stream was opened successfully.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// HTTP status code returned by the provider, or null when no response was received.
        /// </summary>
        public int? StatusCode { get; set; }

        /// <summary>
        /// Error message when the request failed, including the HTTP status and response body for HTTP errors.
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// The model used for the request, as reported by the provider when it reports one, else the requested model.
        /// </summary>
        public string? Model { get; set; }

        /// <summary>
        /// Total time for the request in milliseconds. For streaming responses, updated when the stream has been
        /// fully consumed.
        /// </summary>
        public long OverallRuntimeMs { get; set; }
    }
}
