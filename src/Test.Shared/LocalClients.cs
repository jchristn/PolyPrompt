namespace Test.Shared
{
    using System.Net;
    using System.Text;
    using PolyPrompt.Auth;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;

    /// <summary>
    /// Builds one instance of every PolyPrompt client, pointed at a local endpoint, with test credentials. Used by the
    /// cross-provider cases that must hold for every client.
    /// </summary>
    internal static class LocalClients
    {
        public const string TestKey = "test-key";

        public static StaticAwsCredential AwsCredential()
        {
            return new StaticAwsCredential("AKIDTESTEXAMPLE", "wJalrXUtnFEMI/K7MDENG+bPxRfiCYEXAMPLEKEY", "us-east-1");
        }

        /// <summary>
        /// One client per provider, pointed at the endpoint, with every section's model set so each supported operation
        /// can run. Cohere clients use the Cohere test key so the local server routes them; OpenAiClient uses the OpenAI
        /// Responses fixture key; Azure uses the v1 routes.
        /// </summary>
        public static List<ProviderClientBase> All(string endpoint, HttpClient? httpClient = null)
        {
            ClientOptions options = new ClientOptions { HttpClient = httpClient, TimeoutMs = 3000 };
            return new List<ProviderClientBase>
            {
                new XAiClient(LocalXAiRoutes.Key, endpoint, options),
                new OllamaClient(endpoint, TestKey, options),
                new OpenAiCompatibleClient(endpoint, TestKey, options) { Defaults = { Completion = { Model = "gpt-4o-mini" }, Embedding = { Model = "text-embedding-3-small" } } },
                new OpenAiClient(LocalOpenAiResponsesRoutes.Key, endpoint, options),
                new AzureOpenAiClient(endpoint, TestKey, options) { Defaults = { Completion = { Model = "test-deployment" }, Embedding = { Model = "test-embedding-deployment" } } },
                new GeminiClient(TestKey, endpoint, options),
                new VertexAiClient("test-project", "us-central1", new StaticTokenCredential("vertex-token"), endpoint, options),
                new AnthropicClient(TestKey, endpoint, options),
                new CohereClient(LocalExtendedRoutes.CohereTestKey, endpoint, options),
                new BedrockClient("us-east-1", AwsCredential(), endpoint, options) { Defaults = { Completion = { Model = "anthropic.claude-3-5-sonnet-20240620-v1:0" } } },
                new VoyageAiClient(TestKey, endpoint, options),
                new TeiClient(endpoint, TestKey, options),
                new TypeSafeClient(TestKey, endpoint, options),
            };
        }

        /// <summary>
        /// The provider clients that support a capability; the others are disposed.
        /// </summary>
        public static List<ProviderClientBase> Supporting(Capability capability, string endpoint, HttpClient? httpClient = null)
        {
            List<ProviderClientBase> all = All(endpoint, httpClient);
            foreach (ProviderClientBase client in all.Where(c => !c.Supports(capability))) client.Dispose();
            return all.Where(c => c.Supports(capability)).ToList();
        }

        public static List<ProviderClientBase> Completion(string endpoint, HttpClient? httpClient = null) => Supporting(Capability.Chat, endpoint, httpClient);

        public static List<ProviderClientBase> Embedding(string endpoint, HttpClient? httpClient = null) => Supporting(Capability.Embed, endpoint, httpClient);

        public static List<ProviderClientBase> Rerank(string endpoint, HttpClient? httpClient = null) => Supporting(Capability.Rerank, endpoint, httpClient);

        public static List<ProviderClientBase> Classification(string endpoint, HttpClient? httpClient = null) => Supporting(Capability.Classify, endpoint, httpClient);

        public static List<ProviderClientBase> Models(string endpoint, HttpClient? httpClient = null) => Supporting(Capability.Models, endpoint, httpClient);

        public static void DisposeAll(IEnumerable<HttpClientBase> clients)
        {
            foreach (HttpClientBase client in clients) client.Dispose();
        }
    }

    /// <summary>
    /// An HTTP handler that answers every request with a function, and records the requests and the peak number in
    /// flight at once.
    /// </summary>
    internal sealed class LocalFakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, int, Task<HttpResponseMessage>> _Respond;
        private readonly object _Lock = new object();
        private int _InFlight = 0;
        private int _Count = 0;

        public LocalFakeHandler(Func<HttpRequestMessage, int, Task<HttpResponseMessage>> respond)
        {
            _Respond = respond;
        }

        /// <summary>
        /// A handler that answers every request with the same status and body.
        /// </summary>
        public static LocalFakeHandler Always(HttpStatusCode status, string body = "{}")
        {
            return new LocalFakeHandler((request, index) => Task.FromResult(Json(status, body)));
        }

        public int PeakInFlight { get; private set; }

        public List<string> RequestUrls { get; } = new List<string>();

        public int Count
        {
            get { lock (_Lock) return _Count; }
        }

        public static HttpResponseMessage Json(HttpStatusCode status, string body)
        {
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            int index;
            lock (_Lock)
            {
                index = _Count++;
                _InFlight++;
                if (_InFlight > PeakInFlight) PeakInFlight = _InFlight;
                RequestUrls.Add(request.RequestUri?.ToString() ?? string.Empty);
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await _Respond(request, index).ConfigureAwait(false);
            }
            finally
            {
                lock (_Lock) _InFlight--;
            }
        }
    }
}
