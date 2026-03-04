namespace PolyPrompt.Models
{
    /// <summary>
    /// Response from a non-streaming text generation request.
    /// </summary>
    public class GenerationResponse
    {
        /// <summary>
        /// The generated text returned by the model.
        /// </summary>
        public string? Text { get; set; }

        /// <summary>
        /// The model name used for this generation.
        /// </summary>
        public string? Model { get; set; }

        /// <summary>
        /// Whether the request was successful.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// HTTP status code from the request.
        /// </summary>
        public int StatusCode { get; set; }

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
