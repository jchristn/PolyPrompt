namespace PolyPrompt.Models
{
    /// <summary>
    /// Tool call requested by a model.
    /// </summary>
    public class ToolCall
    {
        #region Private-Members

        private static readonly SerializationHelper.Serializer _Serializer = new SerializationHelper.Serializer();

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
