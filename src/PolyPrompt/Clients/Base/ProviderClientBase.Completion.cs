namespace PolyPrompt.Clients
{
    using System.Diagnostics;
    using System.Runtime.CompilerServices;
    using PolyPrompt.Models;
    using PolyPrompt.Telemetry;

    public abstract partial class ProviderClientBase
    {
        #region Completion-Members

        /// <summary>
        /// Maximum tokens sent when neither the call nor <c>Defaults.Completion</c> sets one and
        /// <see cref="SendDefaultMaxTokens"/> is true, or the provider requires a limit.
        /// </summary>
        public const int DefaultMaxTokens = 4096;

        /// <summary>
        /// Whether <see cref="DefaultMaxTokens"/> is sent when neither the call nor <c>Defaults.Completion</c> sets
        /// <c>MaxTokens</c>. When false, no output limit is sent and the provider's own default applies (for Ollama, no
        /// limit; for most hosted APIs, the model's maximum). Providers whose API requires a limit (Anthropic, and
        /// Anthropic models on Bedrock) still send <see cref="DefaultMaxTokens"/>. An explicit <c>MaxTokens</c> is always
        /// sent. Default: true, except on <c>OllamaClient</c> and <c>OllamaCloudClient</c>, where it is false.
        /// </summary>
        public bool SendDefaultMaxTokens { get; set; } = true;

        /// <summary>
        /// The most inline media (<see cref="ContentPart.Data"/>) one chat request may carry, counted as encoded on the
        /// wire (base64, plus the data URL prefix where the provider uses one). A larger request throws
        /// <see cref="ArgumentException"/> before it is sent, naming the provider's route for large media. Default: the
        /// provider's documented request limit, or 20 MB where none is documented.
        /// </summary>
        public long MaxInlineMediaBytes
        {
            get => _MaxInlineMediaBytes ?? DefaultMaxInlineMediaBytes;
            set
            {
                if (value <= 0) throw new ArgumentOutOfRangeException(nameof(MaxInlineMediaBytes), "MaxInlineMediaBytes must be greater than zero.");
                _MaxInlineMediaBytes = value;
            }
        }

        private long? _MaxInlineMediaBytes = null;

        #endregion

        #region Completion-Methods

        /// <summary>
        /// Send a single user prompt and return the full response. The system prompt, when set, comes from the options.
        /// </summary>
        /// <param name="prompt">User message.</param>
        /// <param name="options">Optional per-call settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A ChatResponse containing the completion text, metadata, and timing.</returns>
        /// <exception cref="ArgumentNullException">Thrown when prompt is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when no model is configured.</exception>
        public Task<ChatResponse> ChatAsync(string prompt, CompletionOptions? options = null, CancellationToken token = default)
        {
            RequireCapability(Capability.Chat, nameof(ChatAsync));
            ArgumentNullException.ThrowIfNull(prompt);
            return ChatAsync(FromPrompt(prompt, options), token);
        }

        /// <summary>
        /// Send one message, which may carry images, audio, or video, and return the full response.
        /// </summary>
        /// <param name="message">The message, usually from <see cref="ChatMessage.User(string, ContentPart[])"/>.</param>
        /// <param name="options">Optional per-call settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A ChatResponse containing the completion text, metadata, and timing.</returns>
        /// <exception cref="ArgumentNullException">Thrown when message is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the message or one of its parts is not valid.</exception>
        /// <exception cref="CapabilityNotSupportedException">Thrown when a part's modality is not supported.</exception>
        /// <exception cref="InvalidOperationException">Thrown when no model is configured.</exception>
        public Task<ChatResponse> ChatAsync(ChatMessage message, CompletionOptions? options = null, CancellationToken token = default)
        {
            RequireCapability(Capability.Chat, nameof(ChatAsync));
            ArgumentNullException.ThrowIfNull(message);
            return ChatAsync(new ChatRequest { Messages = { message }, Options = options }, token);
        }

        /// <summary>
        /// Send a conversation and return the full response.
        /// </summary>
        /// <param name="messages">The conversation, in order.</param>
        /// <param name="options">Optional per-call settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A ChatResponse containing the completion text, metadata, and timing.</returns>
        /// <exception cref="ArgumentNullException">Thrown when messages is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the conversation is empty or a message or part is not valid.</exception>
        /// <exception cref="CapabilityNotSupportedException">Thrown when a part's modality is not supported.</exception>
        /// <exception cref="InvalidOperationException">Thrown when no model is configured.</exception>
        public Task<ChatResponse> ChatAsync(IEnumerable<ChatMessage> messages, CompletionOptions? options = null, CancellationToken token = default)
        {
            RequireCapability(Capability.Chat, nameof(ChatAsync));
            ArgumentNullException.ThrowIfNull(messages);
            return ChatAsync(new ChatRequest { Messages = messages.ToList(), Options = options }, token);
        }

        /// <summary>
        /// Send a chat request, optionally with tools, and return assistant text and any tool calls. Tool execution is
        /// owned by the caller; append ToAssistantMessage and tool-result messages to continue. When the options set a
        /// system prompt and the messages contain no system message, the system prompt is sent as the first message.
        /// </summary>
        /// <param name="request">Messages, tools, and optional per-call settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A ChatResponse containing assistant text and requested tool calls.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="ArgumentException">Thrown when request.Messages is null or empty, or a message or part is not valid.</exception>
        /// <exception cref="CapabilityNotSupportedException">Thrown when tools or a part's modality are not supported.</exception>
        /// <exception cref="InvalidOperationException">Thrown when no model is configured.</exception>
        public Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken token = default)
        {
            RequireChat(request, nameof(ChatAsync));
            ResolvedCompletion settings = ResolveChat(request, out List<ChatMessage> messages);
            return InstrumentAsync(PolyPromptTelemetryNames.OperationChat, settings.Model, s => Enrich(s, settings, request),
                () => WithApplied(ChatCoreAsync(request, messages, settings, token), settings), token);
        }

        /// <summary>
        /// Send a single user prompt and stream response chunks as they arrive.
        /// </summary>
        /// <param name="prompt">User message.</param>
        /// <param name="options">Optional per-call settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A ChatStreamingResponse containing metadata and an async enumerable of chunks.</returns>
        /// <exception cref="ArgumentNullException">Thrown when prompt is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when no model is configured.</exception>
        public Task<ChatStreamingResponse> ChatStreamingAsync(string prompt, CompletionOptions? options = null, CancellationToken token = default)
        {
            RequireCapability(Capability.Chat, nameof(ChatStreamingAsync));
            ArgumentNullException.ThrowIfNull(prompt);
            return ChatStreamingAsync(FromPrompt(prompt, options), token);
        }

        /// <summary>
        /// Send one message, which may carry images, audio, or video, and stream response chunks as they arrive.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="options">Optional per-call settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A ChatStreamingResponse containing metadata and an async enumerable of chunks.</returns>
        /// <exception cref="ArgumentNullException">Thrown when message is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the message or one of its parts is not valid.</exception>
        /// <exception cref="CapabilityNotSupportedException">Thrown when a part's modality is not supported.</exception>
        /// <exception cref="InvalidOperationException">Thrown when no model is configured.</exception>
        public Task<ChatStreamingResponse> ChatStreamingAsync(ChatMessage message, CompletionOptions? options = null, CancellationToken token = default)
        {
            RequireCapability(Capability.Chat, nameof(ChatStreamingAsync));
            ArgumentNullException.ThrowIfNull(message);
            return ChatStreamingAsync(new ChatRequest { Messages = { message }, Options = options }, token);
        }

        /// <summary>
        /// Send a conversation and stream response chunks as they arrive.
        /// </summary>
        /// <param name="messages">The conversation, in order.</param>
        /// <param name="options">Optional per-call settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A ChatStreamingResponse containing metadata and an async enumerable of chunks.</returns>
        /// <exception cref="ArgumentNullException">Thrown when messages is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the conversation is empty or a message or part is not valid.</exception>
        /// <exception cref="CapabilityNotSupportedException">Thrown when a part's modality is not supported.</exception>
        /// <exception cref="InvalidOperationException">Thrown when no model is configured.</exception>
        public Task<ChatStreamingResponse> ChatStreamingAsync(IEnumerable<ChatMessage> messages, CompletionOptions? options = null, CancellationToken token = default)
        {
            RequireCapability(Capability.Chat, nameof(ChatStreamingAsync));
            ArgumentNullException.ThrowIfNull(messages);
            return ChatStreamingAsync(new ChatRequest { Messages = messages.ToList(), Options = options }, token);
        }

        /// <summary>
        /// Send a chat request, optionally with tools, and stream response chunks as they arrive. Enumerate Chunks, then
        /// append ToAssistantMessage and tool-result messages to continue.
        /// </summary>
        /// <param name="request">Messages, tools, and optional per-call settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A ChatStreamingResponse containing streamed chunks, accumulated text, and requested tool calls.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="ArgumentException">Thrown when request.Messages is null or empty, or a message or part is not valid.</exception>
        /// <exception cref="CapabilityNotSupportedException">Thrown when tools or a part's modality are not supported.</exception>
        /// <exception cref="InvalidOperationException">Thrown when no model is configured.</exception>
        public Task<ChatStreamingResponse> ChatStreamingAsync(ChatRequest request, CancellationToken token = default)
        {
            RequireChat(request, nameof(ChatStreamingAsync));
            ResolvedCompletion settings = ResolveChat(request, out List<ChatMessage> messages);
            return InstrumentStreamingAsync(PolyPromptTelemetryNames.OperationChatStream, settings.Model, s => Enrich(s, settings, request),
                () => WithApplied(ChatStreamingCoreAsync(request, messages, settings, token), settings),
                (r, s) => r.Chunks = s.WrapStream(r.Chunks, r, c => !string.IsNullOrEmpty(c.Text) || !string.IsNullOrEmpty(c.ReasoningText) || (c.ToolCallDeltas != null && c.ToolCallDeltas.Count > 0)),
                token);
        }

        /// <summary>
        /// Generate text from a prompt. The prompt is sent unchanged: no system prompt or reasoning setting is applied.
        /// Providers with a raw completion endpoint (Ollama <c>/api/generate</c>, OpenAI <c>/v1/completions</c>) use it;
        /// the others send the prompt as a single user message.
        /// </summary>
        /// <param name="prompt">The prompt text.</param>
        /// <param name="options">Optional per-call settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A GenerationResponse containing the generated text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when prompt is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when no model is configured.</exception>
        public Task<GenerationResponse> GenerateAsync(string prompt, CompletionOptions? options = null, CancellationToken token = default)
        {
            RequireCapability(Capability.Generate, nameof(GenerateAsync));
            ArgumentNullException.ThrowIfNull(prompt);
            ResolvedCompletion settings = Resolve(options);
            return InstrumentAsync(PolyPromptTelemetryNames.OperationGenerate, settings.Model, s => Enrich(s, settings, null),
                () => WithApplied(GenerateCoreAsync(prompt, settings, token), settings), token);
        }

        /// <summary>
        /// Generate text from a prompt with streaming. See <see cref="GenerateAsync"/>.
        /// </summary>
        /// <param name="prompt">The prompt text.</param>
        /// <param name="options">Optional per-call settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A GenerationStreamingResponse containing an async enumerable of chunks.</returns>
        /// <exception cref="ArgumentNullException">Thrown when prompt is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when no model is configured.</exception>
        public Task<GenerationStreamingResponse> GenerateStreamingAsync(string prompt, CompletionOptions? options = null, CancellationToken token = default)
        {
            RequireCapability(Capability.Generate, nameof(GenerateStreamingAsync));
            ArgumentNullException.ThrowIfNull(prompt);
            ResolvedCompletion settings = Resolve(options);
            return InstrumentStreamingAsync(PolyPromptTelemetryNames.OperationGenerateStream, settings.Model, s => Enrich(s, settings, null),
                () => WithApplied(GenerateStreamingCoreAsync(prompt, settings, token), settings),
                (r, s) => r.Chunks = s.WrapStream(r.Chunks, r, c => !string.IsNullOrEmpty(c.Text) || !string.IsNullOrEmpty(c.ReasoningText)),
                token);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Provider implementation of <see cref="ChatAsync(ChatRequest, CancellationToken)"/>. Every chat overload ends here.
        /// </summary>
        /// <param name="request">The validated request (for tools and tool choice).</param>
        /// <param name="messages">Messages to send, with the system prompt prepended when it applies.</param>
        /// <param name="settings">Merged settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The chat response.</returns>
        protected virtual Task<ChatResponse> ChatCoreAsync(ChatRequest request, List<ChatMessage> messages, ResolvedCompletion settings, CancellationToken token)
        {
            throw NotSupported(Capability.Chat, "ChatAsync");
        }

        /// <summary>
        /// Provider implementation of <see cref="ChatStreamingAsync(ChatRequest, CancellationToken)"/>. Every streaming
        /// chat overload ends here.
        /// </summary>
        /// <param name="request">The validated request (for tools and tool choice).</param>
        /// <param name="messages">Messages to send, with the system prompt prepended when it applies.</param>
        /// <param name="settings">Merged settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The streaming chat response.</returns>
        protected virtual Task<ChatStreamingResponse> ChatStreamingCoreAsync(ChatRequest request, List<ChatMessage> messages, ResolvedCompletion settings, CancellationToken token)
        {
            throw NotSupported(Capability.Chat, "ChatStreamingAsync");
        }

        /// <summary>
        /// Provider implementation of <see cref="GenerateAsync"/>.
        /// </summary>
        /// <param name="prompt">Prompt (not null).</param>
        /// <param name="settings">Merged settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The generation response.</returns>
        protected virtual Task<GenerationResponse> GenerateCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token)
        {
            throw NotSupported(Capability.Generate, "GenerateAsync");
        }

        /// <summary>
        /// Provider implementation of <see cref="GenerateStreamingAsync"/>.
        /// </summary>
        /// <param name="prompt">Prompt (not null).</param>
        /// <param name="settings">Merged settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The streaming generation response.</returns>
        protected virtual Task<GenerationStreamingResponse> GenerateStreamingCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token)
        {
            throw NotSupported(Capability.Generate, "GenerateStreamingAsync");
        }

        /// <summary>
        /// The provider's default for <see cref="MaxInlineMediaBytes"/>. Default: 20 MB.
        /// </summary>
        protected virtual long DefaultMaxInlineMediaBytes => 20L * 1024 * 1024;

        /// <summary>
        /// The provider's route for media too large to send inline, used in the size error. Default: send it by URL or
        /// as a provider file reference.
        /// </summary>
        protected virtual string LargeMediaHint => "Send it by URL (ContentPart.ImageUrl or VideoUrl) or as a provider file (ContentPart.FileReferenceOf) instead.";

        /// <summary>
        /// Extra characters the provider's wire form adds to an inline part beyond its base64 (for example a data URL
        /// prefix). Default: 0.
        /// </summary>
        /// <param name="part">An inline part.</param>
        /// <returns>The extra length.</returns>
        protected virtual int InlineMediaOverhead(ContentPart part)
        {
            return 0;
        }

        /// <summary>
        /// Whether the provider's API rejects a request without an output token limit for this model. When true,
        /// <see cref="DefaultMaxTokens"/> is sent even if <see cref="SendDefaultMaxTokens"/> is false. Default: false.
        /// </summary>
        /// <param name="model">Resolved model name.</param>
        /// <returns>True when a limit must be sent.</returns>
        protected virtual bool RequiresMaxTokens(string model)
        {
            return false;
        }

        /// <summary>
        /// Merge per-call options over <c>Defaults.Completion</c>.
        /// </summary>
        /// <param name="options">Per-call options (nullable).</param>
        /// <returns>The merged settings.</returns>
        /// <exception cref="InvalidOperationException">Thrown when neither the call nor the defaults set a model.</exception>
        protected ResolvedCompletion Resolve(CompletionOptions? options)
        {
            CompletionOptions defaults = Defaults.Completion ?? throw NotSupported(Capability.Chat, "Resolve");
            ValidateOptionsType(options, defaults, nameof(CompletionOptions));
            string? model = options?.Model ?? defaults.Model;
            if (string.IsNullOrWhiteSpace(model))
                throw new InvalidOperationException(NoModelMessage("Completion"));

            return new ResolvedCompletion(
                model,
                options?.MaxTokens ?? defaults.MaxTokens ?? (SendDefaultMaxTokens || RequiresMaxTokens(model) ? DefaultMaxTokens : (int?)null),
                options?.Temperature ?? defaults.Temperature,
                options?.TopP ?? defaults.TopP,
                options?.SystemPrompt ?? defaults.SystemPrompt,
                options?.ReasoningEffort ?? defaults.ReasoningEffort,
                options);
        }

        /// <summary>
        /// Wrap a raw chat chunk enumerable with timing and accumulation of text, reasoning, and tool calls.
        /// </summary>
        /// <param name="response">The streaming response object to update.</param>
        /// <param name="rawChunks">The raw async enumerable of chunks from the provider.</param>
        /// <param name="sw">Stopwatch started before the HTTP request was made.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="owner">Optional disposable owner released when enumeration ends.</param>
        /// <returns>A wrapped async enumerable that updates timing and accumulated output on the response.</returns>
        protected async IAsyncEnumerable<ChatStreamingChunk> WrapChunksWithTiming(
            ChatStreamingResponse response,
            IAsyncEnumerable<ChatStreamingChunk> rawChunks,
            Stopwatch sw,
            [EnumeratorCancellation] CancellationToken token = default,
            IDisposable? owner = null)
        {
            Dictionary<int, ToolCallAssembly> toolCalls = new Dictionary<int, ToolCallAssembly>();

            try
            {
                await foreach (ChatStreamingChunk chunk in rawChunks.WithCancellation(token).ConfigureAwait(false))
                {
                    bool hasText = !string.IsNullOrEmpty(chunk.Text);
                    bool hasToolDeltas = chunk.ToolCallDeltas != null && chunk.ToolCallDeltas.Count > 0;
                    bool hasReasoning = !string.IsNullOrEmpty(chunk.ReasoningText);

                    if (hasText || hasToolDeltas || hasReasoning)
                    {
                        if (response.ChunkCount == 0)
                        {
                            response.TimeToFirstTokenMs = sw.ElapsedMilliseconds;
                        }
                        response.ChunkCount++;
                        response.TimeToLastTokenMs = sw.ElapsedMilliseconds;
                    }

                    if (hasText)
                    {
                        response.Text = string.IsNullOrEmpty(response.Text)
                            ? chunk.Text
                            : response.Text + chunk.Text;
                    }

                    if (hasReasoning)
                    {
                        response.Reasoning = string.IsNullOrEmpty(response.Reasoning)
                            ? chunk.ReasoningText
                            : response.Reasoning + chunk.ReasoningText;
                    }

                    if (hasToolDeltas)
                    {
                        response.ToolCallDeltaCount += chunk.ToolCallDeltas!.Count;

                        foreach (ToolCallDelta delta in chunk.ToolCallDeltas)
                        {
                            if (!toolCalls.TryGetValue(delta.Index, out ToolCallAssembly? assembly))
                            {
                                assembly = new ToolCallAssembly(delta.Index);
                                toolCalls[delta.Index] = assembly;
                            }

                            assembly.Apply(delta);
                        }

                        RefreshToolCalls(response, toolCalls);
                    }

                    if (chunk.Usage != null) response.Usage = chunk.Usage;
                    if (chunk.FinishReason != null) response.FinishReason = chunk.FinishReason;
                    if (chunk.ResponseId != null) response.ResponseId = chunk.ResponseId;
                    if (chunk.Model != null) response.Model = chunk.Model;

                    yield return chunk;
                }

                sw.Stop();
                response.OverallRuntimeMs = sw.ElapsedMilliseconds;

                int tokenCount = response.Usage?.CompletionTokens ?? response.ChunkCount;

                if (tokenCount > 0 && response.OverallRuntimeMs > 0)
                {
                    response.OverallTokensPerSecond = tokenCount / (response.OverallRuntimeMs / 1000.0);
                }

                if (tokenCount > 0 && response.TimeToLastTokenMs > response.TimeToFirstTokenMs)
                {
                    double interTokenSec = (response.TimeToLastTokenMs - response.TimeToFirstTokenMs) / 1000.0;
                    response.InterTokenTokensPerSecond = tokenCount / interTokenSec;
                }
            }
            finally
            {
                if (sw.IsRunning)
                {
                    sw.Stop();
                    response.OverallRuntimeMs = sw.ElapsedMilliseconds;
                }
                owner?.Dispose();
            }
        }

        /// <summary>
        /// Wrap a raw generation chunk enumerable with timing-tracking logic.
        /// </summary>
        /// <param name="response">The streaming response object to update with timing.</param>
        /// <param name="rawChunks">The raw async enumerable of generation chunks.</param>
        /// <param name="sw">Stopwatch started before the HTTP request was made.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="owner">Optional disposable owner released when enumeration ends.</param>
        /// <returns>A wrapped async enumerable that updates timing on the response as chunks flow through.</returns>
        protected async IAsyncEnumerable<GenerationStreamingChunk> WrapGenerationChunksWithTiming(
            GenerationStreamingResponse response,
            IAsyncEnumerable<GenerationStreamingChunk> rawChunks,
            Stopwatch sw,
            [EnumeratorCancellation] CancellationToken token = default,
            IDisposable? owner = null)
        {
            try
            {
                await foreach (GenerationStreamingChunk chunk in rawChunks.WithCancellation(token).ConfigureAwait(false))
                {
                    if (!string.IsNullOrEmpty(chunk.Text) || !string.IsNullOrEmpty(chunk.ReasoningText))
                    {
                        if (response.ChunkCount == 0)
                        {
                            response.TimeToFirstTokenMs = sw.ElapsedMilliseconds;
                        }
                        response.ChunkCount++;
                        response.TimeToLastTokenMs = sw.ElapsedMilliseconds;
                    }

                    if (!string.IsNullOrEmpty(chunk.ReasoningText)) response.Reasoning = (response.Reasoning ?? "") + chunk.ReasoningText;
                    if (chunk.Usage != null) response.Usage = chunk.Usage;
                    if (chunk.ResponseId != null) response.ResponseId = chunk.ResponseId;
                    if (chunk.FinishReason != null) response.FinishReason = chunk.FinishReason;

                    yield return chunk;
                }

                sw.Stop();
                response.OverallRuntimeMs = sw.ElapsedMilliseconds;

                if (response.ChunkCount > 0 && response.OverallRuntimeMs > 0)
                {
                    response.OverallTokensPerSecond = response.ChunkCount / (response.OverallRuntimeMs / 1000.0);
                }

                if (response.ChunkCount > 0 && response.TimeToLastTokenMs > response.TimeToFirstTokenMs)
                {
                    double interTokenSec = (response.TimeToLastTokenMs - response.TimeToFirstTokenMs) / 1000.0;
                    response.InterTokenTokensPerSecond = response.ChunkCount / interTokenSec;
                }
            }
            finally
            {
                if (sw.IsRunning)
                {
                    sw.Stop();
                    response.OverallRuntimeMs = sw.ElapsedMilliseconds;
                }
                owner?.Dispose();
            }
        }

        /// <summary>
        /// Normalizes a reasoning value: returns null for null/empty/whitespace, otherwise the value unchanged, so an
        /// absent or empty reasoning channel surfaces as null rather than an empty string.
        /// </summary>
        /// <param name="value">The raw reasoning string.</param>
        /// <returns>The value, or null when it is null/empty/whitespace.</returns>
        protected static string? NormalizeReasoning(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        /// <summary>
        /// True when the tool choice directive is "none".
        /// </summary>
        /// <param name="toolChoice">Tool choice directive.</param>
        /// <returns>True when tools must not be used.</returns>
        protected static bool IsToolChoiceNone(string? toolChoice)
        {
            return string.Equals(toolChoice, "none", StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region Private-Methods

        private static async Task<TResponse> WithApplied<TResponse>(Task<TResponse> core, ResolvedCompletion settings)
            where TResponse : ResponseBase
        {
            TResponse response = await core.ConfigureAwait(false);
            if (response != null && settings.AppliedReasoningEffort != null)
            {
                response.AppliedReasoningEffort = settings.AppliedReasoningEffort;
                OperationScope.Current?.SetTag(PolyPromptTelemetryNames.ReasoningEffort, settings.AppliedReasoningEffort);
            }
            return response!;
        }

        private static void Enrich(OperationScope scope, ResolvedCompletion settings, ChatRequest? request)
        {
            scope.SetCompletionSettings(settings.MaxTokens, settings.Temperature, settings.TopP);
            if (request != null)
            {
                scope.SetTag(PolyPromptTelemetryNames.ToolDefinitionCount, request.Tools?.Count ?? 0);
                if (request.Messages != null) scope.RecordInputMedia(request.Messages);
            }
        }

        private static ChatRequest FromPrompt(string prompt, CompletionOptions? options)
        {
            return new ChatRequest { Messages = { ChatMessage.User(prompt) }, Options = options, FromPrompt = true };
        }

        /// <summary>
        /// Capability checks for a chat request, before anything is resolved or sent: Chat always, ToolCalling when tools
        /// are present, and the input modality of every media part.
        /// </summary>
        private void RequireChat(ChatRequest request, string operation)
        {
            RequireCapability(Capability.Chat, operation);
            ArgumentNullException.ThrowIfNull(request);
            if (request.Tools != null && request.Tools.Count > 0) RequireCapability(Capability.ToolCalling, operation);
            if (request.Messages == null) return;

            foreach (ChatMessage message in request.Messages)
            {
                if (message?.Content == null) continue;
                foreach (ContentPart part in message.Content)
                {
                    if (part == null) continue;
                    switch (part.Type)
                    {
                        case ContentPartType.Image: RequireCapability(Capability.ImageInput, operation); break;
                        case ContentPartType.Audio: RequireCapability(Capability.AudioInput, operation); break;
                        case ContentPartType.Video: RequireCapability(Capability.VideoInput, operation); break;
                    }
                }
            }
        }

        private void CheckInlineMediaSize(List<ChatMessage> messages)
        {
            long total = 0;
            foreach (ChatMessage message in messages)
            {
                foreach (ContentPart part in message.Content)
                {
                    if (part.Data == null) continue;
                    long encoded = 4L * ((part.Data.Length + 2) / 3);
                    if (EscapeMediaInJson) encoded += 5L * Convert.ToBase64String(part.Data).Count(c => c == '+');
                    total += encoded + InlineMediaOverhead(part);
                }
            }

            if (total > MaxInlineMediaBytes)
                throw new ArgumentException(ProviderName + " accepts at most " + MaxInlineMediaBytes + " bytes of inline media per request (MaxInlineMediaBytes); this request carries " + total + ". " + LargeMediaHint);
        }

        private ResolvedCompletion ResolveChat(ChatRequest request, out List<ChatMessage> messages)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (request.Messages == null || request.Messages.Count == 0)
                throw new ArgumentException("Chat requests require at least one message.", nameof(request));

            for (int i = 0; i < request.Messages.Count; i++)
            {
                ChatMessage message = request.Messages[i];
                if (message == null)
                    throw new ArgumentException("Messages[" + i + "] cannot be null.", nameof(request));

                bool isUser = string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase);
                for (int j = 0; j < message.Content.Count; j++)
                {
                    string path = "Messages[" + i + "].Content[" + j + "]";
                    ContentPart part = message.Content[j];
                    if (part == null) throw new ArgumentException(path + " cannot be null.", nameof(request));
                    part.Validate(path);
                    if (part.Type != ContentPartType.Text && !isUser)
                        throw new ArgumentException(path + ": " + part.Type + " parts are accepted only on user messages (this message's role is '" + message.Role + "').", nameof(request));
                }
            }

            CheckInlineMediaSize(request.Messages);
            ResolvedCompletion settings = Resolve(request.Options);

            bool hasSystem = request.Messages.Any(m => string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase));
            if (!hasSystem && !string.IsNullOrEmpty(settings.SystemPrompt))
            {
                messages = new List<ChatMessage>(request.Messages.Count + 1) { ChatMessage.System(settings.SystemPrompt) };
                messages.AddRange(request.Messages);
            }
            else
            {
                messages = request.Messages;
            }

            return settings;
        }

        private static void RefreshToolCalls(
            ChatStreamingResponse response,
            Dictionary<int, ToolCallAssembly> toolCallAssemblies)
        {
            response.ToolCalls.Clear();

            foreach (ToolCallAssembly assembly in toolCallAssemblies.Values.OrderBy(item => item.Index))
            {
                if (!string.IsNullOrWhiteSpace(assembly.Name))
                {
                    response.ToolCalls.Add(assembly.ToToolCall());
                }
            }
        }

        #endregion

        #region Protected-Classes

        /// <summary>
        /// Completion settings after merging per-call options over the client defaults.
        /// </summary>
        protected sealed class ResolvedCompletion
        {
            /// <summary>
            /// Model name (never null or empty).
            /// </summary>
            public string Model { get; }

            /// <summary>
            /// Maximum tokens to generate, or null to send no limit (see <see cref="SendDefaultMaxTokens"/>).
            /// </summary>
            public int? MaxTokens { get; }

            /// <summary>
            /// Sampling temperature, or null to send none.
            /// </summary>
            public double? Temperature { get; }

            /// <summary>
            /// Nucleus sampling value, or null to send none.
            /// </summary>
            public double? TopP { get; }

            /// <summary>
            /// System prompt, or null for none.
            /// </summary>
            public string? SystemPrompt { get; }

            /// <summary>
            /// Reasoning effort, or null to send no reasoning field.
            /// </summary>
            public ReasoningEffort? ReasoningEffort { get; }

            /// <summary>
            /// The per-call options as passed (possibly a provider-specific subtype), for reading provider settings
            /// with <see cref="ClientBase.Pick{TOptions, TValue}"/>.
            /// </summary>
            public CompletionOptions? Call { get; }

            /// <summary>
            /// The reasoning effort wire value the provider sent, set by the provider while it builds the request body.
            /// Copied to <see cref="ResponseBase.AppliedReasoningEffort"/> and the telemetry attribute.
            /// </summary>
            public string? AppliedReasoningEffort { get; set; }

            /// <summary>
            /// Initialize merged settings.
            /// </summary>
            /// <param name="model">Model.</param>
            /// <param name="maxTokens">Max tokens.</param>
            /// <param name="temperature">Temperature.</param>
            /// <param name="topP">Top-p.</param>
            /// <param name="systemPrompt">System prompt.</param>
            /// <param name="reasoningEffort">Reasoning effort.</param>
            /// <param name="call">Per-call options.</param>
            public ResolvedCompletion(
                string model,
                int? maxTokens,
                double? temperature,
                double? topP,
                string? systemPrompt,
                ReasoningEffort? reasoningEffort,
                CompletionOptions? call)
            {
                Model = model;
                MaxTokens = maxTokens;
                Temperature = temperature;
                TopP = topP;
                SystemPrompt = systemPrompt;
                ReasoningEffort = reasoningEffort;
                Call = call;
            }
        }

        #endregion
    }
}
