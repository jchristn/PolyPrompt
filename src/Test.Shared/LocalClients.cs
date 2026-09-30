namespace Test.Shared
{
    using System.Net;
    using System.Text;
    using PolyPrompt.Auth;
    using PolyPrompt.Clients;

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
        /// Every client, pointed at the endpoint. Cohere clients use the Cohere test key so the local server routes them.
        /// </summary>
        public static List<ClientBase> All(string endpoint, HttpClient? httpClient = null)
        {
            List<ClientBase> clients = new List<ClientBase>();
            clients.AddRange(Completion(endpoint, httpClient));
            clients.AddRange(Embedding(endpoint, httpClient));
            clients.Add(new TeiSparseEmbeddingClient(endpoint, TestKey, httpClient: httpClient));
            clients.AddRange(Rerank(endpoint, httpClient));
            clients.AddRange(Classification(endpoint, httpClient));
            clients.Add(new TypeSafeDecisionClient(endpoint, TestKey, httpClient: httpClient));
            clients.AddRange(Models(endpoint, httpClient));
            foreach (ClientBase client in clients) client.TimeoutMs = 3000;
            return clients;
        }

        public static List<CompletionClientBase> Completion(string endpoint, HttpClient? httpClient = null)
        {
            return Timed(new List<CompletionClientBase>
            {
                new OllamaCompletionClient(endpoint, TestKey, httpClient: httpClient),
                new OpenAiCompletionClient(endpoint, TestKey, httpClient: httpClient),
                new AzureOpenAiCompletionClient(endpoint, "test-deployment", TestKey, apiVersion: "2024-10-21", httpClient: httpClient),
                new GeminiCompletionClient(endpoint, TestKey, httpClient: httpClient),
                new VertexAiCompletionClient("test-project", "us-central1", new StaticTokenCredential("vertex-token"), endpoint, httpClient: httpClient),
                new AnthropicCompletionClient(endpoint, TestKey, httpClient: httpClient),
                new CohereCompletionClient(endpoint, LocalExtendedRoutes.CohereTestKey, httpClient: httpClient),
                new BedrockCompletionClient(AwsCredential(), "us-east-1", endpoint, httpClient: httpClient),
            });
        }

        public static List<EmbeddingClientBase> Embedding(string endpoint, HttpClient? httpClient = null)
        {
            return Timed(new List<EmbeddingClientBase>
            {
                new OllamaEmbeddingClient(endpoint, TestKey, httpClient: httpClient),
                new OpenAiEmbeddingClient(endpoint, TestKey, httpClient: httpClient),
                new AzureOpenAiEmbeddingClient(endpoint, "test-embedding-deployment", TestKey, apiVersion: "2024-10-21", httpClient: httpClient),
                new GeminiEmbeddingClient(endpoint, TestKey, httpClient: httpClient),
                new VertexAiEmbeddingClient("test-project", "us-central1", new StaticTokenCredential("vertex-token"), endpoint, httpClient: httpClient),
                new BedrockEmbeddingClient(AwsCredential(), "us-east-1", endpoint, httpClient: httpClient),
                new VoyageAiEmbeddingClient(endpoint, TestKey, httpClient: httpClient),
                new CohereEmbeddingClient(endpoint, LocalExtendedRoutes.CohereTestKey, httpClient: httpClient),
                new TeiEmbeddingClient(endpoint, TestKey, httpClient: httpClient),
            });
        }

        public static List<RerankClientBase> Rerank(string endpoint, HttpClient? httpClient = null)
        {
            return Timed(new List<RerankClientBase>
            {
                new BedrockRerankClient(AwsCredential(), "us-east-1", endpoint, httpClient: httpClient),
                new VoyageAiRerankClient(endpoint, TestKey, httpClient: httpClient),
                new CohereRerankClient(endpoint, LocalExtendedRoutes.CohereTestKey, httpClient: httpClient),
                new TeiRerankClient(endpoint, TestKey, httpClient: httpClient),
            });
        }

        public static List<ClassificationClientBase> Classification(string endpoint, HttpClient? httpClient = null)
        {
            return Timed(new List<ClassificationClientBase>
            {
                new CohereClassificationClient(endpoint, LocalExtendedRoutes.CohereTestKey, httpClient: httpClient),
                new TeiClassificationClient(endpoint, TestKey, httpClient: httpClient),
            });
        }

        public static List<ModelClientBase> Models(string endpoint, HttpClient? httpClient = null)
        {
            return Timed(new List<ModelClientBase>
            {
                new OllamaModelClient(endpoint, TestKey, httpClient: httpClient),
                new OpenAiModelClient(endpoint, TestKey, httpClient: httpClient),
                new AzureOpenAiModelClient(endpoint, TestKey, apiVersion: "2024-10-21", httpClient: httpClient),
                new GeminiModelClient(endpoint, TestKey, httpClient: httpClient),
                new AnthropicModelClient(endpoint, TestKey, httpClient: httpClient),
                new CohereModelClient(endpoint, LocalExtendedRoutes.CohereTestKey, httpClient: httpClient),
                new BedrockModelClient(AwsCredential(), "us-east-1", endpoint, httpClient: httpClient),
                new TeiModelClient(endpoint, TestKey, httpClient: httpClient),
            });
        }

        public static void DisposeAll(IEnumerable<ClientBase> clients)
        {
            foreach (ClientBase client in clients) client.Dispose();
        }

        private static List<T> Timed<T>(List<T> clients) where T : ClientBase
        {
            foreach (T client in clients) client.TimeoutMs = 3000;
            return clients;
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
