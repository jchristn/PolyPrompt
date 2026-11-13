namespace PolyPrompt.Models
{
    using System.Text.Json;

    /// <summary>
    /// A complete Responses API result from <c>ResponsesApiClientBase</c> native operations (OpenAI, Azure OpenAI, and
    /// xAI). The full provider JSON, including hosted-tool output, is in <see cref="ResponseBase.RawResponse"/>.
    /// </summary>
    public class ResponsesApiResponse : ToolChatResponse
    {
        /// <summary>
        /// Provider response status: <c>completed</c>, <c>incomplete</c>, <c>queued</c>, <c>in_progress</c>,
        /// <c>failed</c>, or <c>cancelled</c>.
        /// </summary>
        public string? Status { get; set; }

        /// <summary>
        /// Whether the request that created this response sent <c>store: true</c>, so it can be retrieved and continued by
        /// id (<c>ContinueResponseAsync</c>). Null when unknown (for example a response read with <c>GetResponseAsync</c>,
        /// which is stored by definition and reports true).
        /// </summary>
        public bool? Stored { get; set; }
    }

    /// <summary>
    /// JSON result of a Responses API resource operation (deletion, input-item listing, compaction, or token counting).
    /// </summary>
    public class ResponsesApiJsonResponse : ResponseBase
    {
        /// <summary>
        /// Provider JSON result, retaining unknown and future fields.
        /// </summary>
        public JsonElement? Data { get; set; }
    }
}
