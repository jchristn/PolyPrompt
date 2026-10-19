namespace PolyPrompt.ConsoleSupport;

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using PolyPrompt.Models;

/// <summary>Runs Mux's complete configured built-in registry in a companion JIT process.</summary>
public sealed class ProcessToolRuntime : IConsoleToolRuntime
{
    private readonly string _HostPath;
    private readonly SemaphoreSlim _Gate = new(1, 1);
    private Process? _Process;
    private bool _Disposed;

    public ProcessToolRuntime(string? hostPath = null)
    {
        _HostPath = hostPath ?? Environment.GetEnvironmentVariable("POLYPROMPT_TOOL_HOST")
            ?? Path.Combine(AppContext.BaseDirectory, "tools", "Console.Tools.dll");
    }

    public async Task<IReadOnlyList<ToolDefinition>> GetToolsAsync(CancellationToken token = default)
    {
        using JsonDocument reply = await ExchangeAsync("{\"operation\":\"list\"}", token);
        if (!reply.RootElement.TryGetProperty("tools", out JsonElement tools))
            throw new InvalidOperationException(reply.RootElement.GetProperty("content").GetString());
        List<ToolDefinition> result = new();
        foreach (JsonElement tool in tools.EnumerateArray())
        {
            // Keep schema values as JsonElements: PolyPrompt serializes them without reflection in Native AOT.
            Dictionary<string, object> schema = new();
            foreach (JsonProperty property in tool.GetProperty("parameters").EnumerateObject())
                schema[property.Name] = property.Value.Clone();
            result.Add(ToolDefinition.Function(tool.GetProperty("name").GetString()!, tool.GetProperty("description").GetString()!, schema));
        }
        return result;
    }

    public async Task<string> ExecuteAsync(ToolCall call, string workingDirectory, CancellationToken token = default)
    {
        using JsonDocument arguments = JsonDocument.Parse(call.ArgumentsJson ?? "{}");
        using MemoryStream buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("operation", "execute");
            writer.WriteString("id", call.Id);
            writer.WriteString("name", call.Name);
            writer.WriteString("workingDirectory", workingDirectory);
            writer.WritePropertyName("arguments");
            arguments.RootElement.WriteTo(writer);
            writer.WriteEndObject();
        }
        using JsonDocument reply = await ExchangeAsync(Encoding.UTF8.GetString(buffer.ToArray()), token);
        return reply.RootElement.GetProperty("content").GetString() ?? "";
    }

    private async Task<JsonDocument> ExchangeAsync(string request, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_Disposed, this);
        await _Gate.WaitAsync(token);
        try
        {
            if (_Process == null || _Process.HasExited)
            {
                Stop();
                if (!File.Exists(_HostPath)) throw new FileNotFoundException("Build the console with its Console.Tools companion, or set POLYPROMPT_TOOL_HOST.", _HostPath);
                ProcessStartInfo start = new("dotnet")
                {
                    RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
                };
                start.ArgumentList.Add(_HostPath);
                _Process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start the Mux tool host.");
                _Process.ErrorDataReceived += (_, _) => { }; // Drain diagnostics without mixing them into model output.
                _Process.BeginErrorReadLine();
            }
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            try
            {
                await _Process.StandardInput.WriteLineAsync(request.AsMemory(), timeout.Token);
                await _Process.StandardInput.FlushAsync(timeout.Token);
                string? line = await _Process.StandardOutput.ReadLineAsync(timeout.Token);
                return JsonDocument.Parse(line ?? throw new IOException("Mux tool host exited without a response. Check POLYPROMPT_MUX_SETTINGS."));
            }
            catch { Stop(); throw; }
        }
        finally { _Gate.Release(); }
    }

    private void Stop()
    {
        if (_Process == null) return;
        if (!_Process.HasExited) _Process.Kill(entireProcessTree: true);
        _Process.Dispose();
        _Process = null;
    }

    public void Dispose()
    {
        if (_Disposed) return;
        _Disposed = true;
        Stop();
    }
}
