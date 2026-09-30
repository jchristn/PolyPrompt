namespace PolyPrompt.Models
{
    /// <summary>
    /// Response from a rerank request. Results are always sorted by score, highest first, regardless of the
    /// order the provider returned them in, and each result's Index refers back to the caller's input list.
    /// </summary>
    public class RerankResponse : ResponseBase
    {
        #region Public-Members

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

        #endregion
    }
}
