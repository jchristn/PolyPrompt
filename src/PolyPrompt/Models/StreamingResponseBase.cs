namespace PolyPrompt.Models
{
    /// <summary>
    /// Shared streaming lifecycle. Enumerate once and dispose the enumerator to finalize state.
    /// Instances are not thread safe. Success retains each provider's existing open-success behavior;
    /// use State to determine final completion. Caller cancellation is rethrown after recording Cancelled.
    /// </summary>
    public abstract class StreamingResponseBase : ResponseBase, IStreamingResponse
    {
        /// <inheritdoc />
        public StreamingState State { get; internal set; }
        /// <inheritdoc />
        public string? StreamError { get; internal set; }
    }
}
