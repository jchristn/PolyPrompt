namespace PolyPrompt.Clients
{
    using System.Text;
    using System.Text.Json;
    using PolyPrompt.Models;
    using SyslogLogging;

    /// <summary>
    /// Agent harness client for Anthropic's Claude Code CLI (<c>claude</c>). Each run is
    /// <c>claude -p --output-format stream-json --verbose --include-partial-messages</c> with the prompt on standard input;
    /// a session is resumed with <c>--resume &lt;session_id&gt;</c>. The system prompt (and any system messages in the
    /// history) is passed with <c>--append-system-prompt</c>, so Claude Code keeps its own system prompt. Authentication
    /// is the CLI's own (<c>claude auth login</c>, or <c>ANTHROPIC_API_KEY</c> in the environment); connectivity checks it
    /// with <c>claude auth status --json</c>. Claude Code has no model-listing command, so this client has no
    /// <c>ListModelsAsync</c>.
    /// <para>
    /// Permission mapping (verified against Claude Code 2.1.312): <see cref="CliPermissionMode.Restricted"/> uses
    /// <c>--permission-mode dontAsk --tools Read,Glob,Grep --strict-mcp-config</c>: only the built-in file-reading tools
    /// exist, no MCP server is loaded, and anything that would prompt is denied.
    /// <see cref="CliPermissionMode.ReadOnly"/> uses <c>--permission-mode dontAsk --disallowedTools Edit,Write,NotebookEdit</c>:
    /// reads and the read-only shell commands Claude Code approves without asking run, the editing tools are denied, and
    /// anything else that would prompt is denied (allow rules in the user's own Claude Code settings still apply).
    /// <see cref="CliPermissionMode.WorkspaceWrite"/> uses <c>--permission-mode acceptEdits</c>: file edits inside the
    /// working directory run without asking and, since nobody can answer a prompt in print mode, everything else that
    /// would prompt is denied. <see cref="CliPermissionMode.Unrestricted"/> uses <c>--permission-mode bypassPermissions</c>;
    /// no other mode passes it or <c>--dangerously-skip-permissions</c>.
    /// </para>
    /// </summary>
    public class ClaudeCodeAgentClient : CliAgentClientBase
    {
        #region Public-Members

        /// <summary>
        /// Default executable name.
        /// </summary>
        public const string DefaultExecutable = "claude";

        /// <summary>
        /// Claude Code resumes sessions with <c>--resume &lt;session_id&gt;</c>.
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
            get { return "Claude Code"; }
        }

        #endregion

        #region Private-Members

        private const string _CurrentMessageKey = "claude:message";
        private const string _TextSeenPrefix = "claude:text:";
        private const string _ThinkingSeenPrefix = "claude:thinking:";
        private const string _ToolPrefix = "claude:tool:";
        private const string _ApiErrorKey = "claude:apiError";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a Claude Code client.
        /// </summary>
        /// <param name="executable">Executable name or path. Default: <c>claude</c>.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        public ClaudeCodeAgentClient(string executable = DefaultExecutable, LoggingModule? logging = null)
            : base(executable, logging ?? new LoggingModule())
        {
            _Header = "[ClaudeCode] ";
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Build <c>claude -p --output-format stream-json --verbose --include-partial-messages ...</c>; the prompt travels on
        /// standard input (plain text, the default <c>--input-format</c>), so long prompts never reach the argument list.
        /// </summary>
        /// <param name="state">Run state.</param>
        /// <returns>The invocation.</returns>
        protected override CliInvocation BuildInvocation(CliRunState state)
        {
            List<string> args = new List<string> { "-p", "--output-format", "stream-json", "--verbose", "--include-partial-messages" };

            switch (state.PermissionMode)
            {
                case CliPermissionMode.Unrestricted:
                    args.Add("--permission-mode");
                    args.Add("bypassPermissions");
                    break;
                case CliPermissionMode.WorkspaceWrite:
                    args.Add("--permission-mode");
                    args.Add("acceptEdits");
                    break;
                case CliPermissionMode.ReadOnly:
                    args.Add("--permission-mode");
                    args.Add("dontAsk");
                    args.Add("--disallowedTools");
                    args.Add("Edit,Write,NotebookEdit");
                    break;
                default:
                    args.Add("--permission-mode");
                    args.Add("dontAsk");
                    args.Add("--tools");
                    args.Add("Read,Glob,Grep");
                    args.Add("--strict-mcp-config");
                    break;
            }

            if (!string.IsNullOrWhiteSpace(state.Model))
            {
                args.Add("--model");
                args.Add(state.Model!);
            }

            string? system = SystemPrompt(state.Request);
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

            return new CliInvocation { Arguments = args, StandardInput = state.Prompt };
        }

        /// <summary>
        /// Map one <c>claude -p --output-format stream-json --verbose --include-partial-messages</c> line. Only top-level
        /// messages (<c>parent_tool_use_id</c> null) produce text, reasoning, and tool events; subagent traffic is ignored.
        /// <code>
        /// system/init {session_id, model}                 -> Started (session id; model is the reported model)
        /// system/compact_boundary                         -> Notice
        /// system/api_retry                                -> Notice (IsError)
        /// system/hook_*, status, thinking_tokens, other   -> (none)
        /// stream_event message_start                      -> (none; remembers the message id)
        /// stream_event content_block_delta text_delta     -> TextDelta
        /// stream_event content_block_delta thinking_delta -> ReasoningDelta
        /// stream_event other (tool input, signature, ...) -> (none)
        /// assistant {error, content[text]}                -> Notice (IsError); the error code is kept for the result
        /// assistant content[text|thinking]                -> TextDelta / ReasoningDelta only when no partial delta of that
        ///                                                    kind arrived for the message (partial deltas are the source)
        /// assistant content[tool_use]                     -> ToolUse (name, id, JSON input)
        /// user content[tool_result]                       -> ToolResult (ToolUse first if unseen; is_error)
        /// rate_limit_event status allowed                 -> (none)
        /// rate_limit_event other status                   -> Notice (IsError when rejected)
        /// result is_error false                           -> final text = result; usage, cost; Completed at exit
        /// result is_error true                            -> failure (error_max_turns is TurnLimitReached); usage, cost
        /// result permission_denials[]                     -> Notice (IsError) per denied tool call
        /// </code>
        /// Text: partial <c>text_delta</c> events build the streamed text and the assistant message repeats it, so the
        /// assistant copy is used only when no delta arrived for that message (for example a synthetic message); the
        /// final text is <c>result.result</c>. Usage (from <c>result.usage</c>): input_tokens -> PromptTokens,
        /// cache_read_input_tokens -> CachedPromptTokens, cache_creation_input_tokens -> CacheCreationTokens,
        /// output_tokens -> CompletionTokens, output_tokens_details.thinking_tokens -> ReasoningTokens. Claude Code reports
        /// no total, so TotalTokens stays null. total_cost_usd -> CostUsd exactly as reported.
        /// </summary>
        /// <param name="line">Line.</param>
        /// <param name="state">Run state.</param>
        protected override void ParseLine(string line, CliRunState state)
        {
            JsonElement root = ParseJsonObject(line);
            string? type = GetString(root, "type");
            string? subtype = GetString(root, "subtype");
            state.CurrentEventType = type;

            switch (type)
            {
                case "system":
                    state.CurrentEventType = subtype == null ? type : type + "/" + subtype;
                    ParseSystem(root, subtype, state);
                    break;

                case "stream_event":
                    {
                        JsonElement? evt = GetObject(root, "event");
                        if (evt.HasValue)
                        {
                            state.CurrentEventType = type + "/" + (GetString(evt.Value, "type") ?? "");
                            if (IsTopLevel(root)) ParseStreamEvent(evt.Value, state);
                        }
                        break;
                    }

                case "assistant":
                    if (IsTopLevel(root)) ParseAssistant(root, state);
                    break;

                case "user":
                    if (IsTopLevel(root)) ParseUser(root, state);
                    break;

                case "rate_limit_event":
                    {
                        JsonElement? info = GetObject(root, "rate_limit_info");
                        string? status = info.HasValue ? GetString(info.Value, "status") : null;
                        if (status != null && status != "allowed")
                        {
                            string? limitType = GetString(info!.Value, "rateLimitType");
                            state.Notice("Rate limit " + status + (limitType != null ? " (" + limitType + ")" : "") + ".", status == "rejected");
                        }
                        break;
                    }

                case "result":
                    ParseResult(root, subtype, state);
                    break;

                default:
                    break;
            }
        }

        /// <summary>
        /// Check the login with <c>claude auth status --json</c> (exit code zero when logged in). The message names the
        /// authentication method and provider only; account details in the output are not copied.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The check.</returns>
        protected override async Task<CliAuthenticationCheck?> CheckAuthenticationAsync(CancellationToken token)
        {
            CliCommandResult status = await RunCommandAsync(new[] { "auth", "status", "--json" }, Math.Min(TimeoutMs, 30000), token).ConfigureAwait(false);
            string? method = null;
            string? provider = null;
            try
            {
                JsonElement root = ParseJsonObject(status.StandardOutput.Trim());
                method = GetString(root, "authMethod");
                provider = GetString(root, "apiProvider");
            }
            catch (JsonException)
            {
            }

            string message;
            if (status.Succeeded)
            {
                message = "Logged in" + (method != null ? " using " + method : "") + (provider != null ? " (" + provider + ")" : "") + ".";
            }
            else
            {
                message = "Not logged in. Run 'claude auth login' or set ANTHROPIC_API_KEY.";
                string? stderr = FirstNonEmptyLine(status.StandardError);
                if (stderr != null) message += " " + stderr;
            }

            return new CliAuthenticationCheck { Authenticated = status.Succeeded, Message = message };
        }

        #endregion

        #region Private-Methods

        private static string? SystemPrompt(CliAgentRequest request)
        {
            string? system = request.SystemPrompt;
            if (request.History != null)
            {
                foreach (ChatMessage message in request.History)
                {
                    if (message != null && string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(message.Content))
                        system = string.IsNullOrWhiteSpace(system) ? message.Content : system + "\n\n" + message.Content;
                }
            }
            return string.IsNullOrWhiteSpace(system) ? null : system;
        }

        private static bool IsTopLevel(JsonElement root)
        {
            return !root.TryGetProperty("parent_tool_use_id", out JsonElement parent) || parent.ValueKind == JsonValueKind.Null;
        }

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

                case "compact_boundary":
                    {
                        JsonElement? metadata = GetObject(root, "compact_metadata");
                        string? trigger = metadata.HasValue ? GetString(metadata.Value, "trigger") : null;
                        int? tokens = metadata.HasValue ? GetInt(metadata.Value, "pre_tokens") : null;
                        state.Notice("Conversation compacted" + (trigger != null ? " (" + trigger + ")" : "") + (tokens.HasValue ? " at " + tokens.Value + " tokens" : "") + ".");
                        break;
                    }

                case "api_retry":
                    // Field names beyond the subtype were not observed live; render whatever error text is present.
                    state.Notice("Retrying the API request: " + (GetString(root, "error") ?? GetRendered(root, "error") ?? "transient error") + ".", true);
                    break;

                default:
                    break;
            }
        }

        private static void ParseStreamEvent(JsonElement evt, CliRunState state)
        {
            switch (GetString(evt, "type"))
            {
                case "message_start":
                    {
                        JsonElement? message = GetObject(evt, "message");
                        state.Properties[_CurrentMessageKey] = message.HasValue ? GetString(message.Value, "id") : null;
                        break;
                    }

                case "content_block_delta":
                    {
                        JsonElement? delta = GetObject(evt, "delta");
                        if (!delta.HasValue) break;
                        string messageId = state.Properties.TryGetValue(_CurrentMessageKey, out object? id) ? id as string ?? "" : "";
                        switch (GetString(delta.Value, "type"))
                        {
                            case "text_delta":
                                state.Properties[_TextSeenPrefix + messageId] = true;
                                state.Text(GetString(delta.Value, "text"));
                                break;
                            case "thinking_delta":
                                state.Properties[_ThinkingSeenPrefix + messageId] = true;
                                state.Reasoning(GetString(delta.Value, "thinking"));
                                break;
                            default:
                                break;
                        }
                        break;
                    }

                default:
                    break;
            }
        }

        private static void ParseAssistant(JsonElement root, CliRunState state)
        {
            JsonElement? message = GetObject(root, "message");
            if (!message.HasValue) return;

            string? error = GetString(root, "error");
            if (error != null || GetBool(root, "is_api_error_message") == true)
            {
                state.Properties[_ApiErrorKey] = error;
                state.Notice(ContentText(message.Value) ?? error, true);
                return;
            }

            string messageId = GetString(message.Value, "id") ?? "";
            bool textSeen = state.Properties.ContainsKey(_TextSeenPrefix + messageId);
            bool thinkingSeen = state.Properties.ContainsKey(_ThinkingSeenPrefix + messageId);
            if (!message.Value.TryGetProperty("content", out JsonElement content) || content.ValueKind != JsonValueKind.Array) return;

            foreach (JsonElement block in content.EnumerateArray())
            {
                if (block.ValueKind != JsonValueKind.Object) continue;
                switch (GetString(block, "type"))
                {
                    case "text":
                        if (!textSeen) state.Text(GetString(block, "text"));
                        break;
                    case "thinking":
                        if (!thinkingSeen) state.Reasoning(GetString(block, "thinking"));
                        break;
                    case "tool_use":
                    case "server_tool_use":
                    case "mcp_tool_use":
                        ReportToolUse(GetString(block, "name"), GetString(block, "id"), GetRendered(block, "input"), state);
                        break;
                    default:
                        break;
                }
            }
        }

        private static void ParseUser(JsonElement root, CliRunState state)
        {
            JsonElement? message = GetObject(root, "message");
            if (!message.HasValue) return;
            if (!message.Value.TryGetProperty("content", out JsonElement content) || content.ValueKind != JsonValueKind.Array) return;

            foreach (JsonElement block in content.EnumerateArray())
            {
                if (block.ValueKind != JsonValueKind.Object || GetString(block, "type") != "tool_result") continue;
                string? id = GetString(block, "tool_use_id");
                string? name = id != null && state.Properties.TryGetValue(_ToolPrefix + id, out object? known) ? known as string : null;
                if (id == null || !state.Properties.ContainsKey(_ToolPrefix + id)) ReportToolUse(null, id, null, state);
                state.ToolResult(name, id, ToolResultText(block), GetBool(block, "is_error") == true);
            }
        }

        private static void ParseResult(JsonElement root, string? subtype, CliRunState state)
        {
            string? sessionId = GetString(root, "session_id");
            if (!string.IsNullOrEmpty(sessionId)) state.SessionId = sessionId;

            JsonElement? usage = GetObject(root, "usage");
            if (usage.HasValue) state.Usage = ParseUsage(usage.Value);
            decimal? cost = GetDecimal(root, "total_cost_usd");
            if (cost.HasValue) state.CostUsd = cost;

            if (root.TryGetProperty("permission_denials", out JsonElement denials) && denials.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement denial in denials.EnumerateArray())
                {
                    if (denial.ValueKind != JsonValueKind.Object) continue;
                    state.Notice("Permission denied: " + (GetString(denial, "tool_name") ?? "tool") + " " + (GetRendered(denial, "tool_input") ?? ""), true);
                }
            }

            bool isError = GetBool(root, "is_error") == true || (subtype != null && subtype != "success");
            if (!isError)
            {
                string? result = GetString(root, "result");
                if (result != null) state.FinalText = result;
                state.Complete();
                return;
            }

            string? message = GetString(root, "result");
            if (string.IsNullOrWhiteSpace(message) && root.TryGetProperty("errors", out JsonElement errors) && errors.ValueKind == JsonValueKind.Array)
            {
                List<string> parts = new List<string>();
                foreach (JsonElement entry in errors.EnumerateArray())
                {
                    if (entry.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(entry.GetString())) parts.Add(entry.GetString()!);
                }
                if (parts.Count > 0) message = string.Join("; ", parts);
            }
            if (string.IsNullOrWhiteSpace(message)) message = "Claude Code reported " + (subtype ?? "an error") + ".";

            string? apiError = state.Properties.TryGetValue(_ApiErrorKey, out object? code) ? code as string : null;
            int? status = GetInt(root, "api_error_status");
            List<string> details = new List<string>();
            if (!string.IsNullOrEmpty(apiError)) details.Add(apiError!);
            if (status.HasValue) details.Add("status " + status.Value);
            if (subtype != null && subtype != "success") details.Add(subtype);
            if (details.Count > 0) message += " (" + string.Join(", ", details) + ")";

            state.Fail(message, ClassifyResultError(subtype, apiError, status));
        }

        /// <summary>
        /// Kinds from the structured fields: the documented assistant error codes and HTTP status. Null leaves the
        /// message-based classification (then ProviderError) to the base.
        /// </summary>
        private static CliFailureKind? ClassifyResultError(string? subtype, string? apiError, int? status)
        {
            if (subtype == "error_max_turns") return CliFailureKind.TurnLimitReached;
            if (apiError == "authentication_failed" || apiError == "oauth_org_not_allowed" || status == 401 || status == 403) return CliFailureKind.NotAuthenticated;
            if (apiError == "rate_limit" || status == 429) return CliFailureKind.RateLimited;
            return null;
        }

        private static void ReportToolUse(string? name, string? id, string? input, CliRunState state)
        {
            if (id != null) state.Properties[_ToolPrefix + id] = name;
            state.ToolUse(name, id, input);
        }

        private static string? ContentText(JsonElement message)
        {
            if (!message.TryGetProperty("content", out JsonElement content) || content.ValueKind != JsonValueKind.Array) return null;
            StringBuilder sb = new StringBuilder();
            foreach (JsonElement block in content.EnumerateArray())
            {
                if (block.ValueKind == JsonValueKind.Object && GetString(block, "type") == "text") sb.Append(GetString(block, "text"));
            }
            return sb.Length == 0 ? null : sb.ToString();
        }

        private static string? ToolResultText(JsonElement block)
        {
            if (!block.TryGetProperty("content", out JsonElement content)) return null;
            if (content.ValueKind == JsonValueKind.String) return content.GetString();
            if (content.ValueKind != JsonValueKind.Array) return RenderJson(content);

            StringBuilder sb = new StringBuilder();
            foreach (JsonElement part in content.EnumerateArray())
            {
                if (part.ValueKind == JsonValueKind.Object && GetString(part, "type") == "text")
                {
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(GetString(part, "text"));
                }
            }
            return sb.Length > 0 ? sb.ToString() : RenderJson(content);
        }

        private static TokenUsage ParseUsage(JsonElement usage)
        {
            JsonElement? details = GetObject(usage, "output_tokens_details");
            return new TokenUsage
            {
                PromptTokens = GetInt(usage, "input_tokens"),
                CachedPromptTokens = GetInt(usage, "cache_read_input_tokens"),
                CacheCreationTokens = GetInt(usage, "cache_creation_input_tokens"),
                CompletionTokens = GetInt(usage, "output_tokens"),
                ReasoningTokens = details.HasValue ? GetInt(details.Value, "thinking_tokens") : null,
            };
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
