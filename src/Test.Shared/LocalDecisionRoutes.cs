namespace Test.Shared
{
    using System.Globalization;
    using System.Net;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Nodes;

    /// <summary>
    /// Local test server route for the TypeSafe System One decision API (<c>POST /v1/systemone</c>). The response is
    /// derived from the request: a choice picks the first option whose value appears in the state text (else the first
    /// option), a noul (binary) question answers 0.83, and a score question answers 1.6 on its scale (clamped). Magic
    /// strings in the state text select error and edge-case responses:
    /// <list type="bullet">
    /// <item><c>err422</c>, <c>err429</c>, <c>err529</c>: that HTTP status with a TypeSafe error body.</item>
    /// <item><c>missinganswer</c>: omits the answer for the last question.</item>
    /// <item><c>badanswer</c>: answers carry only a confidence, not the typed field.</item>
    /// <item><c>noanswers</c>: a body without an answers object.</item>
    /// <item><c>notjson</c>: a non-JSON 200 body.</item>
    /// <item><c>scorehigh</c>: score answers far above the scale, to exercise level clamping.</item>
    /// <item><c>slow</c>: waits 150 ms before answering.</item>
    /// </list>
    /// </summary>
    internal static class LocalDecisionRoutes
    {
        public static async Task<bool> TryHandleAsync(HttpListenerContext context, string path, string requestBody)
        {
            if (path != "/v1/systemone") return false;

            JsonObject? request;
            try
            {
                request = JsonNode.Parse(requestBody) as JsonObject;
            }
            catch (JsonException)
            {
                request = null;
            }

            if (request == null)
            {
                await WriteAsync(context, 400, "{\"error\":{\"type\":\"invalid_request_error\",\"message\":\"body is not a JSON object\"}}").ConfigureAwait(false);
                return true;
            }

            JsonNode? stateNode = request["state"];
            string state = stateNode == null ? string.Empty : stateNode.GetValueKind() == JsonValueKind.String ? stateNode.GetValue<string>() : stateNode.ToJsonString();

            if (state.Contains("slow", StringComparison.Ordinal)) await Task.Delay(150).ConfigureAwait(false);

            if (state.Contains("err422", StringComparison.Ordinal))
            {
                await WriteAsync(context, 422, "{\"error\":{\"type\":\"validation_error\",\"message\":\"questions.intent.criteria: at least two options are required\"}}").ConfigureAwait(false);
                return true;
            }

            if (state.Contains("err429", StringComparison.Ordinal))
            {
                await WriteAsync(context, 429, "{\"error\":{\"type\":\"rate_limit_error\",\"message\":\"rate limit exceeded\"}}").ConfigureAwait(false);
                return true;
            }

            if (state.Contains("err529", StringComparison.Ordinal))
            {
                await WriteAsync(context, 529, "{\"error\":{\"type\":\"overloaded_error\",\"message\":\"overloaded\"}}").ConfigureAwait(false);
                return true;
            }

            if (state.Contains("notjson", StringComparison.Ordinal))
            {
                await WriteAsync(context, 200, "definitely not json").ConfigureAwait(false);
                return true;
            }

            string model = request["model"]?.GetValue<string>() ?? "jev-unknown";
            JsonObject response = new JsonObject
            {
                ["model"] = model,
                ["usage"] = new JsonObject { ["input_tokens"] = 42, ["output_tokens"] = 0 }
            };

            if (state.Contains("noanswers", StringComparison.Ordinal))
            {
                await WriteAsync(context, 200, response.ToJsonString()).ConfigureAwait(false);
                return true;
            }

            JsonObject answers = new JsonObject();
            JsonObject questions = request["questions"] as JsonObject ?? new JsonObject();
            List<KeyValuePair<string, JsonNode?>> entries = questions.ToList();
            if (state.Contains("missinganswer", StringComparison.Ordinal) && entries.Count > 0) entries.RemoveAt(entries.Count - 1);

            foreach (KeyValuePair<string, JsonNode?> entry in entries)
            {
                JsonObject question = entry.Value as JsonObject ?? new JsonObject();
                string type = question["type"]?.GetValue<string>() ?? string.Empty;

                if (state.Contains("badanswer", StringComparison.Ordinal))
                {
                    answers[entry.Key] = new JsonObject { ["confidence"] = 0.5 };
                    continue;
                }

                answers[entry.Key] = type switch
                {
                    "noul" => new JsonObject
                    {
                        ["noul"] = 0.83,
                        ["confidence"] = 0.66,
                        ["probabilities"] = new JsonObject { ["true"] = 0.83, ["false"] = 0.17 }
                    },
                    "choice" => BuildChoice(question, state),
                    "score" => BuildScore(question, state),
                    _ => new JsonObject { ["confidence"] = 0.0 }
                };
            }

            response["answers"] = answers;
            await WriteAsync(context, 200, response.ToJsonString()).ConfigureAwait(false);
            return true;
        }

        private static JsonObject BuildChoice(JsonObject question, string state)
        {
            List<string> values = (question["criteria"] as JsonObject)?.Select(option => option.Key).ToList() ?? new List<string>();
            string chosen = values.FirstOrDefault(value => state.Contains(value, StringComparison.Ordinal)) ?? values.FirstOrDefault() ?? string.Empty;

            JsonObject probabilities = new JsonObject();
            double rest = values.Count > 1 ? 0.3 / (values.Count - 1) : 0.0;
            foreach (string value in values) probabilities[value] = value == chosen ? 0.7 : rest;

            return new JsonObject { ["choice"] = chosen, ["confidence"] = 0.55, ["probabilities"] = probabilities };
        }

        private static JsonObject BuildScore(JsonObject question, string state)
        {
            JsonArray levels = question["criteria"] as JsonArray ?? new JsonArray();
            double score = state.Contains("scorehigh", StringComparison.Ordinal) ? 99.0 : Math.Min(1.6, Math.Max(0, levels.Count - 1));

            JsonObject probabilities = new JsonObject();
            JsonObject legend = new JsonObject();
            for (int i = 0; i < levels.Count; i++)
            {
                string key = i.ToString(CultureInfo.InvariantCulture);
                probabilities[key] = 1.0 / levels.Count;
                legend[key] = levels[i]?.GetValue<string>() ?? string.Empty;
            }

            return new JsonObject { ["score"] = score, ["confidence"] = 0.4, ["probabilities"] = probabilities, ["legend"] = legend };
        }

        private static async Task WriteAsync(HttpListenerContext context, int statusCode, string body)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            context.Response.Close();
        }
    }
}
