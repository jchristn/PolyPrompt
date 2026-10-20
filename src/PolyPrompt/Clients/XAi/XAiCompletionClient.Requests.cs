namespace PolyPrompt.Clients
{
    using System.Text.Json;
    using PolyPrompt.Helpers;
    using PolyPrompt.Models;
    using PolyPrompt.Options;

    public partial class XAiCompletionClient
    {
        private T? Value<T>(ResolvedCompletion settings, Func<XAiCompletionOptions, T?> select) where T : struct => Pick(settings.Call, Defaults, select);
        private T? Reference<T>(ResolvedCompletion settings, Func<XAiCompletionOptions, T?> select) where T : class => PickRef(settings.Call, Defaults, select);

        private Dictionary<string, object> BuildBody(object input, ResolvedCompletion settings, bool stream, bool chat, ToolChatRequest? request = null)
        {
            Dictionary<string, object> body = new() { ["model"] = settings.Model, ["input"] = input, ["stream"] = stream, ["max_output_tokens"] = settings.MaxTokens };
            string? previous = Reference(settings, o => o.PreviousResponseId);
            if (!string.IsNullOrWhiteSpace(previous)) body["previous_response_id"] = previous;
            if (chat && request == null && string.IsNullOrWhiteSpace(previous) && !string.IsNullOrEmpty(settings.SystemPrompt)) body["instructions"] = settings.SystemPrompt;
            if (settings.Temperature.HasValue) body["temperature"] = settings.Temperature.Value;
            if (settings.TopP.HasValue)
            {
                if (settings.TopP <= 0) throw new ArgumentOutOfRangeException(nameof(CompletionOptions.TopP), "xAI top_p must be greater than zero.");
                body["top_p"] = settings.TopP.Value;
            }
            void Add(string name, object? value) { if (value != null) body[name] = value; }
            Add("store", Value(settings, o => o.Store));
            Add("parallel_tool_calls", Value(settings, o => o.ParallelToolCalls));
            int? turns = Value(settings, o => o.MaxTurns);
            if (turns <= 0) throw new ArgumentOutOfRangeException(nameof(XAiCompletionOptions.MaxTurns));
            Add("max_turns", turns);
            int? topK = Value(settings, o => o.TopK);
            if (topK <= 0) throw new ArgumentOutOfRangeException(nameof(XAiCompletionOptions.TopK));
            Add("top_k", topK);
            double? minP = Value(settings, o => o.MinP);
            if (minP.HasValue && (!double.IsFinite(minP.Value) || minP < 0 || minP > 1)) throw new ArgumentOutOfRangeException(nameof(XAiCompletionOptions.MinP));
            Add("min_p", minP);
            bool? logprobs = Value(settings, o => o.Logprobs);
            Add("logprobs", logprobs);
            int? topLogprobs = Value(settings, o => o.TopLogprobs);
            if (topLogprobs < 0 || topLogprobs > 8 || (topLogprobs.HasValue && logprobs != true)) throw new ArgumentException("TopLogprobs must be 0..8 and requires Logprobs=true.");
            Add("top_logprobs", topLogprobs);
            string? tier = Reference(settings, o => o.ServiceTier);
            if (tier != null && tier != "auto" && tier != "priority" && tier != "fast") throw new ArgumentException("ServiceTier must be auto, priority, or fast.");
            Add("service_tier", tier);
            Add("prompt_cache_key", Reference(settings, o => o.PromptCacheKey));
            Add("user", Reference(settings, o => o.User));
            Add("safety_identifier", Reference(settings, o => o.SafetyIdentifier));
            Add("guidelines", Reference(settings, o => o.Guidelines));
            Dictionary<string, object>? format = Reference(settings, o => o.TextFormat);
            if (format != null) body["text"] = new Dictionary<string, object> { ["format"] = format };
            List<string> include = new(Reference(settings, o => o.Include) ?? new List<string>());
            if (include.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Include must not contain empty field names.");
            if (!include.Contains("reasoning.encrypted_content")) include.Add("reasoning.encrypted_content");
            body["include"] = include.Distinct().ToList();
            if (chat)
            {
                string? effort = Reference(settings, o => o.ReasoningEffortOverride);
                if (effort == null && settings.ReasoningEffort != null)
                    effort = settings.ReasoningEffort.Level switch { ReasoningEffortLevel.Minimal => "low", ReasoningEffortLevel.Low => "low", ReasoningEffortLevel.Medium => "medium", ReasoningEffortLevel.High => "high", _ => throw new ArgumentOutOfRangeException(nameof(ReasoningEffort.Level)) };
                if (effort != null && effort != "low" && effort != "medium" && effort != "high" && effort != "xhigh") throw new ArgumentException("xAI reasoning effort must be low, medium, high, or xhigh.");
                string? summary = Reference(settings, o => o.ReasoningSummary);
                if (summary != null && summary != "auto" && summary != "concise" && summary != "detailed") throw new ArgumentException("ReasoningSummary must be auto, concise, or detailed.");
                if (effort != null || summary != null)
                {
                    Dictionary<string, object> reasoning = new();
                    if (effort != null) reasoning["effort"] = effort;
                    if (summary != null) reasoning["summary"] = summary;
                    body["reasoning"] = reasoning;
                }
            }
            List<object> tools = new();
            HashSet<string> names = new(StringComparer.Ordinal);
            bool? strict = Value(settings, o => o.StrictTools);
            IEnumerable<ToolDefinition> functions = (request?.Tools ?? new List<ToolDefinition>()).Concat(Reference(settings, o => o.FunctionTools) ?? new List<ToolDefinition>());
            foreach (ToolDefinition tool in functions)
            {
                if (tool == null || tool.Type != "function" || string.IsNullOrWhiteSpace(tool.Name) || tool.Parameters == null) throw new ArgumentException("Function tools require a name and parameter schema.");
                if (!names.Add(tool.Name)) throw new ArgumentException("Duplicate function tool: " + tool.Name);
                Dictionary<string, object> function = new() { ["type"] = "function", ["name"] = tool.Name, ["description"] = tool.Description, ["parameters"] = tool.Parameters };
                if (strict.HasValue) function["strict"] = strict.Value;
                tools.Add(function);
            }
            List<XAiHostedTool>? hosted = Reference(settings, o => o.HostedTools);
            if (hosted != null)
                foreach (XAiHostedTool tool in hosted)
                {
                    if (tool == null || tool.Parameters == null || tool.Parameters.ContainsKey("type")) throw new ArgumentException("Hosted tools require parameters without a type override.");
                    if (tool.Type is not ("web_search" or "x_search" or "code_interpreter" or "file_search" or "mcp" or "image_generation" or "shell" or "tool_search"))
                        throw new ArgumentException("Unknown xAI hosted tool type: " + tool.Type);
                    Dictionary<string, object> item = new(tool.Parameters) { ["type"] = tool.Type };
                    tools.Add(item);
                }
            if (tools.Count > 1000) throw new ArgumentException("xAI accepts at most 1000 tools.");
            if (tools.Count > 0) body["tools"] = tools;
            JsonElement? toolChoice = Value(settings, o => o.ToolChoice);
            if (toolChoice.HasValue)
            {
                if (toolChoice.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Object)) throw new ArgumentException("ToolChoice must be a string or object.");
                body["tool_choice"] = toolChoice.Value;
            }
            else if (request?.ToolChoice != null && tools.Count > 0)
            {
                string choice = request.ToolChoice;
                if (choice is "auto" or "none" or "required") body["tool_choice"] = choice;
                else if (choice == "any") body["tool_choice"] = "required";
                else body["tool_choice"] = new Dictionary<string, object> { ["type"] = "function", ["name"] = choice };
            }
            Dictionary<string, JsonElement>? additional = Reference(settings, o => o.AdditionalParameters);
            if (additional != null)
                foreach (var pair in additional)
                {
                    if (ReservedFields.Contains(pair.Key) || body.ContainsKey(pair.Key)) throw new ArgumentException("AdditionalParameters cannot override built-in field " + pair.Key);
                    if (pair.Value.ValueKind == JsonValueKind.Undefined) throw new ArgumentException("Additional parameter has undefined JSON: " + pair.Key);
                    body[pair.Key] = pair.Value;
                }
            return body;
        }

        private static readonly HashSet<string> ReservedFields = new("model input stream max_output_tokens instructions previous_response_id store parallel_tool_calls max_turns top_k min_p logprobs top_logprobs service_tier prompt_cache_key user safety_identifier guidelines text include reasoning reasoning_effort tools tool_choice".Split(' '), StringComparer.Ordinal);

        private static List<object> BuildInput(List<ChatMessage> messages)
        {
            List<object> input = new();
            foreach (ChatMessage message in messages)
            {
                if (message.Role == "assistant" && message.ProviderState.HasValue
                    && !ToolTurnState.IsTagged(message.ProviderState.Value))
                {
                    JsonElement state = message.ProviderState.Value;
                    if (state.ValueKind != JsonValueKind.Array) throw new ArgumentException("xAI assistant ProviderState must be a Responses output array.");
                    foreach (JsonElement item in state.EnumerateArray()) input.Add(item);
                    continue; // The state already contains the assistant text and function calls, in their original order.
                }
                if (message.Role == "tool")
                {
                    if (string.IsNullOrWhiteSpace(message.ToolCallId)) throw new ArgumentException("xAI tool results require ToolCallId (the function's call_id).");
                    input.Add(new Dictionary<string, object> { ["type"] = "function_call_output", ["call_id"] = message.ToolCallId, ["output"] = message.Content ?? "" });
                    continue;
                }
                if (message.Role is not ("system" or "developer" or "user" or "assistant")) throw new ArgumentException("Unsupported xAI message role: " + message.Role);
                if (!string.IsNullOrEmpty(message.Content) || message.ToolCalls.Count == 0)
                    input.Add(new Dictionary<string, object> { ["role"] = message.Role, ["content"] = message.Content ?? "" });
                foreach (ToolCall call in message.ToolCalls)
                {
                    if (string.IsNullOrWhiteSpace(call.Id) || string.IsNullOrWhiteSpace(call.Name)) throw new ArgumentException("xAI assistant function calls require an ID and name.");
                    input.Add(new Dictionary<string, object> { ["type"] = "function_call", ["call_id"] = call.Id, ["name"] = call.Name, ["arguments"] = call.ArgumentsJson ?? "{}" });
                }
            }
            return input;
        }
    }
}
