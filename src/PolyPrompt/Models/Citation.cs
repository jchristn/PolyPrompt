namespace PolyPrompt.Models
{
    using System.Text.Json;

    /// <summary>
    /// A provider citation, normalized across providers. Citations appear only when the provider produces them (hosted
    /// search tools, or documents with citations enabled); PolyPrompt never enables them implicitly.
    /// <see cref="TextStart"/> and <see cref="TextEnd"/> always index the response's final <c>Text</c> in .NET string
    /// units (UTF-16 code units, end exclusive); positions inside the cited source are in <see cref="Source"/>.
    /// </summary>
    public sealed class Citation
    {
        /// <summary>
        /// What kind of source is cited.
        /// </summary>
        public CitationKind Kind { get; init; }

        /// <summary>
        /// Source URL, when the source is a web page.
        /// </summary>
        public string? Url { get; init; }

        /// <summary>
        /// Source title, when the provider reports one. (xAI's citation number is not used as a title.)
        /// </summary>
        public string? Title { get; init; }

        /// <summary>
        /// The quoted source text, when the provider returns it.
        /// </summary>
        public string? CitedText { get; init; }

        /// <summary>
        /// Start of the cited span in the response's <c>Text</c> (UTF-16 code units), or null when the provider gives no span.
        /// </summary>
        public int? TextStart { get; init; }

        /// <summary>
        /// End of the cited span in the response's <c>Text</c> (UTF-16 code units, exclusive), or null when the provider gives no span.
        /// </summary>
        public int? TextEnd { get; init; }

        /// <summary>
        /// Where in the source the citation points (document or file, and a character, page, block, or chunk range), or
        /// null when the provider gives no source location.
        /// </summary>
        public CitationSourceLocation? Source { get; init; }

        /// <summary>
        /// The provider's original citation object.
        /// </summary>
        public JsonElement Raw { get; init; }
    }

    /// <summary>
    /// Kinds of cited source.
    /// </summary>
    public enum CitationKind
    {
        /// <summary>A web page.</summary>
        Url,

        /// <summary>A document supplied in the request.</summary>
        Document,

        /// <summary>A stored file (for example an OpenAI file or container file).</summary>
        File,

        /// <summary>A search result supplied in the request or returned by a tool.</summary>
        SearchResult,
    }

    /// <summary>
    /// Location of a citation inside its source. Fields the provider does not report are null.
    /// </summary>
    public sealed class CitationSourceLocation
    {
        /// <summary>
        /// Index of the cited document or search result in the request, when the provider reports one.
        /// </summary>
        public int? DocumentIndex { get; init; }

        /// <summary>
        /// Provider identifier of the cited document, file, or tool call, when reported.
        /// </summary>
        public string? DocumentId { get; init; }

        /// <summary>
        /// Title or file name of the cited document, when reported.
        /// </summary>
        public string? DocumentTitle { get; init; }

        /// <summary>
        /// Unit of <see cref="Start"/> and <see cref="End"/>: <c>char</c>, <c>page</c>, <c>block</c>, <c>chunk</c>, or
        /// <c>search_result</c>. Null when no range is reported.
        /// </summary>
        public string? Unit { get; init; }

        /// <summary>
        /// Start of the range in the source, in <see cref="Unit"/>s (pages are 1-indexed where the provider says so).
        /// </summary>
        public int? Start { get; init; }

        /// <summary>
        /// End of the range in the source, exclusive.
        /// </summary>
        public int? End { get; init; }
    }
}
