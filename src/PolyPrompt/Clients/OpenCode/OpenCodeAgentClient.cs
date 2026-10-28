namespace PolyPrompt.Clients
{
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using PolyPrompt.Models;
    using SyslogLogging;

    /// <summary>
    /// Agent harness client for the OpenCode CLI (<c>opencode</c>). Each run is
    /// <c>opencode run --format json --standalone --thinking</c> with the prompt on standard input; a session is resumed with
    /// <c>--session &lt;id&gt;</c>. Authentication is the CLI's own (<c>opencode auth login</c>, provider API keys in the
    /// environment, or OpenCode's free models, which need no credentials).
    /// <para>
    /// Every run passes <c>--standalone</c> so OpenCode starts a private server as a child of the run instead of handing
    /// the turn to its shared background service: the per-run permission configuration below only reaches a server
    /// started with the run's environment (verified against OpenCode 2.2.0, whose background service ignores the
    /// client's <c>OPENCODE_CONFIG_CONTENT</c>), and cancelling or timing out the run kills the whole process tree that
    /// did the work.
    /// </para>
    /// <para>
    /// Permission mapping. OpenCode has no sandbox; its permissions are configuration rules (<c>allow</c>, <c>ask</c>,
    /// <c>deny</c>) per tool, and <c>opencode run</c> rejects every <c>ask</c> because nobody can answer it.
    /// <see cref="CliPermissionMode.Restricted"/> and <see cref="CliPermissionMode.ReadOnly"/> pin <c>--agent build</c>
    /// and set <c>OPENCODE_CONFIG_CONTENT</c> so that, at the top level and on the <c>build</c> agent (agent rules take
    /// precedence over top-level rules), everything is denied (<c>"*": "deny"</c>) except <c>read</c> (with
    /// <c>.env</c> files denied), <c>glob</c>, and <c>grep</c>. Shell commands stay denied in
    /// <see cref="CliPermissionMode.ReadOnly"/> because OpenCode cannot limit a command to reading.
    /// <see cref="CliPermissionMode.WorkspaceWrite"/> adds <c>edit</c> (which covers OpenCode's edit, write, and patch
    /// tools) and keeps <c>external_directory</c> denied, so edits outside the working directory are refused; the shell
    /// stays denied because it cannot be confined to the workspace. <see cref="CliPermissionMode.Unrestricted"/> passes
    /// <c>--auto</c> (approve everything not explicitly denied) and leaves OpenCode's configuration untouched. Any
    /// <c>OPENCODE_CONFIG_CONTENT</c> the caller already supplies (request, client, or process environment) is kept, with
    /// its <c>permission</c> and <c>agent.build.permission</c> replaced; inline configuration that is not a JSON object is
    /// replaced, since OpenCode silently ignores invalid inline configuration.
    /// </para>
    /// <para>
    /// OpenCode has no system-prompt option for <c>run</c>, so system instructions are prepended to the prompt. OpenCode
    /// takes its project directory from <c>PWD</c> rather than the process's working directory, so every run sets
    /// <c>PWD</c> to the run's working directory (or the current directory when none is set).
    /// </para>
    /// </summary>
    public class OpenCodeAgentClient : CliAgentClientBase
    {
        #region Public-Members

        /// <summary>
        /// Default executable name.
        /// </summary>
        public const string DefaultExecutable = "opencode";

        /// <summary>
        /// Environment variable OpenCode reads inline JSON configuration from.
        /// </summary>
        public const string ConfigContentVariable = "OPENCODE_CONFIG_CONTENT";

        /// <summary>
        /// OpenCode resumes sessions with <c>opencode run --session &lt;id&gt;</c>.
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
            get { return "OpenCode"; }
        }

        #endregion

        #region Private-Members

        private const string _AgentName = "build";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize an OpenCode client.
        /// </summary>
        /// <param name="executable">Executable name or path. Default: <c>opencode</c>.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        public OpenCodeAgentClient(string executable = DefaultExecutable, LoggingModule? logging = null)
            : base(executable, logging ?? new LoggingModule())
        {
            _Header = "[OpenCode] ";
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// List the models available across OpenCode's configured providers (<c>opencode models</c>, one
        /// <c>provider/model</c> per line). <see cref="ModelInformation.Name"/> is the full <c>provider/model</c> to pass as
        /// the model; <see cref="ModelInformation.OwnedBy"/> and the <c>provider</c> metadata entry hold the provider.
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
        /// Build <c>opencode run --format json --standalone --thinking ...</c>; the prompt travels on standard input
        /// (verified: <c>opencode run</c> reads the message from standard input when none is given as an argument).
        /// </summary>
        /// <param name="state">Run state.</param>
        /// <returns>The invocation.</returns>
        protected override CliInvocation BuildInvocation(CliRunState state)
        {
            List<string> args = new List<string> { "run", "--format", "json", "--standalone", "--thinking" };

            // OpenCode 2.2.0 takes its project directory from PWD rather than the process's working directory, and a
            // started process inherits the parent's PWD; point it at the directory the run actually starts in.
            Dictionary<string, string?> environment = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { "PWD", Path.GetFullPath(state.WorkingDirectory ?? Directory.GetCurrentDirectory()) },
            };
            CliInvocation invocation = new CliInvocation { StandardInput = state.PromptWithSystem, Environment = environment };

            if (state.PermissionMode == CliPermissionMode.Unrestricted)
            {
                args.Add("--auto");
            }
            else
            {
                args.Add("--agent");
                args.Add(_AgentName);
                environment[ConfigContentVariable] = BuildConfigContent(state.PermissionMode, CallerConfigContent(state.Request));
            }

            if (!string.IsNullOrWhiteSpace(state.Model))
            {
                args.Add("-m");
                args.Add(state.Model!);
            }

            if (!string.IsNullOrEmpty(state.ResumeSessionId))
            {
                args.Add("--session");
                args.Add(state.ResumeSessionId!);
            }

            invocation.Arguments = args;
            return invocation;
        }

        /// <summary>
        /// Map one <c>opencode run --format json</c> line. Every line carries the session id at the top level; the payload is
        /// in <c>part</c> (or <c>error</c>).
        /// <code>
        /// (first line with a sessionID)                 -> Started (session id)
        /// step_start                                    -> (none; opens a step for usage accounting)
        /// text {part.text}                              -> TextDelta (the text of the last message is the final text)
        /// reasoning {part.text}                         -> ReasoningDelta (only emitted with --thinking)
        /// tool_use {part.tool, part.id, part.state}     -> ToolUse, then ToolResult (OpenCode reports a tool once, when it
        ///                                                  has completed or failed; state.status "error" is IsError)
        /// step_finish {part.reason, tokens, cost}       -> Notice carrying that step's usage and cost
        /// error {error.type, message, status}           -> failure; Failed is emitted by the base at exit
        /// "ERROR" heading then a message (not JSON)     -> failure (Crashed); the CLI's usage errors are plain text
        /// </code>
        /// OpenCode reports no terminal success event: a run succeeds when the process exits with code zero.
        /// <para>
        /// Usage: OpenCode reports tokens and cost per step (one model call); a turn is the sum of its steps, which is how
        /// OpenCode itself totals a message. Each step's values are on its Notice exactly as reported. The run's
        /// <see cref="CliRunState.Usage"/> and <see cref="CliRunState.CostUsd"/> are the sums of the step values only when
        /// every step of the turn reported a step_finish; otherwise they stay null rather than under-count (OpenCode 2.2.0
        /// often exits before writing the final step's step_finish). tokens.input -> PromptTokens, tokens.output ->
        /// CompletionTokens, tokens.reasoning -> ReasoningTokens, tokens.cache.read -> CachedPromptTokens,
        /// tokens.cache.write -> CacheCreationTokens, tokens.total -> TotalTokens (null when not reported), cost ->
        /// CostUsd.
        /// </para>
        /// </summary>
        /// <param name="line">Line.</param>
        /// <param name="state">Run state.</param>
        protected override void ParseLine(string line, CliRunState state)
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                ParsePlainLine(trimmed, state);
                return;
            }

            JsonElement root = ParseJsonObject(line);
            string? type = GetString(root, "type");
            state.CurrentEventType = type;

            if (!state.Properties.ContainsKey("started"))
            {
                string? sessionId = GetString(root, "sessionID");
                if (!string.IsNullOrEmpty(sessionId))
                {
                    state.Properties["started"] = true;
                    state.Started(sessionId);
                }
            }

            JsonElement? part = GetObject(root, "part");
            switch (type)
            {
                case "step_start":
                    StepCounter(state, "steps:started", 1);
                    state.Usage = null;
                    state.CostUsd = null;
                    break;

                case "text":
                    if (part.HasValue)
                    {
                        NoteContent(state);
                        string? text = GetString(part.Value, "text");
                        state.Text(text);
                        TrackFinalText(part.Value, text, state);
                    }
                    break;

                case "reasoning":
                    if (part.HasValue)
                    {
                        NoteContent(state);
                        state.Reasoning(GetString(part.Value, "text"));
                    }
                    break;

                case "tool_use":
                    if (part.HasValue)
                    {
                        NoteContent(state);
                        ReportTool(part.Value, state);
                    }
                    break;

                case "step_finish":
                    if (part.HasValue) FinishStep(part.Value, state);
                    break;

                case "error":
                    {
                        JsonElement? error = GetObject(root, "error");
                        if (error.HasValue) state.Fail(DescribeError(error.Value), ErrorKind(error.Value));
                        else state.Fail(GetString(root, "message"));
                        break;
                    }

                default:
                    break;
            }
        }

        #endregion

        #region Private-Methods

        private static void ParsePlainLine(string trimmed, CliRunState state)
        {
            if (trimmed.Length == 0) return;

            if (trimmed == "ERROR")
            {
                state.Properties["plain:error"] = true;
                return;
            }

            if (state.Properties.ContainsKey("plain:error"))
            {
                state.Fail(trimmed, CliFailureKind.Crashed);
                return;
            }

            throw new JsonException("Expected a JSON event line.");
        }

        /// <summary>
        /// The caller's inline configuration, in the same precedence the base applies to the environment: the request's
        /// value (a null value removes it), else the client's, else the process environment's.
        /// </summary>
        private string? CallerConfigContent(CliAgentRequest request)
        {
            if (request.Environment != null && request.Environment.TryGetValue(ConfigContentVariable, out string? fromRequest))
                return fromRequest;
            if (Environment.TryGetValue(ConfigContentVariable, out string? fromClient)) return fromClient;
            return System.Environment.GetEnvironmentVariable(ConfigContentVariable);
        }

        /// <summary>
        /// Write the caller's inline configuration with <c>permission</c> and <c>agent.build.permission</c> replaced by the
        /// rules for <paramref name="mode"/>.
        /// </summary>
        private static string BuildConfigContent(CliPermissionMode mode, string? existing)
        {
            JsonElement? caller = null;
            if (!string.IsNullOrWhiteSpace(existing))
            {
                try
                {
                    using JsonDocument document = JsonDocument.Parse(existing!);
                    if (document.RootElement.ValueKind == JsonValueKind.Object) caller = document.RootElement.Clone();
                }
                catch (JsonException)
                {
                    caller = null;
                }
            }

            using MemoryStream stream = new MemoryStream();
            using (Utf8JsonWriter writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                JsonElement? agents = null;
                if (caller.HasValue)
                {
                    foreach (JsonProperty property in caller.Value.EnumerateObject())
                    {
                        if (property.NameEquals("permission")) continue;
                        if (property.NameEquals("agent"))
                        {
                            if (property.Value.ValueKind == JsonValueKind.Object) agents = property.Value;
                            continue;
                        }
                        property.WriteTo(writer);
                    }
                }

                writer.WritePropertyName("permission");
                WritePermissions(writer, mode);

                writer.WritePropertyName("agent");
                writer.WriteStartObject();
                JsonElement? buildAgent = null;
                if (agents.HasValue)
                {
                    foreach (JsonProperty agent in agents.Value.EnumerateObject())
                    {
                        if (agent.NameEquals(_AgentName))
                        {
                            if (agent.Value.ValueKind == JsonValueKind.Object) buildAgent = agent.Value;
                            continue;
                        }
                        agent.WriteTo(writer);
                    }
                }

                writer.WritePropertyName(_AgentName);
                writer.WriteStartObject();
                if (buildAgent.HasValue)
                {
                    foreach (JsonProperty property in buildAgent.Value.EnumerateObject())
                    {
                        if (property.NameEquals("permission")) continue;
                        property.WriteTo(writer);
                    }
                }
                writer.WritePropertyName("permission");
                WritePermissions(writer, mode);
                writer.WriteEndObject();

                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        /// <summary>
        /// Rules are evaluated last match wins, so the catch-all deny comes first.
        /// </summary>
        private static void WritePermissions(Utf8JsonWriter writer, CliPermissionMode mode)
        {
            writer.WriteStartObject();
            writer.WriteString("*", "deny");
            writer.WritePropertyName("read");
            writer.WriteStartObject();
            writer.WriteString("*", "allow");
            writer.WriteString("*.env", "deny");
            writer.WriteString("*.env.*", "deny");
            writer.WriteString("*.env.example", "allow");
            writer.WriteEndObject();
            writer.WriteString("glob", "allow");
            writer.WriteString("grep", "allow");
            if (mode == CliPermissionMode.WorkspaceWrite) writer.WriteString("edit", "allow");
            writer.WriteString("external_directory", "deny");
            writer.WriteEndObject();
        }

        private static void StepCounter(CliRunState state, string key, int delta)
        {
            int current = state.Properties.TryGetValue(key, out object? value) && value is int count ? count : 0;
            state.Properties[key] = current + delta;
        }

        private static int Steps(CliRunState state, string key)
        {
            return state.Properties.TryGetValue(key, out object? value) && value is int count ? count : 0;
        }

        /// <summary>
        /// Content outside any reported step means a step whose usage OpenCode will not report.
        /// </summary>
        private static void NoteContent(CliRunState state)
        {
            if (Steps(state, "steps:started") <= Steps(state, "steps:finished"))
            {
                state.Properties["steps:unreported"] = true;
                state.Usage = null;
                state.CostUsd = null;
            }
        }

        private static void TrackFinalText(JsonElement part, string? text, CliRunState state)
        {
            if (string.IsNullOrEmpty(text)) return;
            string messageId = GetString(part, "messageID") ?? "";
            string previous = state.Properties.TryGetValue("final:message", out object? id) ? id as string ?? "" : "";
            string current = state.Properties.TryGetValue("final:text", out object? value) ? value as string ?? "" : "";
            if (!state.Properties.ContainsKey("final:message") || previous != messageId) current = "";
            current += text;
            state.Properties["final:message"] = messageId;
            state.Properties["final:text"] = current;
            state.FinalText = current;
        }

        private static void ReportTool(JsonElement part, CliRunState state)
        {
            string toolName = GetString(part, "tool") ?? "tool";
            string? callId = GetString(part, "id") ?? GetString(part, "partID");
            JsonElement? toolState = GetObject(part, "state");

            string? input = null;
            string? output = null;
            bool isError = false;
            if (toolState.HasValue)
            {
                input = GetRendered(toolState.Value, "input");
                string? status = GetString(toolState.Value, "status");
                isError = status == "error";
                if (isError)
                {
                    JsonElement? error = GetObject(toolState.Value, "error");
                    output = error.HasValue ? GetString(error.Value, "message") ?? RenderJson(error) : GetString(toolState.Value, "error");
                }
                else
                {
                    output = GetString(toolState.Value, "output");
                }
            }

            state.ToolUse(toolName, callId, input);
            state.ToolResult(toolName, callId, output, isError);
        }

        private static void FinishStep(JsonElement part, CliRunState state)
        {
            StepCounter(state, "steps:finished", 1);

            TokenUsage? step = null;
            JsonElement? tokens = GetObject(part, "tokens");
            if (tokens.HasValue)
            {
                JsonElement? cache = GetObject(tokens.Value, "cache");
                step = new TokenUsage
                {
                    PromptTokens = GetInt(tokens.Value, "input"),
                    CompletionTokens = GetInt(tokens.Value, "output"),
                    ReasoningTokens = GetInt(tokens.Value, "reasoning"),
                    CachedPromptTokens = cache.HasValue ? GetInt(cache.Value, "read") : null,
                    CacheCreationTokens = cache.HasValue ? GetInt(cache.Value, "write") : null,
                    TotalTokens = GetInt(tokens.Value, "total"),
                };
            }
            decimal? cost = GetDecimal(part, "cost");

            string reason = GetString(part, "reason") ?? "unknown";
            state.Emit(new CliAgentEvent(CliAgentEventKind.Notice, "Step finished (" + reason + ").") { Usage = step, CostUsd = cost });

            if (step == null) state.Properties["usage:missing"] = true;
            if (!cost.HasValue) state.Properties["cost:missing"] = true;

            TokenUsage? total = state.Properties.TryGetValue("usage:sum", out object? sum) ? sum as TokenUsage : null;
            if (step != null) total = Add(total, step);
            state.Properties["usage:sum"] = total;

            decimal? costTotal = state.Properties.TryGetValue("cost:sum", out object? c) ? c as decimal? : null;
            if (cost.HasValue) costTotal = (costTotal ?? 0m) + cost.Value;
            state.Properties["cost:sum"] = costTotal;

            bool complete = !state.Properties.ContainsKey("steps:unreported") && Steps(state, "steps:finished") >= Steps(state, "steps:started");
            state.Usage = complete && !state.Properties.ContainsKey("usage:missing") ? total : null;
            state.CostUsd = complete && !state.Properties.ContainsKey("cost:missing") ? costTotal : null;
        }

        private static TokenUsage Add(TokenUsage? a, TokenUsage b)
        {
            if (a == null)
            {
                return new TokenUsage
                {
                    PromptTokens = b.PromptTokens,
                    CompletionTokens = b.CompletionTokens,
                    ReasoningTokens = b.ReasoningTokens,
                    CachedPromptTokens = b.CachedPromptTokens,
                    CacheCreationTokens = b.CacheCreationTokens,
                    TotalTokens = b.TotalTokens,
                };
            }

            return new TokenUsage
            {
                PromptTokens = Sum(a.PromptTokens, b.PromptTokens),
                CompletionTokens = Sum(a.CompletionTokens, b.CompletionTokens),
                ReasoningTokens = Sum(a.ReasoningTokens, b.ReasoningTokens),
                CachedPromptTokens = Sum(a.CachedPromptTokens, b.CachedPromptTokens),
                CacheCreationTokens = Sum(a.CacheCreationTokens, b.CacheCreationTokens),
                TotalTokens = Sum(a.TotalTokens, b.TotalTokens),
            };
        }

        /// <summary>
        /// A field is summed only while every step reported it; a step that omits it makes the turn's value unknown.
        /// </summary>
        private static int? Sum(int? a, int? b)
        {
            if (!a.HasValue || !b.HasValue) return null;
            return a.Value + b.Value;
        }

        /// <summary>
        /// OpenCode errors carry a type such as <c>provider.auth</c>, <c>provider.free-limit</c>, or
        /// <c>provider.no-route</c>, a message, and for upstream failures the HTTP status.
        /// </summary>
        private static string DescribeError(JsonElement error)
        {
            string message = GetString(error, "message") ?? "OpenCode reported an error.";
            List<string> details = new List<string>();
            string? type = GetString(error, "type");
            if (!string.IsNullOrEmpty(type)) details.Add(type!);
            int? status = GetInt(error, "status");
            if (status.HasValue) details.Add("status " + status.Value);
            return details.Count > 0 ? message + " (" + string.Join(", ", details) + ")" : message;
        }

        private static CliFailureKind? ErrorKind(JsonElement error)
        {
            string type = GetString(error, "type") ?? "";
            int? status = GetInt(error, "status");
            if (type == "provider.auth" || status == 401 || status == 403) return CliFailureKind.NotAuthenticated;
            if (type == "provider.free-limit" || type == "provider.rate-limit" || status == 429) return CliFailureKind.RateLimited;
            return null;
        }

        private static List<ModelInformation> ParseModels(string output)
        {
            List<ModelInformation> models = new List<ModelInformation>();
            int unrecognized = 0;
            foreach (string raw in output.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;

                int slash = line.IndexOf('/');
                if (slash <= 0 || slash == line.Length - 1 || line.IndexOf(' ') >= 0)
                {
                    unrecognized++;
                    continue;
                }

                string provider = line.Substring(0, slash);
                ModelInformation model = new ModelInformation
                {
                    Name = line,
                    DisplayName = line.Substring(slash + 1),
                    OwnedBy = provider,
                };
                model.Metadata["provider"] = provider;
                models.Add(model);
            }

            if (models.Count == 0 && unrecognized > 0) throw new FormatException("Expected provider/model lines.");
            return models;
        }

        #endregion
    }
}
