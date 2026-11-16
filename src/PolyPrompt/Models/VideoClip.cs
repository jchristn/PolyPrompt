namespace PolyPrompt.Models
{
    /// <summary>
    /// The portion of a video to use, and how densely to sample it. Sent as Gemini and Vertex AI <c>videoMetadata</c>
    /// and ignored by other providers.
    /// </summary>
    public sealed class VideoClip
    {
        #region Public-Members

        /// <summary>
        /// Start offset in milliseconds, at least 0. Null starts at the beginning. Default: null.
        /// </summary>
        public long? StartOffsetMs
        {
            get => _StartOffsetMs;
            set
            {
                if (value.HasValue && value.Value < 0) throw new ArgumentOutOfRangeException(nameof(StartOffsetMs), "StartOffsetMs must be at least 0.");
                _StartOffsetMs = value;
            }
        }

        /// <summary>
        /// End offset in milliseconds. Must be greater than <see cref="StartOffsetMs"/> when both are set; this is checked
        /// when the request is validated. Null runs to the end. Default: null.
        /// </summary>
        public long? EndOffsetMs
        {
            get => _EndOffsetMs;
            set
            {
                if (value.HasValue && value.Value < 0) throw new ArgumentOutOfRangeException(nameof(EndOffsetMs), "EndOffsetMs must be at least 0.");
                _EndOffsetMs = value;
            }
        }

        /// <summary>
        /// Frames sampled per second, from 0.1 to 24. Null uses the provider default (1 on Gemini). Default: null.
        /// </summary>
        public double? FramesPerSecond
        {
            get => _FramesPerSecond;
            set
            {
                if (value.HasValue && (double.IsNaN(value.Value) || value.Value < 0.1 || value.Value > 24))
                    throw new ArgumentOutOfRangeException(nameof(FramesPerSecond), "FramesPerSecond must be from 0.1 to 24.");
                _FramesPerSecond = value;
            }
        }

        #endregion

        #region Private-Members

        private long? _StartOffsetMs = null;
        private long? _EndOffsetMs = null;
        private double? _FramesPerSecond = null;

        #endregion
    }
}
