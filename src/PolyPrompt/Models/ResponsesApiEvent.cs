namespace PolyPrompt.Models
{
    using System.Text.Json;

    /// <summary>
    /// A native Responses API server-sent event, retaining the complete event payload.
    /// </summary>
    public class ResponsesApiEvent
    {
        /// <summary>
        /// The event, mapped from <see cref="Type"/>. Unrecognized names are <see cref="ResponsesApiEventKind.Other"/>.
        /// </summary>
        public ResponsesApiEventKind Kind { get; set; } = ResponsesApiEventKind.Other;

        /// <summary>
        /// The raw event name, for example <c>response.output_text.delta</c>.
        /// </summary>
        public string Type { get; set; } = "";

        /// <summary>
        /// Complete native JSON event data.
        /// </summary>
        public JsonElement Data { get; set; }

        /// <summary>
        /// Map a raw event name to its kind.
        /// </summary>
        /// <param name="type">Event name.</param>
        /// <returns>The kind.</returns>
        public static ResponsesApiEventKind KindOf(string? type)
        {
            switch (type)
            {
                case "response.created": return ResponsesApiEventKind.Created;
                case "response.queued": return ResponsesApiEventKind.Queued;
                case "response.in_progress": return ResponsesApiEventKind.InProgress;
                case "response.output_item.added": return ResponsesApiEventKind.OutputItemAdded;
                case "response.output_item.done": return ResponsesApiEventKind.OutputItemDone;
                case "response.content_part.added": return ResponsesApiEventKind.ContentPartAdded;
                case "response.content_part.done": return ResponsesApiEventKind.ContentPartDone;
                case "response.output_text.delta": return ResponsesApiEventKind.TextDelta;
                case "response.output_text.done": return ResponsesApiEventKind.TextDone;
                case "response.output_text.annotation.added": return ResponsesApiEventKind.AnnotationAdded;
                case "response.refusal.delta": return ResponsesApiEventKind.RefusalDelta;
                case "response.refusal.done": return ResponsesApiEventKind.RefusalDone;
                case "response.reasoning_summary_part.added": return ResponsesApiEventKind.ReasoningSummaryPartAdded;
                case "response.reasoning_summary_part.done": return ResponsesApiEventKind.ReasoningSummaryPartDone;
                case "response.reasoning_summary_text.delta": return ResponsesApiEventKind.ReasoningSummaryDelta;
                case "response.reasoning_summary_text.done": return ResponsesApiEventKind.ReasoningSummaryDone;
                case "response.reasoning_text.delta": return ResponsesApiEventKind.ReasoningDelta;
                case "response.reasoning_text.done": return ResponsesApiEventKind.ReasoningDone;
                case "response.function_call_arguments.delta": return ResponsesApiEventKind.FunctionCallArgumentsDelta;
                case "response.function_call_arguments.done": return ResponsesApiEventKind.FunctionCallArgumentsDone;
                case "response.custom_tool_call_input.delta": return ResponsesApiEventKind.CustomToolInputDelta;
                case "response.custom_tool_call_input.done": return ResponsesApiEventKind.CustomToolInputDone;
                case "response.compaction.compacting": return ResponsesApiEventKind.Compacting;
                case "response.completed": return ResponsesApiEventKind.Completed;
                case "response.incomplete": return ResponsesApiEventKind.Incomplete;
                case "response.failed": return ResponsesApiEventKind.Failed;
                case "error": return ResponsesApiEventKind.Error;
            }

            if (type != null && (type.StartsWith("response.web_search_call.", StringComparison.Ordinal)
                || type.StartsWith("response.file_search_call.", StringComparison.Ordinal)
                || type.StartsWith("response.code_interpreter_call", StringComparison.Ordinal)
                || type.StartsWith("response.image_generation_call.", StringComparison.Ordinal)
                || type.StartsWith("response.mcp_call", StringComparison.Ordinal)
                || type.StartsWith("response.mcp_list_tools.", StringComparison.Ordinal)
                || type.StartsWith("response.shell_call_", StringComparison.Ordinal)))
            {
                return ResponsesApiEventKind.HostedToolProgress;
            }

            return ResponsesApiEventKind.Other;
        }
    }

    /// <summary>
    /// Kinds of Responses API stream events. <see cref="ResponsesApiEvent.Type"/> always carries the raw name.
    /// </summary>
    public enum ResponsesApiEventKind
    {
        /// <summary>An event PolyPrompt does not map (including Realtime-only names).</summary>
        Other,
        /// <summary><c>response.created</c>.</summary>
        Created,
        /// <summary><c>response.queued</c>.</summary>
        Queued,
        /// <summary><c>response.in_progress</c>.</summary>
        InProgress,
        /// <summary><c>response.output_item.added</c>.</summary>
        OutputItemAdded,
        /// <summary><c>response.output_item.done</c>.</summary>
        OutputItemDone,
        /// <summary><c>response.content_part.added</c>.</summary>
        ContentPartAdded,
        /// <summary><c>response.content_part.done</c>.</summary>
        ContentPartDone,
        /// <summary><c>response.output_text.delta</c>.</summary>
        TextDelta,
        /// <summary><c>response.output_text.done</c>.</summary>
        TextDone,
        /// <summary><c>response.output_text.annotation.added</c> (citations).</summary>
        AnnotationAdded,
        /// <summary><c>response.refusal.delta</c>.</summary>
        RefusalDelta,
        /// <summary><c>response.refusal.done</c>.</summary>
        RefusalDone,
        /// <summary><c>response.reasoning_summary_part.added</c>.</summary>
        ReasoningSummaryPartAdded,
        /// <summary><c>response.reasoning_summary_part.done</c>.</summary>
        ReasoningSummaryPartDone,
        /// <summary><c>response.reasoning_summary_text.delta</c>.</summary>
        ReasoningSummaryDelta,
        /// <summary><c>response.reasoning_summary_text.done</c>.</summary>
        ReasoningSummaryDone,
        /// <summary><c>response.reasoning_text.delta</c>.</summary>
        ReasoningDelta,
        /// <summary><c>response.reasoning_text.done</c>.</summary>
        ReasoningDone,
        /// <summary><c>response.function_call_arguments.delta</c>.</summary>
        FunctionCallArgumentsDelta,
        /// <summary><c>response.function_call_arguments.done</c>.</summary>
        FunctionCallArgumentsDone,
        /// <summary><c>response.custom_tool_call_input.delta</c>.</summary>
        CustomToolInputDelta,
        /// <summary><c>response.custom_tool_call_input.done</c>.</summary>
        CustomToolInputDone,
        /// <summary>Progress of a hosted tool: web search, file search, code interpreter, image generation, MCP, or shell events.</summary>
        HostedToolProgress,
        /// <summary><c>response.compaction.compacting</c>.</summary>
        Compacting,
        /// <summary><c>response.completed</c>.</summary>
        Completed,
        /// <summary><c>response.incomplete</c>.</summary>
        Incomplete,
        /// <summary><c>response.failed</c>.</summary>
        Failed,
        /// <summary><c>error</c>.</summary>
        Error,
    }

    /// <summary>
    /// Native Responses API event stream. Enumerate <see cref="Events"/> completely to populate <see cref="Response"/> and
    /// the final status.
    /// </summary>
    public class ResponsesApiStreamingResponse : StreamingResponseBase
    {
        /// <summary>
        /// Native event stream, including events not projected onto generic chat chunks.
        /// </summary>
        public IAsyncEnumerable<ResponsesApiEvent> Events { get; set; } = Empty();

        /// <summary>
        /// Final response, available after a terminal response event.
        /// </summary>
        public ResponsesApiResponse? Response { get; set; }

        private static async IAsyncEnumerable<ResponsesApiEvent> Empty()
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }
    }
}
