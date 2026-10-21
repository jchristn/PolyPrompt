namespace PolyPrompt.Models
{
    /// <summary>Lifecycle of a single-consumer streaming operation, independent of telemetry.</summary>
    public enum StreamingState
    {
        /// <summary>No stream has opened.</summary>
        NotStarted,
        /// <summary>HTTP headers were accepted; enumeration has not started.</summary>
        Open,
        /// <summary>The consumer is enumerating the stream.</summary>
        Streaming,
        /// <summary>Enumeration ended normally with a provider completion signal.</summary>
        Completed,
        /// <summary>The HTTP request, transport, provider, or protocol failed.</summary>
        Failed,
        /// <summary>The caller cancelled the operation.</summary>
        Cancelled,
        /// <summary>The consumer disposed enumeration before it finished.</summary>
        Abandoned
    }
}
