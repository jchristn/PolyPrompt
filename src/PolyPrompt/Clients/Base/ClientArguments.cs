namespace PolyPrompt.Clients
{
    /// <summary>
    /// Fail-fast validation of constructor arguments shared by the provider clients.
    /// </summary>
    internal static class ClientArguments
    {
        /// <summary>
        /// Validate an endpoint: an absolute http or https URL.
        /// </summary>
        internal static string Endpoint(string? endpoint, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(endpoint)) throw new ArgumentNullException(parameterName);
            string trimmed = endpoint.Trim();
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException(parameterName + " must be an absolute http or https URL, for example https://api.example.com.", parameterName);
            return trimmed;
        }

        /// <summary>
        /// Validate a required API key: not blank, no whitespace, and not a URL.
        /// </summary>
        /// <param name="apiKey">Key.</param>
        /// <param name="parameterName">Parameter name.</param>
        /// <param name="urlHint">Extra guidance appended when the key looks like a URL, or null.</param>
        internal static string RequiredKey(string? apiKey, string parameterName, string? urlHint)
        {
            if (apiKey == null) throw new ArgumentNullException(parameterName);
            if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException(parameterName + " cannot be empty or whitespace.", parameterName);
            return CheckKey(apiKey, parameterName, urlHint);
        }

        /// <summary>
        /// Validate an optional API key: null or empty is allowed; otherwise no whitespace and not a URL.
        /// </summary>
        internal static string? OptionalKey(string? apiKey, string parameterName, string? urlHint = null)
        {
            if (string.IsNullOrEmpty(apiKey)) return null;
            if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException(parameterName + " cannot be whitespace.", parameterName);
            return CheckKey(apiKey, parameterName, urlHint);
        }

        private static string CheckKey(string apiKey, string parameterName, string? urlHint)
        {
            if (LooksLikeUrl(apiKey))
            {
                string message = parameterName + " looks like a URL.";
                if (!string.IsNullOrEmpty(urlHint)) message += " " + urlHint;
                else message += " Pass the endpoint as the endpoint argument.";
                throw new ArgumentException(message, parameterName);
            }

            if (apiKey.Any(char.IsWhiteSpace))
                throw new ArgumentException(parameterName + " must not contain whitespace.", parameterName);
            return apiKey;
        }

        private static bool LooksLikeUrl(string value)
        {
            string v = value.Trim();
            return v.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || v.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }
    }
}
