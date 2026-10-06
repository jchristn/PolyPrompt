namespace Test.Shared
{
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Text.Json.Serialization;
    using System.Text.Json.Serialization.Metadata;
    using PolyPrompt.Helpers;
    using PolyPrompt.Models;
    using Touchstone.Core;

    /// <summary>
    /// Deterministic cases for the AOT-safe serializer: output parity with the previous reflection serializer, the
    /// public <see cref="PolyPromptJsonContext"/>, explicit type metadata overloads, registered resolvers, tolerant
    /// reads, and the failure modes (null arguments, invalid JSON, cycles, depth, non-finite numbers). Registered into
    /// the local behavior suite. Native AOT behavior itself is verified by the Test.Aot project.
    /// </summary>
    internal static class LocalSerializerCases
    {
        public static IEnumerable<TestCaseDescriptor> Create(string suiteId)
        {
            return new List<TestCaseDescriptor>
            {
                Case(suiteId, "ser_wire_graph_parity", "Dictionary and list graphs serialize byte-for-byte like the previous reflection serializer, compact and indented", RunWireGraphParityAsync),
                Case(suiteId, "ser_scalar_parity", "Scalars, enums, flags, undefined enum values, dates, GUIDs, byte arrays, and escaping match the previous serializer", RunScalarParityAsync),
                Case(suiteId, "ser_untyped_reads", "Untyped values deserialize to JsonElement and reads tolerate trailing commas, comments, and quoted numbers", RunUntypedReadsAsync),
                Case(suiteId, "ser_context_round_trip", "PolyPromptJsonContext round-trips conversations, including tool calls and thought signatures", RunContextRoundTripAsync),
                Case(suiteId, "ser_context_settings", "PolyPromptJsonContext omits nulls, writes enums as strings and dates as UTC with microseconds", RunContextSettingsAsync),
                Case(suiteId, "ser_typed_overloads", "SerializeJson and DeserializeJson overloads that take JsonTypeInfo honor the metadata and the pretty flag", RunTypedOverloadsAsync),
                Case(suiteId, "ser_tool_arguments_typed", "ToolCall.DeserializeArguments works with and without explicit type metadata", RunToolArgumentsTypedAsync),
                Case(suiteId, "ser_registered_resolver", "A registered resolver takes precedence over reflection, and registering it twice is harmless", RunRegisteredResolverAsync),
                Case(suiteId, "ser_null_arguments", "Null JSON, type metadata, and resolvers are rejected with ArgumentNullException", RunNullArgumentsAsync),
                Case(suiteId, "ser_invalid_json", "Invalid JSON and JSON that does not match the target type raise JsonException", RunInvalidJsonAsync),
                Case(suiteId, "ser_cycle_and_depth", "Graphs up to the maximum depth serialize; deeper and cyclic graphs raise JsonException instead of overflowing", RunCycleAndDepthAsync),
                Case(suiteId, "ser_non_finite_numbers", "NaN and infinity are rejected like the previous serializer", RunNonFiniteNumbersAsync),
            };
        }

        private static TestCaseDescriptor Case(string suiteId, string caseId, string displayName, Func<CancellationToken, Task> executeAsync)
        {
            return new TestCaseDescriptor(suiteId, caseId, displayName, executeAsync, new[] { "local" });
        }

        #region Parity

        private static Task RunWireGraphParityAsync(CancellationToken token)
        {
            Serializer serializer = new Serializer();

            Dictionary<string, object> graph = new Dictionary<string, object>
            {
                { "model", "test-model" },
                { "stream", false },
                { "max_tokens", 256 },
                { "temperature", 0.25 },
                { "top_p", 0.9f },
                { "seed", 9007199254740993L },
                { "price", 1.10m },
                { "stop", new[] { "\n\n", "END" } },
                { "missing", null! },
                { "messages", new List<Dictionary<string, object>>
                    {
                        new Dictionary<string, object> { { "role", "system" }, { "content", "Be brief." } },
                        new Dictionary<string, object>
                        {
                            { "role", "assistant" },
                            { "tool_calls", new List<object>
                                {
                                    new Dictionary<string, object>
                                    {
                                        { "id", "call_1" },
                                        { "function", new Dictionary<string, object> { { "name", "get_weather" }, { "arguments", "{\"city\":\"Paris\"}" } } }
                                    }
                                }
                            }
                        }
                    }
                },
                { "embedding", new List<float> { 0.1f, -0.2f, 3.5e-8f } },
                { "vectors", new float[][] { new[] { 1f, 2f }, new[] { 3f } } },
                { "counts", new Dictionary<int, string> { { 1, "one" }, { -2, "minus two" } } },
                { "days", new Dictionary<DayOfWeek, int> { { DayOfWeek.Monday, 1 } } },
                { "schema", JsonDocument.Parse("{\"type\":\"object\",\"properties\":{\"city\":{\"type\":\"string\"}},\"required\":[\"city\"]}").RootElement.Clone() },
                { "node", JsonNode.Parse("{\"a\":[1,2,{\"b\":null}]}")! },
                { "poco", new { name = "anonymous", nested = new { value = 3, skipped = (string?)null } } },
                { "empty_object", new Dictionary<string, object>() },
                { "empty_array", new List<object>() },
            };

            foreach (bool pretty in new[] { false, true })
            {
                string expected = JsonSerializer.Serialize(graph, graph.GetType(), LegacyOptions(pretty));
                string actual = serializer.SerializeJson(graph, pretty);
                SharedAssert.Equal(expected, actual, "Graph output should match the previous serializer (pretty=" + pretty + ").");
            }

            return Task.CompletedTask;
        }

        private static Task RunScalarParityAsync(CancellationToken token)
        {
            Serializer serializer = new Serializer();
            object?[] values = new object?[]
            {
                null,
                "plain",
                "escape <html> & 'quotes' \"double\" \\ \u00e9 \u2028 \t",
                true,
                false,
                0,
                int.MinValue,
                long.MaxValue,
                ulong.MaxValue,
                (short)-3,
                (byte)7,
                (sbyte)-7,
                (ushort)9,
                (uint)10,
                1.5,
                -0.0,
                double.MaxValue,
                float.Epsilon,
                123456.789m,
                'x',
                DayOfWeek.Friday,
                (DayOfWeek)42,
                FileAttributes.ReadOnly | FileAttributes.Hidden,
                new DateTime(2026, 10, 6, 12, 30, 45, 123, DateTimeKind.Utc).AddTicks(4560),
                new DateTime(2026, 10, 6, 12, 30, 45, DateTimeKind.Unspecified),
                new DateTimeOffset(2026, 10, 6, 12, 30, 45, TimeSpan.FromHours(-7)),
                TimeSpan.FromMinutes(90.5),
                Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"),
                new byte[] { 0, 1, 2, 250, 255 },
                new int[0],
                new List<string?> { "a", null, "c" },
            };

            foreach (object? value in values)
            {
                foreach (bool pretty in new[] { false, true })
                {
                    string expected = value == null ? "null" : JsonSerializer.Serialize(value, value.GetType(), LegacyOptions(pretty));
                    string actual = serializer.SerializeJson(value, pretty);
                    SharedAssert.Equal(expected, actual, "Output for " + (value?.GetType().Name ?? "null") + " should match the previous serializer.");
                }
            }

            return Task.CompletedTask;
        }

        private static Task RunUntypedReadsAsync(CancellationToken token)
        {
            Serializer serializer = new Serializer();

            Dictionary<string, object>? body = serializer.DeserializeJson<Dictionary<string, object>>(
                "{ // comment\n \"text\": \"hi\", \"n\": 3, \"list\": [1, 2,], \"obj\": { \"k\": true }, }");
            SharedAssert.NotNull(body, "Body should deserialize.");
            SharedAssert.True(body!["text"] is JsonElement, "Untyped string should be a JsonElement.");
            SharedAssert.Equal(JsonValueKind.Number, ((JsonElement)body["n"]).ValueKind, "Untyped number kind.");
            SharedAssert.Equal(2, ((JsonElement)body["list"]).GetArrayLength(), "Trailing comma inside the array should be tolerated.");
            SharedAssert.True(((JsonElement)body["obj"]).GetProperty("k").GetBoolean(), "Nested object should be readable.");

            List<List<Dictionary<string, object>>>? nested = serializer.DeserializeJson<List<List<Dictionary<string, object>>>>("[[{\"label\":\"a\",\"score\":0.9}]]");
            SharedAssert.Equal(1, nested!.Count, "Nested lists should deserialize.");
            SharedAssert.Equal("a", ((JsonElement)nested[0][0]["label"]).GetString(), "Nested dictionary value.");

            TokenUsage? usage = serializer.DeserializeJson<TokenUsage>("{\"PromptTokens\":\"12\",\"CompletionTokens\":3}");
            SharedAssert.Equal(12, usage!.PromptTokens, "Quoted numbers should be read.");
            SharedAssert.Equal(3, usage.CompletionTokens, "Plain numbers should be read.");

            SharedAssert.Equal<string?>(null, serializer.DeserializeJson<string>("null"), "The JSON literal null should produce default.");
            return Task.CompletedTask;
        }

        #endregion

        #region Context

        private static Task RunContextRoundTripAsync(CancellationToken token)
        {
            List<ChatMessage> messages = new List<ChatMessage>
            {
                ChatMessage.System("You are terse."),
                ChatMessage.User("Weather in Paris?"),
                ChatMessage.AssistantToolCalls(new[]
                {
                    new ToolCall { Id = "call_1", Name = "get_weather", ArgumentsJson = "{\"city\":\"Paris\"}", ThoughtSignature = "sig-abc" }
                }),
                ChatMessage.ToolResult("call_1", "get_weather", "{\"temp\":21}"),
            };

            string json = JsonSerializer.Serialize(messages, PolyPromptJsonContext.Default.ListChatMessage);
            List<ChatMessage>? restored = JsonSerializer.Deserialize(json, PolyPromptJsonContext.Default.ListChatMessage);

            SharedAssert.NotNull(restored, "Messages should round-trip.");
            SharedAssert.Equal(4, restored!.Count, "Message count.");
            SharedAssert.Equal("system", restored[0].Role, "System role.");
            SharedAssert.Equal("sig-abc", restored[2].ToolCalls[0].ThoughtSignature, "Thought signature should survive persistence.");
            SharedAssert.Equal("{\"city\":\"Paris\"}", restored[2].ToolCalls[0].ArgumentsJson, "Arguments JSON.");
            SharedAssert.Equal("call_1", restored[3].ToolCallId, "Tool call id on the tool result.");

            Serializer serializer = new Serializer();
            SharedAssert.Equal(json, serializer.SerializeJson(messages, false), "Serializer should use the context for public models.");

            ToolChatRequest request = new ToolChatRequest
            {
                Messages = messages,
                Tools = new List<ToolDefinition>
                {
                    ToolDefinition.Function("get_weather", "Weather lookup", new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object> { { "city", new Dictionary<string, object> { { "type", "string" } } } } },
                    })
                },
                Options = new CompletionOptions { Temperature = 0.2, ReasoningEffort = ReasoningEffortLevel.Low }
            };

            string requestJson = JsonSerializer.Serialize(request, PolyPromptJsonContext.Default.ToolChatRequest);
            ToolChatRequest? restoredRequest = JsonSerializer.Deserialize(requestJson, PolyPromptJsonContext.Default.ToolChatRequest);
            SharedAssert.Equal(ReasoningEffortLevel.Low, restoredRequest!.Options!.ReasoningEffort!.Level, "Reasoning effort level.");
            SharedAssert.Equal(0.2, restoredRequest.Options.Temperature, "Temperature.");
            SharedAssert.Equal("get_weather", restoredRequest.Tools[0].Name, "Tool name.");
            SharedAssert.True(restoredRequest.Tools[0].Parameters["properties"] is JsonElement, "Tool parameter values restore as JsonElement.");
            return Task.CompletedTask;
        }

        private static Task RunContextSettingsAsync(CancellationToken token)
        {
            CallDetail detail = new CallDetail
            {
                Url = "http://localhost/v1/chat",
                TimestampUtc = new DateTime(2026, 10, 6, 1, 2, 3, DateTimeKind.Utc).AddTicks(1234560)
            };

            string json = JsonSerializer.Serialize(detail, PolyPromptJsonContext.Default.CallDetail);
            SharedAssert.True(json.Contains("\"TimestampUtc\":\"2026-10-06T01:02:03.123456Z\""), "Dates should be written as UTC with microseconds: " + json);
            SharedAssert.False(json.Contains("\"Method\""), "Null properties should be omitted: " + json);

            string effort = JsonSerializer.Serialize(new ReasoningEffort(ReasoningEffortLevel.High), PolyPromptJsonContext.Default.ReasoningEffort);
            SharedAssert.True(effort.Contains("\"Level\":\"High\""), "Enums should be written as strings: " + effort);

            CallDetail? read = JsonSerializer.Deserialize("{\"StatusCode\":\"200\",\"Success\":true,}", PolyPromptJsonContext.Default.CallDetail);
            SharedAssert.Equal(200, read!.StatusCode, "Quoted numbers and trailing commas should be tolerated.");
            return Task.CompletedTask;
        }

        #endregion

        #region Typed-Metadata

        private static Task RunTypedOverloadsAsync(CancellationToken token)
        {
            Serializer serializer = new Serializer();
            SerializerTestArgs args = new SerializerTestArgs { CityName = "Oslo", DayCount = 3 };

            string compact = serializer.SerializeJson(args, SerializerTestContext.Default.SerializerTestArgs, false);
            SharedAssert.Equal("{\"city_name\":\"Oslo\",\"day_count\":3}", compact, "Compact output should use the metadata's naming.");

            string pretty = serializer.SerializeJson(args, SerializerTestContext.Default.SerializerTestArgs, true);
            SharedAssert.True(pretty.Contains("\n") && pretty.Contains("  \"city_name\": \"Oslo\""), "Pretty output should be indented: " + pretty);

            SerializerTestArgs? read = serializer.DeserializeJson("{\"city_name\":\"Rome\",\"day_count\":5}", SerializerTestContext.Default.SerializerTestArgs);
            SharedAssert.Equal("Rome", read!.CityName, "City.");
            SharedAssert.Equal(5, read.DayCount, "Days.");

            SharedAssert.Equal<SerializerTestArgs?>(null, serializer.DeserializeJson("null", SerializerTestContext.Default.SerializerTestArgs), "Null literal.");
            return Task.CompletedTask;
        }

        private static Task RunToolArgumentsTypedAsync(CancellationToken token)
        {
            ToolCall call = new ToolCall { Name = "forecast", ArgumentsJson = "{\"city_name\":\"Lima\",\"day_count\":2}" };

            SerializerTestArgs? typed = call.DeserializeArguments(SerializerTestContext.Default.SerializerTestArgs);
            SharedAssert.Equal("Lima", typed!.CityName, "Typed-metadata arguments.");
            SharedAssert.Equal(2, typed.DayCount, "Typed-metadata day count.");

            Dictionary<string, object>? untyped = call.DeserializeArguments<Dictionary<string, object>>();
            SharedAssert.Equal("Lima", ((JsonElement)untyped!["city_name"]).GetString(), "Untyped arguments.");

            ToolCall bad = new ToolCall { Name = "forecast", ArgumentsJson = "{\"day_count\":\"many\"}" };
            SharedAssert.ThrowsAsync<JsonException>(() => Task.FromResult(bad.DeserializeArguments(SerializerTestContext.Default.SerializerTestArgs)), "Mismatched arguments should throw.").GetAwaiter().GetResult();
            return Task.CompletedTask;
        }

        private static Task RunRegisteredResolverAsync(CancellationToken token)
        {
            Serializer.AddTypeInfoResolver(RenamingTestResolver.Instance);
            Serializer.AddTypeInfoResolver(RenamingTestResolver.Instance);

            Serializer serializer = new Serializer();
            SerializerTestArgs args = new SerializerTestArgs { CityName = "Kyiv", DayCount = 1 };

            // Reflection would write "CityName"/"DayCount"; the snake_case names prove the registered resolver ran first.
            SharedAssert.Equal("{\"city_name\":\"Kyiv\",\"day_count\":1}", serializer.SerializeJson(args, false), "Registered resolver should win.");
            SharedAssert.True(RenamingTestResolver.Instance.Calls > 0, "Registered resolver should be consulted.");

            Dictionary<string, object> state = new Dictionary<string, object> { { "args", args } };
            SharedAssert.Equal("{\"args\":{\"city_name\":\"Kyiv\",\"day_count\":1}}", serializer.SerializeJson(state, false), "Registered resolver applies to nested values.");

            SerializerTestArgs? read = serializer.DeserializeJson<SerializerTestArgs>("{\"city_name\":\"Riga\",\"day_count\":4,}");
            SharedAssert.Equal("Riga", read!.CityName, "Registered resolver should be used for reads.");

            ToolCall call = new ToolCall { Name = "forecast", ArgumentsJson = "{\"city_name\":\"Bern\",\"day_count\":6}" };
            SharedAssert.Equal(6, call.DeserializeArguments<SerializerTestArgs>()!.DayCount, "DeserializeArguments<T> should use the registered resolver.");

            // Types the resolver declines still fall through to PolyPromptJsonContext.
            SharedAssert.Equal("{\"PromptTokens\":1}", serializer.SerializeJson(new TokenUsage { PromptTokens = 1 }, false), "Unclaimed types use the built-in context.");
            return Task.CompletedTask;
        }

        #endregion

        #region Failures

        private static async Task RunNullArgumentsAsync(CancellationToken token)
        {
            Serializer serializer = new Serializer();

            await SharedAssert.ThrowsAsync<ArgumentNullException>(() => Task.FromResult(serializer.DeserializeJson<Dictionary<string, object>>(null!)), "Null JSON should throw.").ConfigureAwait(false);
            await SharedAssert.ThrowsAsync<ArgumentNullException>(() => Task.FromResult(serializer.DeserializeJson(null!, SerializerTestContext.Default.SerializerTestArgs)), "Null JSON with metadata should throw.").ConfigureAwait(false);
            await SharedAssert.ThrowsAsync<ArgumentNullException>(() => Task.FromResult(serializer.DeserializeJson<SerializerTestArgs>("{}", null!)), "Null metadata on read should throw.").ConfigureAwait(false);
            await SharedAssert.ThrowsAsync<ArgumentNullException>(() => Task.FromResult(serializer.SerializeJson(new SerializerTestArgs(), null!)), "Null metadata on write should throw.").ConfigureAwait(false);
            await SharedAssert.ThrowsAsync<ArgumentNullException>(() => Task.FromResult(new ToolCall().DeserializeArguments<SerializerTestArgs>(null!)), "Null metadata for tool arguments should throw.").ConfigureAwait(false);
            await SharedAssert.ThrowsAsync<ArgumentNullException>(() => { Serializer.AddTypeInfoResolver(null!); return Task.CompletedTask; }, "Null resolver should throw.").ConfigureAwait(false);

            SharedAssert.Equal("null", serializer.SerializeJson(null, false), "Null object serializes to the null literal.");
        }

        private static async Task RunInvalidJsonAsync(CancellationToken token)
        {
            Serializer serializer = new Serializer();

            await SharedAssert.ThrowsAsync<JsonException>(() => Task.FromResult(serializer.DeserializeJson<Dictionary<string, object>>("{not json")), "Malformed JSON should throw.").ConfigureAwait(false);
            await SharedAssert.ThrowsAsync<JsonException>(() => Task.FromResult(serializer.DeserializeJson<Dictionary<string, object>>("")), "Empty input should throw.").ConfigureAwait(false);
            await SharedAssert.ThrowsAsync<JsonException>(() => Task.FromResult(serializer.DeserializeJson<List<string>>("{\"a\":1}")), "Object for a list should throw.").ConfigureAwait(false);
            await SharedAssert.ThrowsAsync<JsonException>(() => Task.FromResult(serializer.DeserializeJson<TokenUsage>("{\"PromptTokens\":\"lots\"}")), "Non-numeric string for a number should throw.").ConfigureAwait(false);
            await SharedAssert.ThrowsAsync<JsonException>(() => Task.FromResult(new ToolCall { ArgumentsJson = "[1," }.DeserializeArguments<Dictionary<string, object>>()), "Truncated arguments should throw.").ConfigureAwait(false);
        }

        private static async Task RunCycleAndDepthAsync(CancellationToken token)
        {
            Serializer serializer = new Serializer();

            string deepest = serializer.SerializeJson(Nest(Serializer.MaxDepth + 1), false);
            SharedAssert.True(deepest.StartsWith("{\"child\":{"), "A graph at the maximum depth should serialize.");

            await SharedAssert.ThrowsAsync<JsonException>(() => Task.FromResult(serializer.SerializeJson(Nest(Serializer.MaxDepth + 2), false)), "A graph past the maximum depth should throw.").ConfigureAwait(false);

            Dictionary<string, object> cyclic = new Dictionary<string, object>();
            cyclic["self"] = cyclic;
            await SharedAssert.ThrowsAsync<JsonException>(() => Task.FromResult(serializer.SerializeJson(cyclic, false)), "A cyclic dictionary should throw.").ConfigureAwait(false);

            List<object> cyclicList = new List<object>();
            cyclicList.Add(cyclicList);
            await SharedAssert.ThrowsAsync<JsonException>(() => Task.FromResult(serializer.SerializeJson(cyclicList, true)), "A cyclic list should throw.").ConfigureAwait(false);
        }

        private static async Task RunNonFiniteNumbersAsync(CancellationToken token)
        {
            Serializer serializer = new Serializer();

            foreach (object value in new object[] { double.NaN, double.PositiveInfinity, float.NegativeInfinity })
            {
                await SharedAssert.ThrowsAsync<ArgumentException>(() => Task.FromResult(JsonSerializer.Serialize(value, value.GetType(), LegacyOptions(false))), "Previous serializer rejects " + value + ".").ConfigureAwait(false);
                await SharedAssert.ThrowsAsync<ArgumentException>(() => Task.FromResult(serializer.SerializeJson(new Dictionary<string, object> { { "v", value } }, false)), "Serializer rejects " + value + ".").ConfigureAwait(false);
            }
        }

        #endregion

        #region Helpers

        private static JsonSerializerOptions LegacyOptions(bool pretty)
        {
            // The options the reflection-based Serializer used before 3.2.0.
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                WriteIndented = pretty,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                NumberHandling = JsonNumberHandling.AllowReadingFromString,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };

            options.Converters.Add(new JsonStringEnumConverter());
            options.Converters.Add(new UtcDateTimeConverter());
            return options;
        }

        private static Dictionary<string, object> Nest(int levels)
        {
            Dictionary<string, object> root = new Dictionary<string, object>();
            Dictionary<string, object> current = root;
            for (int i = 1; i < levels; i++)
            {
                Dictionary<string, object> child = new Dictionary<string, object>();
                current["child"] = child;
                current = child;
            }

            return root;
        }

        #endregion
    }

    /// <summary>
    /// Typed tool arguments used by the serializer cases.
    /// </summary>
    internal sealed class SerializerTestArgs
    {
        public string CityName { get; set; } = string.Empty;

        public int DayCount { get; set; }
    }

    /// <summary>
    /// A resolver registered globally by the serializer cases. It claims only <see cref="SerializerTestArgs"/>, takes the
    /// metadata from <see cref="SerializerTestContext"/>, and renames properties to snake_case so the output shows that
    /// it, rather than reflection, was used. Every other type returns null and falls through to the next resolver.
    /// </summary>
    internal sealed class RenamingTestResolver : IJsonTypeInfoResolver
    {
        public static readonly RenamingTestResolver Instance = new RenamingTestResolver();

        private int _Calls = 0;

        public int Calls => Volatile.Read(ref _Calls);

        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            if (type != typeof(SerializerTestArgs)) return null;

            Interlocked.Increment(ref _Calls);
            JsonTypeInfo? info = ((IJsonTypeInfoResolver)SerializerTestContext.Default).GetTypeInfo(type, options);
            if (info == null) return null;

            foreach (JsonPropertyInfo property in info.Properties)
                property.Name = JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name);
            return info;
        }
    }

    /// <summary>
    /// Source-generated metadata for the serializer cases. Its snake_case naming applies when its metadata is
    /// used directly; when it is consulted through another options instance, that instance's naming applies.
    /// </summary>
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
    [JsonSerializable(typeof(SerializerTestArgs))]
    internal sealed partial class SerializerTestContext : JsonSerializerContext
    {
    }
}
