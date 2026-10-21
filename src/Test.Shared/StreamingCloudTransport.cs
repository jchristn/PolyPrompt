namespace Test.Shared
{
    internal sealed class StreamingCloudTransport : DelegatingHandler
    {
        private readonly string _Endpoint;
        internal int RequestCount { get; private set; }
        internal bool Disposed { get; private set; }

        internal StreamingCloudTransport(string endpoint) : base(new HttpClientHandler())
        {
            _Endpoint = endpoint;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            RequestCount++;
            if (request.RequestUri!.Host == "ollama.com")
                request.RequestUri = new Uri(_Endpoint + request.RequestUri.AbsolutePath);
            if (request.RequestUri.Host != "127.0.0.1")
                throw new InvalidOperationException("Streaming fixture forbids external transport.");
            return base.SendAsync(request, token);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Disposed = true;
            base.Dispose(disposing);
        }
    }
}
