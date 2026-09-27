namespace PolyPrompt.Options
{
    /// <summary>
    /// Shared normalization for Text Embeddings Inference option values.
    /// </summary>
    internal static class TeiOptionValues
    {
        /// <summary>
        /// Normalize a truncation direction to TEI's wire casing ("Left" or "Right"). Returns null for null,
        /// whitespace, or an unrecognized value.
        /// </summary>
        /// <param name="value">Raw value, case-insensitive.</param>
        /// <returns>"Left", "Right", or null.</returns>
        internal static string? NormalizeTruncationDirection(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            string normalized = value.Trim();
            if (string.Equals(normalized, "left", StringComparison.OrdinalIgnoreCase)) return "Left";
            if (string.Equals(normalized, "right", StringComparison.OrdinalIgnoreCase)) return "Right";
            return null;
        }
    }
}
