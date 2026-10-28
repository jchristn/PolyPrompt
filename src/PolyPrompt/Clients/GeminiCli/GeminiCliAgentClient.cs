namespace PolyPrompt.Clients
{
    using System.Text;
    using System.Text.Json;
    using PolyPrompt.Helpers;
    using PolyPrompt.Models;
    using SyslogLogging;

    /// <summary>
    /// Agent harness client for Google's Gemini CLI (<c>gemini</c>). This runs the CLI as a process and is unrelated to
    /// the HTTP <see cref="GeminiCompletionClient"/>. Each run is a headless <c>gemini --output-format stream-json</c>
    /// with the prompt on standard input (the CLI reads piped standard input as the prompt when no <c>-p</c> is given; it
    /// truncates standard input beyond 8 MB). A session is resumed with <c>--resume &lt;session id&gt;</c>; sessions are
    /// stored per project, so resume from the same working directory. Authentication is the CLI's own (Google login
    /// cached by an interactive run, or <c>GEMINI_API_KEY</c>/<c>GOOGLE_GENAI_USE_VERTEXAI</c>/<c>GOOGLE_GENAI_USE_GCA</c>
    /// in the environment); the CLI has no login-status command, so connectivity cannot verify the login.
    /// <para>
    /// Permission mapping: <see cref="CliPermissionMode.Restricted"/> and <see cref="CliPermissionMode.ReadOnly"/> use
    /// <c>--approval-mode default</c>, under which a headless run denies every tool call that would need approval, so
    /// only the CLI's read-only tools run. (<c>--approval-mode plan</c> is deliberately not used: in headless runs the CLI
    /// switches from plan mode to YOLO mode when the plan is executed.) <see cref="CliPermissionMode.WorkspaceWrite"/> uses
    /// <c>--approval-mode auto_edit</c> (file edits are approved; shell commands still need approval and are denied).
    /// <see cref="CliPermissionMode.Unrestricted"/> uses <c>--approval-mode yolo</c>. The CLI itself downgrades any mode to
    /// <c>default</c> in a folder the user has not trusted, and allow rules in the user's own Gemini CLI policy still
    /// apply. <c>--sandbox</c> is not added because it needs a container runtime or macOS Seatbelt; pass it through
    /// <see cref="CliAgentRequest.ExtraArguments"/> to opt in.
    /// </para>
    /// </summary>
    public class GeminiCliAgentClient : CliAgentClientBase
    {
        #region Public-Members

        /// <summary>
        /// Default executable name.
        /// </summary>
        public const string DefaultExecutable = "gemini";

        /// <summary>
        /// Gemini CLI resumes sessions with <c>--resume &lt;session id&gt;</c>.
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
            get { return "Gemini CLI"; }
        }

        #endregion

        #region Private-Members

        private const int _ExitAuthenticationError = 41;
        private const int _ExitInputError = 42;
        private const int _ExitTurnLimit = 53;

        private const string _FinalTextKey = "gemini:final";
        private const string _TurnEndedKey = "gemini:turn-ended";
        private const string _LastErrorKey = "gemini:last-error";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a Gemini CLI client.
        /// </summary>
        /// <param name="executable">Executable name or path. Default: <c>gemini</c>.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        public GeminiCliAgentClient(string executable = DefaultExecutable, LoggingModule? logging = null)
            : base(executable, logging ?? new LoggingModule())
        {
            _Header = "[GeminiCli] ";
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Build <c>gemini --output-format stream-json --approval-mode &lt;mode&gt; [-m &lt;model&gt;] [--resume &lt;id&gt;]</c>;
        /// the prompt travels on standard input.
        /// </summary>
        /// <param name="state">Run state.</param>
        /// <returns>The invocation.</returns>
        protected override CliInvocation BuildInvocation(CliRunState state)
        {
            List<string> args = new List<string> { "--output-format", "stream-json", "--approval-mode" };

            switch (state.PermissionMode)
            {
                case CliPermissionMode.Unrestricted:
                    args.Add("yolo");
                    break;
                case CliPermissionMode.WorkspaceWrite:
                    args.Add("auto_edit");
                    break;
                default:
                    args.Add("default");
                    break;
            }

            if (!string.IsNullOrWhiteSpace(state.Model))
            {
                args.Add("-m");
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
        /// Map one <c>gemini --output-format stream-json</c> line.
        /// <code>
        /// init {session_id, model}                           -> Started (session id); model is the reported model
        /// message role=user                                  -> (none; echo of the prompt)
        /// message role=assistant {content, delta}            -> TextDelta (the text after the last tool round is the final text)
        /// tool_use {tool_name, tool_id, parameters}          -> ToolUse
        /// tool_result {tool_id, status, output, error}       -> ToolResult (IsError when status is "error")
        /// error {severity, message}                          -> Notice (IsError); the CLI uses it for non-fatal warnings and errors
        /// result status=success {stats}                      -> usage; Completed is emitted by the base at exit
        /// result status=error {error.message, stats}         -> usage; failure (message from error, else the last error event)
        /// </code>
        /// Usage (from <c>result.stats</c>): input_tokens -> PromptTokens, cached -> CachedPromptTokens, output_tokens ->
        /// CompletionTokens, total_tokens -> TotalTokens. The CLI reports no reasoning, cache-creation, or cost figures in
        /// stream-json, so ReasoningTokens, CacheCreationTokens, and CostUsd stay null. Usage is null when stats are absent.
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
                case "init":
                    {
                        string? model = GetString(root, "model");
                        if (!string.IsNullOrEmpty(model)) state.ReportedModel = model;
                        state.Started(GetString(root, "session_id"));
                        break;
                    }

                case "message":
                    if (GetString(root, "role") == "assistant") AppendAssistantText(GetString(root, "content"), state);
                    break;

                case "tool_use":
                    {
                        string? id = GetString(root, "tool_id");
                        string? name = GetString(root, "tool_name");
                        state.Properties["tool:" + (id ?? "")] = name;
                        state.Properties[_TurnEndedKey] = true;
                        state.ToolUse(name, id, GetRendered(root, "parameters"));
                        break;
                    }

                case "tool_result":
                    {
                        string? id = GetString(root, "tool_id");
                        string? name = state.Properties.TryGetValue("tool:" + (id ?? ""), out object? known) ? known as string : null;
                        JsonElement? error = GetObject(root, "error");
                        bool isError = GetString(root, "status") == "error" || error.HasValue;
                        string? output = GetString(root, "output");
                        if (output == null && error.HasValue) output = GetString(error.Value, "message") ?? RenderJson(error);
                        state.Properties[_TurnEndedKey] = true;
                        state.ToolResult(name, id, output, isError);
                        break;
                    }

                case "error":
                    {
                        string? message = GetString(root, "message");
                        if (GetString(root, "severity") == "error" && !string.IsNullOrEmpty(message)) state.Properties[_LastErrorKey] = message;
                        state.Notice(message, true);
                        break;
                    }

                case "result":
                    {
                        JsonElement? stats = GetObject(root, "stats");
                        if (stats.HasValue) state.Usage = ParseUsage(stats.Value);

                        if (GetString(root, "status") == "error")
                        {
                            JsonElement? error = GetObject(root, "error");
                            string? message = error.HasValue ? GetString(error.Value, "message") : null;
                            if (string.IsNullOrWhiteSpace(message) && state.Properties.TryGetValue(_LastErrorKey, out object? last)) message = last as string;
                            state.Fail(string.IsNullOrWhiteSpace(message) ? HarnessName + " reported an error result." : message);
                        }
                        else
                        {
                            state.Complete();
                        }
                        break;
                    }

                default:
                    break;
            }
        }

        /// <summary>
        /// Classify a failed run using Gemini CLI's documented exit codes first: 41 (authentication error) is
        /// <see cref="CliFailureKind.NotAuthenticated"/>, 42 (input error: invalid prompt, arguments, or session to resume)
        /// is <see cref="CliFailureKind.InvalidInput"/>, and 53 (turn limit exceeded) is
        /// <see cref="CliFailureKind.TurnLimitReached"/>. Other exits use the base classification.
        /// </summary>
        /// <param name="state">Run state.</param>
        /// <param name="process">Process outcome.</param>
        /// <returns>The failure kind.</returns>
        protected override CliFailureKind ClassifyFailure(CliRunState state, ProcessRunResult process)
        {
            switch (process.ExitCode)
            {
                case _ExitAuthenticationError: return CliFailureKind.NotAuthenticated;
                case _ExitInputError: return CliFailureKind.InvalidInput;
                case _ExitTurnLimit: return CliFailureKind.TurnLimitReached;
                default: return base.ClassifyFailure(state, process);
            }
        }

        #endregion

        #region Private-Methods

        private static void AppendAssistantText(string? text, CliRunState state)
        {
            if (string.IsNullOrEmpty(text)) return;

            StringBuilder final;
            if (state.Properties.TryGetValue(_FinalTextKey, out object? existing) && existing is StringBuilder sb)
            {
                final = sb;
            }
            else
            {
                final = new StringBuilder();
                state.Properties[_FinalTextKey] = final;
            }

            // Text after a tool round starts a new model turn; only the last turn's text is the final answer (the CLI's
            // own JSON output format reports the same).
            if (state.Properties.TryGetValue(_TurnEndedKey, out object? ended) && ended is bool b && b)
            {
                final.Clear();
                state.Properties[_TurnEndedKey] = false;
            }

            final.Append(text);
            state.FinalText = final.ToString();
            state.Text(text);
        }

        private static TokenUsage ParseUsage(JsonElement stats)
        {
            return new TokenUsage
            {
                PromptTokens = GetInt(stats, "input_tokens"),
                CachedPromptTokens = GetInt(stats, "cached"),
                CompletionTokens = GetInt(stats, "output_tokens"),
                TotalTokens = GetInt(stats, "total_tokens"),
            };
        }

        #endregion
    }
}
