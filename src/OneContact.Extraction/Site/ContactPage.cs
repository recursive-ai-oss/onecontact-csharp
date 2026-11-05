using RecursiveAI.OneContact.Extraction.Markup;
using RecursiveAI.OneHtml;

namespace RecursiveAI.OneContact.Extraction;

/// <summary>
/// One page of a site handed to the extractor: the caller's id for it (normally its URL, or any label that is unique in the set)
/// and its content. Nothing is fetched; the extractor reads exactly what the caller supplies.
/// </summary>
/// <remarks>
/// The id is only a label. It is never parsed or resolved, and it is the identity every field read from the page carries
/// (<see cref="Records.ExtractedField.PageId"/>). Ids must be unique within one site, compared ordinally.
/// </remarks>
public readonly struct ContactPage
{
    private readonly string? _text;
    private readonly ReadOnlyMemory<byte> _bytes;
    private readonly string? _contentType;
    private readonly Form _form;

    private enum Form
    {
        None,
        HtmlString,
        HtmlBytes,
        PlainText,
    }

    private ContactPage(string id, Form form, string? text, ReadOnlyMemory<byte> bytes, string? contentType)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        Id = id;
        _form = form;
        _text = text;
        _bytes = bytes;
        _contentType = contentType;
    }

    /// <summary>The caller's id for the page. Null only for a default-constructed value, which the extractor refuses.</summary>
    public string? Id { get; }

    /// <summary>A page given as an HTML string. Its offsets count UTF-16 code units of <paramref name="html"/>.</summary>
    /// <param name="id">The page's id (not null or empty).</param>
    /// <param name="html">The page.</param>
    /// <returns>The page.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is null or empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="html"/> is null.</exception>
    public static ContactPage FromHtml(string id, string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        return new ContactPage(id, Form.HtmlString, html, default, null);
    }

    /// <summary>A page given as bytes. The encoding is sniffed as a browser would; offsets count bytes of <paramref name="html"/>.</summary>
    /// <param name="id">The page's id (not null or empty).</param>
    /// <param name="html">The page bytes; kept by reference until the site is complete.</param>
    /// <param name="contentType">The transport <c>Content-Type</c>, if known, as an encoding hint; null for none.</param>
    /// <returns>The page.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is null or empty.</exception>
    public static ContactPage FromHtml(string id, ReadOnlyMemory<byte> html, string? contentType = null) =>
        new(id, Form.HtmlBytes, null, html, contentType);

    /// <summary>A plain-text item of the set (a CRM note, a signature). Only the text rules read it; offsets count UTF-16 code units.</summary>
    /// <param name="id">The item's id (not null or empty).</param>
    /// <param name="text">The text.</param>
    /// <returns>The page.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is null or empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    public static ContactPage FromText(string id, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new ContactPage(id, Form.PlainText, text, default, null);
    }

    internal bool IsDefault => _form == Form.None;

    /// <summary>Reads the page with the given readers; the region is the caller's default for values that name none.</summary>
    internal ExtractResult Read(ExtractionSources sources, Region defaultRegion)
    {
        switch (_form)
        {
            case Form.HtmlString:
                return ExtractionPipeline.Run(new PageContext(Document.Parse(_text!), _text), sources, defaultRegion);
            case Form.HtmlBytes:
                return ExtractionPipeline.Run(
                    new PageContext(Document.Parse(_bytes, new ParseOptions { ContentType = _contentType }), null, bytesInput: true),
                    sources,
                    defaultRegion);
            case Form.PlainText:
                return ExtractionPipeline.Run(PageContext.ForPlainText(_text!), sources, defaultRegion);
            default:
                throw new ArgumentException("The page is a default value: build it with ContactPage.FromHtml or FromText.");
        }
    }
}
