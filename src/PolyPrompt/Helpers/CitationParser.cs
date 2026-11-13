namespace PolyPrompt.Helpers
{
    using System.Text;
    using System.Text.Json;
    using PolyPrompt.Models;

    /// <summary>
    /// Normalizes provider citation shapes into <see cref="Citation"/>. Spans always index the response's final Text in
    /// UTF-16 code units, end exclusive; positions inside the cited source go in <see cref="Citation.Source"/>. Shapes
    /// follow each provider's API reference (2026-11-12); unknown members are kept in <see cref="Citation.Raw"/>.
    /// </summary>
    internal static class CitationParser
    {
        #region Anthropic

        /// <summary>
        /// Anthropic Messages: text blocks carry <c>citations[]</c>; the span is the containing block's range in Text.
        /// </summary>
        internal static List<Citation> Anthropic(JsonElement content)
        {
            List<Citation> citations = new List<Citation>();
            if (content.ValueKind != JsonValueKind.Array) return citations;
            int offset = 0;
            foreach (JsonElement block in content.EnumerateArray())
            {
                if (Str(block, "type") != "text") continue;
                string text = Str(block, "text") ?? "";
                int start = offset;
                offset += text.Length;
                if (Prop(block, "citations") is not JsonElement list || list.ValueKind != JsonValueKind.Array) continue;
                foreach (JsonElement c in list.EnumerateArray())
                {
                    string? type = Str(c, "type");
                    CitationKind kind = type switch
                    {
                        "web_search_result_location" => CitationKind.Url,
                        "search_result_location" => CitationKind.SearchResult,
                        _ => CitationKind.Document,
                    };
                    CitationSourceLocation? source = type switch
                    {
                        "char_location" => Location(c, "document_index", "document_title", "char", "start_char_index", "end_char_index"),
                        "page_location" => Location(c, "document_index", "document_title", "page", "start_page_number", "end_page_number"),
                        "content_block_location" => Location(c, "document_index", "document_title", "block", "start_block_index", "end_block_index"),
                        "search_result_location" => Location(c, "search_result_index", "title", "block", "start_block_index", "end_block_index"),
                        _ => null,
                    };
                    citations.Add(new Citation
                    {
                        Kind = kind,
                        Url = Str(c, "url") ?? Str(c, "source"),
                        Title = Str(c, "title") ?? Str(c, "document_title"),
                        CitedText = Str(c, "cited_text"),
                        TextStart = start,
                        TextEnd = offset,
                        Source = source,
                        Raw = c.Clone(),
                    });
                }
            }
            return citations;
        }

        #endregion

        #region Gemini

        /// <summary>
        /// Gemini and Vertex AI: <c>groundingMetadata</c> supports joined to grounding chunks, and <c>citationMetadata</c>
        /// sources. Segment offsets are UTF-8 bytes of the candidate's answer text and are converted to UTF-16 indices.
        /// </summary>
        internal static List<Citation> Gemini(JsonElement root)
        {
            List<Citation> citations = new List<Citation>();
            if (Prop(root, "candidates") is not JsonElement candidates || candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0) return citations;
            JsonElement candidate = candidates[0];
            string text = GeminiText(candidate);
            byte[] utf8 = Encoding.UTF8.GetBytes(text);

            if (Prop(candidate, "groundingMetadata") is JsonElement grounding && grounding.ValueKind == JsonValueKind.Object)
            {
                JsonElement? chunks = Prop(grounding, "groundingChunks");
                if (Prop(grounding, "groundingSupports") is JsonElement supports && supports.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement support in supports.EnumerateArray())
                    {
                        JsonElement segment = Prop(support, "segment") ?? default;
                        int? start = ByteToChar(utf8, Int(segment, "startIndex") ?? 0);
                        int? end = Int(segment, "endIndex") is int e ? ByteToChar(utf8, e) : null;
                        if (Prop(support, "groundingChunkIndices") is not JsonElement indices || indices.ValueKind != JsonValueKind.Array) continue;
                        foreach (JsonElement index in indices.EnumerateArray())
                        {
                            if (!index.TryGetInt32(out int i) || chunks is not JsonElement list || list.ValueKind != JsonValueKind.Array || i < 0 || i >= list.GetArrayLength()) continue;
                            JsonElement chunk = list[i];
                            JsonElement web = Prop(chunk, "web") ?? Prop(chunk, "retrievedContext") ?? default;
                            citations.Add(new Citation
                            {
                                Kind = CitationKind.Url,
                                Url = Str(web, "uri"),
                                Title = Str(web, "title"),
                                CitedText = Str(segment, "text"),
                                TextStart = start,
                                TextEnd = end,
                                Raw = support.Clone(),
                            });
                        }
                    }
                }
            }

            if (Prop(candidate, "citationMetadata") is JsonElement metadata && Prop(metadata, "citationSources") is JsonElement sources && sources.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement source in sources.EnumerateArray())
                {
                    citations.Add(new Citation
                    {
                        Kind = CitationKind.Url,
                        Url = Str(source, "uri"),
                        TextStart = Int(source, "startIndex") is int s ? ByteToChar(utf8, s) : null,
                        TextEnd = Int(source, "endIndex") is int e ? ByteToChar(utf8, e) : null,
                        Raw = source.Clone(),
                    });
                }
            }

            return citations;
        }

        private static string GeminiText(JsonElement candidate)
        {
            StringBuilder text = new StringBuilder();
            JsonElement parts = Prop(Prop(candidate, "content") ?? default, "parts") ?? default;
            if (parts.ValueKind != JsonValueKind.Array) return "";
            foreach (JsonElement part in parts.EnumerateArray())
            {
                if (Prop(part, "thought") is JsonElement thought && thought.ValueKind == JsonValueKind.True) continue;
                text.Append(Str(part, "text"));
            }
            return text.ToString();
        }

        /// <summary>
        /// Convert a UTF-8 byte offset into a UTF-16 index, clamped to the text.
        /// </summary>
        internal static int ByteToChar(byte[] utf8, int byteOffset)
        {
            int clamped = Math.Clamp(byteOffset, 0, utf8.Length);
            return Encoding.UTF8.GetCharCount(utf8, 0, clamped);
        }

        #endregion

        #region Cohere

        /// <summary>
        /// Cohere v2: <c>message.citations[]</c> with character offsets into the message text. Citations of thinking
        /// content are excluded (they index reasoning, not Text).
        /// </summary>
        internal static List<Citation> Cohere(JsonElement root)
        {
            List<Citation> citations = new List<Citation>();
            JsonElement message = Prop(root, "message") ?? default;
            if (Prop(message, "citations") is not JsonElement list || list.ValueKind != JsonValueKind.Array) return citations;
            foreach (JsonElement c in list.EnumerateArray()) AddCohere(c, citations);
            return citations;
        }

        internal static void AddCohere(JsonElement c, List<Citation> citations)
        {
            string? type = Str(c, "type");
            if (string.Equals(type, "THINKING_CONTENT", StringComparison.OrdinalIgnoreCase)) return;
            JsonElement first = Prop(c, "sources") is JsonElement sources && sources.ValueKind == JsonValueKind.Array && sources.GetArrayLength() > 0 ? sources[0] : default;
            bool tool = Str(first, "type") == "tool";
            JsonElement document = Prop(first, "document") ?? default;
            citations.Add(new Citation
            {
                Kind = tool ? CitationKind.SearchResult : CitationKind.Document,
                Title = Str(document, "title"),
                Url = Str(document, "url"),
                CitedText = Str(c, "text"),
                TextStart = Int(c, "start"),
                TextEnd = Int(c, "end"),
                Source = new CitationSourceLocation { DocumentId = Str(first, "id") },
                Raw = c.Clone(),
            });
        }

        #endregion

        #region Bedrock

        /// <summary>
        /// Bedrock Converse: <c>citationsContent</c> blocks; each citation has a location union (document char, page, or
        /// chunk, search result, or web). Spans in the response text are not reported, so TextStart/TextEnd are null.
        /// </summary>
        internal static List<Citation> Bedrock(JsonElement content)
        {
            List<Citation> citations = new List<Citation>();
            if (content.ValueKind != JsonValueKind.Array) return citations;
            foreach (JsonElement block in content.EnumerateArray())
            {
                if (Prop(block, "citationsContent") is not JsonElement cc) continue;
                if (Prop(cc, "citations") is not JsonElement list || list.ValueKind != JsonValueKind.Array) continue;
                foreach (JsonElement c in list.EnumerateArray()) citations.Add(BedrockCitation(c));
            }
            return citations;
        }

        internal static Citation BedrockCitation(JsonElement c)
        {
            JsonElement location = Prop(c, "location") ?? default;
            string? cited = null;
            if (Prop(c, "sourceContent") is JsonElement content && content.ValueKind == JsonValueKind.Array)
                cited = string.Concat(content.EnumerateArray().Select(p => Str(p, "text")));
            CitationKind kind = CitationKind.Document;
            CitationSourceLocation? source = null;
            string? url = null;
            if (Prop(location, "web") is JsonElement web) { kind = CitationKind.Url; url = Str(web, "url"); }
            else if (Prop(location, "documentChar") is JsonElement ch) source = Location(ch, "documentIndex", null, "char", "start", "end");
            else if (Prop(location, "documentPage") is JsonElement pg) source = Location(pg, "documentIndex", null, "page", "start", "end");
            else if (Prop(location, "documentChunk") is JsonElement ck) source = Location(ck, "documentIndex", null, "chunk", "start", "end");
            else if (Prop(location, "searchResultLocation") is JsonElement sr) { kind = CitationKind.SearchResult; source = Location(sr, "searchResultIndex", null, "search_result", "start", "end"); }
            return new Citation { Kind = kind, Url = url, Title = Str(c, "title"), CitedText = string.IsNullOrEmpty(cited) ? null : cited, Source = source, Raw = c.Clone() };
        }

        #endregion

        #region ChatCompletions

        /// <summary>
        /// Chat Completions: <c>choices[0].message.annotations[]</c> with nested <c>url_citation</c>. Offsets are kept as
        /// reported (whether <c>end_index</c> is inclusive is not documented).
        /// </summary>
        internal static List<Citation> ChatCompletions(JsonElement root)
        {
            List<Citation> citations = new List<Citation>();
            if (Prop(root, "choices") is not JsonElement choices || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0) return citations;
            JsonElement message = Prop(choices[0], "message") ?? default;
            if (Prop(message, "annotations") is not JsonElement list || list.ValueKind != JsonValueKind.Array) return citations;
            foreach (JsonElement a in list.EnumerateArray())
            {
                if (Str(a, "type") != "url_citation") continue;
                JsonElement u = Prop(a, "url_citation") ?? default;
                citations.Add(new Citation
                {
                    Kind = CitationKind.Url,
                    Url = Str(u, "url"),
                    Title = Str(u, "title"),
                    TextStart = Int(u, "start_index"),
                    TextEnd = Int(u, "end_index"),
                    Raw = a.Clone(),
                });
            }
            return citations;
        }

        #endregion

        #region Helpers

        private static CitationSourceLocation Location(JsonElement e, string indexName, string? titleName, string unit, string startName, string endName)
        {
            return new CitationSourceLocation
            {
                DocumentIndex = Int(e, indexName),
                DocumentTitle = titleName == null ? null : Str(e, titleName),
                Unit = unit,
                Start = Int(e, startName),
                End = Int(e, endName),
            };
        }

        internal static string? Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        internal static JsonElement? Prop(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) ? v : null;
        internal static int? Int(JsonElement e, string name) => Prop(e, name) is JsonElement v && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : null;

        #endregion
    }
}
