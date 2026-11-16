namespace PolyPrompt.Models
{
    /// <summary>
    /// Image resolution hint. Sent as OpenAI's <c>detail</c> (Responses API and Chat Completions) and ignored by providers
    /// without the concept.
    /// </summary>
    public enum ImageDetail
    {
        /// <summary>Let the provider choose.</summary>
        Auto,

        /// <summary>Low resolution: fewer tokens, less detail.</summary>
        Low,

        /// <summary>High resolution: more tokens, more detail.</summary>
        High
    }
}
