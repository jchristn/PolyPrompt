namespace PolyPrompt.Options
{
    using PolyPrompt.Models;

    /// <summary>
    /// VoyageAI-specific options for rerank requests.
    /// These map to fields on the VoyageAI /v1/rerank request body.
    /// </summary>
    public class VoyageAiRerankOptions : RerankOptions
    {
        #region Public-Members

        /// <summary>
        /// Whether over-length query/document pairs are truncated to the model context length (truncation).
        /// Null uses the provider default (true). When false, over-length inputs cause an error.
        /// </summary>
        public bool? Truncation { get; set; } = null;

        #endregion
    }
}
