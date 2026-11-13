namespace PolyPrompt.Models
{
    /// <summary>
    /// Semantic reasoning effort level. Anchors a <see cref="ReasoningEffort"/> and supplies the per-provider defaults
    /// each level implies; each provider maps every level to its nearest supported wire value (see the reasoning effort
    /// tables in PROVIDERS.md). Callers may override any individual provider value. The wire value actually sent is
    /// reported on <see cref="ResponseBase.AppliedReasoningEffort"/>.
    /// </summary>
    public enum ReasoningEffortLevel
    {
        /// <summary>No reasoning: OpenAI <c>none</c>; xAI and OpenAI-compatible servers omit the field; other providers turn thinking off.</summary>
        None,

        /// <summary>Minimal reasoning; disables extended thinking on providers that can toggle it off (xAI: <c>low</c>).</summary>
        Minimal,

        /// <summary>Low reasoning effort.</summary>
        Low,

        /// <summary>Medium reasoning effort.</summary>
        Medium,

        /// <summary>High reasoning effort.</summary>
        High,

        /// <summary>Extra-high effort: <c>xhigh</c> on OpenAI, xAI, and Anthropic; <see cref="High"/>'s mapping elsewhere.</summary>
        XHigh,

        /// <summary>Maximum effort: <c>max</c> on OpenAI and Anthropic, <c>xhigh</c> on xAI; <see cref="High"/>'s mapping elsewhere.</summary>
        Max
    }
}
