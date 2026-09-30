namespace Test.Shared
{
    using System.Text.Json;
    using PolyPrompt.Auth;
    using PolyPrompt.Clients;
    using PolyPrompt.Helpers;
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using Touchstone.Core;

    /// <summary>
    /// Deterministic local cases for Gemini tool calling: thought signatures (native and OpenAI-compatible, streaming and
    /// non-streaming), tool-result roles, tool-result and argument shapes, tool schema handling, and function call ids.
    /// Most cases run against <see cref="LocalGeminiToolRoutes"/>, a strict mock that rejects what Gemini 3 rejects, so a
    /// passing follow-up request is itself proof that the replayed history is valid. Registered into the local behavior suite.
    /// </summary>
    internal static class LocalGeminiToolCases
    {
        public static IEnumerable<TestCaseDescriptor> Create(string suiteId)
        {
            return new List<TestCaseDescriptor>
            {
                Case(suiteId, "gemini_strict_fixture_rejects_legacy_requests", "The strict Gemini fixture rejects the request shapes PolyPrompt 2.7.1 sent", RunStrictFixtureAsync),
                Case(suiteId, "gemini_thought_signature_roundtrip", "Gemini thought signatures are captured and replayed beside functionCall", RunGeminiSignatureRoundTripAsync),
                Case(suiteId, "gemini_thought_signature_streaming", "Gemini streaming captures and replays thought signatures", RunGeminiSignatureStreamingAsync),
                Case(suiteId, "vertex_thought_signature_roundtrip", "Vertex AI captures and replays thought signatures", RunVertexSignatureRoundTripAsync),
                Case(suiteId, "gemini_thought_signature_sentinel", "Unsigned replayed history gets the skip_thought_signature_validator sentinel on its first call only", RunGeminiSignatureSentinelAsync),
                Case(suiteId, "gemini_tool_result_role_user", "Gemini tool results are sent in user turns, with parallel results merged", RunGeminiToolResultRoleAsync),
                Case(suiteId, "gemini_tool_result_shapes", "Gemini tool results of every JSON shape and plain text never throw and produce an object", RunGeminiToolResultShapesAsync),
                Case(suiteId, "tool_arguments_malformed_no_throw", "Malformed or non-object tool arguments become an empty object on every provider", RunMalformedArgumentsAsync),
                Case(suiteId, "gemini_tool_schema_json_schema", "Gemini sends tool schemas in parametersJsonSchema by default", RunGeminiSchemaJsonSchemaAsync),
                Case(suiteId, "gemini_tool_schema_openapi_subset", "Gemini OpenApiSubset mode sanitizes tool schemas for the parameters field", RunGeminiSchemaOpenApiSubsetAsync),
                Case(suiteId, "gemini_function_call_ids", "Gemini function call ids are used when present and synthetic ids are never sent", RunGeminiFunctionCallIdsAsync),
                Case(suiteId, "openai_compat_thought_signature", "OpenAI client reads and replays Gemini extra_content thought signatures", RunOpenAiCompatSignatureAsync),
                Case(suiteId, "openai_compat_thought_signature_streaming", "OpenAI streaming keeps the first thought signature across deltas", RunOpenAiCompatSignatureStreamingAsync),
                Case(suiteId, "thought_signature_other_providers_unchanged", "Requests without a signature are unchanged and non-Gemini providers never send one", RunOtherProvidersUnchangedAsync),
                Case(suiteId, "serializer_behavior", "The built-in serializer matches the behavior PolyPrompt relies on", RunSerializerBehaviorAsync),
            };
        }

        private static TestCaseDescriptor Case(string suiteId, string caseId, string displayName, Func<CancellationToken, Task> executeAsync)
        {
            return new TestCaseDescriptor(suiteId, caseId, displayName, executeAsync, new[] { "local" });
        }

        #region Fixture

        private static Task RunStrictFixtureAsync(CancellationToken token)
        {
            // The exact shape 2.7.1 sent for a follow-up turn: role "function" and no thought signature.
            string legacy = "{\"contents\":[{\"role\":\"user\",\"parts\":[{\"text\":\"hi\"}]},"
                + "{\"role\":\"model\",\"parts\":[{\"functionCall\":{\"name\":\"get_weather\",\"args\":{}}}]},"
                + "{\"role\":\"function\",\"parts\":[{\"functionResponse\":{\"name\":\"get_weather\",\"response\":{\"a\":1}}}]}]}";
            SharedAssert.True(Validate(legacy) != null, "Fixture should reject the 2.7.1 follow-up request.");

            string functionRole = legacy.Replace("\"args\":{}}}", "\"args\":{}},\"thoughtSignature\":\"x\"}");
            SharedAssert.True(Validate(functionRole)?.Contains("valid role") == true, "Fixture should reject the function role. " + Validate(functionRole));

            string unsigned = legacy.Replace("\"role\":\"function\"", "\"role\":\"user\"");
            SharedAssert.True(Validate(unsigned)?.Contains("thought_signature") == true, "Fixture should reject an unsigned function call.");

            string signed = functionRole.Replace("\"role\":\"function\"", "\"role\":\"user\"");
            SharedAssert.True(Validate(signed) == null, "Fixture should accept a signed call with a user-role result. " + Validate(signed));

            string arrayResponse = signed.Replace("\"response\":{\"a\":1}", "\"response\":[1]");
            SharedAssert.True(Validate(arrayResponse)?.Contains("Struct") == true, "Fixture should reject a non-object function response.");

            string badSchema = "{\"tools\":[{\"functionDeclarations\":[{\"name\":\"t\",\"parameters\":{\"type\":\"object\",\"properties\":{\"a\":{\"type\":\"string\",\"mux_runtime_context\":{}}}}}]}],"
                + "\"contents\":[{\"role\":\"user\",\"parts\":[{\"text\":\"hi\"}]}]}";
            SharedAssert.True(Validate(badSchema)?.Contains("mux_runtime_context") == true, "Fixture should reject unknown keys in parameters.");

            string jsonSchema = badSchema.Replace("\"parameters\"", "\"parametersJsonSchema\"");
            SharedAssert.True(Validate(jsonSchema) == null, "Fixture should accept any JSON Schema in parametersJsonSchema.");

            return Task.CompletedTask;
        }

        private static string? Validate(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return LocalGeminiToolRoutes.ValidateNativeRequest(document.RootElement);
        }

        #endregion

        #region Thought-Signatures

        private static async Task RunGeminiSignatureRoundTripAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using GeminiCompletionClient client = CreateGeminiClient(server);
            await AssertNativeSignatureRoundTripAsync(client, server, streaming: false, "Gemini", token).ConfigureAwait(false);
        }

        private static async Task RunGeminiSignatureStreamingAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using GeminiCompletionClient client = CreateGeminiClient(server);
            await AssertNativeSignatureRoundTripAsync(client, server, streaming: true, "Gemini streaming", token).ConfigureAwait(false);
        }

        private static async Task RunVertexSignatureRoundTripAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using VertexAiCompletionClient client = new VertexAiCompletionClient("test-project", "us-central1", new StaticTokenCredential("vertex-token"), endpoint: server.Endpoint);
            client.Model = LocalGeminiToolRoutes.Model;
            await AssertNativeSignatureRoundTripAsync(client, server, streaming: false, "Vertex", token).ConfigureAwait(false);
            await AssertNativeSignatureRoundTripAsync(client, server, streaming: true, "Vertex streaming", token).ConfigureAwait(false);
        }

        private static async Task AssertNativeSignatureRoundTripAsync(
            CompletionClientBase client,
            LocalOpenAiTestServer server,
            bool streaming,
            string label,
            CancellationToken token)
        {
            int before = server.RequestBodies.Count;
            ToolChatRequest request = CreateWeatherRequest();

            List<ToolCall> calls;
            ChatMessage assistant;
            if (streaming)
            {
                ToolChatStreamingResponse stream = await client.ToolChatStreamingAsync(request, token).ConfigureAwait(false);
                SharedAssert.True(stream.Success, label + " tool chat should start. " + stream.Error);
                await foreach (ToolChatStreamingChunk _ in stream.Chunks.ConfigureAwait(false)) { }
                calls = stream.ToolCalls;
                assistant = stream.ToAssistantMessage();
            }
            else
            {
                ToolChatResponse response = await client.ToolChatAsync(request, token).ConfigureAwait(false);
                SharedAssert.True(response.Success, label + " tool chat should succeed. " + response.Error);
                calls = response.ToolCalls;
                assistant = response.ToAssistantMessage();
            }

            SharedAssert.Equal(2, calls.Count, label + " should parse both parallel calls.");
            SharedAssert.Equal(LocalGeminiToolRoutes.Signature, calls[0].ThoughtSignature, label + " should capture the signature beside functionCall.");
            SharedAssert.True(calls[1].ThoughtSignature == null, label + " should not invent a signature for an unsigned parallel call.");
            SharedAssert.Equal("fc-1", calls[0].Id, label + " should use Gemini's function call id.");
            SharedAssert.Equal("fc-2", calls[1].Id, label + " should use Gemini's second function call id.");
            SharedAssert.True(assistant.ToolCalls[0].ThoughtSignature == LocalGeminiToolRoutes.Signature, label + " ToAssistantMessage should keep the signature.");

            request.Messages.Add(assistant);
            request.Messages.Add(ChatMessage.ToolResult(calls[0].Id, "get_weather", "{\"temperature\":72}"));
            request.Messages.Add(ChatMessage.ToolResult(calls[1].Id, "get_weather", "{\"temperature\":55}"));

            string? finalText;
            if (streaming)
            {
                ToolChatStreamingResponse stream = await client.ToolChatStreamingAsync(request, token).ConfigureAwait(false);
                SharedAssert.True(stream.Success, label + " follow-up should be accepted by the strict fixture. " + stream.Error);
                await foreach (ToolChatStreamingChunk _ in stream.Chunks.ConfigureAwait(false)) { }
                finalText = stream.Text;
            }
            else
            {
                ToolChatResponse final = await client.ToolChatAsync(request, token).ConfigureAwait(false);
                SharedAssert.True(final.Success, label + " follow-up should be accepted by the strict fixture. " + final.Error);
                finalText = final.Text;
            }

            SharedAssert.Equal("Answered 2 tool results.", finalText, label + " follow-up should carry both results in one user turn.");

            LocalJson second = LocalJson.Parse(server.RequestBodies[before + 1]);
            SharedAssert.Equal(3, second.Count("contents"), label + " follow-up should have user, model, and one merged user turn.");
            SharedAssert.Equal("user", second.Str("contents.0.role"), label + " first turn should be user.");
            SharedAssert.Equal("model", second.Str("contents.1.role"), label + " assistant turn should be model.");
            SharedAssert.Equal("user", second.Str("contents.2.role"), label + " tool results should be in a user turn.");
            SharedAssert.Equal(LocalGeminiToolRoutes.Signature, second.Str("contents.1.parts.0.thoughtSignature"), label + " signature should be a sibling of functionCall.");
            SharedAssert.False(second.Has("contents.1.parts.0.functionCall.thoughtSignature"), label + " signature must not be nested inside functionCall.");
            SharedAssert.False(second.Has("contents.1.parts.1.thoughtSignature"), label + " unsigned parallel call should be replayed without a signature.");
            SharedAssert.False(server.RequestBodies[before + 1].Contains("skip_thought_signature_validator", StringComparison.Ordinal), label + " a signed turn must not get the sentinel.");
            SharedAssert.Equal("fc-1", second.Str("contents.1.parts.0.functionCall.id"), label + " functionCall id should be replayed.");
            SharedAssert.Equal("fc-1", second.Str("contents.2.parts.0.functionResponse.id"), label + " functionResponse should carry the matching id.");
            SharedAssert.Equal("fc-2", second.Str("contents.2.parts.1.functionResponse.id"), label + " second functionResponse should carry its id.");
            SharedAssert.False(server.RequestBodies[before + 1].Contains("\"role\":\"function\"", StringComparison.Ordinal), label + " must never send the function role.");
        }

        private static async Task RunGeminiSignatureSentinelAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using GeminiCompletionClient client = CreateGeminiClient(server);

            // History from another provider: OpenAI-style ids and no signatures.
            ToolChatRequest request = CreateWeatherRequest();
            request.Messages.Add(ChatMessage.AssistantToolCalls(new List<ToolCall>
            {
                new ToolCall { Id = "call_abc", Name = "get_weather", ArgumentsJson = "{\"city\":\"Seattle\"}" },
                new ToolCall { Id = "call_def", Name = "get_weather", ArgumentsJson = "{\"city\":\"Portland\"}" }
            }));
            request.Messages.Add(ChatMessage.ToolResult("call_abc", "get_weather", "{\"temperature\":72}"));
            request.Messages.Add(ChatMessage.ToolResult("call_def", "get_weather", "{\"temperature\":55}"));

            ToolChatResponse response = await client.ToolChatAsync(request, token).ConfigureAwait(false);
            SharedAssert.True(response.Success, "Unsigned history should be accepted with the sentinel. " + response.Error);

            LocalJson body = LocalJson.Parse(server.RequestBodies[0]);
            SharedAssert.Equal("skip_thought_signature_validator", body.Str("contents.1.parts.0.thoughtSignature"), "First unsigned call should get the sentinel.");
            SharedAssert.False(body.Has("contents.1.parts.1.thoughtSignature"), "Only the first call of the turn should get the sentinel.");
            SharedAssert.Equal("call_abc", body.Str("contents.1.parts.0.functionCall.id"), "Foreign call ids should be replayed as-is.");
            SharedAssert.Equal("call_def", body.Str("contents.2.parts.1.functionResponse.id"), "Foreign result ids should match their calls.");
        }

        #endregion

        #region Roles-and-Results

        private static async Task RunGeminiToolResultRoleAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using GeminiCompletionClient client = CreateGeminiClient(server);

            ToolChatRequest request = CreateWeatherRequest();
            request.Messages.Add(ChatMessage.AssistantToolCalls(new List<ToolCall>
            {
                new ToolCall { Id = "a", Name = "get_weather", ArgumentsJson = "{}", ThoughtSignature = "s1" }
            }));
            request.Messages.Add(new ChatMessage { Role = "function", ToolCallId = "a", ToolName = "get_weather", Content = "{\"ok\":true}" });
            request.Messages.Add(ChatMessage.User("And tomorrow?"));
            request.Messages.Add(ChatMessage.AssistantToolCalls(new List<ToolCall>
            {
                new ToolCall { Id = "b", Name = "get_weather", ArgumentsJson = "{}", ThoughtSignature = "s2" }
            }));
            request.Messages.Add(ChatMessage.ToolResult("b", "get_weather", "{\"ok\":false}"));

            ToolChatResponse response = await client.ToolChatAsync(request, token).ConfigureAwait(false);
            SharedAssert.True(response.Success, "Strict fixture should accept the translated roles. " + response.Error);

            LocalJson body = LocalJson.Parse(server.RequestBodies[0]);
            SharedAssert.Equal(6, body.Count("contents"), "A tool result followed by a user message must not be merged into it.");
            string[] expected = { "user", "model", "user", "user", "model", "user" };
            for (int i = 0; i < expected.Length; i++)
            {
                SharedAssert.Equal(expected[i], body.Str("contents." + i + ".role"), "Content " + i + " role.");
            }
            SharedAssert.True(body.Has("contents.2.parts.0.functionResponse"), "Legacy function-role message should become a user functionResponse.");
            SharedAssert.Equal(1, body.Count("contents.2.parts"), "A lone tool result should stay a single part.");
            SharedAssert.Equal("And tomorrow?", body.Str("contents.3.parts.0.text"), "The user message should keep its own turn.");
        }

        private static async Task RunGeminiToolResultShapesAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using GeminiCompletionClient client = CreateGeminiClient(server);

            (string Content, string Check)[] cases =
            {
                ("{}", "object-empty"),
                ("{\"a\":1}", "object"),
                ("[]", "array-0"),
                ("[1,2]", "array-2"),
                ("[{\"id\":1}]", "array-1"),
                ("42", "number"),
                ("true", "bool"),
                ("\"x\"", "string-x"),
                ("null", "null"),
                ("plain text", "raw"),
                ("{bad json", "raw"),
                ("", "raw"),
                ("   ", "raw"),
            };

            for (int i = 0; i < cases.Length; i++)
            {
                (string content, string check) = cases[i];
                ToolChatRequest request = CreateWeatherRequest();
                request.Messages.Add(ChatMessage.AssistantToolCalls(new List<ToolCall>
                {
                    new ToolCall { Id = "fc-1", Name = "get_weather", ArgumentsJson = "{}", ThoughtSignature = "s" }
                }));
                request.Messages.Add(ChatMessage.ToolResult("fc-1", "get_weather", content));

                string label = "Tool result '" + content + "'";
                ToolChatResponse response = await client.ToolChatAsync(request, token).ConfigureAwait(false);
                SharedAssert.True(response.Success, label + " should be sent and accepted. " + response.Error);

                LocalJson body = LocalJson.Parse(server.RequestBodies[i]);
                string response0 = "contents.2.parts.0.functionResponse.response";
                SharedAssert.Equal("Object", body.Kind(response0), label + " should produce an object response.");

                switch (check)
                {
                    case "object-empty":
                        SharedAssert.False(body.Has(response0 + ".result"), label + " object should not be wrapped.");
                        break;
                    case "object":
                        SharedAssert.Equal(1, body.Int(response0 + ".a"), label + " object should pass through.");
                        SharedAssert.False(body.Has(response0 + ".result"), label + " object should not be wrapped.");
                        break;
                    case "array-0":
                        SharedAssert.Equal(0, body.Count(response0 + ".result"), label + " should wrap the array, not stringify it.");
                        break;
                    case "array-2":
                        SharedAssert.Equal(2, body.Count(response0 + ".result"), label + " should wrap the array.");
                        SharedAssert.Equal(2, body.Int(response0 + ".result.1"), label + " should keep array values.");
                        break;
                    case "array-1":
                        SharedAssert.Equal(1, body.Int(response0 + ".result.0.id"), label + " should keep nested structure.");
                        break;
                    case "number":
                        SharedAssert.Equal(42, body.Int(response0 + ".result"), label + " should wrap the number as a number.");
                        break;
                    case "bool":
                        SharedAssert.Equal(true, body.Bool(response0 + ".result"), label + " should wrap the boolean.");
                        break;
                    case "string-x":
                        SharedAssert.Equal("x", body.Str(response0 + ".result"), label + " should wrap the decoded string.");
                        break;
                    case "null":
                        SharedAssert.Equal("Null", body.Kind(response0 + ".result"), label + " should wrap JSON null.");
                        break;
                    case "raw":
                        SharedAssert.Equal("String", body.Kind(response0 + ".result"), label + " should wrap the raw text as a string.");
                        SharedAssert.Equal(content, body.Str(response0 + ".result"), label + " should wrap the raw text unchanged.");
                        break;
                }
            }
        }

        private static async Task RunMalformedArgumentsAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();

            List<(CompletionClientBase Client, string Path)> clients = new List<(CompletionClientBase, string)>
            {
                (CreateGeminiClient(server), "contents.1.parts.0.functionCall.args"),
                (new AnthropicCompletionClient(server.Endpoint, "test-key"), "messages.1.content.0.input"),
                (new OllamaCompletionClient(server.Endpoint), "messages.1.tool_calls.0.function.arguments"),
                (CreateBedrockClient(server), "messages.1.content.0.toolUse.input"),
            };

            string[] malformed = { "[1,2]", "not json", "42", "null", "" };

            try
            {
                foreach ((CompletionClientBase client, string path) in clients)
                {
                    foreach (string arguments in malformed)
                    {
                        int index = server.RequestBodies.Count;
                        ToolChatRequest request = CreateWeatherRequest();
                        request.Messages.RemoveAt(0);
                        request.Messages.Add(ChatMessage.AssistantToolCalls(new List<ToolCall>
                        {
                            new ToolCall { Id = "fc-1", Name = "get_weather", ArgumentsJson = arguments, ThoughtSignature = "s" }
                        }));
                        request.Messages.Add(ChatMessage.ToolResult("fc-1", "get_weather", "{\"ok\":true}"));

                        string label = client.GetType().Name + " arguments '" + arguments + "'";
                        try
                        {
                            await client.ToolChatAsync(request, token).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            throw new TestFailureException(label + " should not throw: " + ex.GetType().Name + ": " + ex.Message);
                        }

                        LocalJson body = LocalJson.Parse(server.RequestBodies[index]);
                        SharedAssert.Equal("Object", body.Kind(path), label + " should be sent as an object.");
                        SharedAssert.Equal("{}", body.Str(path), label + " should be sent as an empty object.");
                    }
                }
            }
            finally
            {
                foreach ((CompletionClientBase client, string _) in clients) client.Dispose();
            }
        }

        #endregion

        #region Schemas

        private static async Task RunGeminiSchemaJsonSchemaAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using GeminiCompletionClient client = CreateGeminiClient(server);
            SharedAssert.True(client.Defaults.ToolSchemaMode == null, "No schema mode is set by default, which means JsonSchema.");

            ToolChatRequest request = CreateWeatherRequest();
            request.Tools.Add(ToolDefinition.Function("messy", "Schema with keywords outside the OpenAPI subset.", MessySchema()));
            request.Tools.Add(ToolDefinition.Function("no_args", "Takes no arguments.", new Dictionary<string, object>()));

            ToolChatResponse response = await client.ToolChatAsync(request, token).ConfigureAwait(false);
            SharedAssert.True(response.Success, "Strict fixture should accept parametersJsonSchema. " + response.Error);

            LocalJson body = LocalJson.Parse(server.RequestBodies[0]);
            string messy = "tools.0.functionDeclarations.1";
            SharedAssert.True(body.Has(messy + ".parametersJsonSchema"), "Schema should be sent in parametersJsonSchema.");
            SharedAssert.False(body.Has(messy + ".parameters"), "Schema must not also be sent in parameters.");
            SharedAssert.False(body.Has(messy + ".parametersJsonSchema.$schema"), "Root $schema should be removed.");
            SharedAssert.Equal(false, body.Bool(messy + ".parametersJsonSchema.additionalProperties"), "Standard JSON Schema keywords should be kept.");
            SharedAssert.True(body.Has(messy + ".parametersJsonSchema.properties.mode.mux_runtime_context"), "Unknown keys should pass through unchanged.");
            SharedAssert.Equal("fast", body.Str(messy + ".parametersJsonSchema.properties.mode.const"), "const should be kept in JSON Schema mode.");
            SharedAssert.Equal(2, body.Count(messy + ".parametersJsonSchema.properties.note.type"), "Type arrays should be kept in JSON Schema mode.");
            SharedAssert.False(body.Has("tools.0.functionDeclarations.2.parameters"), "An empty schema should send no parameters.");
            SharedAssert.False(body.Has("tools.0.functionDeclarations.2.parametersJsonSchema"), "An empty schema should send no parametersJsonSchema.");
            SharedAssert.Equal("object", body.Str("tools.0.functionDeclarations.0.parametersJsonSchema.type"), "Clean schemas should be sent as-is.");
        }

        private static async Task RunGeminiSchemaOpenApiSubsetAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using GeminiCompletionClient client = CreateGeminiClient(server);
            client.Defaults.ToolSchemaMode = GeminiToolSchemaMode.OpenApiSubset;

            ToolChatRequest request = CreateWeatherRequest();
            request.Tools.Add(ToolDefinition.Function("messy", "Schema with keywords outside the OpenAPI subset.", MessySchema()));

            ToolChatResponse response = await client.ToolChatAsync(request, token).ConfigureAwait(false);
            SharedAssert.True(response.Success, "Strict fixture should accept the sanitized parameters. " + response.Error);

            LocalJson body = LocalJson.Parse(server.RequestBodies[0]);
            string p = "tools.0.functionDeclarations.1.parameters";
            SharedAssert.False(body.Has("tools.0.functionDeclarations.1.parametersJsonSchema"), "OpenApiSubset mode must not send parametersJsonSchema.");
            SharedAssert.Equal("object", body.Str(p + ".type"), "type should be kept.");
            SharedAssert.Equal("A messy tool.", body.Str(p + ".description"), "description should be kept.");
            SharedAssert.Equal("mode", body.Str(p + ".required.0"), "required should be kept.");
            foreach (string removed in new[] { "$schema", "$id", "$defs", "additionalProperties", "examples", "x-vendor" })
            {
                SharedAssert.False(body.Has(p + "." + removed), removed + " should be removed.");
            }
            SharedAssert.False(body.Has(p + ".properties.mode.mux_runtime_context"), "Nested unknown keys should be removed.");
            SharedAssert.Equal("fast", body.Str(p + ".properties.mode.enum.0"), "const should become a one-value enum.");
            SharedAssert.False(body.Has(p + ".properties.mode.const"), "const itself should be removed.");
            SharedAssert.Equal("string", body.Str(p + ".properties.note.type"), "Type array should collapse to its non-null type.");
            SharedAssert.Equal(true, body.Bool(p + ".properties.note.nullable"), "A null member should become nullable.");
            SharedAssert.Equal(2, body.Count(p + ".properties.value.anyOf"), "oneOf should become anyOf.");
            SharedAssert.Equal("integer", body.Str(p + ".properties.tags.items.type"), "items should be sanitized and kept.");
            SharedAssert.False(body.Has(p + ".properties.tags.items.additionalProperties"), "Keys inside items should be sanitized.");
            SharedAssert.Equal(1, body.Int(p + ".properties.tags.minItems"), "Supported constraints should be kept.");
            SharedAssert.Equal(0, body.Int(p + ".properties.count.minimum"), "minimum should be kept.");
            SharedAssert.Equal("A", body.Str(p + ".properties.level.enum.0"), "String enums should be kept.");
            SharedAssert.Equal("2", body.Str(p + ".properties.level.enum.1"), "Non-string enum values should become strings.");

            // A per-call mode overrides the client default in both directions.
            ToolChatRequest perCall = CreateWeatherRequest();
            perCall.Options = new GeminiCompletionOptions { ToolSchemaMode = GeminiToolSchemaMode.JsonSchema };
            ToolChatResponse perCallResponse = await client.ToolChatAsync(perCall, token).ConfigureAwait(false);
            SharedAssert.True(perCallResponse.Success, "A per-call JsonSchema mode should be accepted. " + perCallResponse.Error);
            LocalJson perCallBody = LocalJson.Parse(server.RequestBodies[1]);
            SharedAssert.True(perCallBody.Has("tools.0.functionDeclarations.0.parametersJsonSchema"), "A per-call JsonSchema mode should override an OpenApiSubset default.");
            SharedAssert.False(perCallBody.Has("tools.0.functionDeclarations.0.parameters"), "A per-call JsonSchema mode should not also send parameters.");
        }

        private static Dictionary<string, object> MessySchema()
        {
            string json = "{\"$schema\":\"http://json-schema.org/draft-07/schema#\",\"$id\":\"messy\",\"type\":\"object\",\"description\":\"A messy tool.\","
                + "\"additionalProperties\":false,\"examples\":[{}],\"x-vendor\":1,\"$defs\":{\"unused\":{\"type\":\"string\"}},"
                + "\"properties\":{"
                + "\"mode\":{\"type\":\"string\",\"const\":\"fast\",\"mux_runtime_context\":{\"a\":1}},"
                + "\"note\":{\"type\":[\"string\",\"null\"]},"
                + "\"value\":{\"oneOf\":[{\"type\":\"string\"},{\"type\":\"number\"}]},"
                + "\"tags\":{\"type\":\"array\",\"minItems\":1,\"items\":{\"type\":\"integer\",\"additionalProperties\":false}},"
                + "\"count\":{\"type\":\"integer\",\"minimum\":0},"
                + "\"level\":{\"type\":\"string\",\"enum\":[\"A\",2]}"
                + "},\"required\":[\"mode\"]}";
            return new Serializer().DeserializeJson<Dictionary<string, object>>(json)!;
        }

        #endregion

        #region Ids

        private static async Task RunGeminiFunctionCallIdsAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using GeminiCompletionClient client = new GeminiCompletionClient(server.Endpoint, "test-key");
            client.Model = "test-model";

            // The default route returns a functionCall without an id, so the client synthesizes one.
            ToolChatRequest request = CreateWeatherRequest();
            ToolChatResponse response = await client.ToolChatAsync(request, token).ConfigureAwait(false);
            SharedAssert.True(response.Success, "Gemini tool chat should succeed. " + response.Error);
            SharedAssert.Equal("gemini-call-0", response.ToolCalls[0].Id, "A missing id should be synthesized.");
            SharedAssert.True(response.ToolCalls[0].ThoughtSignature == null, "A response without a signature should parse to null.");

            request.Messages.Add(response.ToAssistantMessage());
            request.Messages.Add(ChatMessage.ToolResult(response.ToolCalls[0].Id, "get_weather", "{\"ok\":true}"));
            await client.ToolChatAsync(request, token).ConfigureAwait(false);

            LocalJson body = LocalJson.Parse(server.RequestBodies[1]);
            SharedAssert.False(body.Has("contents.1.parts.0.functionCall.id"), "A synthesized id must not be sent as functionCall.id.");
            SharedAssert.False(body.Has("contents.2.parts.0.functionResponse.id"), "A synthesized id must not be sent as functionResponse.id.");
            SharedAssert.Equal("skip_thought_signature_validator", body.Str("contents.1.parts.0.thoughtSignature"), "An unsigned call should get the sentinel.");
        }

        #endregion

        #region OpenAI-Compatible

        private static async Task RunOpenAiCompatSignatureAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiCompletionClient client = new OpenAiCompletionClient(server.Endpoint, "test-key");
            client.Model = LocalGeminiToolRoutes.Model;

            ToolChatRequest request = CreateWeatherRequest();
            ToolChatResponse response = await client.ToolChatAsync(request, token).ConfigureAwait(false);
            SharedAssert.True(response.Success, "OpenAI-compatible tool chat should succeed. " + response.Error);
            SharedAssert.Equal(2, response.ToolCalls.Count, "Both tool calls should parse.");
            SharedAssert.Equal(LocalGeminiToolRoutes.Signature, response.ToolCalls[0].ThoughtSignature, "extra_content signature should be read.");
            SharedAssert.True(response.ToolCalls[1].ThoughtSignature == null, "A call without extra_content should have no signature.");

            request.Messages.Add(response.ToAssistantMessage());
            request.Messages.Add(ChatMessage.ToolResult("call_1", "get_weather", "{\"t\":72}"));
            request.Messages.Add(ChatMessage.ToolResult("call_2", "get_weather", "{\"t\":55}"));

            ToolChatResponse final = await client.ToolChatAsync(request, token).ConfigureAwait(false);
            SharedAssert.True(final.Success, "Follow-up should be accepted by the strict fixture. " + final.Error);
            SharedAssert.Equal("Done.", final.Text, "Follow-up should return the final answer.");

            LocalJson body = LocalJson.Parse(server.RequestBodies[1]);
            SharedAssert.Equal(LocalGeminiToolRoutes.Signature, body.Str("messages.2.tool_calls.0.extra_content.google.thought_signature"), "Signature should be replayed in extra_content.");
            SharedAssert.False(body.Has("messages.2.tool_calls.1.extra_content"), "An unsigned call should be replayed without extra_content.");
        }

        private static async Task RunOpenAiCompatSignatureStreamingAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using OpenAiCompletionClient client = new OpenAiCompletionClient(server.Endpoint, "test-key");
            client.Model = LocalGeminiToolRoutes.Model;

            ToolChatRequest request = CreateWeatherRequest();
            ToolChatStreamingResponse stream = await client.ToolChatStreamingAsync(request, token).ConfigureAwait(false);
            SharedAssert.True(stream.Success, "OpenAI-compatible streaming should start. " + stream.Error);

            bool deltaCarriedSignature = false;
            await foreach (ToolChatStreamingChunk chunk in stream.Chunks.ConfigureAwait(false))
            {
                if (chunk.ToolCallDeltas.Any(delta => delta.ThoughtSignature == LocalGeminiToolRoutes.Signature)) deltaCarriedSignature = true;
            }

            SharedAssert.True(deltaCarriedSignature, "The streaming delta should expose the signature.");
            SharedAssert.Equal(2, stream.ToolCalls.Count, "Both streamed calls should assemble.");
            SharedAssert.Equal("{\"city\":\"Seattle\"}", stream.ToolCalls[0].ArgumentsJson, "Split arguments should reassemble.");
            SharedAssert.Equal(LocalGeminiToolRoutes.Signature, stream.ToolCalls[0].ThoughtSignature, "The first signature should win over a later one.");
            SharedAssert.True(stream.ToolCalls[1].ThoughtSignature == null, "The unsigned call should have no signature.");

            request.Messages.Add(stream.ToAssistantMessage());
            request.Messages.Add(ChatMessage.ToolResult("call_1", "get_weather", "{\"t\":72}"));
            request.Messages.Add(ChatMessage.ToolResult("call_2", "get_weather", "{\"t\":55}"));

            ToolChatStreamingResponse final = await client.ToolChatStreamingAsync(request, token).ConfigureAwait(false);
            SharedAssert.True(final.Success, "Streaming follow-up should be accepted by the strict fixture. " + final.Error);
            await foreach (ToolChatStreamingChunk _ in final.Chunks.ConfigureAwait(false)) { }
            SharedAssert.Equal("Done.", final.Text, "Streaming follow-up should return the final answer.");
        }

        private static async Task RunOtherProvidersUnchangedAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();

            // OpenAI without a signature: the replayed tool call has exactly id, type, and function.
            using (OpenAiCompletionClient openAi = new OpenAiCompletionClient(server.Endpoint, "test-key"))
            {
                openAi.Model = "test-model";
                ToolChatRequest request = CreateWeatherRequest();
                ToolChatResponse response = await openAi.ToolChatAsync(request, token).ConfigureAwait(false);
                SharedAssert.True(response.Success, "OpenAI tool chat should succeed. " + response.Error);
                SharedAssert.True(response.ToolCalls.All(call => call.ThoughtSignature == null), "OpenAI responses without extra_content should have no signature.");

                request.Messages.Add(response.ToAssistantMessage());
                request.Messages.Add(ChatMessage.ToolResult(response.ToolCalls[0].Id, "get_weather", "{\"ok\":true}"));
                int index = server.RequestBodies.Count;
                await openAi.ToolChatAsync(request, token).ConfigureAwait(false);

                string body = server.RequestBodies[index];
                SharedAssert.False(body.Contains("extra_content", StringComparison.Ordinal), "OpenAI request without a signature must not contain extra_content.");
                SharedAssert.False(body.Contains("thought", StringComparison.OrdinalIgnoreCase), "OpenAI request without a signature must not mention thought signatures.");
            }

            // Providers that have no signature concept never send one, even when a ToolCall carries it.
            List<CompletionClientBase> others = new List<CompletionClientBase>
            {
                new AnthropicCompletionClient(server.Endpoint, "test-key"),
                new OllamaCompletionClient(server.Endpoint),
                CreateBedrockClient(server),
                new CohereCompletionClient(server.Endpoint, LocalExtendedRoutes.CohereTestKey),
            };

            try
            {
                foreach (CompletionClientBase client in others)
                {
                    ToolChatRequest request = CreateWeatherRequest();
                    request.Messages.Add(ChatMessage.AssistantToolCalls(new List<ToolCall>
                    {
                        new ToolCall { Id = "call-1", Name = "get_weather", ArgumentsJson = "{\"city\":\"Seattle\"}", ThoughtSignature = "sig-must-not-leak" }
                    }));
                    request.Messages.Add(ChatMessage.ToolResult("call-1", "get_weather", "{\"ok\":true}"));

                    int index = server.RequestBodies.Count;
                    await client.ToolChatAsync(request, token).ConfigureAwait(false);
                    string body = server.RequestBodies[index];
                    SharedAssert.False(body.Contains("sig-must-not-leak", StringComparison.Ordinal), client.GetType().Name + " must not send the thought signature.");
                    SharedAssert.False(body.Contains("extra_content", StringComparison.Ordinal), client.GetType().Name + " must not send extra_content.");
                }
            }
            finally
            {
                foreach (CompletionClientBase client in others) client.Dispose();
            }
        }

        #endregion

        #region Serializer

        private static Task RunSerializerBehaviorAsync(CancellationToken token)
        {
            Serializer serializer = new Serializer();

            Dictionary<string, object>? parsed = serializer.DeserializeJson<Dictionary<string, object>>("{\"a\":1,\"b\":\"x\",\"c\":{\"d\":[1]},\"e\":null,}");
            SharedAssert.NotNull(parsed, "Object JSON should deserialize.");
            SharedAssert.True(parsed!["a"] is JsonElement, "Untyped values should deserialize to JsonElement.");
            SharedAssert.Equal("x", parsed["b"].ToString(), "String values should read back as text.");
            SharedAssert.True(parsed["e"] == null, "JSON null should deserialize to null.");
            SharedAssert.Equal("{\"d\":[1]}", serializer.SerializeJson(parsed["c"], false), "JsonElement values should round-trip unchanged.");
            SharedAssert.Equal(5, serializer.DeserializeJson<Dictionary<string, int>>("{\"a\":\"5\"}")!["a"], "Numbers written as strings should be read.");
            SharedAssert.True(serializer.DeserializeJson<Dictionary<string, object>>("null") == null, "The null literal should deserialize to null.");

            SharedAssert.Equal("{\"a\":null,\"b\":1}", serializer.SerializeJson(new Dictionary<string, object?> { { "a", null }, { "b", 1 } }, false), "Dictionary entries should always be written.");
            SharedAssert.Equal("{\"Id\":\"x\"}", serializer.SerializeJson(new { Id = "x", Name = (string?)null }, false), "Null properties should be omitted.");
            SharedAssert.Equal("{\"Level\":\"High\"}", serializer.SerializeJson(new { Level = ReasoningEffortLevel.High }, false), "Enums should be written as strings.");
            SharedAssert.Equal("null", serializer.SerializeJson(null, false), "A null object should serialize to null.");
            SharedAssert.True(serializer.SerializeJson(new { A = 1 }, true).Contains("\n", StringComparison.Ordinal), "Pretty output should be indented.");

            ToolCall call = new ToolCall { ArgumentsJson = "{\"city\":\"Seattle\"}" };
            SharedAssert.Equal("Seattle", call.DeserializeArguments<Dictionary<string, string>>()!["city"], "ToolCall.DeserializeArguments should work.");

            foreach (string invalid in new[] { "[1]", "42", "plain text", "", "{bad" })
            {
                bool threw = false;
                try { serializer.DeserializeJson<Dictionary<string, object>>(invalid); }
                catch (JsonException) { threw = true; }
                SharedAssert.True(threw, "Deserializing '" + invalid + "' into an object should throw JsonException.");
            }

            bool nullThrew = false;
            try { serializer.DeserializeJson<Dictionary<string, object>>(null!); }
            catch (ArgumentNullException) { nullThrew = true; }
            SharedAssert.True(nullThrew, "A null string should throw ArgumentNullException.");

            return Task.CompletedTask;
        }

        #endregion

        #region Helpers

        private static GeminiCompletionClient CreateGeminiClient(LocalOpenAiTestServer server)
        {
            GeminiCompletionClient client = new GeminiCompletionClient(server.Endpoint, "test-key");
            client.Model = LocalGeminiToolRoutes.Model;
            return client;
        }

        private static BedrockCompletionClient CreateBedrockClient(LocalOpenAiTestServer server)
        {
            BedrockCompletionClient client = new BedrockCompletionClient(
                new StaticAwsCredential("AKIDTESTEXAMPLE", "wJalrXUtnFEMI/K7MDENG+bPxRfiCYEXAMPLEKEY", "us-east-1"),
                "us-east-1",
                endpoint: server.Endpoint);
            client.TimeoutMs = 2000;
            return client;
        }

        private static ToolChatRequest CreateWeatherRequest()
        {
            ToolChatRequest request = new ToolChatRequest();
            request.Messages.Add(ChatMessage.System("Answer with weather guidance."));
            request.Messages.Add(ChatMessage.User("What is the weather in Seattle and Portland?"));
            request.Tools.Add(ToolDefinition.Function(
                "get_weather",
                "Get current weather for a city.",
                new Dictionary<string, object>
                {
                    { "type", "object" },
                    { "properties", new Dictionary<string, object> { { "city", new Dictionary<string, object> { { "type", "string" } } } } },
                    { "required", new List<string> { "city" } }
                }));
            return request;
        }

        #endregion
    }
}
