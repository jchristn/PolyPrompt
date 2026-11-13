namespace PolyPrompt.Clients
{
    using PolyPrompt.Models;

    /// <summary>
    /// Client-wide default settings, one section per capability. Each provider client exposes its own derived type (for
    /// example <c>OpenAiDefaults</c>) whose sections are typed with that provider's options and exist only for the
    /// capabilities it supports. Read through this base type, a section the provider does not support is null.
    /// Per-call options override a section field by field: a non-null per-call value wins, then the section, then the
    /// provider's own default.
    /// </summary>
    public abstract class ClientDefaults
    {
        /// <summary>
        /// Completion defaults (chat, tool chat, generation), or null when the provider has no completion capability.
        /// </summary>
        public virtual CompletionOptions? Completion => null;

        /// <summary>
        /// Embedding defaults, or null when the provider does not support embeddings.
        /// </summary>
        public virtual EmbeddingOptions? Embedding => null;

        /// <summary>
        /// Sparse embedding defaults, or null when the provider does not support sparse embeddings.
        /// </summary>
        public virtual SparseEmbeddingOptions? SparseEmbedding => null;

        /// <summary>
        /// Rerank defaults, or null when the provider does not support reranking.
        /// </summary>
        public virtual RerankOptions? Rerank => null;

        /// <summary>
        /// Classification defaults, or null when the provider does not support classification.
        /// </summary>
        public virtual ClassificationOptions? Classification => null;

        /// <summary>
        /// Decision defaults, or null when the provider does not support decisions.
        /// </summary>
        public virtual DecisionOptions? Decision => null;
    }
}
