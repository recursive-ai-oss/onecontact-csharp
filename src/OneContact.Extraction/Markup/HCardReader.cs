using RecursiveAI.OneContact.Extraction.Records;
using RecursiveAI.OneHtml;

namespace RecursiveAI.OneContact.Extraction.Markup;

/// <summary>
/// Reads microformats2 <c>h-card</c> (microformats.org/wiki/h-card) and classic hCard 1.0 (<c>vcard</c> with unprefixed property
/// classes) into items, naming each property as schema.org does so one builder serves every reader. Properties read: name, given and
/// family name, <c>org</c> (a nested h-card or text), telephone, email, url, and the address with its parts (as an <c>adr</c> or
/// directly on the card). Whether a card is a person or an organization is decided by the builder.
/// </summary>
internal static class HCardReader
{
    private const int MaxItemDepth = 32;

    // Property class (mf2 prefix removed, classic names as they are) -> schema.org name.
    private static readonly Dictionary<string, string> Properties = new(StringComparer.Ordinal)
    {
        ["name"] = "name",
        ["fn"] = "name",
        ["given-name"] = "givenName",
        ["family-name"] = "familyName",
        ["org"] = "worksFor",
        ["tel"] = "telephone",
        ["email"] = "email",
        ["url"] = "url",
        ["adr"] = "address",
        ["street-address"] = "streetAddress",
        ["extended-address"] = "extendedAddress",
        ["locality"] = "addressLocality",
        ["region"] = "addressRegion",
        ["postal-code"] = "postalCode",
        ["country-name"] = "addressCountry",
    };

    private static readonly string[] Prefixes = ["p-", "u-", "dt-", "e-"];

    public static void Read(PageContext page, List<MarkupItem> roots)
    {
        foreach (Node el in page.Elements)
        {
            if (IsRoot(el) && !IsNestedValue(el))
            {
                roots.Add(BuildCard(page, el, 0));
            }
        }
    }

    // A card that is the p-org of an enclosing card is that card's property value, not a card of its own.
    private static bool IsNestedValue(Node el)
    {
        if (!PropertyNames(el).Contains("worksFor"))
        {
            return false;
        }

        for (Node n = el.Parent; !n.IsNull; n = n.Parent)
        {
            if (n.IsElement && IsRoot(n))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsRoot(Node el)
    {
        foreach (string c in Classes(el))
        {
            if (c is "h-card" or "vcard")
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> Classes(Node el)
    {
        string? cls = PageContext.AttributeValue(el, "class");
        return cls is null ? [] : cls.Split(' ', '\t', '\n', '\f', '\r').Where(c => c.Length > 0);
    }

    /// <summary>The schema.org names of the properties an element's classes declare.</summary>
    private static List<string> PropertyNames(Node el)
    {
        var names = new List<string>();
        foreach (string c in Classes(el))
        {
            string bare = c;
            foreach (string prefix in Prefixes)
            {
                if (c.StartsWith(prefix, StringComparison.Ordinal))
                {
                    bare = c[prefix.Length..];
                    break;
                }
            }

            if (Properties.TryGetValue(bare, out string? schema) && !names.Contains(schema))
            {
                names.Add(schema);
            }
        }

        return names;
    }

    private static MarkupItem BuildCard(PageContext page, Node root, int depth)
    {
        var item = new MarkupItem(FieldSource.HCard)
        {
            ElementIndex = root.Index,
            Range = page.OuterRange(root),
            Hidden = page.IsHidden(root),
        };
        item.Types.Add("h-card");
        Collect(page, root, item, depth, isAddress: false);
        AddImpliedProperties(page, root, item);
        return item;
    }

    // microformats2 parsing, "implied properties": a card with no name takes its text as the name, and a link card its href as the url.
    private static void AddImpliedProperties(PageContext page, Node root, MarkupItem item)
    {
        bool hasName = item.Properties.Any(p => p.Name == "name");
        bool isLink = root.LocalName is "a" or "area";
        if (hasName || (item.Properties.Count > 0 && !isLink))
        {
            return;
        }

        MarkupProperty name = ElementValues.New(page, root, "name");
        ElementValues.FromContent(page, root, name);
        if (!string.IsNullOrWhiteSpace(name.Text) && name.Text.Length <= 200)
        {
            item.Properties.Add(name);
        }

        if (isLink && !item.Properties.Any(p => p.Name == "url"))
        {
            MarkupProperty url = ElementValues.New(page, root, "url");
            if (ElementValues.FromAttribute(page, root, "href", url))
            {
                item.Properties.Add(url);
            }
        }
    }

    private static MarkupItem BuildAddress(PageContext page, Node el, int depth)
    {
        var item = new MarkupItem(FieldSource.HCard)
        {
            ElementIndex = el.Index,
            Range = page.OuterRange(el),
            Hidden = page.IsHidden(el),
        };
        item.Types.Add("PostalAddress");
        Collect(page, el, item, depth, isAddress: true);
        return item;
    }

    // Walks the descendants of a card (or its address). A nested card or address is a property value or somebody else's; its
    // subtree is never ours.
    private static void Collect(PageContext page, Node scope, MarkupItem item, int depth, bool isAddress)
    {
        var stack = new Stack<Node>();
        MicrodataReader.PushChildren(scope, stack);
        while (stack.Count > 0)
        {
            Node el = stack.Pop();
            List<string> names = PropertyNames(el);
            bool nestedCard = IsRoot(el);
            if (nestedCard)
            {
                if (names.Contains("worksFor") && depth + 1 < MaxItemDepth)
                {
                    MarkupProperty p = ElementValues.New(page, el, "worksFor");
                    p.Item = BuildCard(page, el, depth + 1);
                    item.Properties.Add(p);
                }

                continue;
            }

            if (isAddress && !IsAddressPart(names))
            {
                names = [];
            }

            bool descend = true;
            foreach (string name in names)
            {
                if (name == "address")
                {
                    if (depth + 1 < MaxItemDepth)
                    {
                        MarkupProperty p = ElementValues.New(page, el, "address");
                        p.Item = BuildAddress(page, el, depth + 1);
                        item.Properties.Add(p);
                    }

                    descend = false;
                    continue;
                }

                item.Properties.Add(Value(page, el, name));
            }

            if (descend)
            {
                MicrodataReader.PushChildren(el, stack);
            }
        }
    }

    private static bool IsAddressPart(List<string> names) =>
        names.Any(n => n is "streetAddress" or "extendedAddress" or "addressLocality" or "addressRegion" or "postalCode" or "addressCountry");

    private static MarkupProperty Value(PageContext page, Node el, string schemaName)
    {
        MarkupProperty p = ElementValues.New(page, el, schemaName);
        string tag = el.LocalName;
        switch (schemaName)
        {
            case "telephone":
            case "email":
            case "url":
                if (tag is "a" or "area" && ElementValues.FromAttribute(page, el, "href", p))
                {
                    return p;
                }

                if (FindValueChild(el) is Node valueChild)
                {
                    ElementValues.FromContent(page, valueChild, p);
                    p.ElementIndex = el.Index;
                    return p;
                }

                break;
            case "worksFor":
                if (FindDescendantWithClass(el, "organization-name") is Node orgName)
                {
                    ElementValues.FromContent(page, orgName, p);
                    return p;
                }

                break;
        }

        if (tag == "abbr" && ElementValues.FromAttribute(page, el, "title", p))
        {
            return p;
        }

        ElementValues.FromContent(page, el, p);
        return p;
    }

    // hCard 1.0: <span class="tel"><span class="type">work</span> <span class="value">+1 ...</span></span>.
    private static Node? FindValueChild(Node el) => FindDescendantWithClass(el, "value");

    private static Node? FindDescendantWithClass(Node el, string cls)
    {
        var stack = new Stack<Node>();
        MicrodataReader.PushChildren(el, stack);
        while (stack.Count > 0)
        {
            Node n = stack.Pop();
            foreach (string c in Classes(n))
            {
                if (c == cls || c == "p-" + cls)
                {
                    return n;
                }
            }

            MicrodataReader.PushChildren(n, stack);
        }

        return null;
    }
}
