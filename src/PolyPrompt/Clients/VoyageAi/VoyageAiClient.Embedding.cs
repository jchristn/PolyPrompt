namespace PolyPrompt.Clients
{
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using PolyPrompt.Telemetry;
    using SyslogLogging;

    public partial class VoyageAiClient
    {

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task<EmbeddingResponse> EmbedCoreAsync(List<string> inputs, string? model, EmbeddingOptions? options, CancellationToken token)
        {
            Dictionary<string, object> body = new Dictionary<string, object>
            {
                { "model", model! },
                { "input", inputs }
            };

            string? inputType = PickRef<VoyageAiEmbeddingOptions, string>(options, Defaults.Embedding, o => o.InputType);
            bool? truncation = Pick<VoyageAiEmbeddingOptions, bool>(options, Defaults.Embedding, o => o.Truncation);
            int? outputDimension = Pick<VoyageAiEmbeddingOptions, int>(options, Defaults.Embedding, o => o.OutputDimension);
            string? outputDtype = PickRef<VoyageAiEmbeddingOptions, string>(options, Defaults.Embedding, o => o.OutputDtype);

            if (!string.IsNullOrEmpty(inputType)) body["input_type"] = inputType;
            if (truncation.HasValue) body["truncation"] = truncation.Value;
            if (outputDimension.HasValue) body["output_dimension"] = outputDimension.Value;
            if (!string.IsNullOrEmpty(outputDtype)) body["output_dtype"] = outputDtype;

            EmbeddingResponse response = new EmbeddingResponse { Model = model };
            return ExecutePostAsync(response, "embed", BuildUrl("/v1/embeddings"), body, ParseEmbeddings, token);
        }

        #endregion

        #region Private-Methods

        private void ParseEmbeddings(string responseBody, EmbeddingResponse response)
        {
            Dictionary<string, object>? responseObj = TryDeserializeObject(responseBody);
            List<Dictionary<string, object>>? data = responseObj == null ? null : ParseNestedList(responseObj, "data");
            if (data == null)
            {
                response.Error = "Response missing 'data' field";
                return;
            }

            for (int i = 0; i < data.Count; i++)
            {
                EmbeddingResult result = new EmbeddingResult { Index = TryGetInt(data[i], "index") ?? i };
                if (data[i].ContainsKey("embedding")) result.Embedding = ParseFloatArray(_Serializer.SerializeJson(data[i]["embedding"], false));
                response.Embeddings.Add(result);
            }

            response.Embeddings.Sort((a, b) => a.Index.CompareTo(b.Index));
        }

        #endregion
    }
}
