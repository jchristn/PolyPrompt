namespace Test.Shared
{
    using System.Globalization;

    /// <summary>
    /// One metric measurement recorded by <see cref="TelemetryCapture"/>.
    /// </summary>
    public sealed class CapturedMeasurement
    {
        /// <summary>
        /// Instrument name.
        /// </summary>
        public string Instrument { get; }

        /// <summary>
        /// Instrument unit, or null.
        /// </summary>
        public string? Unit { get; }

        /// <summary>
        /// Measured value.
        /// </summary>
        public double Value { get; }

        /// <summary>
        /// Labels, with values converted to invariant strings.
        /// </summary>
        public Dictionary<string, string> Tags { get; }

        /// <summary>
        /// Create a captured measurement.
        /// </summary>
        /// <param name="instrument">Instrument name.</param>
        /// <param name="unit">Instrument unit.</param>
        /// <param name="value">Measured value.</param>
        /// <param name="tags">Labels.</param>
        public CapturedMeasurement(string instrument, string? unit, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            Instrument = instrument;
            Unit = unit;
            Value = value;
            Tags = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, object?> tag in tags)
            {
                Tags[tag.Key] = Convert.ToString(tag.Value, CultureInfo.InvariantCulture) ?? string.Empty;
            }
        }

        /// <summary>
        /// Whether every given label is present with the given value.
        /// </summary>
        /// <param name="expected">Expected labels as alternating key and value strings.</param>
        /// <returns>True when all labels match.</returns>
        public bool Has(params string[] expected)
        {
            for (int i = 0; i + 1 < expected.Length; i += 2)
            {
                if (!Tags.TryGetValue(expected[i], out string? actual) || actual != expected[i + 1]) return false;
            }
            return true;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Instrument + "=" + Value.ToString(CultureInfo.InvariantCulture) + " {" + string.Join(", ", Tags.Select(t => t.Key + "=" + t.Value)) + "}";
        }
    }
}
