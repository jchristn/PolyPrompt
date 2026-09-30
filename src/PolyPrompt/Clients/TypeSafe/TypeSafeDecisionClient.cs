namespace PolyPrompt.Clients
{
    using System.Net.Http.Headers;
    using System.Text.Json;
    using PolyPrompt.Models;
    using SyslogLogging;

    /// <summary>
    /// Decision client for the TypeSafe System One API (<c>POST /v1/systemone</c>), served by TypeSafe's hosted API (the
    /// Jev models) and by Ollaya, the local runtime for open decision models. One request asks typed questions about a
    /// state and returns calibrated answers; nothing is generated. PolyPrompt's <see cref="DecisionQuestionType.Binary"/>
    /// questions are TypeSafe's <c>noul</c> questions. The API has no batch endpoint, so the batch overload sends requests
    /// concurrently (see <see cref="DecisionClientBase.MaxConcurrency"/>). HTTP errors, including 422 (validation), 429
    /// (rate limit), and 529 (overloaded), are reported on the response.
    /// </summary>
    public class TypeSafeDecisionClient : DecisionClientBase
    {
        #region Private-Members

        private const string BinaryWireType = "noul";
        private const string ChoiceWireType = "choice";
        private const string ScoreWireType = "score";

        #endregion

        #region Public-Members

        /// <summary>
        /// The TypeSafe hosted API endpoint.
        /// </summary>
        public const string DefaultEndpoint = "https://api.typesafe.ai";

        /// <summary>
        /// Client-wide default settings. Default model: jev-latest.
        /// </summary>
        public override DecisionOptions Defaults { get; } = new DecisionOptions { Model = "jev-latest" };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new TypeSafe decision client.
        /// </summary>
        /// <param name="endpoint">API endpoint URL: the hosted API (default, https://api.typesafe.ai) or a local Ollaya server.</param>
        /// <param name="apiKey">API key, sent as a bearer token. A local Ollaya server accepts any value. Default: null.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        /// <param name="httpClient">Optional HTTP client; see <see cref="ClientBase"/>. Default: null.</param>
        public TypeSafeDecisionClient(
            string endpoint = DefaultEndpoint,
            string? apiKey = null,
            LoggingModule? logging = null,
            HttpClient? httpClient = null)
            : base(endpoint, apiKey, logging ?? new LoggingModule(), httpClient)
        {
            _Header = "[TypeSafe] ";
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Verify connectivity and credentials. Tries <c>GET /v1/models</c> first (served by Ollaya); when that endpoint is
        /// absent (HTTP 404, as on the hosted API), sends a minimal one-question decision instead. Returns false on an HTTP
        /// error, an unreachable server, or a timeout; rethrows only caller cancellation.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the provider accepted the request.</returns>
        public override async Task<bool> ValidateConnectivityAsync(CancellationToken token = default)
        {
            try
            {
                HttpCallResult models = await GetAndRecordAsync(BuildUrl("/v1/models"), token).ConfigureAwait(false);
                if (models.IsSuccessStatusCode) return true;
                if (models.StatusCode != 404) return false;

                DecisionRequest probe = new DecisionRequest
                {
                    State = "ping",
                    Questions = { DecisionQuestion.Binary("reachable", "Is this a test message?") }
                };

                DecisionResponse response = await DecideAsync(probe, null, token).ConfigureAwait(false);
                return response.Success;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _Logging.Debug(_Header + "connectivity probe failed: " + ex.Message);
                return false;
            }
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
        {
            if (!string.IsNullOrEmpty(_ApiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _ApiKey);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override Task<DecisionResponse> DecideCoreAsync(DecisionRequest request, string model, DecisionOptions? options, CancellationToken token)
        {
            Dictionary<string, object> questions = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (DecisionQuestion question in request.Questions)
            {
                questions[question.Id] = BuildQuestion(question);
            }

            Dictionary<string, object> body = new Dictionary<string, object>
            {
                { "model", model },
                { "state", request.State! },
                { "questions", questions }
            };

            DecisionResponse response = new DecisionResponse { Model = model };
            return ExecutePostAsync(response, "decide", BuildUrl("/v1/systemone"), body, (text, r) => ParseAnswers(text, r, request), token);
        }

        #endregion

        #region Private-Methods

        private static Dictionary<string, object> BuildQuestion(DecisionQuestion question)
        {
            Dictionary<string, object> wire = new Dictionary<string, object>
            {
                { "instructions", ContentValue(question.Instructions)! }
            };

            switch (question)
            {
                case BinaryQuestion binary:
                    wire["type"] = BinaryWireType;
                    if (binary.TrueCriterion != null || binary.FalseCriterion != null)
                    {
                        Dictionary<string, object> criteria = new Dictionary<string, object>();
                        if (binary.TrueCriterion != null) criteria["true"] = ContentValue(binary.TrueCriterion)!;
                        if (binary.FalseCriterion != null) criteria["false"] = ContentValue(binary.FalseCriterion)!;
                        wire["criteria"] = criteria;
                    }
                    break;

                case ChoiceQuestion choice:
                    wire["type"] = ChoiceWireType;
                    Dictionary<string, object?> options = new Dictionary<string, object?>(StringComparer.Ordinal);
                    foreach (DecisionOption option in choice.Options) options[option.Value] = ContentValue(option.Description);
                    wire["criteria"] = options;
                    break;

                case ScoreQuestion score:
                    wire["type"] = ScoreWireType;
                    wire["criteria"] = score.Levels.Select(level => ContentValue(level)!).ToList();
                    break;
            }

            return wire;
        }

        private static object? ContentValue(DecisionContent? content)
        {
            return content?.Text;
        }

        private void ParseAnswers(string responseBody, DecisionResponse response, DecisionRequest request)
        {
            Dictionary<string, object>? responseObj = TryDeserializeObject(responseBody);
            Dictionary<string, object>? answers = responseObj == null ? null : ParseNestedObject(responseObj, "answers");
            if (answers == null)
            {
                response.Error = "Response missing 'answers' field";
                return;
            }

            if (responseObj!.ContainsKey("model") && responseObj["model"] != null) response.Model = responseObj["model"]?.ToString() ?? response.Model;

            Dictionary<string, object>? usage = ParseNestedObject(responseObj, "usage");
            if (usage != null)
            {
                TokenUsage tokens = new TokenUsage
                {
                    PromptTokens = TryGetInt(usage, "input_tokens"),
                    CompletionTokens = TryGetInt(usage, "output_tokens")
                };
                if (tokens.PromptTokens.HasValue || tokens.CompletionTokens.HasValue)
                    tokens.TotalTokens = (tokens.PromptTokens ?? 0) + (tokens.CompletionTokens ?? 0);
                response.Usage = tokens;
            }

            foreach (DecisionQuestion question in request.Questions)
            {
                Dictionary<string, object>? answerObj = ParseNestedObject(answers, question.Id);
                if (answerObj == null)
                {
                    response.Error = "Response has no answer for question '" + question.Id + "'";
                    response.Answers.Clear();
                    return;
                }

                DecisionAnswer? answer = ParseAnswer(question, answerObj, out string? error);
                if (answer == null)
                {
                    response.Error = error;
                    response.Answers.Clear();
                    return;
                }

                response.Answers[question.Id] = answer;
            }
        }

        private DecisionAnswer? ParseAnswer(DecisionQuestion question, Dictionary<string, object> answerObj, out string? error)
        {
            error = null;
            DecisionAnswer answer;

            switch (question)
            {
                case BinaryQuestion:
                    double? probability = TryGetDouble(answerObj, BinaryWireType);
                    if (!probability.HasValue)
                    {
                        error = "Answer for question '" + question.Id + "' is missing '" + BinaryWireType + "'";
                        return null;
                    }
                    answer = new BinaryAnswer { Probability = probability.Value };
                    break;

                case ChoiceQuestion:
                    string? choice = answerObj.ContainsKey(ChoiceWireType) ? answerObj[ChoiceWireType]?.ToString() : null;
                    if (string.IsNullOrEmpty(choice))
                    {
                        error = "Answer for question '" + question.Id + "' is missing '" + ChoiceWireType + "'";
                        return null;
                    }
                    answer = new ChoiceAnswer { Value = choice };
                    break;

                case ScoreQuestion scoreQuestion:
                    double? score = TryGetDouble(answerObj, ScoreWireType);
                    if (!score.HasValue)
                    {
                        error = "Answer for question '" + question.Id + "' is missing '" + ScoreWireType + "'";
                        return null;
                    }

                    int level = (int)Math.Clamp(Math.Round(score.Value, MidpointRounding.AwayFromZero), 0, scoreQuestion.Levels.Count - 1);
                    ScoreAnswer scoreAnswer = new ScoreAnswer { Value = score.Value, Level = level, LevelText = scoreQuestion.Levels[level].Text };
                    Dictionary<string, object>? legend = ParseNestedObject(answerObj, "legend");
                    if (legend != null)
                    {
                        foreach (KeyValuePair<string, object> entry in legend) scoreAnswer.Legend[entry.Key] = entry.Value?.ToString() ?? string.Empty;
                    }
                    answer = scoreAnswer;
                    break;

                default:
                    error = "Question '" + question.Id + "' has an unsupported type";
                    return null;
            }

            answer.Id = question.Id;
            answer.Confidence = TryGetDouble(answerObj, "confidence");

            Dictionary<string, object>? probabilities = ParseNestedObject(answerObj, "probabilities");
            if (probabilities != null)
            {
                foreach (string key in probabilities.Keys)
                {
                    double? value = TryGetDouble(probabilities, key);
                    if (value.HasValue) answer.Probabilities[key] = value.Value;
                }
            }

            if (answer is ChoiceAnswer chosen && chosen.Probabilities.TryGetValue(chosen.Value, out double chosenProbability))
                chosen.Probability = chosenProbability;

            return answer;
        }

        #endregion
    }
}
