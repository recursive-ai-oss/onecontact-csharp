using RecursiveAI.OneContact.Phones.Metadata;

namespace RecursiveAI.OneContact.Phones;

public sealed partial class PhoneNumberParser
{
    private static DescKind KindOf(PhoneNumberType type) => type switch
    {
        PhoneNumberType.FixedLine or PhoneNumberType.FixedLineOrMobile => DescKind.FixedLine,
        PhoneNumberType.Mobile => DescKind.Mobile,
        PhoneNumberType.TollFree => DescKind.TollFree,
        PhoneNumberType.PremiumRate => DescKind.PremiumRate,
        PhoneNumberType.SharedCost => DescKind.SharedCost,
        PhoneNumberType.Voip => DescKind.Voip,
        PhoneNumberType.PersonalNumber => DescKind.PersonalNumber,
        PhoneNumberType.Pager => DescKind.Pager,
        PhoneNumberType.Uan => DescKind.Uan,
        PhoneNumberType.Voicemail => DescKind.Voicemail,
        _ => DescKind.General,
    };

    /// <summary>The national significant number: the leading zeros, if the plan keeps them, then the national number.</summary>
    internal static string GetNsn(in PhoneNumber number) => number.NationalSignificantNumber;

    /// <summary>Finds the region metadata a number belongs to: the only one for its calling code, or the first whose plan accepts the number.</summary>
    internal RegionMetadata? RegionMetadataFor(in PhoneNumber number) => RegionMetadataFor(number.CountryCode, GetNsn(number), out _);

    /// <summary>As above, for a calling code and national significant number already in hand; also gives the line type found on the way.</summary>
    internal RegionMetadata? RegionMetadataFor(int callingCode, string nsn, out PhoneNumberType type)
    {
        type = PhoneNumberType.Unknown;
        RegionMetadata[]? regions = _metadata.GetByCallingCode(callingCode);
        if (regions is null)
        {
            return null;
        }

        if (regions.Length == 1)
        {
            type = TypeOf(nsn, regions[0]);
            return regions[0];
        }

        foreach (RegionMetadata r in regions)
        {
            if (r.LeadingDigits is not null)
            {
                if (r.LeadingDigits.Get().LookingAt(nsn, out _))
                {
                    type = TypeOf(nsn, r);
                    return r;
                }
            }
            else
            {
                PhoneNumberType t = TypeOf(nsn, r);
                if (t != PhoneNumberType.Unknown)
                {
                    type = t;
                    return r;
                }
            }
        }

        return null;
    }

    private static bool IsNumberMatchingDesc(string nsn, Desc desc)
    {
        // A type with no numbers lists the length -1 and never matches; an empty list is "as the general description" and matches by pattern alone.
        if (desc.Lengths.Length > 0 && Array.IndexOf(desc.Lengths, nsn.Length) < 0)
        {
            return false;
        }

        return MatchesEntirely(desc, nsn);
    }

    internal static PhoneNumberType TypeOf(string nsn, RegionMetadata md)
    {
        if (!IsNumberMatchingDesc(nsn, md.GetDesc(DescKind.General)))
        {
            return PhoneNumberType.Unknown;
        }

        if (IsNumberMatchingDesc(nsn, md.GetDesc(DescKind.PremiumRate)))
        {
            return PhoneNumberType.PremiumRate;
        }

        if (IsNumberMatchingDesc(nsn, md.GetDesc(DescKind.TollFree)))
        {
            return PhoneNumberType.TollFree;
        }

        if (IsNumberMatchingDesc(nsn, md.GetDesc(DescKind.SharedCost)))
        {
            return PhoneNumberType.SharedCost;
        }

        if (IsNumberMatchingDesc(nsn, md.GetDesc(DescKind.Voip)))
        {
            return PhoneNumberType.Voip;
        }

        if (IsNumberMatchingDesc(nsn, md.GetDesc(DescKind.PersonalNumber)))
        {
            return PhoneNumberType.PersonalNumber;
        }

        if (IsNumberMatchingDesc(nsn, md.GetDesc(DescKind.Pager)))
        {
            return PhoneNumberType.Pager;
        }

        if (IsNumberMatchingDesc(nsn, md.GetDesc(DescKind.Uan)))
        {
            return PhoneNumberType.Uan;
        }

        if (IsNumberMatchingDesc(nsn, md.GetDesc(DescKind.Voicemail)))
        {
            return PhoneNumberType.Voicemail;
        }

        if (IsNumberMatchingDesc(nsn, md.GetDesc(DescKind.FixedLine)))
        {
            if (md.SameMobileAndFixedLinePattern || IsNumberMatchingDesc(nsn, md.GetDesc(DescKind.Mobile)))
            {
                return PhoneNumberType.FixedLineOrMobile;
            }

            return PhoneNumberType.FixedLine;
        }

        if (!md.SameMobileAndFixedLinePattern && IsNumberMatchingDesc(nsn, md.GetDesc(DescKind.Mobile)))
        {
            return PhoneNumberType.Mobile;
        }

        return PhoneNumberType.Unknown;
    }

    /// <summary>Tells how a national number's length fits a region's plan, for one type of line or (with <see cref="PhoneNumberType.Unknown"/>) any.</summary>
    internal static PhonePossibility TestNumberLength(string nsn, RegionMetadata md, PhoneNumberType type)
    {
        Desc general = md.GetDesc(DescKind.General);
        Desc forType = md.GetDesc(KindOf(type));
        int[] lengths = forType.Lengths.Length == 0 ? general.Lengths : forType.Lengths;
        int[] local = forType.LocalOnly;
        if (type == PhoneNumberType.FixedLineOrMobile)
        {
            Desc fixedLine = md.GetDesc(DescKind.FixedLine);
            Desc mobile = md.GetDesc(DescKind.Mobile);
            if (!fixedLine.HasPossibleNumberData)
            {
                // Some non-geographic entities have no fixed-line data: only mobile counts.
                return TestNumberLength(nsn, md, PhoneNumberType.Mobile);
            }

            if (!mobile.HasPossibleNumberData)
            {
                return TestNumberLength(nsn, md, PhoneNumberType.FixedLine);
            }

            List<int> merged = [.. fixedLine.Lengths.Length == 0 ? general.Lengths : fixedLine.Lengths];
            merged.AddRange(mobile.Lengths.Length == 0 ? general.Lengths : mobile.Lengths);
            merged.Sort();
            lengths = [.. merged];
            if (local.Length == 0)
            {
                local = mobile.LocalOnly;
            }
            else
            {
                List<int> mergedLocal = [.. local, .. mobile.LocalOnly];
                mergedLocal.Sort();
                local = [.. mergedLocal];
            }
        }

        if (lengths.Length == 0 || lengths[0] == -1)
        {
            return PhonePossibility.InvalidLength;
        }

        int actual = nsn.Length;
        if (Array.IndexOf(local, actual) >= 0)
        {
            return PhonePossibility.IsPossibleLocalOnly;
        }

        int minimum = lengths[0];
        if (minimum == actual)
        {
            return PhonePossibility.IsPossible;
        }

        if (minimum > actual)
        {
            return PhonePossibility.TooShort;
        }

        if (lengths[^1] < actual)
        {
            return PhonePossibility.TooLong;
        }

        return Array.IndexOf(lengths, actual, 1) >= 0 ? PhonePossibility.IsPossible : PhonePossibility.InvalidLength;
    }

    /// <summary>Gets the type of line the number is, by the plan of its region.</summary>
    /// <param name="number">The number.</param>
    /// <returns>The type; <see cref="PhoneNumberType.Unknown"/> when no region's plan has the number or the calling code is unknown.</returns>
    public PhoneNumberType GetNumberType(PhoneNumber number)
    {
        if (number.CachedType is { } known && ReferenceEquals(_metadata, PhoneMetadata.Default))
        {
            return known;
        }

        RegionMetadata? md = RegionMetadataFor(number);
        return md is null ? PhoneNumberType.Unknown : TypeOf(GetNsn(number), md);
    }

    /// <summary>Tells whether the number matches a number pattern of its region's plan.</summary>
    /// <param name="number">The number.</param>
    /// <returns><see langword="true"/> when it is valid.</returns>
    public bool IsValid(PhoneNumber number) => GetNumberType(number) != PhoneNumberType.Unknown;

    /// <summary>Tells whether the number is valid in a particular region (which matters when several regions share a calling code).</summary>
    /// <param name="number">The number.</param>
    /// <param name="region">The region.</param>
    /// <returns><see langword="true"/> when the region has data, uses the number's calling code and its plan has the number.</returns>
    public bool IsValidForRegion(PhoneNumber number, Region region)
    {
        RegionMetadata? md = _metadata.GetRegionMetadata(region);
        return md is not null && md.CountryCode == number.CountryCode && TypeOf(GetNsn(number), md) != PhoneNumberType.Unknown;
    }

    /// <summary>Finds the region a number belongs to.</summary>
    /// <param name="number">The number.</param>
    /// <returns>The region; <see cref="Region.Unknown"/> when the number matches no region or belongs to a non-geographic entity.</returns>
    public Region GetRegion(PhoneNumber number)
    {
        RegionMetadata? md = RegionMetadataFor(number);
        if (md is null || md.Id == RegionMetadata.NonGeographicId)
        {
            return Region.Unknown;
        }

        return Region.TryParse(md.Id, out Region region) ? region : Region.Unknown;
    }

    /// <summary>Tells whether a length is plausible, and if not, how it misses, against the main region of the calling code.</summary>
    /// <param name="number">The number.</param>
    /// <param name="type">The type of line to test against, or <see cref="PhoneNumberType.Unknown"/> for any.</param>
    /// <returns>The verdict.</returns>
    public PhonePossibility GetPossibility(PhoneNumber number, PhoneNumberType type = PhoneNumberType.Unknown)
    {
        RegionMetadata[]? regions = _metadata.GetByCallingCode(number.CountryCode);
        if (regions is null)
        {
            return PhonePossibility.InvalidCountryCode;
        }

        return TestNumberLength(GetNsn(number), regions[0], type);
    }

    /// <summary>Tells whether the number has a plausible length: a full number's, or a local-only number's.</summary>
    /// <param name="number">The number.</param>
    /// <returns><see langword="true"/> when <see cref="GetPossibility"/> is <see cref="PhonePossibility.IsPossible"/> or <see cref="PhonePossibility.IsPossibleLocalOnly"/>.</returns>
    public bool IsPossible(PhoneNumber number) =>
        GetPossibility(number) is PhonePossibility.IsPossible or PhonePossibility.IsPossibleLocalOnly;

    /// <summary>Tells whether the number belongs to a non-geographic entity (a calling code that belongs to no country, such as 800 or 870).</summary>
    /// <param name="number">The number.</param>
    /// <returns><see langword="true"/> when the calling code is a non-geographic one.</returns>
    public bool IsNonGeographic(PhoneNumber number) => RegionMetadataFor(number)?.Id == RegionMetadata.NonGeographicId;

    /// <summary>Tells whether a number can be dialled from another country (a few numbers can only be dialled inside their country).</summary>
    /// <param name="number">The number.</param>
    /// <returns><see langword="false"/> when the region's plan lists the number as national-only.</returns>
    public bool CanBeInternationallyDialled(PhoneNumber number)
    {
        RegionMetadata? md = RegionMetadataFor(number);
        if (md is null)
        {
            return true;
        }

        return !IsNumberMatchingDesc(GetNsn(number), md.GetDesc(DescKind.NoInternationalDialling));
    }

    /// <summary>Tells whether the number identifies a place: a fixed line, or a mobile in the few plans where mobile numbers carry an area code.</summary>
    /// <param name="number">The number.</param>
    /// <returns><see langword="true"/> when it is geographic.</returns>
    public bool IsGeographical(PhoneNumber number) => IsGeographical(GetNumberType(number), number.CountryCode);

    private static bool IsGeographical(PhoneNumberType type, int countryCode) =>
        type is PhoneNumberType.FixedLine or PhoneNumberType.FixedLineOrMobile
        || (type == PhoneNumberType.Mobile && countryCode is 52 or 54 or 55 or 62 or 86);

    /// <summary>Gets the length of the national destination code: the part of the national number after the calling code that selects an area or service.</summary>
    /// <param name="number">The number.</param>
    /// <returns>The length, or 0 when the number does not split that way.</returns>
    public int GetNationalDestinationCodeLength(PhoneNumber number)
    {
        string text = Format(number.WithoutExtension(), PhoneFormat.International);
        List<string> groups = SplitOnNonDigits(text);

        // The text starts with "+code ", so the first group is empty, the second is the calling code and the third the destination code.
        if (groups.Count <= 3)
        {
            return 0;
        }

        if (GetNumberType(number) == PhoneNumberType.Mobile && number.CountryCode == 54)
        {
            // Argentine mobile numbers carry a separately written "9" before the destination code; it belongs to the code.
            return groups[2].Length + groups[3].Length;
        }

        return groups[2].Length;
    }

    /// <summary>Gets the length of the area code of a geographic number.</summary>
    /// <param name="number">The number.</param>
    /// <returns>The length; 0 when the number has no area code.</returns>
    public int GetAreaCodeLength(PhoneNumber number)
    {
        RegionMetadata? md = RegionMetadataFor(number);
        if (md is null)
        {
            return 0;
        }

        // A plan with no national prefix is taken to have no area codes, except Mexico's.
        if (string.IsNullOrEmpty(md.NationalPrefix) && !number.ItalianLeadingZero && md.CountryCode != 52)
        {
            return 0;
        }

        PhoneNumberType type = TypeOf(GetNsn(number), md);
        if (type == PhoneNumberType.Mobile && number.CountryCode == 86)
        {
            return 0;
        }

        if (!IsGeographical(type, number.CountryCode))
        {
            return 0;
        }

        return GetNationalDestinationCodeLength(number);
    }

    // Splits on runs of non-digits the way a regular-expression split does: a leading separator gives an empty first group, a trailing one gives nothing.
    private static List<string> SplitOnNonDigits(string text)
    {
        List<string> result = [];
        if (text.Length > 0 && text[0] is not (>= '0' and <= '9'))
        {
            result.Add("");
        }

        int i = 0;
        while (i < text.Length)
        {
            if (text[i] is >= '0' and <= '9')
            {
                int j = i;
                while (j < text.Length && text[j] is >= '0' and <= '9')
                {
                    j++;
                }

                result.Add(text[i..j]);
                i = j;
            }
            else
            {
                i++;
            }
        }

        return result;
    }

    /// <summary>Tells whether a text is a vanity number: it has the shape of a number and contains three or more letters outside its extension.</summary>
    /// <param name="text">The text.</param>
    /// <returns><see langword="true"/> for a vanity number.</returns>
    public static bool IsVanityNumber(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!PhoneText.IsViable(text))
        {
            return false;
        }

        _ = PhoneText.StripExtension(text, out int cut);
        return PhoneText.IsVanityShape(text.AsSpan(0, cut));
    }

    /// <summary>Gets the national prefix of a region: the digit(s) dialled before a national number inside the country.</summary>
    /// <param name="region">The region.</param>
    /// <param name="stripNonDigits">Whether to remove the <c>~</c> that marks a pause.</param>
    /// <returns>The prefix; <see langword="null"/> when the region has none or no data.</returns>
    public string? GetNationalPrefix(Region region, bool stripNonDigits = true)
    {
        RegionMetadata? md = _metadata.GetRegionMetadata(region);
        if (md is null)
        {
            return null;
        }

        string prefix = md.NationalPrefix ?? "";
        if (prefix.Length == 0)
        {
            return null;
        }

        return stripNonDigits ? prefix.Replace("~", "", StringComparison.Ordinal) : prefix;
    }

    /// <summary>Tells whether a region belongs to the North American Numbering Plan (calling code 1).</summary>
    /// <param name="region">The region.</param>
    /// <returns><see langword="true"/> for the plan's regions.</returns>
    public bool IsNorthAmericanPlan(Region region)
    {
        RegionMetadata? md = _metadata.GetRegionMetadata(region);
        return md is not null && md.CountryCode == 1;
    }

    /// <summary>Tells whether mobile numbers of a region can move between carriers, so the carrier a number was first assigned to says little about its carrier today.</summary>
    /// <param name="region">The region.</param>
    /// <returns><see langword="true"/> when the region's metadata marks its mobile numbers as portable; <see langword="false"/> otherwise, also for a region without data.</returns>
    public bool IsMobileNumberPortableRegion(Region region)
    {
        RegionMetadata? md = _metadata.GetRegionMetadata(region);
        return md is not null && md.MobileNumberPortableRegion;
    }

    /// <summary>Lists the line types a region's plan has numbers for.</summary>
    /// <param name="region">The region.</param>
    /// <returns>The types in ascending order of <see cref="PhoneNumberType"/>; empty when the region has no data. <see cref="PhoneNumberType.FixedLineOrMobile"/> is never listed.</returns>
    public IReadOnlyList<PhoneNumberType> GetSupportedTypes(Region region)
    {
        RegionMetadata? md = _metadata.GetRegionMetadata(region);
        if (md is null)
        {
            return [];
        }

        List<PhoneNumberType> result = [];
        foreach (PhoneNumberType type in Enum.GetValues<PhoneNumberType>())
        {
            if (type is PhoneNumberType.Unknown)
            {
                continue;
            }

            if (type == PhoneNumberType.FixedLineOrMobile)
            {
                // Never listed: a plan that does not tell them apart lists the fixed-line and mobile types.
                continue;
            }

            Desc d = md.GetDesc(KindOf(type));
            if (d.HasPossibleNumberData || d.Example is not null)
            {
                result.Add(type);
            }
        }

        return result;
    }

    /// <summary>Gets an example number for a region and line type.</summary>
    /// <param name="region">The region.</param>
    /// <param name="type">The line type; <see cref="PhoneNumberType.FixedLineOrMobile"/> gives the fixed-line example and <see cref="PhoneNumberType.Unknown"/> the general description's.</param>
    /// <returns>A valid-looking example, or <see langword="null"/> when the region or the type has none.</returns>
    public PhoneNumber? GetExample(Region region, PhoneNumberType type = PhoneNumberType.FixedLine)
    {
        RegionMetadata? md = _metadata.GetRegionMetadata(region);
        string? example = md?.GetDesc(KindOf(type)).Example;
        if (example is null)
        {
            return null;
        }

        return TryParse(example, region, false, out PhoneNumber value, out _) == System.Buffers.OperationStatus.Done ? value : null;
    }

    /// <summary>Gets an example number for a non-geographic calling code such as 800 or 870.</summary>
    /// <param name="callingCode">The calling code, without <c>+</c>.</param>
    /// <returns>An example, or <see langword="null"/> when the code is not a non-geographic entity with an example.</returns>
    public PhoneNumber? GetNonGeographicExample(int callingCode)
    {
        RegionMetadata? md = _metadata.GetNonGeographic(callingCode);
        if (md is null)
        {
            return null;
        }

        DescKind[] order =
        [
            DescKind.Mobile, DescKind.TollFree, DescKind.SharedCost, DescKind.Voip, DescKind.Voicemail, DescKind.Uan, DescKind.PremiumRate,
        ];
        string cc = callingCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (DescKind kind in order)
        {
            string? example = md.GetDesc(kind).Example;
            if (example is not null && TryParse("+" + cc + example, Region.Unknown, false, out PhoneNumber value, out _) == System.Buffers.OperationStatus.Done)
            {
                return value;
            }
        }

        return null;
    }
}
