namespace PolyPrompt.Clients
{
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using SyslogLogging;

    public partial class CohereClient
    {

        #region Protected-Methods

        /// <inheritdoc />
        protected override bool ClassificationRequiresModel => false;

        /// <inheritdoc />
        protected override Task<ClassificationResponse> ClassifyCoreAsync(List<string> inputs, string? model, ClassificationOptions? options, CancellationToken token)
        {
            Dictionary<string, object> body = new Dictionary<string, object> { { "inputs", inputs } };
            if (!string.IsNullOrWhiteSpace(model)) body["model"] = model;

            // Per-call examples replace the default examples when any are given.
            List<ClassificationExample> examples = (options as CohereClassificationOptions)?.Examples is { Count: > 0 } callExamples
                ? callExamples
                : Defaults.Classification.Examples;
            if (examples.Count > 0)
            {
                body["examples"] = examples.Select(e => new Dictionary<string, object> { { "text", e.Text }, { "label", e.Label } }).ToList();
            }

            string? truncate = PickRef<CohereClassificationOptions, string>(options, Defaults.Classification, o => o.Truncate);
            if (truncate != null) body["truncate"] = truncate;

            ClassificationResponse response = new ClassificationResponse { Model = model };
            return ExecutePostAsync(response, "classify", BuildUrl("/v1/classify"), body, (text, r) => ParseClassifications(text, r, inputs), token);
        }

        #endregion

        #region Private-Methods

        private void ParseClassifications(string responseBody, ClassificationResponse response, List<string> inputs)
        {
            Dictionary<string, object>? responseObj = TryDeserializeObject(responseBody);
            List<Dictionary<string, object>>? classifications = responseObj == null ? null : ParseNestedList(responseObj, "classifications");
            if (classifications == null)
            {
                response.Error = "Response missing 'classifications' field";
                return;
            }

            for (int i = 0; i < classifications.Count; i++)
            {
                response.Classifications.Add(ParseClassification(classifications[i], i, inputs));
            }
        }

        private ClassificationResult ParseClassification(Dictionary<string, object> classificationObj, int index, List<string> inputs)
        {
            ClassificationResult classification = new ClassificationResult();
            classification.Index = index;
            classification.Input = classificationObj.ContainsKey("input") && classificationObj["input"] != null
                ? classificationObj["input"]?.ToString() ?? string.Empty
                : (index < inputs.Count ? inputs[index] : string.Empty);

            Dictionary<string, object>? labels = ParseNestedObject(classificationObj, "labels");
            if (labels != null)
            {
                foreach (string label in labels.Keys)
                {
                    Dictionary<string, object>? labelObj = ParseNestedObject(labels, label);
                    double? confidence = labelObj != null ? TryGetDouble(labelObj, "confidence") : null;
                    if (confidence.HasValue) classification.Labels.Add(new ClassificationLabel { Label = label, Score = confidence.Value });
                }
            }

            classification.Labels = classification.Labels.OrderByDescending(l => l.Score).ToList();

            ClassificationLabel? top = classification.Labels.FirstOrDefault();
            if (top != null)
            {
                classification.Label = top.Label;
                classification.Score = top.Score;
            }
            else
            {
                // Fall back to the deprecated single-label fields when no label map was returned.
                classification.Label = classificationObj.ContainsKey("prediction") ? classificationObj["prediction"]?.ToString() : null;
                classification.Score = TryGetDouble(classificationObj, "confidence");
            }

            return classification;
        }

        #endregion
    }
}
