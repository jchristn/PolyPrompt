namespace Test.Shared
{
    using System.Net;
    using System.Text;
    using System.Text.Json;

    /// <summary>
    /// Strict local routes that behave like Gemini 3 tool calling, selected by the model name <see cref="Model"/>.
    /// The native routes (Gemini and Vertex AI generateContent and streamGenerateContent) reject what the real API
    /// rejects: a content role other than user or model, a model turn whose first function call has no thought signature,
    /// a functionResponse.response that is not an object, and a functionDeclarations[].parameters schema with keys outside
    /// the OpenAPI subset. The OpenAI-compatible route (/v1/chat/completions) rejects a replayed assistant tool call
    /// without extra_content.google.thought_signature. A request that passes validation gets two parallel function calls
    /// (only the first signed, as Gemini does) or, once tool results are present, a final answer.
    /// </summary>
    internal static class LocalGeminiToolRoutes
    {
        /// <summary>
        /// Model name that selects these routes.
        /// </summary>
        public const string Model = "gemini-sigtest";

        /// <summary>
        /// Thought signature attached to the first function call.
        /// </summary>
        public const string Signature = "sig-seattle-Cr4DAYm";

        /// <summary>
        /// A different signature sent on a later streaming delta, which must not replace the first.
        /// </summary>
        public const string LateSignature = "sig-late";

        private static readonly HashSet<string> _OpenApiSchemaKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "type", "format", "title", "description", "nullable", "enum", "default", "example",
            "properties", "required", "items", "minItems", "maxItems", "minProperties", "maxProperties",
            "minLength", "maxLength", "pattern", "minimum", "maximum", "anyOf", "propertyOrdering"
        };

        public static async Task<bool> TryHandleAsync(HttpListenerContext context, string path, string requestBody)
        {
            if (path.Contains("/models/" + Model + ":", StringComparison.Ordinal))
            {
                await HandleNativeAsync(context, path, requestBody).ConfigureAwait(false);
                return true;
            }

            if (path == "/v1/chat/completions" && requestBody.Contains("\"model\":\"" + Model + "\"", StringComparison.Ordinal))
            {
                await HandleOpenAiCompatibleAsync(context, requestBody).ConfigureAwait(false);
                return true;
            }

            return false;
        }

        #region Native

        private static async Task HandleNativeAsync(HttpListenerContext context, string path, string requestBody)
        {
            using JsonDocument document = JsonDocument.Parse(requestBody);
            JsonElement root = document.RootElement;

            string? error = ValidateNativeRequest(root);
            if (error != null)
            {
                await WriteJsonAsync(context, 400, ErrorJson(error)).ConfigureAwait(false);
                return;
            }

            bool streaming = path.Contains(":streamGenerateContent", StringComparison.Ordinal);
            JsonElement contents = root.GetProperty("contents");
            JsonElement last = contents[contents.GetArrayLength() - 1];
            int functionResponses = last.GetProperty("parts").EnumerateArray().Count(part => part.TryGetProperty("functionResponse", out _));

            if (functionResponses > 0)
            {
                string text = "Answered " + functionResponses + " tool results.";
                string final = "{\"responseId\":\"sig-final\",\"modelVersion\":\"" + Model + "\",\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[{\"text\":\"" + text + "\"}]},\"finishReason\":\"STOP\",\"index\":0}]}";
                if (streaming)
                    await WriteSseAsync(context, final).ConfigureAwait(false);
                else
                    await WriteJsonAsync(context, 200, final).ConfigureAwait(false);
                return;
            }

            string first = "{\"functionCall\":{\"id\":\"fc-1\",\"name\":\"get_weather\",\"args\":{\"city\":\"Seattle\"}},\"thoughtSignature\":\"" + Signature + "\"}";
            string second = "{\"functionCall\":{\"id\":\"fc-2\",\"name\":\"get_weather\",\"args\":{\"city\":\"Portland\"}}}";

            if (streaming)
            {
                await WriteSseAsync(
                    context,
                    "{\"responseId\":\"sig-tools\",\"modelVersion\":\"" + Model + "\",\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[" + first + "]},\"index\":0}]}",
                    "{\"responseId\":\"sig-tools\",\"modelVersion\":\"" + Model + "\",\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[" + second + "]},\"finishReason\":\"STOP\",\"index\":0}]}").ConfigureAwait(false);
            }
            else
            {
                await WriteJsonAsync(
                    context,
                    200,
                    "{\"responseId\":\"sig-tools\",\"modelVersion\":\"" + Model + "\",\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[" + first + "," + second + "]},\"finishReason\":\"STOP\",\"index\":0}]}").ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Validate a native generateContent body the way Gemini 3 does. Returns an error message, or null when valid.
        /// </summary>
        internal static string? ValidateNativeRequest(JsonElement root)
        {
            if (root.TryGetProperty("tools", out JsonElement tools))
            {
                foreach (JsonElement tool in tools.EnumerateArray())
                {
                    if (!tool.TryGetProperty("functionDeclarations", out JsonElement declarations)) continue;
                    int index = 0;
                    foreach (JsonElement declaration in declarations.EnumerateArray())
                    {
                        if (declaration.TryGetProperty("parameters", out JsonElement parameters))
                        {
                            if (declaration.TryGetProperty("parametersJsonSchema", out _))
                                return "parameters and parametersJsonSchema are mutually exclusive";

                            string? unknown = FindUnknownSchemaKey(parameters);
                            if (unknown != null)
                                return "Invalid JSON payload received. Unknown name \"" + unknown + "\" at 'tools[0].function_declarations[" + index + "].parameters': Cannot find field.";
                        }
                        else if (declaration.TryGetProperty("parametersJsonSchema", out JsonElement jsonSchema)
                            && jsonSchema.ValueKind != JsonValueKind.Object)
                        {
                            return "parametersJsonSchema must be an object";
                        }
                        index++;
                    }
                }
            }

            if (!root.TryGetProperty("contents", out JsonElement contents) || contents.GetArrayLength() == 0)
                return "contents is required";

            int position = 0;
            foreach (JsonElement content in contents.EnumerateArray())
            {
                string? role = content.TryGetProperty("role", out JsonElement roleElement) ? roleElement.GetString() : null;
                if (role != "user" && role != "model")
                    return "Please use a valid role: user, model.";

                bool firstCall = true;
                foreach (JsonElement part in content.GetProperty("parts").EnumerateArray())
                {
                    position++;
                    if (part.TryGetProperty("functionCall", out JsonElement functionCall))
                    {
                        if (role != "model") return "functionCall parts must be in a model turn.";
                        if (firstCall && !(part.TryGetProperty("thoughtSignature", out JsonElement signature)
                            && signature.ValueKind == JsonValueKind.String
                            && !string.IsNullOrEmpty(signature.GetString())))
                        {
                            string name = functionCall.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? "" : "";
                            return "Function call is missing a thought_signature in functionCall parts. Additional data, function call `" + name + "`, position " + position + ".";
                        }
                        firstCall = false;
                    }

                    if (part.TryGetProperty("functionResponse", out JsonElement functionResponse))
                    {
                        if (role != "user") return "functionResponse parts must be in a user turn.";
                        if (!functionResponse.TryGetProperty("response", out JsonElement response) || response.ValueKind != JsonValueKind.Object)
                            return "Invalid value at 'contents.function_response.response' (type.googleapis.com/google.protobuf.Struct)";
                    }
                }
            }

            return null;
        }

        private static string? FindUnknownSchemaKey(JsonElement schema)
        {
            if (schema.ValueKind != JsonValueKind.Object) return null;

            foreach (JsonProperty property in schema.EnumerateObject())
            {
                if (!_OpenApiSchemaKeys.Contains(property.Name)) return property.Name;

                if (property.Name == "type" && property.Value.ValueKind != JsonValueKind.String) return "type";

                if (property.Name == "properties" && property.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty child in property.Value.EnumerateObject())
                    {
                        string? unknown = FindUnknownSchemaKey(child.Value);
                        if (unknown != null) return unknown;
                    }
                }
                else if (property.Name == "items")
                {
                    string? unknown = FindUnknownSchemaKey(property.Value);
                    if (unknown != null) return unknown;
                }
                else if (property.Name == "anyOf" && property.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement option in property.Value.EnumerateArray())
                    {
                        string? unknown = FindUnknownSchemaKey(option);
                        if (unknown != null) return unknown;
                    }
                }
            }

            return null;
        }

        #endregion

        #region OpenAI-Compatible

        private static async Task HandleOpenAiCompatibleAsync(HttpListenerContext context, string requestBody)
        {
            using JsonDocument document = JsonDocument.Parse(requestBody);
            JsonElement root = document.RootElement;

            bool hasToolResult = false;
            foreach (JsonElement message in root.GetProperty("messages").EnumerateArray())
            {
                string? role = message.TryGetProperty("role", out JsonElement r) ? r.GetString() : null;
                if (role == "tool") hasToolResult = true;

                if (role == "assistant" && message.TryGetProperty("tool_calls", out JsonElement toolCalls) && toolCalls.GetArrayLength() > 0)
                {
                    JsonElement first = toolCalls[0];
                    bool signed = first.TryGetProperty("extra_content", out JsonElement extra)
                        && extra.TryGetProperty("google", out JsonElement google)
                        && google.TryGetProperty("thought_signature", out JsonElement signature)
                        && !string.IsNullOrEmpty(signature.GetString());
                    if (!signed)
                    {
                        await WriteJsonAsync(context, 400, "[{\"error\":{\"code\":400,\"message\":\"Function call is missing a thought_signature in functionCall parts.\",\"status\":\"INVALID_ARGUMENT\"}}]").ConfigureAwait(false);
                        return;
                    }
                }
            }

            bool streaming = root.TryGetProperty("stream", out JsonElement stream) && stream.ValueKind == JsonValueKind.True;

            if (hasToolResult)
            {
                if (streaming)
                {
                    await WriteSseAsync(
                        context,
                        "{\"id\":\"sig-oa-final\",\"object\":\"chat.completion.chunk\",\"model\":\"" + Model + "\",\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"Done.\"},\"finish_reason\":\"stop\"}]}",
                        "[DONE]").ConfigureAwait(false);
                }
                else
                {
                    await WriteJsonAsync(
                        context,
                        200,
                        "{\"id\":\"sig-oa-final\",\"object\":\"chat.completion\",\"model\":\"" + Model + "\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"Done.\"},\"finish_reason\":\"stop\"}]}").ConfigureAwait(false);
                }
                return;
            }

            string extraContent = "\"extra_content\":{\"google\":{\"thought_signature\":\"" + Signature + "\"}}";

            if (streaming)
            {
                await WriteSseAsync(
                    context,
                    "{\"id\":\"sig-oa-tools\",\"object\":\"chat.completion.chunk\",\"model\":\"" + Model + "\",\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"tool_calls\":[{\"index\":0,\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"get_weather\",\"arguments\":\"{\\\"city\\\":\"}," + extraContent + "}]},\"finish_reason\":null}]}",
                    "{\"id\":\"sig-oa-tools\",\"object\":\"chat.completion.chunk\",\"model\":\"" + Model + "\",\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"\\\"Seattle\\\"}\"},\"extra_content\":{\"google\":{\"thought_signature\":\"" + LateSignature + "\"}}}]},\"finish_reason\":null}]}",
                    "{\"id\":\"sig-oa-tools\",\"object\":\"chat.completion.chunk\",\"model\":\"" + Model + "\",\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":1,\"id\":\"call_2\",\"type\":\"function\",\"function\":{\"name\":\"get_weather\",\"arguments\":\"{\\\"city\\\":\\\"Portland\\\"}\"}}]},\"finish_reason\":\"tool_calls\"}]}",
                    "[DONE]").ConfigureAwait(false);
            }
            else
            {
                await WriteJsonAsync(
                    context,
                    200,
                    "{\"id\":\"sig-oa-tools\",\"object\":\"chat.completion\",\"model\":\"" + Model + "\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":null,\"tool_calls\":["
                        + "{\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"get_weather\",\"arguments\":\"{\\\"city\\\":\\\"Seattle\\\"}\"}," + extraContent + "},"
                        + "{\"id\":\"call_2\",\"type\":\"function\",\"function\":{\"name\":\"get_weather\",\"arguments\":\"{\\\"city\\\":\\\"Portland\\\"}\"}}"
                        + "]},\"finish_reason\":\"tool_calls\"}]}").ConfigureAwait(false);
            }
        }

        #endregion

        #region Writers

        private static string ErrorJson(string message)
        {
            return "{\"error\":{\"code\":400,\"message\":" + JsonSerializer.Serialize(message) + ",\"status\":\"INVALID_ARGUMENT\"}}";
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

        private static async Task WriteSseAsync(HttpListenerContext context, params string[] events)
        {
            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/event-stream";
            context.Response.SendChunked = true;

            foreach (string data in events)
            {
                byte[] bytes = Encoding.UTF8.GetBytes("data: " + data + "\n\n");
                await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                await context.Response.OutputStream.FlushAsync().ConfigureAwait(false);
            }

            context.Response.Close();
        }

        #endregion
    }
}
