# Development

## Project structure

```
PolyPrompt/
|-- src/
|   |-- PolyPrompt/              # Core library (NuGet package)
|   |   |-- Auth/                # SigV4 signing, OAuth and AWS credential providers
|   |   |-- Clients/
|   |   |   |-- Base/            # ClientBase, HttpClientBase, ProviderClientBase, CliAgentClientBase
|   |   |   |-- Responses/       # ResponsesApiClientBase and the shared Responses engine
|   |   |   `-- <Provider>/      # One folder per provider or harness
|   |   |-- Helpers/             # Serializer, PolyPromptJsonContext, continuation state, citations
|   |   |-- Telemetry/           # Meter, ActivitySource, and stable names
|   |   |-- Wire/                # AWS event-stream decoder
|   |   |-- Models/              # Requests, responses, Capability, Citation, extensible values
|   |   `-- Options/             # Provider-specific options
|   |-- *Console/                # Provider and harness consoles
|   |-- Console.Shared/, Console.Tools/, AgentConsole.Shared/
|   |-- Test.Shared/             # Test descriptors and the local fixture server
|   |-- Test.Automated/, Test.Xunit/, Test.Nunit/   # Runners over Test.Shared
|   |-- Test.Console/            # Console end-to-end tests
|   |-- Test.Docs/               # Compiles every C# snippet in the docs
|   `-- Test.Aot/                # Native AOT verification
|-- postman/                     # Contract specs, mocks, and simulations
`-- llms.txt                     # Index for coding agents
```

## Building and testing

```bash
dotnet build src/PolyPrompt.sln -c Release
dotnet run --project src/Test.Automated -c Release -f net8.0
dotnet run --project src/Test.Automated -c Release -f net10.0
dotnet test src/Test.Xunit -c Release
dotnet test src/Test.Nunit -c Release
dotnet test src/Test.Docs -c Release
dotnet test src/Test.Console -c Release
src/Test.Aot/verify-aot.sh
```

The local suite runs against an in-process fixture server and a fake agent CLI (macOS or Linux) and needs no credentials. It includes:

- contract tests that parse the README's capability matrix and default-model table and compare them to the code
- a behavior contract that calls every unsupported operation on every client
- `Test.Docs`, which compiles every C# block in README.md, PROVIDERS.md, HARNESSES.md, STREAMING.md, and MIGRATION_V3_TO_V4.md (a block preceded by `<!-- no-compile -->` is skipped)

### Postman simulations

The saved simulations exercise real Mux tool rounds through local contract fixtures:

```bash
postman simulation run postman/simulations/openai-responses.sim.yaml --port 4913 --no-history
POLYPROMPT_OPENAI_RESPONSES_SIMULATION=http://127.0.0.1:4913/openai-responses dotnet test src/Test.Console -c Release
```

`postman/simulations/xai.sim.yaml` (`POLYPROMPT_XAI_SIMULATION`) and `ollamacloud.sim.yaml` (`POLYPROMPT_CONSOLE_SIMULATION`) work the same way.

### Live tests

The live suite runs against one provider at a time and skips cases for unsupported capabilities:

```bash
dotnet run --project src/Test.Automated -- --openai-key sk-... --openai-model gpt-6.1-sol
dotnet run --project src/Test.Automated -- --anthropic-key sk-ant-... --anthropic-model claude-sonnet-5-5
dotnet run --project src/Test.Automated -- --ollama-endpoint http://localhost:11434 --ollama-model gpt-oss:20b --ollama-embedding-model all-minilm
dotnet run --project src/Test.Automated -- --azure-endpoint https://my.openai.azure.com --azure-key ... --azure-model my-deployment
dotnet run --project src/Test.Automated -- --bedrock-region us-east-1 --bedrock-model anthropic.claude-sonnet-5-5
```

The same settings can come from `POLYPROMPT_TEST_*` environment variables (for example `POLYPROMPT_TEST_OPENAI_API_KEY` and `POLYPROMPT_TEST_OPENAI_MODEL`); run `Test.Automated --help` for the list. Harness live suites are opt-in: `POLYPROMPT_TEST_CODEX=1`, `POLYPROMPT_TEST_CLAUDE_CODE=1`, and so on.

## Console samples

Every `*Console` project shares `ConsoleApplicationBase` in `Console.Shared` and holds one provider client. The chat-capable consoles (Ollama, Ollama Cloud, OpenAI, Anthropic, Gemini, Cohere, xAI) share one `ConsoleSession` for streaming, conversation history, reasoning display, and automatic tool execution with the Mux built-in tools (`read_file`, `write_file`, `edit_file`, `list_directory`, `glob`, `grep`, `run_process`, `web_retrieve`, task planning, and more).

```bash
dotnet run --project src/OpenAIConsole -f net10.0                                       # OpenAiClient (Responses)
dotnet run --project src/OpenAIConsole -f net10.0 -- --compatible http://localhost:8000/v1   # OpenAiCompatibleClient
dotnet run --project src/XAiConsole -f net10.0
```

Commands:

- `ch`/`chat`, `tc`/`toolchat`: chat with automatic tools and persistent history
- `gen`, `gens`: generate text (non-streaming, streaming)
- `em`, `emb`, `rr`, `cl`, `sp`: embed, batch embed, rerank, classify, sparse embed (where supported)
- `mod`, `val`, `settings`: list models, validate connectivity (and print `Capabilities`), show settings
- `tools on/off`, `pwd`, `cd PATH`, `reset`, `reasoning LEVEL`: tool, directory, history, and reasoning controls

Automatic tools run with the console user's permissions. The loop is limited to 12 model rounds and 64 tool calls per round. `Console.Tools` is a companion JIT process copied into each console's `tools/` directory, so the library and `AotConsole` never load Mux. `AotConsole --provider ollama|ollamacloud|openai|openai-compatible|anthropic|gemini|xai` is the native console. Harness consoles are described in [HARNESSES.md](HARNESSES.md#consoles).
