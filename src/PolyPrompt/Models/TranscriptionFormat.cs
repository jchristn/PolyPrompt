namespace PolyPrompt.Models
{
    /// <summary>
    /// Transcription response format, sent as <c>response_format</c>.
    /// </summary>
    public enum TranscriptionFormat
    {
        /// <summary>Plain text (<c>text</c>).</summary>
        Text,

        /// <summary>JSON with the text (<c>json</c>).</summary>
        Json,

        /// <summary>JSON with the language, duration, segments, and optional words (<c>verbose_json</c>). On OpenAI, <c>whisper-1</c> only.</summary>
        VerboseJson
    }
}
