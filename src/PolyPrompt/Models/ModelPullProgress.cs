namespace PolyPrompt.Models
{
    /// <summary>
    /// Progress information reported during a model pull operation.
    /// </summary>
    public class ModelPullProgress
    {
        #region Public-Members

        /// <summary>
        /// Status message from the provider (e.g. "pulling manifest", "downloading sha256:abc123", "success").
        /// </summary>
        public string Status { get; set; } = "";

        /// <summary>
        /// Digest identifier for the layer being downloaded. Null when not downloading a layer.
        /// </summary>
        public string? Digest { get; set; } = null;

        /// <summary>
        /// Total bytes for the current layer. Null when not applicable.
        /// </summary>
        public long? TotalBytes { get; set; } = null;

        /// <summary>
        /// Bytes completed for the current layer. Null when not applicable.
        /// </summary>
        public long? CompletedBytes { get; set; } = null;

        /// <summary>
        /// Percentage complete for the current layer (0.0 to 100.0). Null when total is unknown.
        /// </summary>
        public double? PercentComplete
        {
            get
            {
                if (TotalBytes == null || TotalBytes == 0 || CompletedBytes == null) return null;
                return (double)CompletedBytes.Value / (double)TotalBytes.Value * 100.0;
            }
        }

        #endregion
    }
}
