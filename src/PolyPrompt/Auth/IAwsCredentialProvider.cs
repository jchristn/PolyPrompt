namespace PolyPrompt.Auth
{
    using System;

    /// <summary>
    /// Resolves the AWS credentials used to sign a Bedrock request. Resolution happens per request so that
    /// rotating/temporary credentials (role or STS) are always picked up fresh.
    /// </summary>
    public interface IAwsCredentialProvider
    {
        /// <summary>
        /// Resolve the current AWS credentials.
        /// </summary>
        /// <returns>The credentials to sign the next request with.</returns>
        AwsCredentials Resolve();
    }

    /// <summary>
    /// An <see cref="IAwsCredentialProvider"/> backed by fixed, caller-supplied credentials.
    /// </summary>
    public sealed class StaticAwsCredential : IAwsCredentialProvider
    {
        private readonly AwsCredentials _Credentials;

        /// <summary>
        /// Initialize a static credential provider.
        /// </summary>
        /// <param name="credentials">The credentials to return from every <see cref="Resolve"/> call.</param>
        /// <exception cref="ArgumentNullException">Thrown when credentials is null.</exception>
        public StaticAwsCredential(AwsCredentials credentials)
        {
            _Credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        }

        /// <summary>
        /// Convenience constructor building the credentials inline.
        /// </summary>
        /// <param name="accessKeyId">AWS access key id.</param>
        /// <param name="secretAccessKey">AWS secret access key.</param>
        /// <param name="region">AWS region, e.g. <c>us-east-1</c>.</param>
        /// <param name="sessionToken">Optional session token for temporary credentials.</param>
        public StaticAwsCredential(string accessKeyId, string secretAccessKey, string region, string? sessionToken = null)
            : this(new AwsCredentials(accessKeyId, secretAccessKey, region, sessionToken))
        {
        }

        /// <inheritdoc />
        public AwsCredentials Resolve()
        {
            return _Credentials;
        }
    }

    /// <summary>
    /// An <see cref="IAwsCredentialProvider"/> that resolves credentials from the standard AWS environment
    /// variables (<c>AWS_ACCESS_KEY_ID</c>, <c>AWS_SECRET_ACCESS_KEY</c>, optional <c>AWS_SESSION_TOKEN</c>).
    /// The region is taken from an explicit override, else <c>AWS_REGION</c>, else <c>AWS_DEFAULT_REGION</c>.
    /// Resolution reads the environment on every call so rotated values are observed.
    /// </summary>
    public sealed class EnvironmentAwsCredential : IAwsCredentialProvider
    {
        private readonly string? _RegionOverride;

        /// <summary>
        /// Initialize an environment-backed credential provider.
        /// </summary>
        /// <param name="region">
        /// Optional region override. When null the region is read from <c>AWS_REGION</c> or
        /// <c>AWS_DEFAULT_REGION</c> at resolution time.
        /// </param>
        public EnvironmentAwsCredential(string? region = null)
        {
            _RegionOverride = string.IsNullOrEmpty(region) ? null : region;
        }

        /// <inheritdoc />
        /// <exception cref="InvalidOperationException">
        /// Thrown when the required environment variables (access key, secret key, region) are not set.
        /// </exception>
        public AwsCredentials Resolve()
        {
            string? accessKeyId = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID");
            string? secretAccessKey = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY");
            string? sessionToken = Environment.GetEnvironmentVariable("AWS_SESSION_TOKEN");
            string? region = _RegionOverride
                ?? Environment.GetEnvironmentVariable("AWS_REGION")
                ?? Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION");

            if (string.IsNullOrEmpty(accessKeyId) || string.IsNullOrEmpty(secretAccessKey))
                throw new InvalidOperationException("AWS credentials not found in environment (AWS_ACCESS_KEY_ID / AWS_SECRET_ACCESS_KEY).");
            if (string.IsNullOrEmpty(region))
                throw new InvalidOperationException("AWS region not found in environment (set AWS_REGION or AWS_DEFAULT_REGION, or pass a region override).");

            return new AwsCredentials(accessKeyId, secretAccessKey, region, sessionToken);
        }
    }
}
