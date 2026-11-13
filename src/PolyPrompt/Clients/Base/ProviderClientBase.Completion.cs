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
            ResolvedCompletion settings = Resolve(options);
            return InstrumentAsync(PolyPromptTelemetryNames.OperationChat, settings.Model, s => Enrich(s, settings, null),
                () => WithApplied(ChatCoreAsync(prompt, settings, token), settings), token);
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
            ResolvedCompletion settings = Resolve(options);
            return InstrumentStreamingAsync(PolyPromptTelemetryNames.OperationChatStream, settings.Model, s => Enrich(s, settings, null),
                () => WithApplied(ChatStreamingCoreAsync(prompt, settings, token), settings),
                (r, s) => r.Chunks = s.WrapStream(r.Chunks, r, c => !string.IsNullOrEmpty(c.Text) || !string.IsNullOrEmpty(c.ReasoningText)),
                token);
        }

        /// <summary>
        /// Send a message-based chat request, optionally with tools, and return assistant text and any tool calls.
        /// Tool execution is owned by the caller; append ToAssistantMessage and tool-result messages to continue.
        /// When the options set a system prompt and the messages contain no system message, the system prompt is sent
        /// as the first message.
        /// </summary>
        /// <param name="request">Request containing messages, tools, and optional per-call settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A ToolChatResponse containing assistant text and requested tool calls.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="ArgumentException">Thrown when request.Messages is null, empty, or contains a null message.</exception>
        /// <exception cref="InvalidOperationException">Thrown when no model is configured.</exception>
        public Task<ToolChatResponse> ToolChatAsync(ToolChatRequest request, CancellationToken token = default)
        {
            RequireCapability(Capability.ToolChat, nameof(ToolChatAsync));
            ResolvedCompletion settings = ResolveToolChat(request, out List<ChatMessage> messages);
            return InstrumentAsync(PolyPromptTelemetryNames.OperationToolChat, settings.Model, s => Enrich(s, settings, request),
                () => WithApplied(ToolChatCoreAsync(request, messages, settings, token), settings), token);
        }

        /// <summary>
        /// Send a message-based chat request, optionally with tools, and stream response chunks as they arrive.
        /// Enumerate Chunks, then append ToAssistantMessage and tool-result messages to continue.
        /// </summary>
        /// <param name="request">Request containing messages, tools, and optional per-call settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A ToolChatStreamingResponse containing streamed chunks, accumulated text, and requested tool calls.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="ArgumentException">Thrown when request.Messages is null, empty, or contains a null message.</exception>
        /// <exception cref="InvalidOperationException">Thrown when no model is configured.</exception>
        public Task<ToolChatStreamingResponse> ToolChatStreamingAsync(ToolChatRequest request, CancellationToken token = default)
        {
            RequireCapability(Capability.ToolChat, nameof(ToolChatStreamingAsync));
            ResolvedCompletion settings = ResolveToolChat(request, out List<ChatMessage> messages);
            return InstrumentStreamingAsync(PolyPromptTelemetryNames.OperationToolChatStream, settings.Model, s => Enrich(s, settings, request),
                () => WithApplied(ToolChatStreamingCoreAsync(request, messages, settings, token), settings),
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
        /// Provider implementation of <see cref="ChatAsync"/>.
        /// </summary>
        /// <param name="prompt">User message (not null).</param>
        /// <param name="settings">Merged settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The chat response.</returns>
        protected virtual Task<ChatResponse> ChatCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token)
        {
            throw NotSupported(Capability.Chat, "ChatAsync");
        }

        /// <summary>
        /// Provider implementation of <see cref="ChatStreamingAsync"/>.
        /// </summary>
        /// <param name="prompt">User message (not null).</param>
        /// <param name="settings">Merged settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The streaming chat response.</returns>
        protected virtual Task<ChatStreamingResponse> ChatStreamingCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token)
        {
            throw NotSupported(Capability.Chat, "ChatStreamingAsync");
        }

        /// <summary>
        /// Provider implementation of <see cref="ToolChatAsync"/>.
        /// </summary>
        /// <param name="request">The validated request (for tools and tool choice).</param>
        /// <param name="messages">Messages to send, with the system prompt prepended when it applies.</param>
        /// <param name="settings">Merged settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The tool chat response.</returns>
        protected virtual Task<ToolChatResponse> ToolChatCoreAsync(ToolChatRequest request, List<ChatMessage> messages, ResolvedCompletion settings, CancellationToken token)
        {
            throw NotSupported(Capability.ToolChat, "ToolChatAsync");
        }

        /// <summary>
        /// Provider implementation of <see cref="ToolChatStreamingAsync"/>.
        /// </summary>
        /// <param name="request">The validated request (for tools and tool choice).</param>
        /// <param name="messages">Messages to send, with the system prompt prepended when it applies.</param>
        /// <param name="settings">Merged settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The streaming tool chat response.</returns>
        protected virtual Task<ToolChatStreamingResponse> ToolChatStreamingCoreAsync(ToolChatRequest request, List<ChatMessage> messages, ResolvedCompletion settings, CancellationToken token)
        {
            throw NotSupported(Capability.ToolChat, "ToolChatStreamingAsync");
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
        /// Wrap a raw chunk enumerable with timing-tracking logic that updates the response object.
        /// </summary>
        /// <param name="response">The streaming response object to update with timing.</param>
        /// <param name="rawChunks">The raw async enumerable of chunks from the provider.</param>
        /// <param name="sw">Stopwatch started before the HTTP request was made.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="owner">Optional disposable owner released when enumeration ends.</param>
        /// <returns>A wrapped async enumerable that updates timing on the response as chunks flow through.</returns>
        protected async IAsyncEnumerable<ChatStreamingChunk> WrapChunksWithTiming(
            ChatStreamingResponse response,
            IAsyncEnumerable<ChatStreamingChunk> rawChunks,
            Stopwatch sw,
            [EnumeratorCancellation] CancellationToken token = default,
            IDisposable? owner = null)
        {
            try
            {
                await foreach (ChatStreamingChunk chunk in rawChunks.WithCancellation(token).ConfigureAwait(false))
                {
                    bool hasReasoning = !string.IsNullOrEmpty(chunk.ReasoningText);

                    if (!string.IsNullOrEmpty(chunk.Text) || hasReasoning)
                    {
                        if (response.ChunkCount == 0)
                        {
                            response.TimeToFirstTokenMs = sw.ElapsedMilliseconds;
                        }
                        response.ChunkCount++;
                        response.TimeToLastTokenMs = sw.ElapsedMilliseconds;
                    }

                    if (hasReasoning)
                    {
                        response.Reasoning = string.IsNullOrEmpty(response.Reasoning)
                            ? chunk.ReasoningText
                            : response.Reasoning + chunk.ReasoningText;
                    }

                    if (chunk.Usage != null) response.Usage = chunk.Usage;
                    if (chunk.FinishReason != null) response.FinishReason = chunk.FinishReason;
                    if (chunk.ResponseId != null) response.ResponseId = chunk.ResponseId;

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
        /// Wrap a raw tool-chat chunk enumerable with timing and accumulation logic.
        /// </summary>
        /// <param name="response">The streaming response object to update.</param>
        /// <param name="rawChunks">The raw async enumerable of chunks from the provider.</param>
        /// <param name="sw">Stopwatch started before the HTTP request was made.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="owner">Optional disposable owner released when enumeration ends.</param>
        /// <returns>A wrapped async enumerable that updates timing and accumulated output on the response.</returns>
        protected async IAsyncEnumerable<ToolChatStreamingChunk> WrapToolChatChunksWithTiming(
            ToolChatStreamingResponse response,
            IAsyncEnumerable<ToolChatStreamingChunk> rawChunks,
            Stopwatch sw,
            [EnumeratorCancellation] CancellationToken token = default,
            IDisposable? owner = null)
        {
            Dictionary<int, ToolCallAssembly> toolCalls = new Dictionary<int, ToolCallAssembly>();

            try
            {
                await foreach (ToolChatStreamingChunk chunk in rawChunks.WithCancellation(token).ConfigureAwait(false))
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

        private static void Enrich(OperationScope scope, ResolvedCompletion settings, ToolChatRequest? request)
        {
            scope.SetCompletionSettings(settings.MaxTokens, settings.Temperature, settings.TopP);
            if (request != null)
            {
                scope.SetTag(PolyPromptTelemetryNames.ToolDefinitionCount, request.Tools?.Count ?? 0);
            }
        }

        private ResolvedCompletion ResolveToolChat(ToolChatRequest request, out List<ChatMessage> messages)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (request.Messages == null || request.Messages.Count == 0)
                throw new ArgumentException("Tool chat requests require at least one message.", nameof(request));

            for (int i = 0; i < request.Messages.Count; i++)
            {
                if (request.Messages[i] == null)
                    throw new ArgumentException("Messages[" + i + "] cannot be null.", nameof(request));
            }

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
            ToolChatStreamingResponse response,
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
