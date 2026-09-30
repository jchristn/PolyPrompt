namespace PolyPrompt.Clients
{
    /// <summary>
    /// Wire details shared by the Anthropic clients.
    /// </summary>
    internal static class AnthropicProtocol
    {
        internal const string DefaultEndpoint = "https://api.anthropic.com";
        internal const string DefaultVersion = "2023-06-01";
        internal const string Header = "[Anthropic] ";

        private const string ApiKeyHeader = "x-api-key";
        private const string VersionHeader = "anthropic-version";
        private const string WorkspaceHeader = "anthropic-workspace-id";

        /// <summary>
        /// Attach the API key (Anthropic does not use bearer authorization), the API version, and the workspace id when set.
        /// </summary>
        internal static void ApplyHeaders(HttpRequestMessage request, string? apiKey, string version, string? workspaceId)
        {
            Set(request, ApiKeyHeader, apiKey);
            Set(request, VersionHeader, version);
            Set(request, WorkspaceHeader, workspaceId);
        }

        /// <summary>
        /// Validate an anthropic-version value.
        /// </summary>
        internal static string ValidateVersion(string? value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(parameterName);
            return value;
        }

        /// <summary>
        /// Normalize a workspace id: empty or whitespace means none.
        /// </summary>
        internal static string? NormalizeWorkspace(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static void Set(HttpRequestMessage request, string name, string? value)
        {
            request.Headers.Remove(name);
            if (!string.IsNullOrEmpty(value)) request.Headers.TryAddWithoutValidation(name, value);
        }
    }
}
