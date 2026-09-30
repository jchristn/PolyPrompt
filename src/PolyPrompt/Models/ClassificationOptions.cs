namespace PolyPrompt.Models
{
    /// <summary>
    /// Settings for classification operations, shared by every provider. The same type serves as a client's <c>Defaults</c>
    /// and as per-call options; a non-null per-call value wins over the client default. Provider-specific settings
    /// live on derived types.
    /// </summary>
    public class ClassificationOptions
    {
        #region Public-Members

        /// <summary>
        /// Model name. On a client's <c>Defaults</c> this is the client's model; per call it overrides that model.
        /// Ignored by Text Embeddings Inference, which serves a single model. Default: null.
        /// </summary>
        public string? Model { get; set; } = null;

        #endregion
    }
}
