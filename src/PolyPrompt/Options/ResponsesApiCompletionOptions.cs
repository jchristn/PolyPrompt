namespace PolyPrompt.Options
{
    using System.Text.Json;
    using PolyPrompt.Models;

    /// <summary>
    /// Responses API settings shared by <see cref="Clients.OpenAiClient"/>, <see cref="Clients.AzureOpenAiClient"/>, and
    /// <see cref="Clients.XAiClient"/>. Provider-specific fields live on <see cref="OpenAiCompletionOptions"/> and
    /// <see cref="XAiCompletionOptions"/>. Non-null per-call values override the client's <c>Defaults.Completion</c> field
    /// by field.
    /// </summary>
    public class ResponsesApiCompletionOptions : CompletionOptions
    {
        /// <summary>
        /// Persist the response on the provider so it can be retrieved and continued by id. PolyPrompt always sends
        /// <c>store</c> explicitly: <c>false</c> unless this is true. (OpenAI's own API and SDKs default to true, keeping
        /// responses for at least 30 days.) Stateless continuity comes from encrypted reasoning, which is always requested.
        /// Client default: false.
        /// </summary>
        public bool? Store { get; set; }

        /// <summary>
        /// Continue a stored response. Supply only the new input, not the full history. Empty clears a default id.
        /// Prefer <c>ContinueResponseAsync</c>, which checks that the previous response was stored.
        /// </summary>
        public string? PreviousResponseId { get; set; }

        /// <summary>
        /// Allow multiple function calls per model turn.
        /// </summary>
        public bool? ParallelToolCalls { get; set; }

        /// <summary>
        /// Send <c>strict</c> on locally supplied function tools.
        /// </summary>
        public bool? StrictTools { get; set; }

        /// <summary>
        /// Exact <c>reasoning.effort</c> wire value, overriding the mapping from <see cref="CompletionOptions.ReasoningEffort"/>.
        /// Known values a provider rejects throw <see cref="ArgumentException"/>; unknown values pass through.
        /// </summary>
        public ReasoningEffortValue? ReasoningEffortValue { get; set; }

        /// <summary>
        /// Reasoning summary preference: <c>auto</c>, <c>concise</c>, or <c>detailed</c>. The provider may always return detailed.
        /// </summary>
        public ReasoningSummary? ReasoningSummary { get; set; }

        /// <summary>
        /// Additional output fields to include (for example <c>web_search_call.action.sources</c>).
        /// <c>reasoning.encrypted_content</c> is always added so history can be replayed without storage.
        /// </summary>
        public List<string>? Include { get; set; }

        /// <summary>
        /// Local function tools for native (raw input) requests; also combined with <c>ToolChatRequest.Tools</c>.
        /// </summary>
        public List<ToolDefinition>? FunctionTools { get; set; }

        /// <summary>
        /// Native tools: hosted tools (web search, file search, code interpreter, MCP, image generation) and client-executed
        /// non-function tools. Build them with <see cref="ResponsesApiTools"/>, <see cref="OpenAiTools"/>, or
        /// <see cref="XAiTools"/>.
        /// </summary>
        public List<ResponsesApiHostedTool>? HostedTools { get; set; }

        /// <summary>
        /// Text response format object (<c>text.format</c>): <c>text</c>, <c>json_object</c>, or <c>json_schema</c> with
        /// schema, name, and strict fields.
        /// </summary>
        public Dictionary<string, object>? TextFormat { get; set; }

        /// <summary>
        /// Raw <c>tool_choice</c> string or object, overriding <c>ToolChatRequest.ToolChoice</c> when supplied.
        /// </summary>
        public JsonElement? ToolChoice { get; set; }

        /// <summary>
        /// Processing tier. OpenAI accepts every <see cref="Models.ServiceTier"/> known value; xAI accepts auto, priority,
        /// and fast.
        /// </summary>
        public ServiceTier? ServiceTier { get; set; }

        /// <summary>
        /// Routing and prompt-cache affinity key.
        /// </summary>
        public string? PromptCacheKey { get; set; }

        /// <summary>
        /// Opaque end-user identifier.
        /// </summary>
        public string? User { get; set; }

        /// <summary>
        /// Stable hashed end-user identifier for abuse attribution.
        /// </summary>
        public string? SafetyIdentifier { get; set; }

        /// <summary>
        /// Extension body fields for anything PolyPrompt does not model (for example OpenAI's <c>prompt</c> templates or
        /// <c>truncation</c>). Cannot replace built-in request fields; values must be defined JSON.
        /// </summary>
        public Dictionary<string, JsonElement>? AdditionalParameters { get; set; }
    }
}
