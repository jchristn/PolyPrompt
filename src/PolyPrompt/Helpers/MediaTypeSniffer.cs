namespace PolyPrompt.Helpers
{
    using PolyPrompt.Models;

    /// <summary>
    /// Detects common media formats from their leading bytes.
    /// </summary>
    internal static class MediaTypeSniffer
    {
        #region Internal-Methods

        /// <summary>
        /// The media type the bytes start with, or null when the format is not recognized.
        /// </summary>
        /// <param name="data">The bytes.</param>
        /// <returns>A media type from <see cref="MediaTypes"/>, or null.</returns>
        internal static string? Detect(ReadOnlySpan<byte> data)
        {
            if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return MediaTypes.ImageJpeg;
            if (StartsWith(data, 0, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)) return MediaTypes.ImagePng;
            if (StartsWithAscii(data, 0, "GIF87a") || StartsWithAscii(data, 0, "GIF89a")) return MediaTypes.ImageGif;
            if (StartsWithAscii(data, 0, "RIFF") && StartsWithAscii(data, 8, "WEBP")) return MediaTypes.ImageWebp;
            if (StartsWithAscii(data, 0, "RIFF") && StartsWithAscii(data, 8, "WAVE")) return MediaTypes.AudioWav;
            if (StartsWithAscii(data, 0, "fLaC")) return MediaTypes.AudioFlac;
            if (StartsWithAscii(data, 0, "OggS")) return MediaTypes.AudioOgg;
            if (StartsWithAscii(data, 0, "ID3")) return MediaTypes.AudioMpeg;
            if (data.Length >= 2 && data[0] == 0xFF && (data[1] & 0xE0) == 0xE0 && (data[1] & 0x06) != 0) return MediaTypes.AudioMpeg;
            if (StartsWith(data, 0, 0x1A, 0x45, 0xDF, 0xA3)) return MediaTypes.VideoWebm;
            if (StartsWithAscii(data, 4, "ftyp") && data.Length >= 12)
            {
                if (StartsWithAscii(data, 8, "qt  ")) return MediaTypes.VideoQuicktime;
                if (StartsWithAscii(data, 8, "M4A ") || StartsWithAscii(data, 8, "M4B ")) return MediaTypes.AudioMp4;
                return MediaTypes.VideoMp4;
            }

            return null;
        }

        /// <summary>
        /// Whether a declared media type agrees with a detected one. Containers shared by audio and video (WebM, Ogg, MP4)
        /// match either declaration.
        /// </summary>
        /// <param name="detected">The detected media type.</param>
        /// <param name="declared">The declared media type.</param>
        /// <returns>True when they agree.</returns>
        internal static bool Compatible(string detected, string declared)
        {
            string d = Normalize(declared);
            if (string.Equals(detected, d, StringComparison.OrdinalIgnoreCase)) return true;
            string detectedSubtype = detected.Substring(detected.IndexOf('/') + 1);
            string declaredSubtype = d.Substring(d.IndexOf('/') + 1);
            return detectedSubtype switch
            {
                "webm" => declaredSubtype == "webm",
                "ogg" => declaredSubtype == "ogg" || declaredSubtype == "opus",
                "mp4" => declaredSubtype == "mp4" || declaredSubtype == "m4a" || declaredSubtype == "x-m4a" || declaredSubtype == "quicktime",
                "quicktime" => declaredSubtype == "mp4" || declaredSubtype == "quicktime",
                _ => false
            };
        }

        #endregion

        #region Private-Methods

        private static string Normalize(string mediaType)
        {
            string value = mediaType.Trim().ToLowerInvariant();
            return value switch
            {
                "image/jpg" or "image/pjpeg" => MediaTypes.ImageJpeg,
                "audio/x-wav" or "audio/wave" or "audio/vnd.wave" => MediaTypes.AudioWav,
                "audio/mp3" => MediaTypes.AudioMpeg,
                "audio/x-flac" => MediaTypes.AudioFlac,
                _ => value
            };
        }

        private static bool StartsWith(ReadOnlySpan<byte> data, int offset, params byte[] prefix)
        {
            if (data.Length < offset + prefix.Length) return false;
            for (int i = 0; i < prefix.Length; i++) if (data[offset + i] != prefix[i]) return false;
            return true;
        }

        private static bool StartsWithAscii(ReadOnlySpan<byte> data, int offset, string prefix)
        {
            if (data.Length < offset + prefix.Length) return false;
            for (int i = 0; i < prefix.Length; i++) if (data[offset + i] != (byte)prefix[i]) return false;
            return true;
        }

        #endregion
    }
}
