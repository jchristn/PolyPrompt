namespace PolyPrompt.Models
{
    /// <summary>
    /// The kind of content a <see cref="ContentPart"/> carries.
    /// </summary>
    public enum ContentPartType
    {
        /// <summary>Plain text.</summary>
        Text,

        /// <summary>An image. Requires <see cref="Capability.ImageInput"/>.</summary>
        Image,

        /// <summary>Audio. Requires <see cref="Capability.AudioInput"/>.</summary>
        Audio,

        /// <summary>Video. Requires <see cref="Capability.VideoInput"/>.</summary>
        Video
    }
}
