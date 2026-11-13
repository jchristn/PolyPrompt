namespace PolyPrompt.Clients
{
    using PolyPrompt.Models;
    using PolyPrompt.Telemetry;

    public abstract partial class ProviderClientBase
    {
        #region Classification-Methods

        /// <summary>
        /// Classify a single input with labels from the model's trained or example-defined label set. Settings come from
        /// <c>Defaults.Classification</c>. For instruction-driven questions with calibrated confidence, see
        /// <see cref="DecideAsync(DecisionRequest, DecisionOptions?, CancellationToken)"/>.
        /// </summary>
        /// <param name="input">The text to classify.</param>
        /// <param name="options">Optional per-call settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A ClassificationResponse with one result.</returns>
        /// <exception cref="CapabilityNotSupportedException">Thrown when <c>Supports(Capability.Classify)</c> is false for this client.</exception>
        /// <exception cref="ArgumentNullException">Thrown when input is null.</exception>
        public Task<ClassificationResponse> ClassifyAsync(string input, ClassificationOptions? options = null, CancellationToken token = default)
        {
            RequireCapability(Capability.Classify, nameof(ClassifyAsync));
            ArgumentNullException.ThrowIfNull(input);
            string? model = ResolveClassificationModel(options);
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
        /// <exception cref="CapabilityNotSupportedException">Thrown when <c>Supports(Capability.Classify)</c> is false for this client.</exception>
        /// <exception cref="ArgumentNullException">Thrown when inputs is null.</exception>
        /// <exception cref="ArgumentException">Thrown when inputs is empty or contains a null element.</exception>
        public Task<ClassificationResponse> ClassifyAsync(List<string> inputs, ClassificationOptions? options = null, CancellationToken token = default)
        {
            RequireCapability(Capability.Classify, nameof(ClassifyAsync));
            ValidateInputList(inputs, nameof(inputs), "Classification requests require at least one input.");
            string? model = ResolveClassificationModel(options);
            return InstrumentAsync(PolyPromptTelemetryNames.OperationClassify, model, s => s.RecordBatchSize(inputs.Count),
                () => ClassifyCoreAsync(inputs, model, options, token), token);
        }

        #endregion

        #region Classification-Protected

        /// <summary>
        /// True when classification requests need a model name. Providers that serve a single model, or pick one
        /// themselves, return false.
        /// </summary>
        protected virtual bool ClassificationRequiresModel => true;

        /// <summary>
        /// Provider implementation of <see cref="ClassifyAsync(List{string}, ClassificationOptions?, CancellationToken)"/>.
        /// </summary>
        /// <param name="inputs">Validated inputs.</param>
        /// <param name="model">Resolved model.</param>
        /// <param name="options">Per-call options as passed.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The classification response.</returns>
        protected virtual Task<ClassificationResponse> ClassifyCoreAsync(List<string> inputs, string? model, ClassificationOptions? options, CancellationToken token)
        {
            throw NotSupported(Capability.Classify, nameof(ClassifyAsync));
        }

        /// <summary>
        /// Resolve the classification model: the per-call model, else <c>Defaults.Classification.Model</c>.
        /// </summary>
        /// <param name="options">Per-call options.</param>
        /// <returns>The model, or null when none is set and none is required.</returns>
        /// <exception cref="InvalidOperationException">Thrown when a model is required and none is set.</exception>
        protected string? ResolveClassificationModel(ClassificationOptions? options)
        {
            ClassificationOptions? defaults = Defaults.Classification;
            ValidateOptionsType(options, defaults, nameof(ClassificationOptions));
            string? model = options?.Model ?? defaults?.Model;
            if (ClassificationRequiresModel && string.IsNullOrWhiteSpace(model))
                throw new InvalidOperationException(NoModelMessage("Classification"));
            return model;
        }

        #endregion
    }
}
