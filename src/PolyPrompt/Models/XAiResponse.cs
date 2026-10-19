namespace PolyPrompt.Models
{
    using System.Text.Json;

    /// <summary>A complete Responses API result, including provider-hosted output, citations, encrypted reasoning, and raw usage.</summary>
    public class XAiResponse : ToolChatResponse
    {
        /// <summary>Provider response status: completed, incomplete, in_progress, or failed.</summary>
        public string? Status { get; set; }
        /// <summary>The complete provider JSON object, retaining all fields and hosted-tool outputs.</summary>
        public JsonElement? RawResponse { get; set; }
    }

    /// <summary>JSON result of an xAI resource operation (deletion, input listing, or compaction).</summary>
    public class XAiJsonResponse : ResponseBase
    {
        /// <summary>Provider JSON result, retaining unknown and future fields.</summary>
        public JsonElement? Data { get; set; }
    }
}
