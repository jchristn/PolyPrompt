namespace PolyPrompt.Auth
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using SerializationHelper;

    /// <summary>
    /// Supplies a short-lived OAuth bearer token per request. Used by providers whose credentials expire and
    /// must be refreshed (Google Vertex AI via Application Default Credentials or a service-account key; Azure
    /// OpenAI via Azure AD). The provider owns caching and refresh; callers simply await a valid token.
    /// </summary>
    public interface ICredentialProvider
    {
        /// <summary>
        /// Return a currently-valid bearer token, refreshing transparently if the cached token is missing or
        /// close to expiry.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A valid access token (without the "Bearer " prefix).</returns>
        Task<string> GetBearerTokenAsync(CancellationToken token = default);
    }

    /// <summary>
    /// An <see cref="ICredentialProvider"/> that returns a fixed, caller-supplied token. Useful when a token
    /// is obtained out-of-band (for example <c>gcloud auth print-access-token</c>) or for tests. The token is
    /// never refreshed.
    /// </summary>
    public sealed class StaticTokenCredential : ICredentialProvider
    {
        private readonly string _Token;

        /// <summary>
        /// Initialize a static token credential.
        /// </summary>
        /// <param name="token">The bearer token to return (without the "Bearer " prefix).</param>
        /// <exception cref="ArgumentNullException">Thrown when token is null or empty.</exception>
        public StaticTokenCredential(string token)
        {
            if (string.IsNullOrEmpty(token)) throw new ArgumentNullException(nameof(token));
            _Token = token;
        }

        /// <inheritdoc />
        public Task<string> GetBearerTokenAsync(CancellationToken token = default)
        {
            return Task.FromResult(_Token);
        }
    }

    /// <summary>
    /// Base class for credential providers that fetch an access token with a finite lifetime and cache it
    /// until shortly before expiry. Refresh is serialized so concurrent callers share one token exchange.
    /// </summary>
    public abstract class CachingCredentialProvider : ICredentialProvider
    {
        #region Private-Members

        // Refresh this far ahead of the stated expiry so an in-flight request never carries a just-expired
        // token. Cloud access tokens live ~1h; a minute of slack is safe and generous.
        private static readonly TimeSpan _RefreshMargin = TimeSpan.FromSeconds(60);

        private readonly SemaphoreSlim _RefreshLock = new SemaphoreSlim(1, 1);
        private string? _CachedToken;
        private DateTime _ExpiresAtUtc = DateTime.MinValue;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<string> GetBearerTokenAsync(CancellationToken token = default)
        {
            if (IsCurrent()) return _CachedToken!;

            await _RefreshLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                // Re-check inside the lock: another caller may have refreshed while we waited.
                if (IsCurrent()) return _CachedToken!;

                TokenResult result = await FetchTokenAsync(token).ConfigureAwait(false);
                if (string.IsNullOrEmpty(result.AccessToken))
                    throw new InvalidOperationException("Credential provider returned an empty access token.");

                _CachedToken = result.AccessToken;
                int lifetimeSeconds = result.ExpiresInSeconds > 0 ? result.ExpiresInSeconds : 3600;
                _ExpiresAtUtc = DateTime.UtcNow.AddSeconds(lifetimeSeconds);
                return _CachedToken;
            }
            finally
            {
                _RefreshLock.Release();
            }
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Fetch a fresh access token from the underlying credential source.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The freshly-minted token and its lifetime.</returns>
        protected abstract Task<TokenResult> FetchTokenAsync(CancellationToken token);

        /// <summary>The result of a token fetch: the access token and its lifetime in seconds.</summary>
        protected readonly struct TokenResult
        {
            /// <summary>The access token (without the "Bearer " prefix).</summary>
            public string AccessToken { get; }

            /// <summary>The token lifetime in seconds; non-positive values are treated as one hour.</summary>
            public int ExpiresInSeconds { get; }

            /// <summary>Initialize a token result.</summary>
            /// <param name="accessToken">The access token.</param>
            /// <param name="expiresInSeconds">The token lifetime in seconds.</param>
            public TokenResult(string accessToken, int expiresInSeconds)
            {
                AccessToken = accessToken;
                ExpiresInSeconds = expiresInSeconds;
            }
        }

        #endregion

        #region Private-Methods

        private bool IsCurrent()
        {
            return _CachedToken != null && DateTime.UtcNow < _ExpiresAtUtc - _RefreshMargin;
        }

        #endregion
    }

    /// <summary>
    /// An <see cref="ICredentialProvider"/> that mints Google Cloud access tokens from a service-account key
    /// (the JSON Google issues). It builds and RS256-signs a JWT assertion, exchanges it at the token
    /// endpoint for an access token, and caches the result until shortly before expiry. No Google SDK is
    /// required — only the framework's RSA and a single HTTP round-trip.
    /// </summary>
    public sealed class ServiceAccountCredential : CachingCredentialProvider, IDisposable
    {
        #region Private-Members

        private const string DefaultScope = "https://www.googleapis.com/auth/cloud-platform";
        private const string DefaultTokenUri = "https://oauth2.googleapis.com/token";
        private const string JwtBearerGrant = "urn:ietf:params:oauth:grant-type:jwt-bearer";

        private readonly string _ClientEmail;
        private readonly string _PrivateKeyPem;
        private readonly string _TokenUri;
        private readonly string _Scope;
        private readonly Serializer _Serializer = new Serializer();
        private readonly HttpClient _HttpClient;
        private readonly bool _OwnsHttpClient;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a service-account credential from its individual fields.
        /// </summary>
        /// <param name="clientEmail">The service account's <c>client_email</c>.</param>
        /// <param name="privateKeyPem">The service account's PEM-encoded <c>private_key</c>.</param>
        /// <param name="scope">OAuth scope to request. Default: cloud-platform.</param>
        /// <param name="tokenUri">Token exchange endpoint. Default: Google's OAuth token endpoint.</param>
        /// <param name="httpClient">Optional HTTP client for the token exchange; when null one is created and owned.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null or empty.</exception>
        public ServiceAccountCredential(
            string clientEmail,
            string privateKeyPem,
            string? scope = null,
            string? tokenUri = null,
            HttpClient? httpClient = null)
        {
            if (string.IsNullOrEmpty(clientEmail)) throw new ArgumentNullException(nameof(clientEmail));
            if (string.IsNullOrEmpty(privateKeyPem)) throw new ArgumentNullException(nameof(privateKeyPem));

            _ClientEmail = clientEmail;
            _PrivateKeyPem = privateKeyPem;
            _Scope = string.IsNullOrEmpty(scope) ? DefaultScope : scope;
            _TokenUri = string.IsNullOrEmpty(tokenUri) ? DefaultTokenUri : tokenUri;
            _OwnsHttpClient = httpClient is null;
            _HttpClient = httpClient ?? new HttpClient();
        }

        /// <summary>
        /// Build a service-account credential from the JSON key file contents Google issues.
        /// </summary>
        /// <param name="serviceAccountJson">The full service-account key JSON.</param>
        /// <param name="scope">OAuth scope to request. Default: cloud-platform.</param>
        /// <param name="httpClient">Optional HTTP client for the token exchange.</param>
        /// <returns>A configured <see cref="ServiceAccountCredential"/>.</returns>
        /// <exception cref="ArgumentException">Thrown when the JSON is missing required fields.</exception>
        public static ServiceAccountCredential FromJson(string serviceAccountJson, string? scope = null, HttpClient? httpClient = null)
        {
            if (string.IsNullOrEmpty(serviceAccountJson)) throw new ArgumentNullException(nameof(serviceAccountJson));

            Serializer serializer = new Serializer();
            Dictionary<string, object>? fields = serializer.DeserializeJson<Dictionary<string, object>>(serviceAccountJson);
            if (fields == null) throw new ArgumentException("Service-account JSON could not be parsed.", nameof(serviceAccountJson));

            string? clientEmail = fields.ContainsKey("client_email") ? fields["client_email"]?.ToString() : null;
            string? privateKey = fields.ContainsKey("private_key") ? fields["private_key"]?.ToString() : null;
            string? tokenUri = fields.ContainsKey("token_uri") ? fields["token_uri"]?.ToString() : null;

            if (string.IsNullOrEmpty(clientEmail) || string.IsNullOrEmpty(privateKey))
                throw new ArgumentException("Service-account JSON must contain client_email and private_key.", nameof(serviceAccountJson));

            return new ServiceAccountCredential(clientEmail, privateKey, scope, tokenUri, httpClient);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<TokenResult> FetchTokenAsync(CancellationToken token)
        {
            string assertion = BuildSignedJwt();

            using FormUrlEncodedContent content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", JwtBearerGrant),
                new KeyValuePair<string, string>("assertion", assertion),
            });

            using HttpResponseMessage response = await _HttpClient.PostAsync(_TokenUri, content, token).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException("Google token exchange failed with HTTP " + (int)response.StatusCode + ": " + body);

            Dictionary<string, object>? parsed = _Serializer.DeserializeJson<Dictionary<string, object>>(body);
            if (parsed == null || !parsed.ContainsKey("access_token"))
                throw new InvalidOperationException("Google token exchange response did not contain an access_token.");

            string accessToken = parsed["access_token"]?.ToString() ?? string.Empty;
            int expiresIn = 3600;
            if (parsed.ContainsKey("expires_in") && int.TryParse(parsed["expires_in"]?.ToString(), out int parsedExpiry))
                expiresIn = parsedExpiry;

            return new TokenResult(accessToken, expiresIn);
        }

        #endregion

        #region Public-Methods

        /// <summary>Dispose the internally-owned HTTP client, if any.</summary>
        public void Dispose()
        {
            if (_OwnsHttpClient) _HttpClient.Dispose();
        }

        #endregion

        #region Private-Methods

        private string BuildSignedJwt()
        {
            long issuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long expiresAt = issuedAt + 3600;

            Dictionary<string, object> header = new Dictionary<string, object>
            {
                { "alg", "RS256" },
                { "typ", "JWT" },
            };

            Dictionary<string, object> claims = new Dictionary<string, object>
            {
                { "iss", _ClientEmail },
                { "scope", _Scope },
                { "aud", _TokenUri },
                { "iat", issuedAt },
                { "exp", expiresAt },
            };

            string encodedHeader = Base64Url(Encoding.UTF8.GetBytes(_Serializer.SerializeJson(header, false)));
            string encodedClaims = Base64Url(Encoding.UTF8.GetBytes(_Serializer.SerializeJson(claims, false)));
            string signingInput = encodedHeader + "." + encodedClaims;

            using RSA rsa = RSA.Create();
            rsa.ImportFromPem(_PrivateKeyPem);
            byte[] signature = rsa.SignData(Encoding.UTF8.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            return signingInput + "." + Base64Url(signature);
        }

        private static string Base64Url(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        #endregion
    }

    /// <summary>
    /// An <see cref="ICredentialProvider"/> implementing Google Application Default Credentials resolution:
    /// if <c>GOOGLE_APPLICATION_CREDENTIALS</c> points at a service-account key file, tokens are minted from
    /// it; otherwise the GCE/Cloud Run metadata server is queried for the attached service account's token.
    /// Tokens are cached until shortly before expiry.
    /// </summary>
    public sealed class AdcCredential : CachingCredentialProvider, IDisposable
    {
        #region Private-Members

        private const string MetadataTokenUrl =
            "http://metadata.google.internal/computeMetadata/v1/instance/service-accounts/default/token";

        private readonly Serializer _Serializer = new Serializer();
        private readonly HttpClient _HttpClient;
        private readonly bool _OwnsHttpClient;
        private readonly ServiceAccountCredential? _ServiceAccount;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize an Application Default Credentials provider.
        /// </summary>
        /// <param name="httpClient">Optional HTTP client for the token exchange / metadata query.</param>
        public AdcCredential(HttpClient? httpClient = null)
        {
            _OwnsHttpClient = httpClient is null;
            _HttpClient = httpClient ?? new HttpClient();

            string? keyFile = Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS");
            if (!string.IsNullOrEmpty(keyFile) && System.IO.File.Exists(keyFile))
            {
                string json = System.IO.File.ReadAllText(keyFile);
                _ServiceAccount = ServiceAccountCredential.FromJson(json, scope: null, httpClient: _HttpClient);
            }
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<TokenResult> FetchTokenAsync(CancellationToken token)
        {
            // A service-account key file, when configured, is the deterministic path and is preferred.
            if (_ServiceAccount != null)
            {
                string saToken = await _ServiceAccount.GetBearerTokenAsync(token).ConfigureAwait(false);
                // The service-account provider caches on its own; report a conservative lifetime here.
                return new TokenResult(saToken, 3000);
            }

            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, MetadataTokenUrl);
            request.Headers.Add("Metadata-Flavor", "Google");

            using HttpResponseMessage response = await _HttpClient.SendAsync(request, token).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException("GCE metadata token request failed with HTTP " + (int)response.StatusCode + ": " + body);

            Dictionary<string, object>? parsed = _Serializer.DeserializeJson<Dictionary<string, object>>(body);
            if (parsed == null || !parsed.ContainsKey("access_token"))
                throw new InvalidOperationException("GCE metadata token response did not contain an access_token.");

            string accessToken = parsed["access_token"]?.ToString() ?? string.Empty;
            int expiresIn = 3600;
            if (parsed.ContainsKey("expires_in") && int.TryParse(parsed["expires_in"]?.ToString(), out int parsedExpiry))
                expiresIn = parsedExpiry;

            return new TokenResult(accessToken, expiresIn);
        }

        #endregion

        #region Public-Methods

        /// <summary>Dispose the internally-owned HTTP client and any derived service-account credential.</summary>
        public void Dispose()
        {
            _ServiceAccount?.Dispose();
            if (_OwnsHttpClient) _HttpClient.Dispose();
        }

        #endregion
    }
}
