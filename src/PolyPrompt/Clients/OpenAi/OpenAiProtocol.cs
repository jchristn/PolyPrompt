namespace PolyPrompt.Clients
{
    using System.Net.Http.Headers;
    using PolyPrompt.Auth;

    /// <summary>
    /// Wire details shared by the OpenAI clients.
    /// </summary>
    internal static class OpenAiProtocol
    {
        internal const string DefaultEndpoint = "https://api.openai.com";
        internal const string Header = "[OpenAI] ";

        /// <summary>
        /// Build the URL for an operation path such as <c>chat/completions</c>. The <c>/v1</c> prefix is added unless the
        /// endpoint already ends with it, so both <c>https://api.openai.com</c> and OpenAI-compatible base URLs such as
        /// <c>http://localhost:11434/v1</c> work.
        /// </summary>
        internal static string BuildApiUrl(string endpoint, string path)
        {
            string trimmed = endpoint.TrimEnd('/');
            string normalizedPath = path.TrimStart('/');

            if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                return trimmed + "/" + normalizedPath;

            return trimmed + "/v1/" + normalizedPath;
        }

        /// <summary>
        /// Attach the bearer token, when one is configured.
        /// </summary>
        internal static void ApplyAuth(HttpRequestMessage request, string? apiKey)
        {
            if (!string.IsNullOrEmpty(apiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    /// <summary>
    /// Wire details shared by the Azure OpenAI clients.
    /// </summary>
    internal static class AzureOpenAiProtocol
    {
        internal const string Header = "[AzureOpenAI] ";

        /// <summary>
        /// Build an Azure OpenAI URL. Inference operations are deployment-scoped
        /// (<c>/openai/deployments/{deployment}/{path}</c>); model listing is resource-scoped (<c>/openai/{path}</c>).
        /// Every URL carries the <c>api-version</c> query parameter.
        /// </summary>
        internal static string BuildApiUrl(string endpoint, string path, string? deployment, string apiVersion)
        {
            string trimmed = endpoint.TrimEnd('/');
            string normalizedPath = path.TrimStart('/');

            string resourcePath = deployment == null
                ? trimmed + "/openai/" + normalizedPath
                : trimmed + "/openai/deployments/" + Uri.EscapeDataString(deployment) + "/" + normalizedPath;

            return resourcePath + "?api-version=" + Uri.EscapeDataString(apiVersion);
        }

        /// <summary>
        /// Attach either the <c>api-key</c> header or a freshly obtained Azure AD bearer token.
        /// </summary>
        internal static async Task ApplyAuthAsync(HttpRequestMessage request, string? apiKey, ICredentialProvider? credential, CancellationToken token)
        {
            if (credential != null)
            {
                string bearer = await credential.GetBearerTokenAsync(token).ConfigureAwait(false);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            }
            else if (!string.IsNullOrEmpty(apiKey))
            {
                request.Headers.Remove("api-key");
                request.Headers.TryAddWithoutValidation("api-key", apiKey);
            }
        }

        /// <summary>
        /// Validate an api-version value.
        /// </summary>
        internal static string ValidateApiVersion(string? value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(parameterName);
            return value;
        }
    }
}
