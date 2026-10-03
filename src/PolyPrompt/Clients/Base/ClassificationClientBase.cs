namespace PolyPrompt.Clients
{
    using PolyPrompt.Models;
    using PolyPrompt.Telemetry;
    using SyslogLogging;

    /// <summary>
    /// Base class for text classification clients, which assign labels from a model's trained or example-defined label
    /// set. Supports a single input and a batch of inputs. For instruction-driven questions over arbitrary state with
    /// calibrated confidence, see <see cref="DecisionClientBase"/>.
    /// </summary>
    public abstract class ClassificationClientBase : ClientBase
    {
        #region Public-Members

        /// <summary>
        /// Client-wide default settings. Each provider exposes its own options type here. Per-call options override
        /// these values field by field.
        /// </summary>
        public abstract ClassificationOptions Defaults { get; }

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
        /// Initialize a new classification client.
        /// </summary>
        /// <param name="endpoint">Endpoint URL.</param>
        /// <param name="apiKey">API key (nullable).</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="httpClient">Optional HTTP client; see <see cref="ClientBase"/>.</param>
        protected ClassificationClientBase(string endpoint, string? apiKey, LoggingModule logging, HttpClient? httpClient = null)
            : base(endpoint, apiKey, logging, httpClient)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Classify a single input.
        /// </summary>
        /// <param name="input">The text to classify.</param>
        /// <param name="options">Optional per-call settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A ClassificationResponse with one result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when input is null.</exception>
        public Task<ClassificationResponse> ClassifyAsync(string input, ClassificationOptions? options = null, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            string? model = ResolveModel(options);
            return InstrumentAsync(PolyPromptTelemetryNames.OperationClassify, model, s => s.RecordBatchSize(1),
                () => ClassifyCoreAsync(new List<string> { input }, model, options, token), token);
        }

        /// <summary>
        /// Classify a batch of inputs, returning one result per input in input order.
        /// </summary>
        /// <param name="inputs">The texts to classify. Cannot be null or empty, and no element can be null.</param>
        /// <param name="options">Optional per-call settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A ClassificationResponse with one result per input.</returns>
        /// <exception cref="ArgumentNullException">Thrown when inputs is null.</exception>
        /// <exception cref="ArgumentException">Thrown when inputs is empty or contains a null element.</exception>
        public Task<ClassificationResponse> ClassifyAsync(List<string> inputs, ClassificationOptions? options = null, CancellationToken token = default)
        {
            ValidateInputList(inputs, nameof(inputs), "Classification requests require at least one input.");
            string? model = ResolveModel(options);
            return InstrumentAsync(PolyPromptTelemetryNames.OperationClassify, model, s => s.RecordBatchSize(inputs.Count),
                () => ClassifyCoreAsync(inputs, model, options, token), token);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// True when requests need a model name. Providers that serve a single model, or pick one themselves,
        /// return false.
        /// </summary>
        protected virtual bool RequiresModel => true;

        /// <summary>
        /// Provider implementation of <see cref="ClassifyAsync(List{string}, ClassificationOptions?, CancellationToken)"/>.
        /// </summary>
        /// <param name="inputs">Validated inputs.</param>
        /// <param name="model">Resolved model.</param>
        /// <param name="options">Per-call options as passed, for provider-specific settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The classification response.</returns>
        protected abstract Task<ClassificationResponse> ClassifyCoreAsync(List<string> inputs, string? model, ClassificationOptions? options, CancellationToken token);

        /// <summary>
        /// Resolve the model for a call: the per-call model, else <c>Defaults.Model</c>.
        /// </summary>
        /// <param name="options">Per-call options.</param>
        /// <returns>The model, or null when none is set and none is required.</returns>
        /// <exception cref="InvalidOperationException">Thrown when a model is required and none is set.</exception>
        protected string? ResolveModel(ClassificationOptions? options)
        {
            string? model = options?.Model ?? Defaults.Model;
            if (RequiresModel && string.IsNullOrWhiteSpace(model))
                throw new InvalidOperationException("No model is configured. Set Model or pass a model in the options.");
            return model;
        }

        #endregion
    }
}
