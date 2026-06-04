namespace Test.Shared
{
    using System.Net;
    using System.Net.Sockets;
    using System.Text;

    internal sealed class LocalOpenAiTestServer : IDisposable
    {
        private readonly HttpListener _Listener;
        private readonly CancellationTokenSource _Cancellation = new CancellationTokenSource();
        private readonly Task _Loop;
        private bool _Disposed = false;

        public string Endpoint { get; }

        private LocalOpenAiTestServer(HttpListener listener, string endpoint)
        {
            _Listener = listener;
            Endpoint = endpoint.TrimEnd('/');
            _Loop = Task.Run(ListenLoopAsync);
        }

        public static LocalOpenAiTestServer Start()
        {
            int port = GetFreePort();
            string prefix = "http://127.0.0.1:" + port + "/";
            HttpListener listener = new HttpListener();
            listener.Prefixes.Add(prefix);
            listener.Start();
            return new LocalOpenAiTestServer(listener, prefix);
        }

        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;

            _Cancellation.Cancel();
            try { _Listener.Stop(); } catch { }
            try { _Listener.Close(); } catch { }
            try { _Loop.Wait(TimeSpan.FromSeconds(2)); } catch { }
            _Cancellation.Dispose();
        }

        private async Task ListenLoopAsync()
        {
            while (!_Cancellation.IsCancellationRequested)
            {
                try
                {
                    HttpListenerContext context = await _Listener.GetContextAsync().WaitAsync(_Cancellation.Token).ConfigureAwait(false);
                    _ = Task.Run(() => HandleContextAsync(context));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (HttpListenerException)
                {
                    if (_Cancellation.IsCancellationRequested) break;
                }
            }
        }

        private async Task HandleContextAsync(HttpListenerContext context)
        {
            try
            {
                string path = context.Request.Url?.AbsolutePath ?? string.Empty;
                string requestBody;
                using (StreamReader reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8))
                {
                    requestBody = await reader.ReadToEndAsync().ConfigureAwait(false);
                }

                if (path == "/v1/chat/completions")
                {
                    if (requestBody.Contains("\"stream\":true", StringComparison.OrdinalIgnoreCase))
                    {
                        await WriteStreamingChatAsync(context).ConfigureAwait(false);
                    }
                    else
                    {
                        await WriteJsonAsync(
                            context,
                            200,
                            "{\"id\":\"chatcmpl-local\",\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"pong\"},\"finish_reason\":\"stop\",\"index\":0}]}").ConfigureAwait(false);
                    }
                    return;
                }

                if (path == "/v1/models")
                {
                    await WriteJsonAsync(
                        context,
                        200,
                        "{\"data\":[{\"id\":\"test-model\",\"object\":\"model\",\"owned_by\":\"local\"}]}").ConfigureAwait(false);
                    return;
                }

                await WriteJsonAsync(context, 404, "{\"error\":\"not found\"}").ConfigureAwait(false);
            }
            catch
            {
                try { context.Response.Abort(); } catch { }
            }
        }

        private async Task WriteStreamingChatAsync(HttpListenerContext context)
        {
            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/event-stream";
            context.Response.SendChunked = true;

            string chunk = "data: {\"id\":\"chatcmpl-local\",\"model\":\"test-model\",\"choices\":[{\"delta\":{\"content\":\"hello\"},\"index\":0,\"finish_reason\":null}]}\n\n";
            byte[] bytes = Encoding.UTF8.GetBytes(chunk);
            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length, _Cancellation.Token).ConfigureAwait(false);
            await context.Response.OutputStream.FlushAsync(_Cancellation.Token).ConfigureAwait(false);

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), _Cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private static async Task WriteJsonAsync(HttpListenerContext context, int statusCode, string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            context.Response.Close();
        }

        private static int GetFreePort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
