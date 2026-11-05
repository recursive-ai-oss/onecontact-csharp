using RecursiveAI.OneContact.Extraction.Markup;
using RecursiveAI.OneContact.Extraction.Records;
using RecursiveAI.OneHtml;

namespace RecursiveAI.OneContact.Extraction;

/// <summary>
/// Finds people and organizations in a web page. This version reads the page's structured markup (JSON-LD, microdata, RDFa Lite,
/// h-card, <c>mailto:</c> and <c>tel:</c> links, <c>address</c> elements); the visible-text half arrives later and is selected by
/// <see cref="ExtractionSources.Text"/>, which is ignored for now.
/// </summary>
/// <remarks>
/// <para>
/// Every field of every record carries its source, the offsets of the text it was read from, a confidence, the raw and the decoded
/// value, and the value normalized by the core types. Conflicting values are all kept. Nothing is fetched and nothing is stored: the
/// caller is responsible, under data-protection law (GDPR included), for what it extracts and does with the result.
/// </para>
/// <para>
/// Offsets are bytes of the original input when it was given as bytes, and UTF-16 code units of the original string when it was a
/// string (see <see cref="ExtractResult.OffsetUnit"/>). A <see cref="Document"/> parsed from a string by OneHtml has already had its
/// newlines normalized, so its offsets are into that normalized text.
/// </para>
/// <para>Instances are immutable and safe to share between threads.</para>
/// </remarks>
public sealed class ContactExtractor
{
    /// <summary>The readers to run. The default is every markup reader.</summary>
    public ExtractionSources Sources { get; init; } = ExtractionSources.Markup;

    /// <summary>
    /// The region to read national phone numbers and free-text addresses in when neither the record nor the page's <c>lang</c> names
    /// one. The default (<see cref="Region.Unknown"/>) means such values stay unparsed, with the reason on the field, rather than
    /// being read in a guessed country.
    /// </summary>
    public Region DefaultRegion { get; init; }

    /// <summary>Extracts the records of an HTML string with every markup reader.</summary>
    /// <param name="html">The page.</param>
    /// <returns>The records, in the order their markup begins. Fields not tied to a record are not included; use an instance for the full <see cref="ExtractResult"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="html"/> is null.</exception>
    /// <exception cref="HtmlLimitExceededException">The page exceeds OneHtml's default parse limits.</exception>
    public static IReadOnlyList<ContactRecord> Extract(string html) => new ContactExtractor().ExtractHtml(html).Records;

    /// <summary>Extracts the records of an HTML page given as bytes with every markup reader. The encoding is sniffed as a browser would.</summary>
    /// <param name="html">The page bytes; kept by reference while the call runs.</param>
    /// <returns>The records, in the order their markup begins.</returns>
    /// <exception cref="HtmlLimitExceededException">The page exceeds OneHtml's default parse limits.</exception>
    public static IReadOnlyList<ContactRecord> Extract(ReadOnlyMemory<byte> html) => new ContactExtractor().ExtractHtml(html).Records;

    /// <summary>Extracts from an HTML string.</summary>
    /// <param name="html">The page.</param>
    /// <returns>The records, every field flat, and what the readers could not read. Offsets count UTF-16 code units of <paramref name="html"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="html"/> is null.</exception>
    /// <exception cref="HtmlLimitExceededException">The page exceeds OneHtml's default parse limits.</exception>
    public ExtractResult ExtractHtml(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        Document document = Document.Parse(html);
        return Run(new PageContext(document, html));
    }

    /// <summary>Extracts from an HTML page given as bytes.</summary>
    /// <param name="html">The page bytes; kept by reference while the call runs.</param>
    /// <param name="contentType">The transport <c>Content-Type</c>, if known, as a hint for the encoding; null for none.</param>
    /// <returns>The result. Offsets are byte offsets into <paramref name="html"/>.</returns>
    /// <exception cref="HtmlLimitExceededException">The page exceeds OneHtml's default parse limits.</exception>
    public ExtractResult ExtractHtml(ReadOnlyMemory<byte> html, string? contentType = null)
    {
        Document document = Document.Parse(html, new ParseOptions { ContentType = contentType });
        return Run(new PageContext(document, null, bytesInput: true));
    }

    /// <summary>Extracts from a document OneHtml has parsed.</summary>
    /// <param name="document">The document. Offsets are bytes when it was parsed from bytes, otherwise UTF-16 code units of its normalized text.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is null.</exception>
    public ExtractResult Extract(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Run(new PageContext(document, null));
    }

    private ExtractResult Run(PageContext page) => ExtractionPipeline.Run(page, Sources, DefaultRegion);
}
