namespace PolyPrompt.Models
{
    /// <summary>
    /// Timestamp detail for <see cref="TranscriptionFormat.VerboseJson"/>, sent as <c>timestamp_granularities[]</c>.
    /// </summary>
    public enum TranscriptionTimestampGranularity
    {
        /// <summary>Segment start and end times.</summary>
        Segment,

        /// <summary>Word start and end times.</summary>
        Word
    }
}
