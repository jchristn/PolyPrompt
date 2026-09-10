namespace PolyPrompt.Models
{
    /// <summary>
    /// Token usage and timing statistics from a chat completion.
    /// </summary>
    public class ChatStreamingUsage
    {
        /// <summary>
        /// Number of tokens in the prompt.
        /// </summary>
        public int? PromptTokens { get; set; }

        /// <summary>
        /// Number of tokens generated in the completion.
        /// </summary>
        public int? CompletionTokens { get; set; }

        /// <summary>
        /// Total tokens (prompt + completion).
        /// </summary>
        public int? TotalTokens { get; set; }

        /// <summary>
        /// Prompt tokens served from the provider's prompt cache (a cache read). Null when the provider does
        /// not report cache usage. Note the cross-provider semantic: on OpenAI, Azure OpenAI, Gemini, and
        /// Vertex this is a subset of <see cref="PromptTokens"/> (the provider already counts cached tokens
        /// inside the prompt total); on Anthropic and Bedrock it is additional to <see cref="PromptTokens"/>
        /// (the provider reports only the uncached portion as the prompt total). Ollama does not report it.
        /// </summary>
        public int? CachedPromptTokens { get; set; }

        /// <summary>
        /// Prompt tokens written into the provider's prompt cache (a cache-creation/write), billed at a
        /// premium by the providers that report it (Anthropic, Bedrock). Null when the provider does not
        /// report a distinct cache-write count (OpenAI, Azure OpenAI, Gemini, Vertex, Ollama).
        /// </summary>
        public int? CacheCreationTokens { get; set; }

        /// <summary>
        /// Reasoning/thinking tokens billed separately by the model (OpenAI and Azure OpenAI reasoning
        /// models, Gemini and Vertex thinking). Null when the provider does not report a distinct
        /// reasoning-token count (Anthropic and Bedrock bill extended thinking as ordinary output tokens;
        /// Ollama streams thinking as text only).
        /// </summary>
        public int? ReasoningTokens { get; set; }

        /// <summary>
        /// Total request duration in nanoseconds (Ollama only).
        /// </summary>
        public long? TotalDurationNs { get; set; }

        /// <summary>
        /// Model load duration in nanoseconds (Ollama only).
        /// </summary>
        public long? LoadDurationNs { get; set; }

        /// <summary>
        /// Prompt evaluation duration in nanoseconds (Ollama only).
        /// </summary>
        public long? PromptEvalDurationNs { get; set; }

        /// <summary>
        /// Token generation duration in nanoseconds (Ollama only).
        /// </summary>
        public long? EvalDurationNs { get; set; }
    }
}
