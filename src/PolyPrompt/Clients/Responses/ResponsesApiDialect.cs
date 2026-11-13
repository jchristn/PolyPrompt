namespace PolyPrompt.Clients
{
    using PolyPrompt.Models;

    /// <summary>
    /// Per-provider data for the shared Responses API engine. The engine never names a provider; every vendor-specific
    /// value or message comes from here, and provider-only body fields come from the client subclass.
    /// </summary>
    internal sealed class ResponsesApiDialect
    {
        // Universe of known values, across providers. Known values outside a dialect's allowed set are rejected; unknown
        // values pass through.
        internal static readonly string[] KnownEfforts = { "none", "minimal", "low", "medium", "high", "xhigh", "max" };
        internal static readonly string[] KnownSummaries = { "auto", "concise", "detailed" };
        internal static readonly string[] KnownServiceTiers = { "auto", "default", "flex", "scale", "priority", "fast" };

        internal static readonly string[] SharedReservedFields = "model input stream max_output_tokens instructions previous_response_id store parallel_tool_calls service_tier prompt_cache_key user safety_identifier text include reasoning reasoning_effort tools tool_choice".Split(' ');

        /// <summary>Vendor name used in messages, for example <c>xAI</c>.</summary>
        internal string Name { get; init; } = "";

        /// <summary>Reasoning effort values this provider accepts.</summary>
        internal string[] Efforts { get; init; } = KnownEfforts;

        /// <summary>Message thrown for a known effort this provider rejects.</summary>
        internal string EffortError { get; init; } = "";

        /// <summary>Maps a level to a wire value, or null to send no reasoning effort.</summary>
        internal Func<ReasoningEffort, string?> MapEffort { get; init; } = e => e.ToOpenAiResponsesWireValue();

        /// <summary>Reasoning summary values this provider accepts.</summary>
        internal string[] Summaries { get; init; } = KnownSummaries;

        /// <summary>Reject a top_p of zero or less.</summary>
        internal bool TopPMustBePositive { get; init; }

        /// <summary>Service tiers this provider accepts.</summary>
        internal string[] ServiceTiers { get; init; } = KnownServiceTiers;

        /// <summary>Message thrown for a known service tier this provider rejects.</summary>
        internal string ServiceTierError { get; init; } = "";

        /// <summary>Hosted tool types this provider accepts, or null for no client-side check.</summary>
        internal string[]? HostedToolTypes { get; init; }

        /// <summary>Maximum tools per request, or null for no client-side limit.</summary>
        internal int? MaxTools { get; init; }

        /// <summary>Send <c>store</c> even when no value is configured (as false).</summary>
        internal bool AlwaysSendStore { get; init; }

        /// <summary>Replay the untagged 3.x output-array state shape.</summary>
        internal bool ReadsLegacyArrayState { get; init; }

        /// <summary>URL citation titles are citation numbers, not page titles.</summary>
        internal bool CitationTitleIsNumber { get; init; }

        /// <summary>Hint appended to a 404 or 405 from a <c>/responses</c> route, or null.</summary>
        internal string? NotFoundHint { get; init; }

        /// <summary>Body fields that AdditionalParameters cannot replace (shared plus provider-only).</summary>
        internal HashSet<string> ReservedFields { get; init; } = new HashSet<string>(SharedReservedFields, StringComparer.Ordinal);

        /// <summary>Validate a known-set value against this dialect's allowed values.</summary>
        internal static void Check(string value, string[] known, string[] allowed, string error)
        {
            if (Array.IndexOf(known, value) >= 0 && Array.IndexOf(allowed, value) < 0) throw new ArgumentException(error);
        }
    }
}
