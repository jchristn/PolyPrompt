namespace PolyPrompt.Clients
{
    using PolyPrompt.Auth;
    using PolyPrompt.Models;
    using PolyPrompt.Options;

    /// <summary>
    /// AWS Bedrock client. Requests are signed with AWS Signature Version 4.
    /// <list type="bullet">
    /// <item><description>Supported: Chat, ToolCalling, and Generate (the Converse API, <c>converse</c> and
    /// <c>converse-stream</c>), Embed (<c>InvokeModel</c> with Titan or Cohere embedding models), Rerank
    /// (<c>InvokeModel</c> with Cohere or Amazon rerank models), and Models (control-plane
    /// <c>/foundation-models</c>).</description></item>
    /// <item><description>Media input: ImageInput and VideoInput (Converse <c>image</c> and <c>video</c> blocks from inline bytes or <c>s3://</c> locations; video depends on the model).</description></item>
    /// <item><description>Not supported (throw <see cref="CapabilityNotSupportedException"/>): SparseEmbed, Classify,
    /// Decide.</description></item>
    /// <item><description>Auth: AWS credentials from <see cref="IAwsCredentialProvider"/>; the client's region is used for
    /// both the endpoint host and the signature.</description></item>
    /// <item><description>Defaults: no completion model (set <c>Defaults.Completion.Model</c>; IDs and inference profiles
    /// vary by region and account), embedding <c>amazon.titan-embed-text-v2:0</c>, rerank
    /// <c>cohere.rerank-v3-5:0</c>.</description></item>
    /// </list>
    /// </summary>
    /// <seealso href="https://github.com/jchristn/Polyprompt#capability-matrix">Capability matrix</seealso>
    public partial class BedrockClient : ProviderClientBase
    {
        #region Private-Members

        private readonly IAwsCredentialProvider _CredentialProvider;
        private readonly string _Region;
        private readonly string _ControlPlaneEndpoint;

        #endregion

        #region Public-Members

        /// <summary>
        /// Operations this client supports: Chat, ToolCalling, Generate, Embed, Rerank, Models, ImageInput, and VideoInput.
        /// </summary>
        public override Capability Capabilities => Capability.Chat | Capability.ToolCalling | Capability.Generate | Capability.Embed | Capability.Rerank | Capability.Models | Capability.ImageInput | Capability.VideoInput;

        /// <inheritdoc />
        public override string ProviderName => "Bedrock";

        /// <summary>
        /// Client-wide defaults: <see cref="BedrockDefaults.Completion"/>, <see cref="BedrockDefaults.Embedding"/>, and
        /// <see cref="BedrockDefaults.Rerank"/>.
        /// </summary>
        public override BedrockDefaults Defaults { get; } = new BedrockDefaults();

        /// <summary>
        /// The AWS region used for routing and signing.
        /// </summary>
        public string Region
        {
            get { return _Region; }
        }

        /// <summary>
        /// The AWS account that owns the S3 buckets in <c>s3://</c> file references, sent as <c>bucketOwner</c>. Null
        /// when the buckets belong to the calling account. Default: null.
        /// </summary>
        public string? S3BucketOwner { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a Bedrock client.
        /// </summary>
        /// <param name="region">AWS region, for example <c>us-east-1</c>. Selects the endpoint hosts and the signing region.</param>
        /// <param name="credential">Resolves AWS credentials to sign each request.</param>
        /// <param name="endpoint">Optional endpoint override, used for both runtime and control-plane calls. Default:
        /// <c>https://bedrock-runtime.{region}.amazonaws.com</c> for inference and <c>https://bedrock.{region}.amazonaws.com</c>
        /// for model listing.</param>
        /// <param name="options">Optional construction settings.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null or empty.</exception>
        /// <exception cref="ArgumentException">Thrown when endpoint is not an absolute http(s) URL.</exception>
        public BedrockClient(string region, IAwsCredentialProvider credential, string? endpoint = null, ClientOptions? options = null)
            : base(ClientArguments.Endpoint(BedrockProtocol.ResolveRuntimeEndpoint(endpoint, region), nameof(endpoint)), null, options)
        {
            _CredentialProvider = credential ?? throw new ArgumentNullException(nameof(credential));
            _Region = region;
            _ControlPlaneEndpoint = BedrockProtocol.ResolveControlPlaneEndpoint(endpoint, region);
            _Header = BedrockProtocol.Header;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Probe the control-plane <c>/foundation-models</c> route. See <see cref="HttpClientBase.ValidateConnectivityAsync"/>.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the request was accepted.</returns>
        public override Task<bool> ValidateConnectivityAsync(CancellationToken token = default)
        {
            return ProbeAsync(ControlPlaneUrl("/foundation-models"), token);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string LargeMediaHint => "Put it in S3 and send ContentPart.FileReferenceOf(type, \"s3://bucket/key\", mediaType); set S3BucketOwner when the bucket belongs to another account.";

        /// <inheritdoc />
        protected override string? ModelCatalogUrl => "https://docs.aws.amazon.com/bedrock/latest/userguide/model-cards.html";

        /// <inheritdoc />
        protected override Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
        {
            BedrockProtocol.Sign(request, body, _CredentialProvider, _Region);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        private protected override List<Citation>? ExtractCitations(System.Text.Json.JsonElement root)
        {
            return Helpers.CitationParser.Bedrock(Helpers.CitationParser.Prop(Helpers.CitationParser.Prop(Helpers.CitationParser.Prop(root, "output") ?? default, "message") ?? default, "content") ?? default);
        }

        #endregion

        #region Private-Methods

        private string ControlPlaneUrl(string path)
        {
            return _ControlPlaneEndpoint.TrimEnd('/') + path;
        }

        #endregion
    }

    /// <summary>
    /// Defaults for <see cref="BedrockClient"/>.
    /// </summary>
    public sealed class BedrockDefaults : ClientDefaults
    {
        /// <summary>
        /// Completion defaults. No default model: Bedrock model and inference-profile IDs vary by region and account (for example <c>anthropic.claude-sonnet-5-5</c> or a regional profile such as <c>us.anthropic.claude-sonnet-5-5</c>), so set <c>Model</c>.
        /// </summary>
        public override CompletionOptions Completion { get; } = new CompletionOptions();

        /// <summary>
        /// Embedding defaults. Default model: <c>amazon.titan-embed-text-v2:0</c>.
        /// </summary>
        public override BedrockEmbeddingOptions Embedding { get; } = new BedrockEmbeddingOptions { Model = "amazon.titan-embed-text-v2:0" };

        /// <summary>Not supported by Bedrock; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Bedrock does not support sparse embeddings.", true)]
        public new SparseEmbeddingOptions? SparseEmbedding => null;

        /// <summary>
        /// Rerank defaults. Default model: <c>cohere.rerank-v3-5:0</c>.
        /// </summary>
        public override BedrockRerankOptions Rerank { get; } = new BedrockRerankOptions { Model = "cohere.rerank-v3-5:0" };

        /// <summary>Not supported by Bedrock; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Bedrock does not support classification.", true)]
        public new ClassificationOptions? Classification => null;

        /// <summary>Not supported by Bedrock; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Bedrock does not support decisions.", true)]
        public new DecisionOptions? Decision => null;

        /// <summary>Not supported by Bedrock; always null.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Bedrock does not support transcription.", true)]
        public new TranscriptionOptions? Transcription => null;

    }
}
