namespace PolyPrompt.Models
{
    /// <summary>
    /// Base options for classification requests. When a value is null, the client instance default is used.
    /// </summary>
    public class ClassificationOptions
    {
        #region Public-Members

        /// <summary>
        /// Model override for this classification request. Null uses the client's classification model
        /// default. Ignored by Text Embeddings Inference, which serves a single model.
        /// </summary>
        public string? Model { get; set; } = null;

        #endregion
    }
}
