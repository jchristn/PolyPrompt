namespace PolyPrompt.Models
{
    /// <summary>
    /// Fields shared by every operation response. Operations report HTTP errors, malformed provider responses, and
    /// transport failures through response fields. Streaming responses expose final integrity through
    /// <see cref="IStreamingResponse.State"/> and <see cref="IStreamingResponse.StreamError"/> after enumeration.
    /// Invalid arguments, caller cancellation, and request timeouts can throw; timeouts use OperationCanceledException.
    /// </summary>
    public abstract class ResponseBase
    {
        /// <summary>
        /// Opaque provider continuation output, including thinking, signatures, and hosted-tool state.
        /// Preserve unchanged through ToAssistantMessage() and JSON persistence; incompatible protocols ignore it.
        /// </summary>
        public System.Text.Json.JsonElement? ProviderState { get; set; }

        /// <summary>
        /// Whether the request succeeded. For streaming responses, whether the stream was opened successfully.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// HTTP status code returned by the provider, or null when no response was received.
        /// </summary>
        public int? StatusCode { get; set; }

        /// <summary>
        /// Error message when the request failed, including the HTTP status and response body for HTTP errors.
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// The model used for the request, as reported by the provider when it reports one, else the requested model.
        /// </summary>
        public string? Model { get; set; }

        /// <summary>
        /// Total time for the request in milliseconds. For streaming responses, updated when the stream has been
        /// fully consumed.
        /// </summary>
        public long OverallRuntimeMs { get; set; }

        /// <summary>
        /// The provider's complete response JSON, for fields PolyPrompt does not model. For streaming calls, the final
        /// response object when the provider sends one (Responses API <c>response.completed</c>), otherwise null.
        /// Returned to the caller only: it is never written to call details, logs, or telemetry.
        /// </summary>
        public System.Text.Json.JsonElement? RawResponse { get; set; }

        /// <summary>
        /// Citations the provider returned, normalized (see <see cref="Citation"/>). Empty, never null, when there are
        /// none. Streaming responses fill this once, after the stream completes.
        /// </summary>
        public List<Citation> Citations { get; set; } = new List<Citation>();

        /// <summary>
        /// The reasoning effort wire value actually sent, after mapping <see cref="CompletionOptions.ReasoningEffort"/>
        /// to the provider (for example <c>xhigh</c>, <c>false</c>, or a thinking budget such as <c>8192</c>). Null when
        /// no reasoning field was sent.
        /// </summary>
        public string? AppliedReasoningEffort { get; set; }
    }
}
