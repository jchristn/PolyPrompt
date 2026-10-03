namespace PolyPrompt.Clients
{
    using PolyPrompt.Models;
    using PolyPrompt.Telemetry;
    using SyslogLogging;

    /// <summary>
    /// Base class for sparse (lexical, SPLADE-style) embedding clients. Supports a single input and a batch of inputs.
    /// Settings come from <see cref="Defaults"/>, overridden per call by a <see cref="SparseEmbeddingOptions"/>.
    /// </summary>
    public abstract class SparseEmbeddingClientBase : ClientBase
    {
        #region Public-Members

        /// <summary>
        /// Client-wide default settings. Each provider exposes its own options type here. Per-call options override
        /// these values field by field.
        /// </summary>
        public abstract SparseEmbeddingOptions Defaults { get; }

        /// <summary>
        /// Model name used when a call does not set one. Reads and writes <c>Defaults.Model</c>.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null, empty, or whitespace.</exception>
        public string? Model
        {
            get { return Defaults.Model; }
            set
            {
                if (string.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Model));
                Defaults.Model = value;
            }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new sparse embedding client.
        /// </summary>
        /// <param name="endpoint">Endpoint URL.</param>
        /// <param name="apiKey">API key (nullable).</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="httpClient">Optional HTTP client; see <see cref="ClientBase"/>.</param>
        protected SparseEmbeddingClientBase(string endpoint, string? apiKey, LoggingModule logging, HttpClient? httpClient = null)
            : base(endpoint, apiKey, logging, httpClient)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Generate a sparse embedding for a single input.
        /// </summary>
        /// <param name="input">The text to embed.</param>
        /// <param name="options">Optional per-call settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A SparseEmbeddingResponse with one sparse vector.</returns>
        /// <exception cref="ArgumentNullException">Thrown when input is null.</exception>
        public Task<SparseEmbeddingResponse> EmbedSparseAsync(string input, SparseEmbeddingOptions? options = null, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            string? model = ResolveModel(options);
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
        /// <exception cref="ArgumentNullException">Thrown when inputs is null.</exception>
        /// <exception cref="ArgumentException">Thrown when inputs is empty or contains a null element.</exception>
        public Task<SparseEmbeddingResponse> EmbedSparseAsync(List<string> inputs, SparseEmbeddingOptions? options = null, CancellationToken token = default)
        {
            ValidateInputList(inputs, nameof(inputs), "Sparse embedding requests require at least one input.");
            string? model = ResolveModel(options);
            return InstrumentAsync(PolyPromptTelemetryNames.OperationSparseEmbed, model, s => s.RecordBatchSize(inputs.Count),
                () => EmbedSparseCoreAsync(inputs, model, options, token), token);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// True when requests need a model name. Providers that serve a single server-defined model return false.
        /// </summary>
        protected virtual bool RequiresModel => true;

        /// <summary>
        /// Provider implementation of <see cref="EmbedSparseAsync(List{string}, SparseEmbeddingOptions?, CancellationToken)"/>.
        /// </summary>
        /// <param name="inputs">Validated inputs.</param>
        /// <param name="model">Resolved model.</param>
        /// <param name="options">Per-call options as passed, for provider-specific settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The sparse embedding response.</returns>
        protected abstract Task<SparseEmbeddingResponse> EmbedSparseCoreAsync(List<string> inputs, string? model, SparseEmbeddingOptions? options, CancellationToken token);

        /// <summary>
        /// Resolve the model for a call: the per-call model, else <c>Defaults.Model</c>.
        /// </summary>
        /// <param name="options">Per-call options.</param>
        /// <returns>The model, or null when none is set and none is required.</returns>
        /// <exception cref="InvalidOperationException">Thrown when a model is required and none is set.</exception>
        protected string? ResolveModel(SparseEmbeddingOptions? options)
        {
            string? model = options?.Model ?? Defaults.Model;
            if (RequiresModel && string.IsNullOrWhiteSpace(model))
                throw new InvalidOperationException("No model is configured. Set Model or pass a model in the options.");
            return model;
        }

        #endregion
    }
}
