namespace PolyPrompt.Models
{
    /// <summary>
    /// Factories for Responses API tools only xAI accepts.
    /// </summary>
    public static class XAiTools
    {
        /// <summary><c>x_search</c>: search posts on X.</summary>
        /// <param name="parameters">Optional tool fields (for example handle or date filters).</param>
        /// <returns>The tool.</returns>
        public static ResponsesApiHostedTool XSearch(Dictionary<string, object>? parameters = null) => ResponsesApiHostedTool.Create("x_search", parameters);
    }
}
