using RecursiveAI.OneContact.Extraction.Records;
using RecursiveAI.OneHtml;

namespace RecursiveAI.OneContact.Extraction.Markup;

/// <summary>How a property element's value is read, shared by the microdata, RDFa and h-card readers.</summary>
internal static class ElementValues
{
    /// <summary>The value an attribute holds, with the attribute value's own source range.</summary>
    public static bool FromAttribute(PageContext page, Node el, string attribute, MarkupProperty into)
    {
        if (PageContext.TryGetAttribute(el, attribute, out string value, out TextRange range))
        {
            into.Text = value;
            into.Raw = range;
            return true;
        }

        return false;
    }

    /// <summary>The text of the element, with the range between its tags.</summary>
    public static void FromContent(PageContext page, Node el, MarkupProperty into)
    {
        into.Text = PageContext.FlatText(el, keepLines: true);
        into.Raw = page.Trim(page.ContentRange(el));
    }

    public static MarkupProperty New(PageContext page, Node el, string name)
    {
        return new MarkupProperty(name) { ElementIndex = el.Index, Hidden = page.IsHidden(el) };
    }
}

/// <summary>
/// Reads HTML microdata (HTML Living Standard §5.2) into items: top-level items are the elements with <c>itemscope</c> and no
/// <c>itemprop</c>; a property's value is taken from the element as the standard's "property values" rules list (<c>content</c> of <c>meta</c>,
/// <c>href</c> of links, <c>src</c> of media, <c>datetime</c> of <c>time</c>, otherwise its text). <c>itemref</c> is not followed.
/// </summary>
internal static class MicrodataReader
{
    private const int MaxItemDepth = 32;

    public static void Read(PageContext page, List<MarkupItem> roots)
    {
        foreach (Node el in page.Elements)
        {
            if (PageContext.HasAttribute(el, "itemscope") && !PageContext.HasAttribute(el, "itemprop"))
            {
                roots.Add(BuildItem(page, el, 0));
            }
        }
    }

    private static MarkupItem BuildItem(PageContext page, Node scope, int depth)
    {
        var item = new MarkupItem(FieldSource.Microdata)
        {
            ElementIndex = scope.Index,
            Range = page.OuterRange(scope),
            Hidden = page.IsHidden(scope),
            Id = PageContext.AttributeValue(scope, "itemid"),
        };
        string? types = PageContext.AttributeValue(scope, "itemtype");
        if (types is not null)
        {
            foreach (string t in types.Split(' ', '\t', '\n', '\f', '\r'))
            {
                if (t.Length > 0)
                {
                    item.Types.Add(VocabularyName.LocalName(t));
                }
            }
        }

        // Descendants in document order; an itemscope element is somebody else's scope, so its subtree is not ours.
        var stack = new Stack<Node>();
        PushChildren(scope, stack);
        while (stack.Count > 0)
        {
            Node el = stack.Pop();
            string? names = PageContext.AttributeValue(el, "itemprop");
            bool isScope = PageContext.HasAttribute(el, "itemscope");
            if (names is not null)
            {
                foreach (string name in names.Split(' ', '\t', '\n', '\f', '\r'))
                {
                    if (name.Length == 0)
                    {
                        continue;
                    }

                    MarkupProperty? p = isScope ? NestedValue(page, el, name, depth) : ScalarValue(page, el, name);
                    if (p is not null)
                    {
                        item.Properties.Add(p);
                    }
                }
            }

            if (!isScope)
            {
                PushChildren(el, stack);
            }
        }

        return item;
    }

    private static MarkupProperty? NestedValue(PageContext page, Node el, string name, int depth)
    {
        if (depth + 1 >= MaxItemDepth)
        {
            return null;
        }

        MarkupProperty p = ElementValues.New(page, el, name);
        p.Item = BuildItem(page, el, depth + 1);
        return p;
    }

    // HTML Living Standard, "property values": the value depends on the element.
    private static MarkupProperty ScalarValue(PageContext page, Node el, string name)
    {
        MarkupProperty p = ElementValues.New(page, el, name);
        string tag = el.LocalName;
        bool done = tag switch
        {
            "meta" => ElementValues.FromAttribute(page, el, "content", p),
            "audio" or "embed" or "iframe" or "img" or "source" or "track" or "video" => ElementValues.FromAttribute(page, el, "src", p),
            "a" or "area" or "link" => ElementValues.FromAttribute(page, el, "href", p),
            "object" => ElementValues.FromAttribute(page, el, "data", p),
            "data" or "meter" => ElementValues.FromAttribute(page, el, "value", p),
            "time" => ElementValues.FromAttribute(page, el, "datetime", p),
            _ => false,
        };
        if (!done)
        {
            ElementValues.FromContent(page, el, p);
        }

        return p;
    }

    internal static void PushChildren(Node parent, Stack<Node> stack)
    {
        var children = new List<Node>();
        for (Node c = parent.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (c.IsElement)
            {
                children.Add(c);
            }
        }

        for (int i = children.Count - 1; i >= 0; i--)
        {
            stack.Push(children[i]);
        }
    }
}

/// <summary>
/// Reads RDFa Lite 1.1 (<c>vocab</c>, <c>typeof</c>, <c>property</c>, <c>resource</c>, <c>prefix</c>) into items, for schema.org
/// vocabulary only: a <c>typeof</c> counts when its element or an ancestor sets a <c>vocab</c> naming schema.org, or when the type
/// itself is written <c>schema:Name</c> or as a schema.org address.
/// </summary>
internal static class RdfaReader
{
    private const int MaxItemDepth = 32;

    public static void Read(PageContext page, List<MarkupItem> roots)
    {
        foreach (Node el in page.Elements)
        {
            if (PageContext.HasAttribute(el, "typeof") && !PageContext.HasAttribute(el, "property") && IsSchemaItem(el))
            {
                roots.Add(BuildItem(page, el, 0));
            }
        }
    }

    private static bool IsSchemaItem(Node el)
    {
        string types = PageContext.AttributeValue(el, "typeof") ?? string.Empty;
        foreach (string t in Tokens(types))
        {
            if (t.StartsWith("schema:", StringComparison.Ordinal) || t.Contains("schema.org/", StringComparison.Ordinal))
            {
                return true;
            }
        }

        // The nearest vocab decides.
        for (Node n = el; !n.IsNull; n = n.Parent)
        {
            if (n.IsElement && PageContext.AttributeValue(n, "vocab") is string vocab)
            {
                return vocab.Contains("schema.org", StringComparison.OrdinalIgnoreCase);
            }
        }

        return false;
    }

    private static IEnumerable<string> Tokens(string s) => s.Split(' ', '\t', '\n', '\f', '\r').Where(t => t.Length > 0);

    private static MarkupItem BuildItem(PageContext page, Node scope, int depth)
    {
        var item = new MarkupItem(FieldSource.Rdfa)
        {
            ElementIndex = scope.Index,
            Range = page.OuterRange(scope),
            Hidden = page.IsHidden(scope),
            Id = PageContext.AttributeValue(scope, "resource") ?? PageContext.AttributeValue(scope, "about"),
        };
        foreach (string t in Tokens(PageContext.AttributeValue(scope, "typeof") ?? string.Empty))
        {
            item.Types.Add(VocabularyName.LocalName(t));
        }

        var stack = new Stack<Node>();
        MicrodataReader.PushChildren(scope, stack);
        while (stack.Count > 0)
        {
            Node el = stack.Pop();
            string? names = PageContext.AttributeValue(el, "property");
            bool isItem = PageContext.HasAttribute(el, "typeof");
            if (isItem && names is null)
            {
                // A typed element that is no property starts an item of its own.
                continue;
            }

            if (names is not null)
            {
                foreach (string name in Tokens(names))
                {
                    MarkupProperty? p = isItem ? Nested(page, el, name, depth) : Scalar(page, el, name);
                    if (p is not null)
                    {
                        item.Properties.Add(p);
                    }
                }
            }

            if (!isItem)
            {
                MicrodataReader.PushChildren(el, stack);
            }
        }

        return item;
    }

    private static MarkupProperty? Nested(PageContext page, Node el, string name, int depth)
    {
        if (depth + 1 >= MaxItemDepth)
        {
            return null;
        }

        MarkupProperty p = ElementValues.New(page, el, VocabularyName.LocalName(name));
        p.Item = BuildItem(page, el, depth + 1);
        return p;
    }

    // RDFa 1.1 §7.5 (object resolution), reduced to what RDFa Lite allows: content, then a link or media attribute, then resource, then text.
    private static MarkupProperty Scalar(PageContext page, Node el, string name)
    {
        MarkupProperty p = ElementValues.New(page, el, VocabularyName.LocalName(name));
        string tag = el.LocalName;
        bool done = ElementValues.FromAttribute(page, el, "content", p);
        if (!done)
        {
            done = tag switch
            {
                "a" or "area" or "link" => ElementValues.FromAttribute(page, el, "href", p),
                "audio" or "embed" or "iframe" or "img" or "source" or "track" or "video" => ElementValues.FromAttribute(page, el, "src", p),
                "time" => ElementValues.FromAttribute(page, el, "datetime", p),
                _ => false,
            };
        }

        if (!done)
        {
            done = ElementValues.FromAttribute(page, el, "resource", p);
        }

        if (!done)
        {
            ElementValues.FromContent(page, el, p);
        }

        return p;
    }
}
