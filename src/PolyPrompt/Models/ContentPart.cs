namespace PolyPrompt.Models
{
    using PolyPrompt.Helpers;

    /// <summary>
    /// One piece of a message's content: text, or an image, audio clip, or video given as inline bytes, a remote URL, or
    /// a provider-hosted file reference. Exactly one of <see cref="Text"/>, <see cref="Data"/>, <see cref="Url"/>, or
    /// <see cref="FileReference"/> is set. The factories validate when called; parts built with an object initializer are
    /// validated when a request is sent. See MULTIMEDIA.md.
    /// </summary>
    public sealed class ContentPart
    {
        #region Public-Members

        /// <summary>
        /// What this part carries. Default: <see cref="ContentPartType.Text"/>.
        /// </summary>
        public ContentPartType Type { get; set; } = ContentPartType.Text;

        /// <summary>
        /// The text, when <see cref="Type"/> is <see cref="ContentPartType.Text"/>. Default: null.
        /// </summary>
        public string? Text { get; set; } = null;

        /// <summary>
        /// Inline media bytes. Requires <see cref="MediaType"/>. Default: null.
        /// </summary>
        public byte[]? Data { get; set; } = null;

        /// <summary>
        /// A remote <c>http</c> or <c>https</c> URL that the provider fetches. PolyPrompt never downloads it. Accepted by
        /// OpenAI, Azure OpenAI, xAI, Anthropic, and OpenAI-compatible servers, and for YouTube video links by Gemini and
        /// Vertex AI; other providers reject it before sending. Default: null.
        /// </summary>
        public string? Url { get; set; } = null;

        /// <summary>
        /// A provider-hosted file: an OpenAI <c>file_id</c>, a Gemini File API URI, a Vertex AI <c>gs://</c> URI, or a
        /// Bedrock <c>s3://</c> URI. Requires <see cref="MediaType"/>. Default: null.
        /// </summary>
        public string? FileReference { get; set; } = null;

        /// <summary>
        /// MIME type, for example <c>image/png</c> (see <see cref="MediaTypes"/>). Required with <see cref="Data"/> and
        /// <see cref="FileReference"/>, optional with <see cref="Url"/>. Default: null.
        /// </summary>
        public string? MediaType { get; set; } = null;

        /// <summary>
        /// Image resolution hint. Images only. Default: null (provider default).
        /// </summary>
        public ImageDetail? Detail { get; set; } = null;

        /// <summary>
        /// The portion of a video to use and its sampling rate. Video only. Default: null.
        /// </summary>
        public VideoClip? Clip { get; set; } = null;

        /// <summary>
        /// Optional display or file name, used as the file name for uploads. Default: null.
        /// </summary>
        public string? Name { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// A text part.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <returns>The part.</returns>
        /// <exception cref="ArgumentNullException">Thrown when text is null.</exception>
        public static ContentPart FromText(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            return new ContentPart { Type = ContentPartType.Text, Text = text };
        }

        /// <summary>
        /// An inline image.
        /// </summary>
        /// <param name="data">Image bytes.</param>
        /// <param name="mediaType">Image media type, for example <see cref="MediaTypes.ImagePng"/>.</param>
        /// <param name="detail">Optional resolution hint.</param>
        /// <returns>The part.</returns>
        /// <exception cref="ArgumentException">Thrown when the data is empty or the media type is not an image type.</exception>
        public static ContentPart Image(byte[] data, string mediaType, ImageDetail? detail = null)
        {
            return Validated(new ContentPart { Type = ContentPartType.Image, Data = data, MediaType = mediaType, Detail = detail });
        }

        /// <summary>
        /// An image the provider fetches from a URL.
        /// </summary>
        /// <param name="url">An http or https URL.</param>
        /// <param name="detail">Optional resolution hint.</param>
        /// <returns>The part.</returns>
        /// <exception cref="ArgumentException">Thrown when the URL is not an absolute http or https URL.</exception>
        public static ContentPart ImageUrl(string url, ImageDetail? detail = null)
        {
            return Validated(new ContentPart { Type = ContentPartType.Image, Url = url, Detail = detail });
        }

        /// <summary>
        /// Inline audio.
        /// </summary>
        /// <param name="data">Audio bytes.</param>
        /// <param name="mediaType">Audio media type, for example <see cref="MediaTypes.AudioWav"/>.</param>
        /// <returns>The part.</returns>
        /// <exception cref="ArgumentException">Thrown when the data is empty or the media type is not an audio type.</exception>
        public static ContentPart Audio(byte[] data, string mediaType)
        {
            return Validated(new ContentPart { Type = ContentPartType.Audio, Data = data, MediaType = mediaType });
        }

        /// <summary>
        /// Inline video.
        /// </summary>
        /// <param name="data">Video bytes.</param>
        /// <param name="mediaType">Video media type, for example <see cref="MediaTypes.VideoMp4"/>.</param>
        /// <param name="clip">Optional clip and sampling rate (Gemini and Vertex AI).</param>
        /// <returns>The part.</returns>
        /// <exception cref="ArgumentException">Thrown when the data is empty or the media type is not a video type.</exception>
        public static ContentPart Video(byte[] data, string mediaType, VideoClip? clip = null)
        {
            return Validated(new ContentPart { Type = ContentPartType.Video, Data = data, MediaType = mediaType, Clip = clip });
        }

        /// <summary>
        /// A video the provider fetches from a URL: a YouTube link on Gemini and Vertex AI, or a server-fetched URL on
        /// OpenAI-compatible servers that accept <c>video_url</c>.
        /// </summary>
        /// <param name="url">An http or https URL.</param>
        /// <param name="clip">Optional clip and sampling rate (Gemini and Vertex AI).</param>
        /// <returns>The part.</returns>
        /// <exception cref="ArgumentException">Thrown when the URL is not an absolute http or https URL.</exception>
        public static ContentPart VideoUrl(string url, VideoClip? clip = null)
        {
            return Validated(new ContentPart { Type = ContentPartType.Video, Url = url, Clip = clip });
        }

        /// <summary>
        /// A provider-hosted file (see <see cref="FileReference"/>).
        /// </summary>
        /// <param name="type">Image, audio, or video.</param>
        /// <param name="reference">The provider's file identifier or URI.</param>
        /// <param name="mediaType">The file's media type.</param>
        /// <returns>The part.</returns>
        /// <exception cref="ArgumentException">Thrown when the type is Text, or the reference or media type is blank.</exception>
        public static ContentPart FileReferenceOf(ContentPartType type, string reference, string mediaType)
        {
            return Validated(new ContentPart { Type = type, FileReference = reference, MediaType = mediaType });
        }

        /// <summary>
        /// Read a file and infer its part type and media type from its leading bytes, falling back to its extension.
        /// </summary>
        /// <param name="path">Path to an image, audio, or video file.</param>
        /// <returns>The part, with <see cref="Name"/> set to the file name.</returns>
        /// <exception cref="ArgumentException">Thrown when the media type cannot be determined or is not image, audio, or video.</exception>
        public static ContentPart FromFile(string path)
        {
            ArgumentNullException.ThrowIfNull(path);
            byte[] data = File.ReadAllBytes(path);
            string? mediaType = MediaTypeSniffer.Detect(data) ?? MediaTypes.FromFileExtension(path);
            ContentPartType? type = MediaTypes.PartTypeOf(mediaType);
            if (mediaType == null || type == null)
                throw new ArgumentException("Cannot determine the media type of '" + path + "'. Use ContentPart.Image, Audio, or Video with an explicit media type.", nameof(path));

            return Validated(new ContentPart { Type = type.Value, Data = data, MediaType = mediaType, Name = Path.GetFileName(path) });
        }

        /// <summary>
        /// Check that this part is well formed: exactly one source, a type consistent with that source and the media
        /// type, and type-specific settings only on their own type.
        /// </summary>
        /// <param name="path">Where the part is, used in error messages, for example <c>Messages[1].Content[0]</c>.</param>
        /// <exception cref="ArgumentException">Thrown when the part is not well formed.</exception>
        public void Validate(string path = "part")
        {
            int sources = (Text != null ? 1 : 0) + (Data != null ? 1 : 0) + (Url != null ? 1 : 0) + (FileReference != null ? 1 : 0);
            if (sources != 1)
                throw new ArgumentException(path + ": exactly one of Text, Data, Url, or FileReference must be set (found " + sources + ").");

            if (Type == ContentPartType.Text)
            {
                if (Text == null) throw new ArgumentException(path + ": a Text part requires Text.");
                if (MediaType != null || Detail != null || Clip != null) throw new ArgumentException(path + ": a Text part cannot set MediaType, Detail, or Clip.");
                return;
            }

            if (Text != null) throw new ArgumentException(path + ": " + Describe(Type) + " part cannot set Text.");
            if (Detail != null && Type != ContentPartType.Image) throw new ArgumentException(path + ": Detail applies only to Image parts.");
            if (Clip != null && Type != ContentPartType.Video) throw new ArgumentException(path + ": Clip applies only to Video parts.");
            if (Clip != null && Clip.StartOffsetMs.HasValue && Clip.EndOffsetMs.HasValue && Clip.EndOffsetMs.Value <= Clip.StartOffsetMs.Value)
                throw new ArgumentException(path + ": Clip.EndOffsetMs must be greater than Clip.StartOffsetMs.");

            if (MediaType != null) ValidateMediaType(path);

            if (Data != null)
            {
                if (Data.Length == 0) throw new ArgumentException(path + ": Data is empty.");
                if (MediaType == null) throw new ArgumentException(path + ": inline Data requires MediaType.");
                string? sniffed = MediaTypeSniffer.Detect(Data);
                if (sniffed != null && !MediaTypeSniffer.Compatible(sniffed, MediaType))
                    throw new ArgumentException(path + ": MediaType is '" + MediaType + "' but the data looks like '" + sniffed + "'.");
            }
            else if (Url != null)
            {
                if (!Uri.TryCreate(Url, UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    throw new ArgumentException(path + ": Url must be an absolute http or https URL.");
            }
            else if (FileReference != null)
            {
                if (string.IsNullOrWhiteSpace(FileReference)) throw new ArgumentException(path + ": FileReference is blank.");
                if (MediaType == null) throw new ArgumentException(path + ": FileReference requires MediaType.");
            }
        }

        #endregion

        #region Private-Methods

        private static string Describe(ContentPartType type)
        {
            return type is ContentPartType.Image or ContentPartType.Audio ? "an " + type : "a " + type;
        }

        private static ContentPart Validated(ContentPart part)
        {
            part.Validate();
            return part;
        }

        private void ValidateMediaType(string path)
        {
            // Media types are written into JSON bodies and data URLs as-is, so they must be plain tokens.
            foreach (char c in MediaType!)
            {
                if (c == '"' || c == '\\' || char.IsControl(c) || char.IsWhiteSpace(c))
                    throw new ArgumentException(path + ": MediaType '" + MediaType + "' contains a character that is not allowed.");
            }

            ContentPartType? implied = MediaTypes.PartTypeOf(MediaType);
            if (implied != Type)
                throw new ArgumentException(path + ": MediaType '" + MediaType + "' is not " + Describe(Type).ToLowerInvariant() + " type.");
        }

        #endregion
    }
}
