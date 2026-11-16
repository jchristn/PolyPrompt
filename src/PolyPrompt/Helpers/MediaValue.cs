namespace PolyPrompt.Helpers
{
    using System.Security.Cryptography;

    /// <summary>
    /// An inline media value in a wire body: the exact string the provider needs (raw base64 or a data URL). The
    /// serializer writes it unescaped, escaped, or as a placeholder (see <see cref="MediaWriteMode"/>), so builders never
    /// put a raw base64 string into a body and nothing ever searches serialized JSON for media.
    /// </summary>
    internal sealed class MediaValue
    {
        #region Internal-Members

        /// <summary>The string sent on the wire: raw base64, or a <c>data:</c> URL.</summary>
        internal string Encoded { get; }

        /// <summary>The media type.</summary>
        internal string MediaType { get; }

        /// <summary>The decoded byte count.</summary>
        internal int ByteCount { get; }

        #endregion

        #region Private-Members

        private readonly byte[] _Data;
        private string? _Hash = null;

        #endregion

        #region Constructors-and-Factories

        private MediaValue(string encoded, string mediaType, byte[] data)
        {
            Encoded = encoded;
            MediaType = mediaType;
            ByteCount = data.Length;
            _Data = data;
        }

        /// <summary>
        /// Raw base64 (Gemini, Vertex AI, Anthropic, Bedrock, Ollama, and OpenAI <c>input_audio</c>).
        /// </summary>
        /// <param name="data">The bytes.</param>
        /// <param name="mediaType">The media type.</param>
        /// <returns>The value.</returns>
        internal static MediaValue Base64(byte[] data, string mediaType)
        {
            return new MediaValue(Convert.ToBase64String(data), mediaType, data);
        }

        /// <summary>
        /// A <c>data:</c> URL (OpenAI Responses and Chat Completions images).
        /// </summary>
        /// <param name="data">The bytes.</param>
        /// <param name="mediaType">The media type.</param>
        /// <returns>The value.</returns>
        internal static MediaValue DataUrl(byte[] data, string mediaType)
        {
            return new MediaValue("data:" + mediaType + ";base64," + Convert.ToBase64String(data), mediaType, data);
        }

        #endregion

        #region Internal-Methods

        /// <summary>
        /// The recorded placeholder, for example <c>&lt;base64 184211 bytes image/jpeg sha256=ab12cd34&gt;</c>.
        /// </summary>
        /// <returns>The placeholder text.</returns>
        internal string Placeholder()
        {
            _Hash ??= Convert.ToHexString(SHA256.HashData(_Data), 0, 4).ToLowerInvariant();
            return "<base64 " + ByteCount + " bytes " + MediaType + " sha256=" + _Hash + ">";
        }

        #endregion
    }

    /// <summary>
    /// How the serializer writes a <see cref="MediaValue"/>.
    /// </summary>
    internal enum MediaWriteMode
    {
        /// <summary>The encoded string, unescaped (valid JSON: base64 and data URLs need no escaping).</summary>
        Raw,

        /// <summary>The encoded string with the writer's normal escaping.</summary>
        Escaped,

        /// <summary>A placeholder for values above the threshold, and the raw value for the rest.</summary>
        Placeholder
    }

    /// <summary>
    /// Per-serialization media settings and statistics.
    /// </summary>
    internal sealed class MediaWriteContext
    {
        /// <summary>
        /// Initialize the context.
        /// </summary>
        /// <param name="mode">How media is written.</param>
        /// <param name="placeholderThresholdBytes">In placeholder mode, values at or below this many bytes are written in full.</param>
        internal MediaWriteContext(MediaWriteMode mode, int placeholderThresholdBytes)
        {
            Mode = mode;
            PlaceholderThresholdBytes = placeholderThresholdBytes;
        }

        /// <summary>How media is written.</summary>
        internal MediaWriteMode Mode { get; }

        /// <summary>In placeholder mode, values at or below this many bytes are written in full.</summary>
        internal int PlaceholderThresholdBytes { get; }

        /// <summary>Media values written.</summary>
        internal int Count { get; private set; }

        /// <summary>The largest decoded byte count written.</summary>
        internal int MaxByteCount { get; private set; }

        /// <summary>
        /// Write one media value and update the statistics.
        /// </summary>
        /// <param name="writer">The JSON writer.</param>
        /// <param name="value">The value.</param>
        internal void Write(System.Text.Json.Utf8JsonWriter writer, MediaValue value)
        {
            Count++;
            if (value.ByteCount > MaxByteCount) MaxByteCount = value.ByteCount;

            if (Mode == MediaWriteMode.Escaped) writer.WriteStringValue(value.Encoded);
            else if (Mode == MediaWriteMode.Placeholder && value.ByteCount > PlaceholderThresholdBytes) writer.WriteRawValue("\"" + value.Placeholder() + "\"", skipInputValidation: true);
            else writer.WriteRawValue("\"" + value.Encoded + "\"", skipInputValidation: true);
        }
    }
}
