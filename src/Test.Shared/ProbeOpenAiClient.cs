namespace Test.Shared
{
    using System.Text;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;

    internal sealed class ProbeOpenAiClient : OpenAiClient
    {
        internal ProbeOpenAiClient(string endpoint, string apiKey) : base(endpoint, apiKey)
        {
            Model = "test-model";
        }

        internal async Task<CompletionHttpResult> PostProbeAsync(CancellationToken token)
        {
            string url = Endpoint.TrimEnd('/') + "/v1/chat/completions";
            string json = "{\"model\":\"test-model\",\"messages\":[{\"role\":\"user\",\"content\":\"probe\"}],\"max_tokens\":1}";
            using StringContent content = new StringContent(json, Encoding.UTF8, "application/json");
            return await PostAndRecordAsync(url, content, json, token).ConfigureAwait(false);
        }
    }
}
