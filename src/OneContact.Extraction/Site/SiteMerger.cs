using RecursiveAI.OneContact.Extraction.Markup;
using RecursiveAI.OneContact.Extraction.Records;

namespace RecursiveAI.OneContact.Extraction;

/// <summary>
/// Merges the per-page results of one site. Records of one kind are the same entity when they share a name key, or a contact key: an
/// email address that is not a role account or (organizations) a website that is not a social profile. These are the keys a single
/// page uses; phone numbers never decide. One rule is added for pages: two organizations with addresses that share none are branches,
/// and a shared website or email alone does not merge them (a shared name still does).
/// </summary>
internal static class SiteMerger
{
    public static SiteExtractResult Merge(ExtractionSources sources, Region region, List<SiteExtraction.Entry> pages, HashSet<string> siteRegionPages)
    {
        var works = new List<Work>();
        var workOf = new Dictionary<ContactRecord, Work>(ReferenceEqualityComparer.Instance);
        int sequence = 0;
        foreach (SiteExtraction.Entry page in pages)
        {
            foreach (ContactRecord r in page.Result.Records)
            {
                var w = new Work(sequence++, r, page);
                foreach (ExtractedField f in r.Fields)
                {
                    w.Fields.Add(f.CloneForPage(page.Id, page.Index));
                }

                works.Add(w);
            }
        }

        List<Work> survivors = MergeWorks(works, workOf);

        var ungrouped = new List<ExtractedField>();
        foreach (SiteExtraction.Entry page in pages)
        {
            foreach (ExtractedField f in page.Result.Fields)
            {
                if (f.RecordId < 0)
                {
                    ungrouped.Add(f.CloneForPage(page.Id, page.Index));
                }
            }
        }

        AttachTextToRecords(survivors, ungrouped);

        var records = new List<ContactRecord>();
        foreach (Work w in survivors)
        {
            w.Fields.Sort(CompareFields);
            List<ExtractedField> primary = Deduplicate(w.Fields);
            Work first = w.Members[0];
            ContactRecord head = first.Source;
            var record = new ContactRecord(records.Count, w.Kind, head.Source, head.Span, head.HasSpan, primary)
            {
                Confidence = primary.Where(f => f.Kind == FieldKind.Name).Select(f => f.Confidence).DefaultIfEmpty(0.5).Max(),
                Pages = [.. w.Members.Select(m => m.Page.Id).Distinct(StringComparer.Ordinal)],
            };
            foreach (ExtractedField f in primary)
            {
                f.RecordId = record.Id;
                foreach (ExtractedField d in f.Duplicates)
                {
                    d.RecordId = record.Id;
                }
            }

            w.Result = record;
            records.Add(record);
        }

        foreach (Work w in survivors)
        {
            foreach (Work m in w.Members)
            {
                foreach (RecordLink l in m.Source.Links)
                {
                    if (workOf.TryGetValue(l.Target, out Work? t) && t.Result is { } other && !ReferenceEquals(other, w.Result))
                    {
                        w.Result!.AddLink(l.Kind, other);
                    }
                }
            }
        }

        LinkPeopleToOrganizations(survivors);
        foreach (ContactRecord r in records)
        {
            Disagreements.Find(r);
        }

        ungrouped.Sort(CompareFields);
        List<ExtractedField> ungroupedPrimary = Deduplicate(ungrouped);

        var flat = new List<ExtractedField>();
        foreach (ContactRecord r in records)
        {
            flat.AddRange(r.Fields);
        }

        flat.AddRange(ungroupedPrimary);
        flat.Sort(CompareFields);
        for (int i = 0; i < flat.Count; i++)
        {
            flat[i].Id = i;
            foreach (ExtractedField d in flat[i].Duplicates)
            {
                d.Id = i;
                d.RecordId = flat[i].RecordId;
            }
        }

        var sitePages = new List<SitePage>();
        var issues = new List<SiteIssue>();
        foreach (SiteExtraction.Entry p in pages)
        {
            sitePages.Add(new SitePage(p.Id, p.Result.OffsetUnit, p.Result.PageRegion, siteRegionPages.Contains(p.Id), p.Result.Fields.Count));
            foreach (ExtractIssue i in p.Result.Issues)
            {
                issues.Add(new SiteIssue(p.Id, i));
            }
        }

        return new SiteExtractResult(sources, region, sitePages, records, flat, issues, SiteOrganization(records));
    }

    // As on a single page: a value found in text that no record claims joins the one record that already holds it. Here the record may
    // have it from another page. Links, which only suggest a role, stay where they are.
    private static void AttachTextToRecords(List<Work> survivors, List<ExtractedField> ungrouped)
    {
        for (int i = ungrouped.Count - 1; i >= 0; i--)
        {
            ExtractedField f = ungrouped[i];
            if (f.Source != FieldSource.Text || !f.IsNormalized || f.Kind is FieldKind.Name or FieldKind.Organization)
            {
                continue;
            }

            Work? only = null;
            bool several = false;
            foreach (Work w in survivors)
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

    // Where two fields start at the same place the markup reading comes first, as on a single page; pages come in the order supplied.
    private static int CompareFields(ExtractedField a, ExtractedField b)
    {
        int byPage = a.PageIndex.CompareTo(b.PageIndex);
        if (byPage != 0)
        {
            return byPage;
        }

        int x = a.HasSpan ? a.Span.Start : int.MaxValue;
        int y = b.HasSpan ? b.Span.Start : int.MaxValue;
        int byStart = x.CompareTo(y);
        return byStart != 0 ? byStart : (a.Source == FieldSource.Text).CompareTo(b.Source == FieldSource.Text);
    }

    // The organization that turns up on the most pages is the one the site is about.
    private static ContactRecord? SiteOrganization(List<ContactRecord> records)
    {
        ContactRecord? best = null;
        foreach (ContactRecord r in records)
        {
            if (r.Kind == ContactRecordKind.Organization && (best is null || r.Pages.Count > best.Pages.Count))
            {
                best = r;
            }
        }

        return best;
    }

    // A person is tied to an organization where the pages say so: through the markup (already linked), or by an employer named on any
    // page whose name is that organization's. Sharing a page proves nothing (a quoted customer is not staff), so nothing else is inferred.
    private static void LinkPeopleToOrganizations(List<Work> survivors)
    {
        var orgByName = new Dictionary<string, ContactRecord>(StringComparer.Ordinal);
        foreach (Work w in survivors.Where(w => w.Kind == ContactRecordKind.Organization))
        {
            foreach (ExtractedField n in w.Result!.Names)
            {
                orgByName.TryAdd(n.DedupKey, w.Result);
            }
        }

        foreach (Work w in survivors.Where(w => w.Kind == ContactRecordKind.Person))
        {
            ContactRecord person = w.Result!;
            if (person.Organization is not null)
            {
                continue;
            }

            ContactRecord? target = null;
            foreach (ExtractedField employer in person.Organizations)
            {
                if (orgByName.TryGetValue(employer.DedupKey, out target))
                {
                    break;
                }
            }

            if (target is not null)
            {
                person.AddLink(RecordLinkKind.WorksFor, target);
                target.AddLink(RecordLinkKind.HasMember, person);
            }
        }
    }

    private static List<Work> MergeWorks(List<Work> works, Dictionary<ContactRecord, Work> workOf)
    {
        var survivors = new List<Work>();
        var index = new Dictionary<string, Work>(StringComparer.Ordinal);
        foreach (Work w in works.Where(w => w.Kind != ContactRecordKind.Unknown))
        {
            List<string> nameKeys = NameKeys(w);
            List<string> contactKeys = ContactKeys(w);
            Work? target = null;
            foreach (string k in nameKeys)
            {
                if (index.TryGetValue(k, out target))
                {
                    break;
                }
            }

            if (target is null)
            {
                foreach (string k in contactKeys)
                {
                    if (index.TryGetValue(k, out Work? candidate) && !AreBranches(w, candidate))
                    {
                        target = candidate;
                        break;
                    }
                }
            }

            target = Join(w, target, survivors, workOf);
            foreach (string k in nameKeys.Concat(contactKeys))
            {
                index.TryAdd(k, target);
            }
        }

        // Fields that sit together in text with nothing saying whose they are (kind Unknown) join a record of either kind that has the
        // same email address, an organization that has the same website or the same address, or an earlier group of the same kind.
        // A role account (info@, sales@) is what an organization lists as its own, so for an organization it counts here, though it
        // never merges two records of the markup. These are the rules of a single page.
        var addresses = new Dictionary<string, Work>(StringComparer.Ordinal);
        var organizationEmails = new Dictionary<string, Work>(StringComparer.Ordinal);
        foreach (Work w in survivors.Where(w => w.Kind == ContactRecordKind.Organization))
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

        foreach (Work w in works.Where(w => w.Kind == ContactRecordKind.Unknown))
        {
            var own = new List<string>();
            var lookups = new List<string>();
            Work? target = null;
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
                    if (target is null && organizationEmails.TryGetValue(f.DedupKey, out Work? org))
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

            target = Join(w, target, survivors, workOf);
            foreach (string k in own)
            {
                index.TryAdd(k, target);
            }
        }

        // Survivors keep the order their first record came in.
        survivors.Sort((x, y) => x.Sequence.CompareTo(y.Sequence));
        return survivors;
    }

    private static Work Join(Work w, Work? target, List<Work> survivors, Dictionary<ContactRecord, Work> workOf)
    {
        if (target is null)
        {
            survivors.Add(w);
            w.Members.Add(w);
            target = w;
        }
        else
        {
            target.Fields.AddRange(w.Fields);
            target.Members.Add(w);
        }

        workOf[w.Source] = target;
        return target;
    }

    // Two organizations whose addresses share none are two places, not one entity seen twice.
    private static bool AreBranches(Work a, Work b)
    {
        if (a.Kind != ContactRecordKind.Organization || b.Kind != ContactRecordKind.Organization)
        {
            return false;
        }

        HashSet<string> x = AddressKeys(a);
        HashSet<string> y = AddressKeys(b);
        return x.Count > 0 && y.Count > 0 && !x.Overlaps(y);
    }

    private static HashSet<string> AddressKeys(Work w) =>
        [.. w.Fields.Where(f => f.Kind == FieldKind.Address && f.IsNormalized).Select(f => f.DedupKey)];

    private static List<string> NameKeys(Work w)
    {
        var keys = new List<string>();
        foreach (ExtractedField f in w.Fields)
        {
            string k = w.Kind + "|" + f.DedupKey;
            if (f.Kind == FieldKind.Name && !keys.Contains(k))
            {
                keys.Add(k);
            }
        }

        return keys;
    }

    private static List<string> ContactKeys(Work w)
    {
        var keys = new List<string>();
        foreach (ExtractedField f in w.Fields)
        {
            bool shared = f.Facts.Contains("RoleAccount") || f.Facts.Any(x => x.StartsWith("Social", StringComparison.Ordinal));
            bool use = f.Kind == FieldKind.Email || (f.Kind == FieldKind.Website && w.Kind == ContactRecordKind.Organization);
            if (use && f.IsNormalized && !shared)
            {
                keys.Add(w.Kind + "|" + f.Kind + "|" + f.DedupKey);
            }
        }

        return keys;
    }

    // A value read twice for one record is one field, whichever page it came from; the other sightings stay reachable from it.
    private static List<ExtractedField> Deduplicate(List<ExtractedField> fields)
    {
        var primary = new List<ExtractedField>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (ExtractedField f in fields)
        {
            string key = f.Kind + "|" + f.DedupKey;
            if (seen.TryGetValue(key, out int at))
            {
                ExtractedField first = primary[at];
                // Across pages the sighting in markup stands for the value and the same text found in the visible text of another page is its
                // echo; on one page the order of the page decides, as on a single page.
                if (first.Source == FieldSource.Text && f.Source != FieldSource.Text && f.PageIndex != first.PageIndex)
                {
                    f.Duplicates = [.. f.Duplicates, first, .. first.Duplicates];
                    first.Duplicates = [];
                    primary[at] = f;
                }
                else
                {
                    first.Duplicates = [.. first.Duplicates, f, .. f.Duplicates];
                    f.Duplicates = [];
                }

                continue;
            }

            seen[key] = primary.Count;
            primary.Add(f);
        }

        // A sighting that took over the place of a text echo from an earlier page goes back to its own place in the order.
        return [.. primary.OrderBy(f => f, Comparer<ExtractedField>.Create(CompareFields))];
    }

    private sealed class Work(int sequence, ContactRecord source, SiteExtraction.Entry page)
    {
        public int Sequence { get; } = sequence;

        /// <summary>The record as its page gave it.</summary>
        public ContactRecord Source { get; } = source;

        public SiteExtraction.Entry Page { get; } = page;

        public ContactRecordKind Kind => Source.Kind;

        /// <summary>The page records merged into this one, the first being the one that began it. Empty for a record that was merged away.</summary>
        public List<Work> Members { get; } = [];

        public List<ExtractedField> Fields { get; } = [];

        public ContactRecord? Result { get; set; }
    }
}
