namespace PolyPrompt.Helpers
{
    using System.Globalization;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// JSON serializer used by PolyPrompt clients, built on System.Text.Json.
    /// Untyped values (object, Dictionary&lt;string, object&gt;, List&lt;object&gt;) deserialize to <see cref="JsonElement"/>.
    /// Null properties are omitted when writing (dictionary entries are always written), enums are written as strings,
    /// and reads tolerate trailing commas, comments, and numbers written as strings.
    /// </summary>
    public sealed class Serializer
    {
        #region Private-Members

        private static readonly JsonSerializerOptions _Compact = CreateOptions(false);
        private static readonly JsonSerializerOptions _Pretty = CreateOptions(true);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Serialize an object to JSON.
        /// </summary>
        /// <param name="obj">Object to serialize. A null object serializes to the JSON literal null.</param>
        /// <param name="pretty">True to indent the output.</param>
        /// <returns>JSON text.</returns>
        public string SerializeJson(object? obj, bool pretty = true)
        {
            if (obj == null) return "null";
            return JsonSerializer.Serialize(obj, obj.GetType(), pretty ? _Pretty : _Compact);
        }

        /// <summary>
        /// Deserialize JSON text to an instance of the specified type.
        /// </summary>
        /// <typeparam name="T">Target type.</typeparam>
        /// <param name="json">JSON text.</param>
        /// <returns>The deserialized instance, or default when the JSON literal is null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when json is null.</exception>
        /// <exception cref="JsonException">Thrown when json is not valid JSON or does not match the target type.</exception>
        public T? DeserializeJson<T>(string json)
        {
            ArgumentNullException.ThrowIfNull(json);
            return JsonSerializer.Deserialize<T>(json, _Compact);
        }

        #endregion

        #region Private-Methods

        private static JsonSerializerOptions CreateOptions(bool pretty)
        {
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

        #endregion

        #region Private-Classes

        private sealed class UtcDateTimeConverter : JsonConverter<DateTime>
        {
            private const string Format = "yyyy-MM-ddTHH:mm:ss.ffffffZ";

            public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                string? value = reader.GetString();
                if (string.IsNullOrEmpty(value)) return default;
                return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
            }

            public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
            {
                writer.WriteStringValue(value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture));
            }
        }

        #endregion
    }
}
