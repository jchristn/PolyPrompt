namespace PolyPrompt.Models
{
    /// <summary>
    /// Response from a classification request, with one result per input in input order.
    /// </summary>
    public class ClassificationResponse : ResponseBase
    {
        #region Public-Members

        /// <summary>
        /// Classification results, one per input, in input order. Empty when the request failed.
        /// </summary>
        public List<ClassificationResult> Classifications { get; set; } = new List<ClassificationResult>();

        #endregion
    }
}
