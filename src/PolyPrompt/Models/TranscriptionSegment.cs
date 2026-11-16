namespace PolyPrompt.Models
{
    /// <summary>
    /// A timed segment of a transcript.
    /// </summary>
    public sealed class TranscriptionSegment
    {
        /// <summary>Segment index.</summary>
        public int Id { get; set; }

        /// <summary>Start time in seconds.</summary>
        public double StartSeconds { get; set; }

        /// <summary>End time in seconds.</summary>
        public double EndSeconds { get; set; }

        /// <summary>Segment text.</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>Average log probability of the segment's tokens, when reported.</summary>
        public double? AverageLogProbability { get; set; }

        /// <summary>Probability that the segment contains no speech, when reported.</summary>
        public double? NoSpeechProbability { get; set; }
    }
}
