using RecursiveAI.OneContact.Extraction.Markup;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Accessibility;
using RecursiveAI.OneHtml.Css;

namespace RecursiveAI.OneContact.Extraction.Text;

/// <summary>
/// Reads the text a person looking at the page would read. The cascade (style sheets and inline styles, under OneHtml's user agent
/// sheet) says what is shown: <c>display:none</c>, <c>visibility:hidden</c>, the <c>hidden</c> attribute, <c>aria-hidden</c>,
/// transparent, zero-size, clipped or moved-off-screen elements and text indented out of view are left out. Block boundaries, <c>br</c>
/// and list items become line breaks; white space collapses as CSS 2 §16.6 says (the HTML Standard's rendering rules). Text reversed
/// with <c>direction:rtl</c> and <c>unicode-bidi:bidi-override</c> is read in the order it is displayed. Landmarks and headings come
/// from the accessibility tree.
/// </summary>
internal sealed class VisibleTextReader
{
    // Elements whose content a browser does not show as text, whatever the style says.
    private static readonly HashSet<string> NotText = new(StringComparer.Ordinal)
    {
        "script", "style", "template", "noscript", "iframe", "object", "embed", "canvas", "audio", "video", "select", "datalist", "head", "title", "svg", "math",
    };

    // Used only when the cascade could not be computed: the elements that start a new line in a browser's default rendering.
    private static readonly HashSet<string> BlockNames = new(StringComparer.Ordinal)
    {
        "address", "article", "aside", "blockquote", "body", "dd", "details", "div", "dl", "dt", "fieldset", "figcaption", "figure", "footer", "form",
        "h1", "h2", "h3", "h4", "h5", "h6", "header", "hr", "html", "li", "main", "nav", "ol", "p", "pre", "section", "summary", "table", "tbody", "td",
        "tfoot", "th", "thead", "tr", "ul", "caption", "legend", "menu", "dir", "center", "option", "optgroup",
    };

    // Section-like elements: a field and its neighbours belong together when they share the innermost of these.
    internal static readonly HashSet<string> Containers = new(StringComparer.Ordinal)
    {
        "li", "tr", "article", "section", "address", "aside", "footer", "header", "main", "form", "blockquote", "figure", "details", "fieldset", "dd",
    };

    private const int MaxChars = 4_000_000;

    private readonly PageContext _page;
    private readonly DocumentStyles? _styles;
    private readonly Dictionary<int, string> _landmarks = [];
    private readonly HashSet<int> _headings = [];
    private readonly Dictionary<int, bool> _textVisible = [];
    private readonly Dictionary<int, bool> _preserve = [];

    private char[] _chars = new char[1024];
    private int[] _start = new int[1024];
    private int[] _end = new int[1024];
    private byte[] _flags = new byte[1024];
    private int _length;
    private readonly List<TextLine> _lines = [];
    private readonly List<int> _segmentStart = [];
    private readonly List<int> _segmentElement = [];
    private int _lineStart = -1;
    private LineBreak _lineBreak;
    private int _lineContainer = -1;
    private int _lineLandmark = -1;
    private int _lineHeading;
    private LineBreak _pending;
    private int _heading;
    private int _container = -1;
    private int _landmark = -1;

    private VisibleTextReader(PageContext page)
    {
        _page = page;
        try
        {
            _styles = DocumentStyles.Create(page.Document);
            AccessibilityTree tree = AccessibilityTree.Build(page.Document, _styles);
            NavigationLists lists = tree.CreateNavigationLists();
            foreach (LandmarkEntry l in lists.Landmarks)
            {
                _landmarks[l.Node.DomNode.Index] = l.Role;
            }

            foreach (HeadingEntry h in lists.Headings)
            {
                _headings.Add(h.Node.DomNode.Index);
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // A page the cascade or the tree builder cannot handle is still read, with the markup's own hints: attributes for what is
            // hidden, element names for blocks and headings.
        }
    }

    /// <summary>True when the page was too large to read in full; what was read is still returned.</summary>
    public bool Truncated { get; private set; }

    /// <summary>Reads the visible text of a page.</summary>
    /// <param name="page">The page.</param>
    /// <param name="truncated">Set when the text budget ran out.</param>
    /// <returns>The lines of visible text with their source map.</returns>
    public static TextView Read(PageContext page, out bool truncated)
    {
        var reader = new VisibleTextReader(page);
        TextView view = reader.Run();
        truncated = reader.Truncated;
        return view;
    }

    private readonly record struct Frame(Node Node, bool Exit, int Container, int Landmark, bool ParentFlex, bool InOverride, int OverrideStart, bool Block);

    private TextView Run()
    {
        var stack = new Stack<Frame>();
        PushChildren(stack, _page.Document.Root, -1, -1, false, false);
        int budget = 5_000_000;
        while (stack.Count > 0 && budget-- > 0 && !Truncated)
        {
            Frame f = stack.Pop();
            if (f.Exit)
            {
                ExitElement(f);
                continue;
            }

            Node n = f.Node;
            if (n.Kind == NodeKind.Text)
            {
                AppendTextNode(n, f.Container, f.Landmark);
                continue;
            }

            if (!n.IsElement)
            {
                continue;
            }

            EnterElement(stack, f);
        }

        CloseLine();
        return new TextView(
            new string(_chars, 0, _length),
            _lines,
            _start[.._length],
            _end[.._length],
            _flags[.._length],
            [.. _segmentStart],
            [.. _segmentElement]);
    }

    private static void PushChildren(Stack<Frame> stack, Node parent, int container, int landmark, bool parentFlex, bool inOverride)
    {
        for (Node c = parent.LastChild; !c.IsNull; c = c.PreviousSibling)
        {
            if (c.Kind == NodeKind.Text || c.Kind == NodeKind.Element)
            {
                stack.Push(new Frame(c, false, container, landmark, parentFlex, inOverride, -1, false));
            }
        }
    }

    private void EnterElement(Stack<Frame> stack, Frame f)
    {
        Node el = f.Node;
        string name = el.LocalName;
        // With a cascade, "hidden" and display:none come out of the style (a visible child of a visibility:hidden parent is shown); the
        // attribute check is then only for aria-hidden. Without one, the markup's own hints decide.
        if (NotText.Contains(name) || (_styles is null ? _page.IsHidden(el) : AriaHidden(el)))
        {
            return;
        }

        ComputedStyle? style = null;
        string display = string.Empty;
        if (_styles is not null)
        {
            try
            {
                style = _styles.GetStyle(el);
                display = style.GetPropertyValue("display");
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                style = null;
            }
        }

        if (style is not null && IsRemovedByStyle(style, display))
        {
            return;
        }

        if (name == "br")
        {
            Break(LineBreak.Soft);
            return;
        }

        bool block = style is not null ? IsBlockLevel(display) || f.ParentFlex : BlockNames.Contains(name);
        bool flex = style is not null && (display.StartsWith("flex", StringComparison.Ordinal) || display.StartsWith("grid", StringComparison.Ordinal)
            || display.Contains("flex", StringComparison.Ordinal) || display.Contains("grid", StringComparison.Ordinal));

        int container = f.Container;
        if (Containers.Contains(name))
        {
            container = el.Index;
        }

        int landmark = _landmarks.ContainsKey(el.Index) ? el.Index : f.Landmark;
        // The accessibility tree names the headings (role=heading included); without it the element names do.
        bool heading = _headings.Contains(el.Index) || (_headings.Count == 0 && IsHeadingName(name));

        if (block || heading)
        {
            Break(LineBreak.Hard);
        }

        if (heading)
        {
            _heading++;
        }

        bool inOverride = f.InOverride;
        int overrideStart = -1;
        if (!inOverride && style is not null && IsBidiOverride(style))
        {
            inOverride = true;
            overrideStart = _length;
        }

        // The state the lines of this element's text are recorded with.
        stack.Push(new Frame(el, true, f.Container, f.Landmark, f.ParentFlex, f.InOverride, overrideStart, block || heading));
        PushChildrenWithState(stack, el, container, landmark, flex, inOverride);
    }

    private static void PushChildrenWithState(Stack<Frame> stack, Node el, int container, int landmark, bool flex, bool inOverride)
    {
        // The container and landmark apply while the children are read: they are set on entry and restored on exit by the frames of the children.
        for (Node c = el.LastChild; !c.IsNull; c = c.PreviousSibling)
        {
            if (c.Kind == NodeKind.Text || c.Kind == NodeKind.Element)
            {
                stack.Push(new Frame(c, false, container, landmark, flex, inOverride, -1, false));
            }
        }
    }

    private void ExitElement(Frame f)
    {
        if (f.OverrideStart >= 0)
        {
            ReverseFrom(f.OverrideStart);
        }

        if (f.Block)
        {
            Break(LineBreak.Hard);
        }
    }

    private static bool AriaHidden(Node el) =>
        string.Equals(PageContext.AttributeValue(el, "aria-hidden")?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

    // The cascade does not read a unitless zero as a length (the font-size of "font-size:0" stays at its default), but a page that hides text
    // this way is hiding it: the element's own style attribute is read for it.
    private static bool InlineFontSizeZero(Node el)
    {
        string? style = PageContext.AttributeValue(el, "style");
        if (style is null)
        {
            return false;
        }

        string compact = style.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        const string Declaration = "font-size:0";
        int at = compact.IndexOf(Declaration, StringComparison.Ordinal);
        while (at >= 0)
        {
            int next = at + Declaration.Length;
            if (next >= compact.Length || compact[next] is ';' or '!')
            {
                return true;
            }

            at = compact.IndexOf(Declaration, next, StringComparison.Ordinal);
        }

        return false;
    }

    private static bool IsHeadingName(string name) => name.Length == 2 && name[0] == 'h' && name[1] >= '1' && name[1] <= '6';

    private static bool IsBlockLevel(string display)
    {
        if (display.Length == 0)
        {
            return false;
        }

        // The first word of the value ("block flow" -> "block").
        int space = display.IndexOf(' ');
        string first = space < 0 ? display : display[..space];
        if (first is "inline" or "contents" or "ruby" or "ruby-text" or "ruby-base" or "inline-block" or "inline-flex" or "inline-grid" or "inline-table" or "none")
        {
            return false;
        }

        return true;
    }

    private static bool IsBidiOverride(ComputedStyle style)
    {
        string bidi = style.GetPropertyValue("unicode-bidi");
        return style.GetPropertyValue("direction").Equals("rtl", StringComparison.Ordinal)
            && (bidi.Contains("bidi-override", StringComparison.Ordinal) || bidi.Contains("isolate-override", StringComparison.Ordinal));
    }

    // True for elements that are not shown at all, with everything inside them.
    private static bool IsRemovedByStyle(ComputedStyle style, string display)
    {
        if (display.Equals("none", StringComparison.Ordinal))
        {
            return true;
        }

        if (TryNumber(style.GetPropertyValue("opacity"), out double opacity) && opacity <= 0.01)
        {
            return true;
        }

        string position = style.GetPropertyValue("position");
        bool positioned = position is "absolute" or "fixed";
        if (positioned && (Pixels(style.GetPropertyValue("left")) <= -1000 || Pixels(style.GetPropertyValue("top")) <= -1000
            || (Pixels(style.GetPropertyValue("right")) <= -1000 && style.GetPropertyValue("left") is "auto" or "")))
        {
            return true;
        }

        string overflowX = style.GetPropertyValue("overflow-x");
        string overflowY = style.GetPropertyValue("overflow-y");
        bool clipsOverflow = overflowX is "hidden" or "clip" || overflowY is "hidden" or "clip";
        if (clipsOverflow)
        {
            double height = Pixels(style.GetPropertyValue("height"));
            double width = Pixels(style.GetPropertyValue("width"));
            double maxHeight = Pixels(style.GetPropertyValue("max-height"));
            double maxWidth = Pixels(style.GetPropertyValue("max-width"));
            if (IsTiny(height) || IsTiny(width) || IsTiny(maxHeight) || IsTiny(maxWidth))
            {
                return true;
            }
        }

        string clip = style.GetPropertyValue("clip");
        if (positioned && clip.StartsWith("rect(", StringComparison.Ordinal) && IsEmptyRect(clip))
        {
            return true;
        }

        string clipPath = style.GetPropertyValue("clip-path").Replace(" ", string.Empty, StringComparison.Ordinal);
        return clipPath.StartsWith("inset(100%", StringComparison.Ordinal) || clipPath.StartsWith("circle(0", StringComparison.Ordinal);
    }

    // Zero or one pixel in a dimension, as set: a "screen reader only" box or a hidden trap.
    private static bool IsTiny(double px) => px is >= 0 and <= 1;

    private static bool IsEmptyRect(string clip)
    {
        string inner = clip[5..].TrimEnd(')', ' ');
        string[] parts = inner.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4)
        {
            return false;
        }

        // rect(top, right, bottom, left): empty when the box has no width or no height.
        double top = Pixels(parts[0]);
        double right = Pixels(parts[1]);
        double bottom = Pixels(parts[2]);
        double left = Pixels(parts[3]);
        return bottom - top <= 1 || right - left <= 1;
    }

    private static bool TryNumber(string text, out double value) =>
        double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);

    /// <summary>The value of a computed length in pixels; NaN for <c>auto</c>, percentages, unknown units and everything else.</summary>
    internal static double Pixels(string value)
    {
        string v = value.Trim();
        if (v == "0")
        {
            return 0;
        }

        if (v.EndsWith("px", StringComparison.Ordinal) && TryNumber(v[..^2], out double px))
        {
            return px;
        }

        return double.NaN;
    }

    // Whether the characters directly inside this element can be seen: not hidden by visibility, font size or indent.
    private bool IsTextVisible(Node parent)
    {
        if (_styles is null || parent.IsNull || !parent.IsElement)
        {
            return true;
        }

        if (_textVisible.TryGetValue(parent.Index, out bool known))
        {
            return known;
        }

        bool visible = true;
        try
        {
            ComputedStyle s = _styles.GetStyle(parent);
            string visibility = s.GetPropertyValue("visibility");
            if (visibility is "hidden" or "collapse")
            {
                visible = false;
            }
            else if (s.FontSizePx < 1 || InlineFontSizeZero(parent))
            {
                visible = false;
            }
            else if (Pixels(s.GetPropertyValue("text-indent")) <= -1000)
            {
                visible = false;
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            visible = true;
        }

        _textVisible[parent.Index] = visible;
        return visible;
    }

    private bool PreservesNewlines(Node parent)
    {
        if (_styles is null || parent.IsNull || !parent.IsElement)
        {
            return false;
        }

        if (_preserve.TryGetValue(parent.Index, out bool known))
        {
            return known;
        }

        bool keep = false;
        try
        {
            string ws = _styles.GetStyle(parent).GetPropertyValue("white-space");
            keep = ws.StartsWith("pre", StringComparison.Ordinal) || ws.Contains("break-spaces", StringComparison.Ordinal) || ws == "pre-line";
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            keep = false;
        }

        _preserve[parent.Index] = keep;
        return keep;
    }

    private void Break(LineBreak strength)
    {
        if (strength > _pending)
        {
            _pending = strength;
        }
    }

    private void AppendTextNode(Node text, int container, int landmark)
    {
        Node parent = text.Parent;
        if (!IsTextVisible(parent))
        {
            return;
        }

        ReadOnlySpan<char> data = text.Data;
        if (data.IsEmpty)
        {
            return;
        }

        int[]? starts = null;
        int[]? ends = null;
        SourceRange range = text.Source.Range;
        SourceRange verbatim = text.Source.DataRange;
        if (!verbatim.IsPresent && range.IsPresent)
        {
            AlignEntities(_page.Text.AsSpan(range.Start, Math.Min(range.End, _page.Text.Length) - range.Start), data, range.Start, out starts, out ends);
        }

        bool preserve = PreservesNewlines(parent);
        _container = container;
        _landmark = landmark;
        bool first = true;
        for (int i = 0; i < data.Length; i++)
        {
            int s;
            int e;
            if (verbatim.IsPresent)
            {
                s = verbatim.Start + i;
                e = s + 1;
            }
            else if (starts is not null && ends is not null)
            {
                s = starts[i];
                e = ends[i];
            }
            else
            {
                s = -1;
                e = -1;
            }

            char c = data[i];
            if (c == '\n' && preserve)
            {
                Break(LineBreak.Soft);
                continue;
            }

            if (PageContext.IsHtmlSpace(c) || (preserve && c == '\t'))
            {
                if (_length == 0 || _pending != LineBreak.Start)
                {
                    continue;
                }

                if (_chars[_length - 1] == ' ' && _flags[_length - 1] == 2)
                {
                    // The same run of white space: the collapsed space now stands for all of it.
                    if (e >= 0)
                    {
                        _end[_length - 1] = Math.Max(_end[_length - 1], e);
                    }

                    continue;
                }

                if (_chars[_length - 1] == ' ' || _chars[_length - 1] == '\n')
                {
                    continue;
                }

                if (!Append(' ', s, e, 2))
                {
                    return;
                }

                continue;
            }

            if (IsInvisibleFormat(c))
            {
                continue;
            }

            char shown = c is '\u00A0' or '\u202F' or '\u2007' ? ' ' : c;
            if (!FlushBreak())
            {
                return;
            }

            if (first)
            {
                _segmentStart.Add(_length);
                _segmentElement.Add(parent.IsElement ? parent.Index : -1);
                first = false;
            }

            if (!Append(shown, s, e, 0))
            {
                return;
            }
        }
    }

    // Zero-width and soft-hyphen characters have no width: a reader sees the text as if they were not there.
    private static bool IsInvisibleFormat(char c) => c is '\u200B' or '\u200C' or '\u200D' or '\u2060' or '\uFEFF' or '\u00AD' or '\u200E' or '\u200F';

    // Starts a new line when a break is pending and a visible character is about to follow.
    private bool FlushBreak()
    {
        if (_pending != LineBreak.Start && _length > 0)
        {
            if (_chars[_length - 1] == ' ')
            {
                _length--;
            }

            CloseLine();
            if (!Append('\n', -1, -1, 0))
            {
                return false;
            }

            _lineStart = -1;
        }

        if (_lineStart < 0)
        {
            _lineStart = _length;
            _lineBreak = _length == 0 ? LineBreak.Start : _pending;
            _lineContainer = _container;
            _lineLandmark = _landmark;
            _lineHeading = _heading;
        }

        _pending = LineBreak.Start;
        return true;
    }

    private void CloseLine()
    {
        if (_lineStart >= 0)
        {
            int end = _length;
            while (end > _lineStart && _chars[end - 1] == ' ')
            {
                end--;
            }

            if (end > _lineStart)
            {
                _lines.Add(new TextLine(_lineStart, end, _lineBreak, _lineContainer, _lineHeading, _lineLandmark));
            }

            _lineStart = -1;
        }
    }

    private bool Append(char c, int s, int e, byte flag)
    {
        if (_length >= MaxChars)
        {
            Truncated = true;
            return false;
        }

        if (_length == _chars.Length)
        {
            int size = _chars.Length * 2;
            Array.Resize(ref _chars, size);
            Array.Resize(ref _start, size);
            Array.Resize(ref _end, size);
            Array.Resize(ref _flags, size);
        }

        _chars[_length] = c;
        _start[_length] = s;
        _end[_length] = e;
        _flags[_length] = flag;
        _length++;
        return true;
    }

    // Reverses each line of the characters from `from` on, as an override of the bidirectional algorithm displays them (UAX #9 rule HL1).
    private void ReverseFrom(int from)
    {
        int end = _length;
        while (end > from && _chars[end - 1] == ' ')
        {
            end--;
        }

        int i = from;
        while (i < end)
        {
            int j = i;
            while (j < end && _chars[j] != '\n')
            {
                j++;
            }

            int a = i;
            int b = j - 1;
            while (a < b)
            {
                (_chars[a], _chars[b]) = (_chars[b], _chars[a]);
                (_start[a], _start[b]) = (_start[b], _start[a]);
                (_end[a], _end[b]) = (_end[b], _end[a]);
                a++;
                b--;
            }

            for (int k = i; k < j; k++)
            {
                _flags[k] = TextView.Reversed;
            }

            i = j + 1;
        }
    }

    /// <summary>
    /// Maps each character of a text node's decoded data to the source characters that wrote it (a character reference writes one
    /// or two characters with several source ones). When the two cannot be lined up, every character maps to the whole node.
    /// </summary>
    private static void AlignEntities(ReadOnlySpan<char> raw, ReadOnlySpan<char> data, int offset, out int[]? starts, out int[]? ends)
    {
        var s = new int[data.Length];
        var e = new int[data.Length];
        int ri = 0;
        int di = 0;
        bool ok = true;
        while (di < data.Length && ri < raw.Length)
        {
            if (raw[ri] == '&')
            {
                int token = EntityEnd(raw, ri);
                if (token > ri)
                {
                    string written = raw[ri..token].ToString();
                    string decoded = System.Net.WebUtility.HtmlDecode(written);
                    // A name the framework does not know (HTML 5 has more than HTML 4) is taken to stand for one character.
                    if (decoded == written && written[^1] == ';' && written[1] != '#')
                    {
                        decoded = data[di].ToString();
                    }

                    if (decoded != written && decoded.Length > 0 && di + decoded.Length <= data.Length && data.Slice(di, decoded.Length).SequenceEqual(decoded))
                    {
                        for (int k = 0; k < decoded.Length; k++)
                        {
                            s[di + k] = offset + ri;
                            e[di + k] = offset + token;
                        }

                        di += decoded.Length;
                        ri = token;
                        continue;
                    }
                }
            }

            s[di] = offset + ri;
            e[di] = offset + ri + 1;
            di++;
            ri++;
        }

        if (di != data.Length || ri != raw.Length)
        {
            ok = false;
        }

        if (!ok)
        {
            for (int k = 0; k < data.Length; k++)
            {
                s[k] = offset;
                e[k] = offset + raw.Length;
            }
        }

        starts = s;
        ends = e;
    }

    // The end of a character reference that starts at raw[at] ('&'): numeric ones and named ones that end with ';'. Returns `at` when it is not one.
    internal static int EntityEnd(ReadOnlySpan<char> raw, int at)
    {
        int i = at + 1;
        if (i < raw.Length && raw[i] == '#')
        {
            i++;
            if (i < raw.Length && (raw[i] is 'x' or 'X'))
            {
                i++;
                int digits = i;
                while (i < raw.Length && char.IsAsciiHexDigit(raw[i]))
                {
                    i++;
                }

                if (i == digits)
                {
                    return at;
                }
            }
            else
            {
                int digits = i;
                while (i < raw.Length && char.IsAsciiDigit(raw[i]))
                {
                    i++;
                }

                if (i == digits)
                {
                    return at;
                }
            }

            return i < raw.Length && raw[i] == ';' ? i + 1 : i;
        }

        int nameStart = i;
        while (i < raw.Length && char.IsAsciiLetterOrDigit(raw[i]) && i - nameStart < 32)
        {
            i++;
        }

        return i > nameStart && i < raw.Length && raw[i] == ';' ? i + 1 : at;
    }
}
