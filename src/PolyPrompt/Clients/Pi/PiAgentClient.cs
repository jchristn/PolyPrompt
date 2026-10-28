namespace PolyPrompt.Clients
{
    using System.Text;
    using System.Text.Encodings.Web;
    using System.Text.Json;
    using PolyPrompt.Helpers;
    using PolyPrompt.Models;
    using SyslogLogging;

    /// <summary>
    /// Agent harness client for the Pi coding agent (<c>pi</c>), driven in RPC mode (<c>pi --mode rpc</c>): JSON commands
    /// on standard input, JSON responses and session events on standard output. Each run writes a <c>get_state</c>
    /// command (which reports the session id) and a <c>prompt</c> command, reads events until <c>agent_settled</c>, then
    /// closes standard input, which Pi treats as an orderly shutdown. A session is resumed with <c>--session &lt;id&gt;</c>.
    /// Authentication is the CLI's own (<c>/login</c> in Pi, <c>~/.pi/agent/auth.json</c>, or provider API key
    /// environment variables such as <c>ANTHROPIC_API_KEY</c>).
    /// <para>
    /// Permission mapping. Pi has no sandbox and no approval prompts: an enabled tool runs with the permissions of the
    /// process, so modes are mapped to tool allowlists (<c>--tools</c>). <see cref="CliPermissionMode.Restricted"/> and
    /// <see cref="CliPermissionMode.ReadOnly"/> use <c>--tools read,grep,find,ls</c> (Pi's documented read-only set);
    /// <see cref="CliPermissionMode.WorkspaceWrite"/> uses <c>--tools read,grep,find,ls,edit,write</c>. Both leave out
    /// <c>bash</c>, because Pi cannot confine a shell command to the workspace or to reads. Pi does not confine
    /// <c>edit</c>/<c>write</c> to the working directory either, so WorkspaceWrite is "file edits, no shell", not a path
    /// sandbox. All three also pass <c>--no-mcp</c> (an allowlist alone keeps MCP tools) and <c>--no-approve</c> (project-local
    /// <c>.pi</c> settings, extensions, and other trust-gated resources are not loaded).
    /// <see cref="CliPermissionMode.Unrestricted"/> passes none of these, so Pi's configured default tools
    /// (<c>read</c>, <c>bash</c>, <c>edit</c>, <c>write</c> unless changed), MCP servers, and project-trust decision apply.
    /// </para>
    /// <para>
    /// System instructions are sent ahead of the prompt in the <c>prompt</c> command rather than with
    /// <c>--append-system-prompt</c>, because Pi reads that option's value as a file whenever it names an existing path.
    /// </para>
    /// </summary>
    public class PiAgentClient : CliAgentClientBase
    {
        #region Public-Members

        /// <summary>
        /// Default executable name.
        /// </summary>
        public const string DefaultExecutable = "pi";

        /// <summary>
        /// Pi resumes sessions with <c>--session &lt;id&gt;</c>.
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
            get { return "Pi"; }
        }

        #endregion

        #region Private-Members

        private const string _StateRequestId = "polyprompt-state";
        private const string _PromptRequestId = "polyprompt-prompt";
        private const string _ReadOnlyTools = "read,grep,find,ls";
        private const string _WorkspaceWriteTools = "read,grep,find,ls,edit,write";

        private const string _PendingErrorKey = "pi:pending-error";
        private const string _UsageKey = "pi:usage";
        private const string _CostKey = "pi:cost";

        private static readonly JsonWriterOptions _WriterOptions = new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Indented = false,
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a Pi client.
        /// </summary>
        /// <param name="executable">Executable name or path. Default: <c>pi</c>.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        public PiAgentClient(string executable = DefaultExecutable, LoggingModule? logging = null)
            : base(executable, logging ?? new LoggingModule())
        {
            _Header = "[Pi] ";
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// List the models Pi can use with the credentials it has (<c>pi --list-models</c>). <see cref="ModelInformation.Name"/>
        /// is <c>provider/model</c>, the form <c>--model</c> accepts; <see cref="ModelInformation.OwnedBy"/> is the provider.
        /// The context window and maximum output are in <see cref="ModelInformation.Metadata"/> (<c>context</c>,
        /// <c>max_output</c>) exactly as Pi prints them (for example <c>200K</c>), along with <c>thinking</c> and
        /// <c>images</c>. An empty list means Pi has no usable provider credentials or no model matched.
        /// </summary>
        /// <param name="search">Optional fuzzy search passed to <c>--list-models</c>.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The models.</returns>
        /// <exception cref="CliAgentException">Thrown when the CLI is missing or the command fails.</exception>
        public IAsyncEnumerable<ModelInformation> ListModelsAsync(string? search = null, CancellationToken token = default)
        {
            List<string> args = new List<string> { "--list-models" };
            if (!string.IsNullOrWhiteSpace(search)) args.Add(search!);
            return ListModelsFromCommandAsync(args, ParseModels, token);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Build <c>pi --mode rpc [--session &lt;id&gt;] [--model &lt;model&gt;] [permission options]</c>. Standard input
        /// carries two JSON lines (<c>get_state</c>, then <c>prompt</c>) and stays open until the run settles.
        /// </summary>
        /// <param name="state">Run state.</param>
        /// <returns>The invocation.</returns>
        protected override CliInvocation BuildInvocation(CliRunState state)
        {
            List<string> args = new List<string> { "--mode", "rpc" };

            if (!string.IsNullOrEmpty(state.ResumeSessionId))
            {
                args.Add("--session");
                args.Add(state.ResumeSessionId!);
            }

            if (!string.IsNullOrWhiteSpace(state.Model))
            {
                args.Add("--model");
                args.Add(state.Model!);
            }

            switch (state.PermissionMode)
            {
                case CliPermissionMode.Unrestricted:
                    break;
                case CliPermissionMode.WorkspaceWrite:
                    args.Add("--tools");
                    args.Add(_WorkspaceWriteTools);
                    args.Add("--no-mcp");
                    args.Add("--no-approve");
                    break;
                default:
                    args.Add("--tools");
                    args.Add(_ReadOnlyTools);
                    args.Add("--no-mcp");
                    args.Add("--no-approve");
                    break;
            }

            string input = SerializeCommand(_StateRequestId, "get_state", null) + SerializeCommand(_PromptRequestId, "prompt", state.PromptWithSystem);
            return new CliInvocation { Arguments = args, StandardInput = input, KeepStandardInputOpen = true };
        }

        /// <summary>
        /// Map one <c>pi --mode rpc</c> line.
        /// <code>
        /// response get_state {data.sessionId}            -> Started (session id)
        /// response get_state success=false               -> Notice (IsError)
        /// response prompt success=true "handled"         -> completion (no run starts)
        /// response prompt success=true otherwise         -> (none)
        /// response * success=false {error}               -> failure (missing credentials are NotAuthenticated)
        /// message_update thinking_delta {delta}          -> ReasoningDelta
        /// message_update text_delta {delta}              -> TextDelta
        /// message_update other                           -> (none)
        /// message_end assistant                          -> usage, cost, model; its text is the final text;
        ///                                                   stopReason error|aborted is remembered as the run's error
        /// tool_execution_start                           -> ToolUse
        /// tool_execution_end                             -> ToolResult
        /// auto_retry_start {errorMessage}                -> Notice (IsError)
        /// auto_retry_end success=false {finalError}      -> remembered as the run's error
        /// compaction_start / compaction_end              -> Notice (IsError when compaction failed)
        /// extension_error {error}                        -> Notice (IsError)
        /// extension_ui_request notify                    -> Notice
        /// extension_ui_request select|confirm|input|editor -> Notice (IsError; nobody can answer it)
        /// agent_settled aborted=true                     -> failure
        /// agent_settled with a remembered error          -> failure
        /// agent_settled otherwise                        -> completion; Completed is emitted by the base at exit
        /// agent_start, agent_end, turn_*, message_start, tool_execution_update, queue_update, ... -> (none)
        /// </code>
        /// Usage: Pi reports usage on each assistant message, so the run's usage is the sum over its assistant messages of
        /// input -> PromptTokens, cacheRead -> CachedPromptTokens, cacheWrite -> CacheCreationTokens, output ->
        /// CompletionTokens, reasoning -> ReasoningTokens (included in output, as Pi reports it), totalTokens ->
        /// TotalTokens, and cost.total -> CostUsd. A field Pi does not report stays null.
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
                case "response":
                    ParseResponse(root, state);
                    break;

                case "message_update":
                    {
                        JsonElement? update = GetObject(root, "assistantMessageEvent");
                        if (!update.HasValue) break;
                        string? updateType = GetString(update.Value, "type");
                        if (updateType == "text_delta") state.Text(GetString(update.Value, "delta"));
                        else if (updateType == "thinking_delta") state.Reasoning(GetString(update.Value, "delta"));
                        break;
                    }

                case "message_end":
                    {
                        JsonElement? message = GetObject(root, "message");
                        if (message.HasValue && GetString(message.Value, "role") == "assistant") ParseAssistantMessage(message.Value, state);
                        break;
                    }

                case "tool_execution_start":
                    state.ToolUse(GetString(root, "toolName"), GetString(root, "toolCallId"), GetRendered(root, "args"));
                    break;

                case "tool_execution_end":
                    state.ToolResult(GetString(root, "toolName"), GetString(root, "toolCallId"), ToolOutput(root), GetBool(root, "isError") == true);
                    break;

                case "auto_retry_start":
                    {
                        string? error = GetString(root, "errorMessage");
                        int? attempt = GetInt(root, "attempt");
                        int? max = GetInt(root, "maxAttempts");
                        string prefix = attempt.HasValue && max.HasValue ? "Retrying (attempt " + attempt.Value + " of " + max.Value + ")" : "Retrying";
                        state.Notice(prefix + (string.IsNullOrEmpty(error) ? "" : " after: " + DescribeError(error)), true);
                        break;
                    }

                case "auto_retry_end":
                    if (GetBool(root, "success") == false)
                        state.Properties[_PendingErrorKey] = GetString(root, "finalError") ?? "Pi gave up retrying.";
                    break;

                case "compaction_start":
                    state.Notice("Compacting context (" + (GetString(root, "reason") ?? "unknown") + ").");
                    break;

                case "compaction_end":
                    {
                        string? error = GetString(root, "errorMessage");
                        if (!string.IsNullOrEmpty(error)) state.Notice("Compaction failed: " + error, true);
                        else if (GetBool(root, "aborted") == true) state.Notice("Compaction was aborted.", true);
                        else state.Notice("Context compacted.");
                        break;
                    }

                case "extension_error":
                    state.Notice("Extension " + (GetString(root, "extensionPath") ?? "") + " failed: " + (GetString(root, "error") ?? "unknown error"), true);
                    break;

                case "extension_ui_request":
                    ParseExtensionRequest(root, state);
                    break;

                case "agent_settled":
                    {
                        string? pending = state.Properties.TryGetValue(_PendingErrorKey, out object? value) ? value as string : null;
                        if (GetBool(root, "aborted") == true)
                            state.Fail(pending != null ? "Pi aborted the run: " + DescribeError(pending) : "Pi aborted the run.");
                        else if (pending != null)
                            Fail(state, DescribeError(pending));
                        else
                            state.Complete();
                        break;
                    }

                default:
                    break;
            }
        }

        /// <summary>
        /// Classify a failed run. Pi rejects an unknown model, an unknown session, or an unknown option on standard error
        /// before it starts; those are <see cref="CliFailureKind.InvalidInput"/>. Everything else uses the default.
        /// </summary>
        /// <param name="state">Run state.</param>
        /// <param name="process">Process outcome.</param>
        /// <returns>The failure kind.</returns>
        protected override CliFailureKind ClassifyFailure(CliRunState state, ProcessRunResult process)
        {
            if (state.ErrorMessage == null && IsRejectedInput(process.StandardError)) return CliFailureKind.InvalidInput;
            return base.ClassifyFailure(state, process);
        }

        /// <summary>
        /// Check the login. Pi's readiness check (<c>pi auth check --provider &lt;provider&gt; --json</c>) needs a provider,
        /// so the provider of the model a run would use is read first from <c>get_state</c> in a short RPC session that
        /// starts no run (<c>pi --mode rpc --no-session --no-tools --no-mcp --no-approve --offline [--model &lt;model&gt;]</c>).
        /// When Pi has no usable model at all it reports the provider <c>unknown</c>, which is reported as not authenticated.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The check.</returns>
        protected override async Task<CliAuthenticationCheck?> CheckAuthenticationAsync(CancellationToken token)
        {
            int timeoutMs = Math.Min(TimeoutMs, 30000);

            List<string> probeArgs = new List<string> { "--mode", "rpc", "--no-session", "--no-tools", "--no-mcp", "--no-approve", "--offline" };
            if (!string.IsNullOrWhiteSpace(Model))
            {
                probeArgs.Add("--model");
                probeArgs.Add(Model!);
            }

            CliCommandResult probe = await RunCommandAsync(probeArgs, timeoutMs, token, SerializeCommand(_StateRequestId, "get_state", null)).ConfigureAwait(false);
            string? provider = ProviderFromState(probe.StandardOutput);
            if (provider == null)
            {
                string detail = FirstNonEmptyLine(probe.StandardError) ?? FirstNonEmptyLine(probe.StandardOutput) ?? ("exit code " + probe.ExitCode);
                return new CliAuthenticationCheck { Authenticated = false, Message = "Pi did not report the model it would use: " + detail };
            }

            if (string.Equals(provider, "unknown", StringComparison.Ordinal))
                return new CliAuthenticationCheck { Authenticated = false, Message = "Pi has no usable model; log in with /login in pi or set a provider API key." };

            CliCommandResult status = await RunCommandAsync(new[] { "auth", "check", "--provider", provider, "--json" }, timeoutMs, token).ConfigureAwait(false);
            string message = DescribeAuthStatus(status.StandardOutput) ?? FirstNonEmptyLine(status.StandardError + "\n" + status.StandardOutput) ?? "";
            return new CliAuthenticationCheck { Authenticated = status.Succeeded, Message = message };
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Serialize one RPC command as a JSON line (Utf8JsonWriter, so it needs no reflection).
        /// </summary>
        private static string SerializeCommand(string id, string type, string? message)
        {
            using MemoryStream stream = new MemoryStream();
            using (Utf8JsonWriter writer = new Utf8JsonWriter(stream, _WriterOptions))
            {
                writer.WriteStartObject();
                writer.WriteString("id", id);
                writer.WriteString("type", type);
                if (message != null) writer.WriteString("message", message);
                writer.WriteEndObject();
            }
            return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
        }

        private static void ParseResponse(JsonElement root, CliRunState state)
        {
            string? command = GetString(root, "command");
            bool success = GetBool(root, "success") == true;
            string? error = GetString(root, "error");

            if (command == "get_state")
            {
                if (success)
                {
                    JsonElement? data = GetObject(root, "data");
                    state.Started(data.HasValue ? GetString(data.Value, "sessionId") : null);
                }
                else
                {
                    state.Notice("Pi did not report its state: " + (error ?? "unknown error"), true);
                }
                return;
            }

            if (!success)
            {
                Fail(state, error ?? ("Pi rejected the " + (command ?? "unknown") + " command."));
                return;
            }

            if (command == "prompt")
            {
                JsonElement? data = GetObject(root, "data");
                if (data.HasValue && GetString(data.Value, "disposition") == "handled") state.Complete();
            }
        }

        private static void Fail(CliRunState state, string message)
        {
            if (message.IndexOf("No API key found", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("Use /login", StringComparison.OrdinalIgnoreCase) >= 0)
                state.Fail(message, CliFailureKind.NotAuthenticated);
            else
                state.Fail(message);
        }

        private static void ParseAssistantMessage(JsonElement message, CliRunState state)
        {
            string? provider = GetString(message, "provider");
            string? model = GetString(message, "model");
            if (!string.IsNullOrEmpty(model)) state.ReportedModel = string.IsNullOrEmpty(provider) ? model : provider + "/" + model;

            JsonElement? usage = GetObject(message, "usage");
            if (usage.HasValue) AddUsage(usage.Value, state);

            string text = ContentText(message, "text", "text");
            if (text.Length > 0) state.FinalText = text;

            string? stopReason = GetString(message, "stopReason");
            if (stopReason == "error" || stopReason == "aborted")
                state.Properties[_PendingErrorKey] = GetString(message, "errorMessage") ?? ("The model stopped with reason '" + stopReason + "'.");
            else
                state.Properties.Remove(_PendingErrorKey);
        }

        private static void AddUsage(JsonElement usage, CliRunState state)
        {
            TokenUsage total = state.Properties.TryGetValue(_UsageKey, out object? existing) && existing is TokenUsage known ? known : new TokenUsage();
            total.PromptTokens = Add(total.PromptTokens, GetInt(usage, "input"));
            total.CompletionTokens = Add(total.CompletionTokens, GetInt(usage, "output"));
            total.CachedPromptTokens = Add(total.CachedPromptTokens, GetInt(usage, "cacheRead"));
            total.CacheCreationTokens = Add(total.CacheCreationTokens, GetInt(usage, "cacheWrite"));
            total.ReasoningTokens = Add(total.ReasoningTokens, GetInt(usage, "reasoning"));
            total.TotalTokens = Add(total.TotalTokens, GetInt(usage, "totalTokens"));
            state.Properties[_UsageKey] = total;
            state.Usage = total;

            JsonElement? cost = GetObject(usage, "cost");
            decimal? runCost = cost.HasValue ? GetDecimal(cost.Value, "total") : null;
            if (runCost.HasValue)
            {
                decimal sum = (state.Properties.TryGetValue(_CostKey, out object? prior) && prior is decimal previous ? previous : 0m) + runCost.Value;
                state.Properties[_CostKey] = sum;
                state.CostUsd = sum;
            }
        }

        private static int? Add(int? total, int? value)
        {
            if (!value.HasValue) return total;
            return (total ?? 0) + value.Value;
        }

        /// <summary>
        /// Concatenate the content blocks of one type (for example text blocks) of a message or tool result.
        /// </summary>
        private static string ContentText(JsonElement container, string blockType, string field)
        {
            if (!container.TryGetProperty("content", out JsonElement content)) return "";
            if (content.ValueKind == JsonValueKind.String) return blockType == "text" ? content.GetString() ?? "" : "";
            if (content.ValueKind != JsonValueKind.Array) return "";

            StringBuilder sb = new StringBuilder();
            foreach (JsonElement block in content.EnumerateArray())
            {
                if (GetString(block, "type") == blockType) sb.Append(GetString(block, field) ?? "");
            }
            return sb.ToString();
        }

        private static string? ToolOutput(JsonElement root)
        {
            if (!root.TryGetProperty("result", out JsonElement result)) return null;
            if (result.ValueKind == JsonValueKind.Object)
            {
                string text = ContentText(result, "text", "text");
                if (text.Length > 0) return text;
            }
            return RenderJson(result);
        }

        private static void ParseExtensionRequest(JsonElement root, CliRunState state)
        {
            string? method = GetString(root, "method");
            switch (method)
            {
                case "notify":
                    state.Notice(GetString(root, "message"), GetString(root, "notifyType") == "error");
                    break;
                case "select":
                case "confirm":
                case "input":
                case "editor":
                    {
                        string title = GetString(root, "title") ?? "";
                        int? timeout = GetInt(root, "timeout");
                        state.Notice(
                            "A Pi extension asked for input that PolyPrompt cannot answer (" + method + ": " + title + "); "
                            + (timeout.HasValue ? "Pi resolves it with a default after " + timeout.Value + " ms." : "the run waits until it times out."),
                            true);
                        break;
                    }
                default:
                    break;
            }
        }

        /// <summary>
        /// Pi reports provider errors as <c>&lt;status&gt; &lt;JSON body&gt;</c>; surface the body's message, type, and status.
        /// </summary>
        private static string DescribeError(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return message;
            string trimmed = message.Trim();
            int brace = trimmed.IndexOf('{');
            if (brace < 0) return message;

            string prefix = trimmed.Substring(0, brace).Trim();
            int status = 0;
            bool hasStatus = prefix.Length > 0 && int.TryParse(prefix, out status);
            if (prefix.Length > 0 && !hasStatus) return message;

            try
            {
                JsonElement root = ParseJsonObject(trimmed.Substring(brace));
                JsonElement? error = GetObject(root, "error");
                JsonElement source = error ?? root;
                string? inner = GetString(source, "message");
                if (string.IsNullOrEmpty(inner)) return message;

                List<string> details = new List<string>();
                string? code = GetString(source, "type") ?? GetString(source, "code");
                if (!string.IsNullOrEmpty(code)) details.Add(code!);
                if (hasStatus) details.Add("status " + status);
                return details.Count > 0 ? inner + " (" + string.Join(", ", details) + ")" : inner!;
            }
            catch (JsonException)
            {
                return message;
            }
        }

        private static bool IsRejectedInput(string? standardError)
        {
            if (string.IsNullOrEmpty(standardError)) return false;
            return standardError.IndexOf("No session found matching", StringComparison.Ordinal) >= 0
                || standardError.IndexOf("Unknown option:", StringComparison.Ordinal) >= 0
                || (standardError.IndexOf("Error: Model \"", StringComparison.Ordinal) >= 0 && standardError.IndexOf("\" not found", StringComparison.Ordinal) >= 0);
        }

        private static string? ProviderFromState(string output)
        {
            foreach (string line in output.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed[0] != '{') continue;
                try
                {
                    JsonElement root = ParseJsonObject(trimmed);
                    if (GetString(root, "type") != "response" || GetString(root, "command") != "get_state" || GetBool(root, "success") != true) continue;
                    JsonElement? data = GetObject(root, "data");
                    JsonElement? model = data.HasValue ? GetObject(data.Value, "model") : null;
                    if (!model.HasValue) return "unknown";
                    return GetString(model.Value, "provider") ?? "unknown";
                }
                catch (JsonException)
                {
                }
            }
            return null;
        }

        private static string? DescribeAuthStatus(string output)
        {
            string? line = FirstNonEmptyLine(output);
            if (line == null || !line.StartsWith("{", StringComparison.Ordinal)) return null;
            try
            {
                JsonElement root = ParseJsonObject(line);
                string? status = GetString(root, "status");
                if (status == null) return null;
                string? provider = GetString(root, "provider");
                string? reason = GetString(root, "reason");
                string? authType = GetString(root, "authType");
                StringBuilder sb = new StringBuilder();
                sb.Append(provider ?? "provider").Append(": ").Append(status);
                if (!string.IsNullOrEmpty(authType)) sb.Append(" (").Append(authType).Append(')');
                if (!string.IsNullOrEmpty(reason)) sb.Append(" (").Append(reason).Append(')');
                return sb.ToString();
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Parse the <c>pi --list-models</c> table: a header row (<c>provider model context max-out thinking images</c>)
        /// followed by one whitespace-separated row per model. Any other output (for example "No models available" or
        /// "No models matching") yields no models.
        /// </summary>
        private static List<ModelInformation> ParseModels(string output)
        {
            List<ModelInformation> models = new List<ModelInformation>();
            List<string>? columns = null;

            foreach (string rawLine in output.Split('\n'))
            {
                string[] cells = rawLine.Split(new[] { ' ', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                if (cells.Length == 0) continue;

                if (columns == null)
                {
                    if (cells.Length >= 2 && cells[0] == "provider" && cells[1] == "model") columns = new List<string>(cells);
                    continue;
                }

                if (cells.Length < 2) continue;
                ModelInformation model = new ModelInformation
                {
                    Name = cells[0] + "/" + cells[1],
                    DisplayName = cells[1],
                    OwnedBy = cells[0],
                };

                for (int i = 2; i < cells.Length && i < columns.Count; i++)
                {
                    model.Metadata[columns[i].Replace('-', '_')] = cells[i];
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
