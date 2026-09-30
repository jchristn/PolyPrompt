namespace PolyPrompt.Models
{
    /// <summary>
    /// Response from a non-streaming text generation request.
    /// </summary>
    public class GenerationResponse : ResponseBase
    {
        /// <summary>
        /// The generated text returned by the model.
        /// </summary>
        public string? Text { get; set; }
    }
}
