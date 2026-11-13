# Agent Harnesses

Claude Code, Codex, Gemini CLI, Cursor CLI, Mux, Pi, and OpenCode are not model APIs. They are **agent harnesses**: local CLIs that run their own tool loop and report agent events. PolyPrompt runs them as shell-free child processes through `CliAgentClientBase`. That base is separate from the provider clients, and a harness never hands you tool calls to execute.

## Prerequisites

Install each CLI and sign in **with the CLI itself**; PolyPrompt never takes an API key for a harness. The executable must be on `PATH`, or set `Executable`.

| Harness | Client | Executable |
|---|---|---|
| Claude Code | `ClaudeCodeClient` | `claude` |
| Codex | `CodexClient` | `codex` |
| Gemini CLI | `GeminiCliClient` | `gemini` |
| Cursor CLI | `CursorClient` | `cursor-agent` |
| Mux | `MuxClient` | `mux` |
| Pi | `PiClient` | `pi` |
| OpenCode | `OpenCodeClient` | `opencode` |

How to install and sign in:

- **Claude Code**: [setup guide](https://code.claude.com/docs/en/setup), then `claude auth login`.
- **Codex**: [Codex CLI](https://developers.openai.com/codex/cli), then `codex login`, or set `OPENAI_API_KEY` / `CODEX_API_KEY`.
- **Gemini CLI**: [Gemini CLI](https://github.com/google-gemini/gemini-cli), then run `gemini` once and sign in, or set `GEMINI_API_KEY`.
- **Cursor CLI**: [Cursor CLI](https://cursor.com/docs/cli/installation), then `cursor-agent login`, or set `CURSOR_API_KEY`.
- **Mux**: `dotnet tool install -g mux.cli`, then configure an endpoint. `mux probe` checks it.
- **Pi**: [pi.dev](https://pi.dev), then `pi` and `/login`, or a provider key.
- **OpenCode**: [opencode.ai](https://opencode.ai/docs), then `opencode auth login`.

## Run a turn

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

using CodexClient codex = new CodexClient { WorkingDirectory = "/path/to/repo" };

CliConnectivityResult status = await codex.ValidateConnectivityAsync();
if (!status.Success) throw new InvalidOperationException(status.Status + ": " + status.Message);

CliAgentResult result = await codex.RunAsync(new CliAgentRequest("Summarize what this repository does."));
Console.WriteLine(result.Success ? result.Text : result.FailureKind + ": " + result.Error);

CliAgentResult next = await codex.RunAsync(new CliAgentRequest("What is the project name?") { SessionId = result.SessionId });
```

## Stream agent events

```csharp
using PolyPrompt.Clients;
using PolyPrompt.Models;

using ClaudeCodeClient claude = new ClaudeCodeClient { Model = "sonnet" };

await foreach (CliAgentEvent evt in claude.RunStreamingAsync(new CliAgentRequest("Find the TODO comments in src/")))
{
    switch (evt.Kind)
    {
        case CliAgentEventKind.TextDelta: Console.Write(evt.Text); break;
        case CliAgentEventKind.ToolUse: Console.WriteLine("\n[" + evt.ToolName + "] " + evt.ToolInput); break;
        case CliAgentEventKind.Completed: Console.WriteLine("\nDone. Cost: " + evt.CostUsd); break;
        case CliAgentEventKind.Failed: Console.WriteLine("\nFailed (" + evt.FailureKind + "): " + evt.Text); break;
    }
}
```

A harness stream is coarse, and its chunk boundaries vary. The last event is always `Completed` or `Failed`, and cancelling kills the whole process tree. See [STREAMING.md](STREAMING.md).

## Permissions

`CliPermissionMode` controls what the harness may do: `Restricted` (the default), `ReadOnly`, `WorkspaceWrite`, or `Unrestricted`. Only `Unrestricted` ever passes a bypass, yolo, or force flag.

The flags each mode passes:

**Claude Code**
- Restricted: `--permission-mode dontAsk --tools Read,Glob,Grep --strict-mcp-config`
- ReadOnly: `--permission-mode dontAsk --disallowedTools Edit,Write,NotebookEdit`
- WorkspaceWrite: `--permission-mode acceptEdits`
- Unrestricted: `--permission-mode bypassPermissions`

**Codex**
- Restricted and ReadOnly: `-c sandbox_mode="read-only" -c approval_policy="never" --skip-git-repo-check`
- WorkspaceWrite: `-c sandbox_mode="workspace-write" -c approval_policy="never"`
- Unrestricted: `--dangerously-bypass-approvals-and-sandbox`

**Gemini CLI**
- Restricted and ReadOnly: `--approval-mode default`
- WorkspaceWrite: `--approval-mode auto_edit`
- Unrestricted: `--approval-mode yolo`

**Cursor CLI**
- Restricted and ReadOnly: `--mode ask --sandbox enabled`
- WorkspaceWrite: `--sandbox enabled`
- Unrestricted: `--force --sandbox disabled`

**Mux**
- Restricted: `--approval-policy auto --sandbox read-only --deny-tools web_search,web_retrieve`
- ReadOnly: `--approval-policy auto --sandbox read-only`
- WorkspaceWrite: `--approval-policy auto --sandbox workspace-write --deny-tools run_process`
- Unrestricted: `--yolo --sandbox none`

**Pi (RPC)**
- Restricted and ReadOnly: `--tools read,grep,find,ls --no-mcp --no-approve`
- WorkspaceWrite: adds `edit,write`
- Unrestricted: Pi defaults

**OpenCode**
- Restricted and ReadOnly: `--agent build` with an inline read/glob/grep-only permission config
- WorkspaceWrite: adds `edit`
- Unrestricted: `--auto`

## Requests and results

`CliAgentRequest` takes:

- `Prompt` (sent on standard input), `SystemPrompt`, `History`, `SessionId`, `Model`
- `WorkingDirectory`, `PermissionMode`, `TimeoutMs`, `Environment`, `ExtraArguments`, `MaxOutputCharacters`

`CliAgentResult` carries:

- `Success`, `Text`, `Reasoning`, `SessionId`, `Events`, `Usage`, `CostUsd`
- `ExitCode`, `StandardError`, `RawOutput`, `FailureKind`, `TimedOut`, `OutputTruncated`, `MalformedLines`

Failures do not throw; only caller cancellation does. Each process is recorded in `CallDetails` as a `ProcessCallDetail` and reported to telemetry as an `exec {executable}` span.

## Consoles

`src/CliAgentConsole` runs any harness (`--provider codex|claudecode|geminicli|cursor|mux|pi|opencode`). Each harness also has its own console (`src/ClaudeCodeConsole`, `src/CodexConsole`, `src/GeminiCliConsole`, `src/CursorCliConsole`, `src/MuxConsole`, `src/PiConsole`, `src/OpenCodeConsole`) with `--prompt`, `--check`, `--models`, `--setup`, `--permission`, and interactive commands.
