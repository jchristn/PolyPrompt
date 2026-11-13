namespace PolyPrompt.Clients
{
    using PolyPrompt.Models;
    using PolyPrompt.Telemetry;

    public abstract partial class ProviderClientBase
    {
        #region Models-Methods

        /// <summary>
        /// List the models available from the provider. Pagination, where the provider uses it, is followed
        /// automatically. Enumeration ends early (without throwing) when the provider returns an HTTP error.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An async enumerable of ModelInformation objects.</returns>
        /// <exception cref="CapabilityNotSupportedException">Thrown when <c>Supports(Capability.Models)</c> is false for this client.</exception>
        public IAsyncEnumerable<ModelInformation> ListModelsAsync(CancellationToken token = default)
        {
            RequireCapability(Capability.Models, nameof(ListModelsAsync));
            return ListModelsCoreAsync(token);
        }

        /// <summary>
        /// Check whether a model exists. The default implementation searches <see cref="ListModelsAsync"/>
        /// case-insensitively, and also matches a name without its tag (for example "gemma3" matches
        /// "gemma3:latest"). Returns false when the listing fails.
        /// </summary>
        /// <param name="model">The model name to search for.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the model exists, false otherwise.</returns>
        /// <exception cref="ArgumentNullException">Thrown when model is null, empty, or whitespace.</exception>
        /// <exception cref="CapabilityNotSupportedException">Thrown when <c>Supports(Capability.Models)</c> is false for this client.</exception>
        public Task<bool> ModelExistsAsync(string model, CancellationToken token = default)
        {
            RequireCapability(Capability.Models, nameof(ModelExistsAsync));
            if (string.IsNullOrWhiteSpace(model))
                throw new ArgumentNullException(nameof(model));

            return InstrumentValueAsync(PolyPromptTelemetryNames.OperationModelExists, model, () => ModelExistsCoreAsync(model, token), token);
        }

        /// <summary>
        /// Retrieve detailed information about a model.
        /// </summary>
        /// <param name="model">The model name to look up.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A ModelInformation object if found, null otherwise (including on an HTTP error).</returns>
        /// <exception cref="ArgumentNullException">Thrown when model is null, empty, or whitespace.</exception>
        /// <exception cref="CapabilityNotSupportedException">Thrown when <c>Supports(Capability.Models)</c> is false for this client.</exception>
        public Task<ModelInformation?> GetModelInformationAsync(string model, CancellationToken token = default)
        {
            RequireCapability(Capability.Models, nameof(GetModelInformationAsync));
            if (string.IsNullOrWhiteSpace(model))
                throw new ArgumentNullException(nameof(model));

            return InstrumentValueAsync(PolyPromptTelemetryNames.OperationModelInfo, model, () => GetModelInformationCoreAsync(model, token), token);
        }

        #endregion

        #region Models-Protected

        /// <summary>
        /// Provider implementation of <see cref="GetModelInformationAsync"/>.
        /// </summary>
        /// <param name="model">Validated model name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The model information, or null when not found.</returns>
        protected virtual Task<ModelInformation?> GetModelInformationCoreAsync(string model, CancellationToken token)
        {
            throw NotSupported(Capability.Models, nameof(GetModelInformationAsync));
        }

        /// <summary>
        /// Provider implementation of <see cref="ListModelsAsync"/>. Follow pagination; end early (without throwing) on an
        /// HTTP error.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The models.</returns>
        protected virtual IAsyncEnumerable<ModelInformation> ListModelsCoreAsync(CancellationToken token)
        {
            throw NotSupported(Capability.Models, nameof(ListModelsAsync));
        }

        #endregion

        #region Models-Private

        private async Task<bool> ModelExistsCoreAsync(string model, CancellationToken token)
        {
            try
            {
                await foreach (ModelInformation info in ListModelsCoreAsync(token).ConfigureAwait(false))
                {
                    token.ThrowIfCancellationRequested();

                    if (string.Equals(info.Name, model, StringComparison.OrdinalIgnoreCase))
                        return true;

                    int colonIndex = info.Name.IndexOf(':');
                    if (colonIndex > 0)
                    {
                        string baseName = info.Name.Substring(0, colonIndex);
                        if (string.Equals(baseName, model, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }

                return false;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                OperationScope.Current?.ReportError(ex.GetType().FullName ?? ex.GetType().Name);
                _Logging.Debug(_Header + "model lookup failed: " + ex.Message);
                return false;
            }
        }

        #endregion
    }
}
