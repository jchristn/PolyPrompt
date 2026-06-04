namespace PolyPrompt.Models
{
    /// <summary>
    /// Wraps the HTTP response and body from a completion API call.
    /// </summary>
    public class CompletionHttpResult
    {
        /// <summary>
        /// The HTTP response message. The response has already been disposed when this
        /// result is returned; use StatusCode, IsSuccessStatusCode, ResponseHeaders,
        /// and ResponseBody for retained response data.
        /// </summary>
        public HttpResponseMessage? Response { get; set; }

        /// <summary>
        /// HTTP response status code.
        /// </summary>
        public int StatusCode { get; set; }

        /// <summary>
        /// Indicates whether the response status code was successful.
        /// </summary>
        public bool IsSuccessStatusCode { get; set; }

        /// <summary>
        /// Response headers captured before the HTTP response was disposed.
        /// </summary>
        public Dictionary<string, string> ResponseHeaders { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// The response body as a string.
        /// </summary>
        public string ResponseBody { get; set; } = string.Empty;
    }
}
