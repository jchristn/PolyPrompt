namespace PolyPrompt.Clients
{
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Text;
    using PolyPrompt.Models;
    using PolyPrompt.Telemetry;
    using SyslogLogging;

    public partial class OllamaClient
    {
        #region Private-Members

        private TimeSpan _PullTimeout = TimeSpan.FromMinutes(30);

        #endregion

        #region Public-Members

        /// <summary>
        /// Maximum time a <see cref="PullModelAsync"/> call may take, since downloads can be far longer than
        /// <see cref="ClientBase.TimeoutMs"/>. Must be greater than zero. Default: 30 minutes.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when set to zero or less.</exception>
        public TimeSpan PullTimeout
        {
            get { return _PullTimeout; }
            set
            {
                if (value <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(PullTimeout), "PullTimeout must be greater than zero.");
                _PullTimeout = value;
            }
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        protected override async IAsyncEnumerable<ModelInformation> ListModelsCoreAsync([EnumeratorCancellation] CancellationToken token)
        {
            string url = BuildUrl(OllamaProtocol.TagsPath);
            _Logging.Debug(_Header + "GET " + url);

            HttpCallResult result = await GetAndRecordAsync(url, token).ConfigureAwait(false);
            if (!result.IsSuccessStatusCode)
            {
                _Logging.Warn(_Header + "list models failed with status " + result.StatusCode);
                yield break;
            }

            Dictionary<string, object>? responseObj = TryDeserializeObject(result.ResponseBody);
            List<Dictionary<string, object>>? models = responseObj == null ? null : ParseNestedList(responseObj, "models");
            if (models == null) yield break;

            foreach (Dictionary<string, object> modelObj in models)
            {
                token.ThrowIfCancellationRequested();

                ModelInformation info = new ModelInformation();
                info.Name = modelObj.ContainsKey("model") ? modelObj["model"]?.ToString() ?? "" : "";
                info.DisplayName = modelObj.ContainsKey("name") ? modelObj["name"]?.ToString() : null;
                info.ModifiedUtc = ParseTimestamp(modelObj, "modified_at");
                info.SizeBytes = TryGetLong(modelObj, "size");

                if (modelObj.ContainsKey("digest")) info.Metadata["digest"] = modelObj["digest"]?.ToString();

                Dictionary<string, object>? details = ParseNestedObject(modelObj, "details");
                if (details != null)
                {
                    CopyString(details, info, "parameter_size");
                    CopyString(details, info, "quantization_level");
                    CopyString(details, info, "family");
                    CopyString(details, info, "format");
                }

                yield return info;
            }
        }

        /// <summary>
        /// Download a model, reporting progress as the server streams it. Uses <see cref="PullTimeout"/> rather than
        /// <see cref="ClientBase.TimeoutMs"/>.
        /// </summary>
        /// <param name="model">The model name to pull, for example gemma3:4b.</param>
        /// <param name="progress">Callback invoked with each progress update. May be null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the server reported success, false on an HTTP error, a transport failure, or a stream
        /// that ended without success.</returns>
        /// <exception cref="ArgumentNullException">Thrown when model is null, empty, or whitespace.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the token is cancelled or the pull times out.</exception>
        public async Task<bool> PullModelAsync(string model, Func<ModelPullProgress, Task>? progress = null, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(model)) throw new ArgumentNullException(nameof(model));

            return await InstrumentBoolAsync(PolyPromptTelemetryNames.OperationModelPull, model, async () =>
            {
                string url = BuildUrl("/api/pull");
                string json = _Serializer.SerializeJson(new Dictionary<string, object> { { "name", model }, { "stream", true } }, false);

                _Logging.Debug(_Header + "POST " + url + " (pull " + model + ")");
                HttpCallScope? http = null;

                try
                {
                    using CancellationTokenSource timeoutCts = new CancellationTokenSource(_PullTimeout);
                    using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

                    using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url);
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    byte[] body = Encoding.UTF8.GetBytes(json);
                    await PrepareRequestAsync(request, body, linkedCts.Token).ConfigureAwait(false);
                    http = HttpCallScope.Start(TelemetryProvider, "POST", url, body.Length);

                    using HttpResponseMessage response = await _HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token).ConfigureAwait(false);
                    http?.SetStatus((int)response.StatusCode);

                    if (!response.IsSuccessStatusCode)
                    {
                        string errorBody = await response.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);
                        http?.End();
                        _Logging.Warn(_Header + "pull request failed with status " + (int)response.StatusCode + ": " + errorBody);
                        return false;
                    }

                    using Stream stream = await response.Content.ReadAsStreamAsync(linkedCts.Token).ConfigureAwait(false);
                    using StreamReader reader = new StreamReader(stream);

                    bool success = false;
                    string? line;
                    while ((line = await reader.ReadLineAsync(linkedCts.Token).ConfigureAwait(false)) != null)
                    {
                        linkedCts.Token.ThrowIfCancellationRequested();
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        Dictionary<string, object>? chunk = TryDeserializeObject(line);
                        if (chunk == null) continue;

                        string status = chunk.ContainsKey("status") ? chunk["status"]?.ToString() ?? "" : "";
                        if (string.Equals(status, "success", StringComparison.OrdinalIgnoreCase)) success = true;

                        if (progress != null)
                        {
                            ModelPullProgress update = new ModelPullProgress();
                            update.Status = status;
                            update.Digest = chunk.ContainsKey("digest") ? chunk["digest"]?.ToString() : null;
                            update.TotalBytes = TryGetLong(chunk, "total");
                            update.CompletedBytes = TryGetLong(chunk, "completed");
                            await progress(update).ConfigureAwait(false);
                        }
                    }

                    http?.End();
                    return success;
                }
                catch (OperationCanceledException ex)
                {
                    http?.Fail(ex, token);
                    throw;
                }
                catch (Exception ex)
                {
                    http?.Fail(ex, token);
                    _Logging.Warn(_Header + "pull request failed: " + ex.Message);
                    return false;
                }
            }, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Remove a model from the server.
        /// </summary>
        /// <param name="model">The model name to delete.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the server deleted the model, false on an HTTP error (for example, the model does not
        /// exist) or a transport failure.</returns>
        /// <exception cref="ArgumentNullException">Thrown when model is null, empty, or whitespace.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the token is cancelled or the request times out.</exception>
        public async Task<bool> DeleteModelAsync(string model, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(model)) throw new ArgumentNullException(nameof(model));

            return await InstrumentBoolAsync(PolyPromptTelemetryNames.OperationModelDelete, model, async () =>
            {
                string url = BuildUrl("/api/delete");
                string json = _Serializer.SerializeJson(new Dictionary<string, object> { { "model", model } }, false);

                _Logging.Debug(_Header + "DELETE " + url + " (model " + model + ")");

                try
                {
                    HttpCallResult result = await DeleteAndRecordAsync(url, json, token).ConfigureAwait(false);
                    if (result.IsSuccessStatusCode) return true;

                    _Logging.Warn(_Header + "delete request failed with status " + result.StatusCode + ": " + result.ResponseBody);
                    return false;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "delete request failed: " + ex.Message);
                    return false;
                }
            }, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Load a model into memory so the first real request does not wait for it, by sending Ollama an empty
        /// <c>/api/generate</c> request. Use this for chat and generation models; Ollama loads an embedding model on its
        /// first <c>/api/embed</c> call.
        /// </summary>
        /// <param name="model">The model name to load, for example gemma3:4b.</param>
        /// <param name="keepAlive">How long the model stays loaded afterwards; see
        /// <see cref="Options.OllamaCompletionOptions.KeepAlive"/>. A negative value keeps it loaded until the server stops.
        /// Null uses the server default (5 minutes unless <c>OLLAMA_KEEP_ALIVE</c> is set).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the server loaded the model, false on an HTTP error (for example, the model is not pulled)
        /// or a transport failure.</returns>
        /// <exception cref="ArgumentNullException">Thrown when model is null, empty, or whitespace.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the token is cancelled or the request times out.</exception>
        public Task<bool> LoadModelAsync(string model, TimeSpan? keepAlive = null, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(model)) throw new ArgumentNullException(nameof(model));
            return SendKeepAliveAsync(PolyPromptTelemetryNames.OperationModelLoad, "load", model, keepAlive, token);
        }

        /// <summary>
        /// Unload a model from memory now, freeing its RAM or VRAM, by sending Ollama an empty <c>/api/generate</c> request
        /// with <c>keep_alive</c> 0. The model stays on disk.
        /// </summary>
        /// <param name="model">The model name to unload.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the server accepted the request, false on an HTTP error or a transport failure.</returns>
        /// <exception cref="ArgumentNullException">Thrown when model is null, empty, or whitespace.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the token is cancelled or the request times out.</exception>
        public Task<bool> UnloadModelAsync(string model, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(model)) throw new ArgumentNullException(nameof(model));
            return SendKeepAliveAsync(PolyPromptTelemetryNames.OperationModelUnload, "unload", model, TimeSpan.Zero, token);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ModelInformation?> GetModelInformationCoreAsync(string model, CancellationToken token)
        {
            string url = BuildUrl("/api/show");
            _Logging.Debug(_Header + "POST " + url + " (show " + model + ")");

            try
            {
                HttpCallResult result = await PostJsonAsync(url, new Dictionary<string, object> { { "model", model } }, token).ConfigureAwait(false);
                if (!result.IsSuccessStatusCode)
                {
                    _Logging.Warn(_Header + "show request failed with status " + result.StatusCode + ": " + result.ResponseBody);
                    return null;
                }

                Dictionary<string, object>? responseObj = TryDeserializeObject(result.ResponseBody);
                if (responseObj == null) return null;

                ModelInformation info = new ModelInformation();
                info.Name = model;
                info.ModifiedUtc = ParseTimestamp(responseObj, "modified_at");

                Dictionary<string, object>? details = ParseNestedObject(responseObj, "details");
                if (details != null)
                {
                    CopyString(details, info, "parameter_size");
                    CopyString(details, info, "quantization_level");
                    CopyString(details, info, "family");
                    CopyString(details, info, "format");
                    CopyString(details, info, "parent_model");
                    if (details.ContainsKey("families")) info.Metadata["families"] = _Serializer.SerializeJson(details["families"], false);
                }

                CopyString(responseObj, info, "license");
                CopyString(responseObj, info, "template");
                CopyString(responseObj, info, "parameters");
                CopyString(responseObj, info, "renderer");
                CopyString(responseObj, info, "parser");

                List<string>? capabilities = null;
                if (responseObj.ContainsKey("capabilities") && responseObj["capabilities"] != null)
                {
                    string capabilitiesJson = _Serializer.SerializeJson(responseObj["capabilities"], false);
                    info.Metadata["capabilities"] = capabilitiesJson;
                    capabilities = ParseStringList(capabilitiesJson);
                }

                info.SupportsTools = DetectToolSupport(
                    capabilities,
                    info.Metadata.TryGetValue("template", out string? template) ? template : null,
                    info.Metadata.TryGetValue("renderer", out string? renderer) ? renderer : null);

                return info;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "show request failed: " + ex.Message);
                return null;
            }
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Decide whether an Ollama model can actually use tools. Ollama lists the <c>tools</c> capability for some models
        /// whose prompt template drops tools without a word (deepseek-r1 builds, for one), so the listing alone is not
        /// enough: the model also needs a built-in renderer, or a template that references tools (a Go template using
        /// <c>.Tools</c> or a Jinja template using <c>tools</c>). Returns null when the server reports no capabilities.
        /// </summary>
        internal static bool? DetectToolSupport(List<string>? capabilities, string? template, string? renderer)
        {
            if (capabilities == null) return null;
            bool listed = capabilities.Exists(c => string.Equals(c, "tools", StringComparison.OrdinalIgnoreCase));
            if (!listed) return false;
            if (!string.IsNullOrWhiteSpace(renderer) || template == null) return true;
            return template.Contains(".Tools", StringComparison.Ordinal) || template.Contains("tools", StringComparison.Ordinal);
        }

        private List<string>? ParseStringList(string json)
        {
            if (!TryParseJson(json, out System.Text.Json.JsonElement element) || element.ValueKind != System.Text.Json.JsonValueKind.Array) return null;
            List<string> values = new List<string>();
            foreach (System.Text.Json.JsonElement item in element.EnumerateArray())
            {
                if (item.ValueKind == System.Text.Json.JsonValueKind.String) values.Add(item.GetString() ?? "");
            }
            return values;
        }

        private async Task<bool> SendKeepAliveAsync(string operation, string verb, string model, TimeSpan? keepAlive, CancellationToken token)
        {
            return await InstrumentBoolAsync(operation, model, async () =>
            {
                string url = BuildUrl("/api/generate");
                Dictionary<string, object> body = new Dictionary<string, object> { { "model", model }, { "stream", false } };
                if (keepAlive.HasValue) body["keep_alive"] = OllamaProtocol.KeepAliveSeconds(keepAlive.Value);

                _Logging.Debug(_Header + "POST " + url + " (" + verb + " " + model + ")");

                try
                {
                    HttpCallResult result = await PostJsonAsync(url, body, token).ConfigureAwait(false);
                    if (result.IsSuccessStatusCode) return true;

                    _Logging.Warn(_Header + verb + " request failed with status " + result.StatusCode + ": " + result.ResponseBody);
                    return false;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + verb + " request failed: " + ex.Message);
                    return false;
                }
            }, token).ConfigureAwait(false);
        }

        private static void CopyString(Dictionary<string, object> source, ModelInformation info, string key)
        {
            if (source.ContainsKey(key)) info.Metadata[key] = source[key]?.ToString();
        }

        private static DateTime? ParseTimestamp(Dictionary<string, object> source, string key)
        {
            if (!source.ContainsKey(key)) return null;
            string? value = source[key]?.ToString();
            if (!string.IsNullOrEmpty(value) && DateTime.TryParse(value, out DateTime parsed)) return parsed.ToUniversalTime();
            return null;
        }

        #endregion
    }
}
