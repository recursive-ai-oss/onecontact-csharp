namespace RecursiveAI.OneContact.Phones.Metadata;

/// <summary>A pattern that is compiled the first time it is used. Compiling twice on a race is harmless: the results are identical.</summary>
internal sealed class LazyPattern
{
    private PhoneRegex? _compiled;

    internal LazyPattern(string source)
    {
        Source = source;
    }

    internal string Source { get; }

    internal PhoneRegex Get() => _compiled ??= PhoneRegex.Compile(Source);
}

/// <summary>A number description with the lengths already resolved. An empty length list means "same as the general description".</summary>
internal sealed class Desc
{
    internal static readonly int[] NoNumbers = [-1];

    internal Desc(string? pattern, int[] lengths, int[] localOnly, string? example)
    {
        Pattern = pattern is null ? null : new LazyPattern(pattern);
        Lengths = lengths;
        LocalOnly = localOnly;
        Example = example;
    }

    internal LazyPattern? Pattern { get; }

    /// <summary>Gets the possible lengths of national numbers, sorted. <c>[-1]</c> means the type has no numbers; empty means "as the general description".</summary>
    internal int[] Lengths { get; }

    internal int[] LocalOnly { get; }

    internal string? Example { get; }

    /// <summary>Gets a value indicating whether the description says anything about lengths other than "none".</summary>
    internal bool HasPossibleNumberData => Lengths.Length != 1 || Lengths[0] != -1;
}

/// <summary>One number format with the territory-level defaults applied and <c>$NP</c>/<c>$FG</c> expanded.</summary>
internal sealed class NumberFormatRule
{
    internal NumberFormatRule(string pattern, string format, string[] leadingDigits, string? nationalPrefixRule, string? carrierCodeRule, bool nationalPrefixOptional)
    {
        Pattern = new LazyPattern(pattern);
        Format = format;
        LeadingDigits = new LazyPattern[leadingDigits.Length];
        for (int i = 0; i < leadingDigits.Length; i++)
        {
            LeadingDigits[i] = new LazyPattern(leadingDigits[i]);
        }

        NationalPrefixFormattingRule = nationalPrefixRule;
        CarrierCodeFormattingRule = carrierCodeRule;
        NationalPrefixOptionalWhenFormatting = nationalPrefixOptional;
    }

    internal LazyPattern Pattern { get; }

    internal string Format { get; }

    internal LazyPattern[] LeadingDigits { get; }

    internal string? NationalPrefixFormattingRule { get; }

    internal string? CarrierCodeFormattingRule { get; }

    internal bool NationalPrefixOptionalWhenFormatting { get; }

    internal NumberFormatRule WithoutNationalPrefixRule() =>
        new(Pattern.Source, Format, [.. LeadingDigits.Select(l => l.Source)], null, CarrierCodeFormattingRule, NationalPrefixOptionalWhenFormatting);
}

/// <summary>Everything the library knows about one region (or one non-geographic calling code), built from a <see cref="TerritoryData"/>.</summary>
internal sealed class RegionMetadata
{
    internal const string NonGeographicId = "001";

    private readonly Desc[] _descs = new Desc[DescKinds.Count];

    internal RegionMetadata(TerritoryData t)
    {
        Id = t.Id;
        CountryCode = t.CountryCode;
        MainCountryForCode = t.MainCountryForCode;
        LeadingDigits = t.LeadingDigits is null ? null : new LazyPattern(t.LeadingDigits);
        InternationalPrefix = t.InternationalPrefix;
        InternationalPrefixPattern = t.InternationalPrefix is null ? null : new LazyPattern(t.InternationalPrefix);
        PreferredInternationalPrefix = t.PreferredInternationalPrefix;
        NationalPrefix = t.NationalPrefix;
        PreferredExtnPrefix = t.PreferredExtnPrefix;
        string? forParsing = t.NationalPrefixForParsing ?? t.NationalPrefix;
        NationalPrefixForParsing = string.IsNullOrEmpty(forParsing) ? null : new LazyPattern(forParsing);
        NationalPrefixTransformRule = t.NationalPrefixTransformRule;
        MobileNumberPortableRegion = t.MobileNumberPortableRegion;

        BuildDescs(t);
        string np = t.NationalPrefix ?? "";
        List<NumberFormatRule> national = [];
        bool anyExplicitIntl = false;
        foreach (FormatData f in t.Formats)
        {
            string? npRule = Expand(f.NationalPrefixFormattingRule ?? t.NationalPrefixFormattingRule, np);
            string? ccRule = Expand(f.CarrierCodeFormattingRule ?? t.CarrierCodeFormattingRule, np);
            bool optional = f.HasNationalPrefixOptionalWhenFormatting ? f.NationalPrefixOptionalWhenFormatting : t.NationalPrefixOptionalWhenFormatting;
            national.Add(new NumberFormatRule(f.Pattern, f.Format, f.LeadingDigits, npRule, ccRule, optional));
            anyExplicitIntl |= f.IntlFormats.Length > 0;
        }

        NumberFormats = [.. national];

        // When no format spells out an international form, the national formats serve for both. When any does, every format that is
        // not marked "NA" is listed for international use, with its own text unless it gives one.
        List<NumberFormatRule> intl = [];
        if (anyExplicitIntl)
        {
            foreach (FormatData f in t.Formats)
            {
                string text = f.Format;
                if (f.IntlFormats.Length > 0)
                {
                    text = f.IntlFormats[0];
                    if (text == "NA")
                    {
                        continue;
                    }
                }

                intl.Add(new NumberFormatRule(f.Pattern, text, f.LeadingDigits, null, null, false));
            }
        }

        IntlNumberFormats = [.. intl];
    }

    internal string Id { get; }

    internal int CountryCode { get; }

    internal bool MainCountryForCode { get; }

    internal LazyPattern? LeadingDigits { get; }

    internal string? InternationalPrefix { get; }

    internal LazyPattern? InternationalPrefixPattern { get; }

    internal string? PreferredInternationalPrefix { get; }

    internal string? NationalPrefix { get; }

    internal string? PreferredExtnPrefix { get; }

    internal LazyPattern? NationalPrefixForParsing { get; }

    internal string? NationalPrefixTransformRule { get; }

    internal bool MobileNumberPortableRegion { get; }

    internal bool SameMobileAndFixedLinePattern { get; private set; }

    internal NumberFormatRule[] NumberFormats { get; }

    internal NumberFormatRule[] IntlNumberFormats { get; }

    internal Desc GetDesc(DescKind kind) => _descs[(int)kind];

    /// <summary>Checks every pattern in this region compiles; used for metadata that arrives from outside.</summary>
    internal void CompileAll()
    {
        LeadingDigits?.Get();
        InternationalPrefixPattern?.Get();
        NationalPrefixForParsing?.Get();
        foreach (Desc d in _descs)
        {
            d.Pattern?.Get();
        }

        foreach (NumberFormatRule f in NumberFormats.Concat(IntlNumberFormats))
        {
            f.Pattern.Get();
            foreach (LazyPattern l in f.LeadingDigits)
            {
                l.Get();
            }
        }
    }

    private static string? Expand(string? rule, string nationalPrefix)
    {
        if (string.IsNullOrEmpty(rule))
        {
            return null;
        }

        return rule.Replace("$NP", nationalPrefix, StringComparison.Ordinal).Replace("$FG", "$1", StringComparison.Ordinal);
    }

    private void BuildDescs(TerritoryData t)
    {
        SortedSet<int> union = [];
        SortedSet<int> unionLocal = [];
        for (int k = 0; k < DescKinds.Count; k++)
        {
            DescKind kind = (DescKind)k;
            if (kind is DescKind.General or DescKind.NoInternationalDialling)
            {
                continue;
            }

            DescData? d = t.Descs[k];
            if (d is null)
            {
                continue;
            }

            foreach (int v in d.PossibleLengths)
            {
                if (v != -1)
                {
                    union.Add(v);
                }
            }

            foreach (int v in d.LocalOnlyLengths)
            {
                unionLocal.Add(v);
            }
        }

        // A length that is dialled in full is not also "local only".
        unionLocal.ExceptWith(union);
        for (int k = 0; k < DescKinds.Count; k++)
        {
            DescKind kind = (DescKind)k;
            DescData? d = t.Descs[k];
            if (kind == DescKind.General)
            {
                _descs[k] = new Desc(d?.Pattern, union.Count == 0 ? Desc.NoNumbers : [.. union], [.. unionLocal], d?.Example);
            }
            else if (d is null)
            {
                bool relevant = kind is DescKind.FixedLine or DescKind.Mobile or DescKind.TollFree or DescKind.PremiumRate or DescKind.SharedCost
                    or DescKind.PersonalNumber or DescKind.Voip or DescKind.Pager or DescKind.Uan or DescKind.Voicemail;
                _descs[k] = new Desc(null, relevant ? Desc.NoNumbers : [], [], null);
            }
            else
            {
                _descs[k] = new Desc(d.Pattern, d.PossibleLengths, d.LocalOnlyLengths, d.Example);
            }
        }

        Desc fixedLine = _descs[(int)DescKind.FixedLine];
        Desc mobile = _descs[(int)DescKind.Mobile];
        SameMobileAndFixedLinePattern = fixedLine.Pattern is not null && mobile.Pattern is not null
            && string.Equals(fixedLine.Pattern.Source, mobile.Pattern.Source, StringComparison.Ordinal);
    }
}
