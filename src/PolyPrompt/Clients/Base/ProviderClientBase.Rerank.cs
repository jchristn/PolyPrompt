namespace PolyPrompt.Clients
{
    using PolyPrompt.Models;
    using PolyPrompt.Telemetry;

    public abstract partial class ProviderClientBase
    {
        #region Rerank-Methods

        /// <summary>
        /// Score documents for relevance to a query and return them highest score first. Results behave identically on
        /// every provider: sorted by score (ties by index), results whose index does not map to an input document are
        /// dropped, <see cref="RerankOptions.TopN"/> is applied (natively where supported, client-side otherwise), and
        /// <see cref="RerankOptions.ReturnDocuments"/> attaches document text from the caller's list. Settings come from
        /// <c>Defaults.Rerank</c>.
        /// </summary>
        /// <param name="query">The query. Cannot be null, empty, or whitespace.</param>
        /// <param name="documents">The documents to score. Cannot be null or empty, and no element can be null.</param>
        /// <param name="options">Optional per-call settings. A per-call TopN cannot exceed the number of documents.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A RerankResponse whose Results are sorted by score, highest first.</returns>
        /// <exception cref="CapabilityNotSupportedException">Thrown when <c>Supports(Capability.Rerank)</c> is false for this client.</exception>
        /// <exception cref="ArgumentNullException">Thrown when query or documents is null.</exception>
        /// <exception cref="ArgumentException">Thrown when query is empty or whitespace, documents is empty, a document is null, or options is another provider's type.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when a per-call TopN exceeds the number of documents.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the provider needs a model and none is configured.</exception>
        public Task<RerankResponse> RerankAsync(
            string query,
            List<string> documents,
            RerankOptions? options = null,
            CancellationToken token = default)
        {
            RequireCapability(Capability.Rerank, nameof(RerankAsync));
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("Rerank query cannot be empty or whitespace.", nameof(query));

            ValidateInputList(documents, nameof(documents), "Rerank requests require at least one document.");

            RerankOptions? defaults = Defaults.Rerank;
            ValidateOptionsType(options, defaults, nameof(RerankOptions));

            if (options?.TopN != null && options.TopN.Value > documents.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    options.TopN.Value,
                    "TopN (" + options.TopN.Value + ") cannot exceed the number of documents (" + documents.Count + ").");
            }

            int? topN = options?.TopN ?? defaults?.TopN;
            if (topN.HasValue && topN.Value > documents.Count) topN = documents.Count;

            bool returnDocuments = options?.ReturnDocuments ?? defaults?.ReturnDocuments ?? false;

            string? model = options?.Model ?? defaults?.Model;
            if (RerankRequiresModel && string.IsNullOrWhiteSpace(model))
                throw new InvalidOperationException(NoModelMessage("Rerank"));

            return RerankInstrumentedAsync(query, documents, model, topN, returnDocuments, options, token);
        }

        #endregion

        #region Rerank-Protected

        /// <summary>
        /// True when rerank requests need a model name. Providers that serve a single server-defined model return false.
        /// </summary>
        protected virtual bool RerankRequiresModel => true;

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
        protected virtual Task<RerankResponse> RerankCoreAsync(
            string query,
            List<string> documents,
            string? model,
            int? topN,
            RerankOptions? options,
            CancellationToken token)
        {
            throw NotSupported(Capability.Rerank, nameof(RerankAsync));
        }

        #endregion

        #region Rerank-Private

        private async Task<RerankResponse> RerankInstrumentedAsync(string query, List<string> documents, string? model, int? topN, bool returnDocuments, RerankOptions? options, CancellationToken token)
        {
            RerankResponse response = await InstrumentAsync(PolyPromptTelemetryNames.OperationRerank, model, s => s.RecordBatchSize(documents.Count),
                () => RerankCoreAsync(query, documents, model, topN, options, token), token).ConfigureAwait(false);
            if (response.Success) FinalizeRerankResults(response, documents, topN, returnDocuments);
            return response;
        }

        private static void FinalizeRerankResults(RerankResponse response, List<string> documents, int? topN, bool returnDocuments)
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
