namespace PolyPrompt.Clients
{
    using PolyPrompt.Models;

    /// <summary>
    /// Ollama Cloud client for the hosted native Ollama API at https://ollama.com. It inherits every
    /// <see cref="OllamaClient"/> operation and setting and requires bearer authentication.
    /// <list type="bullet">
    /// <item><description>Supported: Chat, ToolChat, Generate, Embed, and Models (the API supports embeddings; the hosted
    /// catalog may not include embedding models).</description></item>
    /// <item><description>Not supported (throw <see cref="CapabilityNotSupportedException"/>): SparseEmbed, Rerank,
    /// Classify, Decide.</description></item>
    /// <item><description>Auth: required bearer API key.</description></item>
    /// <item><description>Defaults: none. The hosted catalog changes, so set <c>Defaults.Completion.Model</c> (for example
    /// <c>gpt-oss:120b</c>) and <c>Defaults.Embedding.Model</c>; a call without a model throws
    /// <see cref="InvalidOperationException"/>.</description></item>
    /// </list>
    /// </summary>
    /// <seealso href="https://github.com/jchristn/Polyprompt#capability-matrix">Capability matrix</seealso>
    public sealed class OllamaCloudClient : OllamaClient
    {
        /// <inheritdoc />
        public override string ProviderName => "Ollama Cloud";

        /// <summary>
        /// Initialize an Ollama Cloud client.
        /// </summary>
        /// <param name="apiKey">Required Ollama Cloud API key (bearer token).</param>
        /// <param name="endpoint">Endpoint override. Default: https://ollama.com:443.</param>
        /// <param name="options">Optional construction settings.</param>
        /// <exception cref="ArgumentException">Thrown when apiKey is blank, contains whitespace, or looks like a URL, or endpoint is not an absolute http(s) URL.</exception>
        public OllamaCloudClient(string apiKey, string? endpoint = null, ClientOptions? options = null)
            : base(endpoint ?? OllamaCloudProtocol.Endpoint, ClientArguments.RequiredKey(apiKey, nameof(apiKey), null), options, new OllamaDefaults(null, null))
        {
            _Header = "[OllamaCloud] ";
        }

        /// <inheritdoc />
        protected override string? ModelCatalogUrl => "https://ollama.com/search?c=cloud";
    }
}
