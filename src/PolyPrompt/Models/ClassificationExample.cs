namespace PolyPrompt.Models
{
    /// <summary>
    /// A labeled example used for few-shot classification by providers that accept examples (Cohere).
    /// </summary>
    public class ClassificationExample
    {
        #region Private-Members

        private string _Text = string.Empty;
        private string _Label = string.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize an empty classification example.
        /// </summary>
        public ClassificationExample()
        {
        }

        /// <summary>
        /// Initialize a classification example.
        /// </summary>
        /// <param name="text">Example text.</param>
        /// <param name="label">Label for the example text.</param>
        /// <exception cref="ArgumentNullException">Thrown when text or label is null.</exception>
        public ClassificationExample(string text, string label)
        {
            Text = text;
            Label = label;
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// Example text. Cannot be null.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null.</exception>
        public string Text
        {
            get { return _Text; }
            set { _Text = value ?? throw new ArgumentNullException(nameof(Text)); }
        }

        /// <summary>
        /// Label for the example text. Cannot be null.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null.</exception>
        public string Label
        {
            get { return _Label; }
            set { _Label = value ?? throw new ArgumentNullException(nameof(Label)); }
        }

        #endregion
    }
}
