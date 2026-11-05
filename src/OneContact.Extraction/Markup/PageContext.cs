using System.Text;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Parsing;

namespace RecursiveAI.OneContact.Extraction.Markup;

/// <summary>
/// One parsed page and the questions every reader asks of it: the source text and where a range of it sits in the original input,
/// the text of an element, whether markup is hidden, and what region the page's <c>lang</c> names.
/// </summary>
internal sealed class PageContext
{
    // HTML block-level elements start a new line in the flat text of an address, as a browser would render them.
    private static readonly HashSet<string> Blocks = new(StringComparer.Ordinal)
    {
        "address", "article", "aside", "blockquote", "dd", "details", "div", "dl", "dt", "fieldset", "figcaption", "figure", "footer", "form",
        "h1", "h2", "h3", "h4", "h5", "h6", "header", "hr", "li", "main", "nav", "ol", "p", "pre", "section", "summary", "table", "tbody", "td", "tfoot",
        "th", "thead", "tr", "ul",
    };

    private readonly Dictionary<int, bool> _hidden = [];
    private readonly int[]? _collapsedCrlf;

    public PageContext(Document document, string? original, bool bytesInput = false)
    {
        Document = document;
        Text = document.Source.Text;
        HasBytes = bytesInput || document.Source.Bytes.Length > 0;
        if (!HasBytes && original is not null && original.Contains('\r', StringComparison.Ordinal))
        {
            _collapsedCrlf = FindCollapsedCrlf(original);
        }

        Elements = ListElements(document);
        PageRegion = ReadPageRegion(document);
    }

    // A plain string has no markup: the page is an empty document and the text is the string itself, so that offsets are its own.
    private PageContext(string plainText)
    {
        Document = Document.Parse(string.Empty);
        Text = plainText;
        HasBytes = false;
        IsPlainText = true;
        Elements = [];
        PageRegion = Region.Unknown;
    }

    /// <summary>A page that is a plain string (a CRM note, a comment): no elements, offsets are chars of the string.</summary>
    public static PageContext ForPlainText(string text) => new(text);

    /// <summary>True when the page is a plain string rather than parsed HTML.</summary>
    public bool IsPlainText { get; }

    public Document Document { get; }

    /// <summary>The decoded, newline-normalized text every <see cref="TextRange"/> points into.</summary>
    public string Text { get; }

    public bool HasBytes { get; }

    /// <summary>Every element in document order (template contents excluded).</summary>
    public List<Node> Elements { get; }

    /// <summary>The region in the page's <c>lang</c> (<c>en-US</c> gives US); unknown when the language has no region.</summary>
    public Region PageRegion { get; }

    public Node GetNode(int index) => Document.GetNode(index);

    /// <summary>Converts a text range to a span in the original input: bytes when the page came from bytes, otherwise chars of the original string.</summary>
    public SourceSpan ToSpan(TextRange range)
    {
        if (!range.IsPresent)
        {
            return default;
        }

        if (HasBytes)
        {
            SourceRange b = Document.ToByteRange(new SourceRange(range.Start, range.End));
            return new SourceSpan(b.Start, b.End - b.Start);
        }

        int s = MapNewlines(range.Start);
        int e = MapNewlines(range.End);
        return new SourceSpan(s, e - s);
    }

    public string Slice(TextRange range) => range.IsPresent && range.End <= Text.Length ? Text.Substring(range.Start, range.End - range.Start) : string.Empty;

    /// <summary>The range with leading and trailing HTML whitespace removed.</summary>
    public TextRange Trim(TextRange range)
    {
        if (!range.IsPresent || range.End > Text.Length)
        {
            return TextRange.None;
        }

        int s = range.Start;
        int e = range.End;
        while (s < e && IsHtmlSpace(Text[s]))
        {
            s++;
        }

        while (e > s && IsHtmlSpace(Text[e - 1]))
        {
            e--;
        }

        return new TextRange(s, e);
    }

    public static bool IsHtmlSpace(char c) => c is ' ' or '\t' or '\n' or '\f' or '\r';

    /// <summary>The element's start tag through its end tag, or to the end of its last descendant when the end tag is implied.</summary>
    public TextRange OuterRange(Node element)
    {
        SourceRange start = element.Source.Range;
        if (!start.IsPresent)
        {
            return TextRange.None;
        }

        int end = OuterEnd(element);
        return end >= start.Start && end <= Text.Length ? new TextRange(start.Start, end) : TextRange.None;
    }

    /// <summary>What lies between an element's start tag and its end tag.</summary>
    public TextRange ContentRange(Node element)
    {
        SourceRange start = element.Source.Range;
        if (!start.IsPresent || !element.Source.HasStartTag)
        {
            return TextRange.None;
        }

        SourceRange endTag = element.Source.EndTag;
        int end = endTag.IsPresent ? endTag.Start : OuterEnd(element);
        return end >= start.End && end <= Text.Length ? new TextRange(start.End, end) : TextRange.None;
    }

    private static int OuterEnd(Node element)
    {
        Node n = element;
        while (true)
        {
            SourceRange endTag = n.Source.EndTag;
            if (endTag.IsPresent)
            {
                return endTag.End;
            }

            Node last = n.LastChild;
            if (last.IsNull)
            {
                SourceRange r = n.Source.Range;
                return r.IsPresent ? r.End : -1;
            }

            n = last;
        }
    }

    /// <summary>
    /// The text a reader would see in an element. Runs of white space collapse to one space, <c>br</c> and block elements break lines,
    /// and scripts, styles and templates are skipped. Lines are separated by <c>\n</c>, and empty lines are dropped.
    /// </summary>
    public static string FlatText(Node element, bool keepLines, Func<Node, bool>? skip = null)
    {
        var sb = new StringBuilder();
        var stack = new Stack<(Node Node, bool Exit)>();
        stack.Push((element, false));
        bool root = true;
        while (stack.Count > 0)
        {
            (Node n, bool exit) = stack.Pop();
            if (exit)
            {
                sb.Append('\n');
                continue;
            }

            if (n.Kind == NodeKind.Text)
            {
                AppendCollapsed(sb, n.Data);
                continue;
            }

            if (!n.IsElement)
            {
                continue;
            }

            string name = n.LocalName;
            if (!root && (name is "script" or "style" or "template" or "noscript" || skip?.Invoke(n) == true))
            {
                continue;
            }

            if (name == "br")
            {
                sb.Append('\n');
                continue;
            }

            bool block = Blocks.Contains(name);
            if (block)
            {
                sb.Append('\n');
                stack.Push((n, true));
            }

            root = false;
            var children = new List<Node>();
            for (Node c = n.FirstChild; !c.IsNull; c = c.NextSibling)
            {
                children.Add(c);
            }

            for (int i = children.Count - 1; i >= 0; i--)
            {
                stack.Push((children[i], false));
            }
        }

        return TidyLines(sb.ToString(), keepLines);
    }

    private static void AppendCollapsed(StringBuilder sb, ReadOnlySpan<char> data)
    {
        foreach (char c in data)
        {
            if (IsHtmlSpace(c))
            {
                if (sb.Length == 0 || sb[^1] is ' ' or '\n')
                {
                    continue;
                }

                sb.Append(' ');
            }
            else
            {
                sb.Append(c);
            }
        }
    }

    private static string TidyLines(string raw, bool keepLines)
    {
        string[] lines = raw.Split('\n');
        var kept = new List<string>(lines.Length);
        foreach (string line in lines)
        {
            string t = line.Trim(' ');
            if (t.Length > 0)
            {
                kept.Add(t);
            }
        }

        return string.Join(keepLines ? "\n" : " ", kept);
    }

    /// <summary>The value of an attribute and where it sits; false when the element has no such attribute.</summary>
    public static bool TryGetAttribute(Node element, string name, out string value, out TextRange range)
    {
        foreach (Attr a in element.Attributes)
        {
            if (string.Equals(a.LocalName, name, StringComparison.Ordinal))
            {
                value = a.Value.ToString();
                SourceRange r = a.ValueRange;
                range = a.Quote == AttributeQuote.NoValue || !r.IsPresent ? TextRange.None : new TextRange(r.Start, r.End);
                return true;
            }
        }

        value = string.Empty;
        range = TextRange.None;
        return false;
    }

    public static bool HasAttribute(Node element, string name) => TryGetAttribute(element, name, out _, out _);

    public static string? AttributeValue(Node element, string name) => TryGetAttribute(element, name, out string v, out _) ? v : null;

    /// <summary>
    /// True when the element or an ancestor is hidden from view by what the markup alone says: a <c>hidden</c> attribute,
    /// <c>aria-hidden="true"</c>, or an inline style with <c>display:none</c> or <c>visibility:hidden</c>. Style sheets are not consulted.
    /// </summary>
    public bool IsHidden(Node element)
    {
        if (element.IsNull)
        {
            return false;
        }

        if (_hidden.TryGetValue(element.Index, out bool known))
        {
            return known;
        }

        bool result = element.IsElement && SelfHidden(element) || IsHidden(element.Parent);
        _hidden[element.Index] = result;
        return result;
    }

    private static bool SelfHidden(Node e)
    {
        if (HasAttribute(e, "hidden"))
        {
            return true;
        }

        if (string.Equals(AttributeValue(e, "aria-hidden")?.Trim(), "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string? style = AttributeValue(e, "style");
        if (style is null)
        {
            return false;
        }

        string compact = style.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        return compact.Contains("display:none", StringComparison.Ordinal) || compact.Contains("visibility:hidden", StringComparison.Ordinal);
    }

    private static List<Node> ListElements(Document document)
    {
        var list = new List<Node>();
        var stack = new Stack<Node>();
        stack.Push(document.Root);
        while (stack.Count > 0)
        {
            Node n = stack.Pop();
            if (n.IsElement)
            {
                list.Add(n);
            }

            var children = new List<Node>();
            for (Node c = n.FirstChild; !c.IsNull; c = c.NextSibling)
            {
                if (c.Kind == NodeKind.Element)
                {
                    children.Add(c);
                }
            }

            for (int i = children.Count - 1; i >= 0; i--)
            {
                stack.Push(children[i]);
            }
        }

        return list;
    }

    private static Region ReadPageRegion(Document document)
    {
        Node root = document.DocumentElement;
        if (root.IsNull)
        {
            return Region.Unknown;
        }

        string? lang = AttributeValue(root, "lang") ?? AttributeValue(root, "xml:lang");
        if (string.IsNullOrWhiteSpace(lang))
        {
            return Region.Unknown;
        }

        // BCP 47: language, then optional script (four letters), then an optional region of two letters.
        string[] parts = lang.Trim().Split('-', '_');
        for (int i = 1; i < parts.Length; i++)
        {
            if (parts[i].Length == 2 && Region.TryParse(parts[i], out Region r))
            {
                return r;
            }

            if (parts[i].Length == 1)
            {
                break;
            }
        }

        return Region.Unknown;
    }

    // Normalizing the newlines of a string collapses each CR LF to one LF; this lists where (the offset of the LF in the normalized text).
    private static int[] FindCollapsedCrlf(string original)
    {
        var positions = new List<int>();
        int normalized = 0;
        for (int i = 0; i < original.Length; i++)
        {
            if (original[i] == '\r' && i + 1 < original.Length && original[i + 1] == '\n')
            {
                positions.Add(normalized);
                i++;
            }

            normalized++;
        }

        return [.. positions];
    }

    private int MapNewlines(int normalizedOffset)
    {
        if (_collapsedCrlf is null)
        {
            return normalizedOffset;
        }

        // Each earlier collapsed pair was one character in the normalized text and two in the original.
        int index = Array.BinarySearch(_collapsedCrlf, normalizedOffset);
        int before = index >= 0 ? index : ~index;
        return normalizedOffset + before;
    }
}
