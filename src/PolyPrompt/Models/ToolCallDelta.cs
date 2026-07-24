namespace PolyPrompt.Models
{
    /// <summary>
    /// Incremental update for a streamed tool call.
    /// Providers may emit tool call IDs, names, and arguments across separate chunks.
    /// </summary>
    public class ToolCallDelta
    {
        /// <summary>
        /// Provider tool call index within the assistant response.
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// Provider tool call identifier. May be null when the provider does not emit IDs.
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// Provider tool call type. Usually function.
        /// </summary>
        public string? Type { get; set; } = null;

        /// <summary>
        /// Tool/function name requested by the model.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Incremental JSON argument fragment emitted by the provider.
        /// </summary>
        public string? ArgumentsJsonDelta { get; set; } = null;

        /// <summary>
        /// Complete JSON argument object for this tool call when the provider emits full arguments.
        /// </summary>
        public string? ArgumentsJson { get; set; } = null;
    }
}
