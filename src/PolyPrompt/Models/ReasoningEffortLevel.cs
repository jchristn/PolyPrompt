namespace PolyPrompt.Models
{
    /// <summary>
    /// Semantic reasoning effort level. Anchors a <see cref="ReasoningEffort"/> and supplies the
    /// per-provider defaults each level implies. Callers may override any individual provider value.
    /// </summary>
    public enum ReasoningEffortLevel
    {
        /// <summary>Minimal reasoning; disables extended thinking on providers that can toggle it off.</summary>
        Minimal,

        /// <summary>Low reasoning effort.</summary>
        Low,

        /// <summary>Medium reasoning effort.</summary>
        Medium,

        /// <summary>High reasoning effort.</summary>
        High
    }
}
