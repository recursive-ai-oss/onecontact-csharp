using RecursiveAI.OneContact.Extraction.Records;
using RecursiveAI.OneContact.Names;

namespace RecursiveAI.OneContact.Extraction.Markup;

/// <summary>
/// Turns a draft into an <see cref="ExtractedField"/> by handing its text to the core value types. Nothing is guessed: a national
/// phone number or a free-text address is read in a region only when the record, the page's <c>lang</c> or the caller names one, and the
/// field then says so with <see cref="ExtractedFieldFacts.RegionInferred"/>. A value the core refuses stays as text with its reason.
/// </summary>
internal sealed class FieldNormalizer
{
    private readonly PageContext _page;
    private readonly Region _fallbackRegion;

    /// <summary>Phone numbers found in text by their shape that were dropped only because no region was known to read them in.</summary>
    public int RegionDrops { get; private set; }

    public FieldNormalizer(PageContext page, Region callerDefault)
    {
        _page = page;
        // The page's own language region is closer to the content than the caller's default.
        _fallbackRegion = !page.PageRegion.IsUnknown ? page.PageRegion : callerDefault;
    }

    /// <summary>
    /// Normalizes the fields of one record (or of the ungrouped fields, with <paramref name="kind"/> null), addresses first so that
    /// their country can serve the phone numbers.
    /// </summary>
    public List<ExtractedField> NormalizeAll(List<FieldDraft> drafts, ContactRecordKind? kind)
    {
        var results = new ExtractedField?[drafts.Count];
        Region recordRegion = Region.Unknown;
        for (int i = 0; i < drafts.Count; i++)
        {
            if (drafts[i].Kind != FieldKind.Address)
            {
                continue;
            }

            ExtractedField f = Normalize(drafts[i], kind, Region.Unknown);
            results[i] = f;
            if (recordRegion.IsUnknown && f.Normalized.TryGetAddress(out PostalAddress a) && !a.Country.IsUnknown)
            {
                recordRegion = a.Country;
            }
        }

        for (int i = 0; i < drafts.Count; i++)
        {
            results[i] ??= Normalize(drafts[i], kind, recordRegion);
        }

        // A phone number found in text by its shape is a number only when the numbering plan says so.
        var kept = new List<ExtractedField>(drafts.Count);
        for (int i = 0; i < drafts.Count; i++)
        {
            ExtractedField f = results[i]!;
            if (drafts[i].RequireValid && !(f.IsNormalized && f.Facts.Contains("Valid")))
            {
                if (f.Issue == ReasonCode.PhoneRegionRequired)
                {
                    RegionDrops++;
                }

                continue;
            }

            kept.Add(f);
        }

        return kept;
    }

    private ExtractedField Normalize(FieldDraft d, ContactRecordKind? recordKind, Region recordRegion)
    {
        SourceSpan span = _page.ToSpan(d.Raw);
        string raw = d.Raw.IsPresent ? _page.Slice(d.Raw) : d.Text;
        ExtractedFieldFacts flags = ExtractedFieldFacts.None;
        if (d.Hidden)
        {
            flags |= ExtractedFieldFacts.Hidden;
        }

        if (d.Composed)
        {
            flags |= ExtractedFieldFacts.Composed;
        }

        if (d.Deobfuscated)
        {
            flags |= ExtractedFieldFacts.Deobfuscated;
        }

        string text = d.Text;
        ContactValue value = default;
        ReasonCode issue = default;
        string[] facts = [];
        bool ok;
        try
        {
            switch (d.Kind)
            {
                case FieldKind.Name:
                    ok = recordKind == ContactRecordKind.Organization
                        ? NormalizeCompany(text, recordRegion, out value, out issue, out facts)
                        : NormalizePerson(text, recordRegion, out value, out issue, out facts);
                    break;
                case FieldKind.Organization:
                    ok = NormalizeCompany(text, recordRegion, out value, out issue, out facts);
                    break;
                case FieldKind.Phone:
                    text = ContactText.PhoneFrom(text);
                    ok = NormalizePhone(text, recordRegion, out value, out issue, out facts, ref flags);
                    break;
                case FieldKind.Email:
                    text = ContactText.EmailFrom(text);
                    ok = NormalizeEmail(text, out value, out issue, out facts);
                    break;
                case FieldKind.Website:
                    text = text.Trim();
                    ok = NormalizeWeb(text, out value, out issue, out facts);
                    break;
                default:
                    ok = NormalizeAddress(d, text, recordRegion, out value, out issue, out facts, ref flags);
                    break;
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // The core parsers are written not to throw. A page is hostile input, though, and one value that trips a defect there must
            // not cost the whole page: the field is kept as text, unnormalized.
            ok = false;
            value = default;
            issue = default;
            facts = [];
        }

        if (!ok)
        {
            flags |= ExtractedFieldFacts.Unnormalized;
        }

        // A number the numbering plan parses but does not accept (a labelled "123-456-7890") is only half as sure as one it accepts.
        bool notValidNumber = d.Kind == FieldKind.Phone && d.Source == FieldSource.Text && !facts.Contains("Valid");
        var field = new ExtractedField(d.Kind, d.Source, raw, text, span, span.Length > 0 || d.Raw.IsPresent, flags, d.Label)
        {
            Normalized = ok ? value : default,
            Issue = issue,
            Facts = facts,
            Confidence = (d.Confidence ?? ConfidenceOf(d.Source)) * (ok && !notValidNumber ? 1.0 : 0.5),
        };
        field.BlockId = d.BlockId;
        field.Decision = d.Model ? DecisionSource.Model : DecisionSource.Rule;
        return field;
    }

    // Markup that names the property outright is certain of it; a link or an address element only suggests it.
    private static double ConfidenceOf(FieldSource source) => source switch
    {
        FieldSource.Link => 0.9,
        FieldSource.AddressElement => 0.7,
        FieldSource.Text => 0.5,
        _ => 1.0,
    };

    private static bool NormalizePerson(string text, Region region, out ContactValue value, out ReasonCode issue, out string[] facts)
    {
        var hints = new NameHints(region);
        if (PersonName.TryParse(text, hints, out PersonName n, out ParseDetail detail) == System.Buffers.OperationStatus.Done)
        {
            value = ContactValue.From(n);
            issue = detail.PrimaryReason;
            facts = FactNames(n.Flags);
            return true;
        }

        value = default;
        issue = detail.PrimaryReason;
        facts = [];
        return false;
    }

    private static bool NormalizeCompany(string text, Region region, out ContactValue value, out ReasonCode issue, out string[] facts)
    {
        var hints = new NameHints(region);
        if (CompanyName.TryParse(text, hints, out CompanyName c, out ParseDetail detail) == System.Buffers.OperationStatus.Done)
        {
            value = ContactValue.From(c);
            issue = detail.PrimaryReason;
            facts = FactNames(c.Flags);
            return true;
        }

        value = default;
        issue = detail.PrimaryReason;
        facts = [];
        return false;
    }

    private bool NormalizePhone(string text, Region recordRegion, out ContactValue value, out ReasonCode issue, out string[] facts, ref ExtractedFieldFacts flags)
    {
        value = default;
        facts = [];
        if (PhoneNumber.TryParse(text, out PhoneNumber p, out ParseDetail detail) == System.Buffers.OperationStatus.Done)
        {
            value = ContactValue.From(p);
            issue = default;
            facts = FactNames(p.Flags);
            return true;
        }

        issue = detail.PrimaryReason;
        Region region = !recordRegion.IsUnknown ? recordRegion : _fallbackRegion;
        if (region.IsUnknown)
        {
            return false;
        }

        if (PhoneNumber.TryParse(text, region, out p, out detail) == System.Buffers.OperationStatus.Done)
        {
            value = ContactValue.From(p);
            issue = default;
            facts = FactNames(p.Flags);
            flags |= ExtractedFieldFacts.RegionInferred;
            return true;
        }

        issue = detail.PrimaryReason;
        return false;
    }

    private static bool NormalizeEmail(string text, out ContactValue value, out ReasonCode issue, out string[] facts)
    {
        if (EmailAddress.TryParse(text, out EmailAddress e, out ParseDetail detail) == System.Buffers.OperationStatus.Done)
        {
            value = ContactValue.From(e);
            issue = default;
            facts = FactNames(e.Flags);
            return true;
        }

        value = default;
        issue = detail.PrimaryReason;
        facts = [];
        return false;
    }

    private static bool NormalizeWeb(string text, out ContactValue value, out ReasonCode issue, out string[] facts)
    {
        if (WebAddress.TryParse(text, out WebAddress w, out ParseDetail detail) == System.Buffers.OperationStatus.Done)
        {
            value = ContactValue.From(w);
            issue = default;
            facts = FactNames(w.Flags);
            return true;
        }

        value = default;
        issue = detail.PrimaryReason;
        facts = [];
        return false;
    }

    private bool NormalizeAddress(FieldDraft d, string text, Region recordRegion, out ContactValue value, out ReasonCode issue, out string[] facts, ref ExtractedFieldFacts flags)
    {
        value = default;
        facts = [];
        issue = default;
        if (d.Parts is { } parts)
        {
            // The page gave the parts: nothing to parse. The country is the one the page names, else none (structure only).
            Region country = Region.Unknown;
            if (!string.IsNullOrWhiteSpace(parts.CountryText) && !CountryNames.TryResolve(parts.CountryText, out country))
            {
                issue = ReasonCode.AddressCountryRequired;
            }
            else if (string.IsNullOrWhiteSpace(parts.CountryText))
            {
                issue = ReasonCode.AddressCountryRequired;
            }

            PostalAddress a = PostalAddress.Create(
                country,
                adminArea: parts.AdminArea,
                locality: parts.Locality,
                postalCode: parts.PostalCode,
                streetLines: parts.StreetLines);
            value = ContactValue.From(a);
            facts = FactNames(a.Flags);
            return true;
        }

        // Free text needs a country to be read in.
        Region read = !recordRegion.IsUnknown ? recordRegion : _fallbackRegion;
        if (read.IsUnknown)
        {
            issue = ReasonCode.AddressCountryRequired;
            return false;
        }

        if (PostalAddress.TryParse(text, read, out PostalAddress parsed, out ParseDetail detail) == System.Buffers.OperationStatus.Done)
        {
            value = ContactValue.From(parsed);
            facts = FactNames(parsed.Flags);
            flags |= ExtractedFieldFacts.RegionInferred;
            return true;
        }

        issue = detail.PrimaryReason;
        return false;
    }

    internal static string[] FactNames<TEnum>(TEnum flags)
        where TEnum : struct, Enum
    {
        ulong bits = Convert.ToUInt64(flags, System.Globalization.CultureInfo.InvariantCulture);
        if (bits == 0)
        {
            return [];
        }

        var names = new List<string>();
        foreach ((ulong bit, string name) in SingleBits<TEnum>.Members)
        {
            if ((bits & bit) != 0)
            {
                names.Add(name);
            }
        }

        return [.. names];
    }

    // The single-bit members of a flags enum with their names, listed once per enum type.
    private static class SingleBits<TEnum>
        where TEnum : struct, Enum
    {
        public static readonly (ulong Bit, string Name)[] Members = Build();

        private static (ulong, string)[] Build()
        {
            var list = new List<(ulong, string)>();
            foreach (TEnum member in Enum.GetValues<TEnum>())
            {
                ulong m = Convert.ToUInt64(member, System.Globalization.CultureInfo.InvariantCulture);
                if (m != 0 && (m & (m - 1)) == 0)
                {
                    list.Add((m, member.ToString()));
                }
            }

            return [.. list];
        }
    }
}
