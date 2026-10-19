using System.Text.Json;
using Mux.Core.Models;
using Mux.Core.Agent;
using Mux.Core.Enums;
using Mux.Core.Subagents;
using Mux.Core.Tasks;
using Mux.Core.Tools;

if (args.Length == 2 && args[0] == "--install-browser")
    return Microsoft.Playwright.Program.Main(new[] { "install", args[1] });

// The host stays on the JIT so the same Mux tools also work for Native AOT console callers.
// Reserve stdout for the JSON-lines protocol, even when a tool writes console output.
TextWriter protocol = Console.Out;
Console.SetOut(Console.Error);
MuxSettings settings = new MuxSettings();
string? settingsPath = Environment.GetEnvironmentVariable("POLYPROMPT_MUX_SETTINGS");
if (!string.IsNullOrWhiteSpace(settingsPath))
    settings = JsonSerializer.Deserialize<MuxSettings>(File.ReadAllText(settingsPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidOperationException("Mux settings must contain an object.");
// Optional subagents use explicit Mux endpoint and subagent configuration files.
// Credentials stay in the user's configuration/environment, never in command-line arguments.
SubagentRegistry? subagents = null;
ISubagentExecutor? executor = null;
string? subagentPath = Environment.GetEnvironmentVariable("POLYPROMPT_MUX_SUBAGENTS");
if (!string.IsNullOrWhiteSpace(subagentPath))
{
    string endpointPath = Environment.GetEnvironmentVariable("POLYPROMPT_MUX_ENDPOINTS")
        ?? throw new InvalidOperationException("POLYPROMPT_MUX_ENDPOINTS is required with POLYPROMPT_MUX_SUBAGENTS.");
    var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    List<EndpointConfig> endpoints = JsonSerializer.Deserialize<List<EndpointConfig>>(File.ReadAllText(endpointPath), jsonOptions) ?? new();
    EndpointConfig endpoint = endpoints.FirstOrDefault(e => e.IsDefault) ?? endpoints.FirstOrDefault()
        ?? throw new InvalidOperationException("At least one subagent endpoint is required.");
    subagents = new SubagentRegistry(JsonSerializer.Deserialize<List<SubagentDefinition>>(File.ReadAllText(subagentPath), jsonOptions));
    executor = new AgentLoopSubagentExecutor(() => new AgentLoopOptions(endpoint)
    {
        MuxSettings = settings,
        ApprovalPolicy = ApprovalPolicyEnum.AutoApprove,
        MaxIterations = 12
    }, name => endpoints.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
}
BuiltInToolRegistry registry = new BuiltInToolRegistry(settings, new TaskPlan(), subagents, executor);
while (await Console.In.ReadLineAsync() is string line)
{
    try
    {
        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement root = document.RootElement;
        if (root.GetProperty("operation").GetString() == "list")
        {
            await protocol.WriteLineAsync(JsonSerializer.Serialize(new
            {
                tools = registry.GetToolDefinitions().Select(t => new { name = t.Name, description = t.Description, parameters = t.ParametersSchema })
            }));
        }
        else if (root.GetProperty("operation").GetString() == "execute")
        {
            using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(settings.ToolTimeoutMs));
            ToolResult result = await registry.ExecuteAsync(root.GetProperty("id").GetString()!, root.GetProperty("name").GetString()!,
                root.GetProperty("arguments"), root.GetProperty("workingDirectory").GetString()!, timeout.Token);
            await protocol.WriteLineAsync(JsonSerializer.Serialize(new { success = result.Success, content = result.Content }));
        }
        else throw new ArgumentException("Unknown tool-host operation.");
    }
    catch (Exception ex)
    {
        await protocol.WriteLineAsync(JsonSerializer.Serialize(new { success = false, content = "Tool error: " + ex.Message }));
    }
    await protocol.FlushAsync();
}

return 0;
