namespace PolyPrompt.Models
{
    /// <summary>
    /// Wraps the HTTP response and body from a completion API call.
    /// </summary>
    public class CompletionHttpResult
    {
        /// <summary>
        /// The HTTP response message.
        /// </summary>
        public HttpResponseMessage Response { get; set; } = null!;

        /// <summary>
        /// The response body as a string.
        /// </summary>
        public string ResponseBody { get; set; } = string.Empty;
    }
}
