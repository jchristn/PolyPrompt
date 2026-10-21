namespace PolyPrompt.Models
{
    using System.Text.Json;
    /// <summary>A native Responses SSE event, retaining the complete event payload.</summary>
    public class XAiResponseEvent
    {
        /// <summary>Event type, for example response.output_text.delta.</summary>
        public string Type { get; set; } = "";
        /// <summary>Complete native JSON event data.</summary>
        public JsonElement Data { get; set; }
    }
    /// <summary>Native xAI event stream. Enumerate Events completely to populate Response and final status.</summary>
    public class XAiStreamingResponse : StreamingResponseBase
    {
        /// <summary>Native event stream, including events not projected onto generic chat chunks.</summary>
        public IAsyncEnumerable<XAiResponseEvent> Events { get; set; } = Empty();
        /// <summary>Final response, available after a terminal response event.</summary>
        public XAiResponse? Response { get; set; }
        private static async IAsyncEnumerable<XAiResponseEvent> Empty() { yield break; }
    }
}
