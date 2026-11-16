namespace PolyPrompt.Models
{
    /// <summary>
    /// Input tokens split by modality, where the provider reports the split (Gemini and Vertex AI
    /// <c>promptTokensDetails</c>; Chat Completions <c>prompt_tokens_details.audio_tokens</c>).
    /// </summary>
    public sealed class ModalityTokens
    {
        /// <summary>Text tokens, when reported.</summary>
        public long? Text { get; set; }

        /// <summary>Image tokens, when reported.</summary>
        public long? Image { get; set; }

        /// <summary>Audio tokens, when reported.</summary>
        public long? Audio { get; set; }

        /// <summary>Video tokens, when reported.</summary>
        public long? Video { get; set; }
    }
}
