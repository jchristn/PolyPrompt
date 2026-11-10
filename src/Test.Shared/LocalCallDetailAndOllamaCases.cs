namespace Test.Shared
{
    using System.Text.Json;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using Touchstone.Core;

    /// <summary>
    /// Deterministic cases added in 3.7.0: what <see cref="ClientBase.CallDetails"/> records (capture level and credential
    /// redaction), Ollama keep-alive, model load and unload, and tool-support detection from <c>/api/show</c>.
    /// Registered into the local behavior suite.
    /// </summary>
    internal static class LocalCallDetailAndOllamaCases
    {
        public static IEnumerable<TestCaseDescriptor> Create(string suiteId)
        {
            return new List<TestCaseDescriptor>
            {
                Case(suiteId, "calldetail_default_metadata_redacted", "CallDetails default to metadata only with credential headers redacted, while the wire still carries the key", RunDefaultMetadataRedactedAsync),
                Case(suiteId, "calldetail_redaction_rules", "Credential redaction covers auth, key, secret, and token headers and keeps rate-limit and request-id headers", RunRedactionRulesAsync),
                Case(suiteId, "calldetail_full_and_unredacted", "CallDetailCapture.Full records bodies and RedactCredentials = false records raw headers", RunFullUnredactedAsync),
                Case(suiteId, "calldetail_none_and_switching", "CallDetailCapture.None records nothing, and a change applies to later calls only", RunNoneAndSwitchingAsync),
                Case(suiteId, "calldetail_process_metadata", "Metadata capture of an agent harness run drops arguments, stdin, and stdout and keeps exit status", RunProcessMetadataAsync),
                Case(suiteId, "ollama_keep_alive", "Ollama KeepAlive is sent as whole seconds on chat, tool chat, generate, and embed, and omitted when unset", RunKeepAliveAsync),
                Case(suiteId, "ollama_load_unload", "OllamaModelClient.LoadModelAsync and UnloadModelAsync send an empty generate with keep_alive", RunLoadUnloadAsync),
                Case(suiteId, "ollama_show_supports_tools", "Ollama model info copies renderer and parser and sets SupportsTools only when tools are listed and rendered", RunSupportsToolsAsync),
            };
        }

        private static TestCaseDescriptor Case(string suiteId, string caseId, string displayName, Func<CancellationToken, Task> executeAsync)
        {
            return new TestCaseDescriptor(suiteId, caseId, displayName, executeAsync, new[] { "local" });
        }

        private static string? Header(Dictionary<string, string>? headers, string name)
        {
            if (headers == null) return null;
            foreach (KeyValuePair<string, string> pair in headers)
            {
                if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)) return pair.Value;
            }
            return null;
        }

        #region CallDetails

        private static async Task RunDefaultMetadataRedactedAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();

            using OpenAiCompletionClient openAi = new OpenAiCompletionClient(server.Endpoint, "sk-very-secret") { Model = "test-model" };
            SharedAssert.Equal(CallDetailCapture.Metadata, openAi.CallDetailCapture, "Capture defaults to Metadata.");
            SharedAssert.True(openAi.RedactCredentials, "Redaction defaults to on.");

            ChatResponse chat = await openAi.ChatAsync("a private prompt", token: token).ConfigureAwait(false);
            SharedAssert.True(chat.Success, "Chat should succeed: " + chat.Error);

            CallDetail detail = openAi.CallDetails.Single();
            SharedAssert.True(detail.RequestBody == null, "Metadata capture leaves the request body null.");
            SharedAssert.True(detail.ResponseBody == null, "Metadata capture leaves the response body null.");
            SharedAssert.True((detail.Url ?? "").EndsWith("/v1/chat/completions", StringComparison.Ordinal), "The URL is kept: " + detail.Url);
            SharedAssert.Equal("POST", detail.Method, "The method is kept.");
            SharedAssert.Equal<int?>(200, detail.StatusCode, "The status is kept.");
            SharedAssert.True(detail.ResponseTimeMs.HasValue, "The timing is kept.");
            SharedAssert.True(detail.Success, "Success is kept.");
            SharedAssert.Equal("[redacted]", Header(detail.RequestHeaders, "Authorization"), "The bearer token is redacted.");
            SharedAssert.NotNull(Header(detail.RequestHeaders, "Content-Type"), "Ordinary headers are kept.");
            SharedAssert.Equal("Bearer sk-very-secret", Header(server.RequestHeaders[0], "Authorization"), "Redaction does not change what is sent.");

            using AnthropicCompletionClient anthropic = new AnthropicCompletionClient(server.Endpoint, "ant-secret") { Model = "test-model" };
            SharedAssert.True((await anthropic.ChatAsync("hi", token: token).ConfigureAwait(false)).Success, "Anthropic chat should succeed.");
            SharedAssert.Equal("[redacted]", Header(anthropic.CallDetails.Single().RequestHeaders, "x-api-key"), "The Anthropic x-api-key is redacted.");

            using AzureOpenAiCompletionClient azure = new AzureOpenAiCompletionClient(server.Endpoint, "test-deployment", "azure-secret");
            SharedAssert.True((await azure.ChatAsync("hi", token: token).ConfigureAwait(false)).Success, "Azure chat should succeed.");
            SharedAssert.Equal("[redacted]", Header(azure.CallDetails.Single().RequestHeaders, "api-key"), "The Azure api-key is redacted.");
        }

        private static async Task RunRedactionRulesAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using HttpClient http = new HttpClient();
            Dictionary<string, string> sent = new Dictionary<string, string>
            {
                { "Proxy-Authorization", "Basic abc" },
                { "Cookie", "session=abc" },
                { "x-goog-api-key", "goog" },
                { "x-amz-security-token", "amz" },
                { "X-Custom-ApiKey", "custom" },
                { "X-Client-Secret", "secret" },
                { "X-Admin-Password", "pw" },
                { "X-Session-Token", "tok" },
                { "X-Auth-User", "user" },
            };
            foreach (KeyValuePair<string, string> pair in sent) http.DefaultRequestHeaders.TryAddWithoutValidation(pair.Key, pair.Value);
            http.DefaultRequestHeaders.TryAddWithoutValidation("x-ratelimit-remaining-tokens", "5000");
            http.DefaultRequestHeaders.TryAddWithoutValidation("x-request-id", "req-1");

            using OpenAiCompletionClient client = new OpenAiCompletionClient(server.Endpoint, "sk", httpClient: http) { Model = "test-model" };
            SharedAssert.True((await client.ChatAsync("hi", token: token).ConfigureAwait(false)).Success, "Chat should succeed.");
            Dictionary<string, string>? recorded = client.CallDetails.Single().RequestHeaders;

            foreach (string name in sent.Keys)
            {
                SharedAssert.Equal("[redacted]", Header(recorded, name), name + " should be redacted.");
            }
            SharedAssert.Equal("5000", Header(recorded, "x-ratelimit-remaining-tokens"), "Rate-limit headers are not credentials.");
            SharedAssert.Equal("req-1", Header(recorded, "x-request-id"), "Request ids are kept.");
        }

        private static async Task RunFullUnredactedAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiCompletionClient client = new OpenAiCompletionClient(server.Endpoint, "sk-raw") { Model = "test-model" };
            client.CallDetailCapture = CallDetailCapture.Full;

            SharedAssert.True((await client.ChatAsync("full capture prompt", token: token).ConfigureAwait(false)).Success, "Chat should succeed.");
            CallDetail full = client.CallDetails.Single();
            SharedAssert.True((full.RequestBody ?? "").Contains("full capture prompt", StringComparison.Ordinal), "Full capture records the request body.");
            SharedAssert.NotEmpty(full.ResponseBody, "Full capture records the response body.");
            SharedAssert.Equal("[redacted]", Header(full.RequestHeaders, "Authorization"), "Full capture still redacts by default.");

            client.ClearCallDetails();
            client.RedactCredentials = false;
            SharedAssert.True((await client.ChatAsync("hi", token: token).ConfigureAwait(false)).Success, "Chat should succeed.");
            SharedAssert.Equal("Bearer sk-raw", Header(client.CallDetails.Single().RequestHeaders, "Authorization"), "RedactCredentials = false records the raw header.");
        }

        private static async Task RunNoneAndSwitchingAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiCompletionClient client = new OpenAiCompletionClient(server.Endpoint, "sk") { Model = "test-model" };

            client.CallDetailCapture = CallDetailCapture.None;
            SharedAssert.True((await client.ChatAsync("hi", token: token).ConfigureAwait(false)).Success, "Chat should succeed.");
            SharedAssert.Equal(0, client.CallDetails.Count, "None records nothing.");
            SharedAssert.Equal(1, server.RequestBodies.Count, "The request was still sent.");

            client.CallDetailCapture = CallDetailCapture.Metadata;
            SharedAssert.True((await client.ChatAsync("hi", token: token).ConfigureAwait(false)).Success, "Chat should succeed.");
            client.CallDetailCapture = CallDetailCapture.Full;
            SharedAssert.True((await client.ChatAsync("third", token: token).ConfigureAwait(false)).Success, "Chat should succeed.");

            List<CallDetail> details = client.CallDetails;
            SharedAssert.Equal(2, details.Count, "Two calls were recorded after None was lifted.");
            SharedAssert.True(details[0].RequestBody == null, "The Metadata call has no body, even after switching to Full.");
            SharedAssert.True((details[1].RequestBody ?? "").Contains("third", StringComparison.Ordinal), "The Full call has its body.");
        }

        private static async Task RunProcessMetadataAsync(CancellationToken token)
        {
            CliAgentProfile profile = LocalClaudeCodeAgentCases.Profile;
            (FakeCli fake, CliAgentClientBase client) = CliAgentCaseKit.Attach(profile, profile.HappyScenario);
            using (fake)
            using (client)
            {
                client.CallDetailCapture = CallDetailCapture.Metadata;
                client.RedactCredentials = true;
                CliAgentResult result = await client.RunAsync(new CliAgentRequest("a private prompt") { SystemPrompt = "a private system prompt" }, token).ConfigureAwait(false);
                SharedAssert.True(result.Success, "Run should succeed: " + result.Error);

                ProcessCallDetail detail = (ProcessCallDetail)client.CallDetails.Single();
                SharedAssert.Equal(0, detail.Arguments.Count, "Arguments are dropped; they can carry the system prompt.");
                SharedAssert.True(detail.StandardInput == null, "Standard input (the prompt) is dropped.");
                SharedAssert.True(detail.RequestBody == null, "The argument JSON is dropped.");
                SharedAssert.True(detail.ResponseBody == null, "Standard output is dropped.");
                SharedAssert.NotEmpty(detail.Executable, "The executable is kept.");
                SharedAssert.Equal<int?>(0, detail.ExitCode, "The exit code is kept.");
                SharedAssert.True(detail.Success, "Success is kept.");
                SharedAssert.True(detail.ResponseTimeMs.HasValue, "The timing is kept.");
            }
        }

        #endregion

        #region Ollama

        private static async Task RunKeepAliveAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OllamaCompletionClient client = new OllamaCompletionClient(server.Endpoint) { Model = "test-model" };

            SharedAssert.True((await client.ChatAsync("unset", token: token).ConfigureAwait(false)).Success, "Chat should succeed.");

            client.Defaults.KeepAlive = TimeSpan.FromMinutes(10);
            SharedAssert.True((await client.ChatAsync("defaults", token: token).ConfigureAwait(false)).Success, "Chat should succeed.");
            SharedAssert.True((await client.ChatAsync("forever", new OllamaCompletionOptions { KeepAlive = Timeout.InfiniteTimeSpan }, token).ConfigureAwait(false)).Success, "Chat should succeed.");
            SharedAssert.True((await client.GenerateAsync("fraction", new OllamaCompletionOptions { KeepAlive = TimeSpan.FromMilliseconds(1500) }, token).ConfigureAwait(false)).Success, "Generate should succeed.");

            ToolChatRequest request = new ToolChatRequest { Options = new OllamaCompletionOptions { KeepAlive = TimeSpan.Zero } };
            request.Messages.Add(ChatMessage.User("unload after"));
            SharedAssert.True((await client.ToolChatAsync(request, token).ConfigureAwait(false)).Success, "Tool chat should succeed.");

            List<string> bodies = server.RequestBodies;
            SharedAssert.Equal(5, bodies.Count, "Five requests.");
            long?[] expected = { null, 600, -1, 2, 0 };
            for (int i = 0; i < expected.Length; i++)
            {
                using JsonDocument doc = JsonDocument.Parse(bodies[i]);
                bool has = doc.RootElement.TryGetProperty("keep_alive", out JsonElement value);
                if (expected[i] == null)
                {
                    SharedAssert.False(has, "Request " + i + " should send no keep_alive.");
                }
                else
                {
                    SharedAssert.True(has && value.ValueKind == JsonValueKind.Number, "Request " + i + " should send keep_alive as a number: " + bodies[i]);
                    SharedAssert.Equal(expected[i]!.Value, value.GetInt64(), "Request " + i + " keep_alive seconds.");
                }
            }

            using OllamaEmbeddingClient embed = new OllamaEmbeddingClient(server.Endpoint) { Model = "test-model" };
            embed.Defaults.KeepAlive = TimeSpan.FromSeconds(30);
            SharedAssert.True((await embed.EmbedAsync("text", token: token).ConfigureAwait(false)).Success, "Embed should succeed.");
            using JsonDocument embedBody = JsonDocument.Parse(server.RequestBodies.Last());
            SharedAssert.Equal(30L, embedBody.RootElement.GetProperty("keep_alive").GetInt64(), "Embedding keep_alive seconds.");
        }

        private static async Task RunLoadUnloadAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OllamaModelClient client = new OllamaModelClient(server.Endpoint);

            SharedAssert.True(await client.LoadModelAsync("test-model", TimeSpan.FromHours(1), token).ConfigureAwait(false), "Load should succeed.");
            SharedAssert.True(await client.LoadModelAsync("test-model", token: token).ConfigureAwait(false), "Load with the server default should succeed.");
            SharedAssert.True(await client.UnloadModelAsync("test-model", token).ConfigureAwait(false), "Unload should succeed.");
            SharedAssert.False(await client.LoadModelAsync("probe-missing", null, token).ConfigureAwait(false), "Loading a model the server lacks returns false.");

            List<string> urls = server.RequestUrls;
            List<string> bodies = server.RequestBodies;
            SharedAssert.Equal(4, bodies.Count, "Four requests.");
            SharedAssert.True(urls.All(u => u.EndsWith("/api/generate", StringComparison.Ordinal)), "All go to /api/generate.");

            using JsonDocument load = JsonDocument.Parse(bodies[0]);
            SharedAssert.Equal("test-model", load.RootElement.GetProperty("model").GetString(), "Load names the model.");
            SharedAssert.False(load.RootElement.TryGetProperty("prompt", out _), "Load sends no prompt.");
            SharedAssert.False(load.RootElement.GetProperty("stream").GetBoolean(), "Load does not stream.");
            SharedAssert.Equal(3600L, load.RootElement.GetProperty("keep_alive").GetInt64(), "Load keep_alive seconds.");

            using JsonDocument loadDefault = JsonDocument.Parse(bodies[1]);
            SharedAssert.False(loadDefault.RootElement.TryGetProperty("keep_alive", out _), "A null keep-alive sends none.");

            using JsonDocument unload = JsonDocument.Parse(bodies[2]);
            SharedAssert.Equal(0L, unload.RootElement.GetProperty("keep_alive").GetInt64(), "Unload sends keep_alive 0.");

            await SharedAssert.ThrowsAsync<ArgumentNullException>(() => client.LoadModelAsync(" ", null, token), "Load requires a model.");
            await SharedAssert.ThrowsAsync<ArgumentNullException>(() => client.UnloadModelAsync("", token), "Unload requires a model.");
        }

        private static async Task RunSupportsToolsAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OllamaModelClient client = new OllamaModelClient(server.Endpoint);

            ModelInformation renderer = (await client.GetModelInformationAsync("probe-renderer", token).ConfigureAwait(false))!;
            SharedAssert.NotNull(renderer, "Model info should parse.");
            SharedAssert.Equal("qwen3-coder", renderer.Metadata["renderer"], "renderer is copied.");
            SharedAssert.Equal("qwen3-coder", renderer.Metadata["parser"], "parser is copied.");
            SharedAssert.Equal<bool?>(true, renderer.SupportsTools, "A built-in renderer handles tools.");

            Dictionary<string, bool?> expected = new Dictionary<string, bool?>
            {
                { "probe-gotemplate", true },
                { "probe-jinja", true },
                { "probe-notemplate", true },
                { "probe-deepseek", false },
                { "probe-notools", false },
                { "probe-nocaps", null },
            };
            foreach (KeyValuePair<string, bool?> pair in expected)
            {
                ModelInformation? info = await client.GetModelInformationAsync(pair.Key, token).ConfigureAwait(false);
                SharedAssert.NotNull(info, pair.Key + " info should parse.");
                SharedAssert.Equal(pair.Value, info!.SupportsTools, pair.Key + " SupportsTools.");
            }

            ModelInformation deepseek = (await client.GetModelInformationAsync("probe-deepseek", token).ConfigureAwait(false))!;
            SharedAssert.True((deepseek.Metadata["capabilities"] ?? "").Contains("tools", StringComparison.Ordinal), "The raw capabilities stay in Metadata.");
            SharedAssert.False(deepseek.Metadata.ContainsKey("renderer"), "No renderer key when Ollama sends none.");

            using OpenAiModelClient openAi = new OpenAiModelClient(server.Endpoint, "sk");
            ModelInformation? other = await openAi.GetModelInformationAsync("test-model", token).ConfigureAwait(false);
            if (other != null) SharedAssert.True(other.SupportsTools == null, "Other providers leave SupportsTools null.");
        }

        #endregion
    }
}
