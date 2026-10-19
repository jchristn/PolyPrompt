namespace Test.Shared
{
    using System.Net;
    using System.Text;
    using System.Text.Json;
    using PolyPrompt.Helpers;

    // Native xAI contract fixture. Deliberately separate from the legacy OpenAI chat routes.
    internal static class LocalXAiRoutes
    {
        public const string Key = "xai-test-key";
        private static string Json(object value) => new Serializer().SerializeJson(value, false);
        private static Dictionary<string, object> Obj(params (string, object)[] fields) => fields.ToDictionary(f => f.Item1, f => f.Item2);
        private static async Task Send(HttpListenerContext c, string value, int status = 200, bool stream = false)
        {
            c.Response.StatusCode = status; c.Response.ContentType = stream ? "text/event-stream" : "application/json";
            byte[] bytes = Encoding.UTF8.GetBytes(value); await c.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false); c.Response.Close();
        }
        public static async Task<bool> TryHandleAsync(HttpListenerContext c, string path, string body)
        {
            bool native = path.StartsWith("/v1/responses", StringComparison.Ordinal);
            bool keyed = c.Request.Headers["Authorization"] == "Bearer " + Key;
            if (!native && !keyed) return false;
            if (path.Contains("/input_items", StringComparison.Ordinal)) { await Send(c, "{\"object\":\"list\",\"data\":[{\"id\":\"input-1\",\"role\":\"user\",\"content\":\"hello\"}],\"has_more\":false,\"last_id\":\"input-1\"}"); return true; }
            if (path.StartsWith("/v1/responses/", StringComparison.Ordinal) && c.Request.HttpMethod == "DELETE") { await Send(c, "{\"id\":\"resp-test\",\"object\":\"response\",\"deleted\":true}"); return true; }
            if (path == "/v1/responses/compact") { await Send(c, "{\"id\":\"cmp-test\",\"object\":\"response.compaction\",\"model\":\"test-model\",\"output\":[{\"type\":\"compaction\",\"encrypted_content\":\"compact-state\"}]}"); return true; }
            if (path.StartsWith("/v1/responses/", StringComparison.Ordinal) && c.Request.HttpMethod == "GET") { await Send(c, Json(Response("test-model", false, false))); return true; }
            if (keyed && c.Request.HttpMethod == "GET")
            {
                object model = Obj(("id", "test-model"), ("owned_by", "xai"), ("created", 1700000000), ("object", "model"), ("aliases", new[] { "test-latest" }), ("capabilities", Obj(("reasoning_effort", true))));
                bool detail = path.Trim('/').Split('/').Length > 2;
                await Send(c, Json(detail ? model : Obj((path == "/v1/models" ? "data" : "models", new[] { model })))); return true;
            }
            using JsonDocument document = JsonDocument.Parse(string.IsNullOrEmpty(body) ? "{}" : body);
            JsonElement request = document.RootElement;
            if (path == "/v1/embeddings")
            {
                JsonElement input = request.GetProperty("input");
                int count = input.ValueKind == JsonValueKind.String || (input.ValueKind == JsonValueKind.Array && input.GetArrayLength() > 0 && input[0].ValueKind == JsonValueKind.Number) ? 1 : input.GetArrayLength();
                bool base64 = request.TryGetProperty("encoding_format", out var format) && format.GetString() == "base64";
                List<object> data = new();
                for (int i = count - 1; i >= 0; i--) data.Add(Obj(("index", i), ("embedding", base64 ? "AACAPwAAAEAAAEBA" : new float[] { 1, 2, 3 })));
                await Send(c, Json(Obj(("model", "test-embedding"), ("data", data), ("usage", Obj(("prompt_tokens", 7), ("total_tokens", 7)))))); return true;
            }
            if (path != "/v1/responses") return false;
            if (body.Contains("xai-http-error", StringComparison.Ordinal)) { await Send(c, "{\"error\":{\"message\":\"rate limited\"}}", 429); return true; }
            if (body.Contains("xai-malformed", StringComparison.Ordinal)) { await Send(c, "{\"status\":\"completed\",\"output\":[]}"); return true; }
            bool stream = request.TryGetProperty("stream", out var streaming) && streaming.GetBoolean();
            bool tools = request.TryGetProperty("tools", out var toolList) && toolList.EnumerateArray().Any(t => t.TryGetProperty("type", out var ttype) && ttype.GetString() == "function")
                && !body.Contains("function_call_output", StringComparison.Ordinal) && !(request.TryGetProperty("tool_choice", out var choice) && choice.ValueKind == JsonValueKind.String && choice.GetString() == "none");
            Dictionary<string, object> response = Response(request.GetProperty("model").GetString()!, tools, body.Contains("xai-incomplete", StringComparison.Ordinal));
            if (tools)
            {
                string name = toolList.EnumerateArray().First(t => t.GetProperty("type").GetString() == "function").GetProperty("name").GetString()!;
                List<object> output = (List<object>)response["output"];
                ((Dictionary<string, object>)output[1])["name"] = name;
            }
            if (tools && body.Contains("xai-parallel", StringComparison.Ordinal))
            {
                List<object> items = (List<object>)response["output"];
                var second = new Dictionary<string, object>((Dictionary<string, object>)items[1]) { ["id"] = "fc-2", ["call_id"] = "call-2", ["arguments"] = "{\"city\":\"Tokyo\"}" };
                items.Add(second);
            }
            if (!stream) { await Send(c, Json(response)); return true; }
            StringBuilder wire = new();
            void Event(string type, object payload) { wire.Append("event: ").Append(type).Append("\r\ndata: ").Append(Json(payload)).Append("\r\n\r\n"); }
            wire.Append(": heartbeat\r\n\r\n");
            Event("response.created", Obj(("type", "response.created"), ("response", Obj(("id", "resp-test"), ("model", "test-model")))));
            Event("response.future_event", Obj(("type", "response.future_event"), ("extra", "retained")));
            if (body.Contains("xai-stream-error", StringComparison.Ordinal)) { Event("error", Obj(("type", "error"), ("message", "provider stream failure"))); await Send(c, wire.ToString(), stream: true); return true; }
            Event("response.reasoning_summary_text.delta", Obj(("type", "response.reasoning_summary_text.delta"), ("delta", "Thinking clearly.")));
            if (tools)
            {
                List<object> items = (List<object>)response["output"];
                for (int outputIndex = items.Count - 1; outputIndex >= 1; outputIndex--)
                {
                    Dictionary<string, object> function = (Dictionary<string, object>)items[outputIndex];
                    Dictionary<string, object> added = new(function) { ["arguments"] = "" };
                    Event("response.output_item.added", Obj(("type", "response.output_item.added"), ("output_index", outputIndex), ("item", added)));
                    string arguments = (string)function["arguments"];
                    Event("response.function_call_arguments.delta", Obj(("type", "response.function_call_arguments.delta"), ("output_index", outputIndex), ("item_id", function["id"]), ("delta", arguments[..5])));
                    Event("response.function_call_arguments.delta", Obj(("type", "response.function_call_arguments.delta"), ("output_index", outputIndex), ("item_id", function["id"]), ("delta", arguments[5..])));
                    Event("response.output_item.done", Obj(("type", "response.output_item.done"), ("output_index", outputIndex), ("item", function)));
                }
            }
            else Event("response.output_text.delta", Obj(("type", "response.output_text.delta"), ("delta", "pong")));
            if (!body.Contains("xai-truncated", StringComparison.Ordinal))
                Event(response["status"].ToString() == "incomplete" ? "response.incomplete" : "response.completed", Obj(("type", response["status"].ToString() == "incomplete" ? "response.incomplete" : "response.completed"), ("response", response)));
            wire.Append("data: [DONE]\r\n\r\n");
            await Send(c, wire.ToString(), stream: true); return true;
        }
        private static Dictionary<string, object> Response(string model, bool function, bool incomplete)
        {
            List<object> output = new()
            {
                Obj(("type", "reasoning"), ("id", "reason-1"), ("encrypted_content", "opaque-state"), ("summary", new[] { Obj(("type", "summary_text"), ("text", "Thinking clearly.")) }))
            };
            if (function) output.Add(Obj(("type", "function_call"), ("id", "fc-1"), ("call_id", "call-1"), ("name", "get_weather"), ("arguments", "{\"city\":\"Seattle\"}"), ("status", incomplete ? "incomplete" : "completed")));
            else output.Add(Obj(("type", "message"), ("id", "msg-1"), ("role", "assistant"), ("content", new[] { Obj(("type", "output_text"), ("text", "pong"), ("annotations", new[] { Obj(("type", "url_citation"), ("url", "https://example.com")) })) })));
            Dictionary<string, object> result = Obj(("id", "resp-test"), ("object", "response"), ("model", model), ("status", incomplete ? "incomplete" : "completed"), ("output", output),
                ("usage", Obj(("input_tokens", 13), ("output_tokens", 8), ("total_tokens", 21), ("input_tokens_details", Obj(("cached_tokens", 3))), ("output_tokens_details", Obj(("reasoning_tokens", 4))), ("cost_in_usd_ticks", 99))));
            if (incomplete) result["incomplete_details"] = Obj(("reason", "max_output_tokens"));
            return result;
        }
    }
}
