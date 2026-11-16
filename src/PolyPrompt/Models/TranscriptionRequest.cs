namespace PolyPrompt.Models
{
    /// <summary>
    /// Audio to transcribe and how. The model comes from <see cref="TranscriptionOptions"/> or <c>Defaults.Transcription</c>.
    /// </summary>
    public sealed class TranscriptionRequest
    {
        #region Public-Members

        /// <summary>
        /// The audio bytes. Required.
        /// </summary>
        public byte[] Audio { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// The audio media type, for example <see cref="MediaTypes.AudioFlac"/>. Null detects it from the bytes. Default: null.
        /// </summary>
        public string? MediaType { get; set; } = null;

        /// <summary>
        /// The file name sent with the upload. Servers use its extension to pick a decoder. Null derives one from the media
        /// type, for example <c>audio.flac</c>. Default: null.
        /// </summary>
        public string? FileName { get; set; } = null;

        /// <summary>
        /// The spoken language as an ISO-639-1 code, for example <c>en</c>. Null lets the model detect it. Default: null.
        /// </summary>
        public string? Language { get; set; } = null;

        /// <summary>
        /// Text that guides style or spelling, or continues a previous chunk. Default: null.
        /// </summary>
        public string? Prompt { get; set; } = null;

        /// <summary>
        /// Sampling temperature from 0 to 1. Null uses the provider default. Default: null.
        /// </summary>
        public double? Temperature
        {
            get => _Temperature;
            set
            {
                if (value.HasValue && (double.IsNaN(value.Value) || value.Value < 0 || value.Value > 1))
                    throw new ArgumentOutOfRangeException(nameof(Temperature), "Temperature must be from 0 to 1.");
                _Temperature = value;
            }
        }

        /// <summary>
        /// The response format. Default: <see cref="TranscriptionFormat.Json"/>.
        /// </summary>
        public TranscriptionFormat Format { get; set; } = TranscriptionFormat.Json;

        /// <summary>
        /// Timestamp detail. Requires <see cref="TranscriptionFormat.VerboseJson"/>. Default: empty.
        /// </summary>
        public List<TranscriptionTimestampGranularity> TimestampGranularities { get; set; } = new List<TranscriptionTimestampGranularity>();

        #endregion

        #region Private-Members

        private double? _Temperature = null;

        #endregion
    }
}
