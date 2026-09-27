namespace Test.Shared
{
    using System.Globalization;
    using System.Text.Json;

    /// <summary>
    /// Dotted-path accessors over a recorded JSON request body, used to assert request translation for
    /// providers whose request shapes have no dedicated Local* model. Array elements are addressed by
    /// numeric path segments, for example "messages.1.content".
    /// </summary>
    internal sealed class LocalJson
    {
        private readonly JsonElement _Root;

        private LocalJson(JsonElement root)
        {
            _Root = root;
        }

        public static LocalJson Parse(string json)
        {
            using JsonDocument document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            return new LocalJson(document.RootElement.Clone());
        }

        public bool Has(string path)
        {
            return TryGet(path, out JsonElement _);
        }

        public string? Str(string path)
        {
            if (!TryGet(path, out JsonElement element)) return null;
            if (element.ValueKind == JsonValueKind.String) return element.GetString();
            if (element.ValueKind == JsonValueKind.Null) return null;
            return element.GetRawText();
        }

        public int? Int(string path)
        {
            if (!TryGet(path, out JsonElement element)) return null;
            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out int value)) return value;
            return null;
        }

        public double? Num(string path)
        {
            if (!TryGet(path, out JsonElement element)) return null;
            if (element.ValueKind == JsonValueKind.Number) return element.GetDouble();
            return null;
        }

        public bool? Bool(string path)
        {
            if (!TryGet(path, out JsonElement element)) return null;
            if (element.ValueKind == JsonValueKind.True) return true;
            if (element.ValueKind == JsonValueKind.False) return false;
            return null;
        }

        public int Count(string path)
        {
            if (!TryGet(path, out JsonElement element)) return -1;
            if (element.ValueKind == JsonValueKind.Array) return element.GetArrayLength();
            return -1;
        }

        public string Kind(string path)
        {
            if (!TryGet(path, out JsonElement element)) return "missing";
            return element.ValueKind.ToString();
        }

        private bool TryGet(string path, out JsonElement element)
        {
            element = _Root;
            if (string.IsNullOrEmpty(path)) return true;

            foreach (string segment in path.Split('.'))
            {
                if (element.ValueKind == JsonValueKind.Object)
                {
                    if (!element.TryGetProperty(segment, out JsonElement child)) return false;
                    element = child;
                }
                else if (element.ValueKind == JsonValueKind.Array
                    && int.TryParse(segment, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                {
                    if (index < 0 || index >= element.GetArrayLength()) return false;
                    element = element[index];
                }
                else
                {
                    return false;
                }
            }

            return true;
        }
    }
}
