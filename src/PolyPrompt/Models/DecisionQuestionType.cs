namespace PolyPrompt.Models
{
    /// <summary>
    /// The kind of answer a decision question asks for.
    /// </summary>
    public enum DecisionQuestionType
    {
        /// <summary>A yes/no question, answered with the probability that the answer is yes.</summary>
        Binary,

        /// <summary>Pick one option from a defined set, answered with the chosen option and a probability per option.</summary>
        Choice,

        /// <summary>Rate along an ordered rubric of levels, answered with a numeric score.</summary>
        Score
    }
}
