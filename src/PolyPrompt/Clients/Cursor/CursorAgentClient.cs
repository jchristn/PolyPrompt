namespace PolyPrompt.Clients
{
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using PolyPrompt.Helpers;
    using PolyPrompt.Models;
    using SyslogLogging;

    /// <summary>
    /// Agent harness client for the Cursor CLI (<c>cursor-agent</c>). Each run is
    /// <c>cursor-agent -p --output-format stream-json --stream-partial-output</c> with the prompt on standard input (print
    /// mode reads standard input to end of file when no prompt argument is given); a session is resumed with
    /// <c>--resume &lt;chatId&gt;</c>. The installer links the same binary as both <c>agent</c> and <c>cursor-agent</c>;
    /// <c>cursor-agent</c> is the default here and callers can set <see cref="CliAgentClientBase.Executable"/> to
    /// <c>agent</c>. Authentication is the CLI's own (<c>cursor-agent login</c>, or <c>CURSOR_API_KEY</c> in the
    /// environment).
    /// <para>
    /// Permission mapping: <see cref="CliPermissionMode.Restricted"/> and <see cref="CliPermissionMode.ReadOnly"/> use
    /// <c>--mode ask</c> (Cursor's read-only Q&amp;A mode; it has no separate read-only-commands level) with
    /// <c>--sandbox enabled</c>. <see cref="CliPermissionMode.WorkspaceWrite"/> uses the default agent mode with
    /// <c>--sandbox enabled</c>: Cursor has no flag that allows workspace writes short of <c>--force</c>, so writes and
    /// commands run only where the user's or project's permission allow list (<c>cli-config.json</c>,
    /// <c>.cursor/cli.json</c>) permits them and anything that would need an approval is denied. Only
    /// <see cref="CliPermissionMode.Unrestricted"/> passes <c>--force</c> (Cursor's "Run Everything"; <c>--yolo</c> is an
    /// alias), together with <c>--sandbox disabled</c>. <c>--sandbox</c> is a per-run override; the CLI exits with an
    /// error where its sandbox is unavailable, and a caller can append <c>--sandbox disabled</c> through
    /// <see cref="CliAgentRequest.ExtraArguments"/> (the last value wins). Note that Cursor also auto-approves in print
    /// mode when the user has saved "Run Everything" as their approval mode; PolyPrompt does not change the CLI's
    /// configuration, so the read-only modes rely on <c>--mode ask</c> in that case.
    /// </para>
    /// <para>
    /// Workspace trust: print mode refuses to run in a directory the user has not trusted unless <c>--trust</c> or
    /// <c>--force</c> is passed. <c>--trust</c> records a saved trust decision, so this client never passes it; trust the
    /// directory once interactively, or add <c>--trust</c> through <see cref="CliAgentRequest.ExtraArguments"/>.
    /// </para>
    /// <para>
    /// Contract sources: https://cursor.com/docs/cli/reference/parameters, https://cursor.com/docs/cli/reference/output-format,
    /// https://cursor.com/docs/cli/headless, https://cursor.com/docs/cli/reference/permissions,
    /// https://cursor.com/docs/cli/reference/authentication, https://cursor.com/docs/cli/changelog, and the command-line
    /// parser, print-mode writer, <c>status</c> and <c>models</c> commands of the published CLI package 2026.10.22-27dcf5f.
    /// </para>
    /// </summary>
    public class CursorAgentClient : CliAgentClientBase
    {
        #region Public-Members

        /// <summary>
        /// Default executable name. The installer also links the binary as <c>agent</c>.
        /// </summary>
        public const string DefaultExecutable = "cursor-agent";

        /// <summary>
        /// Cursor resumes sessions with <c>--resume &lt;chatId&gt;</c>.
        /// </summary>
        public override bool SupportsSessionResume
        {
            get { return true; }
        }

        #endregion

        #region Protected-Members

        /// <inheritdoc />
        protected override string HarnessName
        {
            get { return "Cursor CLI"; }
        }

        #endregion

        #region Private-Members

        private const string _SegmentKey = "cursor:segment";

        private static readonly Regex _AnsiPattern = new Regex(@"\x1B\[[0-9;?]*[A-Za-z]", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex _ModelLinePattern = new Regex(
            @"^(?<id>\S+)(?: - (?<name>.*?))?(?:\s+\((?<tags>(?:current|default)(?:, (?:current|default))*)\))?$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a Cursor CLI client.
        /// </summary>
        /// <param name="executable">Executable name or path. Default: <c>cursor-agent</c> (also installed as <c>agent</c>).</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        public CursorAgentClient(string executable = DefaultExecutable, LoggingModule? logging = null)
            : base(executable, logging ?? new LoggingModule())
        {
            _Header = "[CursorCli] ";
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// List the models available to the signed-in account (<c>cursor-agent models</c>). <see cref="ModelInformation.Name"/>
        /// is the id to pass as the model and <see cref="ModelInformation.DisplayName"/> its display name; the account's
        /// default and currently selected models carry <c>default</c> and <c>current</c> set to <c>true</c> in
        /// <see cref="ModelInformation.Metadata"/>.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The models.</returns>
        /// <exception cref="CliAgentException">Thrown when the CLI is missing or the command fails.</exception>
        public IAsyncEnumerable<ModelInformation> ListModelsAsync(CancellationToken token = default)
        {
            return ListModelsFromCommandAsync(new[] { "models" }, ParseModels, token);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Build <c>cursor-agent -p --output-format stream-json --stream-partial-output ...</c>; the prompt travels on
        /// standard input. Cursor has no documented system-prompt option, so the system prompt is prepended to the prompt.
        /// </summary>
        /// <param name="state">Run state.</param>
        /// <returns>The invocation.</returns>
        protected override CliInvocation BuildInvocation(CliRunState state)
        {
            List<string> args = new List<string> { "-p", "--output-format", "stream-json", "--stream-partial-output" };

            switch (state.PermissionMode)
            {
                case CliPermissionMode.Unrestricted:
                    args.Add("--force");
                    args.Add("--sandbox");
                    args.Add("disabled");
                    break;
                case CliPermissionMode.WorkspaceWrite:
                    args.Add("--sandbox");
                    args.Add("enabled");
                    break;
                default:
                    args.Add("--mode");
                    args.Add("ask");
                    args.Add("--sandbox");
                    args.Add("enabled");
                    break;
            }

            if (!string.IsNullOrWhiteSpace(state.Model))
            {
                args.Add("--model");
                args.Add(state.Model!);
            }

            if (!string.IsNullOrEmpty(state.ResumeSessionId))
            {
                args.Add("--resume");
                args.Add(state.ResumeSessionId!);
            }

            return new CliInvocation { Arguments = args, StandardInput = state.PromptWithSystem };
        }

        /// <summary>
        /// Map one <c>stream-json</c> line.
        /// <code>
        /// system init {session_id, model}                     -> Started (session id; model is the display name)
        /// system task_notification {title, status}            -> Notice
        /// system background_shell_timeout                    -> Notice (IsError)
        /// user                                                -> (none; echo of the prompt)
        /// assistant with timestamp_ms, without model_call_id  -> TextDelta (a streamed delta)
        /// assistant with model_call_id, or without timestamp  -> (none; duplicate flush of text already streamed)
        /// thinking delta {text}                               -> ReasoningDelta
        /// thinking completed                                  -> (none)
        /// tool_call started {call_id, tool_call}              -> ToolUse (name is the tool_call key, or function.name)
        /// tool_call completed {call_id, tool_call.result}     -> ToolResult (IsError unless the result is "success")
        /// retry starting {attempt}                            -> Notice (IsError)
        /// connection reconnecting {attempt}                   -> Notice (IsError)
        /// result success {result, session_id, usage}          -> final text, usage; Completed is emitted by the base at exit
        /// result with is_error true                           -> failure; Failed is emitted by the base at exit
        /// interaction_query, retry resuming, connection reconnected, unknown types -> (none)
        /// </code>
        /// Cursor reports run errors only on standard error, after the session started; see <see cref="ClassifyFailure"/>.
        /// Usage (result.usage, the whole turn): inputTokens -> PromptTokens (Cursor reports the uncached input only),
        /// cacheReadTokens -> CachedPromptTokens, cacheWriteTokens -> CacheCreationTokens, outputTokens -> CompletionTokens.
        /// Cursor reports no total, no reasoning count, and no cost, so TotalTokens, ReasoningTokens and CostUsd stay null.
        /// </summary>
        /// <param name="line">Line.</param>
        /// <param name="state">Run state.</param>
        protected override void ParseLine(string line, CliRunState state)
        {
            JsonElement root = ParseJsonObject(line);
            string? type = GetString(root, "type");
            string? subtype = GetString(root, "subtype");
            state.CurrentEventType = subtype == null ? type : type + "." + subtype;

            switch (type)
            {
                case "system":
                    ParseSystem(root, subtype, state);
                    break;

                case "assistant":
                    ParseAssistant(root, state);
                    break;

                case "thinking":
                    if (subtype == "delta") state.Reasoning(GetString(root, "text"));
                    break;

                case "tool_call":
                    ParseToolCall(root, subtype, state);
                    break;

                case "retry":
                    if (subtype == "starting") state.Notice("Retrying (attempt " + (GetInt(root, "attempt")?.ToString() ?? "?") + ").", true);
                    break;

                case "connection":
                    if (subtype == "reconnecting") state.Notice("Reconnecting (attempt " + (GetInt(root, "attempt")?.ToString() ?? "?") + ").", true);
                    break;

                case "result":
                    ParseResult(root, subtype, state);
                    break;

                default:
                    break;
            }
        }

        /// <summary>
        /// Cursor writes a run error to standard error (not as an event) and exits non-zero. When that happens after the
        /// session started, the harness reached its backend and the error is the harness's report, so it becomes the
        /// structured failure message (and is then classified as authentication, rate limit, or provider error). Without a
        /// started session the failure is classified from standard error by the base (for example a rejected argument is
        /// a crash, a missing login is not authenticated).
        /// </summary>
        /// <param name="state">Run state.</param>
        /// <param name="process">Process outcome.</param>
        /// <returns>The failure kind.</returns>
        protected override CliFailureKind ClassifyFailure(CliRunState state, ProcessRunResult process)
        {
            if (!state.Failed && !string.IsNullOrEmpty(state.SessionId))
            {
                string? reported = ReportedError(process.StandardError);
                if (reported != null) state.Fail(reported);
            }
            return base.ClassifyFailure(state, process);
        }

        /// <summary>
        /// Check the login with <c>cursor-agent status --format json</c>. The command exits with code zero whether or not
        /// a user is logged in, so the JSON <c>isAuthenticated</c> field decides. Account details (email, user id) are
        /// not copied into the message.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The check.</returns>
        protected override async Task<CliAuthenticationCheck?> CheckAuthenticationAsync(CancellationToken token)
        {
            CliCommandResult status = await RunCommandAsync(new[] { "status", "--format", "json" }, Math.Min(TimeoutMs, 30000), token).ConfigureAwait(false);
            string output = StripAnsi(status.StandardOutput).Trim();

            if (output.StartsWith("{", StringComparison.Ordinal))
            {
                try
                {
                    JsonElement root = ParseJsonObject(output);
                    bool authenticated = GetBool(root, "isAuthenticated") == true;
                    string? statusText = GetString(root, "status");
                    string message = GetString(root, "message")
                        ?? (authenticated ? "Logged in." : "Not logged in (" + (statusText ?? "unknown") + ").");
                    return new CliAuthenticationCheck { Authenticated = authenticated && status.Succeeded, Message = message };
                }
                catch (JsonException)
                {
                    // Fall through to the text check.
                }
            }

            string text = (FirstNonEmptyLine(StripAnsi(status.StandardError)) ?? FirstNonEmptyLine(output) ?? "").Trim();
            bool ok = status.Succeeded && ClassifyMessage(output) != CliFailureKind.NotAuthenticated;
            return new CliAuthenticationCheck { Authenticated = ok, Message = text };
        }

        #endregion

        #region Private-Methods

        private static void ParseSystem(JsonElement root, string? subtype, CliRunState state)
        {
            switch (subtype)
            {
                case "init":
                    {
                        string? model = GetString(root, "model");
                        if (!string.IsNullOrEmpty(model)) state.ReportedModel = model;
                        state.Started(GetString(root, "session_id"));
                        break;
                    }

                case "task_notification":
                    {
                        string title = GetString(root, "title") ?? GetString(root, "task_id") ?? "Background task";
                        string? status = GetString(root, "status");
                        state.Notice(status == null ? title : title + " (" + status + ")");
                        break;
                    }

                case "background_shell_timeout":
                    state.Notice("Background shells still running after the turn were aborted (" + (GetInt(root, "aborted_count")?.ToString() ?? "?") + ").", true);
                    break;

                default:
                    break;
            }
        }

        /// <summary>
        /// With <c>--stream-partial-output</c>, deltas carry <c>timestamp_ms</c> and no <c>model_call_id</c>; the text of a
        /// segment is flushed again (with <c>model_call_id</c> before a tool call, without <c>timestamp_ms</c> at the end
        /// of the turn). The CLI also flushes the segment with a timestamp and no <c>model_call_id</c> before retry and
        /// interaction events, so a "delta" whose text equals the whole segment streamed so far is treated as a flush.
        /// </summary>
        private static void ParseAssistant(JsonElement root, CliRunState state)
        {
            string text = MessageText(root);
            bool hasTimestamp = root.TryGetProperty("timestamp_ms", out _);
            bool hasModelCallId = root.TryGetProperty("model_call_id", out _);
            string segment = state.Properties.TryGetValue(_SegmentKey, out object? value) && value is string s ? s : "";

            if (!hasTimestamp || hasModelCallId || (segment.Length > 0 && text == segment))
            {
                state.Properties[_SegmentKey] = "";
                return;
            }

            if (text.Length == 0) return;
            state.Properties[_SegmentKey] = segment + text;
            state.Text(text);
        }

        private static string MessageText(JsonElement root)
        {
            JsonElement? message = GetObject(root, "message");
            if (!message.HasValue) return "";
            if (!message.Value.TryGetProperty("content", out JsonElement content) || content.ValueKind != JsonValueKind.Array) return "";

            StringBuilder sb = new StringBuilder();
            foreach (JsonElement block in content.EnumerateArray())
            {
                if (block.ValueKind != JsonValueKind.Object) continue;
                if (GetString(block, "type") == "text") sb.Append(GetString(block, "text") ?? "");
            }
            return sb.ToString();
        }

        private static void ParseToolCall(JsonElement root, string? subtype, CliRunState state)
        {
            state.Properties[_SegmentKey] = "";
            string? callId = GetString(root, "call_id");
            JsonElement? toolCall = GetObject(root, "tool_call");
            string name = "tool";
            JsonElement? entry = null;
            if (toolCall.HasValue) (name, entry) = ToolEntry(toolCall.Value);

            if (subtype == "started")
            {
                state.Properties["tool:" + (callId ?? "")] = true;
                state.ToolUse(name, callId, entry.HasValue ? ToolInput(entry.Value) : null);
                return;
            }

            if (subtype != "completed") return;

            if (!state.Properties.ContainsKey("tool:" + (callId ?? "")))
                state.ToolUse(name, callId, entry.HasValue ? ToolInput(entry.Value) : null);

            string? output = null;
            bool isError = false;
            JsonElement? result = entry.HasValue ? GetObject(entry.Value, "result") : null;
            if (result.HasValue)
            {
                foreach (JsonProperty outcome in result.Value.EnumerateObject())
                {
                    isError = outcome.Name != "success";
                    output = ToolOutput(outcome.Value, outcome.Name);
                    break;
                }
            }
            state.ToolResult(name, callId, output, isError);
        }

        /// <summary>
        /// A tool call is an object keyed by its kind (<c>readToolCall</c>, <c>writeToolCall</c>, <c>shellToolCall</c>,
        /// ...), or a <c>function</c> object with <c>name</c> and <c>arguments</c>.
        /// </summary>
        private static (string Name, JsonElement? Entry) ToolEntry(JsonElement toolCall)
        {
            foreach (JsonProperty property in toolCall.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Object) continue;
                if (property.Name == "function")
                {
                    string? functionName = GetString(property.Value, "name");
                    return (string.IsNullOrEmpty(functionName) ? "function" : functionName!, property.Value);
                }
                return (property.Name, property.Value);
            }
            return ("tool", null);
        }

        private static string? ToolInput(JsonElement entry)
        {
            if (entry.TryGetProperty("args", out JsonElement args)) return args.ValueKind == JsonValueKind.String ? args.GetString() : RenderJson(args);
            if (entry.TryGetProperty("arguments", out JsonElement arguments)) return arguments.ValueKind == JsonValueKind.String ? arguments.GetString() : RenderJson(arguments);
            return null;
        }

        private static string? ToolOutput(JsonElement outcome, string outcomeName)
        {
            if (outcome.ValueKind == JsonValueKind.String) return outcome.GetString();
            if (outcome.ValueKind != JsonValueKind.Object) return RenderJson(outcome);

            string? content = GetString(outcome, "content") ?? GetString(outcome, "output");
            if (content != null) return content;

            string? error = GetString(outcome, "error") ?? GetString(outcome, "message") ?? GetString(outcome, "reason");
            if (error != null) return outcomeName == "success" ? error : outcomeName + ": " + error;
            return RenderJson(outcome);
        }

        private static void ParseResult(JsonElement root, string? subtype, CliRunState state)
        {
            string? sessionId = GetString(root, "session_id");
            if (!string.IsNullOrEmpty(sessionId)) state.SessionId = sessionId;

            JsonElement? usage = GetObject(root, "usage");
            if (usage.HasValue) state.Usage = ParseUsage(usage.Value);

            string? text = GetString(root, "result");
            if (GetBool(root, "is_error") == true || (subtype != null && subtype != "success"))
            {
                state.Fail(string.IsNullOrWhiteSpace(text) ? "Cursor CLI reported " + (subtype ?? "an error") + "." : text);
                return;
            }

            if (text != null) state.FinalText = text;
            state.Complete();
        }

        private static TokenUsage ParseUsage(JsonElement usage)
        {
            return new TokenUsage
            {
                PromptTokens = GetInt(usage, "inputTokens"),
                CachedPromptTokens = GetInt(usage, "cacheReadTokens"),
                CacheCreationTokens = GetInt(usage, "cacheWriteTokens"),
                CompletionTokens = GetInt(usage, "outputTokens"),
            };
        }

        /// <summary>
        /// The error Cursor printed to standard error, without its warnings.
        /// </summary>
        private static string? ReportedError(string standardError)
        {
            List<string> lines = new List<string>();
            foreach (string raw in StripAnsi(standardError).Split('\n'))
            {
                string trimmed = raw.Trim();
                if (trimmed.Length == 0) continue;
                if (trimmed.StartsWith("Warning:", StringComparison.Ordinal)) continue;
                if (trimmed.StartsWith("Aborting operation", StringComparison.Ordinal)) continue;
                lines.Add(trimmed);
            }
            if (lines.Count == 0) return null;
            string message = string.Join("\n", lines);
            return message.Length > 2000 ? message.Substring(message.Length - 2000) : message;
        }

        /// <summary>
        /// Parse <c>cursor-agent models</c>: an "Available models" heading, then one <c>id - Display Name</c> line per model
        /// with an optional <c>(current, default)</c> suffix, then a "Tip:" line.
        /// </summary>
        private static List<ModelInformation> ParseModels(string output)
        {
            List<ModelInformation> models = new List<ModelInformation>();
            bool sawHeading = false;

            foreach (string raw in StripAnsi(output).Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("Available models", StringComparison.OrdinalIgnoreCase))
                {
                    sawHeading = true;
                    continue;
                }
                if (line.StartsWith("No models available", StringComparison.OrdinalIgnoreCase))
                {
                    sawHeading = true;
                    continue;
                }
                if (line.StartsWith("Tip:", StringComparison.OrdinalIgnoreCase)) continue;
                if (!sawHeading) continue;

                Match match = _ModelLinePattern.Match(line);
                if (!match.Success) continue;

                ModelInformation model = new ModelInformation
                {
                    Name = match.Groups["id"].Value,
                    DisplayName = match.Groups["name"].Success && match.Groups["name"].Value.Length > 0 ? match.Groups["name"].Value.Trim() : null,
                    OwnedBy = "cursor",
                };

                if (match.Groups["tags"].Success)
                {
                    foreach (string tag in match.Groups["tags"].Value.Split(','))
                    {
                        string trimmed = tag.Trim();
                        if (trimmed.Length > 0) model.Metadata[trimmed] = "true";
                    }
                }

                models.Add(model);
            }

            if (!sawHeading) throw new FormatException("Expected an 'Available models' heading.");
            return models;
        }

        private static string StripAnsi(string text)
        {
            return string.IsNullOrEmpty(text) ? "" : _AnsiPattern.Replace(text, "");
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
