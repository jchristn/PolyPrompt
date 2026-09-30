namespace PolyPrompt.Clients
{
    using System.Text.Json;

    /// <summary>
    /// Wire details shared by the Gemini (Google AI Studio) and Vertex AI clients.
    /// </summary>
    internal static class GeminiProtocol
    {
        internal const string DefaultEndpoint = "https://generativelanguage.googleapis.com";
        internal const string Header = "[Gemini] ";
        internal const string VertexHeader = "[VertexAI] ";

        // Keys accepted by Gemini's OpenAPI-subset Schema object (the functionDeclarations[].parameters field).
        private static readonly HashSet<string> _OpenApiSchemaKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "type", "format", "title", "description", "nullable", "enum", "default", "example",
            "properties", "required", "items", "minItems", "maxItems", "minProperties", "maxProperties",
            "minLength", "maxLength", "pattern", "minimum", "maximum", "anyOf", "propertyOrdering"
        };

        /// <summary>
        /// Attach the AI Studio API key as the <c>x-goog-api-key</c> header, which keeps it out of URLs, logs, and
        /// recorded call details.
        /// </summary>
        internal static void ApplyApiKey(HttpRequestMessage request, string? apiKey)
        {
            if (string.IsNullOrEmpty(apiKey)) return;
            request.Headers.Remove("x-goog-api-key");
            request.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);
        }

        /// <summary>
        /// The AI Studio model path, <c>{endpoint}/v1beta/models/{model}</c>. A leading <c>models/</c> on the model is accepted.
        /// </summary>
        internal static string ModelUrl(string endpoint, string model)
        {
            string name = model.StartsWith("models/", StringComparison.Ordinal) ? model.Substring(7) : model;
            return endpoint.TrimEnd('/') + "/v1beta/models/" + name;
        }

        /// <summary>
        /// The Vertex AI publisher model path, <c>{endpoint}/v1/projects/{project}/locations/{region}/publishers/google/models/{model}</c>.
        /// </summary>
        internal static string VertexModelUrl(string endpoint, string project, string region, string model)
        {
            return endpoint.TrimEnd('/')
                + "/v1/projects/" + Uri.EscapeDataString(project)
                + "/locations/" + Uri.EscapeDataString(region)
                + "/publishers/google/models/" + model;
        }

        /// <summary>
        /// The regional Vertex AI endpoint, <c>https://{region}-aiplatform.googleapis.com</c>, unless one is given.
        /// </summary>
        internal static string ResolveVertexEndpoint(string? endpoint, string region)
        {
            if (!string.IsNullOrEmpty(endpoint)) return endpoint;
            if (string.IsNullOrWhiteSpace(region)) throw new ArgumentNullException(nameof(region));
            return "https://" + region + "-aiplatform.googleapis.com";
        }

        /// <summary>
        /// Reduce a JSON Schema to the OpenAPI subset accepted by <c>functionDeclarations[].parameters</c>: unsupported keys
        /// are removed (and recorded in <paramref name="removed"/>), type arrays with <c>null</c> become <c>nullable</c>,
        /// <c>const</c> becomes a one-value <c>enum</c>, <c>oneOf</c> becomes <c>anyOf</c>, and enum values become strings.
        /// </summary>
        internal static object SanitizeOpenApiSchema(JsonElement schema, string path, List<string> removed)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();
            if (schema.ValueKind != JsonValueKind.Object)
            {
                removed.Add(string.IsNullOrEmpty(path) ? "(non-object schema)" : path);
                return result;
            }

            foreach (JsonProperty property in schema.EnumerateObject())
            {
                string key = property.Name;
                string keyPath = string.IsNullOrEmpty(path) ? key : path + "." + key;
                JsonElement value = property.Value;

                if (key == "type" && value.ValueKind == JsonValueKind.Array)
                {
                    List<string> types = value.EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.String)
                        .Select(item => item.GetString()!)
                        .ToList();
                    if (types.Remove("null")) result["nullable"] = true;
                    if (types.Count == 1)
                    {
                        result["type"] = types[0];
                    }
                    else if (types.Count > 1)
                    {
                        result["anyOf"] = types.Select(type => (object)new Dictionary<string, object> { { "type", type } }).ToList();
                    }
                }
                else if (key == "const")
                {
                    result["enum"] = new List<object> { value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText() };
                }
                else if (key == "properties" && value.ValueKind == JsonValueKind.Object)
                {
                    Dictionary<string, object> properties = new Dictionary<string, object>();
                    foreach (JsonProperty child in value.EnumerateObject())
                    {
                        properties[child.Name] = SanitizeOpenApiSchema(child.Value, keyPath + "." + child.Name, removed);
                    }
                    result[key] = properties;
                }
                else if (key == "items" && value.ValueKind == JsonValueKind.Object)
                {
                    result[key] = SanitizeOpenApiSchema(value, keyPath, removed);
                }
                else if ((key == "anyOf" || key == "oneOf") && value.ValueKind == JsonValueKind.Array)
                {
                    List<object> options = new List<object>();
                    int i = 0;
                    foreach (JsonElement option in value.EnumerateArray())
                    {
                        options.Add(SanitizeOpenApiSchema(option, keyPath + "." + i, removed));
                        i++;
                    }
                    result["anyOf"] = options;
                }
                else if (_OpenApiSchemaKeys.Contains(key) && key != "properties" && key != "items" && key != "anyOf")
                {
                    if (key == "enum" && value.ValueKind == JsonValueKind.Array)
                    {
                        result[key] = value.EnumerateArray()
                            .Where(item => item.ValueKind != JsonValueKind.Null)
                            .Select(item => (object)(item.ValueKind == JsonValueKind.String ? item.GetString()! : item.GetRawText()))
                            .ToList();
                    }
                    else
                    {
                        result[key] = value;
                    }
                }
                else
                {
                    removed.Add(keyPath);
                }
            }

            return result;
        }
    }
}
