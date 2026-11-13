namespace PolyPrompt.Models
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// A native Responses API tool definition (hosted tools such as web search, and client-executed non-function tools
    /// such as shell). Parameters keep provider-specific options. Build one with <see cref="ResponsesApiTools"/>,
    /// <see cref="OpenAiTools"/>, the xAI tool factory, or <see cref="Create"/>. Function tools go in
    /// <c>ToolChatRequest.Tools</c> or <c>FunctionTools</c> instead.
    /// </summary>
    public class ResponsesApiHostedTool
    {
        /// <summary>
        /// Tool type, for example <c>web_search</c>, <c>code_interpreter</c>, or <c>mcp</c>.
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = "web_search";

        /// <summary>
        /// Additional tool fields, excluding <c>type</c>.
        /// </summary>
        [JsonPropertyName("parameters")]
        public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// Create a tool of any type with its provider-specific options. Use this for dated or preview variants such as
        /// <c>web_search_2025_08_26</c>, <c>web_search_preview</c>, or <c>computer_use_preview</c>.
        /// </summary>
        /// <param name="type">Tool type.</param>
        /// <param name="parameters">Tool fields, excluding <c>type</c>.</param>
        /// <returns>The tool.</returns>
        /// <exception cref="ArgumentException">Thrown when type is null, empty, or whitespace.</exception>
        public static ResponsesApiHostedTool Create(string type, Dictionary<string, object>? parameters = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(type);
            return new ResponsesApiHostedTool { Type = type, Parameters = parameters ?? new Dictionary<string, object>() };
        }
    }

    /// <summary>
    /// Factories for Responses API tools that both OpenAI and xAI accept.
    /// </summary>
    public static class ResponsesApiTools
    {
        /// <summary><c>web_search</c>: hosted web search.</summary>
        /// <param name="parameters">Optional tool fields (for example <c>filters</c> or <c>user_location</c>).</param>
        /// <returns>The tool.</returns>
        public static ResponsesApiHostedTool WebSearch(Dictionary<string, object>? parameters = null) => ResponsesApiHostedTool.Create("web_search", parameters);

        /// <summary><c>file_search</c>: hosted search over vector stores or collections.</summary>
        /// <param name="vectorStoreIds">Vector store (OpenAI) or collection (xAI) ids.</param>
        /// <param name="parameters">Optional additional tool fields.</param>
        /// <returns>The tool.</returns>
        public static ResponsesApiHostedTool FileSearch(List<string> vectorStoreIds, Dictionary<string, object>? parameters = null)
        {
            ArgumentNullException.ThrowIfNull(vectorStoreIds);
            Dictionary<string, object> fields = new Dictionary<string, object>(parameters ?? new Dictionary<string, object>()) { ["vector_store_ids"] = vectorStoreIds };
            return ResponsesApiHostedTool.Create("file_search", fields);
        }

        /// <summary><c>code_interpreter</c> with an automatic container.</summary>
        /// <param name="parameters">Optional tool fields; <c>container</c> defaults to <c>{ type: "auto" }</c>.</param>
        /// <returns>The tool.</returns>
        public static ResponsesApiHostedTool CodeInterpreter(Dictionary<string, object>? parameters = null)
        {
            Dictionary<string, object> fields = new Dictionary<string, object>(parameters ?? new Dictionary<string, object>());
            if (!fields.ContainsKey("container")) fields["container"] = new Dictionary<string, object> { ["type"] = "auto" };
            return ResponsesApiHostedTool.Create("code_interpreter", fields);
        }

        /// <summary><c>mcp</c>: a remote MCP server.</summary>
        /// <param name="serverLabel">Server label.</param>
        /// <param name="serverUrl">Server URL.</param>
        /// <param name="parameters">Optional additional fields (for example <c>require_approval</c>, <c>headers</c>, <c>allowed_tools</c>).</param>
        /// <returns>The tool.</returns>
        public static ResponsesApiHostedTool Mcp(string serverLabel, string serverUrl, Dictionary<string, object>? parameters = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(serverLabel);
            ArgumentException.ThrowIfNullOrWhiteSpace(serverUrl);
            Dictionary<string, object> fields = new Dictionary<string, object>(parameters ?? new Dictionary<string, object>())
            {
                ["server_label"] = serverLabel,
                ["server_url"] = serverUrl,
            };
            return ResponsesApiHostedTool.Create("mcp", fields);
        }

        /// <summary><c>image_generation</c>: hosted image generation.</summary>
        /// <param name="parameters">Optional tool fields.</param>
        /// <returns>The tool.</returns>
        public static ResponsesApiHostedTool ImageGeneration(Dictionary<string, object>? parameters = null) => ResponsesApiHostedTool.Create("image_generation", parameters);

        /// <summary><c>shell</c>: shell commands the model emits for the caller to execute (see <c>RawResponse</c>).</summary>
        /// <param name="parameters">Optional tool fields.</param>
        /// <returns>The tool.</returns>
        public static ResponsesApiHostedTool Shell(Dictionary<string, object>? parameters = null) => ResponsesApiHostedTool.Create("shell", parameters);

        /// <summary><c>tool_search</c>: hosted tool search.</summary>
        /// <param name="parameters">Optional tool fields.</param>
        /// <returns>The tool.</returns>
        public static ResponsesApiHostedTool ToolSearch(Dictionary<string, object>? parameters = null) => ResponsesApiHostedTool.Create("tool_search", parameters);
    }

    /// <summary>
    /// Factories for Responses API tools only OpenAI (and Azure OpenAI) accept. Calls to client-executed tools
    /// (<c>computer</c>, <c>local_shell</c>, <c>apply_patch</c>, <c>custom</c>) arrive in <c>RawResponse</c> and the native
    /// event stream, not in the generic <c>ToolCalls</c>, which carry function calls only.
    /// </summary>
    public static class OpenAiTools
    {
        /// <summary><c>computer</c>: computer use.</summary>
        /// <param name="parameters">Tool fields (for example <c>display_width</c>, <c>display_height</c>, <c>environment</c>).</param>
        /// <returns>The tool.</returns>
        public static ResponsesApiHostedTool Computer(Dictionary<string, object>? parameters = null) => ResponsesApiHostedTool.Create("computer", parameters);

        /// <summary><c>local_shell</c>.</summary>
        /// <returns>The tool.</returns>
        public static ResponsesApiHostedTool LocalShell() => ResponsesApiHostedTool.Create("local_shell");

        /// <summary><c>apply_patch</c>.</summary>
        /// <returns>The tool.</returns>
        public static ResponsesApiHostedTool ApplyPatch() => ResponsesApiHostedTool.Create("apply_patch");

        /// <summary><c>custom</c>: a freeform-input tool.</summary>
        /// <param name="name">Tool name.</param>
        /// <param name="description">Tool description.</param>
        /// <param name="format">Optional input format object (for example a grammar).</param>
        /// <returns>The tool.</returns>
        public static ResponsesApiHostedTool Custom(string name, string description, Dictionary<string, object>? format = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            Dictionary<string, object> fields = new Dictionary<string, object> { ["name"] = name, ["description"] = description ?? "" };
            if (format != null) fields["format"] = format;
            return ResponsesApiHostedTool.Create("custom", fields);
        }

        /// <summary><c>namespace</c>: a named group of tools.</summary>
        /// <param name="parameters">Tool fields.</param>
        /// <returns>The tool.</returns>
        public static ResponsesApiHostedTool Namespace(Dictionary<string, object> parameters) => ResponsesApiHostedTool.Create("namespace", parameters);

        /// <summary><c>programmatic_tool_calling</c>.</summary>
        /// <param name="parameters">Tool fields.</param>
        /// <returns>The tool.</returns>
        public static ResponsesApiHostedTool ProgrammaticToolCalling(Dictionary<string, object>? parameters = null) => ResponsesApiHostedTool.Create("programmatic_tool_calling", parameters);
    }
}
