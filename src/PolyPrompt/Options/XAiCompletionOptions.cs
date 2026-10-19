namespace PolyPrompt.Options
{
    using System.Text.Json;
    using PolyPrompt.Models;

    /// <summary>Responses API settings. Non-null per-call values override client defaults field by field.</summary>
    public class XAiCompletionOptions : CompletionOptions
    {
        /// <summary>Persist responses for retrieval and previous_response_id continuation. Client default: false.</summary>
        public bool? Store { get; set; }
        /// <summary>Continue a stored response. Supply only new input, not the full previous history. Empty clears a default ID.</summary>
        public string? PreviousResponseId { get; set; }
        /// <summary>Allow multiple function calls per model turn.</summary>
        public bool? ParallelToolCalls { get; set; }
        /// <summary>Require strict function schemas when set. Applies to locally supplied function tools.</summary>
        public bool? StrictTools { get; set; }
        /// <summary>Maximum provider-side agentic tool turns; must be positive.</summary>
        public int? MaxTurns { get; set; }
        /// <summary>xAI reasoning effort override: low, medium, high, or xhigh. Otherwise the common ReasoningEffort is mapped.</summary>
        public string? ReasoningEffortOverride { get; set; }
        /// <summary>Reasoning summary preference: auto, concise, or detailed. The provider may always return detailed.</summary>
        public string? ReasoningSummary { get; set; }
        /// <summary>Additional output fields. Encrypted reasoning is requested automatically for replayable history.</summary>
        public List<string>? Include { get; set; }
        /// <summary>Local function tools for native raw/multimodal requests, also combined with ToolChatRequest.Tools when supplied.</summary>
        public List<ToolDefinition>? FunctionTools { get; set; }
        /// <summary>Additional native tools (web/X search, code interpreter, collections, MCP, image generation, shell, or tool search). Native shell actions use the raw API.</summary>
        public List<XAiHostedTool>? HostedTools { get; set; }
        /// <summary>Text response format object: text, json_object, or json_schema with schema/name/strict fields.</summary>
        public Dictionary<string, object>? TextFormat { get; set; }
        /// <summary>Raw Responses tool_choice string/object, overriding ToolChatRequest.ToolChoice when supplied.</summary>
        public JsonElement? ToolChoice { get; set; }
        /// <summary>Top-k sampling; must be positive.</summary>
        public int? TopK { get; set; }
        /// <summary>Min-p sampling, from zero to one.</summary>
        public double? MinP { get; set; }
        /// <summary>Return output log probabilities on models supporting them.</summary>
        public bool? Logprobs { get; set; }
        /// <summary>Number of alternative log probabilities, from zero to eight; requires Logprobs=true.</summary>
        public int? TopLogprobs { get; set; }
        /// <summary>Processing tier: auto, priority, or fast.</summary>
        public string? ServiceTier { get; set; }
        /// <summary>Routing/cache affinity key.</summary>
        public string? PromptCacheKey { get; set; }
        /// <summary>Opaque end-user identifier.</summary>
        public string? User { get; set; }
        /// <summary>Stable hashed end-user identifier for abuse attribution.</summary>
        public string? SafetyIdentifier { get; set; }
        /// <summary>Per-category output guidelines as provider JSON objects.</summary>
        public List<JsonElement>? Guidelines { get; set; }
        /// <summary>Extension body fields. Cannot replace built-in request fields; values must have valid JSON.</summary>
        public Dictionary<string, JsonElement>? AdditionalParameters { get; set; }
    }
}
