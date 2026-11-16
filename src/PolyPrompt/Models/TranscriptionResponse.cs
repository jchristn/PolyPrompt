namespace PolyPrompt.Models
{
    /// <summary>
    /// A transcript. <see cref="Segments"/> and <see cref="Words"/> are filled only for
    /// <see cref="TranscriptionFormat.VerboseJson"/> and the requested granularities, and stay empty when a server omits them.
    /// </summary>
    public class TranscriptionResponse : ResponseBase
    {
        /// <summary>The transcript text.</summary>
        public string? Text { get; set; }

        /// <summary>The detected or requested language, when reported.</summary>
        public string? Language { get; set; }

        /// <summary>The audio duration in seconds, when reported.</summary>
        public double? DurationSeconds { get; set; }

        /// <summary>Timed segments, when requested and reported.</summary>
        public List<TranscriptionSegment> Segments { get; set; } = new List<TranscriptionSegment>();

        /// <summary>Timed words, when requested and reported.</summary>
        public List<TranscriptionWord> Words { get; set; } = new List<TranscriptionWord>();

        /// <summary>Token or duration usage, when reported.</summary>
        public TokenUsage? Usage { get; set; }
    }
}
