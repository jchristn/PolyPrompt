namespace PolyPrompt.Clients
{
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Text;
    using PolyPrompt.Models;
    using SyslogLogging;

    /// <summary>
    /// Ollama model client: list local models (<c>/api/tags</c>), look one up (<c>/api/show</c>), and, unlike other
    /// providers, download (<c>/api/pull</c>) and remove (<c>/api/delete</c>) models.
    /// </summary>
    public class OllamaModelClient : ModelClientBase
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

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new Ollama model client.
        /// </summary>
        /// <param name="endpoint">Ollama server endpoint URL. Default: http://localhost:11434.</param>
        /// <param name="apiKey">Optional bearer token, for Ollama servers behind an authenticating proxy. Default: null.</param>
        /// <param name="logging">Logging module. Default: new instance.</param>
        /// <param name="httpClient">Optional HTTP client; see <see cref="ClientBase"/>. Default: null.</param>
        public OllamaModelClient(
            string endpoint = OllamaProtocol.DefaultEndpoint,
            string? apiKey = null,
            LoggingModule? logging = null,
            HttpClient? httpClient = null)
            : base(endpoint, apiKey, logging ?? new LoggingModule(), httpClient)
        {
            _Header = OllamaProtocol.Header;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override Task<bool> ValidateConnectivityAsync(CancellationToken token = default)
        {
            return ProbeAsync(BuildUrl(OllamaProtocol.TagsPath), token);
        }

        /// <inheritdoc />
        public override async IAsyncEnumerable<ModelInformation> ListModelsAsync([EnumeratorCancellation] CancellationToken token = default)
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

            string url = BuildUrl("/api/pull");
            string json = _Serializer.SerializeJson(new Dictionary<string, object> { { "name", model }, { "stream", true } }, false);

            _Logging.Debug(_Header + "POST " + url + " (pull " + model + ")");

            try
            {
                using CancellationTokenSource timeoutCts = new CancellationTokenSource(_PullTimeout);
                using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

                using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                await PrepareRequestAsync(request, Encoding.UTF8.GetBytes(json), linkedCts.Token).ConfigureAwait(false);

                using HttpResponseMessage response = await _HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    string errorBody = await response.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);
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

                return success;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "pull request failed: " + ex.Message);
                return false;
            }
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
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task PrepareRequestAsync(HttpRequestMessage request, byte[] body, CancellationToken token)
        {
            OllamaProtocol.ApplyAuth(request, _ApiKey);
            return Task.CompletedTask;
        }

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
                if (responseObj.ContainsKey("capabilities")) info.Metadata["capabilities"] = _Serializer.SerializeJson(responseObj["capabilities"], false);

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
