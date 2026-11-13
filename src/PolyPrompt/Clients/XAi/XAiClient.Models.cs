namespace PolyPrompt.Clients
{
    using System.Runtime.CompilerServices;
    using System.Text.Json;
    using PolyPrompt.Models;
    
    public partial class XAiClient
    {
        /// <inheritdoc />
        protected override IAsyncEnumerable<ModelInformation> ListModelsCoreAsync(CancellationToken token) => ListCatalogAsync("models", token);
        /// <summary>List detailed language models, including capabilities, modalities, aliases, and prices.</summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The models.</returns>
        public IAsyncEnumerable<ModelInformation> ListLanguageModelsAsync(CancellationToken token = default) => ListCatalogAsync("language-models", token);
        /// <summary>List detailed embedding models.</summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The models.</returns>
        public IAsyncEnumerable<ModelInformation> ListEmbeddingModelsAsync(CancellationToken token = default) => ListCatalogAsync("embedding-models", token);
        /// <summary>List image-generation models. Image generation is accessible as a hosted Responses tool, not an embedding/completion operation.</summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The models.</returns>
        public IAsyncEnumerable<ModelInformation> ListImageGenerationModelsAsync(CancellationToken token = default) => ListCatalogAsync("image-generation-models", token);
        /// <summary>List video-generation models. The standalone video-generation API is outside the completion interface.</summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The models.</returns>
        public IAsyncEnumerable<ModelInformation> ListVideoGenerationModelsAsync(CancellationToken token = default) => ListCatalogAsync("video-generation-models", token);
        /// <summary>Look up detailed information in a capability catalog: language-models, embedding-models, image-generation-models, or video-generation-models.</summary>
        /// <param name="catalog">Catalog name.</param>
        /// <param name="model">Model id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The model, or null when not found.</returns>
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
                HttpCallResult result = await GetAndRecordAsync(Url(catalog + "/" + Uri.EscapeDataString(model)), token).ConfigureAwait(false);
                if (!result.IsSuccessStatusCode) return null;
                return ParseModel(ResponsesApiJson.Parse(result.ResponseBody!));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _Logging.Warn(_Header + "model lookup failed: " + ex.Message); return null; }
        }
        private async IAsyncEnumerable<ModelInformation> ListCatalogAsync(string catalog, [EnumeratorCancellation] CancellationToken token)
        {
            HttpCallResult result = await GetAndRecordAsync(Url(catalog), token).ConfigureAwait(false);
            if (!result.IsSuccessStatusCode) yield break;
            JsonElement root = ResponsesApiJson.Parse(result.ResponseBody!);
            JsonElement? items = ResponsesApiJson.Property(root, catalog == "models" ? "data" : "models");
            if (items?.ValueKind != JsonValueKind.Array) yield break;
            foreach (JsonElement item in items.Value.EnumerateArray()) { token.ThrowIfCancellationRequested(); yield return ParseModel(item); }
        }
        private static ModelInformation ParseModel(JsonElement item)
        {
            ModelInformation model = new() { Name = ResponsesApiJson.String(item, "id") ?? "", OwnedBy = ResponsesApiJson.String(item, "owned_by") };
            if (ResponsesApiJson.Property(item, "created") is JsonElement created && created.TryGetInt64(out long seconds)) model.CreatedUtc = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
            foreach (JsonProperty property in item.EnumerateObject()) model.Metadata[property.Name] = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.GetRawText();
            return model;
        }
    }
}
