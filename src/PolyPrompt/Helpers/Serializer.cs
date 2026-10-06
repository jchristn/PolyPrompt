namespace PolyPrompt.Helpers
{
    using System.Buffers;
    using System.Collections;
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Text.Json.Serialization;
    using System.Text.Json.Serialization.Metadata;

    /// <summary>
    /// JSON serializer used by PolyPrompt clients, built on System.Text.Json and safe for Native AOT and trimming.
    /// Untyped values (object, Dictionary&lt;string, object&gt;, List&lt;object&gt;) deserialize to <see cref="JsonElement"/>.
    /// Null properties are omitted when writing (dictionary entries are always written), enums are written as strings,
    /// and reads tolerate trailing commas, comments, and numbers written as strings.
    /// </summary>
    /// <remarks>
    /// Graphs made of dictionaries, lists, arrays, strings, numbers, booleans, enums, dates, GUIDs, byte arrays,
    /// <see cref="JsonElement"/>, <see cref="JsonDocument"/>, and <see cref="JsonNode"/> are written without reflection.
    /// Any other type is resolved through, in order: resolvers registered with <see cref="AddTypeInfoResolver"/>,
    /// <see cref="PolyPromptJsonContext"/>, and reflection when the application allows it
    /// (<see cref="IsReflectionEnabled"/>). In a Native AOT or trimmed application reflection is off, so a type outside
    /// those sources raises <see cref="NotSupportedException"/>; register a source-generated
    /// <see cref="JsonSerializerContext"/> that includes it, or use an overload that takes a <see cref="JsonTypeInfo{T}"/>.
    /// </remarks>
    public sealed class Serializer
    {
        #region Public-Members

        /// <summary>
        /// True when reflection-based serialization is available as a fallback for types that no registered resolver
        /// or <see cref="PolyPromptJsonContext"/> covers. False in Native AOT and trimmed applications.
        /// </summary>
        public static bool IsReflectionEnabled => JsonSerializer.IsReflectionEnabledByDefault;

        /// <summary>
        /// Maximum nesting depth written for dictionary and list graphs. Deeper graphs, including cyclic ones, raise
        /// <see cref="JsonException"/>.
        /// </summary>
        public const int MaxDepth = 64;

        #endregion

        #region Private-Members

        private static readonly object _Lock = new object();
        private static IJsonTypeInfoResolver[] _Resolvers = Array.Empty<IJsonTypeInfoResolver>();
        private static JsonSerializerOptions _Compact = CreateOptions(false, _Resolvers);
        private static JsonSerializerOptions _Pretty = CreateOptions(true, _Resolvers);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register a type metadata resolver, typically a source-generated <see cref="JsonSerializerContext"/>, so
        /// PolyPrompt can serialize and deserialize your own types (tool argument models, decision state objects, values
        /// placed in tool parameter schemas) without reflection. Registered resolvers are consulted before
        /// <see cref="PolyPromptJsonContext"/> and apply to every PolyPrompt client in the process. Registering the same
        /// resolver twice has no effect.
        /// </summary>
        /// <param name="resolver">Resolver to register.</param>
        /// <exception cref="ArgumentNullException">Thrown when resolver is null.</exception>
        public static void AddTypeInfoResolver(IJsonTypeInfoResolver resolver)
        {
            ArgumentNullException.ThrowIfNull(resolver);

            lock (_Lock)
            {
                if (Array.IndexOf(_Resolvers, resolver) >= 0) return;

                IJsonTypeInfoResolver[] resolvers = new IJsonTypeInfoResolver[_Resolvers.Length + 1];
                Array.Copy(_Resolvers, resolvers, _Resolvers.Length);
                resolvers[_Resolvers.Length] = resolver;

                JsonSerializerOptions compact = CreateOptions(false, resolvers);
                JsonSerializerOptions pretty = CreateOptions(true, resolvers);

                _Resolvers = resolvers;
                Volatile.Write(ref _Compact, compact);
                Volatile.Write(ref _Pretty, pretty);
            }
        }

        /// <summary>
        /// Serialize an object to JSON.
        /// </summary>
        /// <param name="obj">Object to serialize. A null object serializes to the JSON literal null.</param>
        /// <param name="pretty">True to indent the output.</param>
        /// <returns>JSON text.</returns>
        /// <exception cref="NotSupportedException">Thrown when the graph contains a type with no available metadata
        /// (see the remarks on <see cref="Serializer"/>).</exception>
        /// <exception cref="JsonException">Thrown when the graph is deeper than <see cref="MaxDepth"/>.</exception>
        public string SerializeJson(object? obj, bool pretty = true)
        {
            JsonSerializerOptions options = pretty ? Volatile.Read(ref _Pretty) : Volatile.Read(ref _Compact);
            ArrayBufferWriter<byte> buffer = new ArrayBufferWriter<byte>();

            using (Utf8JsonWriter writer = new Utf8JsonWriter(buffer, WriterOptions(options)))
            {
                WriteValue(writer, obj, options, 0);
            }

            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }

        /// <summary>
        /// Serialize a value to JSON with explicit type metadata, for example from a source-generated
        /// <see cref="JsonSerializerContext"/>. The metadata's own options control naming and null handling.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="obj">Value to serialize.</param>
        /// <param name="typeInfo">Type metadata.</param>
        /// <param name="pretty">True to indent the output.</param>
        /// <returns>JSON text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when typeInfo is null.</exception>
        public string SerializeJson<T>(T obj, JsonTypeInfo<T> typeInfo, bool pretty = true)
        {
            ArgumentNullException.ThrowIfNull(typeInfo);

            ArrayBufferWriter<byte> buffer = new ArrayBufferWriter<byte>();
            using (Utf8JsonWriter writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = pretty, Encoder = typeInfo.Options.Encoder }))
            {
                JsonSerializer.Serialize(writer, obj, typeInfo);
            }

            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }

        /// <summary>
        /// Deserialize JSON text to an instance of the specified type.
        /// </summary>
        /// <typeparam name="T">Target type.</typeparam>
        /// <param name="json">JSON text.</param>
        /// <returns>The deserialized instance, or default when the JSON literal is null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when json is null.</exception>
        /// <exception cref="JsonException">Thrown when json is not valid JSON or does not match the target type.</exception>
        /// <exception cref="NotSupportedException">Thrown when no metadata is available for T (see the remarks on
        /// <see cref="Serializer"/>).</exception>
        public T? DeserializeJson<T>(string json)
        {
            ArgumentNullException.ThrowIfNull(json);
            return JsonSerializer.Deserialize(json, (JsonTypeInfo<T>)ResolveTypeInfo(typeof(T), Volatile.Read(ref _Compact)));
        }

        /// <summary>
        /// Deserialize JSON text with explicit type metadata, for example from a source-generated
        /// <see cref="JsonSerializerContext"/>. The metadata's own options control naming and read tolerance.
        /// </summary>
        /// <typeparam name="T">Target type.</typeparam>
        /// <param name="json">JSON text.</param>
        /// <param name="typeInfo">Type metadata.</param>
        /// <returns>The deserialized instance, or default when the JSON literal is null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when json or typeInfo is null.</exception>
        /// <exception cref="JsonException">Thrown when json is not valid JSON or does not match the target type.</exception>
        public T? DeserializeJson<T>(string json, JsonTypeInfo<T> typeInfo)
        {
            ArgumentNullException.ThrowIfNull(json);
            ArgumentNullException.ThrowIfNull(typeInfo);
            return JsonSerializer.Deserialize(json, typeInfo);
        }

        #endregion

        #region Private-Methods

        private static JsonSerializerOptions CreateOptions(bool pretty, IJsonTypeInfoResolver[] resolvers)
        {
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                WriteIndented = pretty,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                NumberHandling = JsonNumberHandling.AllowReadingFromString,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };

            options.Converters.Add(new UtcDateTimeConverter());
            foreach (IJsonTypeInfoResolver resolver in resolvers) options.TypeInfoResolverChain.Add(resolver);
            options.TypeInfoResolverChain.Add(PolyPromptJsonContext.Default);
            if (JsonSerializer.IsReflectionEnabledByDefault) AddReflectionFallback(options);

            options.MakeReadOnly();
            return options;
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Only called when JsonSerializer.IsReflectionEnabledByDefault is true. Trimmed and Native AOT applications set that feature switch to false, so this code is removed.")]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Only called when JsonSerializer.IsReflectionEnabledByDefault is true. Trimmed and Native AOT applications set that feature switch to false, so this code is removed.")]
        private static void AddReflectionFallback(JsonSerializerOptions options)
        {
            options.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());
            options.Converters.Add(new JsonStringEnumConverter());
        }

        private static JsonWriterOptions WriterOptions(JsonSerializerOptions options)
        {
            return new JsonWriterOptions
            {
                Indented = options.WriteIndented,
                Encoder = options.Encoder
            };
        }

        private static JsonTypeInfo ResolveTypeInfo(Type type, JsonSerializerOptions options)
        {
            try
            {
                return options.GetTypeInfo(type);
            }
            catch (NotSupportedException ex) when (!JsonSerializer.IsReflectionEnabledByDefault)
            {
                throw new NotSupportedException(
                    "PolyPrompt has no JSON metadata for type '" + type.FullName + "'. Reflection-based serialization is "
                    + "disabled in this application (for example under Native AOT or trimming). Add the type to a "
                    + "source-generated JsonSerializerContext and register it with Serializer.AddTypeInfoResolver, or "
                    + "use an overload that takes a JsonTypeInfo<T>.",
                    ex);
            }
        }

        private static void WriteValue(Utf8JsonWriter writer, object? value, JsonSerializerOptions options, int depth)
        {
            if (depth > MaxDepth)
                throw new JsonException("The object graph is nested more than " + MaxDepth + " levels deep. It may contain a cycle.");

            switch (value)
            {
                case null:
                    writer.WriteNullValue();
                    return;
                case string text:
                    writer.WriteStringValue(text);
                    return;
                case bool flag:
                    writer.WriteBooleanValue(flag);
                    return;
                case JsonElement element:
                    if (element.ValueKind == JsonValueKind.Undefined) writer.WriteNullValue();
                    else element.WriteTo(writer);
                    return;
                case JsonDocument document:
                    document.WriteTo(writer);
                    return;
                case JsonNode node:
                    node.WriteTo(writer, options);
                    return;
                case int number:
                    writer.WriteNumberValue(number);
                    return;
                case long number:
                    writer.WriteNumberValue(number);
                    return;
                case double number:
                    writer.WriteNumberValue(number);
                    return;
                case float number:
                    writer.WriteNumberValue(number);
                    return;
                case decimal number:
                    writer.WriteNumberValue(number);
                    return;
                case short number:
                    writer.WriteNumberValue(number);
                    return;
                case byte number:
                    writer.WriteNumberValue(number);
                    return;
                case sbyte number:
                    writer.WriteNumberValue(number);
                    return;
                case ushort number:
                    writer.WriteNumberValue(number);
                    return;
                case uint number:
                    writer.WriteNumberValue(number);
                    return;
                case ulong number:
                    writer.WriteNumberValue(number);
                    return;
                case char character:
                    writer.WriteStringValue(character.ToString());
                    return;
                case DateTime dateTime:
                    writer.WriteStringValue(UtcDateTimeConverter.Format(dateTime));
                    return;
                case DateTimeOffset dateTimeOffset:
                    writer.WriteStringValue(dateTimeOffset);
                    return;
                case Guid guid:
                    writer.WriteStringValue(guid);
                    return;
                case Enum enumValue:
                    WriteEnum(writer, enumValue);
                    return;
                case byte[] bytes:
                    writer.WriteBase64StringValue(bytes);
                    return;
                case IDictionary dictionary:
                    writer.WriteStartObject();
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        writer.WritePropertyName(KeyToString(entry.Key));
                        WriteValue(writer, entry.Value, options, depth + 1);
                    }
                    writer.WriteEndObject();
                    return;
                case IEnumerable sequence:
                    writer.WriteStartArray();
                    foreach (object? item in sequence) WriteValue(writer, item, options, depth + 1);
                    writer.WriteEndArray();
                    return;
                default:
                    JsonSerializer.Serialize(writer, value, ResolveTypeInfo(value.GetType(), options));
                    return;
            }
        }

        private static void WriteEnum(Utf8JsonWriter writer, Enum value)
        {
            // Matches JsonStringEnumConverter: named values (and flag combinations) as strings, undefined values as numbers.
            string name = value.ToString();
            if (name.Length > 0 && (char.IsDigit(name[0]) || name[0] == '-')) writer.WriteRawValue(name);
            else writer.WriteStringValue(name);
        }

        private static string KeyToString(object key)
        {
            switch (key)
            {
                case string text:
                    return text;
                case Enum enumValue:
                    return enumValue.ToString();
                case IFormattable formattable:
                    return formattable.ToString(null, CultureInfo.InvariantCulture);
                default:
                    return key.ToString() ?? string.Empty;
            }
        }

        #endregion
    }
}
