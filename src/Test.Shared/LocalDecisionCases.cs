namespace Test.Shared
{
    using System.Net;
    using System.Text.Json;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using Touchstone.Core;

    /// <summary>
    /// Deterministic cases for decision models: the request and answer types, request validation, the TypeSafe wire
    /// translation and parsing, error handling, typed accessors, batches (order, concurrency, partial failure), and the
    /// connectivity probe. Registered into the local behavior suite.
    /// </summary>
    internal static class LocalDecisionCases
    {
        public static IEnumerable<TestCaseDescriptor> Create(string suiteId)
        {
            return new List<TestCaseDescriptor>
            {
                Case(suiteId, "decision_content", "DecisionContent converts implicitly from strings, keeps null as null, and FromText rejects null", RunContentAsync),
                Case(suiteId, "decision_factories", "DecisionQuestion and DecisionOption factories build the right question types and values", RunFactoriesAsync),
                Case(suiteId, "decision_validation_request", "Decision requests without a state or questions are rejected before sending", RunValidationRequestAsync),
                Case(suiteId, "decision_validation_questions", "Every invalid question shape is rejected before sending, naming the question", RunValidationQuestionsAsync),
                Case(suiteId, "decision_validation_batch", "Decision batches reject null, empty, null-element, and invalid-element input before sending anything", RunValidationBatchAsync),
                Case(suiteId, "typesafe_wire_format", "TypeSafe requests use POST /v1/systemone with noul/choice/score questions, criteria, and a bearer key", RunWireFormatAsync),
                Case(suiteId, "typesafe_structured_state", "A structured state is sent as a JSON object, not a string", RunStructuredStateAsync),
                Case(suiteId, "typesafe_parsing", "TypeSafe answers parse into typed answers with probabilities, confidence, chosen probability, score level, legend, and usage", RunParsingAsync),
                Case(suiteId, "typesafe_score_level_clamp", "A score above the scale reports the top level", RunScoreClampAsync),
                Case(suiteId, "typesafe_http_errors", "TypeSafe 422, 429, and 529 responses report failure with the status and the provider's message", RunHttpErrorsAsync),
                Case(suiteId, "typesafe_malformed_responses", "Missing answers, missing typed fields, a missing answers object, and non-JSON bodies report failure with no partial answers", RunMalformedAsync),
                Case(suiteId, "decision_typed_accessors", "Typed answer accessors throw KeyNotFound for a missing id and InvalidOperation for the wrong type", RunAccessorsAsync),
                Case(suiteId, "decision_batch_order", "A decision batch returns one response per request in input order", RunBatchOrderAsync),
                Case(suiteId, "decision_batch_concurrency", "A decision batch sends at most MaxConcurrency requests at once, and MaxConcurrency clamps to 1..64", RunBatchConcurrencyAsync),
                Case(suiteId, "decision_batch_partial_failure", "One failed request in a batch fails only its own response", RunBatchPartialFailureAsync),
                Case(suiteId, "decision_cancellation", "Single and batch decisions respect a pre-cancelled token", RunCancellationAsync),
                Case(suiteId, "typesafe_connectivity", "TypeSafe connectivity uses /v1/models, falls back to a probe decision on 404, and fails on other errors", RunConnectivityAsync),
                Case(suiteId, "decision_call_details", "Decision calls are recorded in CallDetails", RunCallDetailsAsync),
            };
        }

        private static TestCaseDescriptor Case(string suiteId, string caseId, string displayName, Func<CancellationToken, Task> executeAsync)
        {
            return new TestCaseDescriptor(suiteId, caseId, displayName, executeAsync, new[] { "local" });
        }

        #region Types

        private static Task RunContentAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            DecisionContent? fromString = "hello";
            SharedAssert.Equal("hello", fromString!.Text, "The implicit conversion should keep the text.");
            SharedAssert.Equal("hello", fromString.ToString(), "ToString should return the text.");

            string? nothing = null;
            DecisionContent? fromNull = nothing;
            SharedAssert.True(fromNull == null, "A null string should convert to a null content.");

            SharedAssert.Equal(string.Empty, DecisionContent.FromText(string.Empty).Text, "FromText should accept an empty string (validation happens on the request).");

            bool threw = false;
            try { DecisionContent.FromText(null!); }
            catch (ArgumentNullException) { threw = true; }
            SharedAssert.True(threw, "FromText should reject null.");

            return Task.CompletedTask;
        }

        private static Task RunFactoriesAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            DecisionQuestion binary = DecisionQuestion.Binary("urgent", "Is it urgent?");
            SharedAssert.True(binary is BinaryQuestion, "Binary should build a BinaryQuestion.");
            SharedAssert.Equal(DecisionQuestionType.Binary, binary.Type, "A binary question should report its type.");
            SharedAssert.Equal("urgent", binary.Id, "The id should be kept.");
            SharedAssert.Equal("Is it urgent?", binary.Instructions!.Text, "The instructions should be kept.");

            ChoiceQuestion choice = (ChoiceQuestion)DecisionQuestion.Choice("intent", "What do they want?", "refund", "billing");
            SharedAssert.Equal(DecisionQuestionType.Choice, choice.Type, "A choice question should report its type.");
            SharedAssert.Equal(2, choice.Options.Count, "Plain string options should become options.");
            SharedAssert.Equal("refund", choice.Options[0].Value, "Option values should be kept in order.");
            SharedAssert.True(choice.Options[0].Description == null, "A plain string option should have no description.");

            ChoiceQuestion described = (ChoiceQuestion)DecisionQuestion.Choice("intent", "What?", DecisionOption.Of("a", "first"), DecisionOption.Of("b"));
            SharedAssert.Equal("first", described.Options[0].Description!.Text, "DecisionOption.Of should keep the description.");
            SharedAssert.True(described.Options[1].Description == null, "DecisionOption.Of without a description should leave it null.");

            ScoreQuestion score = (ScoreQuestion)DecisionQuestion.Score("tone", "How upset?", "calm", "annoyed", "angry");
            SharedAssert.Equal(DecisionQuestionType.Score, score.Type, "A score question should report its type.");
            SharedAssert.Equal(3, score.Levels.Count, "Levels should be kept.");
            SharedAssert.Equal("angry", score.Levels[2].Text, "Levels should be kept in order.");

            return Task.CompletedTask;
        }

        #endregion

        #region Validation

        private static async Task RunValidationRequestAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using TypeSafeDecisionClient client = Client(server);

            await SharedAssert.ThrowsExactAsync<ArgumentNullException>(() => client.DecideAsync((DecisionRequest)null!, null, token), "A null request should be rejected.").ConfigureAwait(false);
            await SharedAssert.ThrowsExactAsync<ArgumentNullException>(() => client.DecideAsync(new DecisionRequest { Questions = { Binary() } }, null, token), "A null state should be rejected.").ConfigureAwait(false);
            await SharedAssert.ThrowsExactAsync<ArgumentException>(() => client.DecideAsync(new DecisionRequest { State = "  ", Questions = { Binary() } }, null, token), "A whitespace state should be rejected.").ConfigureAwait(false);
            await SharedAssert.ThrowsExactAsync<ArgumentException>(() => client.DecideAsync(new DecisionRequest { State = "x" }, null, token), "A request with no questions should be rejected.").ConfigureAwait(false);
            await SharedAssert.ThrowsExactAsync<ArgumentException>(() => client.DecideAsync(new DecisionRequest { State = "x", Questions = null! }, null, token), "A request with null questions should be rejected.").ConfigureAwait(false);

            SharedAssert.Equal(0, server.RequestPaths.Count, "No invalid request should reach the server.");
        }

        private static async Task RunValidationQuestionsAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using TypeSafeDecisionClient client = Client(server);

            List<(string Label, DecisionQuestion?[] Questions, string MessageFragment)> cases = new List<(string, DecisionQuestion?[], string)>
            {
                ("a null question", new DecisionQuestion?[] { null }, "Questions[0]"),
                ("an empty id", new DecisionQuestion?[] { DecisionQuestion.Binary(" ", "x") }, "Questions[0]"),
                ("a duplicate id", new DecisionQuestion?[] { DecisionQuestion.Binary("q", "x"), DecisionQuestion.Binary("q", "y") }, "'q'"),
                ("empty instructions", new DecisionQuestion?[] { DecisionQuestion.Binary("q", " ") }, "'q'"),
                ("null instructions", new DecisionQuestion?[] { new BinaryQuestion { Id = "q" } }, "'q'"),
                ("a choice with one option", new DecisionQuestion?[] { DecisionQuestion.Choice("c", "x", "only") }, "'c'"),
                ("a choice with 256 options", new DecisionQuestion?[] { DecisionQuestion.Choice("c", "x", Enumerable.Range(0, 256).Select(i => "o" + i).ToArray()) }, "'c'"),
                ("a choice with null options", new DecisionQuestion?[] { new ChoiceQuestion { Id = "c", Instructions = "x", Options = null! } }, "'c'"),
                ("a null option", new DecisionQuestion?[] { new ChoiceQuestion { Id = "c", Instructions = "x", Options = { DecisionOption.Of("a"), null! } } }, "'c'"),
                ("an empty option value", new DecisionQuestion?[] { DecisionQuestion.Choice("c", "x", "a", " ") }, "'c'"),
                ("a duplicate option value", new DecisionQuestion?[] { DecisionQuestion.Choice("c", "x", "a", "a") }, "'a'"),
                ("a score with one level", new DecisionQuestion?[] { DecisionQuestion.Score("s", "x", "one") }, "'s'"),
                ("a score with 11 levels", new DecisionQuestion?[] { DecisionQuestion.Score("s", "x", Enumerable.Range(0, 11).Select(i => "l" + i).ToArray()) }, "'s'"),
                ("an empty level", new DecisionQuestion?[] { DecisionQuestion.Score("s", "x", "low", "") }, "'s'"),
                ("a null level", new DecisionQuestion?[] { new ScoreQuestion { Id = "s", Instructions = "x", Levels = { "low", null! } } }, "'s'"),
                ("an unknown question type", new DecisionQuestion?[] { new CustomQuestion { Id = "u", Instructions = "x" } }, "'u'"),
            };

            foreach ((string label, DecisionQuestion?[] questions, string fragment) in cases)
            {
                DecisionRequest request = new DecisionRequest { State = "x", Questions = questions.ToList()! };
                try
                {
                    await client.DecideAsync(request, null, token).ConfigureAwait(false);
                    throw new TestFailureException("A request with " + label + " should be rejected.");
                }
                catch (ArgumentException ex) when (ex is not ArgumentNullException)
                {
                    SharedAssert.True(ex.Message.Contains(fragment, StringComparison.Ordinal), "The error for " + label + " should name " + fragment + ": " + ex.Message);
                }
            }

            DecisionRequest boundaries = new DecisionRequest
            {
                State = "x",
                Questions =
                {
                    DecisionQuestion.Choice("c2", "x", "a", "b"),
                    DecisionQuestion.Choice("c255", "x", Enumerable.Range(0, 255).Select(i => "o" + i).ToArray()),
                    DecisionQuestion.Score("s2", "x", "low", "high"),
                    DecisionQuestion.Score("s10", "x", Enumerable.Range(0, 10).Select(i => "l" + i).ToArray()),
                    DecisionQuestion.Choice("case", "x", "A", "a"),
                }
            };

            DecisionResponse accepted = await client.DecideAsync(boundaries, null, token).ConfigureAwait(false);
            SharedAssert.True(accepted.Success, "Questions at the size limits, and options differing only in case, should be accepted. " + accepted.Error);
            SharedAssert.Equal(1, server.RequestPaths.Count, "Only the valid request should reach the server.");
        }

        private static async Task RunValidationBatchAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using TypeSafeDecisionClient client = Client(server);

            await SharedAssert.ThrowsExactAsync<ArgumentNullException>(() => client.DecideAsync((List<DecisionRequest>)null!, null, token), "A null batch should be rejected.").ConfigureAwait(false);
            await SharedAssert.ThrowsExactAsync<ArgumentException>(() => client.DecideAsync(new List<DecisionRequest>(), null, token), "An empty batch should be rejected.").ConfigureAwait(false);
            await SharedAssert.ThrowsExactAsync<ArgumentException>(() => client.DecideAsync(new List<DecisionRequest> { Request("ok"), null! }, null, token), "A batch with a null element should be rejected.").ConfigureAwait(false);

            try
            {
                await client.DecideAsync(new List<DecisionRequest> { Request("ok"), Request("ok"), new DecisionRequest { State = "x" } }, null, token).ConfigureAwait(false);
                throw new TestFailureException("A batch with an invalid element should be rejected.");
            }
            catch (ArgumentException ex)
            {
                SharedAssert.Equal("requests[2]", ex.ParamName, "The error should name the invalid element.");
            }

            SharedAssert.Equal(0, server.RequestPaths.Count, "No request in an invalid batch should be sent.");
        }

        #endregion

        #region Wire

        private static async Task RunWireFormatAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using TypeSafeDecisionClient client = new TypeSafeDecisionClient(server.Endpoint, "ts-key");

            DecisionRequest request = new DecisionRequest
            {
                State = "My card was charged twice.",
                Questions =
                {
                    new BinaryQuestion { Id = "urgent", Instructions = "Is it urgent?", TrueCriterion = "Needs action today", FalseCriterion = "Can wait" },
                    DecisionQuestion.Binary("plain", "Is it plain?"),
                    DecisionQuestion.Choice("intent", "What do they want?", DecisionOption.Of("refund", "Money back"), DecisionOption.Of("other")),
                    DecisionQuestion.Score("tone", "How upset?", "calm", "annoyed", "angry"),
                }
            };

            DecisionResponse response = await client.DecideAsync(request, null, token).ConfigureAwait(false);
            SharedAssert.True(response.Success, "The decision should succeed. " + response.Error);

            SharedAssert.Equal("/v1/systemone", server.RequestPaths[0], "Decisions should POST to /v1/systemone.");
            SharedAssert.Equal("Bearer ts-key", server.RequestHeaders[0]["Authorization"], "The key should be sent as a bearer token.");

            using JsonDocument document = JsonDocument.Parse(server.RequestBodies[0]);
            JsonElement root = document.RootElement;
            SharedAssert.Equal("jev-latest", root.GetProperty("model").GetString(), "The default model should be jev-latest.");
            SharedAssert.Equal("My card was charged twice.", root.GetProperty("state").GetString(), "Text state should be sent as a string.");

            JsonElement questions = root.GetProperty("questions");
            SharedAssert.Equal(JsonValueKind.Object, questions.ValueKind, "Questions should be an object keyed by id.");
            SharedAssert.Equal("urgent,plain,intent,tone", string.Join(",", questions.EnumerateObject().Select(p => p.Name)), "Questions should keep their order.");

            JsonElement urgent = questions.GetProperty("urgent");
            SharedAssert.Equal("noul", urgent.GetProperty("type").GetString(), "Binary questions should be noul questions.");
            SharedAssert.Equal("Is it urgent?", urgent.GetProperty("instructions").GetString(), "Instructions should be sent.");
            SharedAssert.Equal("Needs action today", urgent.GetProperty("criteria").GetProperty("true").GetString(), "The true criterion should be sent.");
            SharedAssert.Equal("Can wait", urgent.GetProperty("criteria").GetProperty("false").GetString(), "The false criterion should be sent.");
            SharedAssert.False(questions.GetProperty("plain").TryGetProperty("criteria", out _), "A binary question without criteria should send none.");

            JsonElement intent = questions.GetProperty("intent");
            SharedAssert.Equal("choice", intent.GetProperty("type").GetString(), "Choice questions should be choice questions.");
            SharedAssert.Equal("Money back", intent.GetProperty("criteria").GetProperty("refund").GetString(), "An option description should be its criterion.");
            SharedAssert.Equal(JsonValueKind.Null, intent.GetProperty("criteria").GetProperty("other").ValueKind, "An option without a description should be null.");

            JsonElement tone = questions.GetProperty("tone");
            SharedAssert.Equal("score", tone.GetProperty("type").GetString(), "Score questions should be score questions.");
            SharedAssert.Equal("calm,annoyed,angry", string.Join(",", tone.GetProperty("criteria").EnumerateArray().Select(e => e.GetString())), "Levels should be sent as an ordered array.");

            client.Model = "jev-default";
            await client.DecideAsync(request, new DecisionOptions { Model = "jev-override" }, token).ConfigureAwait(false);
            await client.DecideAsync(request, null, token).ConfigureAwait(false);
            using JsonDocument overridden = JsonDocument.Parse(server.RequestBodies[1]);
            using JsonDocument defaulted = JsonDocument.Parse(server.RequestBodies[2]);
            SharedAssert.Equal("jev-override", overridden.RootElement.GetProperty("model").GetString(), "A per-call model should be sent.");
            SharedAssert.Equal("jev-default", defaulted.RootElement.GetProperty("model").GetString(), "The client model should be sent otherwise.");

            using TypeSafeDecisionClient anonymous = new TypeSafeDecisionClient(server.Endpoint);
            await anonymous.DecideAsync(request, null, token).ConfigureAwait(false);
            SharedAssert.False(server.RequestHeaders[3].ContainsKey("Authorization"), "A client without a key (a local Ollaya server) should send no Authorization header.");
        }

        private static async Task RunStructuredStateAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using TypeSafeDecisionClient client = Client(server);

            DecisionRequest request = new DecisionRequest
            {
                State = new Dictionary<string, object> { { "ticket", "charged twice" }, { "priority", 2 } },
                Questions = { Binary() }
            };

            DecisionResponse response = await client.DecideAsync(request, null, token).ConfigureAwait(false);
            SharedAssert.True(response.Success, "A structured state should be accepted. " + response.Error);

            using JsonDocument document = JsonDocument.Parse(server.RequestBodies[0]);
            JsonElement state = document.RootElement.GetProperty("state");
            SharedAssert.Equal(JsonValueKind.Object, state.ValueKind, "A structured state should be sent as a JSON object.");
            SharedAssert.Equal("charged twice", state.GetProperty("ticket").GetString(), "Structured state fields should be kept.");
            SharedAssert.Equal(2, state.GetProperty("priority").GetInt32(), "Structured state numbers should stay numbers.");
        }

        private static async Task RunParsingAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using TypeSafeDecisionClient client = Client(server);

            DecisionResponse response = await client.DecideAsync(Request("customer wants a refund"), new DecisionOptions { Model = "jev-parse" }, token).ConfigureAwait(false);
            SharedAssert.True(response.Success, "The decision should succeed. " + response.Error);
            SharedAssert.Equal(200, response.StatusCode, "The status code should be reported.");
            SharedAssert.True(response.Error == null, "A successful decision should have no error.");
            SharedAssert.Equal("jev-parse", response.Model, "The model should come from the response.");
            SharedAssert.True(response.OverallRuntimeMs >= 0, "The runtime should be reported.");
            SharedAssert.Equal(3, response.Answers.Count, "Every question should be answered.");

            BinaryAnswer urgent = response.Binary("urgent");
            SharedAssert.Equal("urgent", urgent.Id, "Answers should carry their question id.");
            SharedAssert.Equal(DecisionQuestionType.Binary, urgent.Type, "A binary answer should report its type.");
            SharedAssert.Equal(0.83, urgent.Probability, "The binary probability should be parsed.");
            SharedAssert.Equal<double?>(0.66, urgent.Confidence, "The confidence should be parsed.");
            SharedAssert.Equal(0.17, urgent.Probabilities["false"], "The binary distribution should be parsed.");

            ChoiceAnswer intent = response.Choice("intent");
            SharedAssert.Equal("refund", intent.Value, "The chosen option should be parsed.");
            SharedAssert.Equal<double?>(0.7, intent.Probability, "The chosen option's probability should be taken from the distribution.");
            SharedAssert.Equal(4, intent.Probabilities.Count, "Every option's probability should be parsed.");
            SharedAssert.Equal<double?>(0.55, intent.Confidence, "The choice confidence should be parsed.");

            ScoreAnswer tone = response.Score("tone");
            SharedAssert.Equal(1.6, tone.Value, "The score should be parsed.");
            SharedAssert.Equal(2, tone.Level, "The nearest level should be the rounded score.");
            SharedAssert.Equal("angry", tone.LevelText, "The level text should come from the question.");
            SharedAssert.Equal("annoyed", tone.Legend["1"], "The legend should be parsed.");
            SharedAssert.Equal(4, tone.Probabilities.Count, "The per-level distribution should be parsed.");

            SharedAssert.NotNull(response.Usage, "Usage should be parsed.");
            SharedAssert.Equal<int?>(42, response.Usage!.PromptTokens, "Input tokens should be parsed.");
            SharedAssert.Equal<int?>(0, response.Usage.CompletionTokens, "Output tokens should be parsed.");
            SharedAssert.Equal<int?>(42, response.Usage.TotalTokens, "Total tokens should be derived.");
        }

        private static async Task RunScoreClampAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using TypeSafeDecisionClient client = Client(server);

            DecisionResponse response = await client.DecideAsync(Request("scorehigh"), null, token).ConfigureAwait(false);
            SharedAssert.True(response.Success, "The decision should succeed. " + response.Error);
            SharedAssert.Equal(99.0, response.Score("tone").Value, "The raw score should be kept.");
            SharedAssert.Equal(3, response.Score("tone").Level, "The level should clamp to the top of the scale.");
            SharedAssert.Equal("furious", response.Score("tone").LevelText, "The level text should be the top level.");
        }

        private static async Task RunHttpErrorsAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using TypeSafeDecisionClient client = Client(server);

            foreach ((string magic, int status, string message) in new[] { ("err422", 422, "at least two options"), ("err429", 429, "rate limit"), ("err529", 529, "overloaded") })
            {
                DecisionResponse response = await client.DecideAsync(Request(magic), null, token).ConfigureAwait(false);
                SharedAssert.False(response.Success, "HTTP " + status + " should fail.");
                SharedAssert.Equal<int?>(status, response.StatusCode, "HTTP " + status + " should be reported.");
                SharedAssert.True(response.Error != null && response.Error.Contains(message, StringComparison.OrdinalIgnoreCase), "HTTP " + status + " should report the provider's message: " + response.Error);
                SharedAssert.Equal(0, response.Answers.Count, "A failed decision should have no answers.");
            }
        }

        private static async Task RunMalformedAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using TypeSafeDecisionClient client = Client(server);

            DecisionResponse missing = await client.DecideAsync(Request("missinganswer"), null, token).ConfigureAwait(false);
            SharedAssert.False(missing.Success, "A response missing an answer should fail.");
            SharedAssert.True(missing.Error!.Contains("'tone'", StringComparison.Ordinal), "The error should name the unanswered question: " + missing.Error);
            SharedAssert.Equal(0, missing.Answers.Count, "A partial response should not leave partial answers.");

            DecisionResponse bad = await client.DecideAsync(Request("badanswer"), null, token).ConfigureAwait(false);
            SharedAssert.False(bad.Success, "An answer missing its typed field should fail.");
            SharedAssert.True(bad.Error!.Contains("'noul'", StringComparison.Ordinal), "The error should name the missing field: " + bad.Error);
            SharedAssert.Equal(0, bad.Answers.Count, "A malformed response should not leave partial answers.");

            DecisionResponse none = await client.DecideAsync(Request("noanswers"), null, token).ConfigureAwait(false);
            SharedAssert.False(none.Success, "A response without answers should fail.");
            SharedAssert.True(none.Error!.Contains("answers", StringComparison.Ordinal), "The error should mention the answers: " + none.Error);

            DecisionResponse notJson = await client.DecideAsync(Request("notjson"), null, token).ConfigureAwait(false);
            SharedAssert.False(notJson.Success, "A non-JSON response should fail.");
            SharedAssert.NotEmpty(notJson.Error, "A non-JSON response should report an error.");
        }

        private static Task RunAccessorsAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            DecisionResponse response = new DecisionResponse();
            response.Answers["intent"] = new ChoiceAnswer { Id = "intent", Value = "refund" };
            response.Answers["urgent"] = new BinaryAnswer { Id = "urgent", Probability = 0.9 };

            SharedAssert.Equal("refund", response.Choice("intent").Value, "Choice should return the choice answer.");
            SharedAssert.Equal(0.9, response.Binary("urgent").Probability, "Binary should return the binary answer.");

            Expect<KeyNotFoundException>(() => response.Choice("missing"), "A missing id should throw KeyNotFoundException.");
            Expect<InvalidOperationException>(() => response.Binary("intent"), "The wrong answer type should throw InvalidOperationException.");
            Expect<InvalidOperationException>(() => response.Score("urgent"), "The wrong answer type should throw InvalidOperationException.");
            Expect<ArgumentNullException>(() => response.Choice(null!), "A null id should throw ArgumentNullException.");
            return Task.CompletedTask;
        }

        #endregion

        #region Batch

        private static async Task RunBatchOrderAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using TypeSafeDecisionClient client = Client(server);
            client.MaxConcurrency = 8;

            string[] intents = new[] { "billing", "refund", "praise", "other", "refund slow", "billing", "praise slow", "other" };
            List<DecisionRequest> requests = intents.Select(Request).ToList();

            List<DecisionResponse> responses = await client.DecideAsync(requests, null, token).ConfigureAwait(false);
            SharedAssert.Equal(intents.Length, responses.Count, "The batch should return one response per request.");
            for (int i = 0; i < intents.Length; i++)
            {
                SharedAssert.True(responses[i].Success, "Batch response " + i + " should succeed. " + responses[i].Error);
                SharedAssert.Equal(intents[i].Split(' ')[0], responses[i].Choice("intent").Value, "Batch response " + i + " should answer request " + i + ", even when earlier requests are slower.");
            }

            SharedAssert.Equal(intents.Length, server.RequestPaths.Count, "The batch should send one request per element.");
        }

        private static async Task RunBatchConcurrencyAsync(CancellationToken token)
        {
            LocalFakeHandler handler = new LocalFakeHandler(async (request, index) =>
            {
                await Task.Delay(40).ConfigureAwait(false);
                return LocalFakeHandler.Json(HttpStatusCode.OK, "{\"answers\":{\"urgent\":{\"noul\":0.5},\"intent\":{\"choice\":\"other\"},\"tone\":{\"score\":0}}}");
            });

            using HttpClient http = new HttpClient(handler);
            using TypeSafeDecisionClient client = new TypeSafeDecisionClient("http://polyprompt.test", "k", httpClient: http);
            client.MaxConcurrency = 2;

            List<DecisionResponse> responses = await client.DecideAsync(Enumerable.Range(0, 8).Select(i => Request("r" + i)).ToList(), null, token).ConfigureAwait(false);
            SharedAssert.True(responses.All(r => r.Success), "Every batch response should succeed.");
            SharedAssert.Equal(8, handler.Count, "Every request should be sent.");
            SharedAssert.True(handler.PeakInFlight <= 2, "No more than MaxConcurrency requests should be in flight; peak was " + handler.PeakInFlight + ".");
            SharedAssert.True(handler.PeakInFlight == 2, "The batch should use the allowed concurrency; peak was " + handler.PeakInFlight + ".");

            client.MaxConcurrency = 0;
            SharedAssert.Equal(1, client.MaxConcurrency, "MaxConcurrency should clamp to at least 1.");
            client.MaxConcurrency = 1000;
            SharedAssert.Equal(64, client.MaxConcurrency, "MaxConcurrency should clamp to at most 64.");
            SharedAssert.Equal(4, new TypeSafeDecisionClient().MaxConcurrency, "MaxConcurrency should default to 4.");
        }

        private static async Task RunBatchPartialFailureAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using TypeSafeDecisionClient client = Client(server);

            List<DecisionResponse> responses = await client.DecideAsync(new List<DecisionRequest> { Request("refund"), Request("err429"), Request("billing") }, null, token).ConfigureAwait(false);
            SharedAssert.True(responses[0].Success && responses[2].Success, "The requests around a failure should succeed.");
            SharedAssert.False(responses[1].Success, "The rate-limited request should fail.");
            SharedAssert.Equal<int?>(429, responses[1].StatusCode, "The failed request should report its status.");
            SharedAssert.Equal("billing", responses[2].Choice("intent").Value, "A later request should keep its own answers.");
        }

        private static async Task RunCancellationAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using TypeSafeDecisionClient client = Client(server);
            using CancellationTokenSource cancelled = new CancellationTokenSource();
            cancelled.Cancel();

            await SharedAssert.ThrowsAsync<OperationCanceledException>(() => client.DecideAsync(Request("x"), null, cancelled.Token), "A single decision should respect a pre-cancelled token.").ConfigureAwait(false);
            await SharedAssert.ThrowsAsync<OperationCanceledException>(() => client.DecideAsync(new List<DecisionRequest> { Request("x"), Request("y") }, null, cancelled.Token), "A batch should respect a pre-cancelled token.").ConfigureAwait(false);
        }

        #endregion

        #region Connectivity

        private static async Task RunConnectivityAsync(CancellationToken token)
        {
            using (LocalOpenAiTestServer server = LocalOpenAiTestServer.Start())
            using (TypeSafeDecisionClient client = Client(server))
            {
                SharedAssert.True(await client.ValidateConnectivityAsync(token).ConfigureAwait(false), "A server with /v1/models (Ollaya) should validate.");
                SharedAssert.Equal("/v1/models", server.RequestPaths.Single(), "The probe should use /v1/models alone when it exists.");
            }

            LocalFakeHandler hosted = new LocalFakeHandler((request, index) => Task.FromResult(
                request.RequestUri!.AbsolutePath == "/v1/models"
                    ? LocalFakeHandler.Json(HttpStatusCode.NotFound, "{}")
                    : LocalFakeHandler.Json(HttpStatusCode.OK, "{\"answers\":{\"reachable\":{\"noul\":0.9}}}")));
            using (HttpClient http = new HttpClient(hosted))
            using (TypeSafeDecisionClient client = new TypeSafeDecisionClient("http://polyprompt.test", "k", httpClient: http))
            {
                SharedAssert.True(await client.ValidateConnectivityAsync(token).ConfigureAwait(false), "The hosted API (no /v1/models) should validate with a probe decision.");
                SharedAssert.Equal(2, hosted.Count, "A 404 from /v1/models should fall back to one probe decision.");
                SharedAssert.True(hosted.RequestUrls[1].EndsWith("/v1/systemone", StringComparison.Ordinal), "The fallback should be a decision.");
            }

            LocalFakeHandler failingProbe = new LocalFakeHandler((request, index) => Task.FromResult(
                request.RequestUri!.AbsolutePath == "/v1/models"
                    ? LocalFakeHandler.Json(HttpStatusCode.NotFound, "{}")
                    : LocalFakeHandler.Json(HttpStatusCode.Unauthorized, "{\"error\":{\"message\":\"bad key\"}}")));
            using (HttpClient http = new HttpClient(failingProbe))
            using (TypeSafeDecisionClient client = new TypeSafeDecisionClient("http://polyprompt.test", "k", httpClient: http))
            {
                SharedAssert.False(await client.ValidateConnectivityAsync(token).ConfigureAwait(false), "A rejected probe decision should fail.");
            }

            LocalFakeHandler serverError = LocalFakeHandler.Always(HttpStatusCode.InternalServerError);
            using (HttpClient http = new HttpClient(serverError))
            using (TypeSafeDecisionClient client = new TypeSafeDecisionClient("http://polyprompt.test", "k", httpClient: http))
            {
                SharedAssert.False(await client.ValidateConnectivityAsync(token).ConfigureAwait(false), "An HTTP 500 from /v1/models should fail without a probe decision.");
                SharedAssert.Equal(1, serverError.Count, "Only a 404 should trigger the probe decision.");
            }
        }

        private static async Task RunCallDetailsAsync(CancellationToken token)
        {
            using LocalOpenAiTestServer server = LocalOpenAiTestServer.Start();
            using TypeSafeDecisionClient client = Client(server);

            await client.DecideAsync(Request("refund"), null, token).ConfigureAwait(false);
            await client.DecideAsync(Request("err429"), null, token).ConfigureAwait(false);

            SharedAssert.Equal(2, client.CallDetails.Count, "Both decisions should be recorded.");
            CallDetail ok = client.CallDetails[0];
            SharedAssert.Equal("POST", ok.Method, "The decision should be recorded as a POST.");
            SharedAssert.True(ok.Url!.EndsWith("/v1/systemone", StringComparison.Ordinal), "The decision URL should be recorded.");
            SharedAssert.True(ok.Success, "The successful decision should be recorded as successful.");
            SharedAssert.True(ok.RequestBody!.Contains("\"questions\"", StringComparison.Ordinal), "The request body should be recorded.");
            SharedAssert.False(client.CallDetails[1].Success, "The failed decision should be recorded as failed.");
            SharedAssert.Equal<int?>(429, client.CallDetails[1].StatusCode, "The failed decision's status should be recorded.");
        }

        #endregion

        #region Helpers

        private sealed class CustomQuestion : DecisionQuestion
        {
            public override DecisionQuestionType Type => DecisionQuestionType.Binary;
        }

        private static TypeSafeDecisionClient Client(LocalOpenAiTestServer server)
        {
            return new TypeSafeDecisionClient(server.Endpoint, "ts-key") { TimeoutMs = 3000 };
        }

        private static DecisionQuestion Binary()
        {
            return DecisionQuestion.Binary("urgent", "Is it urgent?");
        }

        private static DecisionRequest Request(string state)
        {
            return new DecisionRequest
            {
                State = state,
                Questions =
                {
                    Binary(),
                    DecisionQuestion.Choice("intent", "What does the customer want?", "refund", "billing", "praise", "other"),
                    DecisionQuestion.Score("tone", "How upset is the customer?", "calm", "annoyed", "angry", "furious"),
                }
            };
        }

        private static void Expect<TException>(Action action, string message) where TException : Exception
        {
            try
            {
                action();
            }
            catch (Exception ex) when (ex.GetType() == typeof(TException))
            {
                return;
            }

            throw new TestFailureException(message);
        }

        #endregion
    }
}
