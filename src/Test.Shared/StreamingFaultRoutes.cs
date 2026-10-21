namespace Test.Shared
{
    using System.Net;
    using System.Text;

    internal static class StreamingFaultRoutes
    {
        internal static async Task<bool> TryHandleAsync(HttpListenerContext context, string path, string body, CancellationToken token)
        {
            string? scenario = context.Request.Headers["x-stream-fault"];
            if (scenario == null) return false;
            bool generate = path.EndsWith("generate", StringComparison.Ordinal);
            string part = generate ? "{\"response\":\"hello\",\"done\":false}" :
                "{\"message\":{\"content\":\"hello\",\"thinking\":\"thought\",\"tool_calls\":[{\"function\":{\"name\":\"probe\",\"arguments\":{\"value\":1}}}]},\"done\":false}";
            string done = "{\"done\":true,\"done_reason\":\"stop\",\"eval_count\":2,\"prompt_eval_count\":3}";
            string error = "{\"error\":\"synthetic provider failure\"}";
            string malformed = "{\"done\":tr";
            string payload = scenario switch
            {
                "complete" => part + "\n" + done + "\n",
                "complete-no-newline" => part + "\n" + done,
                "terminal-only" => done,
                "missing-done" or "consumer-abandons" or "cancel-after-partial" or "transport-cut" => part + "\n",
                "empty" => "",
                "whitespace" => " \n\r\n",
                "truncated-first" => malformed,
                "partial-truncated" => part + "\n" + malformed,
                "malformed-then-done" => part + "\n" + malformed + "\n" + done,
                "error-first" => error,
                "error-secret" => "{\"error\":\"synthetic-cloud-token failure\"}",
                "error-long" => "{\"error\":\"" + new string('x', 5000) + "\"}",
                "invalid-utf8" => done,
                "error-after-partial" => part + "\n" + error,
                "error-then-done" => part + "\n" + error + "\n" + done,
                "nonobject" => "[]\nnull\n\"wrong\"\n",
                "missing-done-property" => "{\"message\":{\"content\":\"hello\"}}",
                "done-string-true" => "{\"done\":\"true\"}",
                "after-terminal" => part + "\n" + done + "\n" + part,
                "duplicate-terminal" => part + "\n" + done + "\n" + done,
                "http-401" or "http-500" => error,
                _ => throw new InvalidOperationException("Unknown streaming fixture: " + scenario)
            };
            context.Response.StatusCode = scenario == "http-401" ? 401 : scenario == "http-500" ? 500 : 200;
            context.Response.ContentType = "application/x-ndjson";
            byte[] bytes = scenario == "invalid-utf8" ? new byte[] { 0xff, 0x0a } : Encoding.UTF8.GetBytes(payload);
            if (scenario == "transport-cut") context.Response.ContentLength64 = bytes.Length + 4096;
            await context.Response.OutputStream.WriteAsync(bytes, token).ConfigureAwait(false);
            await context.Response.OutputStream.FlushAsync(token).ConfigureAwait(false);
            if (scenario == "cancel-after-partial") await Task.Delay(500, token).ConfigureAwait(false);
            if (scenario == "transport-cut") context.Response.Abort();
            else context.Response.Close();
            return true;
        }
    }
}
