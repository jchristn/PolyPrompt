namespace PolyPrompt.Clients
{
    using System.Net.Http.Headers;
    using PolyPrompt.Auth;
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using PolyPrompt.Telemetry;
    using SyslogLogging;

    public partial class VertexAiClient
    {

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task<EmbeddingResponse> EmbedCoreAsync(List<string> inputs, string? model, EmbeddingOptions? options, CancellationToken token)
        {
            string? taskType = PickRef<VertexAiEmbeddingOptions, string>(options, Defaults.Embedding, o => o.TaskType);
            string? title = PickRef<VertexAiEmbeddingOptions, string>(options, Defaults.Embedding, o => o.Title);
            int? outputDimensionality = Pick<VertexAiEmbeddingOptions, int>(options, Defaults.Embedding, o => o.OutputDimensionality);
            bool? autoTruncate = Pick<VertexAiEmbeddingOptions, bool>(options, Defaults.Embedding, o => o.AutoTruncate);

            List<Dictionary<string, object>> instances = new List<Dictionary<string, object>>();
            foreach (string input in inputs)
            {
                Dictionary<string, object> instance = new Dictionary<string, object> { { "content", input } };
                if (!string.IsNullOrEmpty(taskType)) instance["task_type"] = taskType;
                if (!string.IsNullOrEmpty(title)) instance["title"] = title;
                instances.Add(instance);
            }

            Dictionary<string, object> body = new Dictionary<string, object> { { "instances", instances } };

            if (outputDimensionality.HasValue || autoTruncate.HasValue)
            {
                Dictionary<string, object> parameters = new Dictionary<string, object>();
                if (outputDimensionality.HasValue) parameters["outputDimensionality"] = outputDimensionality.Value;
                if (autoTruncate.HasValue) parameters["autoTruncate"] = autoTruncate.Value;
                body["parameters"] = parameters;
            }

            string url = GeminiProtocol.VertexModelUrl(_Endpoint, _Project, _Region, model!) + ":predict";
            EmbeddingResponse response = new EmbeddingResponse { Model = model };
            return ExecutePostAsync(response, "embed", url, body, ParsePredictions, token);
        }

        #endregion

        #region Private-Methods

        private void ParsePredictions(string responseBody, EmbeddingResponse response)
        {
            Dictionary<string, object>? responseObj = TryDeserializeObject(responseBody);
            List<Dictionary<string, object>>? predictions = responseObj == null ? null : ParseNestedList(responseObj, "predictions");
            if (predictions == null)
            {
                response.Error = "Response missing 'predictions' field";
                return;
            }

            for (int i = 0; i < predictions.Count; i++)
            {
                Dictionary<string, object>? embeddings = ParseNestedObject(predictions[i], "embeddings");
                if (embeddings == null || !embeddings.ContainsKey("values")) continue;

                response.Embeddings.Add(new EmbeddingResult { Index = i, Embedding = ParseFloatArray(_Serializer.SerializeJson(embeddings["values"], false)) });
            }
        }

        #endregion
    }
}
