namespace PolyPrompt.Models
{
    /// <summary>Final-outcome contract shared by completion and native event streams.</summary>
    public interface IStreamingResponse
    {
        /// <summary>Current lifecycle state. Final after enumeration is disposed. Not thread safe.</summary>
        StreamingState State { get; }
        /// <summary>Failure diagnostic, or null. Partial output remains provisional unless State is Completed.</summary>
        string? StreamError { get; }
    }
}
