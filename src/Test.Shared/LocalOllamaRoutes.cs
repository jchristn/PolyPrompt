namespace Test.Shared
{
    using System.Net;
    using System.Text;

    /// <summary>
    /// Ollama mock routes for model loading and tool-support detection, dispatched before the built-in Ollama routes.
    /// Model names starting with <c>probe-</c> select a <c>/api/show</c> shape; <c>/api/generate</c> without a prompt is
    /// a load or unload request, and <c>probe-missing</c> answers it with 404. Uses no reflection (linked into Test.Aot).
    /// </summary>
    internal static class LocalOllamaRoutes
    {
        public static async Task<bool> TryHandleAsync(HttpListenerContext context, string path, string requestBody)
        {
            if (path == "/api/generate" && !requestBody.Contains("\"prompt\"", StringComparison.Ordinal))
            {
                if (requestBody.Contains("probe-missing", StringComparison.Ordinal))
                {
                    await WriteAsync(context, 404, "{\"error\":\"model 'probe-missing' not found\"}").ConfigureAwait(false);
                    return true;
                }

                string reason = requestBody.Contains("\"keep_alive\":0", StringComparison.Ordinal) ? "unload" : "load";
                await WriteAsync(context, 200, "{\"model\":\"test-model\",\"response\":\"\",\"done\":true,\"done_reason\":\"" + reason + "\"}").ConfigureAwait(false);
                return true;
            }

            if (path == "/api/show" && requestBody.Contains("probe-", StringComparison.Ordinal))
            {
                string body;
                if (requestBody.Contains("probe-renderer", StringComparison.Ordinal))
                    body = "{\"template\":\"{{ .Prompt }}\",\"renderer\":\"qwen3-coder\",\"parser\":\"qwen3-coder\",\"capabilities\":[\"completion\",\"tools\"]}";
                else if (requestBody.Contains("probe-gotemplate", StringComparison.Ordinal))
                    body = "{\"template\":\"{{- if .Tools }}{{ .Tools }}{{ end }}{{ .Prompt }}\",\"capabilities\":[\"completion\",\"tools\"]}";
                else if (requestBody.Contains("probe-jinja", StringComparison.Ordinal))
                    body = "{\"template\":\"{% if tools %}{{ tools }}{% endif %}\",\"capabilities\":[\"completion\",\"tools\"]}";
                else if (requestBody.Contains("probe-deepseek", StringComparison.Ordinal))
                    body = "{\"template\":\"{% set ns = namespace(is_tool=false) %}{% for message in messages %}{{ message['content'] }}{% endfor %}\",\"capabilities\":[\"completion\",\"tools\",\"thinking\"]}";
                else if (requestBody.Contains("probe-notools", StringComparison.Ordinal))
                    body = "{\"template\":\"{{ .Prompt }}\",\"renderer\":\"x\",\"capabilities\":[\"completion\"]}";
                else if (requestBody.Contains("probe-notemplate", StringComparison.Ordinal))
                    body = "{\"capabilities\":[\"completion\",\"tools\"]}";
                else
                    body = "{\"template\":\"{{ .Prompt }}\",\"license\":\"MIT\"}";

                await WriteAsync(context, 200, body).ConfigureAwait(false);
                return true;
            }

            return false;
        }

        private static async Task WriteAsync(HttpListenerContext context, int status, string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
            context.Response.Close();
        }
    }
}
