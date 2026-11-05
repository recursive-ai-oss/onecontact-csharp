using RecursiveAI.OneContact.Extraction.Markup;
using RecursiveAI.OneContact.Extraction.Records;
using RecursiveAI.OneHtml;

namespace RecursiveAI.OneContact.Extraction.Text;

/// <summary>
/// Groups the fields found in visible text into records. Fields belong together when they are in the same block of the page: the
/// same section-like element (a table row, an article, an address element, a list item or section that holds more than one kind of
/// field), the same landmark and the same heading, and not more than four lines apart; for plain text, the same paragraph. A second
/// email address or postal address ends a group, because a person or a company has one main one and a card list would otherwise run
/// together. A group needs at least two kinds of field to be a record: an email address next to a phone number is a contact, an email
/// address alone is a field.
/// </summary>
internal static class TextGrouper
{
    // Always their own block, however few kinds they hold.
    private static readonly HashSet<string> AlwaysBlocks = new(StringComparer.Ordinal) { "tr", "article", "address" };

    private const int MaxGapLines = 4;

    /// <summary>
    /// Builds the records of the groups. Fields that end up in a record are added to <paramref name="grouped"/>.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="view">The visible text the fields were read from.</param>
    /// <param name="items">Fields with a line (<see cref="FieldDraft.Line"/> at least 0), in any order.</param>
    /// <param name="grouped">Receives the fields that were put in a record.</param>
    /// <returns>The records, with their fields.</returns>
    public static List<RecordDraft> Group(PageContext page, TextView view, List<FieldDraft> items, HashSet<FieldDraft> grouped)
    {
        var records = new List<RecordDraft>();
        if (items.Count == 0)
        {
            return records;
        }

        List<FieldDraft> sorted = [.. items.Where(d => d.Line >= 0).OrderBy(d => d.Line).ThenBy(d => d.Raw.Start)];
        Dictionary<int, int[]> chains = [];
        Dictionary<int, HashSet<FieldKind>> kindsIn = [];
        foreach (FieldDraft d in sorted)
        {
            int container = view.Lines[d.Line].Container;
            if (view.IsPlain || container < 0)
            {
                continue;
            }

            if (!chains.TryGetValue(container, out int[]? chain))
            {
                chains[container] = chain = Chain(page, container);
            }

            foreach (int c in chain)
            {
                if (!kindsIn.TryGetValue(c, out HashSet<FieldKind>? set))
                {
                    kindsIn[c] = set = [];
                }

                set.Add(d.Kind);
            }
        }

        List<FieldDraft>? current = null;
        (int Container, int Landmark, int Heading) previousKey = default;
        int previousLine = -1;
        var seen = new Dictionary<FieldKind, List<FieldDraft>>();
        var groups = new List<(List<FieldDraft> Fields, int Container)>();
        int currentContainer = -1;
        foreach (FieldDraft d in sorted)
        {
            TextLine line = view.Lines[d.Line];
            int container = view.IsPlain ? line.Container : Qualifying(page, line.Container, chains, kindsIn);
            var key = (container, line.Landmark, line.Heading);
            bool split = current is null || key != previousKey || d.Line - previousLine > MaxGapLines || Repeats(seen, d);
            if (split)
            {
                current = [];
                currentContainer = container;
                groups.Add((current, currentContainer));
                seen.Clear();
            }

            current!.Add(d);
            if (!seen.TryGetValue(d.Kind, out List<FieldDraft>? list))
            {
                seen[d.Kind] = list = [];
            }

            list.Add(d);
            previousKey = key;
            previousLine = d.Line;
        }

        foreach ((List<FieldDraft> fields, int container) in groups)
        {
            if (fields.Select(f => f.Kind).Distinct().Count() < 2)
            {
                continue;
            }

            ContactRecordKind kind = Decide(fields);
            TextRange hull = TextRange.None;
            foreach (FieldDraft f in fields)
            {
                hull = TextRange.Hull(hull, f.Raw);
            }

            var record = new RecordDraft(kind, FieldSource.Text, hull, view.IsPlain ? -1 : container);
            foreach (FieldDraft f in fields)
            {
                record.Fields.Add(f);
                grouped.Add(f);
            }

            records.Add(record);
        }

        return records;
    }

    // The same value on another line is a second sighting of it, and an address inside an address element that the text rules also read
    // is the same address: neither is a second one.
    private static bool Repeats(Dictionary<FieldKind, List<FieldDraft>> seen, FieldDraft d)
    {
        if (d.Kind is not (FieldKind.Email or FieldKind.Address) || !seen.TryGetValue(d.Kind, out List<FieldDraft>? list))
        {
            return false;
        }

        foreach (FieldDraft other in list)
        {
            bool sameValue = string.Equals(other.Text, d.Text, StringComparison.OrdinalIgnoreCase);
            bool overlaps = other.Raw.IsPresent && d.Raw.IsPresent && other.Raw.Start < d.Raw.End && d.Raw.Start < other.Raw.End;
            if (other.Line != d.Line && !sameValue && !overlaps)
            {
                return true;
            }
        }

        return false;
    }

    // A person when a name is stated; an organization when only a company is; otherwise unknown.
    private static ContactRecordKind Decide(List<FieldDraft> fields)
    {
        bool person = fields.Any(f => f.Kind == FieldKind.Name && !f.CompanyLabel);
        if (person)
        {
            return ContactRecordKind.Person;
        }

        bool company = false;
        foreach (FieldDraft f in fields)
        {
            if (f.CompanyLabel && f.Kind == FieldKind.Organization)
            {
                f.Kind = FieldKind.Name;
                company = true;
            }
        }

        return company ? ContactRecordKind.Organization : ContactRecordKind.Unknown;
    }

    // The enclosing section-like elements of an element, innermost first (the element itself included when it is one).
    private static int[] Chain(PageContext page, int container)
    {
        var chain = new List<int>();
        for (Node n = page.GetNode(container); !n.IsNull; n = n.Parent)
        {
            if (n.IsElement && VisibleTextReader.Containers.Contains(n.LocalName))
            {
                chain.Add(n.Index);
            }
        }

        return [.. chain];
    }

    // The innermost element of the chain that is a block of its own: a row, an article or an address element always is; any other when it holds two kinds of field.
    private static int Qualifying(PageContext page, int container, Dictionary<int, int[]> chains, Dictionary<int, HashSet<FieldKind>> kindsIn)
    {
        if (container < 0 || !chains.TryGetValue(container, out int[]? chain))
        {
            return -1;
        }

        foreach (int c in chain)
        {
            if (AlwaysBlocks.Contains(page.GetNode(c).LocalName) || (kindsIn.TryGetValue(c, out HashSet<FieldKind>? kinds) && kinds.Count >= 2))
            {
                return c;
            }
        }

        return -1;
    }
}
