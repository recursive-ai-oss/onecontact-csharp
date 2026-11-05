namespace RecursiveAI.OneContact.Phones.Metadata;

/// <summary>The number descriptions a territory element can hold, in the order they are stored. Names follow the metadata files' element names.</summary>
internal enum DescKind : byte
{
    General = 0,
    FixedLine,
    Mobile,
    TollFree,
    PremiumRate,
    SharedCost,
    PersonalNumber,
    Voip,
    Pager,
    Uan,
    Voicemail,
    NoInternationalDialling,
    ShortCode,
    StandardRate,
    CarrierSpecific,
    Emergency,
    ExpandedEmergency,
    SmsServices,
}

/// <summary>One number description as written in the metadata: a pattern, the possible lengths and an example. Absent parts stay absent here; the rules for inheriting from the general description are applied when the data is loaded.</summary>
internal sealed class DescData
{
    internal string? Pattern { get; set; }

    internal int[] PossibleLengths { get; set; } = [];

    internal int[] LocalOnlyLengths { get; set; } = [];

    internal string? Example { get; set; }

    internal bool HasPossibleLengths { get; set; }
}

/// <summary>One <c>numberFormat</c> element.</summary>
internal sealed class FormatData
{
    internal string Pattern { get; set; } = "";

    internal string Format { get; set; } = "";

    internal string[] LeadingDigits { get; set; } = [];

    internal string[] IntlFormats { get; set; } = [];

    internal string? NationalPrefixFormattingRule { get; set; }

    internal bool NationalPrefixOptionalWhenFormatting { get; set; }

    internal bool HasNationalPrefixOptionalWhenFormatting { get; set; }

    internal string? CarrierCodeFormattingRule { get; set; }
}

/// <summary>One <c>territory</c> element as written in the metadata, with white space already removed from pattern text.</summary>
internal sealed class TerritoryData
{
    internal string Id { get; set; } = "";

    internal int CountryCode { get; set; }

    internal bool MainCountryForCode { get; set; }

    internal string? LeadingDigits { get; set; }

    internal string? InternationalPrefix { get; set; }

    internal string? PreferredInternationalPrefix { get; set; }

    internal string? NationalPrefix { get; set; }

    internal string? NationalPrefixForParsing { get; set; }

    internal string? NationalPrefixTransformRule { get; set; }

    internal string? PreferredExtnPrefix { get; set; }

    internal string? NationalPrefixFormattingRule { get; set; }

    internal bool NationalPrefixOptionalWhenFormatting { get; set; }

    internal string? CarrierCodeFormattingRule { get; set; }

    internal bool MobileNumberPortableRegion { get; set; }

    internal DescData?[] Descs { get; } = new DescData?[DescKinds.Count];

    internal List<FormatData> Formats { get; } = [];
}

/// <summary>The number of <see cref="DescKind"/> values.</summary>
internal static class DescKinds
{
    internal const int Count = 18;
}
