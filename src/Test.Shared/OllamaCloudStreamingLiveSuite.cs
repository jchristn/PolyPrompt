namespace Test.Shared
{
    using System.Text.Json;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using Touchstone.Core;

    /// <summary>Opt-in live Cloud regression for final streaming state and a native tool continuation.</summary>
    public static class OllamaCloudStreamingLiveSuite
    {
        /// <summary>Create the live suite. Credentials remain in memory and are never included in test names or output.</summary>
        /// <param name="apiKey">Temporary Cloud bearer token.</param>
        /// <param name="model">Cloud model supporting chat, generation, and tools.</param>
        /// <returns>Live descriptors for the selected model.</returns>
        /// <exception cref="ArgumentException">A credential or model is blank.</exception>
        public static TestSuiteDescriptor Create(string apiKey, string model)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
            ArgumentException.ThrowIfNullOrWhiteSpace(model);
            return new TestSuiteDescriptor("ollamacloud_streaming_live", "Live Ollama Cloud streaming",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("ollamacloud_streaming_live", "completion_and_tool_continuation",
                        "Cloud chat, generation, validated tool call, and native continuation", t => RunAsync(apiKey, model, t), new[] { "live", "ollamacloud" })
                });
        }

        private static async Task RunAsync(string apiKey, string model, CancellationToken token)
        {
            using OllamaCloudCompletionClient client = new OllamaCloudCompletionClient(apiKey);
            client.TimeoutMs = 90000;
            OllamaCompletionOptions options = new OllamaCompletionOptions { Model = model, MaxTokens = 256, ReasoningEffort = ReasoningEffort.Low };
            ChatStreamingResponse chat = await client.ChatStreamingAsync("Reply with exactly pong.", options, token).ConfigureAwait(false);
            SharedAssert.Equal(StreamingState.Open, chat.State, "Chat opened");
            string text = "";
            await foreach (ChatStreamingChunk chunk in chat.Chunks.WithCancellation(token).ConfigureAwait(false)) text += chunk.Text;
            SharedAssert.Equal(StreamingState.Completed, chat.State, chat.StreamError ?? "Chat completed");
            SharedAssert.NotEmpty(text, "Chat produced text");

            GenerationStreamingResponse generation = await client.GenerateStreamingAsync("Reply with exactly pong.", options, token).ConfigureAwait(false);
            text = "";
            await foreach (GenerationStreamingChunk chunk in generation.Chunks.WithCancellation(token).ConfigureAwait(false)) text += chunk.Text;
            SharedAssert.Equal(StreamingState.Completed, generation.State, generation.StreamError ?? "Generation completed");
            SharedAssert.NotEmpty(text, "Generation produced text");

            ToolChatRequest request = new ToolChatRequest
            {
                Options = options,
                Messages = new List<ChatMessage> { ChatMessage.User("Call the probe tool now with value 7. Do not answer until you have used the tool.") },
                Tools = new List<ToolDefinition> { ToolDefinition.Function("probe", "Return the given integer", new Dictionary<string, object>
                {
                    ["type"] = "object", ["properties"] = new Dictionary<string, object> { ["value"] = new Dictionary<string, object> { ["type"] = "integer" } },
                    ["required"] = new[] { "value" }
                }) }
            };
            ToolChatStreamingResponse tools = await client.ToolChatStreamingAsync(request, token).ConfigureAwait(false);
            await foreach (ToolChatStreamingChunk chunk in tools.Chunks.WithCancellation(token).ConfigureAwait(false)) { }
            SharedAssert.Equal(StreamingState.Completed, tools.State, tools.StreamError ?? "Tool turn completed");
            SharedAssert.Equal(1, tools.ToolCalls.Count, "Model requested one tool");
            ToolCall call = tools.ToolCalls[0];
            SharedAssert.Equal("probe", call.Name, "Only the advertised tool is accepted");
            using JsonDocument arguments = JsonDocument.Parse(call.ArgumentsJson!);
            SharedAssert.Equal(7, arguments.RootElement.GetProperty("value").GetInt32(), "Arguments validated before producing a result");
            request.Messages.Add(tools.ToAssistantMessage());
            request.Messages.Add(ChatMessage.ToolResult(call.Id, call.Name, "7"));
            ToolChatStreamingResponse continuation = await client.ToolChatStreamingAsync(request, token).ConfigureAwait(false);
            await foreach (ToolChatStreamingChunk chunk in continuation.Chunks.WithCancellation(token).ConfigureAwait(false)) { }
            SharedAssert.Equal(StreamingState.Completed, continuation.State, continuation.StreamError ?? "Native continuation completed");
            SharedAssert.NotEmpty(continuation.Text, "Continuation produced an answer");
            SharedAssert.Equal(0, continuation.ToolCalls.Count, "Continuation accepted the result");
        }
    }
}
