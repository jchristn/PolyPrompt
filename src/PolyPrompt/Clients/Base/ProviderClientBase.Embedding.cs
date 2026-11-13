namespace PolyPrompt.Clients
{
    using PolyPrompt.Models;
    using PolyPrompt.Telemetry;

    public abstract partial class ProviderClientBase
    {
        #region Embedding-Methods

        /// <summary>
        /// Generate a dense embedding for a single input. Settings come from <c>Defaults.Embedding</c>, overridden by
        /// <paramref name="options"/>.
        /// </summary>
        /// <param name="input">The text to embed.</param>
        /// <param name="options">Optional per-call settings: <see cref="EmbeddingOptions"/> or this provider's subtype.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An EmbeddingResponse with one embedding.</returns>
        /// <exception cref="CapabilityNotSupportedException">Thrown when <c>Supports(Capability.Embed)</c> is false for this client.</exception>
        /// <exception cref="ArgumentNullException">Thrown when input is null.</exception>
        /// <exception cref="ArgumentException">Thrown when options is another provider's options type.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the provider needs a model and none is configured.</exception>
        public Task<EmbeddingResponse> EmbedAsync(string input, EmbeddingOptions? options = null, CancellationToken token = default)
        {
            RequireCapability(Capability.Embed, nameof(EmbedAsync));
            ArgumentNullException.ThrowIfNull(input);
            string? model = ResolveEmbeddingModel(options);
            return InstrumentAsync(PolyPromptTelemetryNames.OperationEmbed, model, s => s.RecordBatchSize(1),
                () => EmbedSingleCoreAsync(input, model, options, token), token);
        }

        /// <summary>
        /// Generate dense embeddings for a batch of inputs, one per input in input order.
        /// </summary>
        /// <param name="inputs">The texts to embed. Cannot be null or empty, and no element can be null.</param>
        /// <param name="options">Optional per-call settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An EmbeddingResponse with one embedding per input.</returns>
        /// <exception cref="CapabilityNotSupportedException">Thrown when <c>Supports(Capability.Embed)</c> is false for this client.</exception>
        /// <exception cref="ArgumentNullException">Thrown when inputs is null.</exception>
        /// <exception cref="ArgumentException">Thrown when inputs is empty, contains a null element, or options is another provider's type.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the provider needs a model and none is configured.</exception>
        public Task<EmbeddingResponse> EmbedAsync(List<string> inputs, EmbeddingOptions? options = null, CancellationToken token = default)
        {
            RequireCapability(Capability.Embed, nameof(EmbedAsync));
            ValidateInputList(inputs, nameof(inputs), "Embedding requests require at least one input.");
            string? model = ResolveEmbeddingModel(options);
            return InstrumentAsync(PolyPromptTelemetryNames.OperationEmbed, model, s => s.RecordBatchSize(inputs.Count),
                () => EmbedCoreAsync(inputs, model, options, token), token);
        }

        /// <summary>
        /// Generate a sparse (lexical, SPLADE-style) embedding for a single input. Settings come from
        /// <c>Defaults.SparseEmbedding</c>.
        /// </summary>
        /// <param name="input">The text to embed.</param>
        /// <param name="options">Optional per-call settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A SparseEmbeddingResponse with one sparse vector.</returns>
        /// <exception cref="CapabilityNotSupportedException">Thrown when <c>Supports(Capability.SparseEmbed)</c> is false for this client.</exception>
        /// <exception cref="ArgumentNullException">Thrown when input is null.</exception>
        public Task<SparseEmbeddingResponse> EmbedSparseAsync(string input, SparseEmbeddingOptions? options = null, CancellationToken token = default)
        {
            RequireCapability(Capability.SparseEmbed, nameof(EmbedSparseAsync));
            ArgumentNullException.ThrowIfNull(input);
            string? model = ResolveSparseEmbeddingModel(options);
            return InstrumentAsync(PolyPromptTelemetryNames.OperationSparseEmbed, model, s => s.RecordBatchSize(1),
                () => EmbedSparseCoreAsync(new List<string> { input }, model, options, token), token);
        }

        /// <summary>
        /// Generate sparse embeddings for a batch of inputs, one per input in input order.
        /// </summary>
        /// <param name="inputs">The texts to embed. Cannot be null or empty, and no element can be null.</param>
        /// <param name="options">Optional per-call settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A SparseEmbeddingResponse with one sparse vector per input.</returns>
        /// <exception cref="CapabilityNotSupportedException">Thrown when <c>Supports(Capability.SparseEmbed)</c> is false for this client.</exception>
        /// <exception cref="ArgumentNullException">Thrown when inputs is null.</exception>
        /// <exception cref="ArgumentException">Thrown when inputs is empty or contains a null element.</exception>
        public Task<SparseEmbeddingResponse> EmbedSparseAsync(List<string> inputs, SparseEmbeddingOptions? options = null, CancellationToken token = default)
        {
            RequireCapability(Capability.SparseEmbed, nameof(EmbedSparseAsync));
            ValidateInputList(inputs, nameof(inputs), "Sparse embedding requests require at least one input.");
            string? model = ResolveSparseEmbeddingModel(options);
            return InstrumentAsync(PolyPromptTelemetryNames.OperationSparseEmbed, model, s => s.RecordBatchSize(inputs.Count),
                () => EmbedSparseCoreAsync(inputs, model, options, token), token);
        }

        #endregion

        #region Embedding-Protected

        /// <summary>
        /// True when embedding requests need a model name. Providers that serve a single server-defined model return false.
        /// </summary>
        protected virtual bool EmbeddingRequiresModel => true;

        /// <summary>
        /// True when sparse embedding requests need a model name.
        /// </summary>
        protected virtual bool SparseEmbeddingRequiresModel => true;

        /// <summary>
        /// Provider implementation of the batch <see cref="EmbedAsync(List{string}, EmbeddingOptions?, CancellationToken)"/>.
        /// </summary>
        /// <param name="inputs">Validated inputs.</param>
        /// <param name="model">Resolved model (null only when <see cref="EmbeddingRequiresModel"/> is false and none is set).</param>
        /// <param name="options">Per-call options as passed, for provider-specific settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The embedding response.</returns>
        protected virtual Task<EmbeddingResponse> EmbedCoreAsync(List<string> inputs, string? model, EmbeddingOptions? options, CancellationToken token)
        {
            throw NotSupported(Capability.Embed, nameof(EmbedAsync));
        }

        /// <summary>
        /// Provider implementation of the single-input <see cref="EmbedAsync(string, EmbeddingOptions?, CancellationToken)"/>.
        /// The default sends a batch of one; providers with a dedicated single-input endpoint override it.
        /// </summary>
        /// <param name="input">Validated input.</param>
        /// <param name="model">Resolved model.</param>
        /// <param name="options">Per-call options as passed.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The embedding response.</returns>
        protected virtual Task<EmbeddingResponse> EmbedSingleCoreAsync(string input, string? model, EmbeddingOptions? options, CancellationToken token)
        {
            return EmbedCoreAsync(new List<string> { input }, model, options, token);
        }

        /// <summary>
        /// Provider implementation of <see cref="EmbedSparseAsync(List{string}, SparseEmbeddingOptions?, CancellationToken)"/>.
        /// </summary>
        /// <param name="inputs">Validated inputs.</param>
        /// <param name="model">Resolved model.</param>
        /// <param name="options">Per-call options as passed.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The sparse embedding response.</returns>
        protected virtual Task<SparseEmbeddingResponse> EmbedSparseCoreAsync(List<string> inputs, string? model, SparseEmbeddingOptions? options, CancellationToken token)
        {
            throw NotSupported(Capability.SparseEmbed, nameof(EmbedSparseAsync));
        }

        /// <summary>
        /// Resolve the embedding model for a call: the per-call model, else <c>Defaults.Embedding.Model</c>.
        /// </summary>
        /// <param name="options">Per-call options.</param>
        /// <returns>The model, or null when none is set and none is required.</returns>
        /// <exception cref="InvalidOperationException">Thrown when a model is required and none is set.</exception>
        protected string? ResolveEmbeddingModel(EmbeddingOptions? options)
        {
            EmbeddingOptions? defaults = Defaults.Embedding;
            ValidateOptionsType(options, defaults, nameof(EmbeddingOptions));
            string? model = options?.Model ?? defaults?.Model;
            if (EmbeddingRequiresModel && string.IsNullOrWhiteSpace(model))
                throw new InvalidOperationException(NoModelMessage("Embedding"));
            return model;
        }

        /// <summary>
        /// Resolve the sparse embedding model for a call: the per-call model, else <c>Defaults.SparseEmbedding.Model</c>.
        /// </summary>
        /// <param name="options">Per-call options.</param>
        /// <returns>The model, or null when none is set and none is required.</returns>
        /// <exception cref="InvalidOperationException">Thrown when a model is required and none is set.</exception>
        protected string? ResolveSparseEmbeddingModel(SparseEmbeddingOptions? options)
        {
            SparseEmbeddingOptions? defaults = Defaults.SparseEmbedding;
            ValidateOptionsType(options, defaults, nameof(SparseEmbeddingOptions));
            string? model = options?.Model ?? defaults?.Model;
            if (SparseEmbeddingRequiresModel && string.IsNullOrWhiteSpace(model))
                throw new InvalidOperationException(NoModelMessage("SparseEmbedding"));
            return model;
        }

        #endregion
    }
}
