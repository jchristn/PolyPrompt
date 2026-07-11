namespace PolyPrompt.Models
{
    /// <summary>
    /// Portable function/tool declaration supplied to a tool-capable chat request.
    /// </summary>
    public class ToolDefinition
    {
        #region Public-Members

        /// <summary>
        /// Tool type. The default is function.
        /// </summary>
        public string Type { get; set; } = "function";

        /// <summary>
        /// Function/tool name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Human-readable function/tool description.
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// JSON Schema parameter object for the function/tool.
        /// </summary>
        public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create a function tool definition.
        /// </summary>
        /// <param name="name">Function name.</param>
        /// <param name="description">Function description.</param>
        /// <param name="parameters">JSON Schema parameter object.</param>
        /// <returns>A function tool definition.</returns>
        /// <exception cref="ArgumentNullException">Thrown when name, description, or parameters is null.</exception>
        public static ToolDefinition Function(string name, string description, Dictionary<string, object> parameters)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(description);
            ArgumentNullException.ThrowIfNull(parameters);

            return new ToolDefinition
            {
                Type = "function",
                Name = name,
                Description = description,
                Parameters = parameters
            };
        }

        #endregion
    }
}
