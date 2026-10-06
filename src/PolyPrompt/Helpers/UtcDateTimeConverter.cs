namespace PolyPrompt.Helpers
{
    using System.Globalization;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Writes <see cref="DateTime"/> values as UTC ISO 8601 strings with microsecond precision
    /// (yyyy-MM-ddTHH:mm:ss.ffffffZ) and reads any parseable date as UTC.
    /// </summary>
    public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
    {
        #region Private-Members

        private const string FormatString = "yyyy-MM-ddTHH:mm:ss.ffffffZ";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Format a date the way this converter writes it.
        /// </summary>
        /// <param name="value">Date to format. Local and unspecified dates are converted to UTC.</param>
        /// <returns>The formatted date.</returns>
        public static string Format(DateTime value)
        {
            return value.ToUniversalTime().ToString(FormatString, CultureInfo.InvariantCulture);
        }

        /// <inheritdoc />
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? value = reader.GetString();
            if (string.IsNullOrEmpty(value)) return default;
            return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(Format(value));
        }

        #endregion
    }
}
