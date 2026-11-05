using RecursiveAI.OneContact.Extraction.Markup;

namespace RecursiveAI.OneContact.Extraction.Text;

/// <summary>How a line of visible text starts: after nothing, after a line break inside a block, or after a block boundary.</summary>
internal enum LineBreak
{
    Start = 0,

    /// <summary>A <c>br</c> element, or a newline in text whose white space is preserved.</summary>
    Soft = 1,

    /// <summary>A block-level boundary (a paragraph, a list item, a table cell, a heading).</summary>
    Hard = 2,
}

/// <summary>One line of the visible text and the part of the page it belongs to.</summary>
/// <param name="Start">Index of the first character in <see cref="TextView.Text"/>.</param>
/// <param name="End">Index just past the last character (the line break is not part of the line).</param>
/// <param name="Break">How the line starts.</param>
/// <param name="Container">The element index of the nearest enclosing section-like element (list item, table row, article, footer, ...), or -1 for the page; for plain text, the paragraph number.</param>
/// <param name="Heading">How many headings came before the line in document order: a heading starts a new section.</param>
/// <param name="Landmark">The element index of the nearest enclosing landmark of the accessibility tree, or -1.</param>
internal readonly record struct TextLine(int Start, int End, LineBreak Break, int Container, int Heading, int Landmark);

/// <summary>
/// The text a reader of the page sees, as lines, with a map from every character back to the original input. Characters that no
/// source text produced (a line break between blocks) have no source position. For plain text the map is the identity.
/// </summary>
internal sealed class TextView
{
    public const byte Reversed = 1;

    private readonly int[]? _srcStart;
    private readonly int[]? _srcEnd;
    private readonly byte[]? _flags;
    private readonly int[] _segmentStart;
    private readonly int[] _segmentElement;

    public TextView(string text, List<TextLine> lines, int[]? srcStart, int[]? srcEnd, byte[]? flags, int[] segmentStart, int[] segmentElement)
    {
        Text = text;
        Lines = lines;
        _srcStart = srcStart;
        _srcEnd = srcEnd;
        _flags = flags;
        _segmentStart = segmentStart;
        _segmentElement = segmentElement;
    }

    /// <summary>The visible text; lines are separated by <c>\n</c>.</summary>
    public string Text { get; }

    public List<TextLine> Lines { get; }

    public bool IsPlain => _srcStart is null;

    /// <summary>The index of the line that holds character <paramref name="index"/> (the line before it when the character is a break).</summary>
    public int LineOf(int index)
    {
        int lo = 0;
        int hi = Lines.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (Lines[mid].Start <= index)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return lo;
    }

    /// <summary>The element that holds the text node character <paramref name="index"/> came from, or -1.</summary>
    public int ElementOf(int index)
    {
        if (_segmentStart.Length == 0)
        {
            return -1;
        }

        int pos = Array.BinarySearch(_segmentStart, index);
        if (pos < 0)
        {
            pos = ~pos - 1;
        }

        return pos >= 0 ? _segmentElement[pos] : -1;
    }

    /// <summary>
    /// The range of the original text that produced characters <paramref name="start"/> to <paramref name="end"/> of <see cref="Text"/>:
    /// from the lowest to the highest source offset of its characters, so that text reversed by CSS still maps to one range. Break
    /// characters have no source.
    /// </summary>
    /// <param name="start">First character.</param>
    /// <param name="end">Index past the last character.</param>
    /// <param name="gap">Set when markup or skipped content sits between two neighbouring characters of the range (a value split by tags).</param>
    /// <param name="reversed">Set when a character of the range was reversed by <c>unicode-bidi</c>.</param>
    public TextRange RangeOf(int start, int end, out bool gap, out bool reversed)
    {
        gap = false;
        reversed = false;
        if (_srcStart is null || _srcEnd is null)
        {
            return new TextRange(start, end);
        }

        int lo = int.MaxValue;
        int hi = -1;
        int previousEnd = -1;
        for (int i = start; i < end; i++)
        {
            int s = _srcStart[i];
            if (s < 0)
            {
                continue;
            }

            if (_flags![i] == Reversed)
            {
                reversed = true;
            }
            else if (previousEnd >= 0 && s != previousEnd && !reversed)
            {
                gap = true;
            }

            previousEnd = _srcEnd[i];
            lo = Math.Min(lo, s);
            hi = Math.Max(hi, _srcEnd[i]);
        }

        return hi < 0 ? TextRange.None : new TextRange(lo, hi);
    }

    /// <summary>The source range of a whole line (for placing a field that sits in the markup rather than in the text).</summary>
    public TextRange LineRange(int line) => RangeOf(Lines[line].Start, Lines[line].End, out _, out _);

    /// <summary>
    /// The first line whose source range reaches <paramref name="offset"/> and overlaps <paramref name="within"/>, or -1. Used to put a
    /// value read from an attribute (a link's <c>href</c>) on the line that shows the link.
    /// </summary>
    public int LineAtSource(int offset, TextRange within)
    {
        if (IsPlain || !within.IsPresent)
        {
            return -1;
        }

        int lo = 0;
        int hi = Lines.Count - 1;
        int found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            TextRange r = LineRange(mid);
            if (!r.IsPresent || r.End < offset)
            {
                // A line without source (cannot happen for a non-empty line) is treated as before the offset.
                lo = mid + 1;
            }
            else
            {
                found = mid;
                hi = mid - 1;
            }
        }

        if (found < 0)
        {
            return -1;
        }

        TextRange line = LineRange(found);
        return line.IsPresent && line.Start < within.End && line.End > within.Start ? found : -1;
    }
}
