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
|   |-- Test.Shared/             # Test descriptors, the local fixture server, and Fixtures/ (4.0 wire bodies, media)
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

## Adding a capability

A capability is a `Capability` flag, one public operation on `ProviderClientBase`, and a core hook each provider overrides. `Transcribe` (5.0) is a complete example:

1. **Flag.** Add it to `Models/Capability.cs` with an XML summary naming the method.
2. **Operation.** Add a partial `Clients/Base/ProviderClientBase.<Name>.cs` with the public method. It calls `RequireCapability`, validates arguments, resolves the model from options and `Defaults`, and wraps `InstrumentAsync` around a `protected virtual ...CoreAsync` that throws `NotSupported` by default.
3. **Defaults.** If the operation takes a model, add a section to `ClientDefaults`, a supported section on each implementing provider's defaults type, and a hidden `[Obsolete(..., true)]` member on every other defaults type. `contract_defaults_sections` checks this.
4. **Providers.** Override the core hook, add the flag to `Capabilities`, and name the capability in the client's XML summary between `Supported:` and `Not supported` (`contract_xml_docs` checks it).
5. **Telemetry.** Add an operation constant to `PolyPromptTelemetryNames` and a capability label in `PolyPromptTelemetry.ResolveCapability`; update TELEMETRY.md.
6. **Contracts.** Add the README matrix column (`contract_readme_matrix` parses it), default-model rows, the operation in `LocalArchitectureCases` with its overload count, and an unsupported-call row in `LocalV4Cases`.
7. **Fixtures and tests.** Add a route to the local fixture server (`LocalTranscriptionRoutes` is the pattern), a `Local<Name>Cases.cs` registered in `LocalBehaviorSuite`, AOT checks in `Test.Aot` (link any new fixture files in its csproj), and JSON registrations in `PolyPromptJsonContext`.
8. **Docs.** README, PROVIDERS.md, llms.txt, and the CHANGELOG. Every `csharp` block must compile under `Test.Docs`.

**Wire identity.** `LocalWireIdentityCases` replays 160 text-only requests and compares their bytes, path, and `Content-Type` with `Fixtures/V4Bodies`, captured from 4.0.0. An intended change goes in `Fixtures/V5Changes` under the same name with a `# reason:` first line, and in the migration guide. To capture fixtures, run the suite with `POLYPROMPT_CAPTURE_WIRE_FIXTURES=<dir>`.

**Media fixtures.** `src/Test.Shared/Fixtures/Media` holds a 64x64 red PNG, a one-second 440 Hz WAV, and a one-second blue MP4 (about 18 KB in all), embedded in Test.Shared. With the usual provider variables set, `POLYPROMPT_TEST_IMAGE=1` runs the live image case (needs a vision model), `POLYPROMPT_TEST_VIDEO=1` the live video case (Gemini, Vertex AI, or Amazon Nova), and `POLYPROMPT_TEST_TRANSCRIPTION_MODEL=<model>` live transcription.

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
