namespace PolyPrompt.Clients
{
    using PolyPrompt.Models;
    using PolyPrompt.Telemetry;
    using SyslogLogging;

    /// <summary>
    /// Base class for dense embedding clients. Supports a single input and a batch of inputs. Settings come from
    /// <see cref="Defaults"/>, overridden per call by an <see cref="EmbeddingOptions"/> (or a provider-specific subtype).
    /// </summary>
    public abstract class EmbeddingClientBase : ClientBase
    {
        #region Public-Members

        /// <summary>
        /// Client-wide default settings. Each provider exposes its own options type here. Per-call options override
        /// these values field by field.
        /// </summary>
        public abstract EmbeddingOptions Defaults { get; }

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
        /// Initialize a new embedding client.
        /// </summary>
        /// <param name="endpoint">Endpoint URL.</param>
        /// <param name="apiKey">API key (nullable).</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="httpClient">Optional HTTP client; see <see cref="ClientBase"/>.</param>
        protected EmbeddingClientBase(string endpoint, string? apiKey, LoggingModule logging, HttpClient? httpClient = null)
            : base(endpoint, apiKey, logging, httpClient)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Generate an embedding for a single input.
        /// </summary>
        /// <param name="input">The text to embed.</param>
        /// <param name="options">Optional per-call settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An EmbeddingResponse with one embedding.</returns>
        /// <exception cref="ArgumentNullException">Thrown when input is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the provider needs a model and none is configured.</exception>
        public Task<EmbeddingResponse> EmbedAsync(string input, EmbeddingOptions? options = null, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            string? model = ResolveModel(options);
            return InstrumentAsync(PolyPromptTelemetryNames.OperationEmbed, model, s => s.RecordBatchSize(1),
                () => EmbedSingleCoreAsync(input, model, options, token), token);
        }

        /// <summary>
        /// Generate embeddings for a batch of inputs, one per input in input order.
        /// </summary>
        /// <param name="inputs">The texts to embed. Cannot be null or empty, and no element can be null.</param>
        /// <param name="options">Optional per-call settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An EmbeddingResponse with one embedding per input.</returns>
        /// <exception cref="ArgumentNullException">Thrown when inputs is null.</exception>
        /// <exception cref="ArgumentException">Thrown when inputs is empty or contains a null element.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the provider needs a model and none is configured.</exception>
        public Task<EmbeddingResponse> EmbedAsync(List<string> inputs, EmbeddingOptions? options = null, CancellationToken token = default)
        {
            ValidateInputList(inputs, nameof(inputs), "Embedding requests require at least one input.");
            string? model = ResolveModel(options);
            return InstrumentAsync(PolyPromptTelemetryNames.OperationEmbed, model, s => s.RecordBatchSize(inputs.Count),
                () => EmbedCoreAsync(inputs, model, options, token), token);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// True when requests need a model name. Providers that serve a single server-defined model return false.
        /// </summary>
        protected virtual bool RequiresModel => true;

        /// <summary>
        /// Provider implementation of the batch <see cref="EmbedAsync(List{string}, EmbeddingOptions?, CancellationToken)"/>.
        /// </summary>
        /// <param name="inputs">Validated inputs.</param>
        /// <param name="model">Resolved model (null only when <see cref="RequiresModel"/> is false and none is set).</param>
        /// <param name="options">Per-call options as passed, for provider-specific settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The embedding response.</returns>
        protected abstract Task<EmbeddingResponse> EmbedCoreAsync(List<string> inputs, string? model, EmbeddingOptions? options, CancellationToken token);

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
        /// Resolve the model for a call: the per-call model, else <c>Defaults.Model</c>.
        /// </summary>
        /// <param name="options">Per-call options.</param>
        /// <returns>The model, or null when none is set and none is required.</returns>
        /// <exception cref="InvalidOperationException">Thrown when a model is required and none is set.</exception>
        protected string? ResolveModel(EmbeddingOptions? options)
        {
            string? model = options?.Model ?? Defaults.Model;
            if (RequiresModel && string.IsNullOrWhiteSpace(model))
                throw new InvalidOperationException("No model is configured. Set Model or pass a model in the options.");
            return model;
        }

        #endregion
    }
}
