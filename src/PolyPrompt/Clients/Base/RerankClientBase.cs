namespace PolyPrompt.Clients
{
    using PolyPrompt.Models;
    using SyslogLogging;

    /// <summary>
    /// Base class for rerank clients, which score documents for relevance to a query. Results behave identically on
    /// every provider: they are sorted by score (highest first, ties by index), results whose index does not map to an
    /// input document are dropped, <see cref="RerankOptions.TopN"/> is applied (natively where the provider supports it,
    /// client-side otherwise), and <see cref="RerankOptions.ReturnDocuments"/> attaches document text from the caller's
    /// list. Settings come from <see cref="Defaults"/>, overridden per call by a <see cref="RerankOptions"/>.
    /// </summary>
    public abstract class RerankClientBase : ClientBase
    {
        #region Public-Members

        /// <summary>
        /// Client-wide default settings. Each provider exposes its own options type here. Per-call options override
        /// these values field by field.
        /// </summary>
        public abstract RerankOptions Defaults { get; }

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
        /// Initialize a new rerank client.
        /// </summary>
        /// <param name="endpoint">Endpoint URL.</param>
        /// <param name="apiKey">API key (nullable).</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="httpClient">Optional HTTP client; see <see cref="ClientBase"/>.</param>
        protected RerankClientBase(string endpoint, string? apiKey, LoggingModule logging, HttpClient? httpClient = null)
            : base(endpoint, apiKey, logging, httpClient)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Score a list of documents for relevance to a query and return them highest score first.
        /// </summary>
        /// <param name="query">The query to score documents against. Cannot be null, empty, or whitespace.</param>
        /// <param name="documents">The documents to score. Cannot be null or empty, and no element can be null.</param>
        /// <param name="options">Optional per-call settings. A per-call TopN cannot exceed the number of documents.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A RerankResponse whose Results are sorted by score, highest first.</returns>
        /// <exception cref="ArgumentNullException">Thrown when query or documents is null.</exception>
        /// <exception cref="ArgumentException">Thrown when query is empty or whitespace, documents is empty, or a document is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when a per-call TopN exceeds the number of documents.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the provider needs a model and none is configured.</exception>
        public async Task<RerankResponse> RerankAsync(
            string query,
            List<string> documents,
            RerankOptions? options = null,
            CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("Rerank query cannot be empty or whitespace.", nameof(query));

            ValidateInputList(documents, nameof(documents), "Rerank requests require at least one document.");

            if (options?.TopN != null && options.TopN.Value > documents.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    options.TopN.Value,
                    "TopN (" + options.TopN.Value + ") cannot exceed the number of documents (" + documents.Count + ").");
            }

            int? topN = options?.TopN ?? Defaults.TopN;
            if (topN.HasValue && topN.Value > documents.Count) topN = documents.Count;

            bool returnDocuments = options?.ReturnDocuments ?? Defaults.ReturnDocuments ?? false;

            string? model = options?.Model ?? Defaults.Model;
            if (RequiresModel && string.IsNullOrWhiteSpace(model))
                throw new InvalidOperationException("No model is configured. Set Model or pass a model in the options.");

            RerankResponse response = await RerankCoreAsync(query, documents, model, topN, options, token).ConfigureAwait(false);
            if (response.Success) FinalizeResults(response, documents, topN, returnDocuments);
            return response;
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// True when requests need a model name. Providers that serve a single server-defined model return false.
        /// </summary>
        protected virtual bool RequiresModel => true;

        /// <summary>
        /// Provider implementation of <see cref="RerankAsync"/>. The base class sorts, filters, trims, and attaches
        /// documents afterwards, so the implementation only parses the provider's scores.
        /// </summary>
        /// <param name="query">Validated query.</param>
        /// <param name="documents">Validated documents.</param>
        /// <param name="model">Resolved model.</param>
        /// <param name="topN">Effective TopN (at most the document count), or null for all; send natively when supported.</param>
        /// <param name="options">Per-call options as passed, for provider-specific settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The rerank response with unsorted results.</returns>
        protected abstract Task<RerankResponse> RerankCoreAsync(
            string query,
            List<string> documents,
            string? model,
            int? topN,
            RerankOptions? options,
            CancellationToken token);

        #endregion

        #region Private-Methods

        private static void FinalizeResults(RerankResponse response, List<string> documents, int? topN, bool returnDocuments)
        {
            List<RerankResult> ordered = response.Results
                .Where(r => r.Index >= 0 && r.Index < documents.Count)
                .OrderByDescending(r => r.Score)
                .ThenBy(r => r.Index)
                .ToList();

            if (topN.HasValue && ordered.Count > topN.Value)
            {
                ordered = ordered.Take(topN.Value).ToList();
            }

            foreach (RerankResult result in ordered)
            {
                result.Document = returnDocuments ? documents[result.Index] : null;
            }

            response.Results = ordered;
        }

        #endregion
    }
}
