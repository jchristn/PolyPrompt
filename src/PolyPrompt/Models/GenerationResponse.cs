namespace PolyPrompt.Models
{
    /// <summary>
    /// Response from a non-streaming text generation request.
    /// </summary>
    public class GenerationResponse : ResponseBase
    {
        /// <summary>Reasoning summary returned by providers exposing a separate generation reasoning channel.</summary>
        public string? Reasoning { get; set; }
        /// <summary>Token usage, when reported by the provider.</summary>
        public TokenUsage? Usage { get; set; }
        /// <summary>Provider response identifier.</summary>
        public string? ResponseId { get; set; }
        /// <summary>Provider finish reason, such as stop, tool_calls, or length.</summary>
        public string? FinishReason { get; set; }

        /// <summary>
        /// The generated text returned by the model.
        /// </summary>
        public string? Text { get; set; }
    }
}
