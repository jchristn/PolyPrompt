namespace PolyPrompt.Clients
{
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Text.Json;
    using PolyPrompt.Models;
    using SyslogLogging;

    /// <summary>
    /// Agent harness client for the OpenAI Codex CLI (<c>codex</c>). Each run is <c>codex exec --json</c> with the prompt
    /// on standard input; a session is resumed with <c>codex exec resume &lt;id&gt;</c>. Authentication is the CLI's own
    /// (<c>codex login</c>, or <c>OPENAI_API_KEY</c>/<c>CODEX_API_KEY</c> in the environment).
    /// <para>
    /// Permission mapping: <see cref="CliPermissionMode.Restricted"/> and <see cref="CliPermissionMode.ReadOnly"/> use
    /// <c>-c sandbox_mode="read-only"</c>; <see cref="CliPermissionMode.WorkspaceWrite"/> uses
    /// <c>-c sandbox_mode="workspace-write"</c>; all three add <c>-c approval_policy="never"</c> so nothing waits for an
    /// approval nobody can give. <see cref="CliPermissionMode.Unrestricted"/> uses
    /// <c>--dangerously-bypass-approvals-and-sandbox</c>. The read-only modes also pass <c>--skip-git-repo-check</c>, since
    /// nothing can be modified; the writing modes keep Codex's requirement to run inside a Git repository.
    /// </para>
    /// </summary>
    public class CodexAgentClient : CliAgentClientBase
    {
        #region Public-Members

        /// <summary>
        /// Default executable name.
        /// </summary>
        public const string DefaultExecutable = "codex";

        /// <summary>
        /// Codex resumes sessions with <c>codex exec resume</c>.
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
            get { return "Codex"; }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a Codex client.
        /// </summary>
        /// <param name="executable">Executable name or path. Default: <c>codex</c>.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        public CodexAgentClient(string executable = DefaultExecutable, LoggingModule? logging = null)
            : base(executable, logging ?? new LoggingModule())
        {
            _Header = "[Codex] ";
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// List the models in the Codex model catalog (<c>codex debug models</c>). <see cref="ModelInformation.Name"/> is the
        /// slug to pass as the model; the catalog's visibility, default reasoning level, and supported reasoning levels are
        /// in <see cref="ModelInformation.Metadata"/>.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The models.</returns>
        /// <exception cref="CliAgentException">Thrown when the CLI is missing or the command fails.</exception>
        public IAsyncEnumerable<ModelInformation> ListModelsAsync(CancellationToken token = default)
        {
            return ListModelsFromCommandAsync(new[] { "debug", "models" }, ParseModels, token);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Build <c>codex exec [resume &lt;id&gt;] --json ... -</c>; the prompt travels on standard input.
        /// </summary>
        /// <param name="state">Run state.</param>
        /// <returns>The invocation.</returns>
        protected override CliInvocation BuildInvocation(CliRunState state)
        {
            List<string> args = new List<string> { "exec" };
            if (!string.IsNullOrEmpty(state.ResumeSessionId)) args.Add("resume");
            args.Add("--json");

            switch (state.PermissionMode)
            {
                case CliPermissionMode.Unrestricted:
                    args.Add("--dangerously-bypass-approvals-and-sandbox");
                    break;
                case CliPermissionMode.WorkspaceWrite:
                    args.Add("-c");
                    args.Add("sandbox_mode=\"workspace-write\"");
                    args.Add("-c");
                    args.Add("approval_policy=\"never\"");
                    break;
                default:
                    args.Add("-c");
                    args.Add("sandbox_mode=\"read-only\"");
                    args.Add("-c");
                    args.Add("approval_policy=\"never\"");
                    args.Add("--skip-git-repo-check");
                    break;
            }

            if (!string.IsNullOrWhiteSpace(state.Model))
            {
                args.Add("-m");
                args.Add(state.Model!);
            }

            if (!string.IsNullOrEmpty(state.ResumeSessionId)) args.Add(state.ResumeSessionId!);
            args.Add("-");

            return new CliInvocation { Arguments = args, StandardInput = state.PromptWithSystem };
        }

        /// <summary>
        /// Map one <c>codex exec --json</c> line.
        /// <code>
        /// thread.started {thread_id}                    -> Started (session id)
        /// turn.started                                  -> (none)
        /// item.started  command_execution|mcp_tool_call|web_search|file_change -> ToolUse
        /// item.completed agent_message                  -> TextDelta (the last message is the final text)
        /// item.completed reasoning                      -> ReasoningDelta
        /// item.completed command_execution|mcp_tool_call|web_search|file_change -> ToolResult (ToolUse first if unseen)
        /// item.completed todo_list                      -> Notice
        /// item.completed error                          -> Notice (non-fatal, IsError)
        /// item.updated                                  -> (none)
        /// turn.completed {usage}                        -> usage; Completed is emitted by the base at exit
        /// turn.failed {error.message}                   -> failure; Failed is emitted by the base at exit
        /// error {message} "Reconnecting..."             -> Notice (IsError)
        /// error {message} otherwise                     -> failure
        /// </code>
        /// Usage: input_tokens -> PromptTokens, cached_input_tokens -> CachedPromptTokens, cache_write_input_tokens ->
        /// CacheCreationTokens, output_tokens -> CompletionTokens, reasoning_output_tokens -> ReasoningTokens. Codex reports
        /// no total and no cost, so TotalTokens and CostUsd stay null.
        /// </summary>
        /// <param name="line">Line.</param>
        /// <param name="state">Run state.</param>
        protected override void ParseLine(string line, CliRunState state)
        {
            JsonElement root = ParseJsonObject(line);
            string? type = GetString(root, "type");
            state.CurrentEventType = type;

            switch (type)
            {
                case "thread.started":
                    state.Started(GetString(root, "thread_id"));
                    break;

                case "item.started":
                    {
                        JsonElement? item = GetObject(root, "item");
                        if (item.HasValue && IsToolItem(GetString(item.Value, "type"))) ReportToolUse(item.Value, state);
                        break;
                    }

                case "item.completed":
                    {
                        JsonElement? item = GetObject(root, "item");
                        if (item.HasValue) ParseCompletedItem(item.Value, state);
                        break;
                    }

                case "turn.completed":
                    {
                        JsonElement? usage = GetObject(root, "usage");
                        if (usage.HasValue) state.Usage = ParseUsage(usage.Value);
                        state.Complete();
                        break;
                    }

                case "turn.failed":
                    {
                        JsonElement? error = GetObject(root, "error");
                        state.Fail(DescribeError(error.HasValue ? GetString(error.Value, "message") : null));
                        break;
                    }

                case "error":
                    {
                        string? message = GetString(root, "message");
                        if (message != null && message.StartsWith("Reconnecting", StringComparison.OrdinalIgnoreCase))
                            state.Notice(message, true);
                        else
                            state.Fail(DescribeError(message));
                        break;
                    }

                default:
                    break;
            }
        }

        /// <summary>
        /// Check the login with <c>codex login status</c> (exit code zero when logged in).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The check.</returns>
        protected override async Task<CliAuthenticationCheck?> CheckAuthenticationAsync(CancellationToken token)
        {
            CliCommandResult status = await RunCommandAsync(new[] { "login", "status" }, Math.Min(TimeoutMs, 30000), token).ConfigureAwait(false);
            string message = FirstNonEmptyLine(status.StandardOutput + "\n" + status.StandardError) ?? "";
            return new CliAuthenticationCheck { Authenticated = status.Succeeded, Message = message };
        }

        #endregion

        #region Private-Methods

        private static bool IsToolItem(string? itemType)
        {
            return itemType == "command_execution" || itemType == "mcp_tool_call" || itemType == "web_search" || itemType == "file_change";
        }

        private static void ParseCompletedItem(JsonElement item, CliRunState state)
        {
            string? itemType = GetString(item, "type");
            switch (itemType)
            {
                case "agent_message":
                    {
                        string? text = GetString(item, "text");
                        if (!string.IsNullOrEmpty(text))
                        {
                            state.Text(text);
                            state.FinalText = text;
                        }
                        break;
                    }

                case "reasoning":
                    state.Reasoning(GetString(item, "text"));
                    break;

                case "todo_list":
                    state.Notice(RenderTodoList(item));
                    break;

                case "error":
                    state.Notice(GetString(item, "message"), true);
                    break;

                default:
                    if (IsToolItem(itemType))
                    {
                        string id = GetString(item, "id") ?? "";
                        if (!state.Properties.ContainsKey("tool:" + id)) ReportToolUse(item, state);
                        string? status = GetString(item, "status");
                        int? exitCode = GetInt(item, "exit_code");
                        bool isError = status == "failed" || status == "declined" || (exitCode.HasValue && exitCode.Value != 0) || GetObject(item, "error").HasValue;
                        state.ToolResult(ToolName(item), GetString(item, "id"), ToolOutput(item), isError);
                    }
                    break;
            }
        }

        private static void ReportToolUse(JsonElement item, CliRunState state)
        {
            string id = GetString(item, "id") ?? "";
            state.Properties["tool:" + id] = true;
            state.ToolUse(ToolName(item), GetString(item, "id"), ToolInput(item));
        }

        private static string ToolName(JsonElement item)
        {
            string itemType = GetString(item, "type") ?? "tool";
            if (itemType == "mcp_tool_call")
            {
                string? server = GetString(item, "server");
                string? tool = GetString(item, "tool");
                if (!string.IsNullOrEmpty(tool)) return string.IsNullOrEmpty(server) ? tool! : server + "." + tool;
            }
            return itemType;
        }

        private static string? ToolInput(JsonElement item)
        {
            switch (GetString(item, "type"))
            {
                case "command_execution": return GetString(item, "command");
                case "web_search": return GetString(item, "query");
                case "mcp_tool_call": return GetRendered(item, "arguments");
                case "file_change": return GetRendered(item, "changes");
                default: return null;
            }
        }

        private static string? ToolOutput(JsonElement item)
        {
            switch (GetString(item, "type"))
            {
                case "command_execution": return GetString(item, "aggregated_output");
                case "mcp_tool_call":
                    {
                        JsonElement? error = GetObject(item, "error");
                        if (error.HasValue) return GetString(error.Value, "message") ?? RenderJson(error);
                        return GetRendered(item, "result");
                    }
                case "file_change": return GetRendered(item, "changes");
                default: return null;
            }
        }

        private static string RenderTodoList(JsonElement item)
        {
            StringBuilder sb = new StringBuilder();
            if (item.TryGetProperty("items", out JsonElement items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement entry in items.EnumerateArray())
                {
                    bool done = GetBool(entry, "completed") == true;
                    sb.Append(done ? "[x] " : "[ ] ").Append(GetString(entry, "text") ?? "").Append('\n');
                }
            }
            return sb.ToString().TrimEnd('\n');
        }

        private static TokenUsage ParseUsage(JsonElement usage)
        {
            return new TokenUsage
            {
                PromptTokens = GetInt(usage, "input_tokens"),
                CachedPromptTokens = GetInt(usage, "cached_input_tokens"),
                CacheCreationTokens = GetInt(usage, "cache_write_input_tokens"),
                CompletionTokens = GetInt(usage, "output_tokens"),
                ReasoningTokens = GetInt(usage, "reasoning_output_tokens"),
            };
        }

        /// <summary>
        /// Codex reports upstream API errors as a JSON document inside the message; surface its message and code.
        /// </summary>
        private static string? DescribeError(string? message)
        {
            if (string.IsNullOrWhiteSpace(message)) return message;
            string trimmed = message.Trim();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal)) return message;

            try
            {
                JsonElement root = ParseJsonObject(trimmed);
                JsonElement? error = GetObject(root, "error");
                JsonElement source = error ?? root;
                string? inner = GetString(source, "message");
                if (string.IsNullOrEmpty(inner)) return message;

                List<string> details = new List<string>();
                string? code = GetString(source, "code") ?? GetString(source, "type");
                if (!string.IsNullOrEmpty(code)) details.Add(code!);
                int? status = GetInt(root, "status");
                if (status.HasValue) details.Add("status " + status.Value);
                return details.Count > 0 ? inner + " (" + string.Join(", ", details) + ")" : inner;
            }
            catch (JsonException)
            {
                return message;
            }
        }

        private static List<ModelInformation> ParseModels(string output)
        {
            JsonElement root = ParseJsonObject(output);
            List<ModelInformation> models = new List<ModelInformation>();
            if (!root.TryGetProperty("models", out JsonElement list) || list.ValueKind != JsonValueKind.Array)
                throw new JsonException("Expected a 'models' array.");

            foreach (JsonElement entry in list.EnumerateArray())
            {
                string? slug = GetString(entry, "slug");
                if (string.IsNullOrEmpty(slug)) continue;

                ModelInformation model = new ModelInformation
                {
                    Name = slug!,
                    DisplayName = GetString(entry, "display_name"),
                    Description = GetString(entry, "description"),
                    OwnedBy = "openai",
                };

                string? visibility = GetString(entry, "visibility");
                if (visibility != null) model.Metadata["visibility"] = visibility;
                string? reasoning = GetString(entry, "default_reasoning_level");
                if (reasoning != null) model.Metadata["default_reasoning_level"] = reasoning;
                if (entry.TryGetProperty("supported_reasoning_levels", out JsonElement levels) && levels.ValueKind == JsonValueKind.Array)
                {
                    List<string> efforts = new List<string>();
                    foreach (JsonElement level in levels.EnumerateArray())
                    {
                        string? effort = GetString(level, "effort");
                        if (effort != null) efforts.Add(effort);
                    }
                    if (efforts.Count > 0) model.Metadata["supported_reasoning_levels"] = string.Join(",", efforts);
                }

                models.Add(model);
            }

            return models;
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
