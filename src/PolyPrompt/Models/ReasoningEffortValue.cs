namespace PolyPrompt.Models
{
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// An exact <c>reasoning.effort</c> wire value for Responses API clients, overriding the mapping from <see cref="CompletionOptions.ReasoningEffort"/>. OpenAI accepts none, minimal, low, medium, high, xhigh, and max (model dependent); xAI accepts low, medium, high, and xhigh.
    /// An extensible string-backed value: use the static known values, or construct one from any string (it converts
    /// implicitly from <see cref="string"/>), so a value a provider adds later works without a PolyPrompt release. Each
    /// client validates the known values it does not accept and passes unknown values through unchanged.
    /// </summary>
    [JsonConverter(typeof(ReasoningEffortValueJsonConverter))]
    public readonly struct ReasoningEffortValue : IEquatable<ReasoningEffortValue>
    {
        /// <summary><c>none</c>: No reasoning (OpenAI).</summary>
        public static ReasoningEffortValue None { get; } = new ReasoningEffortValue("none");

        /// <summary><c>minimal</c>: Minimal reasoning (OpenAI).</summary>
        public static ReasoningEffortValue Minimal { get; } = new ReasoningEffortValue("minimal");

        /// <summary><c>low</c>: Low effort.</summary>
        public static ReasoningEffortValue Low { get; } = new ReasoningEffortValue("low");

        /// <summary><c>medium</c>: Medium effort.</summary>
        public static ReasoningEffortValue Medium { get; } = new ReasoningEffortValue("medium");

        /// <summary><c>high</c>: High effort.</summary>
        public static ReasoningEffortValue High { get; } = new ReasoningEffortValue("high");

        /// <summary><c>xhigh</c>: Extra-high effort.</summary>
        public static ReasoningEffortValue XHigh { get; } = new ReasoningEffortValue("xhigh");

        /// <summary><c>max</c>: Maximum effort (OpenAI).</summary>
        public static ReasoningEffortValue Max { get; } = new ReasoningEffortValue("max");


        /// <summary>
        /// Every value PolyPrompt knows, across all providers.
        /// </summary>
        public static IReadOnlyList<ReasoningEffortValue> Known { get; } = new[] { None, Minimal, Low, Medium, High, XHigh, Max };

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
                foreach (ReasoningEffortValue known in Known) if (known._Value == _Value) return true;
                return false;
            }
        }

        /// <summary>
        /// Create a value.
        /// </summary>
        /// <param name="value">Wire value. Trimmed and lower-cased.</param>
        /// <exception cref="ArgumentException">Thrown when value is null, empty, or whitespace.</exception>
        public ReasoningEffortValue(string value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            _Value = value.Trim().ToLowerInvariant();
        }

        /// <summary>
        /// Convert a string to a value.
        /// </summary>
        /// <param name="value">Wire value.</param>
        public static implicit operator ReasoningEffortValue(string value)
        {
            return new ReasoningEffortValue(value);
        }

        /// <inheritdoc />
        public bool Equals(ReasoningEffortValue other)
        {
            return string.Equals(_Value, other._Value, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is ReasoningEffortValue other && Equals(other);
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
        public static bool operator ==(ReasoningEffortValue left, ReasoningEffortValue right) => left.Equals(right);

        /// <summary>Inequality.</summary>
        /// <param name="left">Left.</param>
        /// <param name="right">Right.</param>
        /// <returns>True when not equal.</returns>
        public static bool operator !=(ReasoningEffortValue left, ReasoningEffortValue right) => !left.Equals(right);
    }

    /// <summary>
    /// JSON converter writing <see cref="ReasoningEffortValue"/> as its string value.
    /// </summary>
    public sealed class ReasoningEffortValueJsonConverter : JsonConverter<ReasoningEffortValue>
    {
        /// <inheritdoc />
        public override ReasoningEffortValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? value = reader.GetString();
            if (string.IsNullOrWhiteSpace(value)) throw new JsonException("ReasoningEffortValue must be a non-empty string.");
            return new ReasoningEffortValue(value);
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, ReasoningEffortValue value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.Value);
        }
    }
}
