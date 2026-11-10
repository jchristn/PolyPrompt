namespace PolyPrompt.Models
{
    /// <summary>
    /// Normalized model information returned by ListModelsAsync across all providers.
    /// </summary>
    public class ModelInformation
    {
        #region Public-Members

        /// <summary>
        /// The model identifier to pass to API calls (e.g. "gemma3:4b", "gpt-4o", "gemini-2.5-flash").
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Human-friendly display name for the model. May be null if the provider does not supply one.
        /// </summary>
        public string? DisplayName { get; set; } = null;

        /// <summary>
        /// Model description. May be null if the provider does not supply one.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Owner or organization of the model. May be null if the provider does not supply one.
        /// </summary>
        public string? OwnedBy { get; set; } = null;

        /// <summary>
        /// Creation timestamp in UTC. May be null if the provider does not supply one.
        /// </summary>
        public DateTime? CreatedUtc { get; set; } = null;

        /// <summary>
        /// Last modified timestamp in UTC. May be null if the provider does not supply one.
        /// </summary>
        public DateTime? ModifiedUtc { get; set; } = null;

        /// <summary>
        /// Model file size in bytes. May be null if the provider does not supply one.
        /// </summary>
        public long? SizeBytes { get; set; } = null;

        /// <summary>
        /// Maximum input tokens supported by the model. May be null if the provider does not supply one.
        /// </summary>
        public int? InputTokenLimit { get; set; } = null;

        /// <summary>
        /// Maximum output tokens supported by the model. May be null if the provider does not supply one.
        /// </summary>
        public int? OutputTokenLimit { get; set; } = null;

        /// <summary>
        /// Whether the model can use tools (function calling), or null when the provider does not say. Best-effort: on
        /// Ollama it is true only when the server lists the <c>tools</c> capability and the model's renderer or prompt
        /// template actually renders tools (some templates, such as deepseek-r1 builds, are listed with <c>tools</c> but
        /// drop them). The raw <c>capabilities</c>, <c>template</c>, and <c>renderer</c> values stay in
        /// <see cref="Metadata"/> for callers that want to decide for themselves. Other providers leave it null.
        /// </summary>
        public bool? SupportsTools { get; set; } = null;

        /// <summary>
        /// Provider-specific metadata as key-value pairs. Contains fields such as parameter_size,
        /// quantization_level, family, format, digest (Ollama), object type (OpenAI),
        /// or supportedGenerationMethods (Gemini).
        /// </summary>
        public Dictionary<string, string?> Metadata { get; set; } = new Dictionary<string, string?>();

        #endregion
    }
}
