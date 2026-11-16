namespace PolyPrompt.Clients
{
    using PolyPrompt.Helpers;
    using PolyPrompt.Models;
    using PolyPrompt.Telemetry;

    public abstract partial class ProviderClientBase
    {
        #region Transcription-Methods

        /// <summary>
        /// Transcribe audio to text through the provider's dedicated speech-to-text endpoint. The model comes from
        /// <paramref name="options"/>, else <c>Defaults.Transcription.Model</c>. Segments and words are returned for
        /// <see cref="TranscriptionFormat.VerboseJson"/> on models that support it.
        /// </summary>
        /// <param name="request">The audio and transcription settings.</param>
        /// <param name="options">Optional per-call settings (the model).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The transcript.</returns>
        /// <exception cref="CapabilityNotSupportedException">Thrown when <c>Supports(Capability.Transcribe)</c> is false for this client.</exception>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the audio is empty, the media type is unknown or not audio, timestamp
        /// granularities are set without VerboseJson, or the model does not support the requested format.</exception>
        /// <exception cref="InvalidOperationException">Thrown when no model is configured.</exception>
        public Task<TranscriptionResponse> TranscribeAsync(TranscriptionRequest request, TranscriptionOptions? options = null, CancellationToken token = default)
        {
            RequireCapability(Capability.Transcribe, nameof(TranscribeAsync));
            ArgumentNullException.ThrowIfNull(request);
            if (request.Audio == null || request.Audio.Length == 0) throw new ArgumentException("Transcription requests require audio bytes.", nameof(request));

            string mediaType = request.MediaType ?? MediaTypeSniffer.Detect(request.Audio)
                ?? throw new ArgumentException("Cannot detect the audio format; set TranscriptionRequest.MediaType.", nameof(request));
            if (MediaTypes.PartTypeOf(mediaType) is not (ContentPartType.Audio or ContentPartType.Video))
                throw new ArgumentException("TranscriptionRequest.MediaType '" + mediaType + "' is not an audio type.", nameof(request));
            if (request.TimestampGranularities != null && request.TimestampGranularities.Count > 0 && request.Format != TranscriptionFormat.VerboseJson)
                throw new ArgumentException("TimestampGranularities require Format = TranscriptionFormat.VerboseJson.", nameof(request));

            TranscriptionOptions? defaults = Defaults.Transcription;
            ValidateOptionsType(options, defaults, nameof(TranscriptionOptions));
            string? model = options?.Model ?? defaults?.Model;
            if (string.IsNullOrWhiteSpace(model)) throw new InvalidOperationException(NoModelMessage("Transcription"));
            ValidateTranscriptionFormat(request, model);

            return InstrumentAsync(PolyPromptTelemetryNames.OperationTranscribe, model, s => s.SetTag(PolyPromptTelemetryNames.TranscriptionAudioBytes, request.Audio.Length),
                () => TranscribeWithTagsAsync(request, mediaType, model, token), token);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Provider implementation of <see cref="TranscribeAsync"/>.
        /// </summary>
        /// <param name="request">The validated request.</param>
        /// <param name="mediaType">The audio media type (given or detected).</param>
        /// <param name="model">The resolved model.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The transcript.</returns>
        protected virtual Task<TranscriptionResponse> TranscribeCoreAsync(TranscriptionRequest request, string mediaType, string model, CancellationToken token)
        {
            throw NotSupported(Capability.Transcribe, "TranscribeAsync");
        }

        /// <summary>
        /// Reject a format the model is known not to support, before sending. Default: accept everything.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="model">The resolved model.</param>
        /// <exception cref="ArgumentException">Thrown when the model does not support the format.</exception>
        protected virtual void ValidateTranscriptionFormat(TranscriptionRequest request, string model)
        {
        }

        #endregion

        #region Private-Methods

        private async Task<TranscriptionResponse> TranscribeWithTagsAsync(TranscriptionRequest request, string mediaType, string model, CancellationToken token)
        {
            TranscriptionResponse response = await TranscribeCoreAsync(request, mediaType, model, token).ConfigureAwait(false);
            OperationScope? scope = OperationScope.Current;
            if (scope != null && response != null)
            {
                if (response.DurationSeconds.HasValue) scope.SetTag(PolyPromptTelemetryNames.TranscriptionDurationSeconds, response.DurationSeconds.Value);
                scope.SetTag(PolyPromptTelemetryNames.TranscriptionSegments, response.Segments.Count);
            }
            return response!;
        }

        #endregion
    }
}
