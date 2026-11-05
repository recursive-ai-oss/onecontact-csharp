using RecursiveAI.OneContact.Extraction.Records;
using RecursiveAI.OneHtml;

namespace RecursiveAI.OneContact.Extraction.Markup;

/// <summary>
/// Readers for fields that are not part of a structured item: <c>mailto:</c> and <c>tel:</c> links and <c>address</c> elements. Both
/// skip markup a structured reader already took a value from, so nothing is read twice.
/// </summary>
internal static class LooseReaders
{
    /// <summary>The element indices of the value elements the item readers consumed, with their descendants implied.</summary>
    public static bool IsConsumed(PageContext page, HashSet<int> consumed, Node element)
    {
        if (consumed.Count == 0)
        {
            return false;
        }

        for (Node n = element; !n.IsNull; n = n.Parent)
        {
            if (consumed.Contains(n.Index))
            {
                return true;
            }
        }

        return false;
    }

    private static int NearestAddress(Node element)
    {
        for (Node n = element; !n.IsNull; n = n.Parent)
        {
            if (n.IsElement && n.LocalName == "address")
            {
                return n.Index;
            }
        }

        return -1;
    }

    private static bool IsContactLink(Node n) =>
        n.IsElement && n.LocalName is "a" or "area" && PageContext.AttributeValue(n, "href") is string h
        && (h.TrimStart().StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) || h.TrimStart().StartsWith("tel:", StringComparison.OrdinalIgnoreCase));

    public static void ReadLinks(PageContext page, HashSet<int> consumed, List<FieldDraft> loose)
    {
        foreach (Node a in page.Elements)
        {
            if (a.LocalName is not ("a" or "area") || !PageContext.TryGetAttribute(a, "href", out string href, out TextRange range))
            {
                continue;
            }

            string trimmed = href.Trim();
            bool mail = trimmed.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);
            bool tel = trimmed.StartsWith("tel:", StringComparison.OrdinalIgnoreCase);
            if ((!mail && !tel) || IsConsumed(page, consumed, a))
            {
                continue;
            }

            string linkText = PageContext.FlatText(a, keepLines: false);
            bool hidden = page.IsHidden(a);
            int block = NearestAddress(a);
            if (mail)
            {
                foreach (string recipient in ContactText.MailtoRecipients(trimmed))
                {
                    loose.Add(new FieldDraft
                    {
                        Kind = FieldKind.Email,
                        Source = FieldSource.Link,
                        Text = recipient,
                        Raw = range,
                        Label = LabelFor(linkText, recipient),
                        Hidden = hidden,
                        ElementIndex = a.Index,
                        BlockId = block,
                    });
                }
            }
            else
            {
                string number = ContactText.PhoneFrom(trimmed);
                if (number.Length > 0)
                {
                    loose.Add(new FieldDraft
                    {
                        Kind = FieldKind.Phone,
                        Source = FieldSource.Link,
                        Text = number,
                        Raw = range,
                        Label = LabelFor(linkText, number),
                        Hidden = hidden,
                        ElementIndex = a.Index,
                        BlockId = block,
                    });
                }
            }
        }
    }

    // The link text is a label when it says something other than the value itself.
    private static string? LabelFor(string linkText, string value)
    {
        if (linkText.Length == 0 || string.Equals(linkText, value, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return linkText;
    }

    public static void ReadAddressElements(PageContext page, HashSet<int> consumed, List<FieldDraft> loose)
    {
        foreach (Node el in page.Elements)
        {
            if (el.LocalName != "address" || IsConsumed(page, consumed, el))
            {
                continue;
            }

            // The contact links inside an address element are read as links; the address is what is left.
            string text = PageContext.FlatText(el, keepLines: true, IsContactLink);
            if (text.Length == 0)
            {
                continue;
            }

            loose.Add(new FieldDraft
            {
                Kind = FieldKind.Address,
                Source = FieldSource.AddressElement,
                Text = text,
                Raw = page.Trim(page.ContentRange(el)),
                Hidden = page.IsHidden(el),
                ElementIndex = el.Index,
                BlockId = el.Index,
            });
        }
    }
}
