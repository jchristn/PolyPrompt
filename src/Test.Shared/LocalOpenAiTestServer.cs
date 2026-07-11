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
        private readonly object _RequestBodiesLock = new object();
        private readonly List<string> _RequestBodies = new List<string>();
        private bool _Disposed = false;

        /// <summary>
        /// Base endpoint URL for the local test server.
        /// </summary>
        public string Endpoint { get; }

        /// <summary>
        /// Detached snapshot of request bodies received by the local test server.
        /// </summary>
        public List<string> RequestBodies
        {
            get
            {
                lock (_RequestBodiesLock)
                {
                    return new List<string>(_RequestBodies);
                }
            }
        }

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

                lock (_RequestBodiesLock)
                {
                    _RequestBodies.Add(requestBody);
                }

                if (path == "/v1/chat/completions")
                {
                    LocalOpenAiChatRequest? request = LocalRequestParser.DeserializeOpenAiChatRequest(requestBody);
                    if (request == null)
                    {
                        await WriteJsonAsync(context, 400, "{\"error\":\"invalid request\"}").ConfigureAwait(false);
                        return;
                    }

                    if (request.Stream == true)
                    {
                        await WriteStreamingChatAsync(context).ConfigureAwait(false);
                    }
                    else if (HasToolDefinitions(request))
                    {
                        await WriteJsonAsync(
                            context,
                            200,
                            "{\"id\":\"chatcmpl-tool-local\",\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[{\"id\":\"call-weather-1\",\"type\":\"function\",\"function\":{\"name\":\"get_weather\",\"arguments\":\"{\\\"city\\\":\\\"Seattle\\\",\\\"unit\\\":\\\"fahrenheit\\\"}\"}}]},\"finish_reason\":\"tool_calls\",\"index\":0}]}").ConfigureAwait(false);
                    }
                    else if (HasToolResultMessage(request))
                    {
                        await WriteJsonAsync(
                            context,
                            200,
                            "{\"id\":\"chatcmpl-final-local\",\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"Seattle is 72 F and clear.\"},\"finish_reason\":\"stop\",\"index\":0}]}").ConfigureAwait(false);
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

                if (path == "/v1/embeddings")
                {
                    LocalEmbeddingRequest? request = LocalRequestParser.DeserializeEmbeddingRequest(requestBody);
                    if (request == null)
                    {
                        await WriteJsonAsync(context, 400, "{\"error\":\"invalid request\"}").ConfigureAwait(false);
                        return;
                    }

                    await WriteJsonAsync(
                        context,
                        200,
                        "{\"data\":[{\"index\":0,\"embedding\":[1.0,2.0,3.0]},{\"index\":1,\"embedding\":[4.0,5.0,6.0]}]}").ConfigureAwait(false);
                    return;
                }

                if (path == "/v1/completions")
                {
                    LocalGenerateRequest? request = LocalRequestParser.DeserializeGenerateRequest(requestBody);
                    if (request == null)
                    {
                        await WriteJsonAsync(context, 400, "{\"error\":\"invalid request\"}").ConfigureAwait(false);
                        return;
                    }

                    await WriteJsonAsync(
                        context,
                        200,
                        "{\"choices\":[{\"text\":\"generated text\"}]}").ConfigureAwait(false);
                    return;
                }

                if (path == "/api/chat")
                {
                    LocalOpenAiChatRequest? request = LocalRequestParser.DeserializeOpenAiChatRequest(requestBody);
                    if (request == null)
                    {
                        await WriteJsonAsync(context, 400, "{\"error\":\"invalid request\"}").ConfigureAwait(false);
                        return;
                    }

                    if (HasToolDefinitions(request))
                    {
                        await WriteJsonAsync(
                            context,
                            200,
                            "{\"model\":\"test-model\",\"message\":{\"role\":\"assistant\",\"content\":\"\",\"tool_calls\":[{\"function\":{\"name\":\"get_weather\",\"arguments\":{\"city\":\"Seattle\",\"unit\":\"fahrenheit\"}}}]},\"done\":true,\"done_reason\":\"tool_calls\"}").ConfigureAwait(false);
                    }
                    else if (HasToolResultMessage(request))
                    {
                        await WriteJsonAsync(
                            context,
                            200,
                            "{\"model\":\"test-model\",\"message\":{\"role\":\"assistant\",\"content\":\"Seattle is 72 F and clear.\"},\"done\":true,\"done_reason\":\"stop\"}").ConfigureAwait(false);
                    }
                    else
                    {
                        await WriteJsonAsync(
                            context,
                            200,
                            "{\"model\":\"test-model\",\"message\":{\"role\":\"assistant\",\"content\":\"pong\"},\"done\":true,\"done_reason\":\"stop\"}").ConfigureAwait(false);
                    }
                    return;
                }

                if (path == "/api/embed")
                {
                    LocalEmbeddingRequest? request = LocalRequestParser.DeserializeEmbeddingRequest(requestBody);
                    if (request == null)
                    {
                        await WriteJsonAsync(context, 400, "{\"error\":\"invalid request\"}").ConfigureAwait(false);
                        return;
                    }

                    await WriteJsonAsync(
                        context,
                        200,
                        "{\"embeddings\":[[1.0,2.0,3.0],[4.0,5.0,6.0]]}").ConfigureAwait(false);
                    return;
                }

                if (path == "/api/generate")
                {
                    LocalGenerateRequest? request = LocalRequestParser.DeserializeGenerateRequest(requestBody);
                    if (request == null)
                    {
                        await WriteJsonAsync(context, 400, "{\"error\":\"invalid request\"}").ConfigureAwait(false);
                        return;
                    }

                    await WriteJsonAsync(
                        context,
                        200,
                        "{\"model\":\"test-model\",\"response\":\"generated text\",\"done\":true}").ConfigureAwait(false);
                    return;
                }

                if (path == "/api/tags")
                {
                    await WriteJsonAsync(
                        context,
                        200,
                        "{\"models\":[{\"name\":\"test-model\",\"model\":\"test-model:latest\",\"modified_at\":\"2026-07-11T00:00:00Z\",\"size\":12345,\"digest\":\"abc123\",\"details\":{\"parameter_size\":\"1B\",\"quantization_level\":\"Q4\",\"family\":\"test\",\"format\":\"gguf\"}}]}").ConfigureAwait(false);
                    return;
                }

                if (path == "/api/show")
                {
                    await WriteJsonAsync(
                        context,
                        200,
                        "{\"modified_at\":\"2026-07-11T00:00:00Z\",\"license\":\"MIT\",\"template\":\"template\",\"parameters\":\"params\",\"capabilities\":[\"completion\",\"tools\"],\"details\":{\"parameter_size\":\"1B\",\"quantization_level\":\"Q4\",\"family\":\"test\",\"format\":\"gguf\",\"families\":[\"test\"],\"parent_model\":\"parent\"}}").ConfigureAwait(false);
                    return;
                }

                if (path == "/api/pull")
                {
                    context.Response.StatusCode = 200;
                    context.Response.ContentType = "application/x-ndjson";
                    context.Response.SendChunked = true;
                    await WriteChunkAsync(context, "{\"status\":\"pulling manifest\"}\n").ConfigureAwait(false);
                    await WriteChunkAsync(context, "{\"status\":\"success\"}\n").ConfigureAwait(false);
                    context.Response.Close();
                    return;
                }

                if (path == "/api/delete")
                {
                    await WriteJsonAsync(context, 200, "{\"status\":\"success\"}").ConfigureAwait(false);
                    return;
                }

                if (path.StartsWith("/v1beta/models/", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                    {
                        await WriteJsonAsync(
                            context,
                            200,
                            "{\"name\":\"models/test-model\",\"displayName\":\"Test Model\",\"description\":\"Local Gemini model\",\"inputTokenLimit\":1000,\"outputTokenLimit\":2000,\"supportedGenerationMethods\":[\"generateContent\",\"embedContent\"]}").ConfigureAwait(false);
                        return;
                    }

                    if (path.EndsWith(":embedContent", StringComparison.OrdinalIgnoreCase))
                    {
                        await WriteJsonAsync(
                            context,
                            200,
                            "{\"embedding\":{\"values\":[1.0,2.0,3.0]}}").ConfigureAwait(false);
                        return;
                    }

                    if (path.EndsWith(":batchEmbedContents", StringComparison.OrdinalIgnoreCase))
                    {
                        await WriteJsonAsync(
                            context,
                            200,
                            "{\"embeddings\":[{\"values\":[1.0,2.0,3.0]},{\"values\":[4.0,5.0,6.0]}]}").ConfigureAwait(false);
                        return;
                    }

                    LocalGeminiRequest? request = LocalRequestParser.DeserializeGeminiRequest(requestBody);
                    if (request == null)
                    {
                        await WriteJsonAsync(context, 400, "{\"error\":\"invalid request\"}").ConfigureAwait(false);
                        return;
                    }

                    if (HasFunctionDeclarations(request))
                    {
                        await WriteJsonAsync(
                            context,
                            200,
                            "{\"responseId\":\"gemini-tool-local\",\"modelVersion\":\"test-model\",\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[{\"functionCall\":{\"name\":\"get_weather\",\"args\":{\"city\":\"Seattle\",\"unit\":\"fahrenheit\"}}}]},\"finishReason\":\"STOP\",\"index\":0}]}").ConfigureAwait(false);
                    }
                    else if (HasFunctionResponse(request))
                    {
                        await WriteJsonAsync(
                            context,
                            200,
                            "{\"responseId\":\"gemini-final-local\",\"modelVersion\":\"test-model\",\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[{\"text\":\"Seattle is 72 F and clear.\"}]},\"finishReason\":\"STOP\",\"index\":0}]}").ConfigureAwait(false);
                    }
                    else
                    {
                        await WriteJsonAsync(
                            context,
                            200,
                            "{\"responseId\":\"gemini-chat-local\",\"modelVersion\":\"test-model\",\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[{\"text\":\"pong\"}]},\"finishReason\":\"STOP\",\"index\":0}]}").ConfigureAwait(false);
                    }
                    return;
                }

                if (path == "/v1beta/models")
                {
                    await WriteJsonAsync(
                        context,
                        200,
                        "{\"models\":[{\"name\":\"models/test-model\",\"displayName\":\"Test Model\",\"description\":\"Local Gemini model\",\"inputTokenLimit\":1000,\"outputTokenLimit\":2000,\"supportedGenerationMethods\":[\"generateContent\",\"embedContent\"]}]}").ConfigureAwait(false);
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

                if (path.StartsWith("/v1/models/", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteJsonAsync(
                        context,
                        200,
                        "{\"id\":\"test-model\",\"object\":\"model\",\"owned_by\":\"local\",\"created\":1783728000}").ConfigureAwait(false);
                    return;
                }

                await WriteJsonAsync(context, 404, "{\"error\":\"not found\"}").ConfigureAwait(false);
            }
            catch
            {
                try { context.Response.Abort(); } catch { }
            }
        }

        private static bool HasToolDefinitions(LocalOpenAiChatRequest request)
        {
            return request.Tools != null
                && request.Tools.Any(tool => tool.Function != null && !string.IsNullOrWhiteSpace(tool.Function.Name));
        }

        private static bool HasToolResultMessage(LocalOpenAiChatRequest request)
        {
            return request.Messages != null
                && request.Messages.Any(message => string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasFunctionDeclarations(LocalGeminiRequest request)
        {
            return request.Tools != null
                && request.Tools.Any(tool => tool.FunctionDeclarations != null
                    && tool.FunctionDeclarations.Any(declaration => !string.IsNullOrWhiteSpace(declaration.Name)));
        }

        private static bool HasFunctionResponse(LocalGeminiRequest request)
        {
            return request.Contents != null
                && request.Contents.Any(content => content.Parts != null
                    && content.Parts.Any(part => part.FunctionResponse != null
                        && !string.IsNullOrWhiteSpace(part.FunctionResponse.Name)));
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

        private static async Task WriteChunkAsync(HttpListenerContext context, string chunk)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(chunk);
            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            await context.Response.OutputStream.FlushAsync().ConfigureAwait(false);
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
