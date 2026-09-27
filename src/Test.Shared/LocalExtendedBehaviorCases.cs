namespace Test.Shared
{
    using PolyPrompt.Auth;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using Touchstone.Core;

    /// <summary>
    /// Deterministic local cases for reranking, classification, and sparse embeddings across providers, and
    /// for the Cohere and Text Embeddings Inference clients. Registered into the local behavior suite.
    /// </summary>
    internal static class LocalExtendedBehaviorCases
    {
        private static readonly List<string> _Documents = new List<string>
        {
            "alpha overview",
            "beta is the relevant passage",
            "gamma notes",
            "delta appendix"
        };

        public static IEnumerable<TestCaseDescriptor> Create(string suiteId)
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                // Cross-provider rerank / classify / sparse surface
                Case(suiteId, "rerank_default_not_supported", "Providers without rerank, classify, or sparse embeddings throw without touching the wire", RunDefaultNotSupportedAsync),
                Case(suiteId, "rerank_argument_validation", "Rerank rejects invalid arguments identically on every provider", RunRerankArgumentValidationAsync),
                Case(suiteId, "classify_sparse_embed_argument_validation", "Classify, sparse embed, and embed reject invalid inputs", RunClassifySparseArgumentValidationAsync),
                Case(suiteId, "rerank_result_ordering", "Rerank results are sorted by score and map back to input documents", RunRerankResultOrderingAsync),
                Case(suiteId, "rerank_topn_trimming", "Rerank TopN is sent natively or trimmed client-side", RunRerankTopNTrimmingAsync),
                Case(suiteId, "rerank_out_of_range_and_empty", "Rerank drops unmappable indexes and treats empty results as success", RunRerankOutOfRangeAndEmptyAsync),

                // Cohere
                Case(suiteId, "cohere_chat_translation", "Cohere chat request translation, headers, and usage", RunCohereChatTranslationAsync),
                Case(suiteId, "cohere_chat_streaming", "Cohere streaming chat flow", RunCohereChatStreamingAsync),
                Case(suiteId, "cohere_generation", "Cohere generation maps onto single-turn v2 chat", RunCohereGenerationAsync),
                Case(suiteId, "cohere_tool_chat", "Cohere tool chat flow with tool plan and tool results", RunCohereToolChatAsync),
                Case(suiteId, "cohere_tool_chat_streaming", "Cohere streaming tool chat reassembles split arguments", RunCohereToolChatStreamingAsync),
                Case(suiteId, "cohere_tool_choice_translation", "Cohere tool choice directives are translated", RunCohereToolChoiceTranslationAsync),
                Case(suiteId, "cohere_reasoning_effort", "Reasoning effort maps to Cohere thinking budget", RunCohereReasoningEffortAsync),
                Case(suiteId, "cohere_reasoning_capture", "Cohere thinking is captured separately from answer text", RunCohereReasoningCaptureAsync),
                Case(suiteId, "cohere_usage_billed_fallback", "Cohere usage falls back to billed units", RunCohereUsageBilledFallbackAsync),
                Case(suiteId, "cohere_embedding_translation", "Cohere embedding request translation and parsing", RunCohereEmbeddingTranslationAsync),
                Case(suiteId, "cohere_options_clamping", "Cohere options and client properties clamp and validate", RunCohereOptionsClampingAsync),
                Case(suiteId, "cohere_rerank_translation", "Cohere rerank request translation and metadata", RunCohereRerankTranslationAsync),
                Case(suiteId, "cohere_classify", "Cohere classification with few-shot examples", RunCohereClassifyAsync),
                Case(suiteId, "cohere_models", "Cohere model listing, pagination, lookup, and connectivity", RunCohereModelsAsync),
                Case(suiteId, "cohere_unsupported_operations", "Cohere pull, delete, and sparse embeddings throw", RunCohereUnsupportedOperationsAsync),
                Case(suiteId, "cohere_http_error_handling", "Cohere HTTP errors are surfaced without throwing", RunCohereHttpErrorHandlingAsync),
                Case(suiteId, "cohere_streaming_error_finish", "Cohere streaming ERROR finish reason is surfaced", RunCohereStreamingErrorFinishAsync),
                Case(suiteId, "cohere_tool_arguments_invalid_json", "Cohere malformed tool arguments are passed through without failing", RunCohereToolArgumentsInvalidJsonAsync),
                Case(suiteId, "cohere_streaming_body_timeout", "Cohere streaming timeout covers the response body", RunCohereStreamingBodyTimeoutAsync),
                Case(suiteId, "cohere_cancellation", "Cohere operations respect pre-cancelled tokens", RunCohereCancellationAsync),

                // Text Embeddings Inference
                Case(suiteId, "tei_embedding_translation", "TEI embedding request translation, auth, and parsing", RunTeiEmbeddingTranslationAsync),
                Case(suiteId, "tei_options_clamping", "TEI options normalize and revert invalid values", RunTeiOptionsClampingAsync),
                Case(suiteId, "tei_rerank_translation", "TEI rerank request translation", RunTeiRerankTranslationAsync),
                Case(suiteId, "tei_classify", "TEI classification sends batch form and sorts labels", RunTeiClassifyAsync),
                Case(suiteId, "tei_embed_sparse", "TEI sparse embeddings are translated and parsed", RunTeiEmbedSparseAsync),
                Case(suiteId, "tei_info_and_models", "TEI model listing and lookup come from /info", RunTeiInfoAndModelsAsync),
                Case(suiteId, "tei_validate_connectivity", "TEI connectivity validation uses /health", RunTeiValidateConnectivityAsync),
                Case(suiteId, "tei_unsupported_operations", "TEI chat, tool chat, generation, pull, and delete throw", RunTeiUnsupportedOperationsAsync),
                Case(suiteId, "tei_http_error_handling", "TEI 413/422/424/429 errors are surfaced without throwing", RunTeiHttpErrorHandlingAsync),
                Case(suiteId, "tei_cancellation", "TEI operations respect pre-cancelled tokens", RunTeiCancellationAsync),

                // VoyageAI and Bedrock rerank
                Case(suiteId, "voyageai_rerank_translation", "VoyageAI rerank request translation and parsing", RunVoyageAiRerankTranslationAsync),
                Case(suiteId, "voyageai_rerank_errors_and_cancellation", "VoyageAI rerank errors and cancellation", RunVoyageAiRerankErrorsAsync),
                Case(suiteId, "bedrock_rerank_translation", "Bedrock rerank via InvokeModel is translated and signed", RunBedrockRerankTranslationAsync),
                Case(suiteId, "bedrock_rerank_errors_and_cancellation", "Bedrock rerank errors and cancellation", RunBedrockRerankErrorsAsync),

                // Configuration
                Case(suiteId, "provider_test_configuration_cohere_tei", "Cohere and TEI live-test configuration defaults and environment groups", RunCohereTeiConfigurationAsync),
            };

            return cases;
        }

        private static TestCaseDescriptor Case(string suiteId, string caseId, string displayName, Func<CancellationToken, Task> executeAsync)
        {
            return new TestCaseDescriptor(suiteId, caseId, displayName, executeAsync, new[] { "local" });
        }

        #region Cross-Provider

        private static async Task RunDefaultNotSupportedAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();

            List<CompletionClientBase> noRerank = new List<CompletionClientBase>
            {
                new OpenAiClient(server.Endpoint, "test-key"),
                new OllamaClient(server.Endpoint),
                new GeminiClient(server.Endpoint, "test-key"),
                new AnthropicClient(server.Endpoint, "test-key"),
                new AzureOpenAiClient(server.Endpoint, "test-deployment", "test-key", apiVersion: "2024-10-21"),
                new VertexAiClient("test-project", "us-central1", new StaticTokenCredential("vertex-token"), endpoint: server.Endpoint),
            };

            List<CompletionClientBase> noClassifyOrSparse = new List<CompletionClientBase>(noRerank)
            {
                CreateVoyageAiClient(server),
                CreateBedrockClient(server),
            };

            try
            {
                foreach (CompletionClientBase client in noRerank)
                {
                    string name = client.GetType().Name;
                    await SharedAssert.ThrowsExactAsync<NotSupportedException>(
                        () => client.RerankAsync("query", new List<string> { "doc" }, null, token),
                        name + " RerankAsync should be unsupported.").ConfigureAwait(false);

                    // Unsupported providers reject before validating, so even invalid arguments report NotSupported.
                    await SharedAssert.ThrowsExactAsync<NotSupportedException>(
                        () => client.RerankAsync(null!, null!, null, token),
                        name + " RerankAsync should report NotSupported before argument validation.").ConfigureAwait(false);
                }

                foreach (CompletionClientBase client in noClassifyOrSparse)
                {
                    string name = client.GetType().Name;
                    await SharedAssert.ThrowsExactAsync<NotSupportedException>(
                        () => client.ClassifyAsync(new List<string> { "text" }, null, token),
                        name + " ClassifyAsync (batch) should be unsupported.").ConfigureAwait(false);
                    await SharedAssert.ThrowsExactAsync<NotSupportedException>(
                        () => client.ClassifyAsync("text", null, token),
                        name + " ClassifyAsync (single) should be unsupported.").ConfigureAwait(false);
                    await SharedAssert.ThrowsExactAsync<NotSupportedException>(
                        () => client.EmbedSparseAsync(new List<string> { "text" }, null, token),
                        name + " EmbedSparseAsync (batch) should be unsupported.").ConfigureAwait(false);
                    await SharedAssert.ThrowsExactAsync<NotSupportedException>(
                        () => client.EmbedSparseAsync("text", null, token),
                        name + " EmbedSparseAsync (single) should be unsupported.").ConfigureAwait(false);
                }

                SharedAssert.Equal(0, server.RequestPaths.Count, "Unsupported rerank, classify, and sparse operations should never reach the wire.");
            }
            finally
            {
                foreach (CompletionClientBase client in noClassifyOrSparse) client.Dispose();
            }
        }

        private static async Task RunRerankArgumentValidationAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            await SharedAssert.ThrowsExactAsync<ArgumentOutOfRangeException>(
                () => { RerankOptions options = new RerankOptions(); options.TopN = 0; return Task.CompletedTask; },
                "RerankOptions.TopN = 0 should throw.").ConfigureAwait(false);
            await SharedAssert.ThrowsExactAsync<ArgumentOutOfRangeException>(
                () => { RerankOptions options = new RerankOptions(); options.TopN = -3; return Task.CompletedTask; },
                "RerankOptions.TopN negative should throw.").ConfigureAwait(false);

            RerankOptions nullable = new RerankOptions { TopN = 2 };
            nullable.TopN = null;
            SharedAssert.True(nullable.TopN == null, "RerankOptions.TopN should accept null.");

            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            List<CompletionClientBase> clients = CreateRerankClients(server);

            try
            {
                foreach (CompletionClientBase client in clients)
                {
                    string name = client.GetType().Name;

                    await SharedAssert.ThrowsExactAsync<ArgumentNullException>(
                        () => client.RerankAsync(null!, new List<string> { "doc" }, null, token),
                        name + " should reject a null query.").ConfigureAwait(false);
                    await SharedAssert.ThrowsExactAsync<ArgumentException>(
                        () => client.RerankAsync(string.Empty, new List<string> { "doc" }, null, token),
                        name + " should reject an empty query.").ConfigureAwait(false);
                    await SharedAssert.ThrowsExactAsync<ArgumentException>(
                        () => client.RerankAsync("   ", new List<string> { "doc" }, null, token),
                        name + " should reject a whitespace query.").ConfigureAwait(false);
                    await SharedAssert.ThrowsExactAsync<ArgumentNullException>(
                        () => client.RerankAsync("query", null!, null, token),
                        name + " should reject null documents.").ConfigureAwait(false);
                    await SharedAssert.ThrowsExactAsync<ArgumentException>(
                        () => client.RerankAsync("query", new List<string>(), null, token),
                        name + " should reject an empty document list.").ConfigureAwait(false);
                    await SharedAssert.ThrowsExactAsync<ArgumentException>(
                        () => client.RerankAsync("query", new List<string> { "a", null!, "c" }, null, token),
                        name + " should reject a null document.").ConfigureAwait(false);
                    await SharedAssert.ThrowsExactAsync<ArgumentOutOfRangeException>(
                        () => client.RerankAsync("query", new List<string> { "a", "b" }, new RerankOptions { TopN = 3 }, token),
                        name + " should reject TopN greater than the document count.").ConfigureAwait(false);
                }

                SharedAssert.Equal(0, server.RequestPaths.Count, "Invalid rerank arguments should be rejected before any request is sent.");
            }
            finally
            {
                foreach (CompletionClientBase client in clients) client.Dispose();
            }
        }

        private static async Task RunClassifySparseArgumentValidationAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using CohereClient cohere = CreateCohereClient(server);
            using TeiClient tei = CreateTeiClient(server);

            List<CompletionClientBase> classifiers = new List<CompletionClientBase> { cohere, tei };
            foreach (CompletionClientBase client in classifiers)
            {
                string name = client.GetType().Name;
                await SharedAssert.ThrowsExactAsync<ArgumentNullException>(
                    () => client.ClassifyAsync((List<string>)null!, null, token),
                    name + " ClassifyAsync should reject null inputs.").ConfigureAwait(false);
                await SharedAssert.ThrowsExactAsync<ArgumentException>(
                    () => client.ClassifyAsync(new List<string>(), null, token),
                    name + " ClassifyAsync should reject an empty input list.").ConfigureAwait(false);
                await SharedAssert.ThrowsExactAsync<ArgumentException>(
                    () => client.ClassifyAsync(new List<string> { "ok", null! }, null, token),
                    name + " ClassifyAsync should reject a null input element.").ConfigureAwait(false);
                await SharedAssert.ThrowsExactAsync<ArgumentException>(
                    () => client.ClassifyAsync((string)null!, null, token),
                    name + " ClassifyAsync single should reject a null input.").ConfigureAwait(false);
                await SharedAssert.ThrowsExactAsync<ArgumentException>(
                    () => client.EmbedAsync(new List<string>(), null, token),
                    name + " EmbedAsync should reject an empty input list.").ConfigureAwait(false);
                await SharedAssert.ThrowsExactAsync<ArgumentNullException>(
                    () => client.EmbedAsync((List<string>)null!, null, token),
                    name + " EmbedAsync should reject null inputs.").ConfigureAwait(false);
            }

            await SharedAssert.ThrowsExactAsync<ArgumentNullException>(
                () => tei.EmbedSparseAsync((List<string>)null!, null, token),
                "TEI EmbedSparseAsync should reject null inputs.").ConfigureAwait(false);
            await SharedAssert.ThrowsExactAsync<ArgumentException>(
                () => tei.EmbedSparseAsync(new List<string>(), null, token),
                "TEI EmbedSparseAsync should reject an empty input list.").ConfigureAwait(false);
            await SharedAssert.ThrowsExactAsync<ArgumentException>(
                () => tei.EmbedSparseAsync(new List<string> { null! }, null, token),
                "TEI EmbedSparseAsync should reject a null input element.").ConfigureAwait(false);

            SharedAssert.Equal(0, server.RequestPaths.Count, "Invalid inputs should be rejected before any request is sent.");
        }

        private static async Task RunRerankResultOrderingAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            List<CompletionClientBase> clients = CreateRerankClients(server);

            try
            {
                foreach (CompletionClientBase client in clients)
                {
                    string name = client.GetType().Name;

                    RerankResponse response = await client.RerankAsync("which passage matters?", _Documents, null, token).ConfigureAwait(false);
                    SharedAssert.True(response.Success, name + " rerank should succeed. " + response.Error);
                    SharedAssert.Equal(4, response.Results.Count, name + " rerank should score every document.");
                    SharedAssert.Equal(1, response.Results[0].Index, name + " rerank should rank the relevant document first.");
                    SharedAssert.True(Math.Abs(response.Results[0].Score - 0.95) < 0.0001, name + " rerank should parse the top score.");
                    SharedAssert.Equal(3, response.Results[1].Index, name + " rerank should sort the remaining documents by score.");
                    SharedAssert.Equal(2, response.Results[2].Index, name + " rerank third result should follow score order.");
                    SharedAssert.Equal(0, response.Results[3].Index, name + " rerank lowest-scored document should be last.");

                    for (int i = 1; i < response.Results.Count; i++)
                    {
                        SharedAssert.True(response.Results[i - 1].Score >= response.Results[i].Score, name + " rerank results should be in descending score order.");
                    }

                    SharedAssert.True(response.Results.All(r => r.Document == null), name + " rerank should omit document text unless requested.");
                    SharedAssert.True(response.OverallRuntimeMs >= 0, name + " rerank should record runtime.");

                    RerankResponse withDocs = await client.RerankAsync("which passage matters?", _Documents, new RerankOptions { ReturnDocuments = true }, token).ConfigureAwait(false);
                    SharedAssert.True(withDocs.Success, name + " rerank with documents should succeed.");
                    SharedAssert.True(withDocs.Results.All(r => r.Document == _Documents[r.Index]), name + " rerank should attach the document text that matches each index.");
                }
            }
            finally
            {
                foreach (CompletionClientBase client in clients) client.Dispose();
            }
        }

        private static async Task RunRerankTopNTrimmingAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            List<CompletionClientBase> clients = CreateRerankClients(server);

            try
            {
                foreach (CompletionClientBase client in clients)
                {
                    string name = client.GetType().Name;
                    int before = server.RequestBodies.Count;

                    RerankResponse top2 = await client.RerankAsync("which passage matters?", _Documents, new RerankOptions { TopN = 2 }, token).ConfigureAwait(false);
                    SharedAssert.True(top2.Success, name + " TopN rerank should succeed.");
                    SharedAssert.Equal(2, top2.Results.Count, name + " TopN rerank should return exactly TopN results.");
                    SharedAssert.Equal(1, top2.Results[0].Index, name + " TopN rerank should keep the highest-scored result.");
                    SharedAssert.Equal(3, top2.Results[1].Index, name + " TopN rerank should keep the second-highest result.");

                    LocalJson body = LocalJson.Parse(server.RequestBodies[before]);
                    if (client is TeiClient)
                    {
                        SharedAssert.False(body.Has("top_n") || body.Has("top_k"), "TEI has no native top-N field; it should not be sent.");
                    }
                    else if (client is VoyageAiClient)
                    {
                        SharedAssert.Equal(2, body.Int("top_k"), "VoyageAI should send TopN as top_k.");
                    }
                    else
                    {
                        SharedAssert.Equal(2, body.Int("top_n"), name + " should send TopN as top_n.");
                    }

                    RerankResponse all = await client.RerankAsync("which passage matters?", _Documents, new RerankOptions { TopN = _Documents.Count }, token).ConfigureAwait(false);
                    SharedAssert.True(all.Success, name + " TopN equal to the document count should be accepted.");
                    SharedAssert.Equal(_Documents.Count, all.Results.Count, name + " TopN equal to the document count should return every document.");
                }
            }
            finally
            {
                foreach (CompletionClientBase client in clients) client.Dispose();
            }
        }

        private static async Task RunRerankOutOfRangeAndEmptyAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using CohereClient cohere = CreateCohereClient(server);
            using TeiClient tei = CreateTeiClient(server);

            foreach (CompletionClientBase client in new List<CompletionClientBase> { cohere, tei })
            {
                string name = client.GetType().Name;

                RerankResponse outOfRange = await client.RerankAsync("outofrange", _Documents, null, token).ConfigureAwait(false);
                SharedAssert.True(outOfRange.Success, name + " rerank with an unmappable index should still succeed.");
                SharedAssert.Equal(4, outOfRange.Results.Count, name + " rerank should drop results whose index does not map to a document.");
                SharedAssert.True(outOfRange.Results.All(r => r.Index >= 0 && r.Index < _Documents.Count), name + " rerank results should all map to input documents.");

                RerankResponse empty = await client.RerankAsync("emptyresults", _Documents, null, token).ConfigureAwait(false);
                SharedAssert.True(empty.Success, name + " rerank with no results should be a success.");
                SharedAssert.Equal(0, empty.Results.Count, name + " rerank with no results should return an empty list.");
            }
        }

        #endregion

        #region Cohere

        private static async Task RunCohereChatTranslationAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using CohereClient client = CreateCohereClient(server);

            CohereChatCompletionOptions options = new CohereChatCompletionOptions();
            options.SystemPrompt = "Be brief.";
            options.MaxTokens = 64;
            options.Temperature = 0.5;
            options.TopP = 0.9;
            options.TopK = 40;
            options.Seed = 7;
            options.FrequencyPenalty = 0.3;
            options.PresencePenalty = 0.4;
            options.StopSequences = new List<string> { "a", "b", "c", "d", "e", "f" };

            ChatResponse response = await client.ChatAsync("ping", options, token).ConfigureAwait(false);
            SharedAssert.True(response.Success, "Cohere chat should succeed. " + response.Error);
            SharedAssert.Equal("pong", response.Text, "Cohere chat should parse text content blocks.");
            SharedAssert.True(response.Reasoning == null, "Cohere chat without thinking should leave reasoning null.");
            SharedAssert.Equal("/v2/chat", server.RequestPaths[0], "Cohere chat should POST to /v2/chat.");

            SharedAssert.NotNull(response.Usage, "Cohere chat should parse usage.");
            SharedAssert.Equal(12, response.Usage!.PromptTokens, "Cohere usage should prefer tokens.input_tokens.");
            SharedAssert.Equal(2, response.Usage.CompletionTokens, "Cohere usage should prefer tokens.output_tokens.");
            SharedAssert.Equal(14, response.Usage.TotalTokens, "Cohere usage total should be input plus output.");
            SharedAssert.Equal(0, response.Usage.CachedPromptTokens, "Cohere usage should parse cached_tokens.");
            SharedAssert.True(response.Usage.CacheCreationTokens == null, "Cohere does not report cache writes.");
            SharedAssert.True(response.Usage.ReasoningTokens == null, "Cohere does not report reasoning tokens separately.");

            LocalJson body = LocalJson.Parse(server.RequestBodies[0]);
            SharedAssert.Equal("cohere-test-model", body.Str("model"), "Cohere chat should send the client model.");
            SharedAssert.Equal("system", body.Str("messages.0.role"), "Cohere chat should send the system prompt as a system message.");
            SharedAssert.Equal("Be brief.", body.Str("messages.0.content"), "Cohere system message content should be preserved.");
            SharedAssert.Equal("user", body.Str("messages.1.role"), "Cohere chat should send the prompt as a user message.");
            SharedAssert.Equal("ping", body.Str("messages.1.content"), "Cohere user message content should be preserved.");
            SharedAssert.Equal(64, body.Int("max_tokens"), "Cohere chat should send max_tokens.");
            SharedAssert.True(body.Bool("stream") == false, "Cohere non-streaming chat should send stream false.");
            SharedAssert.Equal(0.5, body.Num("temperature"), "Cohere chat should send temperature.");
            SharedAssert.Equal(0.9, body.Num("p"), "Cohere chat should send TopP as p.");
            SharedAssert.Equal(40, body.Int("k"), "Cohere chat should send TopK as k.");
            SharedAssert.Equal(7, body.Int("seed"), "Cohere chat should send seed.");
            SharedAssert.Equal(0.3, body.Num("frequency_penalty"), "Cohere chat should send frequency_penalty.");
            SharedAssert.Equal(0.4, body.Num("presence_penalty"), "Cohere chat should send presence_penalty.");
            SharedAssert.Equal(5, body.Count("stop_sequences"), "Cohere chat should send at most 5 stop sequences.");
            SharedAssert.False(body.Has("top_p") || body.Has("top_k") || body.Has("thinking") || body.Has("tools"), "Cohere chat should not send non-Cohere or unrequested fields.");

            ChatResponse clamped = await client.ChatAsync("ping", new ChatCompletionOptions { TopP = 1.0 }, token).ConfigureAwait(false);
            SharedAssert.True(clamped.Success, "Cohere chat with TopP 1.0 should succeed.");
            SharedAssert.Equal(0.99, LocalJson.Parse(server.RequestBodies[1]).Num("p"), "Cohere p should be clamped to its 0.99 maximum.");
            SharedAssert.False(LocalJson.Parse(server.RequestBodies[1]).Has("messages.1"), "Cohere chat without a system prompt should send a single user message.");

            List<CompletionCallDetail> details = client.CallDetails;
            SharedAssert.True(details[0].RequestHeaders.TryGetValue("Authorization", out string? auth) && auth == "Bearer " + LocalExtendedRoutes.CohereTestKey,
                "Cohere requests should carry a bearer Authorization header.");
        }

        private static async Task RunCohereChatStreamingAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using CohereClient client = CreateCohereClient(server);

            ChatStreamingResponse streaming = await client.ChatStreamingAsync("ping", token: token).ConfigureAwait(false);
            SharedAssert.True(streaming.Success, "Cohere streaming chat should start.");

            string text = string.Empty;
            await foreach (ChatStreamingChunk chunk in streaming.Chunks.ConfigureAwait(false))
            {
                text += chunk.Text;
            }

            SharedAssert.Equal("pong", text, "Cohere streaming chat should concatenate content deltas in order.");
            SharedAssert.Equal("co-stream", streaming.ResponseId, "Cohere streaming chat should capture the message-start id.");
            SharedAssert.Equal("COMPLETE", streaming.FinishReason, "Cohere streaming chat should capture the message-end finish reason.");
            SharedAssert.True(streaming.ChunkCount >= 2, "Cohere streaming chat should count text chunks.");
            SharedAssert.True(streaming.Reasoning == null, "Cohere streaming chat without thinking should leave reasoning null.");
            SharedAssert.NotNull(streaming.Usage, "Cohere streaming chat should capture usage from message-end.");
            SharedAssert.Equal(12, streaming.Usage!.PromptTokens, "Cohere streaming usage should parse input tokens.");
            SharedAssert.Equal(4, streaming.Usage.CachedPromptTokens, "Cohere streaming usage should parse cached tokens.");
            SharedAssert.True(LocalJson.Parse(server.RequestBodies[0]).Bool("stream") == true, "Cohere streaming chat should send stream true.");
        }

        private static async Task RunCohereGenerationAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using CohereClient client = CreateCohereClient(server);
            client.SystemPrompt = "This system prompt applies to chat only.";

            CohereGenerationOptions options = new CohereGenerationOptions();
            options.Model = "command-r7b-12-2024";
            options.TopK = 5;
            options.MaxTokens = 32;

            GenerationResponse response = await client.GenerateAsync("complete this", options, token).ConfigureAwait(false);
            SharedAssert.True(response.Success, "Cohere generation should succeed.");
            SharedAssert.Equal("pong", response.Text, "Cohere generation should parse text.");
            SharedAssert.Equal("command-r7b-12-2024", response.Model, "Cohere generation should report the override model.");

            LocalJson body = LocalJson.Parse(server.RequestBodies[0]);
            SharedAssert.Equal("/v2/chat", server.RequestPaths[0], "Cohere generation should POST to /v2/chat.");
            SharedAssert.Equal("command-r7b-12-2024", body.Str("model"), "Cohere generation should send the override model.");
            SharedAssert.Equal(1, body.Count("messages"), "Cohere generation should send a single message (no system prompt).");
            SharedAssert.Equal("user", body.Str("messages.0.role"), "Cohere generation should send a user turn.");
            SharedAssert.Equal(5, body.Int("k"), "Cohere generation should send TopK as k.");
            SharedAssert.Equal(32, body.Int("max_tokens"), "Cohere generation should send max_tokens.");

            GenerationStreamingResponse streaming = await client.GenerateStreamingAsync("reasoncapture complete this", token: token).ConfigureAwait(false);
            SharedAssert.True(streaming.Success, "Cohere streaming generation should start.");

            string text = string.Empty;
            bool sawDone = false;
            await foreach (GenerationStreamingChunk chunk in streaming.Chunks.ConfigureAwait(false))
            {
                text += chunk.Text;
                if (chunk.Done) sawDone = true;
            }

            SharedAssert.Equal("pong", text, "Cohere streaming generation should stream only answer text, never thinking.");
            SharedAssert.True(sawDone, "Cohere streaming generation should emit a final done chunk.");
            SharedAssert.Equal("cohere-test-model", LocalJson.Parse(server.RequestBodies[1]).Str("model"), "Cohere streaming generation should fall back to the client model.");
        }

        private static async Task RunCohereToolChatAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using CohereClient client = CreateCohereClient(server);

            ToolChatRequest request = CreateWeatherToolRequest();
            ToolChatResponse response = await client.ToolChatAsync(request, token).ConfigureAwait(false);

            SharedAssert.True(response.Success, "Cohere tool chat should succeed. " + response.Error);
            SharedAssert.Equal(1, response.ToolCalls.Count, "Cohere tool chat should parse one tool call.");
            SharedAssert.Equal("call-weather-1", response.ToolCalls[0].Id, "Cohere tool call id should parse.");
            SharedAssert.Equal("get_weather", response.ToolCalls[0].Name, "Cohere tool call name should parse.");
            SharedAssert.True(response.ToolCalls[0].ArgumentsJson.Contains("Seattle"), "Cohere tool call arguments should parse.");
            SharedAssert.Equal("I will look up the weather.", response.Reasoning, "Cohere tool plan should surface as reasoning.");
            SharedAssert.True(response.Text == null, "Cohere tool plan should not leak into answer text.");
            SharedAssert.Equal("TOOL_CALL", response.FinishReason, "Cohere tool chat should expose the finish reason.");
            SharedAssert.Equal("co-tool", response.ResponseId, "Cohere tool chat should expose the response id.");
            SharedAssert.Equal(40, response.Usage?.PromptTokens, "Cohere tool chat usage should parse input tokens.");
            SharedAssert.Equal(8, response.Usage?.CachedPromptTokens, "Cohere tool chat usage should parse cached tokens.");

            LocalJson first = LocalJson.Parse(server.RequestBodies[0]);
            SharedAssert.Equal("system", first.Str("messages.0.role"), "Cohere tool chat should keep the system message.");
            SharedAssert.Equal("function", first.Str("tools.0.type"), "Cohere tools should be function tools.");
            SharedAssert.Equal("get_weather", first.Str("tools.0.function.name"), "Cohere tool name should be sent.");
            SharedAssert.Equal("object", first.Str("tools.0.function.parameters.type"), "Cohere tool parameters should be sent as JSON Schema.");
            SharedAssert.False(first.Has("tool_choice"), "Cohere tool chat with auto tool choice should omit tool_choice.");
            SharedAssert.False(first.Has("thinking"), "Cohere tool chat without reasoning effort should omit thinking.");

            request.Messages.Add(response.ToAssistantMessage());
            request.Messages.Add(ChatMessage.ToolResult("call-weather-1", "get_weather", "{\"temperature\":72,\"conditions\":\"clear\"}"));

            ToolChatResponse final = await client.ToolChatAsync(request, token).ConfigureAwait(false);
            SharedAssert.True(final.Success, "Cohere follow-up tool chat should succeed.");
            SharedAssert.Equal("Seattle is 72 F and clear.", final.Text, "Cohere follow-up tool chat should return the final answer.");
            SharedAssert.Equal(0, final.ToolCalls.Count, "Cohere follow-up tool chat should not request more tools.");

            LocalJson second = LocalJson.Parse(server.RequestBodies[1]);
            SharedAssert.Equal("assistant", second.Str("messages.2.role"), "Cohere follow-up should replay the assistant turn.");
            SharedAssert.Equal("call-weather-1", second.Str("messages.2.tool_calls.0.id"), "Cohere follow-up should replay the tool call id.");
            SharedAssert.Equal("get_weather", second.Str("messages.2.tool_calls.0.function.name"), "Cohere follow-up should replay the tool call name.");
            SharedAssert.Equal("String", second.Kind("messages.2.tool_calls.0.function.arguments"), "Cohere tool call arguments should be sent as a JSON string.");
            SharedAssert.False(second.Has("messages.2.tool_plan"), "Cohere tool plan (reasoning) should never be resent.");
            SharedAssert.False(second.Str("messages.2.content")?.Contains("look up") == true, "Cohere tool plan should not be resent as content.");
            SharedAssert.Equal("tool", second.Str("messages.3.role"), "Cohere tool results should be tool-role messages.");
            SharedAssert.Equal("call-weather-1", second.Str("messages.3.tool_call_id"), "Cohere tool results should reference the tool call id.");
            SharedAssert.True(second.Str("messages.3.content")?.Contains("72") == true, "Cohere tool result content should be sent.");
        }

        private static async Task RunCohereToolChatStreamingAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using CohereClient client = CreateCohereClient(server);

            ToolChatStreamingResponse streaming = await client.ToolChatStreamingAsync(CreateWeatherToolRequest(), token).ConfigureAwait(false);
            SharedAssert.True(streaming.Success, "Cohere streaming tool chat should start.");

            int deltaChunks = 0;
            await foreach (ToolChatStreamingChunk chunk in streaming.Chunks.ConfigureAwait(false))
            {
                if (chunk.ToolCallDeltas.Count > 0) deltaChunks++;
            }

            SharedAssert.Equal(3, deltaChunks, "Cohere streaming tool chat should surface the start and both argument deltas.");
            SharedAssert.Equal(1, streaming.ToolCalls.Count, "Cohere streaming tool chat should assemble one tool call.");
            SharedAssert.Equal("call-weather-1", streaming.ToolCalls[0].Id, "Cohere streamed tool call id should be captured from tool-call-start.");
            SharedAssert.Equal("get_weather", streaming.ToolCalls[0].Name, "Cohere streamed tool call name should be captured.");
            SharedAssert.Equal("{\"city\":\"Seattle\"}", streaming.ToolCalls[0].ArgumentsJson, "Cohere split tool arguments should be reassembled in order.");
            SharedAssert.Equal("I will check.", streaming.Reasoning, "Cohere streamed tool plan should accumulate as reasoning.");
            SharedAssert.True(string.IsNullOrEmpty(streaming.Text), "Cohere streamed tool plan should not leak into answer text.");
            SharedAssert.Equal("TOOL_CALL", streaming.FinishReason, "Cohere streaming tool chat should capture the finish reason.");
            SharedAssert.Equal(40, streaming.Usage?.PromptTokens, "Cohere streaming tool chat should capture usage.");
            SharedAssert.True(LocalJson.Parse(server.RequestBodies[0]).Bool("stream") == true, "Cohere streaming tool chat should send stream true.");
        }

        private static async Task RunCohereToolChoiceTranslationAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using CohereClient client = CreateCohereClient(server);

            async Task<LocalJson> SendAsync(string? toolChoice)
            {
                ToolChatRequest request = CreateWeatherToolRequest();
                request.Tools.Add(ToolDefinition.Function("get_time", "Get the time.", new Dictionary<string, object> { { "type", "object" } }));
                request.ToolChoice = toolChoice;
                int before = server.RequestBodies.Count;
                ToolChatResponse response = await client.ToolChatAsync(request, token).ConfigureAwait(false);
                SharedAssert.True(response.Success, "Cohere tool chat with tool choice '" + toolChoice + "' should succeed.");
                return LocalJson.Parse(server.RequestBodies[before]);
            }

            LocalJson auto = await SendAsync("auto").ConfigureAwait(false);
            SharedAssert.False(auto.Has("tool_choice"), "Cohere auto tool choice should omit tool_choice.");
            SharedAssert.Equal(2, auto.Count("tools"), "Cohere auto tool choice should send every tool.");

            LocalJson nullChoice = await SendAsync(null).ConfigureAwait(false);
            SharedAssert.False(nullChoice.Has("tool_choice"), "Cohere null tool choice should omit tool_choice.");

            LocalJson required = await SendAsync("required").ConfigureAwait(false);
            SharedAssert.Equal("REQUIRED", required.Str("tool_choice"), "Cohere required tool choice should send REQUIRED.");

            LocalJson any = await SendAsync("ANY").ConfigureAwait(false);
            SharedAssert.Equal("REQUIRED", any.Str("tool_choice"), "Cohere any tool choice should send REQUIRED.");

            LocalJson none = await SendAsync("none").ConfigureAwait(false);
            SharedAssert.Equal("NONE", none.Str("tool_choice"), "Cohere none tool choice should send NONE.");
            SharedAssert.Equal(2, none.Count("tools"), "Cohere none tool choice should still declare the tools.");

            LocalJson named = await SendAsync("get_weather").ConfigureAwait(false);
            SharedAssert.Equal("REQUIRED", named.Str("tool_choice"), "Cohere named tool choice should require a tool call.");
            SharedAssert.Equal(1, named.Count("tools"), "Cohere named tool choice should send only the named tool.");
            SharedAssert.Equal("get_weather", named.Str("tools.0.function.name"), "Cohere named tool choice should keep the named tool.");

            LocalJson unknown = await SendAsync("no_such_tool").ConfigureAwait(false);
            SharedAssert.Equal("REQUIRED", unknown.Str("tool_choice"), "Cohere unknown named tool choice should still require a tool call.");
            SharedAssert.Equal(2, unknown.Count("tools"), "Cohere unknown named tool choice should fall back to every tool.");
        }

        private static async Task RunCohereReasoningEffortAsync(CancellationToken token)
        {
            SharedAssert.Equal(0, ReasoningEffort.Minimal.ToCohereThinkingBudget(), "Minimal should map to a disabled Cohere thinking budget.");
            SharedAssert.Equal(1024, ReasoningEffort.Low.ToCohereThinkingBudget(), "Low should map to 1024.");
            SharedAssert.Equal(4096, ReasoningEffort.Medium.ToCohereThinkingBudget(), "Medium should map to 4096.");
            SharedAssert.Equal(16384, ReasoningEffort.High.ToCohereThinkingBudget(), "High should map to 16384.");

            ReasoningEffort overridden = ReasoningEffort.Low;
            overridden.CohereThinkingBudget = 2000;
            SharedAssert.Equal(2000, overridden.ToCohereThinkingBudget(), "The Cohere budget override should win over the level.");
            overridden.CohereThinkingBudget = 999999;
            SharedAssert.Equal(32768, overridden.CohereThinkingBudget, "The Cohere budget override should clamp to 32768.");
            overridden.CohereThinkingBudget = -5;
            SharedAssert.Equal(0, overridden.CohereThinkingBudget, "The Cohere budget override should clamp to 0.");
            overridden.CohereThinkingBudget = null;
            SharedAssert.Equal(1024, overridden.ToCohereThinkingBudget(), "Clearing the Cohere override should fall back to the level.");

            await SharedAssert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => { new ReasoningEffort((ReasoningEffortLevel)99).ToCohereThinkingBudget(); return Task.CompletedTask; },
                "An undefined level should throw from the Cohere projection.").ConfigureAwait(false);

            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using CohereClient client = CreateCohereClient(server);

            ToolChatRequest high = CreateWeatherToolRequest();
            high.ReasoningEffort = ReasoningEffort.High;
            await client.ToolChatAsync(high, token).ConfigureAwait(false);
            LocalJson highBody = LocalJson.Parse(server.RequestBodies[0]);
            SharedAssert.Equal("enabled", highBody.Str("thinking.type"), "High effort should enable Cohere thinking.");
            SharedAssert.Equal(16384, highBody.Int("thinking.token_budget"), "High effort should send the 16384 token budget.");

            ToolChatRequest minimal = CreateWeatherToolRequest();
            minimal.ReasoningEffort = ReasoningEffort.Minimal;
            await client.ToolChatAsync(minimal, token).ConfigureAwait(false);
            LocalJson minimalBody = LocalJson.Parse(server.RequestBodies[1]);
            SharedAssert.Equal("disabled", minimalBody.Str("thinking.type"), "Minimal effort should disable Cohere thinking.");
            SharedAssert.False(minimalBody.Has("thinking.token_budget"), "Disabled Cohere thinking should not send a token budget.");

            client.ReasoningEffort = ReasoningEffort.Low;
            await client.ToolChatStreamingAsync(CreateWeatherToolRequest(), token).ConfigureAwait(false);
            LocalJson streamingBody = LocalJson.Parse(server.RequestBodies[2]);
            SharedAssert.Equal(1024, streamingBody.Int("thinking.token_budget"), "The client default reasoning effort should apply to streaming tool chat.");
        }

        private static async Task RunCohereReasoningCaptureAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using CohereClient client = CreateCohereClient(server);

            ChatResponse chat = await client.ChatAsync("reasoncapture please", token: token).ConfigureAwait(false);
            SharedAssert.True(chat.Success, "Cohere chat with thinking should succeed.");
            SharedAssert.Equal("pong", chat.Text, "Cohere thinking should not leak into answer text.");
            SharedAssert.Equal("Let me think.", chat.Reasoning, "Cohere thinking content blocks should surface as reasoning.");

            ChatStreamingResponse streaming = await client.ChatStreamingAsync("reasoncapture please", token: token).ConfigureAwait(false);
            string text = string.Empty;
            await foreach (ChatStreamingChunk chunk in streaming.Chunks.ConfigureAwait(false))
            {
                text += chunk.Text;
            }

            SharedAssert.Equal("pong", text, "Cohere streamed thinking should not leak into answer text.");
            SharedAssert.Equal("Let me think.", streaming.Reasoning, "Cohere streamed thinking deltas should accumulate as reasoning.");
        }

        private static async Task RunCohereUsageBilledFallbackAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using CohereClient client = CreateCohereClient(server);

            ChatResponse response = await client.ChatAsync("billedonly", token: token).ConfigureAwait(false);
            SharedAssert.True(response.Success, "Cohere chat with billed-only usage should succeed.");
            SharedAssert.Equal(5, response.Usage?.PromptTokens, "Cohere usage should fall back to billed input units (sent as 5.0).");
            SharedAssert.Equal(6, response.Usage?.CompletionTokens, "Cohere usage should fall back to billed output units (sent as 6.0).");
            SharedAssert.True(response.Usage?.CachedPromptTokens == null, "Cohere cached tokens should be null when not reported.");
        }

        private static async Task RunCohereEmbeddingTranslationAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using CohereClient client = CreateCohereClient(server);

            EmbeddingResponse batch = await client.EmbedAsync(new List<string> { "first", "second" }, null, token).ConfigureAwait(false);
            SharedAssert.True(batch.Success, "Cohere batch embedding should succeed. " + batch.Error);
            SharedAssert.Equal("embed-v4.0", batch.Model, "Cohere embeddings should default to the embedding model, not the chat model.");
            SharedAssert.Equal(2, batch.Embeddings.Count, "Cohere batch embedding should parse both vectors.");
            SharedAssert.Equal(1, batch.Embeddings[1].Index, "Cohere embedding index should follow input order.");
            SharedAssert.Equal(4f, batch.Embeddings[1].Embedding[0], "Cohere float embedding values should parse.");

            LocalJson body = LocalJson.Parse(server.RequestBodies[0]);
            SharedAssert.Equal("/v2/embed", server.RequestPaths[0], "Cohere embeddings should POST to /v2/embed.");
            SharedAssert.Equal("embed-v4.0", body.Str("model"), "Cohere embeddings should send the embedding model.");
            SharedAssert.Equal(2, body.Count("texts"), "Cohere embeddings should send inputs as texts.");
            SharedAssert.Equal("search_document", body.Str("input_type"), "Cohere embeddings should default input_type to search_document.");
            SharedAssert.Equal("float", body.Str("embedding_types.0"), "Cohere embeddings should request float vectors by default.");
            SharedAssert.False(body.Has("output_dimension") || body.Has("truncate"), "Cohere embeddings should omit unset optional fields.");

            CohereEmbeddingOptions options = new CohereEmbeddingOptions();
            options.Model = "embed-english-v3.0";
            options.InputType = "search_query";
            options.EmbeddingType = "int8";
            options.OutputDimension = 512;
            options.Truncate = "start";

            EmbeddingResponse quantized = await client.EmbedAsync("only one", options, token).ConfigureAwait(false);
            SharedAssert.True(quantized.Success, "Cohere int8 embedding should succeed.");
            SharedAssert.Equal(1, quantized.Embeddings.Count, "Cohere single embedding should return one vector.");
            SharedAssert.Equal(10f, quantized.Embeddings[0].Embedding[0], "Cohere int8 vectors should parse from the int8 key.");

            LocalJson optionBody = LocalJson.Parse(server.RequestBodies[1]);
            SharedAssert.Equal("embed-english-v3.0", optionBody.Str("model"), "Cohere embeddings should honor the model override.");
            SharedAssert.Equal(1, optionBody.Count("texts"), "Cohere single embedding should send one text.");
            SharedAssert.Equal("search_query", optionBody.Str("input_type"), "Cohere embeddings should send input_type.");
            SharedAssert.Equal("int8", optionBody.Str("embedding_types.0"), "Cohere embeddings should send embedding_types.");
            SharedAssert.Equal(512, optionBody.Int("output_dimension"), "Cohere embeddings should send output_dimension.");
            SharedAssert.Equal("START", optionBody.Str("truncate"), "Cohere embeddings should send the normalized truncate value.");

            client.EmbeddingModel = "embed-multilingual-v3.0";
            await client.EmbedAsync("again", null, token).ConfigureAwait(false);
            SharedAssert.Equal("embed-multilingual-v3.0", LocalJson.Parse(server.RequestBodies[2]).Str("model"), "Cohere EmbeddingModel should be used when no override is given.");
        }

        private static async Task RunCohereOptionsClampingAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            CohereEmbeddingOptions embed = new CohereEmbeddingOptions();
            embed.InputType = " SEARCH_QUERY ";
            SharedAssert.Equal("search_query", embed.InputType, "Cohere input type should normalize case and whitespace.");
            embed.InputType = "image";
            SharedAssert.True(embed.InputType == null, "The image input type is not supported for text embeddings and should revert to null.");
            embed.InputType = "bogus";
            SharedAssert.True(embed.InputType == null, "An unrecognized Cohere input type should revert to null.");
            embed.EmbeddingType = "Int8";
            SharedAssert.Equal("int8", embed.EmbeddingType, "Cohere embedding type should normalize case.");
            embed.EmbeddingType = "base64";
            SharedAssert.True(embed.EmbeddingType == null, "base64 embeddings are not parseable and should revert to null.");
            embed.OutputDimension = 1536;
            SharedAssert.Equal(1536, embed.OutputDimension, "Cohere output dimension should accept documented values.");
            embed.OutputDimension = 2048;
            SharedAssert.True(embed.OutputDimension == null, "An undocumented Cohere output dimension should revert to null.");
            embed.Truncate = "end";
            SharedAssert.Equal("END", embed.Truncate, "Cohere truncate should normalize to upper case.");
            embed.Truncate = "middle";
            SharedAssert.True(embed.Truncate == null, "An unrecognized Cohere truncate value should revert to null.");

            CohereChatCompletionOptions chat = new CohereChatCompletionOptions();
            chat.TopK = 900;
            SharedAssert.Equal(500, chat.TopK, "Cohere TopK should clamp to 500.");
            chat.TopK = -1;
            SharedAssert.Equal(0, chat.TopK, "Cohere TopK should clamp to 0.");
            chat.FrequencyPenalty = 2.0;
            SharedAssert.Equal(1.0, chat.FrequencyPenalty, "Cohere frequency penalty should clamp to 1.0.");
            chat.PresencePenalty = -1.0;
            SharedAssert.Equal(0.0, chat.PresencePenalty, "Cohere presence penalty should clamp to 0.0.");
            chat.StopSequences = new List<string> { "1", "2", "3", "4", "5", "6", "7" };
            SharedAssert.Equal(5, chat.StopSequences!.Count, "Cohere stop sequences should keep at most 5.");
            chat.StopSequences = null;
            SharedAssert.True(chat.StopSequences == null, "Cohere stop sequences should accept null.");

            CohereGenerationOptions generation = new CohereGenerationOptions();
            generation.TopK = 501;
            SharedAssert.Equal(500, generation.TopK, "Cohere generation TopK should clamp to 500.");

            CohereRerankOptions rerank = new CohereRerankOptions();
            rerank.MaxTokensPerDoc = 0;
            SharedAssert.Equal(1, rerank.MaxTokensPerDoc, "Cohere max tokens per document should clamp to 1.");

            CohereClassificationOptions classify = new CohereClassificationOptions();
            classify.Truncate = " none ";
            SharedAssert.Equal("NONE", classify.Truncate, "Cohere classify truncate should normalize.");
            classify.Examples = null!;
            SharedAssert.True(classify.Examples != null && classify.Examples.Count == 0, "Setting Cohere examples to null should clear the list.");

            await SharedAssert.ThrowsExactAsync<ArgumentNullException>(
                () => { new ClassificationExample(null!, "label"); return Task.CompletedTask; },
                "A null example text should throw.").ConfigureAwait(false);
            await SharedAssert.ThrowsExactAsync<ArgumentNullException>(
                () => { new ClassificationExample("text", null!); return Task.CompletedTask; },
                "A null example label should throw.").ConfigureAwait(false);

            using CohereClient client = new CohereClient("http://127.0.0.1:1", "key");
            SharedAssert.Equal("command-a-03-2025", client.Model, "Cohere should default the chat model.");
            SharedAssert.Equal("embed-v4.0", client.EmbeddingModel, "Cohere should default the embedding model.");
            SharedAssert.Equal("rerank-v3.5", client.RerankModel, "Cohere should default the rerank model.");
            SharedAssert.True(client.ClassificationModel == null, "Cohere should default the classification model to null.");
            SharedAssert.Equal("https://api.cohere.com", new CohereClient().Endpoint, "Cohere should default its endpoint.");

            await SharedAssert.ThrowsExactAsync<ArgumentNullException>(
                () => { client.EmbeddingModel = " "; return Task.CompletedTask; },
                "A blank Cohere embedding model should throw.").ConfigureAwait(false);
            await SharedAssert.ThrowsExactAsync<ArgumentNullException>(
                () => { client.RerankModel = null!; return Task.CompletedTask; },
                "A null Cohere rerank model should throw.").ConfigureAwait(false);

            client.ClassificationModel = "  ";
            SharedAssert.True(client.ClassificationModel == null, "A blank Cohere classification model should normalize to null.");
            client.ModelsPageSize = 0;
            SharedAssert.Equal(1, client.ModelsPageSize, "Cohere models page size should clamp to 1.");
            client.ModelsPageSize = 5000;
            SharedAssert.Equal(1000, client.ModelsPageSize, "Cohere models page size should clamp to 1000.");
        }

        private static async Task RunCohereRerankTranslationAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using CohereClient client = CreateCohereClient(server);

            CohereRerankOptions options = new CohereRerankOptions { TopN = 3, MaxTokensPerDoc = 256 };
            RerankResponse response = await client.RerankAsync("which passage matters?", _Documents, options, token).ConfigureAwait(false);

            SharedAssert.True(response.Success, "Cohere rerank should succeed. " + response.Error);
            SharedAssert.Equal(200, response.StatusCode, "Cohere rerank should report the status code.");
            SharedAssert.Equal("rerank-v3.5", response.Model, "Cohere rerank should default to the rerank model.");
            SharedAssert.Equal("co-rerank", response.ResponseId, "Cohere rerank should expose the response id.");
            SharedAssert.Equal(1, response.SearchUnits, "Cohere rerank should expose billed search units.");
            SharedAssert.Equal(42, response.TotalTokens, "Cohere rerank should expose input tokens.");
            SharedAssert.Equal(3, response.Results.Count, "Cohere rerank should honor TopN.");

            LocalJson body = LocalJson.Parse(server.RequestBodies[0]);
            SharedAssert.Equal("/v2/rerank", server.RequestPaths[0], "Cohere rerank should POST to /v2/rerank.");
            SharedAssert.Equal("rerank-v3.5", body.Str("model"), "Cohere rerank should send the rerank model.");
            SharedAssert.Equal("which passage matters?", body.Str("query"), "Cohere rerank should send the query.");
            SharedAssert.Equal(4, body.Count("documents"), "Cohere rerank should send every document.");
            SharedAssert.Equal("String", body.Kind("documents.0"), "Cohere rerank documents should be plain strings.");
            SharedAssert.Equal(3, body.Int("top_n"), "Cohere rerank should send top_n.");
            SharedAssert.Equal(256, body.Int("max_tokens_per_doc"), "Cohere rerank should send max_tokens_per_doc.");
            SharedAssert.False(body.Has("return_documents"), "Cohere v2 rerank has no return_documents field; documents are attached client-side.");

            await client.RerankAsync("q", _Documents, new RerankOptions { Model = "rerank-v4.0-fast" }, token).ConfigureAwait(false);
            SharedAssert.Equal("rerank-v4.0-fast", LocalJson.Parse(server.RequestBodies[1]).Str("model"), "Cohere rerank should honor the model override.");
            SharedAssert.False(LocalJson.Parse(server.RequestBodies[1]).Has("top_n"), "Cohere rerank should omit top_n when unset.");
        }

        private static async Task RunCohereClassifyAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using CohereClient client = CreateCohereClient(server);

            CohereClassificationOptions options = new CohereClassificationOptions();
            options.Examples.Add(new ClassificationExample("I love it", "positive"));
            options.Examples.Add(new ClassificationExample("Wonderful", "positive"));
            options.Examples.Add(new ClassificationExample("I hate it", "negative"));
            options.Examples.Add(new ClassificationExample("Terrible", "negative"));
            options.Truncate = "END";

            ClassificationResponse response = await client.ClassifyAsync(new List<string> { "this is great", "this is bad" }, options, token).ConfigureAwait(false);
            SharedAssert.True(response.Success, "Cohere classification should succeed. " + response.Error);
            SharedAssert.True(response.Model == null, "Cohere classification without a model should report a null model.");
            SharedAssert.Equal(2, response.Classifications.Count, "Cohere classification should return one result per input.");

            ClassificationResult first = response.Classifications[0];
            SharedAssert.Equal(0, first.Index, "Cohere classification index should follow input order.");
            SharedAssert.Equal("this is great", first.Input, "Cohere classification should echo the input.");
            SharedAssert.Equal("positive", first.Label, "Cohere classification should pick the highest-confidence label.");
            SharedAssert.Equal(0.9, first.Score, "Cohere classification should expose the top confidence.");
            SharedAssert.Equal(2, first.Labels.Count, "Cohere classification should expose every label.");
            SharedAssert.Equal("positive", first.Labels[0].Label, "Cohere classification labels should be sorted by confidence.");
            SharedAssert.Equal("negative", response.Classifications[1].Label, "Cohere second classification should parse.");

            LocalJson body = LocalJson.Parse(server.RequestBodies[0]);
            SharedAssert.Equal("/v1/classify", server.RequestPaths[0], "Cohere classification should POST to /v1/classify.");
            SharedAssert.Equal(2, body.Count("inputs"), "Cohere classification should send inputs.");
            SharedAssert.Equal(4, body.Count("examples"), "Cohere classification should send examples.");
            SharedAssert.Equal("I love it", body.Str("examples.0.text"), "Cohere example text should be sent.");
            SharedAssert.Equal("positive", body.Str("examples.0.label"), "Cohere example label should be sent.");
            SharedAssert.Equal("END", body.Str("truncate"), "Cohere classification should send truncate.");
            SharedAssert.False(body.Has("model"), "Cohere classification should omit the model when none is configured.");

            client.ClassificationModel = "my-finetuned-classifier";
            ClassificationResponse single = await client.ClassifyAsync("great value", null, token).ConfigureAwait(false);
            SharedAssert.True(single.Success, "Cohere single classification should succeed.");
            SharedAssert.Equal("my-finetuned-classifier", single.Model, "Cohere classification should report the configured model.");
            SharedAssert.Equal(1, single.Classifications.Count, "Cohere single classification should return one result.");
            LocalJson singleBody = LocalJson.Parse(server.RequestBodies[1]);
            SharedAssert.Equal("my-finetuned-classifier", singleBody.Str("model"), "Cohere classification should send the configured model.");
            SharedAssert.False(singleBody.Has("examples"), "Cohere classification without examples should omit the field.");

            ClassificationResponse fallback = await client.ClassifyAsync("nolabels here", null, token).ConfigureAwait(false);
            SharedAssert.True(fallback.Success, "Cohere classification without a label map should succeed.");
            SharedAssert.Equal("neutral", fallback.Classifications[0].Label, "Cohere classification should fall back to the prediction field.");
            SharedAssert.Equal(0.5, fallback.Classifications[0].Score, "Cohere classification should fall back to the confidence field.");
            SharedAssert.Equal(0, fallback.Classifications[0].Labels.Count, "Cohere classification fallback should expose no label list.");
        }

        private static async Task RunCohereModelsAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using CohereClient client = CreateCohereClient(server);
            client.ModelsPageSize = 2;

            List<ModelInformation> models = new List<ModelInformation>();
            await foreach (ModelInformation model in client.ListModelsAsync(token).ConfigureAwait(false))
            {
                models.Add(model);
            }

            SharedAssert.Equal(3, models.Count, "Cohere model listing should follow next_page_token across pages.");
            SharedAssert.Equal("command-a-03-2025", models[0].Name, "Cohere model name should parse.");
            SharedAssert.Equal(256000, models[0].InputTokenLimit, "Cohere context length should map to InputTokenLimit.");
            SharedAssert.Equal("chat", models[0].Metadata["endpoints"], "Cohere endpoints should map to metadata.");
            SharedAssert.Equal("tools,strict_tools", models[0].Metadata["features"], "Cohere features should map to metadata.");
            SharedAssert.Equal("false", models[0].Metadata["finetuned"], "Cohere finetuned flag should map to metadata.");
            SharedAssert.Equal("rerank-v3.5", models[2].Name, "Cohere second page should be listed.");

            List<CompletionCallDetail> details = client.CallDetails;
            SharedAssert.True(details[0].Url.Contains("page_size=2"), "Cohere model listing should send page_size.");
            SharedAssert.True(details[1].Url.Contains("page_token=page2"), "Cohere model listing should send the next page token.");

            SharedAssert.True(await client.ModelExistsAsync("rerank-v3.5", token).ConfigureAwait(false), "Cohere ModelExistsAsync should find a model on a later page.");
            SharedAssert.False(await client.ModelExistsAsync("does-not-exist", token).ConfigureAwait(false), "Cohere ModelExistsAsync should return false for an unknown model.");

            ModelInformation? info = await client.GetModelInformationAsync("command-a-03-2025", token).ConfigureAwait(false);
            SharedAssert.NotNull(info, "Cohere model lookup should return information.");
            SharedAssert.Equal("command-a-03-2025", info!.Name, "Cohere model lookup should parse the name.");
            SharedAssert.Equal("true", info.Metadata["finetuned"], "Cohere model lookup should parse the finetuned flag.");
            SharedAssert.True(await client.GetModelInformationAsync("missing-model", token).ConfigureAwait(false) == null, "Cohere model lookup should return null for a missing model.");

            await SharedAssert.ThrowsExactAsync<ArgumentNullException>(
                () => client.GetModelInformationAsync(" ", token),
                "Cohere model lookup should reject a blank model name.").ConfigureAwait(false);

            SharedAssert.True(await client.ValidateConnectivityAsync(token).ConfigureAwait(false), "Cohere connectivity validation should succeed against the local server.");
        }

        private static async Task RunCohereUnsupportedOperationsAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using CohereClient client = CreateCohereClient(server);

            await SharedAssert.ThrowsExactAsync<NotSupportedException>(
                () => client.PullModelAsync("command-a-03-2025", token: token),
                "Cohere PullModelAsync should be unsupported.").ConfigureAwait(false);
            await SharedAssert.ThrowsExactAsync<NotSupportedException>(
                () => client.DeleteModelAsync("command-a-03-2025", token),
                "Cohere DeleteModelAsync should be unsupported.").ConfigureAwait(false);
            await SharedAssert.ThrowsExactAsync<NotSupportedException>(
                () => client.EmbedSparseAsync("text", null, token),
                "Cohere EmbedSparseAsync should be unsupported.").ConfigureAwait(false);

            SharedAssert.Equal(0, server.RequestPaths.Count, "Cohere unsupported operations should never reach the wire.");
        }

        private static async Task RunCohereHttpErrorHandlingAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using CohereClient bad = new CohereClient(server.Endpoint + "/missing", LocalExtendedRoutes.CohereTestKey);
            bad.TimeoutMs = 1000;

            ChatResponse chat = await bad.ChatAsync("hi", token: token).ConfigureAwait(false);
            AssertHttpFailure(chat.Success, chat.StatusCode, chat.Error, 404, "Cohere chat");

            ChatStreamingResponse chatStream = await bad.ChatStreamingAsync("hi", token: token).ConfigureAwait(false);
            AssertHttpFailure(chatStream.Success, chatStream.StatusCode, chatStream.Error, 404, "Cohere streaming chat");

            ToolChatResponse tool = await bad.ToolChatAsync(CreateWeatherToolRequest(), token).ConfigureAwait(false);
            AssertHttpFailure(tool.Success, tool.StatusCode, tool.Error, 404, "Cohere tool chat");

            ToolChatStreamingResponse toolStream = await bad.ToolChatStreamingAsync(CreateWeatherToolRequest(), token).ConfigureAwait(false);
            AssertHttpFailure(toolStream.Success, toolStream.StatusCode, toolStream.Error, 404, "Cohere streaming tool chat");

            GenerationResponse gen = await bad.GenerateAsync("hi", token: token).ConfigureAwait(false);
            AssertHttpFailure(gen.Success, gen.StatusCode, gen.Error, 404, "Cohere generation");

            GenerationStreamingResponse genStream = await bad.GenerateStreamingAsync("hi", token: token).ConfigureAwait(false);
            AssertHttpFailure(genStream.Success, genStream.StatusCode, genStream.Error, 404, "Cohere streaming generation");

            EmbeddingResponse embed = await bad.EmbedAsync("hi", token: token).ConfigureAwait(false);
            AssertHttpFailure(embed.Success, embed.StatusCode, embed.Error, 404, "Cohere embeddings");

            RerankResponse rerank = await bad.RerankAsync("q", _Documents, null, token).ConfigureAwait(false);
            AssertHttpFailure(rerank.Success, rerank.StatusCode, rerank.Error, 404, "Cohere rerank");
            SharedAssert.Equal(0, rerank.Results.Count, "A failed Cohere rerank should return no results.");

            ClassificationResponse classify = await bad.ClassifyAsync("hi", null, token).ConfigureAwait(false);
            AssertHttpFailure(classify.Success, classify.StatusCode, classify.Error, 404, "Cohere classification");

            SharedAssert.False(await bad.ValidateConnectivityAsync(token).ConfigureAwait(false), "Cohere connectivity validation should fail against a bad path.");
            SharedAssert.True(await bad.GetModelInformationAsync("command-a-03-2025", token).ConfigureAwait(false) == null, "Cohere model lookup should return null on HTTP errors.");

            using CohereClient client = CreateCohereClient(server);

            RerankResponse limited = await client.RerankAsync("rerankfail", _Documents, null, token).ConfigureAwait(false);
            AssertHttpFailure(limited.Success, limited.StatusCode, limited.Error, 429, "Cohere rate-limited rerank");
            SharedAssert.True(limited.Error!.Contains("too many requests"), "Cohere rerank errors should include the provider message.");

            EmbeddingResponse badEmbed = await client.EmbedAsync("embedfail", token: token).ConfigureAwait(false);
            AssertHttpFailure(badEmbed.Success, badEmbed.StatusCode, badEmbed.Error, 400, "Cohere rejected embedding");

            ClassificationResponse badClassify = await client.ClassifyAsync("classifyfail", null, token).ConfigureAwait(false);
            AssertHttpFailure(badClassify.Success, badClassify.StatusCode, badClassify.Error, 400, "Cohere rejected classification");

            ChatResponse malformed = await client.ChatAsync("nomessage", token: token).ConfigureAwait(false);
            SharedAssert.False(malformed.Success, "A Cohere chat response without a message should be unsuccessful.");
            SharedAssert.True(malformed.Error?.Contains("message") == true, "A Cohere chat response without a message should explain the problem.");
        }

        private static async Task RunCohereStreamingErrorFinishAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using CohereClient client = CreateCohereClient(server);

            ChatStreamingResponse streaming = await client.ChatStreamingAsync("streamerror", token: token).ConfigureAwait(false);
            string text = string.Empty;
            await foreach (ChatStreamingChunk chunk in streaming.Chunks.ConfigureAwait(false))
            {
                text += chunk.Text;
            }

            SharedAssert.Equal("partial", text, "Cohere streaming should keep text received before an error.");
            SharedAssert.Equal("ERROR", streaming.FinishReason, "Cohere streaming should surface an ERROR finish reason.");
            SharedAssert.True(streaming.Usage == null, "Cohere streaming without usage in message-end should leave usage null.");
        }

        private static async Task RunCohereToolArgumentsInvalidJsonAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using CohereClient client = CreateCohereClient(server);

            ToolChatRequest request = CreateWeatherToolRequest();
            request.Messages.Add(ChatMessage.User("badjsonargs"));

            ToolChatResponse response = await client.ToolChatAsync(request, token).ConfigureAwait(false);
            SharedAssert.True(response.Success, "Cohere tool chat with malformed arguments should not fail the response.");
            SharedAssert.Equal(1, response.ToolCalls.Count, "Cohere tool chat with malformed arguments should still return the tool call.");
            SharedAssert.Equal("{\"city\":", response.ToolCalls[0].ArgumentsJson, "Cohere malformed tool arguments should be passed through verbatim for the caller to handle.");
        }

        private static async Task RunCohereStreamingBodyTimeoutAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using CohereClient client = CreateCohereClient(server);
            client.TimeoutMs = 300;

            ChatStreamingResponse streaming = await client.ChatStreamingAsync("slowstream", token: token).ConfigureAwait(false);
            SharedAssert.True(streaming.Success, "Cohere slow stream should start.");

            bool timedOut = false;
            string? responseId = null;
            try
            {
                await foreach (ChatStreamingChunk chunk in streaming.Chunks.ConfigureAwait(false))
                {
                    responseId ??= chunk.ResponseId;
                }
            }
            catch (OperationCanceledException)
            {
                timedOut = true;
            }

            SharedAssert.True(timedOut, "Cohere streaming body enumeration should time out.");
            SharedAssert.Equal("co-stream", responseId, "Cohere streaming should yield events received before the timeout.");
        }

        private static async Task RunCohereCancellationAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using CohereClient client = CreateCohereClient(server);
            using CancellationTokenSource cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            CancellationToken c = cancelled.Token;

            await SharedAssert.ThrowsAsync<OperationCanceledException>(() => client.ChatAsync("hi", token: c), "Cohere ChatAsync should respect cancellation.").ConfigureAwait(false);
            await SharedAssert.ThrowsAsync<OperationCanceledException>(() => client.ChatStreamingAsync("hi", token: c), "Cohere ChatStreamingAsync should respect cancellation.").ConfigureAwait(false);
            await SharedAssert.ThrowsAsync<OperationCanceledException>(() => client.ToolChatAsync(CreateWeatherToolRequest(), c), "Cohere ToolChatAsync should respect cancellation.").ConfigureAwait(false);
            await SharedAssert.ThrowsAsync<OperationCanceledException>(() => client.ToolChatStreamingAsync(CreateWeatherToolRequest(), c), "Cohere ToolChatStreamingAsync should respect cancellation.").ConfigureAwait(false);
            await SharedAssert.ThrowsAsync<OperationCanceledException>(() => client.GenerateAsync("hi", token: c), "Cohere GenerateAsync should respect cancellation.").ConfigureAwait(false);
            await SharedAssert.ThrowsAsync<OperationCanceledException>(() => client.EmbedAsync("hi", token: c), "Cohere EmbedAsync should respect cancellation.").ConfigureAwait(false);
            await SharedAssert.ThrowsAsync<OperationCanceledException>(() => client.RerankAsync("q", _Documents, null, c), "Cohere RerankAsync should respect cancellation.").ConfigureAwait(false);
            await SharedAssert.ThrowsAsync<OperationCanceledException>(() => client.ClassifyAsync("hi", null, c), "Cohere ClassifyAsync should respect cancellation.").ConfigureAwait(false);
            await SharedAssert.ThrowsAsync<OperationCanceledException>(() => client.GetModelInformationAsync("m", c), "Cohere GetModelInformationAsync should respect cancellation.").ConfigureAwait(false);
            await SharedAssert.ThrowsAsync<OperationCanceledException>(() => client.ValidateConnectivityAsync(c), "Cohere ValidateConnectivityAsync should respect cancellation.").ConfigureAwait(false);
        }

        #endregion

        #region TEI

        private static async Task RunTeiEmbeddingTranslationAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using TeiClient client = CreateTeiClient(server);

            TeiEmbeddingOptions options = new TeiEmbeddingOptions();
            options.Normalize = false;
            options.Truncate = true;
            options.TruncationDirection = "LEFT";
            options.PromptName = " query ";
            options.Dimensions = 256;

            EmbeddingResponse batch = await client.EmbedAsync(new List<string> { "first", "second" }, options, token).ConfigureAwait(false);
            SharedAssert.True(batch.Success, "TEI batch embedding should succeed. " + batch.Error);
            SharedAssert.Equal(2, batch.Embeddings.Count, "TEI batch embedding should parse both vectors.");
            SharedAssert.Equal(1, batch.Embeddings[1].Index, "TEI embedding index should follow input order.");
            SharedAssert.Equal(3, batch.Embeddings[0].Embedding.Length, "TEI embedding length should parse.");
            SharedAssert.True(Math.Abs(batch.Embeddings[1].Embedding[0] - 1.1f) < 0.0001f, "TEI embedding values should parse.");
            SharedAssert.Equal("tei", batch.Model, "TEI embeddings should report the informational client model.");

            LocalJson body = LocalJson.Parse(server.RequestBodies[0]);
            SharedAssert.Equal("/embed", server.RequestPaths[0], "TEI embeddings should POST to /embed.");
            SharedAssert.Equal(2, body.Count("inputs"), "TEI embeddings should send inputs as an array.");
            SharedAssert.True(body.Bool("normalize") == false, "TEI embeddings should send normalize.");
            SharedAssert.True(body.Bool("truncate") == true, "TEI embeddings should send truncate.");
            SharedAssert.Equal("Left", body.Str("truncation_direction"), "TEI embeddings should send the TEI-cased truncation direction.");
            SharedAssert.Equal("query", body.Str("prompt_name"), "TEI embeddings should send the trimmed prompt name.");
            SharedAssert.Equal(256, body.Int("dimensions"), "TEI embeddings should send dimensions.");
            SharedAssert.False(body.Has("model"), "TEI serves a single model, so no model field should be sent.");

            EmbeddingResponse single = await client.EmbedAsync("only", new EmbeddingOptions { Model = "ignored-model" }, token).ConfigureAwait(false);
            SharedAssert.True(single.Success, "TEI single embedding should succeed.");
            SharedAssert.Equal(1, single.Embeddings.Count, "TEI single embedding should return one vector.");
            LocalJson singleBody = LocalJson.Parse(server.RequestBodies[1]);
            SharedAssert.Equal("Array", singleBody.Kind("inputs"), "TEI single embedding should still send an array.");
            SharedAssert.False(singleBody.Has("normalize") || singleBody.Has("model"), "TEI embedding without options should omit optional fields and the model.");

            SharedAssert.False(client.CallDetails[0].RequestHeaders.ContainsKey("Authorization"), "TEI without an API key should not send Authorization.");

            using TeiClient secured = new TeiClient(server.Endpoint, "tei-secret");
            await secured.EmbedAsync("x", token: token).ConfigureAwait(false);
            SharedAssert.True(secured.CallDetails[0].RequestHeaders.TryGetValue("Authorization", out string? auth) && auth == "Bearer tei-secret",
                "TEI with an API key should send a bearer Authorization header.");
        }

        private static async Task RunTeiOptionsClampingAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            TeiEmbeddingOptions embed = new TeiEmbeddingOptions();
            embed.TruncationDirection = "right";
            SharedAssert.Equal("Right", embed.TruncationDirection, "TEI truncation direction should normalize to TEI casing.");
            embed.TruncationDirection = "up";
            SharedAssert.True(embed.TruncationDirection == null, "An unrecognized TEI truncation direction should revert to null.");
            embed.Dimensions = 0;
            SharedAssert.True(embed.Dimensions == null, "A TEI dimension below 1 should revert to null.");
            embed.Dimensions = 64;
            SharedAssert.Equal(64, embed.Dimensions, "A positive TEI dimension should be kept.");
            embed.PromptName = "   ";
            SharedAssert.True(embed.PromptName == null, "A blank TEI prompt name should revert to null.");

            TeiRerankOptions rerank = new TeiRerankOptions { TruncationDirection = " Left " };
            SharedAssert.Equal("Left", rerank.TruncationDirection, "TEI rerank truncation direction should normalize.");
            TeiClassificationOptions classify = new TeiClassificationOptions { TruncationDirection = "sideways" };
            SharedAssert.True(classify.TruncationDirection == null, "TEI classification truncation direction should revert on invalid input.");
            TeiSparseEmbeddingOptions sparse = new TeiSparseEmbeddingOptions { TruncationDirection = "RIGHT", PromptName = " doc " };
            SharedAssert.Equal("Right", sparse.TruncationDirection, "TEI sparse truncation direction should normalize.");
            SharedAssert.Equal("doc", sparse.PromptName, "TEI sparse prompt name should be trimmed.");

            using TeiClient client = new TeiClient();
            SharedAssert.Equal("http://localhost:8080", client.Endpoint, "TEI should default to the local server endpoint.");
            SharedAssert.Equal("tei", client.Model, "TEI should use an informational default model name.");
        }

        private static async Task RunTeiRerankTranslationAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using TeiClient client = CreateTeiClient(server);

            TeiRerankOptions options = new TeiRerankOptions { RawScores = true, Truncate = false, TruncationDirection = "right", Model = "ignored" };
            RerankResponse response = await client.RerankAsync("which passage matters?", _Documents, options, token).ConfigureAwait(false);
            SharedAssert.True(response.Success, "TEI rerank should succeed. " + response.Error);
            SharedAssert.True(response.TotalTokens == null && response.SearchUnits == null && response.ResponseId == null, "TEI rerank reports no usage or id.");

            LocalJson body = LocalJson.Parse(server.RequestBodies[0]);
            SharedAssert.Equal("/rerank", server.RequestPaths[0], "TEI rerank should POST to /rerank.");
            SharedAssert.Equal("which passage matters?", body.Str("query"), "TEI rerank should send the query.");
            SharedAssert.Equal(4, body.Count("texts"), "TEI rerank should send documents as texts.");
            SharedAssert.True(body.Bool("raw_scores") == true, "TEI rerank should send raw_scores.");
            SharedAssert.True(body.Bool("truncate") == false, "TEI rerank should send truncate.");
            SharedAssert.Equal("Right", body.Str("truncation_direction"), "TEI rerank should send truncation_direction.");
            SharedAssert.False(body.Has("model") || body.Has("return_text") || body.Has("documents"), "TEI rerank should not send fields TEI does not use.");
        }

        private static async Task RunTeiClassifyAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using TeiClient client = CreateTeiClient(server);

            ClassificationResponse response = await client.ClassifyAsync(
                new List<string> { "this is great", "this is bad" },
                new TeiClassificationOptions { RawScores = false, Truncate = true },
                token).ConfigureAwait(false);

            SharedAssert.True(response.Success, "TEI classification should succeed. " + response.Error);
            SharedAssert.Equal(2, response.Classifications.Count, "TEI classification should return one result per input.");
            SharedAssert.Equal("positive", response.Classifications[0].Label, "TEI classification should pick the highest-scoring label.");
            SharedAssert.Equal(0.9, response.Classifications[0].Score, "TEI classification should expose the top score.");
            SharedAssert.Equal("positive", response.Classifications[0].Labels[0].Label, "TEI classification labels should be sorted by score.");
            SharedAssert.Equal("this is great", response.Classifications[0].Input, "TEI classification should attach the input.");
            SharedAssert.Equal("negative", response.Classifications[1].Label, "TEI second classification should parse.");

            LocalJson body = LocalJson.Parse(server.RequestBodies[0]);
            SharedAssert.Equal("/predict", server.RequestPaths[0], "TEI classification should POST to /predict.");
            SharedAssert.Equal(2, body.Count("inputs"), "TEI classification should send one batch entry per input.");
            SharedAssert.Equal("Array", body.Kind("inputs.0"), "TEI classification should wrap each input so two inputs are not read as a text pair.");
            SharedAssert.Equal(1, body.Count("inputs.0"), "TEI classification batch entries should hold a single text.");
            SharedAssert.Equal("this is bad", body.Str("inputs.1.0"), "TEI classification should preserve input order.");
            SharedAssert.True(body.Bool("raw_scores") == false, "TEI classification should send raw_scores.");
            SharedAssert.True(body.Bool("truncate") == true, "TEI classification should send truncate.");

            ClassificationResponse single = await client.ClassifyAsync("great!", null, token).ConfigureAwait(false);
            SharedAssert.True(single.Success, "TEI single classification should succeed.");
            SharedAssert.Equal(1, single.Classifications.Count, "TEI single classification should return one result.");
            SharedAssert.Equal("Array", LocalJson.Parse(server.RequestBodies[1]).Kind("inputs.0"), "TEI single classification should still use the batch form.");
        }

        private static async Task RunTeiEmbedSparseAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using TeiClient client = CreateTeiClient(server);

            SparseEmbeddingResponse response = await client.EmbedSparseAsync(
                new List<string> { "first", "second" },
                new TeiSparseEmbeddingOptions { Truncate = true, PromptName = "doc" },
                token).ConfigureAwait(false);

            SharedAssert.True(response.Success, "TEI sparse embedding should succeed. " + response.Error);
            SharedAssert.Equal(2, response.Embeddings.Count, "TEI sparse embedding should return one vector per input.");
            SharedAssert.Equal(1, response.Embeddings[1].Index, "TEI sparse embedding index should follow input order.");
            SharedAssert.Equal(2, response.Embeddings[0].Values.Count, "TEI sparse embedding should parse every non-zero entry.");
            SharedAssert.Equal(10, response.Embeddings[0].Values[0].Index, "TEI sparse entry index should parse.");
            SharedAssert.Equal(0.5f, response.Embeddings[0].Values[0].Value, "TEI sparse entry value should parse.");
            SharedAssert.Equal(201, response.Embeddings[1].Values[1].Index, "TEI sparse entries should be per input.");

            LocalJson body = LocalJson.Parse(server.RequestBodies[0]);
            SharedAssert.Equal("/embed_sparse", server.RequestPaths[0], "TEI sparse embedding should POST to /embed_sparse.");
            SharedAssert.True(body.Bool("truncate") == true, "TEI sparse embedding should send truncate.");
            SharedAssert.Equal("doc", body.Str("prompt_name"), "TEI sparse embedding should send prompt_name.");

            SparseEmbeddingResponse single = await client.EmbedSparseAsync("solo", null, token).ConfigureAwait(false);
            SharedAssert.True(single.Success && single.Embeddings.Count == 1, "TEI single sparse embedding should return one vector.");
        }

        private static async Task RunTeiInfoAndModelsAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using TeiClient client = CreateTeiClient(server);

            List<ModelInformation> models = new List<ModelInformation>();
            await foreach (ModelInformation model in client.ListModelsAsync(token).ConfigureAwait(false))
            {
                models.Add(model);
            }

            SharedAssert.Equal(1, models.Count, "TEI should list exactly the one hosted model.");
            ModelInformation info = models[0];
            SharedAssert.Equal("BAAI/bge-reranker-base", info.Name, "TEI model name should come from model_id.");
            SharedAssert.Equal("bge-reranker", info.DisplayName, "TEI display name should come from served_model_name.");
            SharedAssert.Equal(512, info.InputTokenLimit, "TEI max_input_length should map to InputTokenLimit.");
            SharedAssert.Equal("reranker", info.Metadata["model_type"], "TEI model type should be exposed in metadata.");
            SharedAssert.Equal("32", info.Metadata["max_client_batch_size"], "TEI batch size limit should be exposed in metadata.");
            SharedAssert.Equal("1.8.0", info.Metadata["version"], "TEI version should be exposed in metadata.");
            SharedAssert.Equal("/info", server.RequestPaths[0], "TEI model listing should GET /info.");

            SharedAssert.True(await client.ModelExistsAsync("BAAI/bge-reranker-base", token).ConfigureAwait(false), "TEI ModelExistsAsync should match the hosted model.");
            SharedAssert.False(await client.ModelExistsAsync("other-model", token).ConfigureAwait(false), "TEI ModelExistsAsync should not match another model.");

            SharedAssert.NotNull(await client.GetModelInformationAsync("baai/BGE-reranker-base", token).ConfigureAwait(false), "TEI model lookup should match model_id case-insensitively.");
            SharedAssert.NotNull(await client.GetModelInformationAsync("bge-reranker", token).ConfigureAwait(false), "TEI model lookup should match served_model_name.");
            SharedAssert.True(await client.GetModelInformationAsync("other-model", token).ConfigureAwait(false) == null, "TEI model lookup should return null for another model.");
            await SharedAssert.ThrowsExactAsync<ArgumentNullException>(
                () => client.GetModelInformationAsync(string.Empty, token),
                "TEI model lookup should reject an empty model name.").ConfigureAwait(false);

            using TeiClient bad = new TeiClient(server.Endpoint + "/missing");
            bad.TimeoutMs = 1000;
            int badCount = 0;
            await foreach (ModelInformation model in bad.ListModelsAsync(token).ConfigureAwait(false)) badCount++;
            SharedAssert.Equal(0, badCount, "TEI model listing should yield nothing when /info fails.");
            SharedAssert.True(await bad.GetModelInformationAsync("BAAI/bge-reranker-base", token).ConfigureAwait(false) == null, "TEI model lookup should return null when /info fails.");
        }

        private static async Task RunTeiValidateConnectivityAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using TeiClient client = CreateTeiClient(server);

            SharedAssert.True(await client.ValidateConnectivityAsync(token).ConfigureAwait(false), "TEI connectivity validation should succeed against the local server.");
            SharedAssert.Equal("/health", server.RequestPaths[0], "TEI connectivity validation should GET /health.");

            using TeiClient bad = new TeiClient(server.Endpoint + "/missing");
            bad.TimeoutMs = 1000;
            SharedAssert.False(await bad.ValidateConnectivityAsync(token).ConfigureAwait(false), "TEI connectivity validation should fail on a non-2xx /health.");

            using TeiClient unreachable = new TeiClient("http://127.0.0.1:1");
            unreachable.TimeoutMs = 1000;
            SharedAssert.False(await unreachable.ValidateConnectivityAsync(token).ConfigureAwait(false), "TEI connectivity validation should fail when the server is unreachable.");
        }

        private static async Task RunTeiUnsupportedOperationsAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using TeiClient client = CreateTeiClient(server);

            await SharedAssert.ThrowsExactAsync<NotSupportedException>(() => client.ChatAsync("hi", token: token), "TEI ChatAsync should be unsupported.").ConfigureAwait(false);
            await SharedAssert.ThrowsExactAsync<NotSupportedException>(() => client.ChatStreamingAsync("hi", token: token), "TEI ChatStreamingAsync should be unsupported.").ConfigureAwait(false);
            await SharedAssert.ThrowsExactAsync<NotSupportedException>(() => client.ToolChatAsync(CreateWeatherToolRequest(), token), "TEI ToolChatAsync should be unsupported.").ConfigureAwait(false);
            await SharedAssert.ThrowsExactAsync<NotSupportedException>(() => client.ToolChatStreamingAsync(CreateWeatherToolRequest(), token), "TEI ToolChatStreamingAsync should be unsupported.").ConfigureAwait(false);
            await SharedAssert.ThrowsExactAsync<NotSupportedException>(() => client.GenerateAsync("hi", token: token), "TEI GenerateAsync should be unsupported.").ConfigureAwait(false);
            await SharedAssert.ThrowsExactAsync<NotSupportedException>(() => client.GenerateStreamingAsync("hi", token: token), "TEI GenerateStreamingAsync should be unsupported.").ConfigureAwait(false);
            await SharedAssert.ThrowsExactAsync<NotSupportedException>(() => client.PullModelAsync("m", token: token), "TEI PullModelAsync should be unsupported.").ConfigureAwait(false);
            await SharedAssert.ThrowsExactAsync<NotSupportedException>(() => client.DeleteModelAsync("m", token), "TEI DeleteModelAsync should be unsupported.").ConfigureAwait(false);

            SharedAssert.Equal(0, server.RequestPaths.Count, "TEI unsupported operations should never reach the wire.");
        }

        private static async Task RunTeiHttpErrorHandlingAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using TeiClient client = CreateTeiClient(server);

            EmbeddingResponse batchTooLarge = await client.EmbedAsync("teifail413", token: token).ConfigureAwait(false);
            AssertHttpFailure(batchTooLarge.Success, batchTooLarge.StatusCode, batchTooLarge.Error, 413, "TEI oversized batch");
            SharedAssert.True(batchTooLarge.Error!.Contains("Validation"), "TEI errors should include the error_type.");
            SharedAssert.Equal(0, batchTooLarge.Embeddings.Count, "A failed TEI embedding should return no vectors.");

            EmbeddingResponse tokenizer = await client.EmbedAsync("teifail422", token: token).ConfigureAwait(false);
            AssertHttpFailure(tokenizer.Success, tokenizer.StatusCode, tokenizer.Error, 422, "TEI tokenizer error");

            RerankResponse wrongModel = await client.RerankAsync("teifail424", _Documents, null, token).ConfigureAwait(false);
            AssertHttpFailure(wrongModel.Success, wrongModel.StatusCode, wrongModel.Error, 424, "TEI rerank against a non-reranker model");
            SharedAssert.True(wrongModel.Error!.Contains("re-ranker"), "TEI wrong-model errors should include the server message.");

            ClassificationResponse overloaded = await client.ClassifyAsync("teifail429", null, token).ConfigureAwait(false);
            AssertHttpFailure(overloaded.Success, overloaded.StatusCode, overloaded.Error, 429, "TEI overloaded classification");

            SparseEmbeddingResponse sparse = await client.EmbedSparseAsync("teifail424", null, token).ConfigureAwait(false);
            AssertHttpFailure(sparse.Success, sparse.StatusCode, sparse.Error, 424, "TEI sparse embedding against a dense model");

            using TeiClient bad = new TeiClient(server.Endpoint + "/missing");
            bad.TimeoutMs = 1000;
            EmbeddingResponse missing = await bad.EmbedAsync("x", token: token).ConfigureAwait(false);
            AssertHttpFailure(missing.Success, missing.StatusCode, missing.Error, 404, "TEI embedding on a bad path");
        }

        private static async Task RunTeiCancellationAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using TeiClient client = CreateTeiClient(server);
            using CancellationTokenSource cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            CancellationToken c = cancelled.Token;

            await SharedAssert.ThrowsAsync<OperationCanceledException>(() => client.EmbedAsync("hi", token: c), "TEI EmbedAsync should respect cancellation.").ConfigureAwait(false);
            await SharedAssert.ThrowsAsync<OperationCanceledException>(() => client.RerankAsync("q", _Documents, null, c), "TEI RerankAsync should respect cancellation.").ConfigureAwait(false);
            await SharedAssert.ThrowsAsync<OperationCanceledException>(() => client.ClassifyAsync("hi", null, c), "TEI ClassifyAsync should respect cancellation.").ConfigureAwait(false);
            await SharedAssert.ThrowsAsync<OperationCanceledException>(() => client.EmbedSparseAsync("hi", null, c), "TEI EmbedSparseAsync should respect cancellation.").ConfigureAwait(false);
            await SharedAssert.ThrowsAsync<OperationCanceledException>(() => client.ValidateConnectivityAsync(c), "TEI ValidateConnectivityAsync should respect cancellation.").ConfigureAwait(false);
            await SharedAssert.ThrowsAsync<OperationCanceledException>(() => client.GetModelInformationAsync("m", c), "TEI GetModelInformationAsync should respect cancellation.").ConfigureAwait(false);
            await SharedAssert.ThrowsAsync<OperationCanceledException>(
                async () => { await foreach (ModelInformation m in client.ListModelsAsync(c).ConfigureAwait(false)) { } },
                "TEI ListModelsAsync should respect cancellation.").ConfigureAwait(false);
        }

        #endregion

        #region VoyageAI-and-Bedrock

        private static async Task RunVoyageAiRerankTranslationAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using VoyageAiClient client = CreateVoyageAiClient(server);

            SharedAssert.Equal("rerank-2.5", client.RerankModel, "VoyageAI should default the rerank model.");
            await SharedAssert.ThrowsExactAsync<ArgumentNullException>(
                () => { client.RerankModel = ""; return Task.CompletedTask; },
                "A blank VoyageAI rerank model should throw.").ConfigureAwait(false);

            VoyageAiRerankOptions options = new VoyageAiRerankOptions { TopN = 2, Truncation = false, ReturnDocuments = true };
            RerankResponse response = await client.RerankAsync("which passage matters?", _Documents, options, token).ConfigureAwait(false);

            SharedAssert.True(response.Success, "VoyageAI rerank should succeed. " + response.Error);
            SharedAssert.Equal(2, response.Results.Count, "VoyageAI rerank should honor top_k.");
            SharedAssert.Equal(1, response.Results[0].Index, "VoyageAI rerank should rank the relevant document first.");
            SharedAssert.Equal(_Documents[1], response.Results[0].Document, "VoyageAI rerank should attach document text on request.");
            SharedAssert.Equal(17, response.TotalTokens, "VoyageAI rerank should expose total tokens.");
            SharedAssert.Equal("rerank-2.5", response.Model, "VoyageAI rerank should report the model.");

            LocalJson body = LocalJson.Parse(server.RequestBodies[0]);
            SharedAssert.Equal("/v1/rerank", server.RequestPaths[0], "VoyageAI rerank should POST to /v1/rerank.");
            SharedAssert.Equal("rerank-2.5", body.Str("model"), "VoyageAI rerank should send the rerank model, not the embedding model.");
            SharedAssert.Equal("which passage matters?", body.Str("query"), "VoyageAI rerank should send the query.");
            SharedAssert.Equal(4, body.Count("documents"), "VoyageAI rerank should send the documents.");
            SharedAssert.Equal(2, body.Int("top_k"), "VoyageAI rerank should send top_k.");
            SharedAssert.True(body.Bool("truncation") == false, "VoyageAI rerank should send truncation.");
            SharedAssert.False(body.Has("return_documents"), "VoyageAI documents are attached client-side, so return_documents should not be sent.");
            SharedAssert.True(client.CallDetails[0].RequestHeaders.ContainsKey("Authorization"), "VoyageAI rerank should carry a bearer header.");

            client.RerankModel = "rerank-2.5-lite";
            await client.RerankAsync("q", _Documents, null, token).ConfigureAwait(false);
            LocalJson second = LocalJson.Parse(server.RequestBodies[1]);
            SharedAssert.Equal("rerank-2.5-lite", second.Str("model"), "VoyageAI RerankModel should be used when no override is given.");
            SharedAssert.False(second.Has("top_k") || second.Has("truncation"), "VoyageAI rerank should omit unset optional fields.");
        }

        private static async Task RunVoyageAiRerankErrorsAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using VoyageAiClient client = CreateVoyageAiClient(server);

            RerankResponse failed = await client.RerankAsync("rerankfail", _Documents, null, token).ConfigureAwait(false);
            AssertHttpFailure(failed.Success, failed.StatusCode, failed.Error, 400, "VoyageAI rejected rerank");

            using CancellationTokenSource cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await SharedAssert.ThrowsAsync<OperationCanceledException>(
                () => client.RerankAsync("q", _Documents, null, cancelled.Token),
                "VoyageAI RerankAsync should respect cancellation.").ConfigureAwait(false);
        }

        private static async Task RunBedrockRerankTranslationAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using BedrockClient client = CreateBedrockClient(server);

            SharedAssert.Equal("cohere.rerank-v3-5:0", client.RerankModel, "Bedrock should default to the Cohere rerank model.");
            await SharedAssert.ThrowsExactAsync<ArgumentNullException>(
                () => { client.RerankModel = " "; return Task.CompletedTask; },
                "A blank Bedrock rerank model should throw.").ConfigureAwait(false);

            BedrockRerankOptions options = new BedrockRerankOptions { TopN = 2, MaxTokensPerDoc = 300 };
            RerankResponse response = await client.RerankAsync("which passage matters?", _Documents, options, token).ConfigureAwait(false);
            SharedAssert.True(response.Success, "Bedrock rerank should succeed. " + response.Error);
            SharedAssert.Equal(2, response.Results.Count, "Bedrock rerank should honor TopN.");
            SharedAssert.Equal(1, response.Results[0].Index, "Bedrock rerank should sort by relevance.");

            SharedAssert.Equal("/model/cohere.rerank-v3-5:0/invoke", server.RequestPaths[0], "Bedrock rerank should use InvokeModel for the rerank model.");
            LocalJson body = LocalJson.Parse(server.RequestBodies[0]);
            SharedAssert.Equal("which passage matters?", body.Str("query"), "Bedrock rerank should send the query.");
            SharedAssert.Equal(4, body.Count("documents"), "Bedrock rerank should send the documents.");
            SharedAssert.Equal(2, body.Int("top_n"), "Bedrock rerank should send top_n.");
            SharedAssert.Equal(2, body.Int("api_version"), "Bedrock Cohere rerank should send api_version 2.");
            SharedAssert.Equal(300, body.Int("max_tokens_per_doc"), "Bedrock rerank should send max_tokens_per_doc.");

            CompletionCallDetail detail = client.CallDetails[0];
            SharedAssert.True(detail.RequestHeaders.TryGetValue("Authorization", out string? auth) && auth.StartsWith("AWS4-HMAC-SHA256", StringComparison.Ordinal),
                "Bedrock rerank requests should be SigV4-signed.");

            RerankResponse amazon = await client.RerankAsync("q", _Documents, new RerankOptions { Model = "amazon.rerank-v1:0" }, token).ConfigureAwait(false);
            SharedAssert.True(amazon.Success, "Bedrock Amazon rerank should succeed.");
            SharedAssert.Equal("/model/amazon.rerank-v1:0/invoke", server.RequestPaths[1], "Bedrock rerank should honor the model override.");
            LocalJson amazonBody = LocalJson.Parse(server.RequestBodies[1]);
            SharedAssert.False(amazonBody.Has("api_version"), "Bedrock non-Cohere rerank models should not send api_version.");
            SharedAssert.False(amazonBody.Has("top_n") || amazonBody.Has("max_tokens_per_doc"), "Bedrock rerank should omit unset optional fields.");
        }

        private static async Task RunBedrockRerankErrorsAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using BedrockClient client = CreateBedrockClient(server);

            RerankResponse failed = await client.RerankAsync("rerankfail", _Documents, null, token).ConfigureAwait(false);
            AssertHttpFailure(failed.Success, failed.StatusCode, failed.Error, 400, "Bedrock rejected rerank");
            SharedAssert.True(failed.Error!.Contains("ValidationException"), "Bedrock rerank errors should include the provider message.");

            using CancellationTokenSource cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await SharedAssert.ThrowsAsync<OperationCanceledException>(
                () => client.RerankAsync("q", _Documents, null, cancelled.Token),
                "Bedrock RerankAsync should respect cancellation.").ConfigureAwait(false);
        }

        #endregion

        #region Configuration

        private static async Task RunCohereTeiConfigurationAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            ProviderTestConfiguration cohere = ProviderTestConfiguration.CreateWithDefaults(" Cohere ", apiKey: "co-key");
            SharedAssert.Equal("cohere", cohere.ProviderType, "Cohere provider type should normalize.");
            SharedAssert.Equal(ProviderTestConfiguration.DefaultCohereEndpoint, cohere.Endpoint, "Cohere should use its default endpoint.");
            SharedAssert.Equal("embed-v4.0", cohere.EmbeddingModel, "Cohere should default the embedding model.");
            SharedAssert.Equal("rerank-v3.5", cohere.RerankModel, "Cohere should default the rerank model.");

            ProviderTestConfiguration tei = ProviderTestConfiguration.CreateWithDefaults("TEI");
            SharedAssert.Equal("tei", tei.ProviderType, "TEI provider type should normalize.");
            SharedAssert.Equal(ProviderTestConfiguration.DefaultTeiEndpoint, tei.Endpoint, "TEI should use its default endpoint.");
            SharedAssert.True(tei.ApiKey == null, "TEI should not require an API key.");
            SharedAssert.Equal(string.Empty, tei.RerankModel, "TEI serves a single model, so no rerank model is configured.");

            SharedAssert.Equal("rerank-2.5", ProviderTestConfiguration.CreateWithDefaults("voyageai").RerankModel, "VoyageAI should default the rerank model.");
            SharedAssert.Equal(string.Empty, ProviderTestConfiguration.CreateWithDefaults("openai").RerankModel, "OpenAI has no rerank model.");

            Dictionary<string, string?> original = LocalBehaviorSuite.CaptureProviderEnvironment();
            try
            {
                LocalBehaviorSuite.ClearProviderEnvironment();
                Environment.SetEnvironmentVariable("POLYPROMPT_TEST_COHERE_API_KEY", "co-key");
                Environment.SetEnvironmentVariable("POLYPROMPT_TEST_COHERE_RERANK_MODEL", "rerank-v4.0-pro");
                ProviderTestConfiguration fromEnv = ProviderTestConfiguration.FromEnvironment() ?? throw new TestFailureException("Cohere environment configuration should be created.");
                SharedAssert.Equal("cohere", fromEnv.ProviderType, "Cohere environment provider should be selected.");
                SharedAssert.Equal("co-key", fromEnv.ApiKey, "Cohere environment API key should be retained.");
                SharedAssert.Equal("rerank-v4.0-pro", fromEnv.RerankModel, "Cohere environment rerank model should be retained.");

                LocalBehaviorSuite.ClearProviderEnvironment();
                Environment.SetEnvironmentVariable("POLYPROMPT_TEST_TEI_ENDPOINT", "http://tei.local:9000");
                ProviderTestConfiguration teiEnv = ProviderTestConfiguration.FromEnvironment() ?? throw new TestFailureException("TEI environment configuration should be created.");
                SharedAssert.Equal("tei", teiEnv.ProviderType, "TEI environment provider should be selected.");
                SharedAssert.Equal("http://tei.local:9000", teiEnv.Endpoint, "TEI environment endpoint should be retained.");

                LocalBehaviorSuite.ClearProviderEnvironment();
                Environment.SetEnvironmentVariable("POLYPROMPT_TEST_PROVIDER", "tei");
                Environment.SetEnvironmentVariable("POLYPROMPT_TEST_RERANK_MODEL", "generic-rerank");
                ProviderTestConfiguration generic = ProviderTestConfiguration.FromEnvironment() ?? throw new TestFailureException("Generic environment configuration should be created.");
                SharedAssert.Equal("tei", generic.ProviderType, "The generic provider variable should accept tei.");
                SharedAssert.Equal("generic-rerank", generic.RerankModel, "The generic rerank model variable should be applied.");

                LocalBehaviorSuite.ClearProviderEnvironment();
                Environment.SetEnvironmentVariable("POLYPROMPT_TEST_COHERE_API_KEY", "co-key");
                Environment.SetEnvironmentVariable("POLYPROMPT_TEST_TEI_ENDPOINT", "http://tei.local:9000");
                await SharedAssert.ThrowsExactAsync<ArgumentException>(
                    () => { ProviderTestConfiguration.FromEnvironment(); return Task.CompletedTask; },
                    "Cohere and TEI environment groups together should conflict.").ConfigureAwait(false);
            }
            finally
            {
                LocalBehaviorSuite.RestoreProviderEnvironment(original);
            }
        }

        #endregion

        #region Helpers

        private static void AssertHttpFailure(bool success, int? statusCode, string? error, int expectedStatus, string label)
        {
            SharedAssert.False(success, label + " should be unsuccessful.");
            SharedAssert.Equal(expectedStatus, statusCode ?? 0, label + " should preserve the status code.");
            SharedAssert.NotEmpty(error, label + " should surface an error message.");
        }

        private static List<CompletionClientBase> CreateRerankClients(LocalOpenAiTestServer server)
        {
            return new List<CompletionClientBase>
            {
                CreateCohereClient(server),
                CreateTeiClient(server),
                CreateVoyageAiClient(server),
                CreateBedrockClient(server),
            };
        }

        private static CohereClient CreateCohereClient(LocalOpenAiTestServer server)
        {
            CohereClient client = new CohereClient(server.Endpoint, LocalExtendedRoutes.CohereTestKey);
            client.Model = "cohere-test-model";
            client.TimeoutMs = 2000;
            return client;
        }

        private static TeiClient CreateTeiClient(LocalOpenAiTestServer server)
        {
            TeiClient client = new TeiClient(server.Endpoint);
            client.TimeoutMs = 2000;
            return client;
        }

        private static VoyageAiClient CreateVoyageAiClient(LocalOpenAiTestServer server)
        {
            VoyageAiClient client = new VoyageAiClient(server.Endpoint, "test-key");
            client.Model = "voyage-test";
            client.TimeoutMs = 2000;
            return client;
        }

        private static BedrockClient CreateBedrockClient(LocalOpenAiTestServer server)
        {
            BedrockClient client = new BedrockClient(
                new StaticAwsCredential("AKIDTESTEXAMPLE", "wJalrXUtnFEMI/K7MDENG+bPxRfiCYEXAMPLEKEY", "us-east-1"),
                "us-east-1",
                endpoint: server.Endpoint);
            client.TimeoutMs = 2000;
            return client;
        }

        private static ToolChatRequest CreateWeatherToolRequest()
        {
            ToolChatRequest request = new ToolChatRequest();
            request.Messages.Add(ChatMessage.System("Answer with weather guidance."));
            request.Messages.Add(ChatMessage.User("What is the weather in Seattle?"));
            request.Tools.Add(ToolDefinition.Function(
                "get_weather",
                "Get current weather for a city.",
                new Dictionary<string, object>
                {
                    { "type", "object" },
                    {
                        "properties", new Dictionary<string, object>
                        {
                            { "city", new Dictionary<string, object> { { "type", "string" } } }
                        }
                    },
                    { "required", new List<string> { "city" } }
                }));
            return request;
        }

        #endregion
    }
}
