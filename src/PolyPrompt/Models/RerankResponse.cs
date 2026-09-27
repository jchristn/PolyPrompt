namespace PolyPrompt.Models
{
    /// <summary>
    /// Response from a rerank request. Results are always sorted by score, highest first, regardless of the
    /// order the provider returned them in, and each result's Index refers back to the caller's input list.
    /// </summary>
    public class RerankResponse
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
        /// The model name used for this request.
        /// </summary>
        public string? Model { get; set; }

        /// <summary>
        /// Provider-assigned response identifier, when the provider returns one (Cohere). Null otherwise.
        /// </summary>
        public string? ResponseId { get; set; }

        /// <summary>
        /// Scored documents, highest score first. Empty when the request failed.
        /// </summary>
        public List<RerankResult> Results { get; set; } = new List<RerankResult>();

        /// <summary>
        /// Tokens processed by the request, when the provider reports them (Cohere input tokens, VoyageAI
        /// total tokens). Null when the provider does not report token usage.
        /// </summary>
        public int? TotalTokens { get; set; }

        /// <summary>
        /// Billed search units, when the provider reports them (Cohere). Null otherwise.
        /// </summary>
        public int? SearchUnits { get; set; }

        /// <summary>
        /// Overall runtime of the request in milliseconds.
        /// </summary>
        public long OverallRuntimeMs { get; set; }

        #endregion
    }
}
