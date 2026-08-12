namespace PolyPrompt.Models
{
    /// <summary>
    /// Response from a non-streaming chat completion request.
    /// Contains the completion text, metadata, and timing information.
    /// </summary>
    public class ChatResponse
    {
        /// <summary>
        /// The completion text returned by the model.
        /// </summary>
        public string? Text { get; set; }

        /// <summary>
        /// Reasoning ("thinking") content returned by the model, kept separate from <see cref="Text"/>. Null
        /// when the model produced no reasoning. Returned to the caller for display or logging.
        /// </summary>
        public string? Reasoning { get; set; }

        /// <summary>
        /// The model name used for this completion.
        /// </summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>
        /// Whether the request was successful.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// HTTP status code from the request.
        /// </summary>
        public int? StatusCode { get; set; }

        /// <summary>
        /// Error message if the request failed.
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// Overall runtime of the request in milliseconds.
        /// </summary>
        public long OverallRuntimeMs { get; set; }
    }
}
