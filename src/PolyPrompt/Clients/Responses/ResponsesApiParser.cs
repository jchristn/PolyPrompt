namespace PolyPrompt.Clients
{
    using System.Text;
    using System.Text.Json;
    using PolyPrompt.Models;

    // Non-streaming parsing for the shared Responses API engine: output items, usage, status, errors, and citations.
    public abstract partial class ResponsesApiClientBase
    {
        #region Parsing

        private void ParseResponse<T>(string json, T result) where T : ResponseBase
        {
            ResponsesApiDialect dialect = Dialect;
            try
            {
                JsonElement root = ResponsesApiJson.Parse(json);
                string? id = ResponsesApiJson.String(root, "id");
                JsonElement? output = ResponsesApiJson.Property(root, "output");
                string? status = ResponsesApiJson.String(root, "status");
                if (root.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(id) || output?.ValueKind != JsonValueKind.Array || string.IsNullOrEmpty(status))
                { result.Error = "Malformed " + dialect.Name + " response: id, status, and output array are required."; return; }
                result.Model = ResponsesApiJson.String(root, "model") ?? result.Model;
                result.RawResponse = root;
                SetTurnState(result, ResponsesProtocol, output.Value.Clone());
                if (result is ResponsesApiResponse native)
                {
                    native.Status = status;
                    if (native.Stored == null && ResponsesApiJson.Property(root, "store") is JsonElement stored && stored.ValueKind is JsonValueKind.True or JsonValueKind.False)
                        native.Stored = stored.GetBoolean();
                }
                JsonElement? error = ResponsesApiJson.Property(root, "error");
                if (status is "failed" or "cancelled" || (error.HasValue && error.Value.ValueKind != JsonValueKind.Null))
                { result.Error = error.HasValue && error.Value.ValueKind != JsonValueKind.Null ? ErrorMessage(error.Value) : dialect.Name + " response " + status; return; }
                if (status is not ("completed" or "incomplete" or "in_progress" or "queued")) { result.Error = "Unknown " + dialect.Name + " response status: " + status; return; }
                StringBuilder text = new(), reasoning = new();
                List<ToolCall> calls = new();
                List<Citation> citations = new();
                foreach (JsonElement item in output.Value.EnumerateArray())
                {
                    string? type = ResponsesApiJson.String(item, "type");
                    if (type == "message")
                    {
                        JsonElement? content = ResponsesApiJson.Property(item, "content");
                        if (content?.ValueKind != JsonValueKind.Array) { result.Error = dialect.Name + " message content must be an array."; return; }
                        foreach (JsonElement part in content.Value.EnumerateArray())
                        {
                            string? partType = ResponsesApiJson.String(part, "type");
                            if (partType == "output_text")
                            {
                                int offset = text.Length;
                                text.Append(ResponsesApiJson.String(part, "text"));
                                AddCitations(part, offset, citations, dialect);
                            }
                            else if (partType == "refusal") text.Append(ResponsesApiJson.String(part, "refusal"));
                        }
                    }
                    else if (type == "reasoning")
                    {
                        JsonElement? parts = ResponsesApiJson.Property(item, "summary");
                        if (parts?.ValueKind != JsonValueKind.Array || parts.Value.GetArrayLength() == 0) parts = ResponsesApiJson.Property(item, "content");
                        if (parts?.ValueKind == JsonValueKind.Array)
                            foreach (JsonElement part in parts.Value.EnumerateArray()) reasoning.Append(ResponsesApiJson.String(part, "text"));
                    }
                    else if (type == "function_call")
                    {
                        if (status is "in_progress" or "queued") continue; // Retrieval may include unfinished calls; keep them opaque until completion.
                        string? callId = ResponsesApiJson.String(item, "call_id"), name = ResponsesApiJson.String(item, "name"), arguments = ResponsesApiJson.String(item, "arguments");
                        if (string.IsNullOrWhiteSpace(callId) || string.IsNullOrWhiteSpace(name) || arguments == null)
                        { result.Error = "Malformed " + dialect.Name + " function call: call_id, name, and arguments are required."; return; }
                        if (status == "incomplete" && ResponsesApiJson.String(item, "status") != "completed")
                        { result.Error = dialect.Name + " returned an incomplete function call; it must not be executed."; return; }
                        using JsonDocument args = JsonDocument.Parse(arguments);
                        if (args.RootElement.ValueKind != JsonValueKind.Object) { result.Error = dialect.Name + " function arguments must be a JSON object."; return; }
                        if (calls.Any(c => c.Id == callId)) { result.Error = "Duplicate " + dialect.Name + " function call_id: " + callId; return; }
                        calls.Add(new ToolCall { Id = callId, Name = name, ArgumentsJson = arguments });
                    }
                    // Provider-hosted calls, client-executed non-function tools (custom, shell, local_shell, apply_patch,
                    // computer), encrypted outputs, compaction, and future item types stay intact in
                    // ProviderState/RawResponse and are never mistaken for local functions.
                }
                result.Citations = citations;
                TokenUsage? usage = ParseUsage(ResponsesApiJson.Property(root, "usage"));
                string finish = status == "incomplete"
                    ? IncompleteFinishReason(ResponsesApiJson.String(ResponsesApiJson.Property(root, "incomplete_details") ?? default, "reason"))
                    : status is "in_progress" or "queued" ? status : calls.Count > 0 ? "tool_calls" : "stop";
                string? answer = text.Length > 0 ? text.ToString() : null;
                string? thought = reasoning.Length > 0 ? reasoning.ToString() : null;
                if (result is ChatResponse chat) { chat.Text = answer; chat.Reasoning = thought; chat.Usage = usage; chat.ResponseId = id; chat.FinishReason = finish; }
                else if (result is ToolChatResponse tool) { tool.Text = answer; tool.Reasoning = thought; tool.Usage = usage; tool.ToolCalls = calls; tool.ResponseId = id; tool.FinishReason = finish; }
                else if (result is GenerationResponse generation) { generation.Text = answer; generation.Reasoning = thought; generation.Usage = usage; generation.ResponseId = id; generation.FinishReason = finish; }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
            { result.Error = "Malformed " + dialect.Name + " response: " + ex.Message; }
        }

        /// <summary>
        /// Map <c>incomplete_details.reason</c> to a finish reason: <c>max_output_tokens</c> to <c>length</c>,
        /// <c>content_filter</c> unchanged; other reasons (for example <c>max_messages</c>, <c>steered</c>) pass through.
        /// </summary>
        private static string IncompleteFinishReason(string? reason)
        {
            if (reason == "max_output_tokens") return "length";
            return string.IsNullOrEmpty(reason) ? "incomplete" : reason;
        }

        private static TokenUsage? ParseUsage(JsonElement? usage)
        {
            if (usage?.ValueKind != JsonValueKind.Object) return null;
            JsonElement inputDetails = ResponsesApiJson.Property(usage.Value, "input_tokens_details") ?? default;
            return new TokenUsage
            {
                PromptTokens = ResponsesApiJson.Int(usage.Value, "input_tokens"),
                CompletionTokens = ResponsesApiJson.Int(usage.Value, "output_tokens"),
                TotalTokens = ResponsesApiJson.Int(usage.Value, "total_tokens"),
                CachedPromptTokens = ResponsesApiJson.Int(inputDetails, "cached_tokens"),
                CacheCreationTokens = ResponsesApiJson.Int(inputDetails, "cache_write_tokens"),
                ReasoningTokens = ResponsesApiJson.Int(ResponsesApiJson.Property(usage.Value, "output_tokens_details") ?? default, "reasoning_tokens")
            };
        }

        /// <summary>
        /// Normalize <c>output_text</c> annotations into citations. Offsets are relative to the part, so they are shifted
        /// by the part's position in the concatenated text.
        /// </summary>
        private static void AddCitations(JsonElement part, int offset, List<Citation> citations, ResponsesApiDialect dialect)
        {
            if (ResponsesApiJson.Property(part, "annotations") is not JsonElement annotations || annotations.ValueKind != JsonValueKind.Array) return;
            foreach (JsonElement annotation in annotations.EnumerateArray())
            {
                Citation? citation = ToCitation(annotation, offset, dialect);
                if (citation != null) citations.Add(citation);
            }
        }

        private static Citation? ToCitation(JsonElement annotation, int offset, ResponsesApiDialect dialect)
        {
            string? type = ResponsesApiJson.String(annotation, "type");
            int? start = ResponsesApiJson.Int(annotation, "start_index");
            int? end = ResponsesApiJson.Int(annotation, "end_index");
            int? textStart = start.HasValue ? offset + start.Value : null;
            int? textEnd = end.HasValue ? offset + end.Value : null;
            switch (type)
            {
                case "url_citation":
                    return new Citation
                    {
                        Kind = CitationKind.Url,
                        Url = ResponsesApiJson.String(annotation, "url"),
                        Title = dialect.CitationTitleIsNumber ? null : ResponsesApiJson.String(annotation, "title"),
                        TextStart = textStart,
                        TextEnd = textEnd,
                        Raw = annotation.Clone(),
                    };
                case "file_citation":
                case "container_file_citation":
                case "file_path":
                    return new Citation
                    {
                        Kind = CitationKind.File,
                        Title = ResponsesApiJson.String(annotation, "filename"),
                        TextStart = textStart,
                        TextEnd = textEnd,
                        Source = new CitationSourceLocation
                        {
                            DocumentId = ResponsesApiJson.String(annotation, "file_id"),
                            DocumentTitle = ResponsesApiJson.String(annotation, "filename"),
                        },
                        Raw = annotation.Clone(),
                    };
                default:
                    return null;
            }
        }

        private static string ErrorMessage(JsonElement error) => ResponsesApiJson.String(error, "message") ?? error.ToString();

        private static void ParseJsonResponse(string json, ResponsesApiJsonResponse result)
        {
            result.Data = ResponsesApiJson.Parse(json);
            if (result.Data.Value.ValueKind != JsonValueKind.Object) result.Error = "Response is not a JSON object.";
            else if (ResponsesApiJson.Property(result.Data.Value, "error") is JsonElement error && error.ValueKind != JsonValueKind.Null) result.Error = ErrorMessage(error);
        }

        #endregion
    }
}
