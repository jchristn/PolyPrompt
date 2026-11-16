namespace PolyPrompt.Models
{
    /// <summary>
    /// A timed word of a transcript.
    /// </summary>
    public sealed class TranscriptionWord
    {
        /// <summary>The word.</summary>
        public string Word { get; set; } = string.Empty;

        /// <summary>Start time in seconds.</summary>
        public double StartSeconds { get; set; }

        /// <summary>End time in seconds.</summary>
        public double EndSeconds { get; set; }
    }
}
