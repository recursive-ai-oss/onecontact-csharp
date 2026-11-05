using System.Text;
using RecursiveAI.OneContact.Extraction;
using RecursiveAI.OneContact.Extraction.Records;

namespace RecursiveAI.OneContact.Tests;

/// <summary>Sites of a few pages written for these tests (example domains, 555-01xx numbers) and their golden results.</summary>
public sealed class ExtractionSiteTests
{
    private static string SitesRoot => Path.Combine(ExtractionFixtures.Root, "sites");

    private static IEnumerable<string> SiteDirs() => Directory.EnumerateDirectories(SitesRoot).Order(StringComparer.Ordinal);

    private static IEnumerable<string> FilesOf(string dir) =>
        Directory.EnumerateFiles(dir).Where(f => !f.EndsWith("golden.txt", StringComparison.Ordinal)).Order(StringComparer.Ordinal);

    private static List<ContactPage> BytePages(string dir) =>
        [.. FilesOf(dir).Select(f => f.EndsWith(".txt", StringComparison.Ordinal)
            ? ContactPage.FromText(Path.GetFileName(f), File.ReadAllText(f))
            : ContactPage.FromHtml(Path.GetFileName(f), File.ReadAllBytes(f)))];

    private static List<ContactPage> StringPages(string dir) =>
        [.. FilesOf(dir).Select(f => f.EndsWith(".txt", StringComparison.Ordinal)
            ? ContactPage.FromText(Path.GetFileName(f), File.ReadAllText(f))
            : ContactPage.FromHtml(Path.GetFileName(f), File.ReadAllText(f)))];

    private static SiteExtractResult Site(string name) => new ContactExtractor().ExtractSite(BytePages(Path.Combine(SitesRoot, name)));

    [Fact]
    public void There_are_at_least_fifteen_sites()
    {
        Assert.True(SiteDirs().Count() >= 15);
    }

    [Fact]
    public void Every_site_gives_its_golden_result()
    {
        string? update = Environment.GetEnvironmentVariable("ONECONTACT_UPDATE_GOLDEN");
        var failures = new List<string>();
        foreach (string dir in SiteDirs())
        {
            string name = Path.GetFileName(dir);
            string actual = Dump(new ContactExtractor().ExtractSite(BytePages(dir)));
            if (!string.IsNullOrEmpty(update))
            {
                Directory.CreateDirectory(update);
                File.WriteAllText(Path.Combine(update, name + ".txt"), actual, new UTF8Encoding(false));
                continue;
            }

            string golden = Path.Combine(dir, "golden.txt");
            if (!File.Exists(golden) || File.ReadAllText(golden) != actual)
            {
                failures.Add(name);
            }
        }

        Assert.True(failures.Count == 0, "differs from golden: " + string.Join(", ", failures));
    }

    // Done when: every field and every sighting of it points into its own page.
    [Fact]
    public void Offsets_point_into_the_page_each_field_came_from()
    {
        foreach (string dir in SiteDirs())
        {
            foreach (bool asBytes in new[] { true, false })
            {
                SiteExtractResult site = new ContactExtractor().ExtractSite(asBytes ? BytePages(dir) : StringPages(dir));
                foreach (ExtractedField f in site.Fields)
                {
                    foreach (ExtractedField sighting in new[] { f }.Concat(f.Duplicates))
                    {
                        Assert.NotNull(sighting.PageId);
                        if (!sighting.HasSpan)
                        {
                            continue;
                        }

                        string file = Path.Combine(dir, sighting.PageId!);
                        string actual = asBytes
                            ? Encoding.UTF8.GetString(File.ReadAllBytes(file).AsSpan(sighting.Span.Start, sighting.Span.Length))
                            : File.ReadAllText(file).Substring(sighting.Span.Start, sighting.Span.Length);
                        Assert.True(actual == sighting.Raw, $"{Path.GetFileName(dir)}/{sighting.PageId}: '{actual}' vs '{sighting.Raw}'");
                    }
                }

                Assert.All(site.Pages, p => Assert.Equal(asBytes && !p.Id.EndsWith(".txt", StringComparison.Ordinal) ? OffsetUnit.Bytes : OffsetUnit.Chars, p.OffsetUnit));
            }
        }
    }

    // A site of one page is that page: the merge adds nothing and loses nothing.
    [Fact]
    public void A_site_of_one_page_equals_the_page_alone()
    {
        var failures = new List<string>();
        foreach (string file in ExtractionFixtures.PageFiles().Concat(ExtractionFixtures.TextPageFiles()))
        {
            byte[] bytes = File.ReadAllBytes(file);
            foreach (ExtractionSources sources in new[] { ExtractionSources.Markup, ExtractionSources.All })
            {
                var extractor = new ContactExtractor { Sources = sources };
                string single = WithoutLinks(ExtractionFixtures.Dump(extractor.ExtractHtml(bytes)));
                SiteExtractResult site = extractor.ExtractSite([ContactPage.FromHtml("p", bytes)]);
                string merged = WithoutLinks(ExtractionFixtures.DumpParts("Bytes", site.Records, site.Ungrouped, site.Issues.Select(i => i.Issue)));
                if (single != merged)
                {
                    string[] x = single.Split('\n');
                    string[] y = merged.Split('\n');
                    int at = 0;
                    while (at < x.Length && at < y.Length && x[at] == y[at])
                    {
                        at++;
                    }

                    failures.Add(ExtractionFixtures.NameOf(file) + " " + sources + "\n  single: " + (at < x.Length ? x[at] : "(end)") + "\n  site:   " + (at < y.Length ? y[at] : "(end)"));
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    // The site ties a person to the one organization of their page; a single page leaves such a person unlinked, so links are left out.
    private static string WithoutLinks(string dump) =>
        string.Join('\n', dump.Split('\n').Where(l => !l.StartsWith("  link ", StringComparison.Ordinal)));

    [Fact]
    public async Task The_stream_form_gives_the_same_result()
    {
        foreach (string dir in SiteDirs())
        {
            string sync = Dump(new ContactExtractor().ExtractSite(BytePages(dir)));
            string streamed = Dump(await new ContactExtractor().ExtractSiteAsync(Stream(BytePages(dir)), TestContext.Current.CancellationToken));
            Assert.Equal(sync, streamed);
        }

        static async IAsyncEnumerable<ContactPage> Stream(List<ContactPage> pages)
        {
            foreach (ContactPage p in pages)
            {
                await Task.Yield();
                yield return p;
            }
        }
    }

    [Fact]
    public void Page_ids_must_be_unique_and_pages_must_be_built()
    {
        var extractor = new ContactExtractor();
        Assert.Throws<ArgumentException>(() => extractor.ExtractSite([ContactPage.FromText("a", "x"), ContactPage.FromText("a", "y")]));
        Assert.Throws<ArgumentException>(() => extractor.ExtractSite([default]));
        Assert.Throws<ArgumentException>(() => ContactPage.FromText("", "x"));
        Assert.Throws<ArgumentNullException>(() => extractor.ExtractSite(null!));
        SiteExtraction one = extractor.BeginSite();
        _ = one.Complete();
        Assert.Throws<InvalidOperationException>(() => one.Add(ContactPage.FromText("a", "x")));
        Assert.Throws<InvalidOperationException>(() => one.Complete());
    }

    [Fact]
    public void The_same_organization_on_three_pages_is_one_record_with_every_sighting()
    {
        SiteExtractResult site = Site("duplicate-org");
        ContactRecord org = Assert.Single(site.Records, r => r.Kind == ContactRecordKind.Organization);
        Assert.Equal(["contact.html", "index.html"], org.Pages.Order(StringComparer.Ordinal));
        ExtractedField phone = Assert.Single(org.Phones, p => p.Normalized.Canonical == "+12025550143");
        Assert.Equal(["contact.html", "index.html"], new[] { phone }.Concat(phone.Duplicates).Select(d => d.PageId!).Distinct().Order(StringComparer.Ordinal));
        // The link on the third page is not tied to the record: it names no entity, so it stays a field of its own.
        Assert.Contains(site.Ungrouped, f => f.PageId == "about.html" && f.Kind == FieldKind.Phone);
        Assert.Same(org, site.Organization);
    }

    [Fact]
    public void A_stale_phone_on_another_page_stays_and_is_disputed()
    {
        SiteExtractResult site = Site("stale-phone");
        ContactRecord org = Assert.Single(site.Records);
        Assert.Equal(2, org.Phones.Count);
        Assert.All(org.Phones, p => Assert.True(p.Flags.HasFlag(ExtractedFieldFacts.Disputed)));
        Assert.Contains(org.Disagreements, d => d.Kind == FieldKind.Phone);
        Assert.Equal(["contact.html", "index.html"], org.Phones.Select(p => p.PageId!).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Branches_with_their_own_addresses_are_not_merged_into_the_head_office()
    {
        SiteExtractResult site = Site("multi-location");
        List<ContactRecord> orgs = [.. site.Records.Where(r => r.Kind == ContactRecordKind.Organization)];
        Assert.Equal(3, orgs.Count);
        Assert.Contains(orgs, o => o.Name!.Text == "Elm Coffee Denver" && o.Phones.Count == 1);
        Assert.Contains(orgs, o => o.Name!.Text == "Elm Coffee Austin" && o.Phones.Count == 1);
        ContactRecord head = orgs.Single(o => o.Name!.Text == "Elm Coffee Roasters Inc");
        Assert.Equal(2, head.Pages.Count);
        Assert.Single(head.Addresses);
    }

    [Fact]
    public void People_are_linked_to_the_organization_the_pages_support()
    {
        SiteExtractResult named = Site("staff-named-employer");
        ContactRecord org = Assert.Single(named.Records, r => r.Kind == ContactRecordKind.Organization);
        foreach (ContactRecord person in named.Records.Where(r => r.Kind == ContactRecordKind.Person))
        {
            Assert.Same(org, person.Organization);
        }

        Assert.Equal(2, org.Links.Count(l => l.Kind == RecordLinkKind.HasMember));

        // Sharing a page with the only organization is not support: the people stay unlinked.
        SiteExtractResult same = Site("staff-same-page");
        Assert.Equal(2, same.Records.Count(r => r.Kind == ContactRecordKind.Person));
        Assert.All(same.Records.Where(r => r.Kind == ContactRecordKind.Person), r => Assert.Null(r.Organization));

        // Two people sharing a role account are not one person, and nobody is linked to an organization that no page names.
        SiteExtractResult role = Site("role-account");
        Assert.Equal(2, role.Records.Count(r => r.Kind == ContactRecordKind.Person));
        Assert.All(role.Records, r => Assert.Null(r.Organization));
    }

    [Fact]
    public void A_page_without_a_region_is_read_in_the_one_region_the_site_names()
    {
        SiteExtractResult site = Site("region-from-site");
        Assert.Equal(Region.US, site.Region);
        Assert.True(site.Pages.Single(p => p.Id == "contact.html").RegionFromSite);
        Assert.False(site.Pages.Single(p => p.Id == "index.html").RegionFromSite);
        ExtractedField phone = Assert.Single(site.Of(FieldKind.Phone));
        Assert.True(phone.IsNormalized);
        Assert.True(phone.Flags.HasFlag(ExtractedFieldFacts.RegionInferred));
        Assert.Equal("contact.html", phone.PageId);

        // Two regions on the site: nothing is chosen, so the national numbers stay unparsed with the reason.
        SiteExtractResult two = Site("region-conflict");
        Assert.True(two.Region.IsUnknown);
        ExtractedField bare = Assert.Single(two.Fields, f => f.PageId == "contact.html" && f.Kind == FieldKind.Phone);
        Assert.False(bare.IsNormalized);
        Assert.Equal(ReasonCode.PhoneRegionRequired, bare.Issue);
    }

    [Fact]
    public void A_callers_default_region_is_never_replaced_by_the_site()
    {
        string dir = Path.Combine(SitesRoot, "region-from-site");
        SiteExtractResult site = new ContactExtractor { DefaultRegion = Region.CA }.ExtractSite(BytePages(dir));
        Assert.All(site.Pages, p => Assert.False(p.RegionFromSite));
    }

    [Fact]
    public void A_malformed_block_on_one_page_is_reported_with_the_page_and_costs_nothing_else()
    {
        SiteExtractResult site = Site("malformed-page");
        SiteIssue issue = Assert.Single(site.Issues);
        Assert.Equal("about.html", issue.PageId);
        Assert.Equal(ExtractIssueKind.MalformedJsonLd, issue.Issue.Kind);
        Assert.Single(site.Records, r => r.Kind == ContactRecordKind.Organization);
    }

    [Fact]
    public void Links_repeated_on_every_page_are_one_field_with_all_sightings()
    {
        SiteExtractResult site = Site("footer-links");
        ExtractedField mail = Assert.Single(site.Of(FieldKind.Email));
        Assert.Equal(2, mail.Duplicates.Count);
        Assert.Equal(3, new[] { mail }.Concat(mail.Duplicates).Select(d => d.PageId).Distinct().Count());
    }

    private static string Dump(SiteExtractResult site)
    {
        var sb = new StringBuilder();
        sb.Append("region ").Append(site.Region.IsUnknown ? "none" : site.Region.ToString()).Append('\n');
        foreach (SitePage p in site.Pages)
        {
            sb.Append("page ").Append(p.Id).Append(' ').Append(p.OffsetUnit).Append(" lang-region=").Append(p.Region.IsUnknown ? "none" : p.Region.ToString())
              .Append(p.RegionFromSite ? " site-region" : string.Empty).Append(" fields=").Append(p.FieldCount).Append('\n');
        }

        sb.Append("organization ").Append(site.Organization?.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none").Append('\n');
        foreach (ContactRecord r in site.Records)
        {
            sb.Append("record ").Append(r.Id).Append(' ').Append(r.Kind).Append(" pages=").Append(string.Join(',', r.Pages))
              .Append(" conf=").Append(r.Confidence.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
            foreach (ExtractedField f in r.Fields)
            {
                Field(sb, f, "  ");
            }

            foreach (RecordLink l in r.Links)
            {
                sb.Append("  link ").Append(l.Kind).Append(" -> record ").Append(l.Target.Id).Append('\n');
            }

            foreach (FieldDisagreement d in r.Disagreements)
            {
                sb.Append("  disagreement ").Append(d.Kind).Append(": ").Append(string.Join(", ", d.Fields.Select(f => "#" + f.Id))).Append('\n');
            }
        }

        foreach (ExtractedField f in site.Ungrouped)
        {
            Field(sb, f, "ungrouped ");
        }

        foreach (SiteIssue i in site.Issues)
        {
            sb.Append("issue ").Append(i.PageId).Append(' ').Append(i.Issue.Kind).Append(' ').Append(i.Issue.Source).Append('\n');
        }

        return sb.ToString();
    }

    private static void Field(StringBuilder sb, ExtractedField f, string indent)
    {
        sb.Append(indent).Append('#').Append(f.Id).Append(' ').Append(f.Kind).Append(' ').Append(f.PageId).Append(' ').Append(f.Source)
          .Append(" raw=\"").Append(ExtractionFixtures.Escape(f.Raw)).Append("\" text=\"").Append(ExtractionFixtures.Escape(f.Text)).Append('"')
          .Append(f.HasSpan ? $" span={f.Span.Start}+{f.Span.Length}" : " span=none");
        if (f.Flags != ExtractedFieldFacts.None)
        {
            sb.Append(" flags=").Append(f.Flags.ToString().Replace(", ", "|", StringComparison.Ordinal));
        }

        if (f.IsNormalized)
        {
            sb.Append(" => ").Append(f.Normalized.Kind).Append(':').Append(ExtractionFixtures.Escape(f.Normalized.Canonical));
        }

        if (!f.Issue.IsNone)
        {
            sb.Append(" issue=").Append(f.Issue.Id);
        }

        sb.Append('\n');
        foreach (ExtractedField d in f.Duplicates)
        {
            sb.Append(indent).Append("  also ").Append(d.PageId).Append(' ').Append(d.Source).Append(" span=").Append(d.Span.Start).Append('+').Append(d.Span.Length)
              .Append(d.Flags.HasFlag(ExtractedFieldFacts.Deobfuscated) ? " deobfuscated" : string.Empty).Append('\n');
        }
    }
}
