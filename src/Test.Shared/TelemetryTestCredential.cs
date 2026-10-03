namespace Test.Shared
{
    using PolyPrompt.Auth;

    /// <summary>
    /// Caching credential for telemetry tests: returns a fixed token, or throws when <see cref="Fail"/> is set, so tests
    /// can observe cache hits, misses, and failed refreshes. Its type name gives it a unique credential source label.
    /// </summary>
    public sealed class TelemetryTestCredential : CachingCredentialProvider
    {
        /// <summary>
        /// When true, token fetches throw <see cref="InvalidOperationException"/>.
        /// </summary>
        public bool Fail { get; set; } = false;

        /// <summary>
        /// Number of token fetches.
        /// </summary>
        public int FetchCount { get; private set; } = 0;

        /// <inheritdoc />
        protected override Task<TokenResult> FetchTokenAsync(CancellationToken token)
        {
            FetchCount++;
            if (Fail) throw new InvalidOperationException("Telemetry test credential refused to issue a token.");
            return Task.FromResult(new TokenResult("telemetry-token", 3600));
        }
    }
}
