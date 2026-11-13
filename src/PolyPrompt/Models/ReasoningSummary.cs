namespace PolyPrompt.Models
{
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Reasoning summary preference for Responses API clients (<c>reasoning.summary</c>). OpenAI and xAI accept auto, concise, and detailed.
    /// An extensible string-backed value: use the static known values, or construct one from any string (it converts
    /// implicitly from <see cref="string"/>), so a value a provider adds later works without a PolyPrompt release. Each
    /// client validates the known values it does not accept and passes unknown values through unchanged.
    /// </summary>
    [JsonConverter(typeof(ReasoningSummaryJsonConverter))]
    public readonly struct ReasoningSummary : IEquatable<ReasoningSummary>
    {
        /// <summary><c>auto</c>: Let the provider choose.</summary>
        public static ReasoningSummary Auto { get; } = new ReasoningSummary("auto");

        /// <summary><c>concise</c>: A short summary.</summary>
        public static ReasoningSummary Concise { get; } = new ReasoningSummary("concise");

        /// <summary><c>detailed</c>: A detailed summary.</summary>
        public static ReasoningSummary Detailed { get; } = new ReasoningSummary("detailed");


        /// <summary>
        /// Every value PolyPrompt knows, across all providers.
        /// </summary>
        public static IReadOnlyList<ReasoningSummary> Known { get; } = new[] { Auto, Concise, Detailed };

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
                foreach (ReasoningSummary known in Known) if (known._Value == _Value) return true;
                return false;
            }
        }

        /// <summary>
        /// Create a value.
        /// </summary>
        /// <param name="value">Wire value. Trimmed and lower-cased.</param>
        /// <exception cref="ArgumentException">Thrown when value is null, empty, or whitespace.</exception>
        public ReasoningSummary(string value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            _Value = value.Trim().ToLowerInvariant();
        }

        /// <summary>
        /// Convert a string to a value.
        /// </summary>
        /// <param name="value">Wire value.</param>
        public static implicit operator ReasoningSummary(string value)
        {
            return new ReasoningSummary(value);
        }

        /// <inheritdoc />
        public bool Equals(ReasoningSummary other)
        {
            return string.Equals(_Value, other._Value, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is ReasoningSummary other && Equals(other);
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
        public static bool operator ==(ReasoningSummary left, ReasoningSummary right) => left.Equals(right);

        /// <summary>Inequality.</summary>
        /// <param name="left">Left.</param>
        /// <param name="right">Right.</param>
        /// <returns>True when not equal.</returns>
        public static bool operator !=(ReasoningSummary left, ReasoningSummary right) => !left.Equals(right);
    }

    /// <summary>
    /// JSON converter writing <see cref="ReasoningSummary"/> as its string value.
    /// </summary>
    public sealed class ReasoningSummaryJsonConverter : JsonConverter<ReasoningSummary>
    {
        /// <inheritdoc />
        public override ReasoningSummary Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? value = reader.GetString();
            if (string.IsNullOrWhiteSpace(value)) throw new JsonException("ReasoningSummary must be a non-empty string.");
            return new ReasoningSummary(value);
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, ReasoningSummary value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.Value);
        }
    }
}
