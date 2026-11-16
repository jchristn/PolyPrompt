namespace PolyPrompt.Models
{
    /// <summary>
    /// Per-call transcription settings, and the type of <c>Defaults.Transcription</c>.
    /// </summary>
    public class TranscriptionOptions
    {
        #region Public-Members

        /// <summary>
        /// The transcription model (an Azure deployment name on <c>AzureOpenAiClient</c>). Null uses
        /// <c>Defaults.Transcription.Model</c>. Default: null.
        /// </summary>
        public string? Model { get; set; } = null;

        #endregion
    }
}
