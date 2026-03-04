namespace PolyPrompt.Models
{
    /// <summary>
    /// Base options for embedding requests.
    /// When a value is null, the client instance default is used.
    /// </summary>
    public class EmbeddingOptions
    {
        #region Public-Members

        /// <summary>
        /// Model override for this embedding request. Null uses client default.
        /// </summary>
        public string? Model { get; set; } = null;

        #endregion
    }
}
