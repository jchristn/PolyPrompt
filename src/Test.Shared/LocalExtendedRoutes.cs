namespace Test.Shared
{
    using System.Globalization;
    using System.Net;
    using System.Text;

    /// <summary>
    /// Local test server routes for Cohere, Text Embeddings Inference, VoyageAI rerank, and Bedrock rerank.
    /// Cohere requests are recognized by their test bearer key (so Cohere's /v1/models does not collide with
    /// the OpenAI and Anthropic routes); TEI paths are unique. Magic strings in request text select error and
    /// edge-case responses, mirroring the conventions of <see cref="LocalOpenAiTestServer"/>.
    /// </summary>
    internal static class LocalExtendedRoutes
    {
        /// <summary>
        /// Bearer key the tests give to CohereClient so the server can route Cohere requests.
        /// </summary>
        public const string CohereTestKey = "cohere-key";

        public static async Task<bool> TryHandleAsync(HttpListenerContext context, string path, string requestBody)
        {
            if (IsCohereRequest(context))
            {
                return await TryHandleCohereAsync(context, path, requestBody).ConfigureAwait(false);
            }

            if (path == "/embed" || path == "/rerank" || path == "/predict" || path == "/embed_sparse" || path == "/info" || path == "/health")
            {
                await HandleTeiAsync(context, path, requestBody).ConfigureAwait(false);
                return true;
            }

            if (path == "/v1/rerank")
            {
                await HandleVoyageRerankAsync(context, requestBody).ConfigureAwait(false);
                return true;
            }

            if (path.StartsWith("/model/", StringComparison.OrdinalIgnoreCase)
                && path.Contains("rerank", StringComparison.OrdinalIgnoreCase)
                && path.EndsWith("/invoke", StringComparison.OrdinalIgnoreCase))
            {
                await HandleBedrockRerankAsync(context, requestBody).ConfigureAwait(false);
                return true;
            }

            return false;
        }

        #region Cohere

        private static bool IsCohereRequest(HttpListenerContext context)
        {
            string? authorization = context.Request.Headers["Authorization"];
            return string.Equals(authorization, "Bearer " + CohereTestKey, StringComparison.Ordinal);
        }

        private static async Task<bool> TryHandleCohereAsync(HttpListenerContext context, string path, string requestBody)
        {
            if (path == "/v2/chat")
            {
                await HandleCohereChatAsync(context, requestBody).ConfigureAwait(false);
                return true;
            }

            if (path == "/v2/embed")
            {
                await HandleCohereEmbedAsync(context, requestBody).ConfigureAwait(false);
                return true;
            }

            if (path == "/v2/rerank")
            {
                await HandleCohereRerankAsync(context, requestBody).ConfigureAwait(false);
                return true;
            }

            if (path == "/v1/classify")
            {
                await HandleCohereClassifyAsync(context, requestBody).ConfigureAwait(false);
                return true;
            }

            if (path == "/v1/models")
            {
                string? pageToken = context.Request.QueryString["page_token"];
                if (string.IsNullOrEmpty(pageToken))
                {
                    await WriteJsonAsync(
                        context,
                        200,
                        "{\"models\":[{\"name\":\"command-a-03-2025\",\"endpoints\":[\"chat\"],\"finetuned\":false,\"context_length\":256000,\"features\":[\"tools\",\"strict_tools\"]},{\"name\":\"embed-v4.0\",\"endpoints\":[\"embed\"],\"finetuned\":false,\"context_length\":128000}],\"next_page_token\":\"page2\"}").ConfigureAwait(false);
                }
                else
                {
                    await WriteJsonAsync(
                        context,
                        200,
                        "{\"models\":[{\"name\":\"rerank-v3.5\",\"endpoints\":[\"rerank\"],\"finetuned\":false,\"context_length\":4096}]}").ConfigureAwait(false);
                }
                return true;
            }

            if (path.StartsWith("/v1/models/", StringComparison.Ordinal))
            {
                string name = Uri.UnescapeDataString(path.Substring("/v1/models/".Length));
                if (name.Contains("missing", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteJsonAsync(context, 404, "{\"message\":\"model not found\"}").ConfigureAwait(false);
                }
                else
                {
                    await WriteJsonAsync(
                        context,
                        200,
                        "{\"name\":\"" + name + "\",\"endpoints\":[\"chat\",\"embed\"],\"finetuned\":true,\"context_length\":256000,\"features\":[\"tools\"]}").ConfigureAwait(false);
                }
                return true;
            }

            return false;
        }

        private static async Task HandleCohereChatAsync(HttpListenerContext context, string requestBody)
        {
            LocalJson body = LocalJson.Parse(requestBody);
            bool stream = body.Bool("stream") == true;
            bool hasTools = body.Count("tools") > 0;
            bool hasToolResult = requestBody.Contains("\"tool_call_id\"", StringComparison.Ordinal);

            if (stream)
            {
                context.Response.StatusCode = 200;
                context.Response.ContentType = "text/event-stream";
                context.Response.SendChunked = true;

                await WriteSseAsync(context, "message-start", "{\"type\":\"message-start\",\"id\":\"co-stream\",\"delta\":{\"message\":{\"role\":\"assistant\"}}}").ConfigureAwait(false);

                if (requestBody.Contains("slowstream", StringComparison.OrdinalIgnoreCase))
                {
                    await Task.Delay(3000).ConfigureAwait(false);
                    try { context.Response.Close(); } catch { }
                    return;
                }

                if (hasTools && !hasToolResult)
                {
                    await WriteSseAsync(context, "tool-plan-delta", "{\"type\":\"tool-plan-delta\",\"delta\":{\"message\":{\"tool_plan\":\"I will \"}}}").ConfigureAwait(false);
                    await WriteSseAsync(context, "tool-plan-delta", "{\"type\":\"tool-plan-delta\",\"delta\":{\"message\":{\"tool_plan\":\"check.\"}}}").ConfigureAwait(false);
                    await WriteSseAsync(context, "tool-call-start", "{\"type\":\"tool-call-start\",\"index\":0,\"delta\":{\"message\":{\"tool_calls\":{\"id\":\"call-weather-1\",\"type\":\"function\",\"function\":{\"name\":\"get_weather\",\"arguments\":\"\"}}}}}").ConfigureAwait(false);
                    await WriteSseAsync(context, "tool-call-delta", "{\"type\":\"tool-call-delta\",\"index\":0,\"delta\":{\"message\":{\"tool_calls\":{\"function\":{\"arguments\":\"{\\\"city\\\":\"}}}}}").ConfigureAwait(false);
                    await WriteSseAsync(context, "tool-call-delta", "{\"type\":\"tool-call-delta\",\"index\":0,\"delta\":{\"message\":{\"tool_calls\":{\"function\":{\"arguments\":\"\\\"Seattle\\\"}\"}}}}}").ConfigureAwait(false);
                    await WriteSseAsync(context, "tool-call-end", "{\"type\":\"tool-call-end\",\"index\":0}").ConfigureAwait(false);
                    await WriteSseAsync(context, "message-end", "{\"type\":\"message-end\",\"delta\":{\"finish_reason\":\"TOOL_CALL\",\"usage\":{\"billed_units\":{\"input_tokens\":11,\"output_tokens\":7},\"tokens\":{\"input_tokens\":40,\"output_tokens\":7},\"cached_tokens\":8}}}").ConfigureAwait(false);
                }
                else if (requestBody.Contains("streamerror", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteSseAsync(context, "content-delta", "{\"type\":\"content-delta\",\"index\":0,\"delta\":{\"message\":{\"content\":{\"text\":\"partial\"}}}}").ConfigureAwait(false);
                    await WriteSseAsync(context, "message-end", "{\"type\":\"message-end\",\"delta\":{\"finish_reason\":\"ERROR\"}}").ConfigureAwait(false);
                }
                else
                {
                    if (requestBody.Contains("reasoncapture", StringComparison.OrdinalIgnoreCase))
                    {
                        await WriteSseAsync(context, "content-start", "{\"type\":\"content-start\",\"index\":0,\"delta\":{\"message\":{\"content\":{\"type\":\"thinking\",\"thinking\":\"\"}}}}").ConfigureAwait(false);
                        await WriteSseAsync(context, "content-delta", "{\"type\":\"content-delta\",\"index\":0,\"delta\":{\"message\":{\"content\":{\"thinking\":\"Let me \"}}}}").ConfigureAwait(false);
                        await WriteSseAsync(context, "content-delta", "{\"type\":\"content-delta\",\"index\":0,\"delta\":{\"message\":{\"content\":{\"thinking\":\"think.\"}}}}").ConfigureAwait(false);
                        await WriteSseAsync(context, "content-end", "{\"type\":\"content-end\",\"index\":0}").ConfigureAwait(false);
                    }

                    await WriteSseAsync(context, "content-start", "{\"type\":\"content-start\",\"index\":1,\"delta\":{\"message\":{\"content\":{\"type\":\"text\",\"text\":\"\"}}}}").ConfigureAwait(false);
                    await WriteSseAsync(context, "content-delta", "{\"type\":\"content-delta\",\"index\":1,\"delta\":{\"message\":{\"content\":{\"text\":\"po\"}}}}").ConfigureAwait(false);
                    await WriteSseAsync(context, "content-delta", "{\"type\":\"content-delta\",\"index\":1,\"delta\":{\"message\":{\"content\":{\"text\":\"ng\"}}}}").ConfigureAwait(false);
                    await WriteSseAsync(context, "content-end", "{\"type\":\"content-end\",\"index\":1}").ConfigureAwait(false);
                    await WriteSseAsync(context, "message-end", "{\"type\":\"message-end\",\"delta\":{\"finish_reason\":\"COMPLETE\",\"usage\":{\"billed_units\":{\"input_tokens\":3,\"output_tokens\":2},\"tokens\":{\"input_tokens\":12,\"output_tokens\":2},\"cached_tokens\":4}}}").ConfigureAwait(false);
                }

                context.Response.Close();
                return;
            }

            if (requestBody.Contains("nomessage", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(context, 200, "{\"id\":\"co-empty\",\"finish_reason\":\"COMPLETE\"}").ConfigureAwait(false);
                return;
            }

            if (hasTools && hasToolResult)
            {
                await WriteJsonAsync(
                    context,
                    200,
                    "{\"id\":\"co-final\",\"finish_reason\":\"COMPLETE\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"Seattle is 72 F and clear.\"}]},\"usage\":{\"billed_units\":{\"input_tokens\":20,\"output_tokens\":5},\"tokens\":{\"input_tokens\":60,\"output_tokens\":5}}}").ConfigureAwait(false);
                return;
            }

            if (hasTools)
            {
                string arguments = requestBody.Contains("badjsonargs", StringComparison.OrdinalIgnoreCase)
                    ? "{\\\"city\\\":"
                    : "{\\\"city\\\":\\\"Seattle\\\",\\\"unit\\\":\\\"fahrenheit\\\"}";

                await WriteJsonAsync(
                    context,
                    200,
                    "{\"id\":\"co-tool\",\"finish_reason\":\"TOOL_CALL\",\"message\":{\"role\":\"assistant\",\"tool_plan\":\"I will look up the weather.\",\"tool_calls\":[{\"id\":\"call-weather-1\",\"type\":\"function\",\"function\":{\"name\":\"get_weather\",\"arguments\":\"" + arguments + "\"}}]},\"usage\":{\"billed_units\":{\"input_tokens\":11,\"output_tokens\":7},\"tokens\":{\"input_tokens\":40,\"output_tokens\":7},\"cached_tokens\":8}}").ConfigureAwait(false);
                return;
            }

            if (requestBody.Contains("reasoncapture", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(
                    context,
                    200,
                    "{\"id\":\"co-reason\",\"finish_reason\":\"COMPLETE\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"thinking\",\"thinking\":\"Let me think.\"},{\"type\":\"text\",\"text\":\"pong\"}]},\"usage\":{\"tokens\":{\"input_tokens\":12,\"output_tokens\":9}}}").ConfigureAwait(false);
                return;
            }

            if (requestBody.Contains("billedonly", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(
                    context,
                    200,
                    "{\"id\":\"co-billed\",\"finish_reason\":\"COMPLETE\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"pong\"}]},\"usage\":{\"billed_units\":{\"input_tokens\":5.0,\"output_tokens\":6.0}}}").ConfigureAwait(false);
                return;
            }

            await WriteJsonAsync(
                context,
                200,
                "{\"id\":\"co-chat\",\"finish_reason\":\"COMPLETE\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"pong\"}]},\"usage\":{\"billed_units\":{\"input_tokens\":3,\"output_tokens\":2},\"tokens\":{\"input_tokens\":12,\"output_tokens\":2},\"cached_tokens\":0}}").ConfigureAwait(false);
        }

        private static async Task HandleCohereEmbedAsync(HttpListenerContext context, string requestBody)
        {
            if (requestBody.Contains("embedfail", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(context, 400, "{\"message\":\"invalid request: input_type is required\"}").ConfigureAwait(false);
                return;
            }

            LocalJson body = LocalJson.Parse(requestBody);
            int count = Math.Max(0, body.Count("texts"));
            string type = body.Str("embedding_types.0") ?? "float";

            StringBuilder vectors = new StringBuilder("[");
            for (int i = 0; i < count; i++)
            {
                if (i > 0) vectors.Append(',');
                if (type == "float")
                    vectors.Append('[').Append(Num(i * 3 + 1)).Append(',').Append(Num(i * 3 + 2)).Append(',').Append(Num(i * 3 + 3)).Append(']');
                else
                    vectors.Append('[').Append(10 + i).Append(',').Append(20 + i).Append(',').Append(30 + i).Append(']');
            }
            vectors.Append(']');

            await WriteJsonAsync(
                context,
                200,
                "{\"id\":\"co-embed\",\"embeddings\":{\"" + type + "\":" + vectors + "},\"texts\":[],\"meta\":{\"billed_units\":{\"input_tokens\":4}}}").ConfigureAwait(false);
        }

        private static async Task HandleCohereRerankAsync(HttpListenerContext context, string requestBody)
        {
            if (requestBody.Contains("rerankfail", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(context, 429, "{\"message\":\"too many requests\"}").ConfigureAwait(false);
                return;
            }

            LocalJson body = LocalJson.Parse(requestBody);
            string results = requestBody.Contains("emptyresults", StringComparison.OrdinalIgnoreCase)
                ? "[]"
                : BuildUnsortedResults(body, "documents", "relevance_score", requestBody.Contains("outofrange", StringComparison.OrdinalIgnoreCase));

            await WriteJsonAsync(
                context,
                200,
                "{\"id\":\"co-rerank\",\"results\":" + results + ",\"meta\":{\"api_version\":{\"version\":\"2\"},\"billed_units\":{\"search_units\":1},\"tokens\":{\"input_tokens\":42,\"output_tokens\":0}}}").ConfigureAwait(false);
        }

        private static async Task HandleCohereClassifyAsync(HttpListenerContext context, string requestBody)
        {
            if (requestBody.Contains("classifyfail", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(context, 400, "{\"message\":\"examples are required when no fine-tuned model is given\"}").ConfigureAwait(false);
                return;
            }

            LocalJson body = LocalJson.Parse(requestBody);
            int count = Math.Max(0, body.Count("inputs"));

            StringBuilder classifications = new StringBuilder("[");
            for (int i = 0; i < count; i++)
            {
                string input = body.Str("inputs." + i) ?? string.Empty;
                if (i > 0) classifications.Append(',');

                if (input.Contains("nolabels", StringComparison.OrdinalIgnoreCase))
                {
                    classifications.Append("{\"id\":\"c" + i + "\",\"input\":\"" + input + "\",\"prediction\":\"neutral\",\"confidence\":0.5,\"classification_type\":\"single-label\"}");
                    continue;
                }

                bool positive = input.Contains("great", StringComparison.OrdinalIgnoreCase);
                string top = positive ? "positive" : "negative";
                string posScore = positive ? "0.9" : "0.2";
                string negScore = positive ? "0.1" : "0.8";

                classifications.Append("{\"id\":\"c" + i + "\",\"input\":\"" + input + "\",\"prediction\":\"" + top + "\",\"predictions\":[\"" + top + "\"],\"confidence\":" + (positive ? posScore : negScore)
                    + ",\"labels\":{\"negative\":{\"confidence\":" + negScore + "},\"positive\":{\"confidence\":" + posScore + "}},\"classification_type\":\"single-label\"}");
            }
            classifications.Append(']');

            await WriteJsonAsync(
                context,
                200,
                "{\"id\":\"co-classify\",\"classifications\":" + classifications + ",\"meta\":{\"billed_units\":{\"classifications\":" + count + "}}}").ConfigureAwait(false);
        }

        #endregion

        #region TEI

        private static async Task HandleTeiAsync(HttpListenerContext context, string path, string requestBody)
        {
            if (path == "/health")
            {
                await WriteJsonAsync(context, 200, string.Empty).ConfigureAwait(false);
                return;
            }

            if (path == "/info")
            {
                await WriteJsonAsync(
                    context,
                    200,
                    "{\"model_id\":\"BAAI/bge-reranker-base\",\"model_sha\":null,\"model_dtype\":\"float16\",\"served_model_name\":\"bge-reranker\",\"model_type\":{\"reranker\":{\"id2label\":{\"0\":\"LABEL_0\"},\"label2id\":{\"LABEL_0\":0}}},\"max_concurrent_requests\":512,\"max_input_length\":512,\"max_batch_tokens\":16384,\"max_client_batch_size\":32,\"auto_truncate\":false,\"tokenization_workers\":4,\"version\":\"1.8.0\"}").ConfigureAwait(false);
                return;
            }

            if (await TryWriteTeiErrorAsync(context, requestBody).ConfigureAwait(false)) return;

            LocalJson body = LocalJson.Parse(requestBody);

            if (path == "/embed")
            {
                int count = Math.Max(0, body.Count("inputs"));
                StringBuilder vectors = new StringBuilder("[");
                for (int i = 0; i < count; i++)
                {
                    if (i > 0) vectors.Append(',');
                    vectors.Append('[').Append(Num(0.1 + i)).Append(',').Append(Num(0.2 + i)).Append(',').Append(Num(0.3 + i)).Append(']');
                }
                vectors.Append(']');
                await WriteJsonAsync(context, 200, vectors.ToString()).ConfigureAwait(false);
                return;
            }

            if (path == "/rerank")
            {
                string results = requestBody.Contains("emptyresults", StringComparison.OrdinalIgnoreCase)
                    ? "[]"
                    : BuildUnsortedResults(body, "texts", "score", requestBody.Contains("outofrange", StringComparison.OrdinalIgnoreCase));
                await WriteJsonAsync(context, 200, results).ConfigureAwait(false);
                return;
            }

            if (path == "/predict")
            {
                int count = Math.Max(0, body.Count("inputs"));
                StringBuilder predictions = new StringBuilder("[");
                for (int i = 0; i < count; i++)
                {
                    string input = body.Str("inputs." + i + ".0") ?? string.Empty;
                    bool positive = input.Contains("great", StringComparison.OrdinalIgnoreCase);
                    if (i > 0) predictions.Append(',');
                    predictions.Append("[{\"label\":\"negative\",\"score\":" + (positive ? "0.1" : "0.8") + "},{\"label\":\"positive\",\"score\":" + (positive ? "0.9" : "0.2") + "}]");
                }
                predictions.Append(']');
                await WriteJsonAsync(context, 200, predictions.ToString()).ConfigureAwait(false);
                return;
            }

            // /embed_sparse
            int sparseCount = Math.Max(0, body.Count("inputs"));
            StringBuilder sparse = new StringBuilder("[");
            for (int i = 0; i < sparseCount; i++)
            {
                if (i > 0) sparse.Append(',');
                sparse.Append("[{\"index\":" + (10 + i) + ",\"value\":0.5},{\"index\":" + (200 + i) + ",\"value\":0.25}]");
            }
            sparse.Append(']');
            await WriteJsonAsync(context, 200, sparse.ToString()).ConfigureAwait(false);
        }

        private static async Task<bool> TryWriteTeiErrorAsync(HttpListenerContext context, string requestBody)
        {
            if (requestBody.Contains("teifail413", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(context, 413, "{\"error\":\"batch size 64 > maximum allowed batch size 32\",\"error_type\":\"Validation\"}").ConfigureAwait(false);
                return true;
            }

            if (requestBody.Contains("teifail422", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(context, 422, "{\"error\":\"Input validation error: inputs must have less than 512 tokens\",\"error_type\":\"Tokenizer\"}").ConfigureAwait(false);
                return true;
            }

            if (requestBody.Contains("teifail424", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(context, 424, "{\"error\":\"model is not a re-ranker model\",\"error_type\":\"Backend\"}").ConfigureAwait(false);
                return true;
            }

            if (requestBody.Contains("teifail429", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(context, 429, "{\"error\":\"Model is overloaded\",\"error_type\":\"Overloaded\"}").ConfigureAwait(false);
                return true;
            }

            return false;
        }

        #endregion

        #region VoyageAI-and-Bedrock

        private static async Task HandleVoyageRerankAsync(HttpListenerContext context, string requestBody)
        {
            if (requestBody.Contains("rerankfail", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(context, 400, "{\"detail\":\"invalid rerank request\"}").ConfigureAwait(false);
                return;
            }

            LocalJson body = LocalJson.Parse(requestBody);
            int count = Math.Max(0, body.Count("documents"));
            int? topK = body.Int("top_k");

            // VoyageAI returns results sorted by relevance and applies top_k server-side.
            List<KeyValuePair<int, double>> scored = new List<KeyValuePair<int, double>>();
            for (int i = 0; i < count; i++)
            {
                scored.Add(new KeyValuePair<int, double>(i, ScoreDocument(body.Str("documents." + i), i)));
            }

            List<KeyValuePair<int, double>> ordered = scored.OrderByDescending(p => p.Value).ToList();
            if (topK.HasValue) ordered = ordered.Take(topK.Value).ToList();

            string data = "[" + string.Join(",", ordered.Select(p => "{\"index\":" + p.Key + ",\"relevance_score\":" + Num(p.Value) + "}")) + "]";

            await WriteJsonAsync(
                context,
                200,
                "{\"object\":\"list\",\"data\":" + data + ",\"model\":\"" + (body.Str("model") ?? "rerank-2.5") + "\",\"usage\":{\"total_tokens\":17}}").ConfigureAwait(false);
        }

        private static async Task HandleBedrockRerankAsync(HttpListenerContext context, string requestBody)
        {
            if (requestBody.Contains("rerankfail", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(context, 400, "{\"message\":\"ValidationException: invalid rerank request\"}").ConfigureAwait(false);
                return;
            }

            LocalJson body = LocalJson.Parse(requestBody);
            string results = BuildUnsortedResults(body, "documents", "relevance_score", false);
            await WriteJsonAsync(context, 200, "{\"results\":" + results + "}").ConfigureAwait(false);
        }

        #endregion

        #region Helpers

        // Scores documents so that any document containing "relevant" ranks first, and returns them in input
        // order (deliberately unsorted) so clients must sort. An optional out-of-range index proves that
        // clients drop results that do not map back to an input document.
        private static string BuildUnsortedResults(LocalJson body, string documentsKey, string scoreKey, bool includeOutOfRange)
        {
            int count = Math.Max(0, body.Count(documentsKey));
            List<string> items = new List<string>();
            for (int i = 0; i < count; i++)
            {
                double score = ScoreDocument(body.Str(documentsKey + "." + i), i);
                items.Add("{\"index\":" + i + ",\"" + scoreKey + "\":" + Num(score) + "}");
            }

            if (includeOutOfRange) items.Add("{\"index\":99,\"" + scoreKey + "\":0.99}");

            return "[" + string.Join(",", items) + "]";
        }

        private static double ScoreDocument(string? document, int index)
        {
            if (document != null && document.Contains("relevant", StringComparison.OrdinalIgnoreCase)) return 0.95;
            return 0.1 + (0.01 * index);
        }

        private static string Num(double value)
        {
            return value.ToString("0.0###", CultureInfo.InvariantCulture);
        }

        private static async Task WriteSseAsync(HttpListenerContext context, string eventType, string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes("event: " + eventType + "\ndata: " + json + "\n\n");
            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            await context.Response.OutputStream.FlushAsync().ConfigureAwait(false);
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

        #endregion
    }
}
