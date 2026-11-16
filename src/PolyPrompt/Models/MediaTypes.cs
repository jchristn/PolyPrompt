namespace PolyPrompt.Models
{
    /// <summary>
    /// MIME type constants for common image, audio, and video formats, and lookup by file extension.
    /// </summary>
    public static class MediaTypes
    {
        #region Public-Members

        /// <summary>JPEG image.</summary>
        public const string ImageJpeg = "image/jpeg";

        /// <summary>PNG image.</summary>
        public const string ImagePng = "image/png";

        /// <summary>WebP image.</summary>
        public const string ImageWebp = "image/webp";

        /// <summary>GIF image.</summary>
        public const string ImageGif = "image/gif";

        /// <summary>WAV audio.</summary>
        public const string AudioWav = "audio/wav";

        /// <summary>MP3 audio.</summary>
        public const string AudioMpeg = "audio/mpeg";

        /// <summary>FLAC audio.</summary>
        public const string AudioFlac = "audio/flac";

        /// <summary>Ogg audio.</summary>
        public const string AudioOgg = "audio/ogg";

        /// <summary>WebM audio.</summary>
        public const string AudioWebm = "audio/webm";

        /// <summary>MP4 (M4A) audio.</summary>
        public const string AudioMp4 = "audio/mp4";

        /// <summary>MP4 video.</summary>
        public const string VideoMp4 = "video/mp4";

        /// <summary>WebM video.</summary>
        public const string VideoWebm = "video/webm";

        /// <summary>QuickTime (MOV) video.</summary>
        public const string VideoQuicktime = "video/quicktime";

        #endregion

        #region Public-Methods

        /// <summary>
        /// The media type for a file's extension, or null when the extension is not one of the constants above.
        /// </summary>
        /// <param name="path">A file path or name.</param>
        /// <returns>The media type, or null.</returns>
        public static string? FromFileExtension(string path)
        {
            ArgumentNullException.ThrowIfNull(path);
            string extension = Path.GetExtension(path).ToLowerInvariant();
            return extension switch
            {
                ".jpg" or ".jpeg" => ImageJpeg,
                ".png" => ImagePng,
                ".webp" => ImageWebp,
                ".gif" => ImageGif,
                ".wav" => AudioWav,
                ".mp3" => AudioMpeg,
                ".flac" => AudioFlac,
                ".ogg" or ".oga" or ".opus" => AudioOgg,
                ".weba" => AudioWebm,
                ".m4a" => AudioMp4,
                ".mp4" or ".m4v" => VideoMp4,
                ".webm" => VideoWebm,
                ".mov" => VideoQuicktime,
                _ => null
            };
        }

        /// <summary>
        /// The content type implied by a media type's top-level type (<c>image/</c>, <c>audio/</c>, <c>video/</c>), or null.
        /// </summary>
        /// <param name="mediaType">A media type.</param>
        /// <returns>The content part type, or null.</returns>
        public static ContentPartType? PartTypeOf(string? mediaType)
        {
            if (string.IsNullOrEmpty(mediaType)) return null;
            if (mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return ContentPartType.Image;
            if (mediaType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)) return ContentPartType.Audio;
            if (mediaType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)) return ContentPartType.Video;
            return null;
        }

        #endregion
    }
}
