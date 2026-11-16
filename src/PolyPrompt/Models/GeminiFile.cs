namespace PolyPrompt.Models
{
    /// <summary>
    /// A file in the Gemini File API. Send it with
    /// <c>ContentPart.FileReferenceOf(type, file.Uri, file.MediaType)</c> once <see cref="State"/> is
    /// <see cref="GeminiFileState.Active"/>. Files expire (see <see cref="ExpirationUtc"/>, about 48 hours after upload).
    /// </summary>
    public sealed class GeminiFile
    {
        /// <summary>Resource name, for example <c>files/abc123</c>. Use it with Get, Wait, and Delete.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Display name, when one was given.</summary>
        public string? DisplayName { get; set; }

        /// <summary>The URI to send as a file reference.</summary>
        public string? Uri { get; set; }

        /// <summary>The media type.</summary>
        public string? MediaType { get; set; }

        /// <summary>Size in bytes.</summary>
        public long? SizeBytes { get; set; }

        /// <summary>Processing state. Video is processed after upload and must reach Active before use.</summary>
        public GeminiFileState State { get; set; } = GeminiFileState.Unspecified;

        /// <summary>The processing error message when <see cref="State"/> is <see cref="GeminiFileState.Failed"/>.</summary>
        public string? StateError { get; set; }

        /// <summary>Base64 SHA-256 of the contents, when reported.</summary>
        public string? Sha256Hash { get; set; }

        /// <summary>When the file was created.</summary>
        public DateTime? CreatedUtc { get; set; }

        /// <summary>When the file expires and is deleted.</summary>
        public DateTime? ExpirationUtc { get; set; }
    }

    /// <summary>
    /// Gemini File API processing state.
    /// </summary>
    public enum GeminiFileState
    {
        /// <summary>Not reported.</summary>
        Unspecified,

        /// <summary>Still processing; not usable yet.</summary>
        Processing,

        /// <summary>Ready to use.</summary>
        Active,

        /// <summary>Processing failed.</summary>
        Failed
    }

    /// <summary>
    /// Result of a Gemini File API call. <see cref="File"/> is null when the call failed or deleted a file.
    /// </summary>
    public class GeminiFileResponse : ResponseBase
    {
        /// <summary>The file, when returned.</summary>
        public GeminiFile? File { get; set; }
    }

    /// <summary>
    /// One page of Gemini File API files.
    /// </summary>
    public class GeminiFileListResponse : ResponseBase
    {
        /// <summary>The files on this page.</summary>
        public List<GeminiFile> Files { get; set; } = new List<GeminiFile>();

        /// <summary>The token for the next page, or null on the last page.</summary>
        public string? NextPageToken { get; set; }
    }
}
