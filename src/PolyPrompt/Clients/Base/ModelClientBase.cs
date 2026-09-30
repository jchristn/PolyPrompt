namespace PolyPrompt.Clients
{
    using PolyPrompt.Models;
    using SyslogLogging;

    /// <summary>
    /// Base class for model catalog clients: list the models a provider serves, check whether one exists, and look up
    /// its details. Providers that can also download or remove models (Ollama) add those operations on their own client.
    /// </summary>
    public abstract class ModelClientBase : ClientBase
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new model client.
        /// </summary>
        /// <param name="endpoint">Endpoint URL.</param>
        /// <param name="apiKey">API key (nullable).</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="httpClient">Optional HTTP client; see <see cref="ClientBase"/>.</param>
        protected ModelClientBase(string endpoint, string? apiKey, LoggingModule logging, HttpClient? httpClient = null)
            : base(endpoint, apiKey, logging, httpClient)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// List the models available from the provider. Pagination, where the provider uses it, is followed
        /// automatically. Enumeration ends early (without throwing) when the provider returns an HTTP error.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An async enumerable of ModelInformation objects.</returns>
        public abstract IAsyncEnumerable<ModelInformation> ListModelsAsync(CancellationToken token = default);

        /// <summary>
        /// Check whether a model exists. The default implementation searches <see cref="ListModelsAsync"/>
        /// case-insensitively, and also matches a name without its tag (for example "gemma3" matches
        /// "gemma3:latest"). Returns false when the listing fails.
        /// </summary>
        /// <param name="model">The model name to search for.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the model exists, false otherwise.</returns>
        /// <exception cref="ArgumentNullException">Thrown when model is null, empty, or whitespace.</exception>
        public virtual async Task<bool> ModelExistsAsync(string model, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(model))
                throw new ArgumentNullException(nameof(model));

            try
            {
                await foreach (ModelInformation info in ListModelsAsync(token).ConfigureAwait(false))
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
                _Logging.Debug(_Header + "model lookup failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Retrieve detailed information about a model.
        /// </summary>
        /// <param name="model">The model name to look up.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A ModelInformation object if found, null otherwise (including on an HTTP error).</returns>
        /// <exception cref="ArgumentNullException">Thrown when model is null, empty, or whitespace.</exception>
        public Task<ModelInformation?> GetModelInformationAsync(string model, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(model))
                throw new ArgumentNullException(nameof(model));

            return GetModelInformationCoreAsync(model, token);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Provider implementation of <see cref="GetModelInformationAsync"/>.
        /// </summary>
        /// <param name="model">Validated model name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The model information, or null when not found.</returns>
        protected abstract Task<ModelInformation?> GetModelInformationCoreAsync(string model, CancellationToken token);

        #endregion
    }
}
