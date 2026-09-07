namespace PolyPrompt.Options
{
    /// <summary>
    /// Azure OpenAI options for chat completion requests. Azure OpenAI is wire-compatible with OpenAI for the
    /// chat body, so this inherits <see cref="OpenAiChatCompletionOptions"/> (FrequencyPenalty,
    /// PresencePenalty, Seed) unchanged and exists as a first-class, discoverable type for Azure callers.
    /// </summary>
    public class AzureOpenAiChatCompletionOptions : OpenAiChatCompletionOptions
    {
    }
}
