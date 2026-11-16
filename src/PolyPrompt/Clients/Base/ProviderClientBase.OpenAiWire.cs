namespace PolyPrompt.Clients
{
    using System.Runtime.CompilerServices;
    using System.Text.Json;
    using PolyPrompt.Models;
    using PolyPrompt.Options;

    public abstract partial class ProviderClientBase
    {
        #region OpenAi-Wire

        // OpenAI-style /embeddings, /models, and /audio/transcriptions, shared by OpenAiClient, OpenAiCompatibleClient, and
        // AzureOpenAiClient.

        /// <summary>
        /// POST an OpenAI-style <c>/audio/transcriptions</c> multipart request.
        /// </summary>
        private protected Task<TranscriptionResponse> OpenAiTranscribeAsync(string url, TranscriptionRequest request, string mediaType, string model, CancellationToken token)
        {
            List<Helpers.MultipartField> fields = new List<Helpers.MultipartField>
            {
                Helpers.MultipartField.File("file", request.Audio, request.FileName ?? DefaultAudioFileName(mediaType), mediaType),
                Helpers.MultipartField.Text("model", model),
                Helpers.MultipartField.Text("response_format", TranscriptionFormatValue(request.Format)),
            };
            if (!string.IsNullOrWhiteSpace(request.Language)) fields.Add(Helpers.MultipartField.Text("language", request.Language));
            if (!string.IsNullOrEmpty(request.Prompt)) fields.Add(Helpers.MultipartField.Text("prompt", request.Prompt));
            if (request.Temperature.HasValue) fields.Add(Helpers.MultipartField.Text("temperature", request.Temperature.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
            foreach (TranscriptionTimestampGranularity granularity in (request.TimestampGranularities ?? new List<TranscriptionTimestampGranularity>()).Distinct())
                fields.Add(Helpers.MultipartField.Text("timestamp_granularities[]", granularity == TranscriptionTimestampGranularity.Word ? "word" : "segment"));

            TranscriptionResponse response = new TranscriptionResponse { Model = model };
            TranscriptionFormat format = request.Format;
            return ExecuteMultipartAsync(response, "transcribe", url, fields, (body, r) => OpenAiParseTranscription(body, r, format), token);
        }

        /// <summary>
        /// Reject the formats OpenAI's <c>gpt-4o</c> transcription models do not support: they return <c>json</c> only
        /// (the diarizing model also <c>text</c>), without segments or timestamps; <c>whisper-1</c> supports everything.
        /// </summary>
        private protected static void OpenAiValidateTranscriptionFormat(TranscriptionRequest request, string model)
        {
            if (!model.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase) || !model.Contains("transcribe", StringComparison.OrdinalIgnoreCase)) return;
            bool diarize = model.Contains("diarize", StringComparison.OrdinalIgnoreCase);
            bool supported = request.Format == TranscriptionFormat.Json || (diarize && request.Format == TranscriptionFormat.Text);
            if (!supported || (request.TimestampGranularities != null && request.TimestampGranularities.Count > 0))
                throw new ArgumentException("Model '" + model + "' returns " + (diarize ? "json or text" : "json") + " without segments or timestamps. Use whisper-1 for VerboseJson and TimestampGranularities.", nameof(request));
        }

        private static string TranscriptionFormatValue(TranscriptionFormat format)
        {
            return format switch
            {
                TranscriptionFormat.Text => "text",
                TranscriptionFormat.VerboseJson => "verbose_json",
                _ => "json"
            };
        }

        private static string DefaultAudioFileName(string mediaType)
        {
            string subtype = mediaType.Substring(mediaType.IndexOf('/') + 1).ToLowerInvariant();
            string extension = subtype switch
            {
                "mpeg" or "mp3" => "mp3",
                "wav" or "x-wav" or "wave" or "vnd.wave" => "wav",
                "x-flac" or "flac" => "flac",
                "mp4" or "x-m4a" => "m4a",
                "quicktime" => "mov",
                _ => subtype
            };
            return "audio." + extension;
        }

        private void OpenAiParseTranscription(string body, TranscriptionResponse response, TranscriptionFormat format)
        {
            if (format == TranscriptionFormat.Text)
            {
                response.Text = body.TrimEnd('\r', '\n');
                response.RawResponse = null;
                return;
            }

            Dictionary<string, object>? root = TryDeserializeObject(body);
            if (root == null)
            {
                response.Error = "Transcription response is not a JSON object";
                return;
            }

            response.Text = root.ContainsKey("text") ? root["text"]?.ToString() : null;
            response.Language = root.ContainsKey("language") ? root["language"]?.ToString() : null;
            response.DurationSeconds = TryGetDouble(root, "duration");

            foreach (Dictionary<string, object> segment in ParseNestedList(root, "segments") ?? new List<Dictionary<string, object>>())
            {
                response.Segments.Add(new TranscriptionSegment
                {
                    Id = TryGetInt(segment, "id") ?? response.Segments.Count,
                    StartSeconds = TryGetDouble(segment, "start") ?? 0,
                    EndSeconds = TryGetDouble(segment, "end") ?? 0,
                    Text = segment.ContainsKey("text") ? segment["text"]?.ToString() ?? "" : "",
                    AverageLogProbability = TryGetDouble(segment, "avg_logprob"),
                    NoSpeechProbability = TryGetDouble(segment, "no_speech_prob")
                });
            }

            foreach (Dictionary<string, object> word in ParseNestedList(root, "words") ?? new List<Dictionary<string, object>>())
            {
                response.Words.Add(new TranscriptionWord
                {
                    Word = word.ContainsKey("word") ? word["word"]?.ToString() ?? "" : "",
                    StartSeconds = TryGetDouble(word, "start") ?? 0,
                    EndSeconds = TryGetDouble(word, "end") ?? 0
                });
            }

            Dictionary<string, object>? usage = ParseNestedObject(root, "usage");
            if (usage != null)
            {
                response.Usage = new TokenUsage
                {
                    PromptTokens = TryGetInt(usage, "input_tokens"),
                    CompletionTokens = TryGetInt(usage, "output_tokens"),
                    TotalTokens = TryGetInt(usage, "total_tokens")
                };
                if (response.DurationSeconds == null && TryGetDouble(usage, "seconds") is double seconds) response.DurationSeconds = seconds;
            }
        }

        /// <summary>
        /// POST an OpenAI-style <c>/embeddings</c> request.
        /// </summary>
        private protected Task<EmbeddingResponse> OpenAiEmbedAsync(string url, List<string> inputs, string? model, EmbeddingOptions? options, CancellationToken token)
        {
            Dictionary<string, object> body = new Dictionary<string, object>
            {
                { "model", model! },
                { "input", inputs }
            };

            string? encodingFormat = PickRef<OpenAiEmbeddingOptions, string>(options, Defaults.Embedding, o => o.EncodingFormat);
            int? dimensions = Pick<OpenAiEmbeddingOptions, int>(options, Defaults.Embedding, o => o.Dimensions);
            if (!string.IsNullOrEmpty(encodingFormat)) body["encoding_format"] = encodingFormat;
            if (dimensions.HasValue) body["dimensions"] = dimensions.Value;

            EmbeddingResponse response = new EmbeddingResponse { Model = model };
            return ExecutePostAsync(response, "embed", url, body, OpenAiParseEmbeddings, token);
        }

        /// <summary>
        /// GET an OpenAI-style <c>/models</c> list.
        /// </summary>
        private protected async IAsyncEnumerable<ModelInformation> OpenAiListModelsAsync(string url, [EnumeratorCancellation] CancellationToken token)
        {
            _Logging.Debug(_Header + "GET " + StripQuery(url));

            HttpCallResult result = await GetAndRecordAsync(url, token).ConfigureAwait(false);
            if (!result.IsSuccessStatusCode)
            {
                _Logging.Warn(_Header + "list models failed with status " + result.StatusCode);
                yield break;
            }

            Dictionary<string, object>? responseObj = TryDeserializeObject(result.ResponseBody);
            List<Dictionary<string, object>>? data = responseObj == null ? null : ParseNestedList(responseObj, "data");
            if (data == null) yield break;

            foreach (Dictionary<string, object> modelObj in data)
            {
                token.ThrowIfCancellationRequested();
                yield return OpenAiParseModel(modelObj, string.Empty);
            }
        }

        /// <summary>
        /// GET an OpenAI-style <c>/models/{id}</c>.
        /// </summary>
        private protected async Task<ModelInformation?> OpenAiGetModelAsync(string url, string model, CancellationToken token)
        {
            _Logging.Debug(_Header + "GET " + StripQuery(url));

            try
            {
                HttpCallResult result = await GetAndRecordAsync(url, token).ConfigureAwait(false);
                if (!result.IsSuccessStatusCode)
                {
                    _Logging.Warn(_Header + "get model failed with status " + result.StatusCode + ": " + result.ResponseBody);
                    return null;
                }

                Dictionary<string, object>? responseObj = TryDeserializeObject(result.ResponseBody);
                return responseObj == null ? null : OpenAiParseModel(responseObj, model);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "get model failed: " + ex.Message);
                return null;
            }
        }

        private void OpenAiParseEmbeddings(string responseBody, EmbeddingResponse response)
        {
            Dictionary<string, object>? responseObj = TryDeserializeObject(responseBody);
            if (responseObj == null || !responseObj.ContainsKey("data"))
            {
                response.Error = "Response missing 'data' field";
                return;
            }

            List<Dictionary<string, object>>? data = ParseNestedList(responseObj, "data");
            if (data == null) return;

            for (int i = 0; i < data.Count; i++)
            {
                Dictionary<string, object> item = data[i];
                EmbeddingResult result = new EmbeddingResult();
                result.Index = TryGetInt(item, "index") ?? i;

                if (item.ContainsKey("embedding") && item["embedding"] is JsonElement vector)
                {
                    result.Embedding = vector.ValueKind == JsonValueKind.String
                        ? OpenAiDecodeBase64Floats(vector.GetString())
                        : ParseFloatArray(vector.GetRawText());
                }

                response.Embeddings.Add(result);
            }

            response.Embeddings.Sort((a, b) => a.Index.CompareTo(b.Index));
        }

        private static float[] OpenAiDecodeBase64Floats(string? base64)
        {
            if (string.IsNullOrEmpty(base64)) return Array.Empty<float>();
            byte[] bytes = Convert.FromBase64String(base64);
            float[] values = new float[bytes.Length / sizeof(float)];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = BitConverter.ToSingle(bytes, i * sizeof(float));
            }
            return values;
        }

        private static ModelInformation OpenAiParseModel(Dictionary<string, object> modelObj, string fallbackName)
        {
            ModelInformation info = new ModelInformation();
            info.Name = modelObj.ContainsKey("id") ? modelObj["id"]?.ToString() ?? fallbackName : fallbackName;
            info.OwnedBy = modelObj.ContainsKey("owned_by") ? modelObj["owned_by"]?.ToString() : null;

            long? created = TryGetLong(modelObj, "created");
            if (created.HasValue) info.CreatedUtc = DateTimeOffset.FromUnixTimeSeconds(created.Value).UtcDateTime;

            if (modelObj.ContainsKey("object")) info.Metadata["object"] = modelObj["object"]?.ToString();
            return info;
        }

        #endregion
    }
}
