using RecursiveAI.OneContact.Extraction.Records;

namespace RecursiveAI.OneContact.Extraction.Markup;

/// <summary>Finds the fields of a record whose values do not fit together, marks them <see cref="ExtractedFieldFacts.Disputed"/> and lists them.</summary>
internal static class Disagreements
{
    // Addresses and organizations are not compared: two spellings of one address have different keys, and a person can work for
    // several organizations, so a difference there says nothing.
    private static readonly FieldKind[] Compared = [FieldKind.Name, FieldKind.Phone, FieldKind.Email, FieldKind.Website];

    public static void Find(ContactRecord record)
    {
        foreach (FieldKind kind in Compared)
        {
            IReadOnlyList<ExtractedField> fields = record.Of(kind);
            if (fields.Count < 2 || !Disagree(kind, fields))
            {
                continue;
            }

            foreach (ExtractedField f in fields)
            {
                f.Flags |= ExtractedFieldFacts.Disputed;
                foreach (ExtractedField d in f.Duplicates)
                {
                    d.Flags |= ExtractedFieldFacts.Disputed;
                }
            }

            record.AddDisagreement(new FieldDisagreement(kind, fields));
        }
    }

    private static bool Disagree(FieldKind kind, IReadOnlyList<ExtractedField> fields)
    {
        // A record has one name: two different ones are a disagreement wherever they come from.
        if (kind == FieldKind.Name)
        {
            return fields.Select(f => f.DedupKey).Distinct(StringComparer.Ordinal).Count() > 1;
        }

        // Other kinds can have many values. They disagree when two sources (on one page, or on different pages of a site) each report values and share none of them.
        var bySource = new Dictionary<(string?, FieldSource), HashSet<string>>();
        foreach (ExtractedField f in fields)
        {
            Add(bySource, f.PageId, f.Source, f.DedupKey);
            foreach (ExtractedField d in f.Duplicates)
            {
                Add(bySource, d.PageId, d.Source, d.DedupKey);
            }
        }

        List<(string?, FieldSource)> sources = [.. bySource.Keys];
        for (int i = 0; i < sources.Count; i++)
        {
            for (int j = i + 1; j < sources.Count; j++)
            {
                if (!bySource[sources[i]].Overlaps(bySource[sources[j]]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void Add(Dictionary<(string?, FieldSource), HashSet<string>> map, string? page, FieldSource source, string key)
    {
        if (!map.TryGetValue((page, source), out HashSet<string>? set))
        {
            map[(page, source)] = set = new HashSet<string>(StringComparer.Ordinal);
        }

        set.Add(key);
    }
}
