using RecursiveAI.OneContact.Extraction.Markup;
using RecursiveAI.OneContact.Extraction.Records;

namespace RecursiveAI.OneContact.Extraction;

/// <summary>
/// Reads the pages of one site one at a time and merges what they give into one <see cref="SiteExtractResult"/>. This is the
/// low-level form of <see cref="ContactExtractor.ExtractSite(IEnumerable{ContactPage})"/>: add pages as they arrive, then call
/// <see cref="Complete"/>.
/// </summary>
/// <remarks>
/// <para>
/// Each page is parsed, read and released before the next one is taken, so the parsed document of only one page is alive at a time.
/// What stays is the values read from each page (strings and spans), plus the input of a page that needs the site's region (see
/// <see cref="SiteExtractResult.Region"/>), which is kept by reference until <see cref="Complete"/>.
/// </para>
/// <para>An instance is not thread-safe and is used once: after <see cref="Complete"/> it refuses further pages.</para>
/// </remarks>
public sealed class SiteExtraction
{
    private readonly ExtractionSources _sources;
    private readonly Region _defaultRegion;
    private readonly ExtractionLimits _limits;
    private readonly List<Entry> _pages = [];
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);
    private bool _completed;

    internal SiteExtraction(ExtractionSources sources, Region defaultRegion, ExtractionLimits limits)
    {
        _limits = limits;
        _sources = sources;
        _defaultRegion = defaultRegion;
    }

    /// <summary>Reads one page.</summary>
    /// <param name="page">The page; its id must differ from every page added before.</param>
    /// <exception cref="ArgumentException">The page is a default value, or its id was already added.</exception>
    /// <exception cref="InvalidOperationException"><see cref="Complete"/> was already called.</exception>
    /// <exception cref="ContactExtractionException">The page is past the extractor's <see cref="ExtractionLimits"/>.</exception>
    public void Add(ContactPage page)
    {
        if (_completed)
        {
            throw new InvalidOperationException("The site is complete; start a new SiteExtraction.");
        }

        if (page.IsDefault)
        {
            throw new ArgumentException("The page is a default value: build it with ContactPage.FromHtml or FromText.", nameof(page));
        }

        string id = page.Id!;
        if (!_ids.Add(id))
        {
            throw new ArgumentException($"A page with the id '{id}' was already added; ids identify pages and must be unique.", nameof(page));
        }

        ExtractResult result = page.Read(_sources, _defaultRegion, _limits);
        // Only a page that needs a region and has none keeps its input: the site may be able to supply the region at the end.
        bool needy = _defaultRegion.IsUnknown && result.PageRegion.IsUnknown && NeedsRegion(result);
        _pages.Add(new Entry(id, _pages.Count, result, needy ? page : null));
    }

    /// <summary>Merges the pages added so far into the site result.</summary>
    /// <returns>The merged records and fields.</returns>
    /// <exception cref="InvalidOperationException">Called twice.</exception>
    public SiteExtractResult Complete()
    {
        if (_completed)
        {
            throw new InvalidOperationException("The site is already complete.");
        }

        _completed = true;
        Region siteRegion = SiteRegionOf(_pages);
        var siteRegionPages = new HashSet<string>(StringComparer.Ordinal);
        if (!siteRegion.IsUnknown)
        {
            foreach (Entry e in _pages)
            {
                if (e.Input is { } input)
                {
                    // The page named no region and the site names exactly one: read it again in that region. Its values say so with
                    // RegionInferred, as for a region taken from the page's own language.
                    e.Result = input.Read(_sources, siteRegion, _limits);
                    e.Input = null;
                    siteRegionPages.Add(e.Id);
                }
            }
        }

        foreach (Entry e in _pages)
        {
            e.Input = null;
        }

        return SiteMerger.Merge(_sources, siteRegion, _pages, siteRegionPages);
    }

    // A value that a region would have turned into something: a national phone number or a free-text address that was refused for want of one.
    private static bool NeedsRegion(ExtractResult r)
    {
        if (r.RegionDrops > 0)
        {
            return true;
        }

        foreach (ExtractedField f in r.Fields)
        {
            if ((f.Flags & ExtractedFieldFacts.Unnormalized) != 0
                && ((f.Kind == FieldKind.Phone && f.Issue == ReasonCode.PhoneRegionRequired)
                    || (f.Kind == FieldKind.Address && f.Issue == ReasonCode.AddressCountryRequired)))
            {
                return true;
            }
        }

        return false;
    }

    // Only the site's own content counts: the regions of the pages' languages and the countries that markup gave for addresses. More
    // than one region (a site in two countries) means none: the extractor does not choose.
    private static Region SiteRegionOf(List<Entry> pages)
    {
        Region found = Region.Unknown;
        foreach (Entry e in pages)
        {
            if (!Take(ref found, e.Result.PageRegion))
            {
                return Region.Unknown;
            }

            foreach (ExtractedField f in e.Result.Fields)
            {
                if (f.Kind == FieldKind.Address
                    && (f.Flags & ExtractedFieldFacts.RegionInferred) == 0
                    && f.Normalized.TryGetAddress(out PostalAddress a)
                    && !Take(ref found, a.Country))
                {
                    return Region.Unknown;
                }
            }
        }

        return found;
    }

    private static bool Take(ref Region found, Region candidate)
    {
        if (candidate.IsUnknown || candidate == found)
        {
            return true;
        }

        if (found.IsUnknown)
        {
            found = candidate;
            return true;
        }

        return false;
    }

    internal sealed class Entry(string id, int index, ExtractResult result, ContactPage? input)
    {
        public string Id { get; } = id;

        public int Index { get; } = index;

        public ExtractResult Result { get; set; } = result;

        public ContactPage? Input { get; set; } = input;
    }
}
