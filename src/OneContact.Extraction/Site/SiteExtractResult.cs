using RecursiveAI.OneContact.Extraction.Records;

namespace RecursiveAI.OneContact.Extraction;

/// <summary>One page of a site as the extractor read it.</summary>
/// <param name="Id">The caller's id for the page.</param>
/// <param name="OffsetUnit">What the spans of the fields read from this page count: bytes for a page given as bytes, UTF-16 code units for a string.</param>
/// <param name="Region">The region of the page's own <c>lang</c>; unknown when it names none.</param>
/// <param name="RegionFromSite">True when the page was read a second time in the site's region, because it had values that needed a region and named none (see <see cref="SiteExtractResult.Region"/>).</param>
/// <param name="FieldCount">How many fields the page gave, before the pages were merged.</param>
public sealed record SitePage(string Id, OffsetUnit OffsetUnit, Region Region, bool RegionFromSite, int FieldCount);

/// <summary>A problem a reader met on one page of the site.</summary>
/// <param name="PageId">The page.</param>
/// <param name="Issue">What went wrong, with its span in that page.</param>
public sealed record SiteIssue(string PageId, ExtractIssue Issue);

/// <summary>
/// What a set of pages from one site gave, merged: the records de-duplicated across pages, every field flat, and what each page
/// contributed. Nothing is fetched and nothing is stored.
/// </summary>
/// <remarks>
/// <para>
/// Every <see cref="ExtractedField"/> keeps the id of its page (<see cref="ExtractedField.PageId"/>), its source, and its span in that
/// page, in that page's <see cref="SitePage.OffsetUnit"/>. A value seen on several pages is one field with the other sightings in
/// <see cref="ExtractedField.Duplicates"/>, each with its own page. Values that do not fit together are kept and flagged
/// <see cref="ExtractedFieldFacts.Disputed"/>; see <see cref="ContactRecord.Disagreements"/>.
/// </para>
/// <para>How records are merged is described in <c>docs/extraction.md</c>, "Pages of one site".</para>
/// </remarks>
public sealed class SiteExtractResult
{
    internal SiteExtractResult(
        ExtractionSources sources,
        Region region,
        List<SitePage> pages,
        List<ContactRecord> records,
        List<ExtractedField> fields,
        List<SiteIssue> issues,
        ContactRecord? organization)
    {
        Sources = sources;
        Region = region;
        Pages = pages;
        Records = records;
        Fields = fields;
        Issues = issues;
        Organization = organization;
    }

    /// <summary>The readers that ran.</summary>
    public ExtractionSources Sources { get; }

    /// <summary>
    /// The one region the site's own content points to: the <c>lang</c> regions of its pages and the countries its markup gives for
    /// addresses, when they all agree. Unknown when there is no such content or the pages name more than one region. It is used only
    /// for pages that need a region and name none, and never when the caller gave <see cref="ContactExtractor.DefaultRegion"/>.
    /// </summary>
    public Region Region { get; }

    /// <summary>The pages, in the order they were supplied.</summary>
    public IReadOnlyList<SitePage> Pages { get; }

    /// <summary>The person and organization records, merged across pages, in the order their first page and markup begin.</summary>
    public IReadOnlyList<ContactRecord> Records { get; }

    /// <summary>Every field, flat, in page order and then document order. Fields seen more than once appear once.</summary>
    public IReadOnlyList<ExtractedField> Fields { get; }

    /// <summary>The fields that belong to no record, de-duplicated across pages.</summary>
    public IReadOnlyList<ExtractedField> Ungrouped => Fields.Where(f => f.RecordId < 0).ToList();

    /// <summary>Problems met on single pages: a malformed JSON-LD block, a budget used up.</summary>
    public IReadOnlyList<SiteIssue> Issues { get; }

    /// <summary>
    /// The organization record the site is about: the organization found on the most pages (the first one on a tie); null when no page
    /// gave an organization. Other organization records (branches, partners) are in <see cref="Records"/>.
    /// </summary>
    public ContactRecord? Organization { get; }

    /// <summary>The flat fields of one kind.</summary>
    /// <param name="kind">The kind to select.</param>
    /// <returns>The matching fields in order.</returns>
    public IReadOnlyList<ExtractedField> Of(FieldKind kind) => Fields.Where(f => f.Kind == kind).ToList();
}
