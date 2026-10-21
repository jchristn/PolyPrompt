namespace PolyPrompt.ConsoleSupport;

using PolyPrompt.Clients;
using PolyPrompt.Models;

/// <summary>Provider-independent conversation, rendering, and automatic tool-result loop.</summary>
public sealed class ConsoleSession(CompletionClientBase client, IConsoleToolRuntime tools, TextWriter output) : IDisposable
{
    private readonly List<ChatMessage> _Messages = new();
    public string WorkingDirectory { get; set; } = Directory.GetCurrentDirectory();
    public int MaxRounds { get; set; } = 12;
    public IReadOnlyList<ChatMessage> Messages => _Messages;
    public void Reset() => _Messages.Clear();

    public async Task SendAsync(string prompt, bool streaming, bool enableTools = true, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        if (MaxRounds < 1) throw new InvalidOperationException("MaxRounds must be positive.");
        IReadOnlyList<ToolDefinition> definitions = enableTools ? await tools.GetToolsAsync(token) : Array.Empty<ToolDefinition>();
        int start = _Messages.Count;
        _Messages.Add(ChatMessage.User(prompt));
        try
        {
            for (int round = 0; round < MaxRounds; round++)
            {
                token.ThrowIfCancellationRequested();
                ToolChatRequest request = new() { Messages = BuildMessages(), Tools = definitions.ToList() };
                ConsoleRenderer renderer = new(output);
                List<ToolCall> calls;
                ChatMessage assistant;
                string? finish;
                if (streaming)
                {
                    ToolChatStreamingResponse response = await client.ToolChatStreamingAsync(request, token);
                    try
                    {
                        await foreach (ToolChatStreamingChunk chunk in response.Chunks.WithCancellation(token))
                        {
                            renderer.Thinking(chunk.ReasoningText);
                            renderer.Answer(chunk.Text);
                        }
                        if (response.State != StreamingState.Completed) throw new InvalidOperationException(response.StreamError ?? "Streaming chat did not complete.");
                        if (!renderer.HasReasoning) renderer.Thinking(response.Reasoning);
                        if (!renderer.HasAnswer) renderer.Answer(response.Text);
                    }
                    finally { renderer.Finish(); }
                    calls = response.ToolCalls;
                    assistant = response.ToAssistantMessage();
                    assistant.Content = response.Text ?? "";
                    finish = response.FinishReason;
                    PrintUsage(response.Usage);
                }
                else
                {
                    ToolChatResponse response = await client.ToolChatAsync(request, token);
                    if (!response.Success) throw new InvalidOperationException(response.Error ?? "Chat failed.");
                    renderer.Thinking(response.Reasoning);
                    renderer.Answer(response.Text);
                    renderer.Finish();
                    calls = response.ToolCalls;
                    assistant = response.ToAssistantMessage();
                    assistant.Content = response.Text ?? "";
                    finish = response.FinishReason;
                    PrintUsage(response.Usage);
                }
                _Messages.Add(assistant);
                if (calls.Count == 0)
                {
                    if (finish is "length" or "max_tokens" or "MAX_TOKENS")
                        output.WriteLine("Response reached the token limit. Increase Max tokens if thinking consumed the answer budget.");
                    return;
                }
                if (!enableTools) throw new InvalidOperationException("The model requested a tool while tools are disabled.");
                if (calls.Count > 64) throw new InvalidOperationException("Model requested more than 64 tool calls in one round.");
                foreach (ToolCall call in calls)
                {
                    token.ThrowIfCancellationRequested();
                    output.WriteLine("--- Tool: " + call.Name + " ---");
                    output.WriteLine(call.ArgumentsJson);
                    string result;
                    try
                    {
                        // Execute only tools that were actually advertised on this turn.
                        result = definitions.Any(t => t.Name == call.Name)
                            ? await tools.ExecuteAsync(call, WorkingDirectory, token)
                            : "Tool error: unknown tool '" + call.Name + "'.";
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (Exception ex) { result = "Tool error: " + ex.Message; }
                    output.WriteLine(result);
                    _Messages.Add(ChatMessage.ToolResult(call.Id, call.Name, result));
                }
            }
            throw new InvalidOperationException("Tool loop reached its " + MaxRounds + " round limit.");
        }
        catch
        {
            // Keep the next turn free of partial/invalid tool-call histories after a failed or canceled request.
            _Messages.RemoveRange(start, _Messages.Count - start);
            throw;
        }
    }

    private List<ChatMessage> BuildMessages()
    {
        string system = (client.Defaults.SystemPrompt ?? "") + "\nWorking directory: " + WorkingDirectory
            + "\nUse the provided tools to inspect files or perform requested actions; do not invent tool results. Tool paths may be relative to the working directory.";
        return new[] { ChatMessage.System(system) }.Concat(_Messages).ToList();
    }

    private void PrintUsage(TokenUsage? usage)
    {
        if (usage == null || (!usage.PromptTokens.HasValue && !usage.CompletionTokens.HasValue && !usage.ReasoningTokens.HasValue)) return;
        output.WriteLine("--- Usage ---");
        if (usage.PromptTokens.HasValue) output.WriteLine("Input tokens: " + usage.PromptTokens);
        if (usage.CompletionTokens.HasValue) output.WriteLine("Output tokens: " + usage.CompletionTokens);
        if (usage.ReasoningTokens.HasValue) output.WriteLine("Reasoning tokens: " + usage.ReasoningTokens);
        output.WriteLine();
    }

    public void Dispose() => tools.Dispose();
}
