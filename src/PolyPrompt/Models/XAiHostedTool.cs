namespace PolyPrompt.Models
{
    /// <summary>A native Responses tool definition. Parameters preserve provider-specific options. Most tools are hosted; native shell calls require caller handling through the raw API.</summary>
    public class XAiHostedTool
    {
        /// <summary>Tool type, for example web_search, x_search, code_interpreter, file_search, mcp, image_generation, shell, or tool_search.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("type")]
        public string Type { get; set; } = "web_search";
        /// <summary>Additional tool fields, excluding type. These are sent to xAI. The generic function-call loop does not execute native shell actions.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("parameters")]
        public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();
        /// <summary>Create a provider tool with its complete provider-specific options.</summary>
        public static XAiHostedTool Create(string type, Dictionary<string, object>? parameters = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(type);
            return new XAiHostedTool { Type = type, Parameters = parameters ?? new Dictionary<string, object>() };
        }
    }
}
