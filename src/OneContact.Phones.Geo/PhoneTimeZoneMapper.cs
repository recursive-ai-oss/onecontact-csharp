using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using RecursiveAI.OneContact.Phones.Geo.Data;

namespace RecursiveAI.OneContact.Phones.Geo;

/// <summary>
/// Gives the time zones a phone number is likely to be in, as IANA identifiers (CLDR's canonical ones), from the area the number's
/// leading digits name. It says nothing about where the person is now.
/// </summary>
/// <remarks>The data loads on first use. Results never depend on the culture.</remarks>
public sealed class PhoneTimeZoneMapper
{
    /// <summary>The identifier the mapper answers with when it knows nothing.</summary>
    public const string UnknownTimeZone = "Etc/Unknown";

    private static readonly Lazy<PhoneTimeZoneMapper> DefaultInstance = new(() => new PhoneTimeZoneMapper(PhoneNumberParser.Default));
    private static readonly ReadOnlyCollection<string> UnknownList = new([UnknownTimeZone]);

    private readonly PhoneNumberParser _parser;
    private readonly ConcurrentDictionary<int, ReadOnlyCollection<string>> _lists = new();

    /// <summary>Creates a mapper that classifies numbers with a parser, which carries the numbering-plan metadata.</summary>
    /// <param name="parser">The parser.</param>
    /// <exception cref="ArgumentNullException"><paramref name="parser"/> is <see langword="null"/>.</exception>
    public PhoneTimeZoneMapper(PhoneNumberParser parser)
    {
        ArgumentNullException.ThrowIfNull(parser);
        _parser = parser;
    }

    /// <summary>Gets a mapper over the default metadata.</summary>
    public static PhoneTimeZoneMapper Default => DefaultInstance.Value;

    /// <summary>Gets the time zones of a number, checking it first: a number that is not valid has the unknown zone, a geographic number the zones of its area, and any other number the zones of its whole country (or countries sharing the calling code).</summary>
    /// <param name="number">The number.</param>
    /// <returns>Zone identifiers in the data's order; <see cref="UnknownTimeZone"/> alone when nothing is known.</returns>
    public IReadOnlyList<string> GetTimeZones(PhoneNumber number)
    {
        PhoneNumberType type = _parser.GetNumberType(number);
        if (type == PhoneNumberType.Unknown)
        {
            return UnknownList;
        }

        return _parser.IsGeographical(number) ? GetTimeZonesForGeographicalNumber(number) : ForCallingCode(number.CountryCode);
    }

    /// <summary>Gets the time zones of the area a number's leading digits name, without checking the number's validity or type.</summary>
    /// <param name="number">The number.</param>
    /// <returns>Zone identifiers in the data's order; <see cref="UnknownTimeZone"/> alone when nothing is known.</returns>
    public IReadOnlyList<string> GetTimeZonesForGeographicalNumber(PhoneNumber number)
    {
        if (number.CountryCode == 0 || !GeoData.TimeZones.TryGetMap("map", out PrefixMap map))
        {
            return UnknownList;
        }

        string digits = number.CountryCode.ToString(System.Globalization.CultureInfo.InvariantCulture) + number.NationalSignificantNumber;
        return map.TryFindLongest(digits, out int description) ? Zones(map, description) : UnknownList;
    }

    private ReadOnlyCollection<string> ForCallingCode(int callingCode)
    {
        if (callingCode != 0 && GeoData.TimeZones.TryGetMap("map", out PrefixMap map)
            && map.TryFindExact(callingCode.ToString(System.Globalization.CultureInfo.InvariantCulture), out int description))
        {
            return Zones(map, description);
        }

        return UnknownList;
    }

    private ReadOnlyCollection<string> Zones(PrefixMap map, int description) =>
        _lists.GetOrAdd(description, d => new ReadOnlyCollection<string>(map.GetDescription(d).Split('&')));
}
