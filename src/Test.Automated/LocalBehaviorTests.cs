namespace Test.Automated
{
    using System.Diagnostics;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;

    public partial class Program
    {
        private static async Task RunLocalBehaviorTests()
        {
            PrintSection("Local Behavior Tests");

            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiClient client = new OpenAiClient(server.Endpoint, "test-key");
            client.Model = "test-model";
            client.TimeoutMs = 1000;
            client.MaxCallDetails = 2;

            ChatResponse first = await client.ChatAsync("first").ConfigureAwait(false);
            Assert("Local ChatAsync succeeds", first.Success && first.Text == "pong");

            List<CompletionCallDetail> snapshot = client.CallDetails;
            Assert("CallDetails snapshot contains first call", snapshot.Count == 1);

            string? originalUrl = snapshot[0].Url;
            snapshot[0].Url = "mutated";
            Assert("CallDetails snapshot is detached", client.CallDetails[0].Url == originalUrl);

            await client.ChatAsync("second").ConfigureAwait(false);
            await client.ChatAsync("third").ConfigureAwait(false);
            Assert("CallDetails honors max retention", client.CallDetails.Count == 2);

            client.MaxCallDetails = 0;
            await client.ChatAsync("disabled").ConfigureAwait(false);
            Assert("CallDetails can be disabled", client.CallDetails.Count == 0);

            client.MaxCallDetails = 1000;
            await client.ChatAsync("enabled").ConfigureAwait(false);
            Assert("CallDetails can be re-enabled", client.CallDetails.Count == 1);

            client.ClearCallDetails();
            Assert("ClearCallDetails clears retained entries", client.CallDetails.Count == 0);

            client.TimeoutMs = 1;
            Assert("TimeoutMs preserves 1ms values", client.TimeoutMs == 1);

            client.TimeoutMs = 999999;
            Assert("TimeoutMs preserves large values", client.TimeoutMs == 999999);

            bool timeoutRejected = false;
            try
            {
                client.TimeoutMs = 0;
            }
            catch (ArgumentOutOfRangeException)
            {
                timeoutRejected = true;
            }
            Assert("TimeoutMs rejects zero in local tests", timeoutRejected);

            client.TimeoutMs = 100;

            using CancellationTokenSource preCancelled = new CancellationTokenSource();
            preCancelled.Cancel();

            bool validateCancelled = false;
            try
            {
                await client.ValidateConnectivityAsync(preCancelled.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                validateCancelled = true;
            }
            Assert("ValidateConnectivityAsync propagates cancellation", validateCancelled);

            ChatStreamingResponse streaming = await client.ChatStreamingAsync("stream").ConfigureAwait(false);
            Assert("Local streaming request starts", streaming.Success);

            Stopwatch streamWatch = Stopwatch.StartNew();
            bool streamTimedOut = false;
            int chunks = 0;
            try
            {
                await foreach (ChatStreamingChunk chunk in streaming.Chunks.ConfigureAwait(false))
                {
                    if (!string.IsNullOrEmpty(chunk.Text)) chunks++;
                }
            }
            catch (OperationCanceledException)
            {
                streamTimedOut = true;
            }
            streamWatch.Stop();

            Assert("Streaming body timeout throws", streamTimedOut);
            Assert("Streaming body yielded initial chunk", chunks > 0);
            Assert("Streaming body uses subsecond TimeoutMs", streamWatch.ElapsedMilliseconds < 3000);

            using ProbeOpenAiClient probe = new ProbeOpenAiClient(server.Endpoint, "test-key");
            probe.TimeoutMs = 1000;
            CompletionHttpResult result = await probe.PostProbeAsync().ConfigureAwait(false);
            Assert("Probe PostAndRecordAsync succeeds", result.IsSuccessStatusCode && result.StatusCode == 200);

            bool responseDisposed = result.Response == null;
            if (result.Response != null)
            {
                try
                {
                    await result.Response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    responseDisposed = true;
                }
            }
            Assert("PostAndRecordAsync disposes response content", responseDisposed);

            Console.WriteLine("");
        }

        private sealed class ProbeOpenAiClient : OpenAiClient
        {
            public ProbeOpenAiClient(string endpoint, string apiKey) : base(endpoint, apiKey)
            {
                Model = "test-model";
            }

            public async Task<CompletionHttpResult> PostProbeAsync()
            {
                string url = Endpoint.TrimEnd('/') + "/v1/chat/completions";
                string json = "{\"model\":\"test-model\",\"messages\":[{\"role\":\"user\",\"content\":\"probe\"}],\"max_tokens\":1}";
                using StringContent content = new StringContent(json, Encoding.UTF8, "application/json");
                return await PostAndRecordAsync(url, content, json, CancellationToken.None).ConfigureAwait(false);
            }
        }

        private sealed class LocalOpenAiTestServer : IDisposable
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
                _Loop = Task.Run(() => ListenLoopAsync());
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
                    string path = context.Request.Url?.AbsolutePath ?? "";
                    string requestBody = "";
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
}
