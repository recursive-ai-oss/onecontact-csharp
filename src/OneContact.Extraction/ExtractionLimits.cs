using RecursiveAI.OneContact.Extraction.Markup;
using RecursiveAI.OneHtml;

namespace RecursiveAI.OneContact.Extraction;

/// <summary>
/// The bounds on the work one input may cause, for reading untrusted pages and text. The defaults are generous: real pages are far below them. An input past
/// <see cref="MaxInputLength"/>, <see cref="MaxNesting"/> or <see cref="MaxNodes"/> is refused with a <see cref="ContactExtractionException"/>; more than
/// <see cref="MaxFields"/> fields stops the read and the result keeps what was read, with an issue of kind <see cref="Records.ExtractIssueKind.LimitExceeded"/>.
/// </summary>
public sealed record ExtractionLimits
{
    /// <summary>Gets the limits used when none are set.</summary>
    public static ExtractionLimits Default { get; } = new();

    /// <summary>Gets the largest input: bytes for bytes, UTF-16 code units for a string. Default 64 MiB.</summary>
    public int MaxInputLength { get; init; } = 64 * 1024 * 1024;

    /// <summary>Gets the deepest element nesting of an HTML page. Default 4096.</summary>
    public int MaxNesting { get; init; } = 4096;

    /// <summary>Gets the most nodes (elements, text, comments) one HTML page may hold. Default 4,194,304.</summary>
    public int MaxNodes { get; init; } = 4 * 1024 * 1024;

    /// <summary>Gets the most fields kept from one page or text. Default 100,000.</summary>
    public int MaxFields { get; init; } = 100_000;

    internal ParseLimits ToParseLimits() => new()
    {
        MaxInputLength = MaxInputLength,
        MaxOpenElements = MaxNesting,
        MaxNodes = MaxNodes,
    };

    internal Document ParseHtml(string html)
    {
        try
        {
            return Document.Parse(html, new ParseOptions { Limits = ToParseLimits() });
        }
        catch (HtmlLimitExceededException e)
        {
            throw Wrap(e);
        }
    }

    internal Document ParseHtml(ReadOnlyMemory<byte> html, string? contentType)
    {
        try
        {
            return Document.Parse(html, new ParseOptions { ContentType = contentType, Limits = ToParseLimits() });
        }
        catch (HtmlLimitExceededException e)
        {
            throw Wrap(e);
        }
    }

    internal void CheckText(string text)
    {
        if (text.Length > MaxInputLength)
        {
            throw new ContactExtractionException("MaxInputLength", MaxInputLength, text.Length, null);
        }
    }

    private static ContactExtractionException Wrap(HtmlLimitExceededException e) =>
        new(e.LimitName switch
        {
            "MaxInputLength" => "MaxInputLength",
            "MaxOpenElements" => "MaxNesting",
            "MaxNodes" => "MaxNodes",
            _ => e.LimitName,
        }, e.Limit, e.Actual, e);
}

/// <summary>
/// Thrown when an input is past one of the <see cref="ExtractionLimits"/>. It is the exception callers handle for hostile input; the HTML parser's own
/// exception, when that was the cause, is its <see cref="Exception.InnerException"/>.
/// </summary>
public sealed class ContactExtractionException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="limitName">The <see cref="ExtractionLimits"/> property that was exceeded.</param>
    /// <param name="limit">The bound.</param>
    /// <param name="actual">The value that exceeded it, or the first value over the bound that was seen.</param>
    /// <param name="inner">The cause, if any.</param>
    public ContactExtractionException(string limitName, long limit, long actual, Exception? inner)
        : base($"The input exceeds the {limitName} limit of {limit} (saw {actual}).", inner)
    {
        LimitName = limitName;
        Limit = limit;
        Actual = actual;
    }

    /// <summary>Gets the name of the <see cref="ExtractionLimits"/> property that was exceeded.</summary>
    public string LimitName { get; }

    /// <summary>Gets the bound.</summary>
    public long Limit { get; }

    /// <summary>Gets the value that exceeded the bound.</summary>
    public long Actual { get; }
}
