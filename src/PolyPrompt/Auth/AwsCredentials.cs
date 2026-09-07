namespace PolyPrompt.Auth
{
    using System;

    /// <summary>
    /// Immutable set of AWS credentials plus the region they apply to, resolved once per request by an
    /// <see cref="IAwsCredentialProvider"/> and consumed by <see cref="SigV4Signer"/>. Supports permanent
    /// access keys as well as temporary (role/STS) credentials that carry a session token.
    /// </summary>
    public sealed class AwsCredentials
    {
        #region Public-Members

        /// <summary>AWS access key id (e.g. the value of <c>AWS_ACCESS_KEY_ID</c>).</summary>
        public string AccessKeyId { get; }

        /// <summary>AWS secret access key (e.g. the value of <c>AWS_SECRET_ACCESS_KEY</c>).</summary>
        public string SecretAccessKey { get; }

        /// <summary>
        /// Optional session token for temporary credentials (e.g. the value of <c>AWS_SESSION_TOKEN</c>).
        /// When present it is sent as the <c>x-amz-security-token</c> header and folded into the signature.
        /// Null for permanent credentials.
        /// </summary>
        public string? SessionToken { get; }

        /// <summary>AWS region the credentials target, e.g. <c>us-east-1</c>.</summary>
        public string Region { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new set of AWS credentials.
        /// </summary>
        /// <param name="accessKeyId">AWS access key id.</param>
        /// <param name="secretAccessKey">AWS secret access key.</param>
        /// <param name="region">AWS region, e.g. <c>us-east-1</c>.</param>
        /// <param name="sessionToken">Optional session token for temporary credentials.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null or empty.</exception>
        public AwsCredentials(string accessKeyId, string secretAccessKey, string region, string? sessionToken = null)
        {
            if (string.IsNullOrEmpty(accessKeyId)) throw new ArgumentNullException(nameof(accessKeyId));
            if (string.IsNullOrEmpty(secretAccessKey)) throw new ArgumentNullException(nameof(secretAccessKey));
            if (string.IsNullOrEmpty(region)) throw new ArgumentNullException(nameof(region));

            AccessKeyId = accessKeyId;
            SecretAccessKey = secretAccessKey;
            Region = region;
            SessionToken = string.IsNullOrEmpty(sessionToken) ? null : sessionToken;
        }

        #endregion
    }
}
