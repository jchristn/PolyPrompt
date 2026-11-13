namespace Test.Shared
{
    using System.Net;
    using System.Text;
    using System.Text.Json;
    using PolyPrompt.Helpers;

    /// <summary>
    /// OpenAI Responses API contract fixture (and the Azure OpenAI v1 routes). Keyed by its own bearer key, and sharing no
    /// helpers with the xAI fixture. Shapes follow OpenAI's OpenAPI spec: output items, annotations, usage details
    /// (cached and cache-write tokens), incomplete reasons, and the SSE event names.
    /// </summary>
    internal static class LocalOpenAiResponsesRoutes
    {
        public const string Key = "openai-responses-key";

        // Text with an accent, CJK, and a non-BMP emoji, so offset unit errors show up.
        public const string CitedText = "Café 東京 😀 see source.";

        private static string Json(object value) => new Serializer().SerializeJson(value, false);
        private static Dictionary<string, object> Obj(params (string, object)[] fields) => fields.ToDictionary(f => f.Item1, f => f.Item2);

        private static async Task Send(HttpListenerContext c, string value, int status = 200, bool stream = false)
        {
            c.Response.StatusCode = status;
            c.Response.ContentType = stream ? "text/event-stream" : "application/json";
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            await c.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
            c.Response.Close();
        }

        public static async Task<bool> TryHandleAsync(HttpListenerContext c, string path, string body)
        {
            bool azure = path.StartsWith("/openai/v1/", StringComparison.Ordinal);
            bool keyed = c.Request.Headers["Authorization"] == "Bearer " + Key;
            if (!azure && !keyed) return false;

            if (azure && string.IsNullOrEmpty(c.Request.Headers["api-key"]) && string.IsNullOrEmpty(c.Request.Headers["Authorization"]))
            {
                await Send(c, "{\"error\":{\"code\":\"401\",\"message\":\"missing credentials\"}}", 401);
                return true;
            }

            string route = azure ? path.Substring("/openai/v1".Length) : (path.StartsWith("/v1/", StringComparison.Ordinal) ? path.Substring(3) : path);
            string method = c.Request.HttpMethod;

            if (route == "/models" && method == "GET")
            {
                await Send(c, "{\"object\":\"list\",\"data\":[{\"id\":\"test-model\",\"object\":\"model\",\"created\":1700000000,\"owned_by\":\"openai\"}]}");
                return true;
            }
            if (route.StartsWith("/models/", StringComparison.Ordinal) && method == "GET")
            {
                string id = Uri.UnescapeDataString(route.Substring("/models/".Length));
                if (id != "test-model") { await Send(c, "{\"error\":{\"message\":\"not found\"}}", 404); return true; }
                await Send(c, "{\"id\":\"test-model\",\"object\":\"model\",\"created\":1700000000,\"owned_by\":\"openai\"}");
                return true;
            }
            if (route == "/embeddings")
            {
                await Send(c, "{\"object\":\"list\",\"data\":[{\"index\":1,\"embedding\":[4.0,5.0,6.0]},{\"index\":0,\"embedding\":[1.0,2.0,3.0]}],\"model\":\"test-embedding\",\"usage\":{\"prompt_tokens\":4,\"total_tokens\":4}}");
                return true;
            }
            if (route == "/chat/completions")
            {
                // Azure's v1 Chat Completions route, for OpenAiCompatibleClient with AuthPlacement.Header("api-key").
                await Send(c, "{\"id\":\"az-v1-chat\",\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"pong\"},\"finish_reason\":\"stop\",\"index\":0}]}");
                return true;
            }
            if (route.Contains("/input_items", StringComparison.Ordinal))
            {
                await Send(c, "{\"object\":\"list\",\"data\":[{\"id\":\"input-1\",\"type\":\"message\",\"role\":\"user\",\"content\":[{\"type\":\"input_text\",\"text\":\"hello\"}]}],\"has_more\":false,\"first_id\":\"input-1\",\"last_id\":\"input-1\"}");
                return true;
            }
            if (route == "/responses/compact")
            {
                await Send(c, "{\"id\":\"cmp-1\",\"object\":\"response.compaction\",\"output\":[{\"type\":\"compaction\",\"encrypted_content\":\"oai-compact-state\"}],\"usage\":{\"input_tokens\":10,\"output_tokens\":2,\"total_tokens\":12}}");
                return true;
            }
            if (route == "/responses/input_tokens")
            {
                await Send(c, "{\"object\":\"response.input_tokens\",\"input_tokens\":42}");
                return true;
            }
            if (route.StartsWith("/responses/", StringComparison.Ordinal) && route.EndsWith("/cancel", StringComparison.Ordinal))
            {
                Dictionary<string, object> cancelled = Response("test-model", false, null, false);
                cancelled["status"] = "cancelled";
                cancelled["store"] = true;
                await Send(c, Json(cancelled));
                return true;
            }
            if (route.StartsWith("/responses/", StringComparison.Ordinal) && method == "DELETE")
            {
                await Send(c, "{\"id\":\"oai-resp\",\"object\":\"response.deleted\",\"deleted\":true}");
                return true;
            }
            if (route.StartsWith("/responses/", StringComparison.Ordinal) && method == "GET")
            {
                Dictionary<string, object> stored = Response("test-model", false, null, false);
                stored["store"] = true;
                await Send(c, Json(stored));
                return true;
            }
            if (route != "/responses") return false;

            if (body.Contains("oai-http-error", StringComparison.Ordinal)) { await Send(c, "{\"error\":{\"message\":\"rate limited\",\"type\":\"rate_limit\"}}", 429); return true; }
            if (body.Contains("oai-not-found", StringComparison.Ordinal)) { await Send(c, "{\"error\":{\"message\":\"Not found\"}}", 404); return true; }
            if (body.Contains("oai-invalid-encrypted", StringComparison.Ordinal)) { await Send(c, "{\"error\":{\"code\":\"invalid_encrypted_content\",\"message\":\"The encrypted content could not be verified.\"}}", 400); return true; }
            if (body.Contains("oai-malformed", StringComparison.Ordinal)) { await Send(c, "{\"status\":\"completed\",\"output\":[]}"); return true; }

            using JsonDocument document = JsonDocument.Parse(string.IsNullOrEmpty(body) ? "{}" : body);
            JsonElement request = document.RootElement;
            bool stream = request.TryGetProperty("stream", out JsonElement streaming) && streaming.ValueKind == JsonValueKind.True;
            bool tools = request.TryGetProperty("tools", out JsonElement toolList) && toolList.ValueKind == JsonValueKind.Array
                && toolList.EnumerateArray().Any(t => t.TryGetProperty("type", out JsonElement type) && type.GetString() == "function")
                && !body.Contains("function_call_output", StringComparison.Ordinal)
                && !(request.TryGetProperty("tool_choice", out JsonElement choice) && choice.ValueKind == JsonValueKind.String && choice.GetString() == "none");
            string? incomplete = body.Contains("oai-incomplete-filter", StringComparison.Ordinal) ? "content_filter"
                : body.Contains("oai-incomplete-messages", StringComparison.Ordinal) ? "max_messages"
                : body.Contains("oai-incomplete", StringComparison.Ordinal) ? "max_output_tokens" : null;
            bool citations = body.Contains("oai-citations", StringComparison.Ordinal);
            string model = request.TryGetProperty("model", out JsonElement m) ? m.GetString() ?? "test-model" : "test-model";

            Dictionary<string, object> response = Response(model, tools, incomplete, citations);
            if (request.TryGetProperty("store", out JsonElement store) && store.ValueKind == JsonValueKind.True) response["store"] = true;
            if (request.TryGetProperty("background", out JsonElement background) && background.ValueKind == JsonValueKind.True) response["status"] = "queued";
            if (body.Contains("oai-failed", StringComparison.Ordinal))
            {
                response["status"] = "failed";
                response["error"] = Obj(("code", "server_error"), ("message", "The model failed."));
            }
            if (tools)
            {
                string name = toolList.EnumerateArray().First(t => t.GetProperty("type").GetString() == "function").GetProperty("name").GetString()!;
                List<object> output = (List<object>)response["output"];
                ((Dictionary<string, object>)output[1])["name"] = name;
                if (body.Contains("oai-parallel", StringComparison.Ordinal))
                {
                    Dictionary<string, object> second = new((Dictionary<string, object>)output[1]) { ["id"] = "fc-oai-2", ["call_id"] = "call-oai-2", ["arguments"] = "{\"city\":\"Tokyo\"}" };
                    output.Add(second);
                }
            }

            if (!stream) { await Send(c, Json(response)); return true; }

            StringBuilder wire = new();
            void Event(string type, Dictionary<string, object> payload)
            {
                payload["type"] = type;
                wire.Append("event: ").Append(type).Append('\n').Append("data: ").Append(Json(payload)).Append("\n\n");
            }

            Event("response.created", Obj(("response", Obj(("id", "oai-resp"), ("model", model), ("status", "in_progress"))), ("sequence_number", 0)));
            Event("response.in_progress", Obj(("response", Obj(("id", "oai-resp"), ("model", model), ("status", "in_progress"))), ("sequence_number", 1)));
            if (body.Contains("oai-stream-error", StringComparison.Ordinal))
            {
                Event("error", Obj(("code", "server_error"), ("message", "provider stream failure")));
                await Send(c, wire.ToString(), stream: true);
                return true;
            }
            Event("response.output_item.added", Obj(("output_index", 0), ("item", Obj(("type", "reasoning"), ("id", "rs-oai-1"), ("summary", new object[0])))));
            Event("response.reasoning_summary_part.added", Obj(("output_index", 0), ("item_id", "rs-oai-1"), ("summary_index", 0), ("part", Obj(("type", "summary_text"), ("text", "")))));
            Event("response.reasoning_summary_text.delta", Obj(("output_index", 0), ("item_id", "rs-oai-1"), ("summary_index", 0), ("delta", "Planning ")));
            Event("response.reasoning_summary_text.delta", Obj(("output_index", 0), ("item_id", "rs-oai-1"), ("summary_index", 0), ("delta", "the answer.")));
            Event("response.reasoning_summary_text.done", Obj(("output_index", 0), ("item_id", "rs-oai-1"), ("summary_index", 0), ("text", "Planning the answer.")));
            Event("response.reasoning_summary_part.done", Obj(("output_index", 0), ("item_id", "rs-oai-1"), ("summary_index", 0), ("part", Obj(("type", "summary_text"), ("text", "Planning the answer.")))));
            Event("response.output_item.done", Obj(("output_index", 0), ("item", ((List<object>)response["output"])[0])));
            Event("response.web_search_call.searching", Obj(("output_index", 0), ("item_id", "ws-1")));
            Event("response.compaction.compacting", Obj(("sequence_number", 9)));
            Event("response.future_event", Obj(("extra", "retained")));
            List<object> items = (List<object>)response["output"];
            if (tools)
            {
                for (int outputIndex = items.Count - 1; outputIndex >= 1; outputIndex--)
                {
                    Dictionary<string, object> function = (Dictionary<string, object>)items[outputIndex];
                    Dictionary<string, object> added = new(function) { ["arguments"] = "" };
                    Event("response.output_item.added", Obj(("output_index", outputIndex), ("item", added)));
                    string arguments = (string)function["arguments"];
                    Event("response.function_call_arguments.delta", Obj(("output_index", outputIndex), ("item_id", function["id"]), ("delta", arguments[..4])));
                    Event("response.function_call_arguments.delta", Obj(("output_index", outputIndex), ("item_id", function["id"]), ("delta", arguments[4..])));
                    Event("response.function_call_arguments.done", Obj(("output_index", outputIndex), ("item_id", function["id"]), ("arguments", arguments)));
                    Event("response.output_item.done", Obj(("output_index", outputIndex), ("item", function)));
                }
            }
            else
            {
                Dictionary<string, object> message = (Dictionary<string, object>)items[1];
                Event("response.output_item.added", Obj(("output_index", 1), ("item", Obj(("type", "message"), ("id", message["id"]), ("role", "assistant"), ("content", new object[0])))));
                Event("response.content_part.added", Obj(("output_index", 1), ("item_id", message["id"]), ("content_index", 0), ("part", Obj(("type", "output_text"), ("text", "")))));
                string text = citations ? CitedText : "pong";
                Event("response.output_text.delta", Obj(("output_index", 1), ("item_id", message["id"]), ("content_index", 0), ("delta", text[..2])));
                Event("response.output_text.delta", Obj(("output_index", 1), ("item_id", message["id"]), ("content_index", 0), ("delta", text[2..])));
                if (citations)
                    Event("response.output_text.annotation.added", Obj(("output_index", 1), ("item_id", message["id"]), ("content_index", 0), ("annotation_index", 0), ("annotation", UrlCitation())));
                Event("response.output_text.done", Obj(("output_index", 1), ("item_id", message["id"]), ("content_index", 0), ("text", text)));
                Event("response.content_part.done", Obj(("output_index", 1), ("item_id", message["id"]), ("content_index", 0), ("part", Obj(("type", "output_text"), ("text", text)))));
                Event("response.output_item.done", Obj(("output_index", 1), ("item", message)));
            }
            if (!body.Contains("oai-truncated", StringComparison.Ordinal))
            {
                string status = response["status"].ToString()!;
                string terminal = status == "incomplete" ? "response.incomplete" : status == "failed" ? "response.failed" : "response.completed";
                Event(terminal, Obj(("response", response)));
            }
            await Send(c, wire.ToString(), stream: true);
            return true;
        }

        private static Dictionary<string, object> UrlCitation()
        {
            // "see source" in CitedText: "Café 東京 😀 " is 11 UTF-16 code units (the emoji is a surrogate pair).
            int start = CitedText.IndexOf("see source", StringComparison.Ordinal);
            return Obj(("type", "url_citation"), ("url", "https://example.com/source"), ("title", "Example Source"), ("start_index", start), ("end_index", start + "see source".Length));
        }

        private static Dictionary<string, object> Response(string model, bool function, string? incomplete, bool citations)
        {
            List<object> output = new()
            {
                Obj(("type", "reasoning"), ("id", "rs-oai-1"), ("encrypted_content", "oai-encrypted-state"), ("summary", new[] { Obj(("type", "summary_text"), ("text", "Planning the answer.")) }))
            };
            if (function)
            {
                output.Add(Obj(("type", "function_call"), ("id", "fc-oai-1"), ("call_id", "call-oai-1"), ("name", "get_weather"), ("arguments", "{\"city\":\"Seattle\"}"), ("status", "completed")));
            }
            else if (citations)
            {
                // Two output_text parts; the second's file citation is part-relative and must be shifted.
                output.Add(Obj(("type", "message"), ("id", "msg-oai-1"), ("role", "assistant"), ("status", "completed"), ("content", new[]
                {
                    Obj(("type", "output_text"), ("text", CitedText), ("annotations", new object[] { UrlCitation() })),
                    Obj(("type", "output_text"), ("text", " More."), ("annotations", new object[]
                    {
                        Obj(("type", "container_file_citation"), ("container_id", "cntr-1"), ("file_id", "file-1"), ("filename", "notes.txt"), ("start_index", 1), ("end_index", 5)),
                        Obj(("type", "file_citation"), ("file_id", "file-2"), ("filename", "data.csv"), ("index", 6)),
                    })),
                })));
            }
            else
            {
                output.Add(Obj(("type", "message"), ("id", "msg-oai-1"), ("role", "assistant"), ("status", "completed"), ("content", new[] { Obj(("type", "output_text"), ("text", "pong"), ("annotations", new object[0])) })));
            }

            Dictionary<string, object> result = Obj(("id", "oai-resp"), ("object", "response"), ("created_at", 1700000000), ("model", model),
                ("status", incomplete != null ? "incomplete" : "completed"), ("output", output), ("store", false),
                ("usage", Obj(("input_tokens", 20), ("output_tokens", 9), ("total_tokens", 29),
                    ("input_tokens_details", Obj(("cached_tokens", 5), ("cache_write_tokens", 2))),
                    ("output_tokens_details", Obj(("reasoning_tokens", 6))))));
            if (incomplete != null) result["incomplete_details"] = Obj(("reason", incomplete));
            return result;
        }
    }
}
