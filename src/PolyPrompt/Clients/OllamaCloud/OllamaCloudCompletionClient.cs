namespace PolyPrompt.Clients
{
    using SyslogLogging;

    /// <summary>
    /// Ollama Cloud completion client using the native Ollama API at https://ollama.com:443.
    /// Inherits Ollama's operations and settings and requires bearer authentication.
    /// </summary>
    public sealed class OllamaCloudCompletionClient : OllamaCompletionClient
    {
        /// <summary>
        /// Initialize a new Ollama Cloud completion client with a fixed endpoint and bearer authentication.
        /// </summary>
        /// <param name="apiKey">Required nonempty Ollama Cloud bearer token.</param>
        /// <param name="logging">Optional logging module.</param>
        /// <param name="httpClient">Optional HTTP client; see <see cref="ClientBase"/>.</param>
        /// <exception cref="ArgumentException">The token is empty or contains whitespace.</exception>
        public OllamaCloudCompletionClient(string apiKey, LoggingModule? logging = null, HttpClient? httpClient = null)
            : base(OllamaCloudProtocol.Endpoint, OllamaCloudProtocol.RequireToken(apiKey), logging, httpClient)
        {
            _Header = "[OllamaCloud] ";
        }
    }
}
