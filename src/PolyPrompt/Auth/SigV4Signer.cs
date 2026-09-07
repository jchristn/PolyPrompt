namespace PolyPrompt.Auth
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Net.Http;
    using System.Security.Cryptography;
    using System.Text;

    /// <summary>
    /// A minimal, dependency-free implementation of AWS Signature Version 4 (header-based signing), used to
    /// authenticate Bedrock requests. Signing is per-request: the signature covers the method, canonical
    /// path and query, the signed headers, and a SHA-256 hash of the body, stamped with the request time.
    /// The low-level building blocks (<see cref="CreateCanonicalRequest"/>, <see cref="CreateStringToSign"/>,
    /// <see cref="DeriveSigningKey"/>, <see cref="HexSha256"/>) are public so they can be verified directly
    /// against AWS's published canonical test vectors.
    /// </summary>
    public static class SigV4Signer
    {
        #region Public-Constants

        /// <summary>The SigV4 algorithm identifier.</summary>
        public const string Algorithm = "AWS4-HMAC-SHA256";

        /// <summary>SHA-256 hex digest of an empty payload (used for bodyless requests such as GET).</summary>
        public const string EmptyPayloadHash =
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Sign an outbound request in place, adding the <c>x-amz-date</c>, <c>x-amz-content-sha256</c>,
        /// <c>Authorization</c> (and <c>x-amz-security-token</c> for temporary credentials) headers. The
        /// <c>host</c> header is derived from the request URI and folded into the signature.
        /// </summary>
        /// <param name="request">The request to sign; must have an absolute <see cref="HttpRequestMessage.RequestUri"/>.</param>
        /// <param name="body">The exact request body bytes (empty array for a bodyless request).</param>
        /// <param name="credentials">The AWS credentials and region.</param>
        /// <param name="service">The AWS service name, e.g. <c>bedrock</c>.</param>
        /// <param name="signingTime">The instant to sign for (usually now, in UTC).</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the request has no absolute URI.</exception>
        public static void Sign(
            HttpRequestMessage request,
            byte[] body,
            AwsCredentials credentials,
            string service,
            DateTimeOffset signingTime)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (credentials == null) throw new ArgumentNullException(nameof(credentials));
            if (string.IsNullOrEmpty(service)) throw new ArgumentNullException(nameof(service));
            if (request.RequestUri == null || !request.RequestUri.IsAbsoluteUri)
                throw new ArgumentException("SigV4 signing requires an absolute request URI.", nameof(request));

            body ??= Array.Empty<byte>();
            Uri uri = request.RequestUri;

            string amzDate = signingTime.UtcDateTime.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
            string dateStamp = signingTime.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            string payloadHash = HexSha256(body);
            string host = uri.IsDefaultPort ? uri.Host : uri.Host + ":" + uri.Port;

            // Headers folded into the signature. Host and the two x-amz-* markers are always signed; the
            // security token is signed only for temporary credentials.
            SortedDictionary<string, string> signedHeaders = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                { "host", host },
                { "x-amz-content-sha256", payloadHash },
                { "x-amz-date", amzDate },
            };
            if (!string.IsNullOrEmpty(credentials.SessionToken))
                signedHeaders["x-amz-security-token"] = credentials.SessionToken!;

            string canonicalUri = CanonicalizePath(uri.AbsolutePath);
            string canonicalQuery = CanonicalizeQuery(uri.Query);

            string canonicalRequest = CreateCanonicalRequest(
                request.Method.Method, canonicalUri, canonicalQuery, signedHeaders, payloadHash);
            string stringToSign = CreateStringToSign(signingTime, credentials.Region, service, canonicalRequest);
            byte[] signingKey = DeriveSigningKey(credentials.SecretAccessKey, dateStamp, credentials.Region, service);
            string signature = ToHex(HmacSha256(signingKey, Encoding.UTF8.GetBytes(stringToSign)));

            string signedHeaderNames = string.Join(";", signedHeaders.Keys);
            string credentialScope = dateStamp + "/" + credentials.Region + "/" + service + "/aws4_request";
            string authorization = Algorithm
                + " Credential=" + credentials.AccessKeyId + "/" + credentialScope
                + ", SignedHeaders=" + signedHeaderNames
                + ", Signature=" + signature;

            // Attach the computed headers. TryAddWithoutValidation keeps values verbatim (some services are
            // strict about exact byte-for-byte header content matching the signature).
            request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
            request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
            if (!string.IsNullOrEmpty(credentials.SessionToken))
                request.Headers.TryAddWithoutValidation("x-amz-security-token", credentials.SessionToken);
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }

        /// <summary>
        /// Build the SigV4 canonical request string.
        /// </summary>
        /// <param name="httpMethod">The HTTP method (e.g. GET, POST).</param>
        /// <param name="canonicalUri">The canonicalized (URI-encoded) path.</param>
        /// <param name="canonicalQueryString">The canonicalized, sorted query string.</param>
        /// <param name="signedHeaders">The headers to sign, keyed by lowercase name, ordered ordinally.</param>
        /// <param name="payloadHash">The hex SHA-256 of the payload.</param>
        /// <returns>The canonical request string.</returns>
        public static string CreateCanonicalRequest(
            string httpMethod,
            string canonicalUri,
            string canonicalQueryString,
            SortedDictionary<string, string> signedHeaders,
            string payloadHash)
        {
            StringBuilder canonicalHeaders = new StringBuilder();
            foreach (KeyValuePair<string, string> header in signedHeaders)
            {
                canonicalHeaders.Append(header.Key).Append(':').Append(NormalizeHeaderValue(header.Value)).Append('\n');
            }

            string signedHeaderNames = string.Join(";", signedHeaders.Keys);

            return httpMethod + "\n"
                + canonicalUri + "\n"
                + canonicalQueryString + "\n"
                + canonicalHeaders + "\n"
                + signedHeaderNames + "\n"
                + payloadHash;
        }

        /// <summary>
        /// Build the SigV4 "string to sign" from a canonical request.
        /// </summary>
        /// <param name="signingTime">The signing instant.</param>
        /// <param name="region">The AWS region.</param>
        /// <param name="service">The AWS service name.</param>
        /// <param name="canonicalRequest">The canonical request produced by <see cref="CreateCanonicalRequest"/>.</param>
        /// <returns>The string to sign.</returns>
        public static string CreateStringToSign(
            DateTimeOffset signingTime, string region, string service, string canonicalRequest)
        {
            string amzDate = signingTime.UtcDateTime.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
            string dateStamp = signingTime.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            string credentialScope = dateStamp + "/" + region + "/" + service + "/aws4_request";
            string hashedCanonicalRequest = HexSha256(Encoding.UTF8.GetBytes(canonicalRequest));

            return Algorithm + "\n"
                + amzDate + "\n"
                + credentialScope + "\n"
                + hashedCanonicalRequest;
        }

        /// <summary>
        /// Derive the SigV4 signing key via the HMAC chain over date, region, service, and terminator.
        /// </summary>
        /// <param name="secretKey">The AWS secret access key.</param>
        /// <param name="dateStamp">The <c>yyyyMMdd</c> date stamp.</param>
        /// <param name="region">The AWS region.</param>
        /// <param name="service">The AWS service name.</param>
        /// <returns>The derived signing key bytes.</returns>
        public static byte[] DeriveSigningKey(string secretKey, string dateStamp, string region, string service)
        {
            byte[] kDate = HmacSha256(Encoding.UTF8.GetBytes("AWS4" + secretKey), Encoding.UTF8.GetBytes(dateStamp));
            byte[] kRegion = HmacSha256(kDate, Encoding.UTF8.GetBytes(region));
            byte[] kService = HmacSha256(kRegion, Encoding.UTF8.GetBytes(service));
            return HmacSha256(kService, Encoding.UTF8.GetBytes("aws4_request"));
        }

        /// <summary>
        /// Compute the lowercase hex SHA-256 digest of the given bytes.
        /// </summary>
        /// <param name="data">The bytes to hash.</param>
        /// <returns>The lowercase hex digest.</returns>
        public static string HexSha256(byte[] data)
        {
            byte[] hash = SHA256.HashData(data ?? Array.Empty<byte>());
            return ToHex(hash);
        }

        #endregion

        #region Private-Methods

        private static string CanonicalizePath(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath)) return "/";

            // URI-encode each segment (RFC 3986) while preserving path separators. Bedrock model ids embed
            // characters such as ':' and '.', which must be encoded consistently in the canonical path.
            string[] segments = absolutePath.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                segments[i] = UriEncode(segments[i], isPath: false);
            }

            string encoded = string.Join("/", segments);
            return string.IsNullOrEmpty(encoded) ? "/" : encoded;
        }

        private static string CanonicalizeQuery(string query)
        {
            if (string.IsNullOrEmpty(query)) return string.Empty;
            if (query.StartsWith("?", StringComparison.Ordinal)) query = query.Substring(1);
            if (string.IsNullOrEmpty(query)) return string.Empty;

            List<KeyValuePair<string, string>> pairs = new List<KeyValuePair<string, string>>();
            foreach (string part in query.Split('&'))
            {
                if (part.Length == 0) continue;
                int eq = part.IndexOf('=');
                string key = eq >= 0 ? part.Substring(0, eq) : part;
                string value = eq >= 0 ? part.Substring(eq + 1) : string.Empty;
                pairs.Add(new KeyValuePair<string, string>(
                    UriEncode(Uri.UnescapeDataString(key), isPath: false),
                    UriEncode(Uri.UnescapeDataString(value), isPath: false)));
            }

            return string.Join("&", pairs
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .ThenBy(p => p.Value, StringComparer.Ordinal)
                .Select(p => p.Key + "=" + p.Value));
        }

        private static string NormalizeHeaderValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            // Trim and collapse internal runs of whitespace to a single space, per the SigV4 spec.
            string trimmed = value.Trim();
            StringBuilder sb = new StringBuilder(trimmed.Length);
            bool previousWasSpace = false;
            foreach (char c in trimmed)
            {
                if (c == ' ')
                {
                    if (!previousWasSpace) sb.Append(' ');
                    previousWasSpace = true;
                }
                else
                {
                    sb.Append(c);
                    previousWasSpace = false;
                }
            }

            return sb.ToString();
        }

        private static string UriEncode(string value, bool isPath)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            StringBuilder sb = new StringBuilder(value.Length * 2);
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            foreach (byte b in bytes)
            {
                char c = (char)b;
                bool unreserved = (c >= 'A' && c <= 'Z')
                    || (c >= 'a' && c <= 'z')
                    || (c >= '0' && c <= '9')
                    || c == '-' || c == '_' || c == '.' || c == '~';

                if (unreserved || (isPath && c == '/'))
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
                }
            }

            return sb.ToString();
        }

        private static byte[] HmacSha256(byte[] key, byte[] data)
        {
            using HMACSHA256 hmac = new HMACSHA256(key);
            return hmac.ComputeHash(data);
        }

        private static string ToHex(byte[] bytes)
        {
            StringBuilder sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        #endregion
    }
}
