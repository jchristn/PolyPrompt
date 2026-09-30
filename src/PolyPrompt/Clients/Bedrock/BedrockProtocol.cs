namespace PolyPrompt.Clients
{
    using PolyPrompt.Auth;

    /// <summary>
    /// Wire details shared by the AWS Bedrock clients.
    /// </summary>
    internal static class BedrockProtocol
    {
        internal const string Header = "[Bedrock] ";
        private const string ServiceName = "bedrock";

        /// <summary>
        /// The runtime (inference) endpoint, <c>https://bedrock-runtime.{region}.amazonaws.com</c>, unless one is given.
        /// </summary>
        internal static string ResolveRuntimeEndpoint(string? endpoint, string region)
        {
            if (string.IsNullOrWhiteSpace(region)) throw new ArgumentNullException(nameof(region));
            return !string.IsNullOrEmpty(endpoint) ? endpoint : "https://bedrock-runtime." + region + ".amazonaws.com";
        }

        /// <summary>
        /// The control-plane endpoint used for model listing, <c>https://bedrock.{region}.amazonaws.com</c>, unless one is given.
        /// </summary>
        internal static string ResolveControlPlaneEndpoint(string? endpoint, string region)
        {
            if (string.IsNullOrWhiteSpace(region)) throw new ArgumentNullException(nameof(region));
            return !string.IsNullOrEmpty(endpoint) ? endpoint : "https://bedrock." + region + ".amazonaws.com";
        }

        /// <summary>
        /// The <c>InvokeModel</c> URL for a model.
        /// </summary>
        internal static string InvokeUrl(string endpoint, string model)
        {
            return endpoint.TrimEnd('/') + "/model/" + model + "/invoke";
        }

        /// <summary>
        /// Sign a request with AWS Signature Version 4. The client's region is authoritative for both the endpoint host and
        /// the signature, so a credential provider configured for another region cannot desynchronize the two.
        /// </summary>
        internal static void Sign(HttpRequestMessage request, byte[] body, IAwsCredentialProvider provider, string region)
        {
            AwsCredentials resolved = provider.Resolve();
            AwsCredentials signing = new AwsCredentials(resolved.AccessKeyId, resolved.SecretAccessKey, region, resolved.SessionToken);
            SigV4Signer.Sign(request, body, signing, ServiceName, DateTimeOffset.UtcNow);
        }
    }
}
