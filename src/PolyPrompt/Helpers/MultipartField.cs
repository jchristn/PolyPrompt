namespace PolyPrompt.Helpers
{
    using System.Net.Http.Headers;
    using System.Text;
    using System.Text.Json;

    /// <summary>
    /// One field of a <c>multipart/form-data</c> body: a text value, or a file.
    /// </summary>
    internal sealed class MultipartField
    {
        #region Internal-Members

        /// <summary>The form field name.</summary>
        internal string Name { get; }

        /// <summary>The text value, for a text field.</summary>
        internal string? Value { get; }

        /// <summary>The file bytes, for a file field.</summary>
        internal byte[]? Data { get; }

        /// <summary>The file name, for a file field.</summary>
        internal string? FileName { get; }

        /// <summary>The file media type, for a file field.</summary>
        internal string? MediaType { get; }

        #endregion

        #region Constructors-and-Factories

        private MultipartField(string name, string? value, byte[]? data, string? fileName, string? mediaType)
        {
            Name = name;
            Value = value;
            Data = data;
            FileName = fileName;
            MediaType = mediaType;
        }

        /// <summary>A text field.</summary>
        /// <param name="name">Field name.</param>
        /// <param name="value">Value.</param>
        /// <returns>The field.</returns>
        internal static MultipartField Text(string name, string value) => new MultipartField(name, value, null, null, null);

        /// <summary>A file field.</summary>
        /// <param name="name">Field name.</param>
        /// <param name="data">File bytes.</param>
        /// <param name="fileName">File name.</param>
        /// <param name="mediaType">Media type.</param>
        /// <returns>The field.</returns>
        internal static MultipartField File(string name, byte[] data, string fileName, string mediaType) => new MultipartField(name, null, data, fileName, mediaType);

        #endregion

        #region Internal-Methods

        /// <summary>
        /// Build the body bytes and the Content-Type header value (with its boundary).
        /// </summary>
        /// <param name="fields">The fields, in order.</param>
        /// <param name="contentType">The Content-Type value.</param>
        /// <returns>The body bytes.</returns>
        internal static byte[] Build(IReadOnlyList<MultipartField> fields, out string contentType)
        {
            string boundary = "polyprompt-" + Guid.NewGuid().ToString("N");
            using MultipartFormDataContent content = new MultipartFormDataContent(boundary);
            foreach (MultipartField field in fields)
            {
                if (field.Data == null)
                {
                    content.Add(new StringContent(field.Value ?? string.Empty, Encoding.UTF8), field.Name);
                }
                else
                {
                    ByteArrayContent file = new ByteArrayContent(field.Data);
                    file.Headers.ContentType = MediaTypeHeaderValue.Parse(field.MediaType!);
                    content.Add(file, field.Name, field.FileName!);
                }
            }

            contentType = content.Headers.ContentType!.ToString();
            using MemoryStream stream = new MemoryStream();
            content.CopyTo(stream, null, CancellationToken.None);
            return stream.ToArray();
        }

        /// <summary>
        /// The recorded summary: a JSON object of field names to values, with files shown as
        /// <c>&lt;binary N bytes type name=file&gt;</c>.
        /// </summary>
        /// <param name="fields">The fields.</param>
        /// <returns>The JSON summary.</returns>
        internal static string Summary(IReadOnlyList<MultipartField> fields)
        {
            using MemoryStream stream = new MemoryStream();
            using (Utf8JsonWriter writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            {
                writer.WriteStartObject();
                foreach (IGrouping<string, MultipartField> group in fields.GroupBy(f => f.Name))
                {
                    writer.WritePropertyName(group.Key);
                    List<string> values = group.Select(f => f.Data == null ? f.Value ?? "" : "<binary " + f.Data.Length + " bytes " + f.MediaType + " name=" + f.FileName + ">").ToList();
                    if (values.Count == 1) writer.WriteStringValue(values[0]);
                    else
                    {
                        writer.WriteStartArray();
                        foreach (string value in values) writer.WriteStringValue(value);
                        writer.WriteEndArray();
                    }
                }
                writer.WriteEndObject();
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        #endregion
    }
}
