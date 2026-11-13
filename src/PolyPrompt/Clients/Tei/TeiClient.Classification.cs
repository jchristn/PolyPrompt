namespace PolyPrompt.Clients
{
    using System.Text.Json;
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using SyslogLogging;

    public partial class TeiClient
    {

        #region Protected-Methods

        /// <inheritdoc />
        protected override bool ClassificationRequiresModel => false;

        /// <inheritdoc />
        protected override Task<ClassificationResponse> ClassifyCoreAsync(List<string> inputs, string? model, ClassificationOptions? options, CancellationToken token)
        {
            Dictionary<string, object> body = new Dictionary<string, object>
            {
                { "inputs", inputs.Select(i => new List<string> { i }).ToList() }
            };

            bool? rawScores = Pick<TeiClassificationOptions, bool>(options, Defaults.Classification, o => o.RawScores);
            bool? truncate = Pick<TeiClassificationOptions, bool>(options, Defaults.Classification, o => o.Truncate);
            string? direction = PickRef<TeiClassificationOptions, string>(options, Defaults.Classification, o => o.TruncationDirection);

            if (rawScores.HasValue) body["raw_scores"] = rawScores.Value;
            if (truncate.HasValue) body["truncate"] = truncate.Value;
            if (direction != null) body["truncation_direction"] = direction;

            ClassificationResponse response = new ClassificationResponse { Model = model };
            return ExecutePostAsync(response, "classify", BuildUrl("/predict"), body, (text, r) => ParsePredictions(text, r, inputs), token);
        }

        #endregion

        #region Private-Methods

        private void ParsePredictions(string responseBody, ClassificationResponse response, List<string> inputs)
        {
            if (!TryParseJson(responseBody, out JsonElement root) || root.ValueKind != JsonValueKind.Array)
            {
                response.Error = "Response is not a JSON array of predictions";
                return;
            }

            // A batch returns one prediction list per input. Tolerate a flat list (the single-input form).
            List<List<Dictionary<string, object>>> perInput = new List<List<Dictionary<string, object>>>();
            try
            {
                if (root.GetArrayLength() > 0 && root[0].ValueKind == JsonValueKind.Object)
                    perInput.Add(_Serializer.DeserializeJson<List<Dictionary<string, object>>>(root.GetRawText()) ?? new List<Dictionary<string, object>>());
                else
                    perInput = _Serializer.DeserializeJson<List<List<Dictionary<string, object>>>>(root.GetRawText()) ?? perInput;
            }
            catch (JsonException)
            {
                response.Error = "Response is not a JSON array of predictions";
                return;
            }

            for (int i = 0; i < perInput.Count; i++)
            {
                ClassificationResult classification = new ClassificationResult { Index = i, Input = i < inputs.Count ? inputs[i] : string.Empty };

                foreach (Dictionary<string, object> prediction in perInput[i])
                {
                    string? label = prediction.ContainsKey("label") ? prediction["label"]?.ToString() : null;
                    double? score = TryGetDouble(prediction, "score");
                    if (label != null && score.HasValue) classification.Labels.Add(new ClassificationLabel { Label = label, Score = score.Value });
                }

                classification.Labels = classification.Labels.OrderByDescending(l => l.Score).ToList();
                ClassificationLabel? top = classification.Labels.FirstOrDefault();
                classification.Label = top?.Label;
                classification.Score = top?.Score;

                response.Classifications.Add(classification);
            }
        }

        #endregion
    }
}
