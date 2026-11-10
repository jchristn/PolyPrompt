namespace PolyPrompt.Models
{
    /// <summary>
    /// How much of each upstream call a client records in <c>CallDetails</c>.
    /// </summary>
    public enum CallDetailCapture
    {
        /// <summary>
        /// Record nothing.
        /// </summary>
        None = 0,

        /// <summary>
        /// Record call metadata only: URL, method, headers, status, timing, success, and error. Request and response bodies
        /// are left null. For process executions, the arguments, standard input, and standard output are also left out
        /// (arguments can carry a system prompt); the executable, working directory, exit code, standard error, and
        /// failure details are kept.
        /// </summary>
        Metadata = 1,

        /// <summary>
        /// Record everything, including request and response bodies, which contain prompts and model output.
        /// </summary>
        Full = 2,
    }
}
