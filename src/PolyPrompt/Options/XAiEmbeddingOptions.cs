namespace PolyPrompt.Options
{
    /// <summary>xAI embedding settings. Select a model from the embedding-models catalog; no model is guessed.</summary>
    public class XAiEmbeddingOptions : OpenAiEmbeddingOptions
    {
        /// <summary>Request the provider's preview embedding format when supported by the model.</summary>
        public bool? Preview { get; set; }
        /// <summary>Opaque end-user identifier.</summary>
        public string? User { get; set; }
    }
}
