namespace PolyPrompt.Models
{
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Processing tier for Responses API clients (<c>service_tier</c>). OpenAI accepts all six known values; xAI accepts auto, priority, and fast. OpenAI reports <c>priority</c> when <c>fast</c> was requested, so the tier a response reports is not an echo of the requested one.
    /// An extensible string-backed value: use the static known values, or construct one from any string (it converts
    /// implicitly from <see cref="string"/>), so a value a provider adds later works without a PolyPrompt release. Each
    /// client validates the known values it does not accept and passes unknown values through unchanged.
    /// </summary>
    [JsonConverter(typeof(ServiceTierJsonConverter))]
    public readonly struct ServiceTier : IEquatable<ServiceTier>
    {
        /// <summary><c>auto</c>: Provider default selection.</summary>
        public static ServiceTier Auto { get; } = new ServiceTier("auto");

        /// <summary><c>default</c>: Standard processing (OpenAI).</summary>
        public static ServiceTier Default { get; } = new ServiceTier("default");

        /// <summary><c>flex</c>: Lower-cost, higher-latency processing (OpenAI).</summary>
        public static ServiceTier Flex { get; } = new ServiceTier("flex");

        /// <summary><c>scale</c>: Scale tier (OpenAI).</summary>
        public static ServiceTier Scale { get; } = new ServiceTier("scale");

        /// <summary><c>priority</c>: Priority processing.</summary>
        public static ServiceTier Priority { get; } = new ServiceTier("priority");

        /// <summary><c>fast</c>: Fast processing.</summary>
        public static ServiceTier Fast { get; } = new ServiceTier("fast");


        /// <summary>
        /// Every value PolyPrompt knows, across all providers.
        /// </summary>
        public static IReadOnlyList<ServiceTier> Known { get; } = new[] { Auto, Default, Flex, Scale, Priority, Fast };

        private readonly string? _Value;

        /// <summary>
        /// The wire value, trimmed and lower-cased.
        /// </summary>
        public string Value => _Value ?? "";

        /// <summary>
        /// True when <see cref="Value"/> is one of <see cref="Known"/>.
        /// </summary>
        public bool IsKnown
        {
            get
            {
                foreach (ServiceTier known in Known) if (known._Value == _Value) return true;
                return false;
            }
        }

        /// <summary>
        /// Create a value.
        /// </summary>
        /// <param name="value">Wire value. Trimmed and lower-cased.</param>
        /// <exception cref="ArgumentException">Thrown when value is null, empty, or whitespace.</exception>
        public ServiceTier(string value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            _Value = value.Trim().ToLowerInvariant();
        }

        /// <summary>
        /// Convert a string to a value.
        /// </summary>
        /// <param name="value">Wire value.</param>
        public static implicit operator ServiceTier(string value)
        {
            return new ServiceTier(value);
        }

        /// <inheritdoc />
        public bool Equals(ServiceTier other)
        {
            return string.Equals(_Value, other._Value, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is ServiceTier other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return _Value == null ? 0 : StringComparer.Ordinal.GetHashCode(_Value);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Value;
        }

        /// <summary>Equality.</summary>
        /// <param name="left">Left.</param>
        /// <param name="right">Right.</param>
        /// <returns>True when equal.</returns>
        public static bool operator ==(ServiceTier left, ServiceTier right) => left.Equals(right);

        /// <summary>Inequality.</summary>
        /// <param name="left">Left.</param>
        /// <param name="right">Right.</param>
        /// <returns>True when not equal.</returns>
        public static bool operator !=(ServiceTier left, ServiceTier right) => !left.Equals(right);
    }

    /// <summary>
    /// JSON converter writing <see cref="ServiceTier"/> as its string value.
    /// </summary>
    public sealed class ServiceTierJsonConverter : JsonConverter<ServiceTier>
    {
        /// <inheritdoc />
        public override ServiceTier Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? value = reader.GetString();
            if (string.IsNullOrWhiteSpace(value)) throw new JsonException("ServiceTier must be a non-empty string.");
            return new ServiceTier(value);
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, ServiceTier value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.Value);
        }
    }
}
