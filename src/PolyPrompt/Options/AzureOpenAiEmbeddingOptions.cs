namespace PolyPrompt.Options
{
    /// <summary>
    /// Azure OpenAI options for embedding requests. Azure OpenAI is wire-compatible with OpenAI for the
    /// embeddings body, so this inherits <see cref="OpenAiEmbeddingOptions"/> (EncodingFormat, Dimensions)
    /// unchanged and exists as a first-class, discoverable type for Azure callers.
    /// </summary>
    public class AzureOpenAiEmbeddingOptions : OpenAiEmbeddingOptions
    {
    }
}
