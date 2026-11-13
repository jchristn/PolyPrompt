namespace PolyPrompt.Options
{
    using System.Text.Json;

    /// <summary>
    /// OpenAI Responses API settings for <see cref="Clients.OpenAiClient"/> and <see cref="Clients.AzureOpenAiClient"/>:
    /// the shared <see cref="ResponsesApiCompletionOptions"/> plus OpenAI-only request fields. (The 3.x Chat Completions
    /// settings with this name are now <see cref="OpenAiCompatibleCompletionOptions"/>.) Fields PolyPrompt does not model
    /// (<c>prompt</c>, <c>truncation</c>, <c>prompt_cache_retention</c>, <c>image</c>, <c>moderation</c>) go in
    /// <see cref="ResponsesApiCompletionOptions.AdditionalParameters"/>.
    /// </summary>
    public class OpenAiCompletionOptions : ResponsesApiCompletionOptions
    {
        /// <summary>
        /// Up to 16 key/value pairs attached to the response (<c>metadata</c>).
        /// </summary>
        public Dictionary<string, string>? Metadata { get; set; }

        /// <summary>
        /// Run the response in the background (<c>background</c>). Requires <see cref="ResponsesApiCompletionOptions.Store"/>
        /// = true; PolyPrompt throws <see cref="ArgumentException"/> otherwise. Poll with <c>GetResponseAsync</c> and stop
        /// with <c>CancelResponseAsync</c>.
        /// </summary>
        public bool? Background { get; set; }

        /// <summary>
        /// Conversation id the response belongs to (<c>conversation</c>).
        /// </summary>
        public string? Conversation { get; set; }

        /// <summary>
        /// Maximum number of hosted tool calls in the response (<c>max_tool_calls</c>). Must be positive.
        /// </summary>
        public int? MaxToolCalls { get; set; }

        /// <summary>
        /// Number of most likely tokens to return per position, 0..20 (<c>top_logprobs</c>). Add
        /// <c>message.output_text.logprobs</c> to <see cref="ResponsesApiCompletionOptions.Include"/> to receive them.
        /// </summary>
        public int? TopLogprobs { get; set; }

        /// <summary>
        /// Prompt cache options object (<c>prompt_cache_options</c>), for example <c>{ "ttl": "24h" }</c>.
        /// </summary>
        public JsonElement? PromptCacheOptions { get; set; }

        /// <summary>
        /// Server-side context management (<c>context_management</c>), for example
        /// <c>[{ "type": "compaction", "compact_threshold": 200000 }]</c>.
        /// </summary>
        public JsonElement? ContextManagement { get; set; }

        /// <summary>
        /// Streaming options object (<c>stream_options</c>), sent only on streaming calls.
        /// </summary>
        public JsonElement? StreamOptions { get; set; }
    }
}
