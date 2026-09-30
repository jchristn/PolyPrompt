namespace PolyPrompt.Models
{
    /// <summary>
    /// Tool call requested by a model.
    /// </summary>
    public class ToolCall
    {
        #region Private-Members

        private static readonly PolyPrompt.Helpers.Serializer _Serializer = new PolyPrompt.Helpers.Serializer();

        #endregion

        #region Public-Members

        /// <summary>
        /// Provider tool call identifier. May be null when the provider does not emit IDs.
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// Tool/function name requested by the model.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Tool arguments as a JSON object string. Defaults to an empty JSON object.
        /// </summary>
        public string ArgumentsJson { get; set; } = "{}";

        /// <summary>
        /// Opaque provider token that must be sent back, unchanged, when this tool call is replayed in conversation history.
        /// Gemini 3 models attach a thought signature to the function calls they emit and reject a follow-up request whose
        /// replayed function call is missing it. Populated by the Gemini and Vertex AI clients (native thoughtSignature)
        /// and by the OpenAI client (Gemini's OpenAI-compatible extra_content.google.thought_signature); null otherwise.
        /// Callers that persist conversations must store and restore this value along with the tool call.
        /// </summary>
        public string? ThoughtSignature { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Deserialize ArgumentsJson into a typed arguments object.
        /// </summary>
        /// <typeparam name="T">Argument model type.</typeparam>
        /// <returns>The deserialized argument object, or null when the JSON literal is null.</returns>
        public T? DeserializeArguments<T>()
        {
            return _Serializer.DeserializeJson<T>(ArgumentsJson);
        }

        #endregion
    }
}
