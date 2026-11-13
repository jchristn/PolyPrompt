namespace PolyPrompt.Clients
{
    using System.Net.Http.Headers;
    using PolyPrompt.Models;
    using PolyPrompt.Options;

    /// <summary>
    /// xAI (Grok) client for the Responses API.
    /// <list type="bullet">
    /// <item><description>Supported: Chat, ToolChat, and Generate (<c>/v1/responses</c>, with encrypted reasoning replayed
    /// through <see cref="ResponseBase.ProviderState"/>), the native Responses operations on
    /// <see cref="ResponsesApiClientBase"/>, and Models (<c>/v1/models</c>, plus the language, embedding, image, and video
    /// catalogs).</description></item>
    /// <item><description>Not supported (throw <see cref="CapabilityNotSupportedException"/>): Embed (xAI no longer offers
    /// embedding models), SparseEmbed, Rerank, Classify, Decide.</description></item>
    /// <item><description>Auth: required API key, sent as a bearer token.</description></item>
    /// <item><description>Defaults: completion <c>grok-4.7</c>, Store = false.</description></item>
    /// </list>
    /// xAI-only settings are on <see cref="XAiCompletionOptions"/>; <see cref="XAiTools.XSearch"/> adds X search.
    /// </summary>
    /// <seealso href="https://github.com/jchristn/Polyprompt#capability-matrix">Capability matrix</seealso>
    public partial class XAiClient : ResponsesApiClientBase
    {
        #region Private-Members

        private const string DefaultEndpoint = "https://api.x.ai";

        // xAI values: PolyPrompt 3.7.0's tested behavior (reasoning effort low/medium/high/xhigh; summary auto/concise/
        // detailed; service tier auto/priority/fast; at most 1000 tools). Verified against docs.x.ai on 2026-11-12: no
        // /responses/{id}/cancel or /responses/input_tokens, background unsupported, no embeddings.
        private static readonly ResponsesApiDialect _Dialect = new ResponsesApiDialect
        {
            Name = "xAI",
            Efforts = new[] { "low", "medium", "high", "xhigh" },
            EffortError = "xAI reasoning effort must be low, medium, high, or xhigh.",
            MapEffort = e => e.ToXAiWireValue(),
            ServiceTiers = new[] { "auto", "priority", "fast" },
            ServiceTierError = "ServiceTier must be auto, priority, or fast.",
            HostedToolTypes = new[] { "web_search", "x_search", "code_interpreter", "file_search", "mcp", "image_generation", "shell", "tool_search" },
            MaxTools = 1000,
            TopPMustBePositive = true,
            ReadsLegacyArrayState = true,
            CitationTitleIsNumber = true,
            ReservedFields = new HashSet<string>(ResponsesApiDialect.SharedReservedFields.Concat(new[] { "max_turns", "top_k", "min_p", "logprobs", "top_logprobs", "guidelines" }), StringComparer.Ordinal),
        };

        #endregion

        #region Public-Members

        /// <summary>
        /// Operations this client supports: Chat, ToolChat, Generate, and Models.
        /// </summary>
        public override Capability Capabilities => Capability.Chat | Capability.ToolChat | Capability.Generate | Capability.Models;

        /// <inheritdoc />
        public override string ProviderName => "xAI";

        /// <summary>
        /// Client-wide defaults: <see cref="XAiDefaults.Completion"/> (model <c>grok-4.7</c>, Store = false).
        /// </summary>
        public override XAiDefaults Defaults { get; } = new XAiDefaults();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize an xAI client.
        /// </summary>
        /// <param name="apiKey">Required xAI API key, sent as a bearer token.</param>
        /// <param name="endpoint">API root or versioned <c>/v1</c> URL. Default: https://api.x.ai.</param>
        /// <param name="options">Optional construction settings.</param>
        /// <exception cref="ArgumentNullException">Thrown when apiKey is null.</exception>
        /// <exception cref="ArgumentException">Thrown when apiKey is blank, contains whitespace, or looks like a URL, or endpoint is not an absolute http(s) URL.</exception>
        public XAiClient(string apiKey, string? endpoint = null, ClientOptions? options = null)
            : base(ClientArguments.Endpoint(endpoint ?? DefaultEndpoint, nameof(endpoint)), ClientArguments.RequiredKey(apiKey, nameof(apiKey), null), options)
        {
            _Header = "[XAi] ";
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Probe <c>/v1/models</c>. See <see cref="HttpClientBase.ValidateConnectivityAsync"/>.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the provider accepted the credentials.</returns>
        public override Task<bool> ValidateConnectivityAsync(CancellationToken token = default) => ProbeAsync(Url("models"), token);

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        private protected override ResponsesApiDialect Dialect => _Dialect;

        /// <inheritdoc />
        protected override string? ModelCatalogUrl => "https://docs.x.ai/docs/models";

        /// <inheritdoc />
        protected override string Url(string path) => OpenAiProtocol.BuildApiUrl(_Endpoint, path);

        /// <inheritdoc />
        protected override Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _ApiKey);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        private protected override void AddProviderFields(Dictionary<string, object> body, ResolvedCompletion settings, bool stream, ResponsesApiFieldStage stage)
        {
            void Add(string name, object? value) { if (value != null) body[name] = value; }
            if (stage == ResponsesApiFieldStage.AfterParallelToolCalls)
            {
                int? turns = Value<XAiCompletionOptions, int>(settings, o => o.MaxTurns);
                if (turns <= 0) throw new ArgumentOutOfRangeException(nameof(XAiCompletionOptions.MaxTurns));
                Add("max_turns", turns);
                int? topK = Value<XAiCompletionOptions, int>(settings, o => o.TopK);
                if (topK <= 0) throw new ArgumentOutOfRangeException(nameof(XAiCompletionOptions.TopK));
                Add("top_k", topK);
                double? minP = Value<XAiCompletionOptions, double>(settings, o => o.MinP);
                if (minP.HasValue && (!double.IsFinite(minP.Value) || minP < 0 || minP > 1)) throw new ArgumentOutOfRangeException(nameof(XAiCompletionOptions.MinP));
                Add("min_p", minP);
                bool? logprobs = Value<XAiCompletionOptions, bool>(settings, o => o.Logprobs);
                Add("logprobs", logprobs);
                int? topLogprobs = Value<XAiCompletionOptions, int>(settings, o => o.TopLogprobs);
                if (topLogprobs < 0 || topLogprobs > 8 || (topLogprobs.HasValue && logprobs != true)) throw new ArgumentException("TopLogprobs must be 0..8 and requires Logprobs=true.");
                Add("top_logprobs", topLogprobs);
            }
            else if (stage == ResponsesApiFieldStage.AfterSafetyIdentifier)
            {
                Add("guidelines", Reference<XAiCompletionOptions, List<System.Text.Json.JsonElement>>(settings, o => o.Guidelines));
            }
        }

        #endregion
    }

    /// <summary>
    /// Defaults for <see cref="XAiClient"/>.
    /// </summary>
    public sealed class XAiDefaults : ClientDefaults
    {
        /// <summary>
        /// Completion defaults, including xAI settings such as <see cref="XAiCompletionOptions.MaxTurns"/>. Default model: <c>grok-4.7</c>; storage is off.
        /// </summary>
        public override XAiCompletionOptions Completion { get; } = new XAiCompletionOptions { Model = "grok-4.7", Store = false };

        /// <summary>Not supported by xAI; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("xAI does not support embeddings.", true)]
        public new EmbeddingOptions? Embedding => null;

        /// <summary>Not supported by xAI; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("xAI does not support sparse embeddings.", true)]
        public new SparseEmbeddingOptions? SparseEmbedding => null;

        /// <summary>Not supported by xAI; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("xAI does not support reranking.", true)]
        public new RerankOptions? Rerank => null;

        /// <summary>Not supported by xAI; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("xAI does not support classification.", true)]
        public new ClassificationOptions? Classification => null;

        /// <summary>Not supported by xAI; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("xAI does not support decisions.", true)]
        public new DecisionOptions? Decision => null;

    }
}
