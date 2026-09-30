namespace PolyPrompt.Models
{
    /// <summary>
    /// Settings for decision operations, shared by every provider. The same type serves as a decision client's
    /// <c>Defaults</c> and as per-call options; a non-null per-call value wins over the client default.
    /// </summary>
    public class DecisionOptions
    {
        /// <summary>
        /// Model name. On a client's <c>Defaults</c> this is the client's model; per call it overrides that model.
        /// Default: null.
        /// </summary>
        public string? Model { get; set; } = null;
    }
}
