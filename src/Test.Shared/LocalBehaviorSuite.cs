namespace Test.Shared
{
    using System.Diagnostics;
    using System.Text;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using Touchstone.Core;

    public static class LocalBehaviorSuite
    {
        private const string SuiteId = "local_behavior";

        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Local behavior",
                new List<TestCaseDescriptor>
                {
                    Case("chat_and_call_details", "Chat and CallDetails behavior", RunChatAndCallDetailsAsync),
                    Case("timeout_validation", "TimeoutMs validation preserves positive values", RunTimeoutValidationAsync),
                    Case("validate_connectivity_cancellation", "ValidateConnectivityAsync propagates cancellation", RunValidateConnectivityCancellationAsync),
                    Case("streaming_body_timeout", "Streaming timeout covers the response body", RunStreamingBodyTimeoutAsync),
                    Case("post_and_record_disposes_response", "PostAndRecordAsync disposes non-streaming responses", RunPostAndRecordDisposesResponseAsync),
                });
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<CancellationToken, Task> executeAsync)
        {
            return new TestCaseDescriptor(SuiteId, caseId, displayName, executeAsync, new[] { "local" });
        }

        private static async Task RunChatAndCallDetailsAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiClient client = CreateClient(server);
            client.MaxCallDetails = 2;

            ChatResponse first = await client.ChatAsync("first", token: token).ConfigureAwait(false);
            SharedAssert.True(first.Success && first.Text == "pong", "Local ChatAsync should succeed.");

            List<CompletionCallDetail> snapshot = client.CallDetails;
            SharedAssert.Equal(1, snapshot.Count, "CallDetails snapshot should contain the first call.");

            string? originalUrl = snapshot[0].Url;
            snapshot[0].Url = "mutated";
            SharedAssert.Equal(originalUrl, client.CallDetails[0].Url, "CallDetails snapshot should be detached from retained state.");

            await client.ChatAsync("second", token: token).ConfigureAwait(false);
            await client.ChatAsync("third", token: token).ConfigureAwait(false);
            SharedAssert.Equal(2, client.CallDetails.Count, "CallDetails should honor max retention.");

            client.MaxCallDetails = 0;
            await client.ChatAsync("disabled", token: token).ConfigureAwait(false);
            SharedAssert.Equal(0, client.CallDetails.Count, "CallDetails should be disabled when MaxCallDetails is zero.");

            client.MaxCallDetails = 1000;
            await client.ChatAsync("enabled", token: token).ConfigureAwait(false);
            SharedAssert.Equal(1, client.CallDetails.Count, "CallDetails should be re-enabled after MaxCallDetails is raised.");

            client.ClearCallDetails();
            SharedAssert.Equal(0, client.CallDetails.Count, "ClearCallDetails should clear retained entries.");
        }

        private static async Task RunTimeoutValidationAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiClient client = CreateClient(server);

            client.TimeoutMs = 1;
            SharedAssert.Equal(1, client.TimeoutMs, "TimeoutMs should preserve 1ms values.");

            client.TimeoutMs = 999999;
            SharedAssert.Equal(999999, client.TimeoutMs, "TimeoutMs should preserve large positive values.");

            await SharedAssert.ThrowsAsync<ArgumentOutOfRangeException>(
                () =>
                {
                    client.TimeoutMs = 0;
                    return Task.CompletedTask;
                },
                "TimeoutMs should reject zero.").ConfigureAwait(false);

            await SharedAssert.ThrowsAsync<ArgumentOutOfRangeException>(
                () =>
                {
                    client.TimeoutMs = -1;
                    return Task.CompletedTask;
                },
                "TimeoutMs should reject negative values.").ConfigureAwait(false);
        }

        private static async Task RunValidateConnectivityCancellationAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiClient client = CreateClient(server);
            using CancellationTokenSource preCancelled = new CancellationTokenSource();
            preCancelled.Cancel();

            await SharedAssert.ThrowsAsync<OperationCanceledException>(
                () => client.ValidateConnectivityAsync(preCancelled.Token),
                "ValidateConnectivityAsync should propagate cancellation.").ConfigureAwait(false);
        }

        private static async Task RunStreamingBodyTimeoutAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiClient client = CreateClient(server);
            client.TimeoutMs = 100;

            ChatStreamingResponse streaming = await client.ChatStreamingAsync("stream", token: token).ConfigureAwait(false);
            SharedAssert.True(streaming.Success, "Local streaming request should start.");

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

            SharedAssert.True(streamTimedOut, "Streaming body enumeration should time out.");
            SharedAssert.True(chunks > 0, "Streaming body should yield the initial chunk before timing out.");
            SharedAssert.True(streamWatch.ElapsedMilliseconds < 3000, "Streaming timeout should use the subsecond TimeoutMs value.");
        }

        private static async Task RunPostAndRecordDisposesResponseAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using ProbeOpenAiClient probe = new ProbeOpenAiClient(server.Endpoint, "test-key");
            probe.TimeoutMs = 1000;

            CompletionHttpResult result = await probe.PostProbeAsync(token).ConfigureAwait(false);
            SharedAssert.True(result.IsSuccessStatusCode && result.StatusCode == 200, "Probe PostAndRecordAsync should succeed.");

            bool responseDisposed = result.Response == null;
            if (result.Response != null)
            {
                try
                {
                    await result.Response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    responseDisposed = true;
                }
            }

            SharedAssert.True(responseDisposed, "PostAndRecordAsync should dispose the retained response object.");
        }

        private static OpenAiClient CreateClient(LocalOpenAiTestServer server)
        {
            OpenAiClient client = new OpenAiClient(server.Endpoint, "test-key");
            client.Model = "test-model";
            client.TimeoutMs = 1000;
            return client;
        }

        private sealed class ProbeOpenAiClient : OpenAiClient
        {
            public ProbeOpenAiClient(string endpoint, string apiKey) : base(endpoint, apiKey)
            {
                Model = "test-model";
            }

            public async Task<CompletionHttpResult> PostProbeAsync(CancellationToken token)
            {
                string url = Endpoint.TrimEnd('/') + "/v1/chat/completions";
                string json = "{\"model\":\"test-model\",\"messages\":[{\"role\":\"user\",\"content\":\"probe\"}],\"max_tokens\":1}";
                using StringContent content = new StringContent(json, Encoding.UTF8, "application/json");
                return await PostAndRecordAsync(url, content, json, token).ConfigureAwait(false);
            }
        }
    }
}
