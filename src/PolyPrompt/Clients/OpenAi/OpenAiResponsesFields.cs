namespace PolyPrompt.Clients
{
    using System.Text.Json;
    using PolyPrompt.Models;
    using PolyPrompt.Options;

    /// <summary>
    /// OpenAI Responses API dialect data and OpenAI-only body fields, shared by <see cref="OpenAiClient"/> and
    /// <see cref="AzureOpenAiClient"/>.
    /// </summary>
    internal static class OpenAiResponsesFields
    {
        // Verified against OpenAI's OpenAPI spec (github.com/openai/openai-openapi, main branch, openapi.json), 2026-11-12:
        // reasoning.effort none/minimal/low/medium/high/xhigh/max; reasoning.summary auto/concise/detailed; service_tier
        // auto/default/flex/scale/priority/fast; store defaults to true server-side; no client-side tool type or count limit.
        internal static readonly string[] ProviderFields = { "metadata", "background", "conversation", "max_tool_calls", "top_logprobs", "prompt_cache_options", "context_management", "stream_options" };

        internal static ResponsesApiDialect Dialect(string name, string? notFoundHint)
        {
            return new ResponsesApiDialect
            {
                Name = name,
                Efforts = ResponsesApiDialect.KnownEfforts,
                EffortError = name + " reasoning effort must be none, minimal, low, medium, high, xhigh, or max.",
                MapEffort = e => e.ToOpenAiResponsesWireValue(),
                ServiceTiers = ResponsesApiDialect.KnownServiceTiers,
                ServiceTierError = "ServiceTier must be auto, default, flex, scale, priority, or fast.",
                HostedToolTypes = null,
                MaxTools = null,
                AlwaysSendStore = true,
                ReadsLegacyArrayState = false,
                NotFoundHint = notFoundHint,
                ReservedFields = new HashSet<string>(ResponsesApiDialect.SharedReservedFields.Concat(ProviderFields), StringComparer.Ordinal),
            };
        }

        /// <summary>
        /// Add the OpenAI-only fields (after <c>safety_identifier</c>).
        /// </summary>
        internal static void Add(Dictionary<string, object> body, object? call, object? defaults, bool stream)
        {
            T? Value<T>(Func<OpenAiCompletionOptions, T?> select) where T : struct
            {
                T? value = call is OpenAiCompletionOptions c ? select(c) : null;
                return value ?? (defaults is OpenAiCompletionOptions d ? select(d) : null);
            }

            T? Reference<T>(Func<OpenAiCompletionOptions, T?> select) where T : class
            {
                T? value = call is OpenAiCompletionOptions c ? select(c) : null;
                return value ?? (defaults is OpenAiCompletionOptions d ? select(d) : null);
            }

            Dictionary<string, string>? metadata = Reference(o => o.Metadata);
            if (metadata != null) body["metadata"] = metadata;

            bool? background = Value(o => o.Background);
            if (background == true && !(body.TryGetValue("store", out object? store) && store is bool stored && stored))
                throw new ArgumentException("Background = true requires Store = true.");
            if (background.HasValue) body["background"] = background.Value;

            string? conversation = Reference(o => o.Conversation);
            if (!string.IsNullOrWhiteSpace(conversation)) body["conversation"] = conversation;

            int? maxToolCalls = Value(o => o.MaxToolCalls);
            if (maxToolCalls <= 0) throw new ArgumentOutOfRangeException(nameof(OpenAiCompletionOptions.MaxToolCalls), "MaxToolCalls must be positive.");
            if (maxToolCalls.HasValue) body["max_tool_calls"] = maxToolCalls.Value;

            int? topLogprobs = Value(o => o.TopLogprobs);
            if (topLogprobs < 0 || topLogprobs > 20) throw new ArgumentOutOfRangeException(nameof(OpenAiCompletionOptions.TopLogprobs), "TopLogprobs must be 0..20.");
            if (topLogprobs.HasValue) body["top_logprobs"] = topLogprobs.Value;

            JsonElement? cacheOptions = Value(o => o.PromptCacheOptions);
            if (cacheOptions.HasValue) body["prompt_cache_options"] = cacheOptions.Value;

            JsonElement? contextManagement = Value(o => o.ContextManagement);
            if (contextManagement.HasValue) body["context_management"] = contextManagement.Value;

            JsonElement? streamOptions = Value(o => o.StreamOptions);
            if (stream && streamOptions.HasValue) body["stream_options"] = streamOptions.Value;
        }

        // Fields /responses/input_tokens accepts, from a full request body.
        private static readonly HashSet<string> _InputTokenFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "model", "input", "instructions", "previous_response_id", "conversation", "parallel_tool_calls", "reasoning", "text", "tools", "tool_choice",
        };

        internal static Dictionary<string, object> InputTokensBody(Dictionary<string, object> body)
        {
            return body.Where(pair => _InputTokenFields.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        }
    }
}
