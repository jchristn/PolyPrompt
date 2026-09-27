namespace PolyPrompt.Models
{
    /// <summary>
    /// Response from a classification request, with one result per input in input order.
    /// </summary>
    public class ClassificationResponse
    {
        #region Public-Members

        /// <summary>
        /// Whether the request was successful.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// HTTP status code from the request.
        /// </summary>
        public int StatusCode { get; set; }

        /// <summary>
        /// Error message if the request failed; null on success.
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// The model name used for this request, or null when the provider chose its default.
        /// </summary>
        public string? Model { get; set; }

        /// <summary>
        /// Classification results, one per input, in input order. Empty when the request failed.
        /// </summary>
        public List<ClassificationResult> Classifications { get; set; } = new List<ClassificationResult>();

        /// <summary>
        /// Overall runtime of the request in milliseconds.
        /// </summary>
        public long OverallRuntimeMs { get; set; }

        #endregion
    }
}
