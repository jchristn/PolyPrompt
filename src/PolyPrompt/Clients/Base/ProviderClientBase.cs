namespace PolyPrompt.Clients
{
    using PolyPrompt.Helpers;
    using PolyPrompt.Models;
    using PolyPrompt.Telemetry;
    using SyslogLogging;

    /// <summary>
    /// Base class for every model-API provider client (<c>OpenAiClient</c>, <c>AnthropicClient</c>, and so on). It
    /// declares every model operation: chat, tool chat, and generation (each streaming or not), dense and sparse
    /// embeddings, reranking, classification, decisions, and the model catalog. Each provider overrides the operations it
    /// supports and reports them in <see cref="Capabilities"/>; calling any other operation throws
    /// <see cref="CapabilityNotSupportedException"/> synchronously, before any request is sent. Code written against this
    /// type runs on any provider and can branch on <see cref="Supports"/>.
    /// </summary>
    /// <remarks>
    /// The public operations validate their arguments, check the capability, merge per-call options over
    /// <see cref="Defaults"/>, and record telemetry; providers implement the protected <c>...CoreAsync</c> members.
    /// See the capability matrix: https://github.com/jchristn/Polyprompt#capability-matrix
    /// </remarks>
    public abstract partial class ProviderClientBase : HttpClientBase
    {
        #region Public-Members

        /// <summary>
        /// The operations this client supports. A constant per client type.
        /// </summary>
        public abstract Capability Capabilities { get; }

        /// <summary>
        /// Provider display name used in exception messages, for example <c>Anthropic</c>.
        /// </summary>
        public abstract string ProviderName { get; }

        /// <summary>
        /// Client-wide default settings, one section per supported capability. Each client exposes its own derived type
        /// (for example <c>OpenAiDefaults</c>); read through <see cref="ClientDefaults"/>, unsupported sections are null.
        /// </summary>
        public abstract ClientDefaults Defaults { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a provider client.
        /// </summary>
        /// <param name="endpoint">Endpoint URL (already validated and normalized by the provider).</param>
        /// <param name="apiKey">API key, or null.</param>
        /// <param name="options">Construction settings, or null for defaults.</param>
        protected ProviderClientBase(string endpoint, string? apiKey, ClientOptions? options)
            : base(endpoint, apiKey, options?.Logging ?? new LoggingModule(), options?.HttpClient)
        {
            if (options != null)
            {
                if (options.TimeoutMs.HasValue) TimeoutMs = options.TimeoutMs.Value;
                if (options.CallDetailCapture.HasValue) CallDetailCapture = options.CallDetailCapture.Value;
                if (options.RedactCredentials.HasValue) RedactCredentials = options.RedactCredentials.Value;
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// True when this client supports every flag in <paramref name="capability"/>.
        /// </summary>
        /// <param name="capability">One or more capability flags.</param>
        /// <returns>True when all requested capabilities are supported.</returns>
        public bool Supports(Capability capability)
        {
            return (Capabilities & capability) == capability;
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// URL of the provider's model list, quoted when a call has no model. Null when there is none to cite.
        /// </summary>
        protected virtual string? ModelCatalogUrl => null;

        /// <summary>
        /// Throw <see cref="CapabilityNotSupportedException"/> when <paramref name="capability"/> is not supported.
        /// </summary>
        /// <param name="capability">Capability.</param>
        /// <param name="operation">Operation name, for example <c>EmbedAsync</c>.</param>
        /// <exception cref="CapabilityNotSupportedException">Thrown when the capability is not supported.</exception>
        protected void RequireCapability(Capability capability, string operation)
        {
            if (!Supports(capability)) throw new CapabilityNotSupportedException(ProviderName, capability, operation);
        }

        /// <summary>
        /// Build the exception thrown by a base <c>...CoreAsync</c> member that a provider did not override.
        /// </summary>
        /// <param name="capability">Capability.</param>
        /// <param name="operation">Operation name.</param>
        /// <returns>The exception.</returns>
        protected CapabilityNotSupportedException NotSupported(Capability capability, string operation)
        {
            return new CapabilityNotSupportedException(ProviderName, capability, operation);
        }

        /// <summary>
        /// Throw when <paramref name="options"/> is another provider's options type. The base type and this client's own
        /// options type (or a type between them) are accepted.
        /// </summary>
        /// <param name="options">Per-call options, or null.</param>
        /// <param name="section">This client's defaults section for the capability.</param>
        /// <param name="baseTypeName">Name of the shared base options type, for the message.</param>
        /// <exception cref="ArgumentException">Thrown when the options type belongs to another provider.</exception>
        protected void ValidateOptionsType(object? options, object? section, string baseTypeName)
        {
            if (options == null || section == null) return;
            Type given = options.GetType();
            Type own = section.GetType();
            if (own.IsAssignableFrom(given) || given.IsAssignableFrom(own)) return;
            throw new ArgumentException(given.Name + " cannot be used with " + GetType().Name + ". Use " + own.Name + " or " + baseTypeName + ".", "options");
        }

        /// <summary>
        /// The message for a call with no model, naming the defaults section to set.
        /// </summary>
        /// <param name="section">Defaults section name, for example <c>Completion</c>.</param>
        /// <returns>The message.</returns>
        protected string NoModelMessage(string section)
        {
            string message = "No model is configured. Set client.Defaults." + section + ".Model (or options.Model).";
            string? url = ModelCatalogUrl;
            if (!string.IsNullOrEmpty(url)) message += " See " + url + ".";
            return message;
        }

        /// <summary>
        /// Store continuation state on a response in the PolyPrompt 4.0 envelope, tagged with the protocol, this client's
        /// provider label, and its endpoint.
        /// </summary>
        /// <param name="response">Response.</param>
        /// <param name="protocol">Wire protocol, for example <c>anthropic</c> or <c>responses</c>.</param>
        /// <param name="value">State value.</param>
        private protected void SetTurnState(ResponseBase response, string protocol, object value)
        {
            ToolTurnState.Set(response, protocol, TelemetryProvider, Endpoint, value);
        }

        /// <summary>
        /// Read continuation state written by a compatible client: a 4.0 envelope with the same protocol, provider, and
        /// endpoint, or a 3.x envelope with the same protocol (and endpoint, when the 3.x writer recorded one).
        /// </summary>
        /// <param name="message">Assistant message.</param>
        /// <param name="protocol">Wire protocol.</param>
        /// <param name="value">State value.</param>
        /// <param name="legacyProtocol">3.x protocol tag, when it differs from <paramref name="protocol"/>.</param>
        /// <returns>True when compatible state was found.</returns>
        private protected bool TryGetTurnState(ChatMessage message, string protocol, out System.Text.Json.JsonElement value, string? legacyProtocol = null)
        {
            return ToolTurnState.TryGet(message, protocol, TelemetryProvider, Endpoint, out value, legacyProtocol);
        }

        #endregion
    }
}
