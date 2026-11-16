namespace PolyPrompt.Models
{
    /// <summary>
    /// A file uploaded to OpenAI (<c>/v1/files</c>). Send an image with
    /// <c>ContentPart.FileReferenceOf(ContentPartType.Image, file.Id, mediaType)</c>.
    /// </summary>
    public sealed class OpenAiFile
    {
        /// <summary>The file ID, for example <c>file-abc123</c>.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>The uploaded file name.</summary>
        public string? FileName { get; set; }

        /// <summary>The purpose, for example <c>vision</c> or <c>user_data</c>.</summary>
        public string? Purpose { get; set; }

        /// <summary>Size in bytes.</summary>
        public long? SizeBytes { get; set; }

        /// <summary>When the file was created.</summary>
        public DateTime? CreatedUtc { get; set; }
    }

    /// <summary>
    /// Result of an OpenAI file upload. <see cref="File"/> is null when the call failed.
    /// </summary>
    public class OpenAiFileResponse : ResponseBase
    {
        /// <summary>The file, when returned.</summary>
        public OpenAiFile? File { get; set; }
    }

    /// <summary>
    /// Result of deleting a provider file.
    /// </summary>
    public class FileDeleteResponse : ResponseBase
    {
        /// <summary>The file ID or resource name.</summary>
        public string? Id { get; set; }

        /// <summary>True when the provider confirmed the deletion.</summary>
        public bool Deleted { get; set; }
    }
}
