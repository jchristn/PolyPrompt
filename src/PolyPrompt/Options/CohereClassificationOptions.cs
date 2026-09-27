namespace PolyPrompt.Options
{
    using PolyPrompt.Models;

    /// <summary>
    /// Cohere-specific options for classification requests.
    /// These map to fields on the Cohere /v1/classify request body.
    /// </summary>
    public class CohereClassificationOptions : ClassificationOptions
    {
        #region Private-Members

        private List<ClassificationExample> _Examples = new List<ClassificationExample>();
        private string? _Truncate = null;

        private static readonly HashSet<string> _TruncateValues =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "NONE", "START", "END" };

        #endregion

        #region Public-Members

        /// <summary>
        /// Labeled examples for few-shot classification (examples). Required unless the request targets a
        /// fine-tuned classification model; Cohere requires at least 2 examples per label and accepts at most
        /// 2,500. Setting null clears the list. Default: empty.
        /// </summary>
        public List<ClassificationExample> Examples
        {
            get { return _Examples; }
            set { _Examples = value ?? new List<ClassificationExample>(); }
        }

        /// <summary>
        /// How over-length inputs are handled (truncate). Valid values are "NONE", "START", and "END"; values
        /// are normalized (trimmed, upper-cased) and an unrecognized value reverts to null. Null uses the
        /// provider default ("END").
        /// </summary>
        public string? Truncate
        {
            get { return _Truncate; }
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    _Truncate = null;
                    return;
                }

                string normalized = value.Trim().ToUpperInvariant();
                _Truncate = _TruncateValues.Contains(normalized) ? normalized : null;
            }
        }

        #endregion
    }
}
