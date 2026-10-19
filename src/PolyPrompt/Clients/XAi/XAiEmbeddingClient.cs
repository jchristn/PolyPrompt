namespace PolyPrompt.Clients
{
    using System.Buffers.Binary;
    using System.Text.Json;
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using SyslogLogging;

    /// <summary>xAI text/token embeddings, including batches, dimensions, float/base64 vectors, and usage.</summary>
    public class XAiEmbeddingClient : EmbeddingClientBase
    {
        /// <summary>Embedding defaults. Model must be configured from the xAI embedding-model catalog.</summary>
        public override XAiEmbeddingOptions Defaults { get; } = new();
        /// <summary>Initialize an xAI embedding client.</summary>
        /// <param name="apiKey">Required bearer token.</param>
        /// <param name="endpoint">API root or versioned /v1 URL.</param>
        /// <param name="logging">Optional logging module.</param>
        /// <param name="httpClient">Optional HTTP client.</param>
        public XAiEmbeddingClient(string apiKey, string endpoint = XAiProtocol.Endpoint, LoggingModule? logging = null, HttpClient? httpClient = null)
            : base(endpoint, XAiProtocol.RequireKey(apiKey), logging ?? new LoggingModule(), httpClient) { _Header = "[XAi] "; }
        /// <inheritdoc />
        public override Task<bool> ValidateConnectivityAsync(CancellationToken token = default) => ProbeAsync(XAiProtocol.Url(_Endpoint, "models"), token);
        /// <inheritdoc />
        protected override Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token) { XAiProtocol.Auth(request, _ApiKey); return Task.CompletedTask; }
        /// <inheritdoc />
        protected override Task<EmbeddingResponse> EmbedCoreAsync(List<string> inputs, string? model, EmbeddingOptions? options, CancellationToken token)
        {
            if (inputs.Count > 128 || inputs.Any(string.IsNullOrEmpty)) throw new ArgumentException("xAI accepts 1..128 nonempty embedding inputs.", nameof(inputs));
            return EmbedInputAsync(inputs, inputs.Count, model!, options, token);
        }
        /// <summary>Embed a pre-tokenized input. Token IDs must be nonnegative and the input nonempty.</summary>
        public Task<EmbeddingResponse> EmbedTokensAsync(IEnumerable<int> tokens, XAiEmbeddingOptions? options = null, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(tokens);
            List<int> input = tokens.ToList();
            if (input.Count == 0 || input.Any(t => t < 0)) throw new ArgumentException("Token input must be nonempty and nonnegative.", nameof(tokens));
            string model = ResolveModel(options)!;
            return InstrumentAsync("embed", model, null, () => EmbedInputAsync(input, 1, model, options, token), token);
        }
        /// <summary>Embed up to 128 pre-tokenized inputs, retaining provider input indices.</summary>
        public Task<EmbeddingResponse> EmbedTokenBatchAsync(IEnumerable<IEnumerable<int>> inputs, XAiEmbeddingOptions? options = null, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(inputs);
            List<List<int>> batch = new();
            foreach (IEnumerable<int> item in inputs)
            {
                if (item == null) throw new ArgumentException("Token batch must not contain null.", nameof(inputs));
                List<int> values = item.ToList();
                if (values.Count == 0 || values.Any(t => t < 0)) throw new ArgumentException("Each token input must be nonempty and nonnegative.", nameof(inputs));
                batch.Add(values);
            }
            if (batch.Count is < 1 or > 128) throw new ArgumentException("xAI accepts 1..128 embedding inputs.", nameof(inputs));
            string model = ResolveModel(options)!;
            return InstrumentAsync("embed", model, null, () => EmbedInputAsync(batch, batch.Count, model, options, token), token);
        }
        private Task<EmbeddingResponse> EmbedInputAsync(object input, int count, string model, EmbeddingOptions? options, CancellationToken token)
        {
            Dictionary<string, object> body = new() { ["model"] = model, ["input"] = input };
            string? encoding = PickRef<OpenAiEmbeddingOptions, string>(options, Defaults, o => o.EncodingFormat);
            if (encoding != null && encoding != "float" && encoding != "base64") throw new ArgumentException("EncodingFormat must be float or base64.");
            int? dimensions = Pick<OpenAiEmbeddingOptions, int>(options, Defaults, o => o.Dimensions);
            if (dimensions <= 0) throw new ArgumentOutOfRangeException(nameof(XAiEmbeddingOptions.Dimensions));
            if (dimensions.HasValue) body["dimensions"] = dimensions.Value;
            if (encoding != null) body["encoding_format"] = encoding;
            bool? preview = Pick<XAiEmbeddingOptions, bool>(options, Defaults, o => o.Preview);
            if (preview.HasValue) body["preview"] = preview.Value;
            string? user = PickRef<XAiEmbeddingOptions, string>(options, Defaults, o => o.User);
            if (user != null) body["user"] = user;
            return ExecutePostAsync(new EmbeddingResponse { Model = model }, "embed", XAiProtocol.Url(_Endpoint, "embeddings"), body, (json, r) => ParseEmbeddings(json, r, count), token);
        }
        private static void ParseEmbeddings(string json, EmbeddingResponse result, int expected)
        {
            JsonElement root = XAiProtocol.Parse(json);
            JsonElement? data = XAiProtocol.Property(root, "data");
            if (data?.ValueKind != JsonValueKind.Array || data.Value.GetArrayLength() != expected) { result.Error = "xAI embedding response has an invalid data count."; return; }
            result.Model = XAiProtocol.String(root, "model") ?? result.Model;
            HashSet<int> indices = new();
            foreach (JsonElement item in data.Value.EnumerateArray())
            {
                int index = item.GetProperty("index").GetInt32();
                if (index < 0 || index >= expected || !indices.Add(index)) { result.Error = "xAI embedding indices must be unique and match the inputs."; return; }
                JsonElement vector = item.GetProperty("embedding");
                float[] values;
                if (vector.ValueKind == JsonValueKind.String)
                {
                    byte[] bytes = Convert.FromBase64String(vector.GetString()!);
                    if (bytes.Length == 0 || bytes.Length % sizeof(float) != 0) { result.Error = "Invalid base64 float vector length."; return; }
                    values = new float[bytes.Length / sizeof(float)];
                    for (int i = 0; i < values.Length; i++) values[i] = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(i * 4, 4)));
                }
                else if (vector.ValueKind == JsonValueKind.Array) values = vector.EnumerateArray().Select(v => v.GetSingle()).ToArray();
                else { result.Error = "Embedding must be a float array or base64 string."; return; }
                if (values.Length == 0 || values.Any(v => !float.IsFinite(v))) { result.Error = "Embedding values must be finite and nonempty."; return; }
                result.Embeddings.Add(new EmbeddingResult { Index = index, Embedding = values });
            }
            result.Embeddings.Sort((a, b) => a.Index.CompareTo(b.Index));
            JsonElement? usage = XAiProtocol.Property(root, "usage");
            if (usage?.ValueKind == JsonValueKind.Object)
                result.Usage = new TokenUsage { PromptTokens = usage.Value.TryGetProperty("prompt_tokens", out var p) ? p.GetInt32() : null, TotalTokens = usage.Value.TryGetProperty("total_tokens", out var t) ? t.GetInt32() : null };
        }
    }
}
