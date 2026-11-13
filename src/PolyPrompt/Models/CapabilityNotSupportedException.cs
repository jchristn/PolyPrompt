namespace PolyPrompt.Models
{
    /// <summary>
    /// Thrown when an operation is called on a provider client whose provider does not support it, for example
    /// <c>EmbedAsync</c> on <c>AnthropicClient</c>. It is thrown synchronously from the call, before any network activity,
    /// and is never reported through a response's <c>Error</c> field. No telemetry or call detail is recorded for the call.
    /// Check <c>client.Supports(capability)</c> first to avoid it.
    /// </summary>
    public sealed class CapabilityNotSupportedException : NotSupportedException
    {
        /// <summary>
        /// Anchor of the README capability matrix, linked from every message.
        /// </summary>
        public const string MatrixUrl = "https://github.com/jchristn/Polyprompt#capability-matrix";

        /// <summary>
        /// Provider display name, for example <c>Anthropic</c>.
        /// </summary>
        public string Provider { get; }

        /// <summary>
        /// The unsupported capability.
        /// </summary>
        public Capability Capability { get; }

        /// <summary>
        /// The operation that was called, for example <c>EmbedAsync</c>.
        /// </summary>
        public string Operation { get; }

        /// <summary>
        /// Initialize the exception.
        /// </summary>
        /// <param name="provider">Provider display name.</param>
        /// <param name="capability">The unsupported capability.</param>
        /// <param name="operation">The operation that was called.</param>
        public CapabilityNotSupportedException(string provider, Capability capability, string operation)
            : base(provider + " does not support " + capability + " (" + operation + "). Check client.Supports(Capability."
                + capability + ") first, or see the capability matrix: " + MatrixUrl)
        {
            Provider = provider;
            Capability = capability;
            Operation = operation;
        }
    }
}
