namespace Test.Shared
{
    using System.Text;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;

    internal sealed class ProbeOpenAiClient : OpenAiCompatibleClient
    {
        internal ProbeOpenAiClient(string endpoint, string apiKey) : base(endpoint, apiKey)
        {
            Defaults.Completion.Model = "test-model";
        }

        internal async Task<HttpCallResult> PostProbeAsync(CancellationToken token)
        {
            string url = Endpoint.TrimEnd('/') + "/v1/chat/completions";
            string json = "{\"model\":\"test-model\",\"messages\":[{\"role\":\"user\",\"content\":\"probe\"}],\"max_tokens\":1}";
            return await PostAndRecordAsync(url, Encoding.UTF8.GetBytes(json), JsonContentType, json, token).ConfigureAwait(false);
        }
    }
}
