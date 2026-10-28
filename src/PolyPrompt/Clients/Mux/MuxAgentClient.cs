namespace PolyPrompt.Clients
{
    using System.Text;
    using System.Text.Json;
    using PolyPrompt.Models;
    using SyslogLogging;

    /// <summary>
    /// Agent harness client for the Mux CLI (<c>mux</c>). Each run is <c>mux print --output-format jsonl</c> with the
    /// prompt on standard input; the final text is assembled from the streamed <c>assistant_text</c> events (the same
    /// text <c>--output-last-message</c> would write), so no temporary file is used. Mux talks to whichever endpoint its
    /// own configuration (<c>~/.mux/endpoints.json</c>, or <c>MUX_CONFIG_DIR</c>) selects; <see cref="Endpoint"/> picks a
    /// named endpoint and the model overrides that endpoint's model name. Mux has no login: credentials live in the
    /// endpoint configuration, and the connectivity check runs <c>mux probe</c>, which sends one small request to the
    /// endpoint.
    /// <para>
    /// This client always runs the <c>mux</c> CLI as a subprocess (decision D4). Hosting Mux in-process through the
    /// <c>Mux.Core</c> package is a possible future opt-in fast path; PolyPrompt does not reference it.
    /// </para>
    /// <para>
    /// Sessions: <c>mux print</c> is stateless unless a session flag is given. A run resumes a session with
    /// <c>--resume &lt;id&gt;</c> (from <see cref="CliAgentRequest.SessionId"/> or the provider state in the history).
    /// When <see cref="PersistSessions"/> is true, a new run is started under <c>--session-id &lt;new id&gt;</c> so a
    /// later run can resume it; otherwise the run reports no session id and history is replayed into the prompt.
    /// </para>
    /// <para>
    /// Permission mapping (Mux's <c>--sandbox</c> is an application-level confinement of its own tools; it does not
    /// OS-sandbox subprocesses): <see cref="CliPermissionMode.Restricted"/> uses
    /// <c>--approval-policy auto --sandbox read-only --deny-tools web_search,web_retrieve</c>, so only workspace reads
    /// and searches run, every mutating tool (write, edit, delete, directory changes, <c>run_process</c>) is refused, and
    /// the network tools are removed. <see cref="CliPermissionMode.ReadOnly"/> uses
    /// <c>--approval-policy auto --sandbox read-only</c> (reads, searches, and web reads run; mutating tools are refused).
    /// <see cref="CliPermissionMode.WorkspaceWrite"/> uses
    /// <c>--approval-policy auto --sandbox workspace-write --deny-tools run_process</c>: file writes are confined to the
    /// working directory, and <c>run_process</c> is removed because Mux cannot confine a subprocess.
    /// <see cref="CliPermissionMode.Unrestricted"/> uses <c>--yolo --sandbox none</c>. <c>--approval-policy auto</c> is
    /// not a bypass here: with a sandbox posture, any tool that would otherwise need an approval is refused before it
    /// runs (the equivalent of Codex's <c>approval_policy=never</c> inside its read-only sandbox). Subagents inherit the
    /// sandbox posture and the denied tools. A refused tool call ends the run with exit code 2, which this client reports
    /// as a failure.
    /// </para>
    /// </summary>
    public class MuxAgentClient : CliAgentClientBase
    {
        #region Public-Members

        /// <summary>
        /// Default executable name.
        /// </summary>
        public const string DefaultExecutable = "mux";

        /// <summary>
        /// Mux resumes persisted sessions with <c>mux print --resume &lt;id&gt;</c>.
        /// </summary>
        public override bool SupportsSessionResume
        {
            get { return true; }
        }

        /// <summary>
        /// Named endpoint from the Mux configuration (<c>--endpoint</c>), or null for Mux's default endpoint. Also used by
        /// the connectivity check and <see cref="ListModelsAsync"/>.
        /// </summary>
        public string? Endpoint { get; set; } = null;

        /// <summary>
        /// True to pass <c>--show-thinking</c> so the model's reasoning streams as
        /// <see cref="CliAgentEventKind.ReasoningDelta"/> events. Default: true.
        /// </summary>
        public bool ShowThinking { get; set; } = true;

        /// <summary>
        /// True to start each new run under a fresh <c>--session-id</c> so it is saved and can be resumed. Default: false,
        /// matching <c>mux print</c>, which does not persist sessions unless asked.
        /// </summary>
        public bool PersistSessions { get; set; } = false;

        #endregion

        #region Protected-Members

        /// <inheritdoc />
        protected override string HarnessName
        {
            get { return "Mux"; }
        }

        #endregion

        #region Private-Members

        private const int _ProbeTimeoutMs = 60000;
        private const string _StartedKey = "mux:started";
        private const string _LastErrorKey = "mux:lastError";
        private const string _PendingToolKey = "mux:pendingTool";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a Mux client.
        /// </summary>
        /// <param name="executable">Executable name or path. Default: <c>mux</c>.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        public MuxAgentClient(string executable = DefaultExecutable, LoggingModule? logging = null)
            : base(executable, logging ?? new LoggingModule())
        {
            _Header = "[Mux] ";
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// List the models the configured endpoints serve (<c>mux endpoint models [endpoint] --output-format json</c>),
        /// queried live from each backend: the endpoint named by <see cref="Endpoint"/>, or every configured endpoint.
        /// <see cref="ModelInformation.Name"/> is the model id to pass as the model; the endpoint name, adapter type,
        /// whether it is the default endpoint, and whether the model is the endpoint's configured model are in
        /// <see cref="ModelInformation.Metadata"/>. Mux exits non-zero when any endpoint cannot be queried.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The models.</returns>
        /// <exception cref="CliAgentException">Thrown when the CLI is missing or the command fails.</exception>
        public IAsyncEnumerable<ModelInformation> ListModelsAsync(CancellationToken token = default)
        {
            List<string> args = new List<string> { "endpoint", "models" };
            if (!string.IsNullOrWhiteSpace(Endpoint)) args.Add(Endpoint!);
            args.Add("--output-format");
            args.Add("json");
            return ListModelsFromCommandAsync(args, ParseModels, token);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Build <c>mux print --output-format jsonl ...</c>; the prompt travels on standard input and system instructions
        /// use <c>--append-system-prompt</c>, which keeps Mux's own system prompt (tool guidance, project instructions).
        /// </summary>
        /// <param name="state">Run state.</param>
        /// <returns>The invocation.</returns>
        protected override CliInvocation BuildInvocation(CliRunState state)
        {
            List<string> args = new List<string> { "print", "--output-format", "jsonl" };

            if (!string.IsNullOrWhiteSpace(Endpoint))
            {
                args.Add("--endpoint");
                args.Add(Endpoint!);
            }

            if (!string.IsNullOrWhiteSpace(state.Model))
            {
                args.Add("--model");
                args.Add(state.Model!);
            }

            switch (state.PermissionMode)
            {
                case CliPermissionMode.Unrestricted:
                    args.Add("--yolo");
                    args.Add("--sandbox");
                    args.Add("none");
                    break;
                case CliPermissionMode.WorkspaceWrite:
                    args.Add("--approval-policy");
                    args.Add("auto");
                    args.Add("--sandbox");
                    args.Add("workspace-write");
                    args.Add("--deny-tools");
                    args.Add("run_process");
                    break;
                case CliPermissionMode.ReadOnly:
                    args.Add("--approval-policy");
                    args.Add("auto");
                    args.Add("--sandbox");
                    args.Add("read-only");
                    break;
                default:
                    args.Add("--approval-policy");
                    args.Add("auto");
                    args.Add("--sandbox");
                    args.Add("read-only");
                    args.Add("--deny-tools");
                    args.Add("web_search,web_retrieve");
                    break;
            }

            if (ShowThinking) args.Add("--show-thinking");

            string? system = SystemInstructions(state.Request);
            if (!string.IsNullOrWhiteSpace(system))
            {
                args.Add("--append-system-prompt");
                args.Add(system!);
            }

            if (!string.IsNullOrEmpty(state.ResumeSessionId))
            {
                args.Add("--resume");
                args.Add(state.ResumeSessionId!);
            }
            else if (PersistSessions)
            {
                args.Add("--session-id");
                args.Add(Guid.NewGuid().ToString());
            }

            return new CliInvocation { Arguments = args, StandardInput = state.Prompt };
        }

        /// <summary>
        /// Map one <c>mux print --output-format jsonl</c> line (contract version 2).
        /// <code>
        /// run_started {sessionId, model}                 -> Started (session id when not empty; model is the reported model)
        /// assistant_text {text}                          -> TextDelta (the concatenation is the final text)
        /// assistant_thinking {text}                      -> ReasoningDelta (only with --show-thinking)
        /// tool_call_proposed {toolCall{id,name,arguments}} -> ToolUse
        /// tool_call_approved                             -> (none)
        /// tool_call_completed {toolCallId,toolName,result{success,content}} -> ToolResult (IsError when success is false)
        /// error, before run_started                      -> failure (bad flag value, unknown endpoint, missing session)
        /// error tool_call_denied|approval_error|tool_execution_error -> Notice (IsError); ToolResult (IsError) for a
        ///                                                   denied pending call; the run ends with a non-zero exit
        /// error, any other code                          -> failure (model request, connection, context, budget)
        /// task_plan_updated {changeKind, tasks}          -> Notice (rendered task list)
        /// hook {hookEvent, hookName, outcome, message}   -> Notice (IsError when blocked or warning)
        /// context_compacted                              -> Notice
        /// heartbeat, context_status                      -> (none)
        /// run_completed {status, sessionId, usage}       -> usage and cost; status completed -> success, otherwise
        ///                                                   failure (max_iterations_reached is TurnLimitReached)
        /// </code>
        /// Usage (on <c>run_completed</c>): inputTokens -> PromptTokens, outputTokens -> CompletionTokens, cachedTokens ->
        /// CachedPromptTokens, reasoningTokens -> ReasoningTokens, totalTokens -> TotalTokens (Mux reports a total), and
        /// usage.costUsd -> CostUsd only when Mux reports it (the model has a rate in Mux's pricing.json). Mux's
        /// <c>estimatedTokens</c> is an estimate and is not mapped; CacheCreationTokens is not reported and stays null.
        /// </summary>
        /// <param name="line">Line.</param>
        /// <param name="state">Run state.</param>
        protected override void ParseLine(string line, CliRunState state)
        {
            JsonElement root = ParseJsonObject(line);
            string? type = GetString(root, "eventType");
            state.CurrentEventType = type;

            switch (type)
            {
                case "run_started":
                    {
                        state.Properties[_StartedKey] = true;
                        string? model = GetString(root, "model");
                        if (!string.IsNullOrEmpty(model)) state.ReportedModel = model;
                        string? sessionId = GetString(root, "sessionId");
                        state.Started(string.IsNullOrEmpty(sessionId) ? null : sessionId);
                        break;
                    }

                case "assistant_text":
                    state.Text(GetString(root, "text"));
                    break;

                case "assistant_thinking":
                    state.Reasoning(GetString(root, "text"));
                    break;

                case "tool_call_proposed":
                    {
                        JsonElement? call = GetObject(root, "toolCall");
                        if (!call.HasValue) break;
                        string? id = GetString(call.Value, "id");
                        string? name = GetString(call.Value, "name");
                        state.Properties[_PendingToolKey] = new KeyValuePair<string?, string?>(id, name);
                        state.ToolUse(name, id, GetRendered(call.Value, "arguments"));
                        break;
                    }

                case "tool_call_completed":
                    {
                        string? id = GetString(root, "toolCallId");
                        JsonElement? result = GetObject(root, "result");
                        bool isError = result.HasValue && GetBool(result.Value, "success") == false;
                        string? output = result.HasValue ? GetRendered(result.Value, "content") : null;
                        state.Properties.Remove(_PendingToolKey);
                        state.ToolResult(GetString(root, "toolName"), id, output, isError);
                        break;
                    }

                case "error":
                    ParseError(root, state);
                    break;

                case "task_plan_updated":
                    state.Notice(RenderTaskPlan(root));
                    break;

                case "hook":
                    {
                        string? outcome = GetString(root, "outcome");
                        string text = "Hook " + (GetString(root, "hookName") ?? "?") + " (" + (GetString(root, "hookEvent") ?? "?") + ") " + (outcome ?? "ran");
                        string? message = GetString(root, "message");
                        if (!string.IsNullOrEmpty(message)) text += ": " + message;
                        state.Notice(text, outcome == "blocked" || outcome == "warning");
                        break;
                    }

                case "context_compacted":
                    {
                        string text = "Context compacted (" + (GetString(root, "strategy") ?? GetString(root, "mode") ?? "summary") + ")";
                        int? before = GetInt(root, "messagesBefore");
                        int? after = GetInt(root, "messagesAfter");
                        if (before.HasValue && after.HasValue) text += ": " + before.Value + " -> " + after.Value + " messages";
                        state.Notice(text);
                        break;
                    }

                case "run_completed":
                    ParseCompleted(root, state);
                    break;

                default:
                    break;
            }
        }

        /// <summary>
        /// Check backend access with <c>mux probe --output-format json</c>, which validates the configuration and sends one
        /// small request to the selected endpoint. Mux has no login command; this is the closest equivalent.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The check.</returns>
        protected override async Task<CliAuthenticationCheck?> CheckAuthenticationAsync(CancellationToken token)
        {
            List<string> args = new List<string> { "probe", "--output-format", "json" };
            if (!string.IsNullOrWhiteSpace(Endpoint))
            {
                args.Add("--endpoint");
                args.Add(Endpoint!);
            }
            if (!string.IsNullOrWhiteSpace(Model))
            {
                args.Add("--model");
                args.Add(Model!);
            }

            CliCommandResult probe = await RunCommandAsync(args, Math.Min(TimeoutMs, _ProbeTimeoutMs), token).ConfigureAwait(false);
            JsonElement? payload = TryParseObject(probe.StandardOutput);

            if (probe.Succeeded)
            {
                string endpoint = payload.HasValue ? GetString(payload.Value, "endpointName") ?? "" : "";
                string model = payload.HasValue ? GetString(payload.Value, "model") ?? "" : "";
                return new CliAuthenticationCheck { Authenticated = true, Message = "Mux reached endpoint " + endpoint + " (" + model + ")." };
            }

            if (payload.HasValue)
            {
                string? category = GetString(payload.Value, "failureCategory");
                string? code = GetString(payload.Value, "errorCode");
                string? message = GetString(payload.Value, "errorMessage");
                string prefix = category == "authentication"
                    ? "Mux probe: authentication failed"
                    : "Mux probe failed";
                string details = string.Join(", ", new[] { code, category }.Where(s => !string.IsNullOrEmpty(s)));
                return new CliAuthenticationCheck
                {
                    Authenticated = false,
                    Message = prefix + (details.Length > 0 ? " (" + details + ")" : "") + ": " + (message ?? "").Trim(),
                };
            }

            string fallback = probe.Error ?? FirstNonEmptyLine(probe.StandardError + "\n" + probe.StandardOutput) ?? ("Mux probe exited with code " + probe.ExitCode + ".");
            return new CliAuthenticationCheck { Authenticated = false, Message = fallback };
        }

        /// <summary>
        /// Run the base connectivity check, then report a probe failure that is not about credentials (an unreachable
        /// backend, a configuration error, a rate limit) as <see cref="CliConnectivityStatus.Error"/> instead of
        /// <see cref="CliConnectivityStatus.NotAuthenticated"/>.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The connectivity result.</returns>
        protected override async Task<CliConnectivityResult> ValidateConnectivityCoreAsync(CancellationToken token)
        {
            CliConnectivityResult result = await base.ValidateConnectivityCoreAsync(token).ConfigureAwait(false);
            if (result.Status == CliConnectivityStatus.NotAuthenticated && ClassifyMessage(result.Message) != CliFailureKind.NotAuthenticated)
                result.Status = CliConnectivityStatus.Error;
            return result;
        }

        #endregion

        #region Private-Methods

        private static void ParseError(JsonElement root, CliRunState state)
        {
            string? code = GetString(root, "errorCode") ?? GetString(root, "code");
            string? category = GetString(root, "failureCategory");
            string? message = GetString(root, "message");
            string described = DescribeError(message, code);

            bool started = state.Properties.ContainsKey(_StartedKey);
            bool toolError = code == "tool_call_denied" || code == "approval_error" || code == "tool_execution_error"
                || category == "approval" || category == "tool";

            if (started && toolError)
            {
                // Mux keeps going after a refused or failed tool call and reports the outcome on run_completed.
                state.Properties[_LastErrorKey] = described;
                if (state.Properties.TryGetValue(_PendingToolKey, out object? pending) && pending is KeyValuePair<string?, string?> tool)
                {
                    state.Properties.Remove(_PendingToolKey);
                    state.ToolResult(tool.Value, tool.Key, message, true);
                }
                state.Notice(described, true);
                return;
            }

            state.Fail(described, KindForCode(code));
        }

        private static void ParseCompleted(JsonElement root, CliRunState state)
        {
            string? sessionId = GetString(root, "sessionId");
            if (!string.IsNullOrEmpty(sessionId)) state.SessionId = sessionId;

            JsonElement? usage = GetObject(root, "usage");
            if (usage.HasValue)
            {
                state.Usage = new TokenUsage
                {
                    PromptTokens = GetInt(usage.Value, "inputTokens"),
                    CompletionTokens = GetInt(usage.Value, "outputTokens"),
                    CachedPromptTokens = GetInt(usage.Value, "cachedTokens"),
                    ReasoningTokens = GetInt(usage.Value, "reasoningTokens"),
                    TotalTokens = GetInt(usage.Value, "totalTokens"),
                };
                state.CostUsd = GetDecimal(usage.Value, "costUsd");
            }

            string? status = GetString(root, "status");
            if (status == "completed")
            {
                state.Complete();
                return;
            }

            string? lastError = state.Properties.TryGetValue(_LastErrorKey, out object? value) ? value as string : null;
            string message = "Mux run ended with status " + (status ?? "unknown") + (lastError != null ? ": " + lastError : ".");
            state.Fail(message, status == "max_iterations_reached" ? CliFailureKind.TurnLimitReached : (CliFailureKind?)null);
        }

        private static CliFailureKind? KindForCode(string? code)
        {
            switch (code)
            {
                case "invalid_argument":
                case "unsupported_option":
                case "endpoint_not_found":
                    return CliFailureKind.InvalidInput;
                case "max_iterations_reached":
                    return CliFailureKind.TurnLimitReached;
                default:
                    return null;
            }
        }

        private static string DescribeError(string? message, string? code)
        {
            string text = string.IsNullOrWhiteSpace(message) ? "Mux reported an error." : message!.Trim();
            return string.IsNullOrEmpty(code) ? text : text + " (" + code + ")";
        }

        private static string RenderTaskPlan(JsonElement root)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Task plan ").Append(GetString(root, "changeKind") ?? "updated");
            int? completed = GetInt(root, "completedCount");
            int? total = GetInt(root, "totalCount");
            if (completed.HasValue && total.HasValue) sb.Append(" (").Append(completed.Value).Append('/').Append(total.Value).Append(" done)");

            if (root.TryGetProperty("tasks", out JsonElement tasks) && tasks.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement task in tasks.EnumerateArray())
                {
                    string status = GetString(task, "status") ?? "pending";
                    sb.Append('\n').Append(status == "completed" ? "[x] " : "[ ] ").Append(GetString(task, "title") ?? "");
                    if (status != "completed" && status != "pending") sb.Append(" (").Append(status).Append(')');
                }
            }
            return sb.ToString();
        }

        private static string? SystemInstructions(CliAgentRequest request)
        {
            string? system = request.SystemPrompt;
            if (request.History != null)
            {
                foreach (ChatMessage message in request.History)
                {
                    if (string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(message.Content))
                        system = string.IsNullOrWhiteSpace(system) ? message.Content : system + "\n\n" + message.Content;
                }
            }
            return system;
        }

        private static List<ModelInformation> ParseModels(string output)
        {
            JsonElement root = ParseJsonObject(output.Trim());
            if (!root.TryGetProperty("endpoints", out JsonElement endpoints) || endpoints.ValueKind != JsonValueKind.Array)
                throw new JsonException("Expected an 'endpoints' array.");

            List<ModelInformation> models = new List<ModelInformation>();
            foreach (JsonElement endpoint in endpoints.EnumerateArray())
            {
                if (!endpoint.TryGetProperty("models", out JsonElement list) || list.ValueKind != JsonValueKind.Array) continue;

                string? endpointName = GetString(endpoint, "name");
                string? adapterType = GetString(endpoint, "adapterType");
                string? configured = GetString(endpoint, "configuredModel");
                bool? isDefault = GetBool(endpoint, "isDefault");

                foreach (JsonElement entry in list.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.String) continue;
                    string? name = entry.GetString();
                    if (string.IsNullOrEmpty(name)) continue;

                    ModelInformation model = new ModelInformation { Name = name!, OwnedBy = adapterType };
                    if (endpointName != null) model.Metadata["endpoint"] = endpointName;
                    if (adapterType != null) model.Metadata["adapter_type"] = adapterType;
                    if (isDefault.HasValue) model.Metadata["default_endpoint"] = isDefault.Value ? "true" : "false";
                    model.Metadata["configured"] = string.Equals(name, configured, StringComparison.OrdinalIgnoreCase) ? "true" : "false";
                    models.Add(model);
                }
            }

            return models;
        }

        private static JsonElement? TryParseObject(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            try
            {
                return ParseJsonObject(text.Trim());
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string? FirstNonEmptyLine(string text)
        {
            foreach (string line in text.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.Length > 0) return trimmed;
            }
            return null;
        }

        #endregion
    }
}
