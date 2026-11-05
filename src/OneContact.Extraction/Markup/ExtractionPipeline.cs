using RecursiveAI.OneContact.Extraction.Records;
using RecursiveAI.OneContact.Extraction.Text;
using RecursiveAI.OneHtml;

namespace RecursiveAI.OneContact.Extraction.Markup;

/// <summary>Runs the readers over one page and assembles the result.</summary>
internal static class ExtractionPipeline
{
    private const int MaxFields = 100_000;

    public static ExtractResult Run(PageContext page, ExtractionSources sources, Region callerDefault)
    {
        var issues = new List<ExtractIssue>();
        var items = new List<MarkupItem>();
        if (page.IsPlainText)
        {
            // A plain string has no markup: only the text rules apply.
            sources &= ExtractionSources.Text;
        }

        if (sources.HasFlag(ExtractionSources.JsonLd))
        {
            JsonLdReader.Read(page, items, issues);
        }

        if (sources.HasFlag(ExtractionSources.Microdata))
        {
            MicrodataReader.Read(page, items);
        }

        if (sources.HasFlag(ExtractionSources.Rdfa))
        {
            RdfaReader.Read(page, items);
        }

        if (sources.HasFlag(ExtractionSources.HCard))
        {
            HCardReader.Read(page, items);
        }

        HashSet<int> consumed = CollectConsumed(page, items);
        var loose = new List<FieldDraft>();
        if (sources.HasFlag(ExtractionSources.Links))
        {
            LooseReaders.ReadLinks(page, consumed, loose);
        }

        if (sources.HasFlag(ExtractionSources.AddressElements))
        {
            LooseReaders.ReadAddressElements(page, consumed, loose);
        }

        var builder = new RecordBuilder(items);
        builder.Build(items);

        bool limited = builder.LimitReached;

        // A loose field joins the innermost record whose element holds it: markup nesting decides, not proximity.
        Dictionary<int, RecordDraft> byElement = [];
        foreach (RecordDraft r in builder.Records)
        {
            if (r.ElementIndex >= 0)
            {
                byElement.TryAdd(r.ElementIndex, r);
            }
        }

        var ungrouped = new List<FieldDraft>();
        foreach (FieldDraft d in loose)
        {
            RecordDraft? home = null;
            for (Node n = page.GetNode(d.ElementIndex); !n.IsNull && home is null; n = n.Parent)
            {
                byElement.TryGetValue(n.Index, out home);
            }

            if (home is not null)
            {
                home.Fields.Add(d);
            }
            else
            {
                ungrouped.Add(d);
            }
        }

        if (sources.HasFlag(ExtractionSources.Text))
        {
            ReadText(page, builder, byElement, ungrouped, issues, callerDefault);
        }

        // Records come in the order their text begins, whichever reader found them (the sort is stable).
        List<RecordDraft> ordered = [.. builder.Records.OrderBy(r => r.Range.IsPresent ? r.Range.Start : int.MaxValue)];
        builder.Records.Clear();
        builder.Records.AddRange(ordered);

        int total = builder.Records.Sum(r => r.Fields.Count) + ungrouped.Count;
        if (total > MaxFields)
        {
            limited = true;
            Truncate(builder.Records, ungrouped, MaxFields);
        }

        var normalizer = new FieldNormalizer(page, callerDefault);
        var works = new List<RecordWork>();
        foreach (RecordDraft r in builder.Records)
        {
            works.Add(new RecordWork(r, normalizer.NormalizeAll(r.Fields, r.Kind)));
        }

        List<ExtractedField> ungroupedFields = normalizer.NormalizeAll(ungrouped, null);
        AttachTextToRecords(works, ungroupedFields);
        if (limited)
        {
            issues.Add(new ExtractIssue(ExtractIssueKind.LimitExceeded, FieldSource.JsonLd, default, "A work budget was used up; the result holds what was read before that."));
        }

        return Assemble(page, sources, works, ungroupedFields, issues);
    }

    // The visible-text half: fields found in the text join the record whose element holds them, or are grouped with their neighbours.
    private static void ReadText(PageContext page, RecordBuilder builder, Dictionary<int, RecordDraft> byElement, List<FieldDraft> ungrouped, List<ExtractIssue> issues, Region callerDefault)
    {
        TextView view;
        if (page.IsPlainText)
        {
            view = PlainTextView.Build(page.Text);
        }
        else
        {
            view = VisibleTextReader.Read(page, out bool truncated);
            if (truncated)
            {
                issues.Add(new ExtractIssue(ExtractIssueKind.LimitExceeded, FieldSource.Text, default, "The visible text is longer than the reader's budget; the text after it was not read."));
            }
        }

        List<FieldDraft> found = TextFieldReader.Read(page, view, (page.PageRegion.IsUnknown ? callerDefault : page.PageRegion) is { IsUnknown: false } textRegion ? new MatcherPhoneFinder(textRegion) : null);
        var pending = new List<FieldDraft>();
        foreach (FieldDraft d in found)
        {
            RecordDraft? home = null;
            for (Node n = d.ElementIndex >= 0 ? page.GetNode(d.ElementIndex) : default; !n.IsNull && home is null; n = n.Parent)
            {
                byElement.TryGetValue(n.Index, out home);
            }

            if (home is not null)
            {
                home.Fields.Add(d);
            }
            else
            {
                pending.Add(d);
            }
        }

        // Links and address elements that no record took sit among the text: they take part in the grouping.
        var loose = new List<FieldDraft>();
        foreach (FieldDraft d in ungrouped)
        {
            if (d.Raw.IsPresent && d.ElementIndex >= 0)
            {
                d.Line = view.LineAtSource(d.Raw.Start, page.OuterRange(page.GetNode(d.ElementIndex)));
                if (d.Line >= 0)
                {
                    loose.Add(d);
                }
            }
        }

        var grouped = new HashSet<FieldDraft>(ReferenceEqualityComparer.Instance);
        List<RecordDraft> groups = TextGrouper.Group(page, view, [.. pending, .. loose], grouped);
        builder.Records.AddRange(groups);
        ungrouped.RemoveAll(grouped.Contains);
        foreach (FieldDraft d in pending)
        {
            if (!grouped.Contains(d))
            {
                ungrouped.Add(d);
            }
        }
    }

    // A text field that no group took, and that repeats a field of exactly one record, is another sighting of that field.
    private static void AttachTextToRecords(List<RecordWork> works, List<ExtractedField> ungrouped)
    {
        for (int i = ungrouped.Count - 1; i >= 0; i--)
        {
            ExtractedField f = ungrouped[i];
            if (f.Source != FieldSource.Text || !f.IsNormalized || f.Kind is FieldKind.Name or FieldKind.Organization)
            {
                continue;
            }

            RecordWork? only = null;
            bool several = false;
            foreach (RecordWork w in works)
            {
                if (w.Fields.Any(x => x.Kind == f.Kind && x.DedupKey == f.DedupKey))
                {
                    several |= only is not null;
                    only ??= w;
                }
            }

            if (only is not null && !several)
            {
                only.Fields.Add(f);
                ungrouped.RemoveAt(i);
            }
        }
    }

    private static void Truncate(List<RecordDraft> records, List<FieldDraft> ungrouped, int max)
    {
        int left = max;
        foreach (RecordDraft r in records)
        {
            if (r.Fields.Count > left)
            {
                r.Fields.RemoveRange(left, r.Fields.Count - left);
            }

            left -= r.Fields.Count;
        }

        if (ungrouped.Count > left)
        {
            ungrouped.RemoveRange(left, ungrouped.Count - left);
        }
    }

    // Elements that already gave a value to a structured reader: links and address elements inside them are not read again.
    private static HashSet<int> CollectConsumed(PageContext page, List<MarkupItem> roots)
    {
        var consumed = new HashSet<int>();
        var stack = new Stack<MarkupItem>(roots);
        int budget = 1_000_000;
        while (stack.Count > 0 && budget-- > 0)
        {
            MarkupItem item = stack.Pop();
            if (item.ElementIndex >= 0 && page.GetNode(item.ElementIndex).LocalName == "address")
            {
                consumed.Add(item.ElementIndex);
            }

            foreach (MarkupProperty p in item.Properties)
            {
                if (p.ElementIndex >= 0)
                {
                    consumed.Add(p.ElementIndex);
                }

                if (p.Item is not null)
                {
                    stack.Push(p.Item);
                }
            }
        }

        return consumed;
    }

    private sealed class RecordWork
    {
        public RecordWork(RecordDraft draft, List<ExtractedField> fields)
        {
            Draft = draft;
            Fields = fields;
        }

        public RecordDraft Draft { get; }

        public List<ExtractedField> Fields { get; }

        public List<(RecordLinkKind Kind, RecordDraft Target)> Links => Draft.Links;

        public ContactRecord? Result { get; set; }
    }

    private static ExtractResult Assemble(PageContext page, ExtractionSources sources, List<RecordWork> works, List<ExtractedField> ungrouped, List<ExtractIssue> issues)
    {
        Dictionary<RecordDraft, RecordWork> workOf = works.ToDictionary(w => w.Draft);
        List<RecordWork> survivors = MergeRecords(works, out Dictionary<RecordWork, RecordWork> redirect);

        var records = new List<ContactRecord>();
        foreach (RecordWork w in survivors)
        {
            w.Fields.Sort(CompareBySpan);
            List<ExtractedField> primary = Deduplicate(w.Fields);
            SourceSpan span = page.ToSpan(w.Draft.Range);
            var record = new ContactRecord(records.Count, w.Draft.Kind, w.Draft.Source, span, w.Draft.Range.IsPresent, primary);
            record.Confidence = primary.Where(f => f.Kind == FieldKind.Name).Select(f => f.Confidence).DefaultIfEmpty(0.5).Max();
            foreach (ExtractedField f in primary)
            {
                f.RecordId = record.Id;
                f.BlockId = -1;
            }

            Disagreements.Find(record);
            w.Result = record;
            records.Add(record);
        }

        foreach (RecordWork w in survivors)
        {
            foreach ((RecordLinkKind kind, RecordDraft target) in w.Links)
            {
                RecordWork? t = workOf.TryGetValue(target, out RecordWork? tw) ? Resolve(tw, redirect) : null;
                if (t?.Result is ContactRecord other && !ReferenceEquals(other, w.Result))
                {
                    w.Result!.AddLink(kind, other);
                }
            }
        }

        ungrouped.Sort(CompareBySpan);
        List<ExtractedField> ungroupedPrimary = Deduplicate(ungrouped);

        var flat = new List<ExtractedField>();
        foreach (ContactRecord r in records)
        {
            flat.AddRange(r.Fields);
        }

        flat.AddRange(ungroupedPrimary);
        flat.Sort(CompareBySpan);
        for (int i = 0; i < flat.Count; i++)
        {
            flat[i].Id = i;
            foreach (ExtractedField d in flat[i].Duplicates)
            {
                d.Id = i;
                d.RecordId = flat[i].RecordId;
            }
        }

        return new ExtractResult(sources, page.HasBytes ? OffsetUnit.Bytes : OffsetUnit.Chars, records, flat, issues);
    }

    // Where two fields start at the same place (an itemprop and the text it holds), the markup reading comes first.
    private static int CompareBySpan(ExtractedField a, ExtractedField b)
    {
        int x = a.HasSpan ? a.Span.Start : int.MaxValue;
        int y = b.HasSpan ? b.Span.Start : int.MaxValue;
        int byStart = x.CompareTo(y);
        return byStart != 0 ? byStart : (a.Source == FieldSource.Text).CompareTo(b.Source == FieldSource.Text);
    }

    // A record that was merged into another forwards to it.
    private static RecordWork Resolve(RecordWork w, Dictionary<RecordWork, RecordWork> redirect) => redirect.TryGetValue(w, out RecordWork? t) ? Resolve(t, redirect) : w;

    /// <summary>
    /// De-duplicates records within the page. Two records of one kind are the same entity when they share a name key or a contact key:
    /// an email address that is not a role account, or (for organizations) a website that is not a social profile. A shared contact
    /// key merges records whose names differ; the two names are then kept and reported as a disagreement. Phone numbers never
    /// decide: a main line is shared by the people behind it.
    /// </summary>
    private static List<RecordWork> MergeRecords(List<RecordWork> works, out Dictionary<RecordWork, RecordWork> redirect)
    {
        redirect = new Dictionary<RecordWork, RecordWork>(ReferenceEqualityComparer.Instance);
        var survivors = new List<RecordWork>();
        var index = new Dictionary<string, RecordWork>(StringComparer.Ordinal);
        foreach (RecordWork w in works)
        {
            if (w.Draft.Kind == ContactRecordKind.Unknown)
            {
                continue;
            }

            List<string> keys = [.. KeysOf(w, FieldKind.Name), .. ContactKeys(w)];
            RecordWork? target = null;
            foreach (string k in keys)
            {
                if (index.TryGetValue(k, out target))
                {
                    break;
                }
            }

            if (target is null)
            {
                survivors.Add(w);
                target = w;
            }
            else
            {
                target.Fields.AddRange(w.Fields);
                redirect[w] = target;
            }

            foreach (string k in keys)
            {
                index.TryAdd(k, target);
            }
        }

        // Fields that sit together in text with nothing saying whose they are (kind Unknown) join a record of either kind that has the
        // same email address, an organization that has the same website or the same address, or an earlier group of the same kind.
        // A role account (info@, sales@) is what an organization lists as its own, so for an organization it counts here, though it never
        // merges two records of the markup.
        var addresses = new Dictionary<string, RecordWork>(StringComparer.Ordinal);
        var organizationEmails = new Dictionary<string, RecordWork>(StringComparer.Ordinal);
        foreach (RecordWork w in survivors.Where(w => w.Draft.Kind == ContactRecordKind.Organization))
        {
            foreach (ExtractedField f in w.Fields.Where(f => f.IsNormalized))
            {
                if (f.Kind == FieldKind.Address)
                {
                    addresses.TryAdd(f.DedupKey, w);
                }
                else if (f.Kind == FieldKind.Email)
                {
                    organizationEmails.TryAdd(f.DedupKey, w);
                }
            }
        }

        foreach (RecordWork w in works)
        {
            if (w.Draft.Kind != ContactRecordKind.Unknown)
            {
                continue;
            }

            var own = new List<string>();
            var lookups = new List<string>();
            RecordWork? target = null;
            foreach (ExtractedField f in w.Fields)
            {
                bool role = f.Facts.Contains("RoleAccount");
                bool social = f.Facts.Any(x => x.StartsWith("Social", StringComparison.Ordinal));
                if (!f.IsNormalized || social)
                {
                    continue;
                }

                if (f.Kind == FieldKind.Email)
                {
                    own.Add("Unknown|Email|" + f.DedupKey);
                    if (target is null && organizationEmails.TryGetValue(f.DedupKey, out RecordWork? org))
                    {
                        target = org;
                    }

                    if (!role)
                    {
                        lookups.Add("Person|Email|" + f.DedupKey);
                        lookups.Add("Organization|Email|" + f.DedupKey);
                    }
                }
                else if (f.Kind == FieldKind.Website)
                {
                    own.Add("Unknown|Website|" + f.DedupKey);
                    lookups.Add("Organization|Website|" + f.DedupKey);
                }
            }

            if (target is null)
            {
                foreach (string k in lookups.Concat(own))
                {
                    if (index.TryGetValue(k, out target))
                    {
                        break;
                    }
                }
            }

            if (target is null)
            {
                foreach (ExtractedField f in w.Fields.Where(f => f.Kind == FieldKind.Address && f.IsNormalized))
                {
                    if (addresses.TryGetValue(f.DedupKey, out target))
                    {
                        break;
                    }
                }
            }

            if (target is null)
            {
                survivors.Add(w);
                target = w;
            }
            else
            {
                target.Fields.AddRange(w.Fields);
                redirect[w] = target;
            }

            foreach (string k in own)
            {
                index.TryAdd(k, target);
            }
        }

        // Survivors keep the order their records began in.
        var position = new Dictionary<RecordWork, int>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < works.Count; i++)
        {
            position[works[i]] = i;
        }

        survivors.Sort((x, y) => position[x].CompareTo(position[y]));
        return survivors;
    }

    private static List<string> KeysOf(RecordWork w, FieldKind kind)
    {
        var keys = new List<string>();
        foreach (ExtractedField f in w.Fields)
        {
            if (f.Kind == kind && !keys.Contains(w.Draft.Kind + "|" + f.DedupKey))
            {
                keys.Add(w.Draft.Kind + "|" + f.DedupKey);
            }
        }

        return keys;
    }

    private static List<string> ContactKeys(RecordWork w)
    {
        var keys = new List<string>();
        foreach (ExtractedField f in w.Fields)
        {
            bool shared = f.Facts.Contains("RoleAccount") || f.Facts.Any(x => x.StartsWith("Social", StringComparison.Ordinal));
            bool use = f.Kind == FieldKind.Email || (f.Kind == FieldKind.Website && w.Draft.Kind == ContactRecordKind.Organization);
            if (use && f.IsNormalized && !shared)
            {
                keys.Add(w.Draft.Kind + "|" + f.Kind + "|" + f.DedupKey);
            }
        }

        return keys;
    }

    // The same value read twice for one record is one field; the other sightings stay reachable from it.
    private static List<ExtractedField> Deduplicate(List<ExtractedField> fields)
    {
        var primary = new List<ExtractedField>();
        var seen = new Dictionary<string, ExtractedField>(StringComparer.Ordinal);
        var dups = new Dictionary<ExtractedField, List<ExtractedField>>(ReferenceEqualityComparer.Instance);
        foreach (ExtractedField f in fields)
        {
            string key = f.Kind + "|" + f.DedupKey;
            if (seen.TryGetValue(key, out ExtractedField? first))
            {
                if (!dups.TryGetValue(first, out List<ExtractedField>? list))
                {
                    dups[first] = list = [];
                }

                list.Add(f);
                continue;
            }

            seen[key] = f;
            primary.Add(f);
        }

        foreach ((ExtractedField first, List<ExtractedField> list) in dups)
        {
            first.Duplicates = list;
        }

        return primary;
    }
}
