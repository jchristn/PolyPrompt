namespace PolyPrompt.Options
{
    using System.Text.Json;

    /// <summary>
    /// xAI Responses API settings for <see cref="Clients.XAiClient"/>: the shared
    /// <see cref="ResponsesApiCompletionOptions"/> plus xAI-only request fields. Non-null per-call values override client
    /// defaults field by field.
    /// </summary>
    public class XAiCompletionOptions : ResponsesApiCompletionOptions
    {
        /// <summary>
        /// Maximum provider-side agentic tool turns (<c>max_turns</c>); must be positive.
        /// </summary>
        public int? MaxTurns { get; set; }

        /// <summary>
        /// Top-k sampling (<c>top_k</c>); must be positive.
        /// </summary>
        public int? TopK { get; set; }

        /// <summary>
        /// Min-p sampling (<c>min_p</c>), from zero to one.
        /// </summary>
        public double? MinP { get; set; }

        /// <summary>
        /// Return output log probabilities on models supporting them (<c>logprobs</c>).
        /// </summary>
        public bool? Logprobs { get; set; }

        /// <summary>
        /// Number of alternative log probabilities, from zero to eight (<c>top_logprobs</c>); requires Logprobs = true.
        /// </summary>
        public int? TopLogprobs { get; set; }

        /// <summary>
        /// Per-category output guidelines as provider JSON objects (<c>guidelines</c>).
        /// </summary>
        public List<JsonElement>? Guidelines { get; set; }
    }
}
