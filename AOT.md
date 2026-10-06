# Native AOT and Trimming

PolyPrompt 3.2.0 and later is compatible with [Native AOT](https://learn.microsoft.com/dotnet/core/deploying/native-aot/) and [trimming](https://learn.microsoft.com/dotnet/core/deploying/trimming/trim-self-contained). The package is marked `IsAotCompatible`, builds with zero trim and AOT analyzer warnings, and a native binary that uses every client publishes with zero warnings and sends byte-for-byte the same requests as the JIT build.

Most applications need no changes. The one thing to know: if PolyPrompt serializes **your own types** (typed tool arguments, a decision `State` object, or a class you put inside a tool's parameter schema), give it source-generated metadata for them, because reflection is not available in a native binary.

## Contents

1. [Quick start](#quick-start)
2. [What works without any setup](#what-works-without-any-setup)
3. [Your own types](#your-own-types)
4. [Saving conversations and responses](#saving-conversations-and-responses)
5. [How serialization works](#how-serialization-works)
6. [Errors](#errors)
7. [Verification](#verification)
8. [Compatibility notes](#compatibility-notes)

## Quick start

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
</PropertyGroup>
```

```bash
dotnet publish -c Release -r linux-x64     # or osx-arm64, win-x64, ...
```

If your code calls `ToolCall.DeserializeArguments<T>()` or passes your own objects to PolyPrompt, add a source-generated context and register it once at startup:

```csharp
using System.Text.Json.Serialization;
using PolyPrompt.Helpers;

[JsonSerializable(typeof(WeatherArgs))]
internal partial class AppJsonContext : JsonSerializerContext { }

// At startup, before the first call:
Serializer.AddTypeInfoResolver(AppJsonContext.Default);
```

[src/AotConsole](src/AotConsole/Program.cs) is a complete sample: chat, streaming, a tool-calling loop with typed arguments, and saving the conversation, published as a native binary.

## What works without any setup

Every client and every operation: chat, tool chat, generation, streaming, embeddings, sparse embeddings, reranking, classification, decisions, model management, connectivity checks, credential providers (including AWS SigV4 and Google service-account tokens), and telemetry.

Tool parameter schemas and other values work without setup when they are built from:

- `Dictionary<string, object>` (or any `IDictionary`) and lists, arrays, or any `IEnumerable`
- `string`, `bool`, all numeric types, `char`, enums (written as strings), `DateTime`, `DateTimeOffset`, `Guid`, `TimeSpan`, and `byte[]` (written as base64)
- `JsonElement`, `JsonDocument`, and `JsonNode` (including `JsonObject` and `JsonArray`)

That covers the way the README builds schemas:

```csharp
ToolDefinition tool = ToolDefinition.Function("get_weather", "Get the weather.", new Dictionary<string, object>
{
    { "type", "object" },
    { "properties", new Dictionary<string, object> { { "city", new Dictionary<string, object> { { "type", "string" } } } } },
    { "required", new[] { "city" } }
});
```

A schema you already have as JSON text works too: `JsonDocument.Parse(schemaJson).RootElement.Clone()` as a value, or `JsonNode.Parse(schemaJson)`.

## Your own types

Reflection-based JSON is disabled in Native AOT and trimmed applications, so PolyPrompt needs source-generated metadata for any other type it serializes or deserializes. These are the places your types can reach PolyPrompt:

| Where | Example |
|---|---|
| Typed tool arguments | `call.DeserializeArguments<WeatherArgs>()` |
| Decision state | `new DecisionRequest { State = new Order { ... } }` |
| A class or anonymous object inside a tool schema or other dictionary | `{ "items", new ItemSchema() }` |
| Direct use of `Serializer` | `serializer.DeserializeJson<MyType>(json)` |

Anonymous types cannot be source-generated. In a native binary, replace them with dictionaries or named classes.

There are two ways to provide metadata.

**Register a resolver once.** PolyPrompt consults registered resolvers first, for every client in the process:

```csharp
[JsonSerializable(typeof(WeatherArgs))]
[JsonSerializable(typeof(Order))]
internal partial class AppJsonContext : JsonSerializerContext { }

Serializer.AddTypeInfoResolver(AppJsonContext.Default);

WeatherArgs? args = call.DeserializeArguments<WeatherArgs>();
```

**Or pass the metadata explicitly** where an overload takes a `JsonTypeInfo<T>`:

```csharp
WeatherArgs? args = call.DeserializeArguments(AppJsonContext.Default.WeatherArgs);

Serializer serializer = new Serializer();
string json = serializer.SerializeJson(order, AppJsonContext.Default.Order, pretty: false);
Order? copy = serializer.DeserializeJson(json, AppJsonContext.Default.Order);
```

The two differ in which options apply:

- A **registered** context is used with PolyPrompt's options: null properties omitted, enums as strings, UTC dates, and tolerant reads (trailing commas, comments, numbers in strings). Naming settings on the context's `[JsonSourceGenerationOptions]` do not apply. Use `[JsonPropertyName]` on properties when the JSON names differ from the C# names, which is typical for tool arguments (`"city"` vs `City`).
- **Explicit** metadata uses the context's own options, exactly as `JsonSerializer` would.

`Serializer.AddTypeInfoResolver` accepts any `IJsonTypeInfoResolver`, including combined resolvers and resolvers with modifiers. Registering the same instance twice has no effect. Register before the first call; registration rebuilds PolyPrompt's options, and a type already resolved before registration may stay cached by `System.Text.Json` for in-flight calls.

## Saving conversations and responses

`PolyPromptJsonContext` is a public, source-generated context for PolyPrompt's own models, so you can persist conversations and responses without reflection:

```csharp
using PolyPrompt.Helpers;

string json = JsonSerializer.Serialize(messages, PolyPromptJsonContext.Default.ListChatMessage);
List<ChatMessage>? restored = JsonSerializer.Deserialize(json, PolyPromptJsonContext.Default.ListChatMessage);
```

It covers `ChatMessage`, `ToolCall`, `ToolDefinition`, `ToolChatRequest`, `CompletionOptions`, `ReasoningEffort`, `TokenUsage`, `CallDetail`, `ChatResponse`, `ToolChatResponse`, `GenerationResponse`, `EmbeddingResponse`, `SparseEmbeddingResponse`, `RerankResponse`, `ClassificationResponse`, `ModelInformation`, and lists of the common ones (`ListChatMessage`, `ListToolCall`, `ListToolDefinition`, `ListCallDetail`, `ListModelInformation`). Persist `ToolCall.ThoughtSignature` with the conversation; Gemini 3 models reject replayed tool calls without it, and the context includes it.

Decision questions and answers are abstract, polymorphic types and are not included.

## How serialization works

All of PolyPrompt's JSON goes through `PolyPrompt.Helpers.Serializer`:

1. **Writing.** Dictionaries, lists, and the value types listed above are written directly with `Utf8JsonWriter`, with no reflection and no type registration. The output is byte-for-byte what the reflection-based serializer in 3.1 wrote (the test suite checks this). Any other value is written with metadata from step 3.
2. **Reading.** `DeserializeJson<T>` looks up metadata for `T` in step 3. Untyped values (`object`, `Dictionary<string, object>`, `List<object>`) become `JsonElement`.
3. **Metadata lookup**, in order: resolvers registered with `Serializer.AddTypeInfoResolver`, then `PolyPromptJsonContext`, then reflection when the application allows it (`Serializer.IsReflectionEnabled`). Reflection is enabled on the JIT by default and disabled in Native AOT and trimmed applications; the code that sets it up is removed from native binaries.

On the JIT nothing changes from 3.1: your types are still found by reflection if you do not register them.

## Errors

| Situation | Exception |
|---|---|
| No metadata for a type, with reflection disabled | `NotSupportedException`, naming the type and pointing to `Serializer.AddTypeInfoResolver` |
| Invalid JSON, or JSON that does not match the target type | `JsonException` |
| A dictionary or list graph nested more than `Serializer.MaxDepth` (64) levels, including a cycle | `JsonException` |
| `NaN` or infinity | `ArgumentException` (unchanged from 3.1) |
| A null `json`, `typeInfo`, or resolver argument | `ArgumentNullException` |

Client operations catch serialization failures the way they catch other failures. For example a decision request whose `State` type has no metadata returns an unsuccessful response with the message in `Error`.

## Verification

The `Test.Aot` project runs every client against the local mock server, plus serializer checks, on the JIT with reflection and as a native binary in which any trim or AOT warning (in PolyPrompt or its dependencies) is a build error. Both runs must pass, and they must send byte-identical request bodies:

```bash
src/Test.Aot/verify-aot.sh                      # current platform, net8.0 and net10.0
src/Test.Aot/verify-aot.sh linux-x64 net10.0    # a specific runtime identifier and framework
```

Native AOT publishing needs the platform toolchain: Xcode command line tools on macOS, `clang` and `zlib` development packages on Linux, and the C++ build tools on Windows. See [Native AOT prerequisites](https://learn.microsoft.com/dotnet/core/deploying/native-aot/#prerequisites).

The hermetic test suite (`Test.Automated`, `Test.Xunit`, `Test.Nunit`) includes `ser_*` cases for output parity with 3.1, `PolyPromptJsonContext`, the typed overloads, registered resolvers, and the error cases.

## Compatibility notes

- PolyPrompt's only package dependency, `SyslogLogging` 2.4.0 and later, is also AOT compatible.
- The interactive console harnesses (`OllamaConsole` and the others) use an input library that is not verified for Native AOT; use them on the JIT. `AotConsole` is the native sample.
- `UtcDateTimeConverter`, previously private, is now public so source-generated contexts (including your own) can use PolyPrompt's date format: `[JsonSourceGenerationOptions(Converters = new[] { typeof(UtcDateTimeConverter) })]`.
