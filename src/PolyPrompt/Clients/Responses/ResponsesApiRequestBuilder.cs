namespace PolyPrompt.Clients
{
    using System.Text.Json;
    using PolyPrompt.Helpers;
    using PolyPrompt.Models;
    using PolyPrompt.Options;

    // Request building for the shared Responses API engine: the body, input projection, and reserved fields.
    public abstract partial class ResponsesApiClientBase
    {
        #region Request-Building

        /// <summary>
        /// Where in the body a provider-only field is added, so field order (and request digests) stay stable.
        /// </summary>
        private protected enum ResponsesApiFieldStage
        {
            /// <summary>After <c>parallel_tool_calls</c>.</summary>
            AfterParallelToolCalls,

            /// <summary>After <c>safety_identifier</c>.</summary>
            AfterSafetyIdentifier,
        }

        /// <summary>
        /// Read a struct setting: the per-call value, else <c>Defaults.Completion</c>.
        /// </summary>
        private protected T? Value<TOptions, T>(ResolvedCompletion settings, Func<TOptions, T?> select) where TOptions : class where T : struct => Pick(settings.Call, Defaults.Completion, select);

        /// <summary>
        /// Read a reference setting: the per-call value, else <c>Defaults.Completion</c>.
        /// </summary>
        private protected T? Reference<TOptions, T>(ResolvedCompletion settings, Func<TOptions, T?> select) where TOptions : class where T : class => PickRef(settings.Call, Defaults.Completion, select);

        /// <summary>
        /// Add provider-only body fields at a stage. The engine never names them.
        /// </summary>
        /// <param name="body">Request body.</param>
        /// <param name="settings">Merged settings.</param>
        /// <param name="stream">Whether the call streams.</param>
        /// <param name="stage">Where in the body the fields belong.</param>
        private protected abstract void AddProviderFields(Dictionary<string, object> body, ResolvedCompletion settings, bool stream, ResponsesApiFieldStage stage);

        private Dictionary<string, object> BuildBody(object input, ResolvedCompletion settings, bool stream, bool chat, ToolChatRequest? request = null, string? previousOverride = null)
        {
            ResponsesApiDialect dialect = Dialect;
            Dictionary<string, object> body = new() { ["model"] = settings.Model, ["input"] = input, ["stream"] = stream };
            if (settings.MaxTokens.HasValue) body["max_output_tokens"] = settings.MaxTokens.Value;
            string? previous = previousOverride ?? Reference<ResponsesApiCompletionOptions, string>(settings, o => o.PreviousResponseId);
            if (!string.IsNullOrWhiteSpace(previous)) body["previous_response_id"] = previous;
            if (chat && request == null && string.IsNullOrWhiteSpace(previous) && !string.IsNullOrEmpty(settings.SystemPrompt)) body["instructions"] = settings.SystemPrompt;
            if (settings.Temperature.HasValue) body["temperature"] = settings.Temperature.Value;
            if (settings.TopP.HasValue)
            {
                if (dialect.TopPMustBePositive && settings.TopP <= 0) throw new ArgumentOutOfRangeException(nameof(CompletionOptions.TopP), dialect.Name + " top_p must be greater than zero.");
                body["top_p"] = settings.TopP.Value;
            }
            void Add(string name, object? value) { if (value != null) body[name] = value; }
            bool? store = Value<ResponsesApiCompletionOptions, bool>(settings, o => o.Store);
            if (dialect.AlwaysSendStore) store ??= false;
            Add("store", store);
            Add("parallel_tool_calls", Value<ResponsesApiCompletionOptions, bool>(settings, o => o.ParallelToolCalls));
            AddProviderFields(body, settings, stream, ResponsesApiFieldStage.AfterParallelToolCalls);
            ServiceTier? tier = Value<ResponsesApiCompletionOptions, ServiceTier>(settings, o => o.ServiceTier);
            if (tier.HasValue)
            {
                ResponsesApiDialect.Check(tier.Value.Value, ResponsesApiDialect.KnownServiceTiers, dialect.ServiceTiers, dialect.ServiceTierError);
                body["service_tier"] = tier.Value.Value;
            }
            Add("prompt_cache_key", Reference<ResponsesApiCompletionOptions, string>(settings, o => o.PromptCacheKey));
            Add("user", Reference<ResponsesApiCompletionOptions, string>(settings, o => o.User));
            Add("safety_identifier", Reference<ResponsesApiCompletionOptions, string>(settings, o => o.SafetyIdentifier));
            AddProviderFields(body, settings, stream, ResponsesApiFieldStage.AfterSafetyIdentifier);
            Dictionary<string, object>? format = Reference<ResponsesApiCompletionOptions, Dictionary<string, object>>(settings, o => o.TextFormat);
            if (format != null) body["text"] = new Dictionary<string, object> { ["format"] = format };
            List<string> include = new(Reference<ResponsesApiCompletionOptions, List<string>>(settings, o => o.Include) ?? new List<string>());
            if (include.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Include must not contain empty field names.");
            if (!include.Contains("reasoning.encrypted_content")) include.Add("reasoning.encrypted_content");
            body["include"] = include.Distinct().ToList();
            if (chat)
            {
                string? effort = Value<ResponsesApiCompletionOptions, ReasoningEffortValue>(settings, o => o.ReasoningEffortValue)?.Value;
                if (effort == null && settings.ReasoningEffort != null) effort = dialect.MapEffort(settings.ReasoningEffort);
                if (effort != null) ResponsesApiDialect.Check(effort, ResponsesApiDialect.KnownEfforts, dialect.Efforts, dialect.EffortError);
                string? summary = Value<ResponsesApiCompletionOptions, ReasoningSummary>(settings, o => o.ReasoningSummary)?.Value;
                if (summary != null) ResponsesApiDialect.Check(summary, ResponsesApiDialect.KnownSummaries, dialect.Summaries, "ReasoningSummary must be auto, concise, or detailed.");
                if (effort != null || summary != null)
                {
                    Dictionary<string, object> reasoning = new();
                    if (effort != null) reasoning["effort"] = effort;
                    if (summary != null) reasoning["summary"] = summary;
                    body["reasoning"] = reasoning;
                }
                if (effort != null) settings.AppliedReasoningEffort = effort;
            }
            List<object> tools = new();
            HashSet<string> names = new(StringComparer.Ordinal);
            bool? strict = Value<ResponsesApiCompletionOptions, bool>(settings, o => o.StrictTools);
            IEnumerable<ToolDefinition> functions = (request?.Tools ?? new List<ToolDefinition>()).Concat(Reference<ResponsesApiCompletionOptions, List<ToolDefinition>>(settings, o => o.FunctionTools) ?? new List<ToolDefinition>());
            foreach (ToolDefinition tool in functions)
            {
                if (tool == null || tool.Type != "function" || string.IsNullOrWhiteSpace(tool.Name) || tool.Parameters == null) throw new ArgumentException("Function tools require a name and parameter schema.");
                if (!names.Add(tool.Name)) throw new ArgumentException("Duplicate function tool: " + tool.Name);
                Dictionary<string, object> function = new() { ["type"] = "function", ["name"] = tool.Name, ["description"] = tool.Description, ["parameters"] = tool.Parameters };
                if (strict.HasValue) function["strict"] = strict.Value;
                tools.Add(function);
            }
            List<ResponsesApiHostedTool>? hosted = Reference<ResponsesApiCompletionOptions, List<ResponsesApiHostedTool>>(settings, o => o.HostedTools);
            if (hosted != null)
                foreach (ResponsesApiHostedTool tool in hosted)
                {
                    if (tool == null || tool.Parameters == null || tool.Parameters.ContainsKey("type")) throw new ArgumentException("Hosted tools require parameters without a type override.");
                    if (string.IsNullOrWhiteSpace(tool.Type)) throw new ArgumentException("Hosted tools require a type.");
                    if (dialect.HostedToolTypes != null && Array.IndexOf(dialect.HostedToolTypes, tool.Type) < 0)
                        throw new ArgumentException("Unknown " + dialect.Name + " hosted tool type: " + tool.Type);
                    Dictionary<string, object> item = new(tool.Parameters) { ["type"] = tool.Type };
                    tools.Add(item);
                }
            if (dialect.MaxTools.HasValue && tools.Count > dialect.MaxTools.Value) throw new ArgumentException(dialect.Name + " accepts at most " + dialect.MaxTools.Value + " tools.");
            if (tools.Count > 0) body["tools"] = tools;
            JsonElement? toolChoice = Value<ResponsesApiCompletionOptions, JsonElement>(settings, o => o.ToolChoice);
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
            Dictionary<string, JsonElement>? additional = Reference<ResponsesApiCompletionOptions, Dictionary<string, JsonElement>>(settings, o => o.AdditionalParameters);
            if (additional != null)
                foreach (var pair in additional)
                {
                    if (dialect.ReservedFields.Contains(pair.Key) || body.ContainsKey(pair.Key)) throw new ArgumentException("AdditionalParameters cannot override built-in field " + pair.Key);
                    if (pair.Value.ValueKind == JsonValueKind.Undefined) throw new ArgumentException("Additional parameter has undefined JSON: " + pair.Key);
                    body[pair.Key] = pair.Value;
                }
            return body;
        }

        private List<object> BuildInput(List<ChatMessage> messages)
        {
            ResponsesApiDialect dialect = Dialect;
            List<object> input = new();
            foreach (ChatMessage message in messages)
            {
                if (message.Role == "assistant" && message.ProviderState.HasValue)
                {
                    JsonElement state = message.ProviderState.Value;
                    JsonElement output = default;
                    bool replay = false;
                    if (ToolTurnState.IsEnvelope(state))
                    {
                        replay = TryGetTurnState(message, ResponsesProtocol, out output);
                    }
                    else if (dialect.ReadsLegacyArrayState)
                    {
                        if (state.ValueKind != JsonValueKind.Array) throw new ArgumentException(dialect.Name + " assistant ProviderState must be a Responses output array.");
                        output = state;
                        replay = true;
                    }

                    if (replay && output.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement item in output.EnumerateArray()) input.Add(item);
                        continue; // The state already contains the assistant text and function calls, in their original order.
                    }
                    // Another protocol's, provider's, or endpoint's state: fall back to the plain message below.
                }
                if (message.Role == "tool")
                {
                    if (string.IsNullOrWhiteSpace(message.ToolCallId)) throw new ArgumentException(dialect.Name + " tool results require ToolCallId (the function's call_id).");
                    input.Add(new Dictionary<string, object> { ["type"] = "function_call_output", ["call_id"] = message.ToolCallId, ["output"] = message.Content ?? "" });
                    continue;
                }
                if (message.Role is not ("system" or "developer" or "user" or "assistant")) throw new ArgumentException("Unsupported " + dialect.Name + " message role: " + message.Role);
                if (!string.IsNullOrEmpty(message.Content) || message.ToolCalls.Count == 0)
                    input.Add(new Dictionary<string, object> { ["role"] = message.Role, ["content"] = message.Content ?? "" });
                foreach (ToolCall call in message.ToolCalls)
                {
                    if (string.IsNullOrWhiteSpace(call.Id) || string.IsNullOrWhiteSpace(call.Name)) throw new ArgumentException(dialect.Name + " assistant function calls require an ID and name.");
                    input.Add(new Dictionary<string, object> { ["type"] = "function_call", ["call_id"] = call.Id, ["name"] = call.Name, ["arguments"] = call.ArgumentsJson ?? "{}" });
                }
            }
            return input;
        }

        #endregion
    }
}
