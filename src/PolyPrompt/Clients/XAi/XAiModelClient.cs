namespace PolyPrompt.Clients
{
    using System.Runtime.CompilerServices;
    using System.Text.Json;
    using PolyPrompt.Models;
    using SyslogLogging;

    /// <summary>xAI general and capability-specific model catalogs. Detailed metadata is retained without losing provider fields.</summary>
    public class XAiModelClient : ModelClientBase
    {
        /// <summary>Initialize an xAI model client.</summary>
        /// <param name="apiKey">Required bearer token.</param>
        /// <param name="endpoint">API root or versioned /v1 URL.</param>
        /// <param name="logging">Optional logging module.</param>
        /// <param name="httpClient">Optional HTTP client.</param>
        public XAiModelClient(string apiKey, string endpoint = XAiProtocol.Endpoint, LoggingModule? logging = null, HttpClient? httpClient = null)
            : base(endpoint, XAiProtocol.RequireKey(apiKey), logging ?? new LoggingModule(), httpClient) { _Header = "[XAi] "; }
        /// <inheritdoc />
        public override Task<bool> ValidateConnectivityAsync(CancellationToken token = default) => ProbeAsync(XAiProtocol.Url(_Endpoint, "models"), token);
        /// <inheritdoc />
        protected override Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token) { XAiProtocol.Auth(request, _ApiKey); return Task.CompletedTask; }
        /// <inheritdoc />
        public override IAsyncEnumerable<ModelInformation> ListModelsAsync(CancellationToken token = default) => ListCatalogAsync("models", token);
        /// <summary>List detailed language models, including capabilities, modalities, aliases, and prices.</summary>
        public IAsyncEnumerable<ModelInformation> ListLanguageModelsAsync(CancellationToken token = default) => ListCatalogAsync("language-models", token);
        /// <summary>List detailed embedding models.</summary>
        public IAsyncEnumerable<ModelInformation> ListEmbeddingModelsAsync(CancellationToken token = default) => ListCatalogAsync("embedding-models", token);
        /// <summary>List image-generation models. Image generation is accessible as a hosted Responses tool, not an embedding/completion operation.</summary>
        public IAsyncEnumerable<ModelInformation> ListImageGenerationModelsAsync(CancellationToken token = default) => ListCatalogAsync("image-generation-models", token);
        /// <summary>List video-generation models. The standalone video-generation API is outside the completion interface.</summary>
        public IAsyncEnumerable<ModelInformation> ListVideoGenerationModelsAsync(CancellationToken token = default) => ListCatalogAsync("video-generation-models", token);
        /// <summary>Look up detailed information in a capability catalog: language-models, embedding-models, image-generation-models, or video-generation-models.</summary>
        public async Task<ModelInformation?> GetCatalogModelInformationAsync(string catalog, string model, CancellationToken token = default)
        {
            if (catalog is not ("language-models" or "embedding-models" or "image-generation-models" or "video-generation-models")) throw new ArgumentException("Unknown xAI model catalog.", nameof(catalog));
            ArgumentException.ThrowIfNullOrWhiteSpace(model);
            return await InstrumentValueAsync("model_info", model, () => GetInformationAsync(catalog, model, token), token).ConfigureAwait(false);
        }
        /// <inheritdoc />
        protected override Task<ModelInformation?> GetModelInformationCoreAsync(string model, CancellationToken token) => GetInformationAsync("models", model, token);
        private async Task<ModelInformation?> GetInformationAsync(string catalog, string model, CancellationToken token)
        {
            try
            {
                HttpCallResult result = await GetAndRecordAsync(XAiProtocol.Url(_Endpoint, catalog + "/" + Uri.EscapeDataString(model)), token).ConfigureAwait(false);
                if (!result.IsSuccessStatusCode) return null;
                return ParseModel(XAiProtocol.Parse(result.ResponseBody!));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _Logging.Warn(_Header + "model lookup failed: " + ex.Message); return null; }
        }
        private async IAsyncEnumerable<ModelInformation> ListCatalogAsync(string catalog, [EnumeratorCancellation] CancellationToken token)
        {
            HttpCallResult result = await GetAndRecordAsync(XAiProtocol.Url(_Endpoint, catalog), token).ConfigureAwait(false);
            if (!result.IsSuccessStatusCode) yield break;
            JsonElement root = XAiProtocol.Parse(result.ResponseBody!);
            JsonElement? items = XAiProtocol.Property(root, catalog == "models" ? "data" : "models");
            if (items?.ValueKind != JsonValueKind.Array) yield break;
            foreach (JsonElement item in items.Value.EnumerateArray()) { token.ThrowIfCancellationRequested(); yield return ParseModel(item); }
        }
        private static ModelInformation ParseModel(JsonElement item)
        {
            ModelInformation model = new() { Name = XAiProtocol.String(item, "id") ?? "", OwnedBy = XAiProtocol.String(item, "owned_by") };
            if (XAiProtocol.Property(item, "created") is JsonElement created && created.TryGetInt64(out long seconds)) model.CreatedUtc = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
            foreach (JsonProperty property in item.EnumerateObject()) model.Metadata[property.Name] = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.GetRawText();
            return model;
        }
    }
}
