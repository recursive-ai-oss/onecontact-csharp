using RecursiveAI.OneContact.Data;
using RecursiveAI.OneContact.Phones.Metadata;

namespace RecursiveAI.OneContact;

/// <summary>
/// The numbering-plan data the phone engine works from: for each region its calling code, dialling prefixes, number patterns and
/// formats. <see cref="Default"/> is the data compiled into this library; <see cref="LoadXml"/> reads a newer or custom
/// <c>PhoneNumberMetadata.xml</c> supplied by the caller, with the same rules and bounds as the build-time tool.
/// </summary>
/// <remarks>Instances are immutable once built and safe to share between threads.</remarks>
public sealed class PhoneMetadata
{
    private static readonly EmbeddedTables Tables = new(typeof(PhoneMetadata).Assembly, "OneContact.Data.libphonenumber.ocd");
    private static readonly Lazy<PhoneMetadata> DefaultInstance = new(
        () => new PhoneMetadata(PhoneMetadataCodec.Read(Tables.GetTable("main")), compileAll: false),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly Dictionary<Region, RegionMetadata> _byRegion = [];
    private readonly Dictionary<int, RegionMetadata> _nonGeographic = [];
    private readonly Dictionary<int, RegionMetadata[]> _byCallingCode = [];
    private readonly Region[] _regions;
    private readonly int[] _callingCodes;

    private PhoneMetadata(List<TerritoryData> territories, bool compileAll)
    {
        List<RegionMetadata> all = [];
        foreach (TerritoryData t in territories)
        {
            if (t.CountryCode is < 1 or > 999)
            {
                throw new InvalidDataException($"Territory '{t.Id}' has an invalid country calling code.");
            }

            RegionMetadata r = new(t);
            if (compileAll)
            {
                r.CompileAll();
            }

            all.Add(r);
        }

        // Regions that share a calling code are listed main region first, then in file order.
        Dictionary<int, List<RegionMetadata>> grouped = [];
        List<Region> regions = [];
        foreach (RegionMetadata r in all)
        {
            if (r.Id == RegionMetadata.NonGeographicId)
            {
                if (!_nonGeographic.TryAdd(r.CountryCode, r))
                {
                    throw new InvalidDataException($"Calling code {r.CountryCode} is defined twice as a non-geographic entity.");
                }
            }
            else
            {
                if (!Region.TryParse(r.Id, out Region region) || !string.Equals(region.Code, r.Id, StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"'{r.Id}' is not a region code.");
                }

                if (!_byRegion.TryAdd(region, r))
                {
                    throw new InvalidDataException($"Region '{r.Id}' is defined twice.");
                }

                regions.Add(region);
            }

            if (!grouped.TryGetValue(r.CountryCode, out List<RegionMetadata>? list))
            {
                list = [];
                grouped.Add(r.CountryCode, list);
            }

            if (r.MainCountryForCode)
            {
                list.Insert(0, r);
            }
            else
            {
                list.Add(r);
            }
        }

        foreach ((int code, List<RegionMetadata> list) in grouped)
        {
            _byCallingCode.Add(code, [.. list]);
        }

        regions.Sort((a, b) => string.CompareOrdinal(a.Code, b.Code));
        _regions = [.. regions];
        int[] codes = [.. _byCallingCode.Keys];
        Array.Sort(codes);
        _callingCodes = codes;
    }

    /// <summary>Gets the metadata compiled into this library (libphonenumber release 9.0.43).</summary>
    public static PhoneMetadata Default => DefaultInstance.Value;

    /// <summary>Gets the regions that have metadata, in ordinal order of their codes. Non-geographic entities are not regions.</summary>
    public IReadOnlyList<Region> Regions => _regions;

    /// <summary>Gets every country calling code that has metadata, including the non-geographic ones, in ascending order.</summary>
    public IReadOnlyList<int> CallingCodes => _callingCodes;

    /// <summary>Reads metadata from a <c>PhoneNumberMetadata.xml</c> file.</summary>
    /// <param name="xml">The file. Documents over 32 million characters, or with implausible counts or pattern lengths, are refused.</param>
    /// <returns>The metadata. Every pattern in it has been compiled, so a bad pattern fails here and not later.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="xml"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">The document is malformed, outside the bounds, defines a region twice or contains a pattern this library cannot run.</exception>
    public static PhoneMetadata LoadXml(Stream xml) => new(PhoneMetadataXml.Read(xml), compileAll: true);

    /// <summary>Gets the calling code of a region.</summary>
    /// <param name="region">The region.</param>
    /// <returns>The calling code, or 0 when the region has no metadata.</returns>
    public int GetCallingCode(Region region) => _byRegion.TryGetValue(region, out RegionMetadata? r) ? r.CountryCode : 0;

    /// <summary>Gets the regions that use a calling code, the main region first.</summary>
    /// <param name="callingCode">The calling code, without <c>+</c>.</param>
    /// <returns>The regions; empty when the code is unknown or non-geographic.</returns>
    public IReadOnlyList<Region> GetRegions(int callingCode)
    {
        if (!_byCallingCode.TryGetValue(callingCode, out RegionMetadata[]? list))
        {
            return [];
        }

        List<Region> result = [];
        foreach (RegionMetadata r in list)
        {
            if (r.Id != RegionMetadata.NonGeographicId && Region.TryParse(r.Id, out Region region))
            {
                result.Add(region);
            }
        }

        return result;
    }

    /// <summary>Gets the international dialling prefix of a region as the data states it: a digit string, or a pattern when the region has several.</summary>
    /// <param name="region">The region.</param>
    /// <returns>The prefix text, or <see langword="null"/> when the region has none or no data.</returns>
    public string? GetInternationalPrefix(Region region) => GetRegionMetadata(region)?.InternationalPrefix;

    /// <summary>Reads one of the other compiled tables (short numbers, alternate formats) from the library's data resource.</summary>
    internal static List<TerritoryData> ReadEmbedded(string table) => PhoneMetadataCodec.Read(Tables.GetTable(table));

    internal bool TryGetRegion(Region region, out RegionMetadata metadata)
    {
        if (_byRegion.TryGetValue(region, out RegionMetadata? r))
        {
            metadata = r;
            return true;
        }

        metadata = null!;
        return false;
    }

    internal RegionMetadata? GetRegionMetadata(Region region) => _byRegion.GetValueOrDefault(region);

    internal RegionMetadata? GetNonGeographic(int callingCode) => _nonGeographic.GetValueOrDefault(callingCode);

    /// <summary>Gets the regions sharing a calling code, main first; the single non-geographic entry for non-geographic codes; null for unknown codes.</summary>
    internal RegionMetadata[]? GetByCallingCode(int callingCode) => _byCallingCode.GetValueOrDefault(callingCode);

    internal bool HasCallingCode(int callingCode) => _byCallingCode.ContainsKey(callingCode);
}
