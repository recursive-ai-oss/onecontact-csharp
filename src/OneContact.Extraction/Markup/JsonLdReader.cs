using RecursiveAI.OneContact.Extraction.Records;
using RecursiveAI.OneHtml;

namespace RecursiveAI.OneContact.Extraction.Markup;

/// <summary>
/// Reads a <c>script type="application/ld+json"</c> block into items. JSON-LD (W3C JSON-LD 1.1) is read as plain JSON: the reader
/// understands <c>@type</c>, <c>@id</c>, <c>@graph</c>, <c>@value</c>, <c>@list</c>/<c>@set</c> and prefixed or absolute names, which
/// is how schema.org markup is written on pages. It does not expand contexts: a context that renames terms is not honoured.
/// </summary>
internal static class JsonLdReader
{
    public static void Read(PageContext page, List<MarkupItem> roots, List<ExtractIssue> issues)
    {
        foreach (Node script in page.Elements)
        {
            if (script.LocalName != "script" || !IsJsonLd(PageContext.AttributeValue(script, "type")))
            {
                continue;
            }

            ReadBlock(page, script, roots, issues);
        }
    }

    private static bool IsJsonLd(string? type)
    {
        if (type is null)
        {
            return false;
        }

        int semicolon = type.IndexOf(';', StringComparison.Ordinal);
        string essence = (semicolon >= 0 ? type[..semicolon] : type).Trim();
        return string.Equals(essence, "application/ld+json", StringComparison.OrdinalIgnoreCase);
    }

    private static void ReadBlock(PageContext page, Node script, List<MarkupItem> roots, List<ExtractIssue> issues)
    {
        // Script data is raw text: no character references, so the characters of the text node are the source characters.
        int baseOffset = -1;
        string text = string.Empty;
        Node child = script.FirstChild;
        if (!child.IsNull && child.NextSibling.IsNull && child.Kind == NodeKind.Text)
        {
            text = child.Data.ToString();
            SourceRange data = child.Source.DataRange;
            baseOffset = data.IsPresent && data.End - data.Start == text.Length ? data.Start : -1;
        }
        else if (!child.IsNull)
        {
            text = script.TextContent ?? string.Empty;
        }

        JsonNode? tree = JsonSyntax.TryParse(text, out int error);
        if (tree is null)
        {
            TextRange where = baseOffset >= 0 ? new TextRange(baseOffset + Math.Min(error, text.Length), baseOffset + Math.Min(error, text.Length)) : page.OuterRange(script);
            issues.Add(new ExtractIssue(ExtractIssueKind.MalformedJsonLd, FieldSource.JsonLd, page.ToSpan(where), "The JSON-LD block is not valid JSON, so nothing was read from it."));
            return;
        }

        // Structured markup is read even when hidden; the flag only reports it.
        var reader = new Converter(baseOffset, page.IsHidden(script));
        reader.AddRoots(tree, roots);
    }

    private sealed class Converter
    {
        private readonly int _base;
        private readonly bool _hidden;

        public Converter(int baseOffset, bool hidden)
        {
            _base = baseOffset;
            _hidden = hidden;
        }

        private TextRange Range(JsonNode n, int inset = 0) => _base >= 0 ? new TextRange(_base + n.Start + inset, _base + n.End - inset) : TextRange.None;

        public void AddRoots(JsonNode node, List<MarkupItem> roots)
        {
            switch (node)
            {
                case JsonArray a:
                    foreach (JsonNode n in a.Items)
                    {
                        AddRoots(n, roots);
                    }

                    break;
                case JsonObject o:
                    if (Find(o, "@graph") is JsonNode graph)
                    {
                        // The node holding @graph may be a node itself, or only a wrapper with a context.
                        if (o.Members.Any(m => m.Key == "@type"))
                        {
                            roots.Add(ToItem(o));
                        }

                        AddRoots(graph, roots);
                    }
                    else
                    {
                        roots.Add(ToItem(o));
                    }

                    break;
            }
        }

        private static JsonNode? Find(JsonObject o, string key)
        {
            foreach (KeyValuePair<string, JsonNode> m in o.Members)
            {
                if (m.Key == key)
                {
                    return m.Value;
                }
            }

            return null;
        }

        private MarkupItem ToItem(JsonObject o)
        {
            var item = new MarkupItem(FieldSource.JsonLd) { Range = Range(o), Hidden = _hidden };
            foreach (KeyValuePair<string, JsonNode> m in o.Members)
            {
                switch (m.Key)
                {
                    case "@type":
                        AddTypes(item, m.Value);
                        break;
                    case "@id":
                        if (m.Value is JsonString id)
                        {
                            item.Id = id.Value;
                        }

                        break;
                    case "@context":
                    case "@graph":
                    case "@reverse":
                    case "@index":
                    case "@language":
                        break;
                    default:
                        AddValues(item, VocabularyName.LocalName(m.Key), m.Value);
                        break;
                }
            }

            return item;
        }

        private static void AddTypes(MarkupItem item, JsonNode value)
        {
            switch (value)
            {
                case JsonString s:
                    item.Types.Add(VocabularyName.LocalName(s.Value));
                    break;
                case JsonArray a:
                    foreach (JsonNode n in a.Items)
                    {
                        AddTypes(item, n);
                    }

                    break;
            }
        }

        private void AddValues(MarkupItem item, string name, JsonNode value)
        {
            switch (value)
            {
                case JsonString s:
                    item.Properties.Add(new MarkupProperty(name) { Text = s.Value, Raw = Range(s, 1) });
                    break;
                case JsonScalar n when n.IsNumber:
                    item.Properties.Add(new MarkupProperty(name) { Text = n.Literal, Raw = Range(n) });
                    break;
                case JsonArray a:
                    foreach (JsonNode e in a.Items)
                    {
                        AddValues(item, name, e);
                    }

                    break;
                case JsonObject o:
                    AddObjectValue(item, name, o);
                    break;
            }
        }

        private void AddObjectValue(MarkupItem item, string name, JsonObject o)
        {
            if (Find(o, "@value") is JsonString v)
            {
                item.Properties.Add(new MarkupProperty(name) { Text = v.Value, Raw = Range(v, 1) });
                return;
            }

            if (Find(o, "@list") is JsonNode list)
            {
                AddValues(item, name, list);
                return;
            }

            if (Find(o, "@set") is JsonNode set)
            {
                AddValues(item, name, set);
                return;
            }

            if (o.Members.Count == 1 && o.Members[0].Key == "@id" && o.Members[0].Value is JsonString only)
            {
                item.Properties.Add(new MarkupProperty(name) { Reference = only.Value });
                return;
            }

            item.Properties.Add(new MarkupProperty(name) { Item = ToItem(o) });
        }
    }
}

/// <summary>Reduces a vocabulary term to its local name: <c>http://schema.org/Person</c>, <c>schema:Person</c> and <c>Person</c> all give <c>Person</c>.</summary>
internal static class VocabularyName
{
    public static string LocalName(string term)
    {
        string t = term.Trim();
        if (t.Contains("://", StringComparison.Ordinal))
        {
            int cut = Math.Max(t.LastIndexOf('/'), t.LastIndexOf('#'));
            return cut >= 0 ? t[(cut + 1)..] : t;
        }

        int colon = t.LastIndexOf(':');
        return colon >= 0 ? t[(colon + 1)..] : t;
    }
}
