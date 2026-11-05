using RecursiveAI.OneContact.Phones.Metadata;

namespace RecursiveAI.OneContact.Phones;

/// <summary>What a call to a short number is expected to cost the caller.</summary>
public enum ShortNumberCost
{
    /// <summary>The call is free for the caller.</summary>
    TollFree = 0,

    /// <summary>The call costs what a call to an ordinary number costs.</summary>
    StandardRate = 1,

    /// <summary>The call costs more than an ordinary call.</summary>
    PremiumRate = 2,

    /// <summary>The data does not say.</summary>
    Unknown = 3,
}

/// <summary>
/// Answers questions about short numbers (emergency services, directory assistance, SMS shortcodes and the like): whether a
/// number is possible or valid as a short number in a region, what it costs, and whether it reaches emergency services. The data
/// is the short-number metadata, which is separate from the numbering plans in <see cref="PhoneMetadata"/>.
/// </summary>
/// <remarks>
/// Short numbers are dialled inside one region, so most questions are asked "for a region". A short number that is valid in one
/// region is usually not a number at all elsewhere. Instances are immutable and safe to share between threads.
/// </remarks>
public sealed class ShortNumberInfo
{
    // Callers in these regions must dial an emergency number exactly: extra digits after it do not still reach the service.
    private static readonly string[] EmergencyMustBeExact = ["BR", "CL", "NI"];

    private static readonly Lazy<ShortNumberInfo> DefaultInstance = new(
        () => new ShortNumberInfo(PhoneMetadata.Default, PhoneMetadata.ReadEmbedded("short")),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly PhoneMetadata _main;
    private readonly Dictionary<Region, RegionMetadata> _short = [];
    private readonly Region[] _regions;

    private ShortNumberInfo(PhoneMetadata main, List<TerritoryData> territories)
    {
        _main = main;
        List<Region> regions = [];
        foreach (TerritoryData t in territories)
        {
            // A short-number territory has no calling code of its own; the numbering plan supplies it.
            if (!Region.TryParse(t.Id, out Region region) || !string.Equals(region.Code, t.Id, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"'{t.Id}' is not a region code.");
            }

            t.CountryCode = Math.Max(1, main.GetCallingCode(region));
            if (!_short.TryAdd(region, new RegionMetadata(t)))
            {
                throw new InvalidDataException($"Region '{t.Id}' is defined twice.");
            }

            regions.Add(region);
        }

        regions.Sort((a, b) => string.CompareOrdinal(a.Code, b.Code));
        _regions = [.. regions];
    }

    /// <summary>Gets the short-number data compiled into this library, used with <see cref="PhoneMetadata.Default"/>.</summary>
    public static ShortNumberInfo Default => DefaultInstance.Value;

    /// <summary>Gets the regions that have short-number data, in code order.</summary>
    public IReadOnlyList<Region> SupportedRegions => _regions;

    /// <summary>Reads newer or custom short-number metadata.</summary>
    /// <param name="main">The numbering-plan data that maps regions to calling codes.</param>
    /// <param name="shortNumberXml">A <c>ShortNumberMetadata.xml</c> document. The same bounds as <see cref="PhoneMetadata.LoadXml"/> apply.</param>
    /// <returns>The short-number data.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">The document is malformed, outside the bounds or defines a region twice.</exception>
    public static ShortNumberInfo LoadXml(PhoneMetadata main, Stream shortNumberXml)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(shortNumberXml);
        ShortNumberInfo info = new(main, PhoneMetadataXml.Read(shortNumberXml));
        foreach (RegionMetadata r in info._short.Values)
        {
            r.CompileAll();
        }

        return info;
    }

    /// <summary>Tells whether the length of a number is one a short number of its calling code's regions can have.</summary>
    /// <param name="number">The number.</param>
    /// <returns><see langword="true"/> when some region that uses the number's calling code accepts its length.</returns>
    public bool IsPossible(PhoneNumber number)
    {
        foreach (Region region in _main.GetRegions(number.CountryCode))
        {
            if (IsPossibleForRegion(number, region))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Tells whether the length of a number is one a short number of the region can have.</summary>
    /// <param name="number">The number. Its calling code must be the region's.</param>
    /// <param name="region">The region the number is dialled in.</param>
    /// <returns><see langword="true"/> when the region has short-number data and accepts the length.</returns>
    public bool IsPossibleForRegion(PhoneNumber number, Region region)
    {
        if (!TryGet(number, region, out RegionMetadata? md))
        {
            return false;
        }

        return Array.IndexOf(md.GetDesc(DescKind.General).Lengths, number.NationalSignificantNumber.Length) >= 0;
    }

    /// <summary>Tells whether a number is a short number in any region that uses its calling code.</summary>
    /// <param name="number">The number.</param>
    /// <returns><see langword="true"/> when some region's short-number data accepts it.</returns>
    public bool IsValid(PhoneNumber number)
    {
        foreach (Region region in _main.GetRegions(number.CountryCode))
        {
            if (IsValidForRegion(number, region))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Tells whether a number is a short number in the region.</summary>
    /// <param name="number">The number. Its calling code must be the region's.</param>
    /// <param name="region">The region the number is dialled in.</param>
    /// <returns><see langword="true"/> when the region's short-number data accepts it.</returns>
    public bool IsValidForRegion(PhoneNumber number, Region region)
    {
        if (!TryGet(number, region, out RegionMetadata? md))
        {
            return false;
        }

        string nsn = number.NationalSignificantNumber;
        return Matches(md, DescKind.General, nsn) && Matches(md, DescKind.ShortCode, nsn);
    }

    /// <summary>Gets what a call to the number is expected to cost, over every region that uses its calling code.</summary>
    /// <param name="number">The number.</param>
    /// <returns>The most expensive answer any region gives: premium rate, then unknown, then standard rate, then toll free.</returns>
    public ShortNumberCost GetExpectedCost(PhoneNumber number)
    {
        IReadOnlyList<Region> regions = _main.GetRegions(number.CountryCode);
        if (regions.Count == 0)
        {
            return ShortNumberCost.Unknown;
        }

        ShortNumberCost cost = ShortNumberCost.TollFree;
        foreach (Region region in regions)
        {
            switch (GetExpectedCostForRegion(number, region))
            {
                case ShortNumberCost.PremiumRate:
                    return ShortNumberCost.PremiumRate;
                case ShortNumberCost.Unknown:
                    cost = ShortNumberCost.Unknown;
                    break;
                case ShortNumberCost.StandardRate:
                    if (cost != ShortNumberCost.Unknown)
                    {
                        cost = ShortNumberCost.StandardRate;
                    }

                    break;
            }
        }

        return cost;
    }

    /// <summary>Gets what a call to the number is expected to cost in a region.</summary>
    /// <param name="number">The number. Its calling code must be the region's.</param>
    /// <param name="region">The region the number is dialled in.</param>
    /// <returns>The cost class; <see cref="ShortNumberCost.Unknown"/> when the number is not possible there or no class lists it. Emergency numbers are toll free.</returns>
    public ShortNumberCost GetExpectedCostForRegion(PhoneNumber number, Region region)
    {
        if (!TryGet(number, region, out RegionMetadata? md) || !IsPossibleForRegion(number, region))
        {
            return ShortNumberCost.Unknown;
        }

        string nsn = number.NationalSignificantNumber;
        if (Matches(md, DescKind.PremiumRate, nsn))
        {
            return ShortNumberCost.PremiumRate;
        }

        if (Matches(md, DescKind.StandardRate, nsn))
        {
            return ShortNumberCost.StandardRate;
        }

        if (Matches(md, DescKind.TollFree, nsn))
        {
            return ShortNumberCost.TollFree;
        }

        return IsEmergencyNumber(nsn, region) ? ShortNumberCost.TollFree : ShortNumberCost.Unknown;
    }

    /// <summary>Tells whether dialling the text from a region reaches emergency services, allowing extra digits after the number.</summary>
    /// <param name="number">The dialled text. A leading <c>+</c> means it is not a local short number.</param>
    /// <param name="region">The region the call is placed from.</param>
    /// <returns><see langword="true"/> when the digits start with one of the region's emergency numbers. In Brazil, Chile and Nicaragua the whole text must be the emergency number, because extra digits there form another number.</returns>
    public bool ConnectsToEmergencyNumber(ReadOnlySpan<char> number, Region region) => MatchesEmergency(number, region, allowPrefix: true);

    /// <summary>Tells whether the text is exactly an emergency number of the region.</summary>
    /// <param name="number">The dialled text. A leading <c>+</c> means it is not a local short number.</param>
    /// <param name="region">The region the call is placed from.</param>
    /// <returns><see langword="true"/> when the digits are an emergency number of the region and nothing more.</returns>
    public bool IsEmergencyNumber(ReadOnlySpan<char> number, Region region) => MatchesEmergency(number, region, allowPrefix: false);

    /// <summary>Tells whether the number reaches different services, or none, depending on the carrier.</summary>
    /// <param name="number">The number.</param>
    /// <returns><see langword="true"/> when the region the number belongs to lists it as carrier-specific. Where several regions share the calling code, the first whose short codes include the number is the one asked.</returns>
    public bool IsCarrierSpecific(PhoneNumber number)
    {
        IReadOnlyList<Region> regions = _main.GetRegions(number.CountryCode);
        Region chosen = Region.Unknown;
        if (regions.Count == 1)
        {
            chosen = regions[0];
        }
        else
        {
            string nsn = number.NationalSignificantNumber;
            foreach (Region region in regions)
            {
                if (_short.TryGetValue(region, out RegionMetadata? md) && Matches(md, DescKind.ShortCode, nsn))
                {
                    chosen = region;
                    break;
                }
            }
        }

        return !chosen.IsUnknown && IsCarrierSpecificForRegion(number, chosen);
    }

    /// <summary>Tells whether the number reaches different services, or none, depending on the carrier, in the region.</summary>
    /// <param name="number">The number. Its calling code must be the region's.</param>
    /// <param name="region">The region the number is dialled in.</param>
    /// <returns><see langword="true"/> when the region lists it as carrier-specific.</returns>
    public bool IsCarrierSpecificForRegion(PhoneNumber number, Region region) =>
        TryGet(number, region, out RegionMetadata? md) && IsPossibleForRegion(number, region)
        && Matches(md, DescKind.CarrierSpecific, number.NationalSignificantNumber);

    /// <summary>Tells whether the number is mainly for sending or receiving text messages in the region.</summary>
    /// <param name="number">The number. Its calling code must be the region's.</param>
    /// <param name="region">The region the number is dialled in.</param>
    /// <returns><see langword="true"/> when the region lists it as an SMS service.</returns>
    public bool IsSmsServiceForRegion(PhoneNumber number, Region region) =>
        TryGet(number, region, out RegionMetadata? md) && Matches(md, DescKind.SmsServices, number.NationalSignificantNumber);

    /// <summary>Gets an example short number of a region.</summary>
    /// <param name="region">The region.</param>
    /// <returns>The digits, or an empty string when the region has no short-number data.</returns>
    public string GetExample(Region region) =>
        _short.TryGetValue(region, out RegionMetadata? md) ? md.GetDesc(DescKind.ShortCode).Example ?? "" : "";

    /// <summary>Gets an example short number of a region and cost class.</summary>
    /// <param name="region">The region.</param>
    /// <param name="cost">The cost class; <see cref="ShortNumberCost.Unknown"/> has no examples.</param>
    /// <returns>The digits, or an empty string when there is none.</returns>
    public string GetExample(Region region, ShortNumberCost cost)
    {
        if (!_short.TryGetValue(region, out RegionMetadata? md))
        {
            return "";
        }

        DescKind kind = cost switch
        {
            ShortNumberCost.TollFree => DescKind.TollFree,
            ShortNumberCost.StandardRate => DescKind.StandardRate,
            ShortNumberCost.PremiumRate => DescKind.PremiumRate,
            _ => DescKind.General,
        };
        return kind == DescKind.General ? "" : md.GetDesc(kind).Example ?? "";
    }

    private bool TryGet(PhoneNumber number, Region region, out RegionMetadata md)
    {
        if (_short.TryGetValue(region, out RegionMetadata? m) && _main.GetCallingCode(region) == number.CountryCode)
        {
            md = m;
            return true;
        }

        md = null!;
        return false;
    }

    private static bool Matches(RegionMetadata md, DescKind kind, string nsn)
    {
        Desc d = md.GetDesc(kind);
        // A description the file leaves out has no numbers; an empty length list means "as the general description".
        if (d.Pattern is null)
        {
            return false;
        }

        int[] lengths = d.Lengths.Length == 0 ? md.GetDesc(DescKind.General).Lengths : d.Lengths;
        return Array.IndexOf(lengths, nsn.Length) >= 0 && d.Pattern.Get().IsMatch(nsn);
    }

    private bool MatchesEmergency(ReadOnlySpan<char> text, Region region, bool allowPrefix)
    {
        ReadOnlySpan<char> possible = PhoneText.ExtractPossibleNumber(text, out _);
        if (possible.Length == 0 || PhoneText.PlusCharsPattern.LookingAt(possible, out _))
        {
            return false;
        }

        if (!_short.TryGetValue(region, out RegionMetadata? md))
        {
            return false;
        }

        Desc emergency = md.GetDesc(DescKind.Emergency);
        if (emergency.Pattern is null)
        {
            return false;
        }

        string digits = PhoneText.NormalizeDigitsOnly(possible);
        if (allowPrefix && Array.IndexOf(EmergencyMustBeExact, region.Code) < 0)
        {
            return emergency.Pattern.Get().LookingAt(digits, out _);
        }

        return Matches(md, DescKind.Emergency, digits);
    }
}
