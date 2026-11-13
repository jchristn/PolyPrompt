namespace PolyPrompt.Models
{
    /// <summary>
    /// The model-API operations a provider client can support. Every provider client exposes every operation; a client's
    /// <c>Capabilities</c> says which ones work, and an operation outside that set throws
    /// <see cref="CapabilityNotSupportedException"/> before any request is sent. Check with <c>client.Supports(...)</c>.
    /// Flags describe provider-level support: a particular model may still lack a feature (for example tool calling).
    /// See the capability matrix: https://github.com/jchristn/Polyprompt#capability-matrix
    /// </summary>
    [Flags]
    public enum Capability
    {
        /// <summary>No capability.</summary>
        None = 0,

        /// <summary>Single-prompt chat: <c>ChatAsync</c> and <c>ChatStreamingAsync</c>.</summary>
        Chat = 1 << 0,

        /// <summary>Message-based chat with optional tools: <c>ToolChatAsync</c> and <c>ToolChatStreamingAsync</c>.</summary>
        ToolChat = 1 << 1,

        /// <summary>Text generation: <c>GenerateAsync</c> and <c>GenerateStreamingAsync</c>.</summary>
        Generate = 1 << 2,

        /// <summary>Dense embeddings: <c>EmbedAsync</c>.</summary>
        Embed = 1 << 3,

        /// <summary>Sparse (lexical) embeddings: <c>EmbedSparseAsync</c>.</summary>
        SparseEmbed = 1 << 4,

        /// <summary>Document reranking: <c>RerankAsync</c>.</summary>
        Rerank = 1 << 5,

        /// <summary>Text classification: <c>ClassifyAsync</c>.</summary>
        Classify = 1 << 6,

        /// <summary>Decision models: <c>DecideAsync</c>.</summary>
        Decide = 1 << 7,

        /// <summary>Model catalog: <c>ListModelsAsync</c>, <c>ModelExistsAsync</c>, and <c>GetModelInformationAsync</c>.</summary>
        Models = 1 << 8,
    }
}
