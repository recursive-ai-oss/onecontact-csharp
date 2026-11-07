using RecursiveAI.OneContact.Phones.Geo.Data;

namespace RecursiveAI.OneContact.Phones.Geo;

/// <summary>
/// Describes where a phone number is: the area of a fixed line, or the country when the plan gives nothing finer (a mobile number
/// whose plan has no area codes, a toll-free number). The descriptions are the numbering-plan listings of the compiled data, in the
/// language asked for.
/// </summary>
/// <remarks>
/// <para>Languages are BCP 47 tags such as <c>de</c>, <c>pt-BR</c> or <c>zh-Hant-TW</c>. A language the data has no table for
/// falls back to English, except Chinese, Japanese and Korean, which fall back to the country name alone. Country names come
/// from the Unicode CLDR territory names in that language. A tag that does not start with a language counts as English.
/// <c>CultureInfo</c> never affects results.</para>
/// <para>A description names a place, not a person's location: a number that moved between regions still describes where it was assigned.</para>
/// <para>The data loads lazily, one table per language and calling code, on first use.</para>
/// </remarks>
public sealed class PhoneGeocoder
{
    private static readonly Lazy<PhoneGeocoder> DefaultInstance = new(() => new PhoneGeocoder(PhoneNumberParser.Default));

    private readonly PhoneNumberParser _parser;

    /// <summary>Creates a geocoder that classifies numbers with a parser, which carries the numbering-plan metadata.</summary>
    /// <param name="parser">The parser. Pass one built on newer metadata to classify numbers by it; the descriptions stay those of the package.</param>
    /// <exception cref="ArgumentNullException"><paramref name="parser"/> is <see langword="null"/>.</exception>
    public PhoneGeocoder(PhoneNumberParser parser)
    {
        ArgumentNullException.ThrowIfNull(parser);
        _parser = parser;
    }

    /// <summary>Gets a geocoder over the default metadata.</summary>
    public static PhoneGeocoder Default => DefaultInstance.Value;

    /// <summary>Describes a number, after checking that it is valid.</summary>
    /// <param name="number">The number.</param>
    /// <param name="languageTag">The language, as a BCP 47 tag.</param>
    /// <returns>The description: the area for a place-bound number, the country name for any other valid number when the calling code says which country it is, an empty string otherwise (also when the number is not valid).</returns>
    public string GetDescription(PhoneNumber number, string languageTag)
    {
        // A geographic number has a known type, so asking that first classifies the number once; only the others need the type itself.
        if (_parser.IsGeographical(number))
        {
            return GetDescriptionForValidNumber(number, languageTag);
        }

        return _parser.GetNumberType(number) == PhoneNumberType.Unknown ? string.Empty : CountryOfNumber(number, LanguageTag.Parse(languageTag));
    }

    /// <summary>Describes a number for a user in a region: the same description when the number belongs to the user's own region, and only the country name otherwise.</summary>
    /// <param name="number">The number.</param>
    /// <param name="languageTag">The language, as a BCP 47 tag.</param>
    /// <param name="userRegion">The region the user is in; <see cref="Region.Unknown"/> never equals a number's region.</param>
    /// <returns>The description, as for <see cref="GetDescription(PhoneNumber, string)"/>; an empty string when the number is not valid.</returns>
    public string GetDescription(PhoneNumber number, string languageTag, Region userRegion)
    {
        // A geographic number has a known type, so asking that first classifies the number once; only the others need the type itself.
        if (_parser.IsGeographical(number))
        {
            return GetDescriptionForValidNumber(number, languageTag, userRegion);
        }

        return _parser.GetNumberType(number) == PhoneNumberType.Unknown ? string.Empty : CountryOfNumber(number, LanguageTag.Parse(languageTag));
    }

    /// <summary>Describes a number without checking its validity or type: the area its leading digits name, else the country when the calling code says which one.</summary>
    /// <param name="number">The number.</param>
    /// <param name="languageTag">The language, as a BCP 47 tag.</param>
    /// <returns>The description; an empty string when nothing is known about where the number is.</returns>
    public string GetDescriptionForValidNumber(PhoneNumber number, string languageTag)
    {
        LanguageTag tag = LanguageTag.Parse(languageTag);
        return GeoData.Lookup(GeoData.Geocoding, "g", tag, number) ?? CountryOfNumber(number, tag);
    }

    /// <summary>Describes a number for a user in a region, without checking its validity or type: the same description as without a region when the number belongs to the user's region, otherwise the name of the number's region.</summary>
    /// <param name="number">The number.</param>
    /// <param name="languageTag">The language, as a BCP 47 tag.</param>
    /// <param name="userRegion">The region the user is in.</param>
    /// <returns>The description; an empty string when nothing is known about where the number is.</returns>
    public string GetDescriptionForValidNumber(PhoneNumber number, string languageTag, Region userRegion)
    {
        Region region = _parser.GetRegion(number);
        if (region.IsUnknown)
        {
            return string.Empty;
        }

        return region == userRegion
            ? GetDescriptionForValidNumber(number, languageTag)
            : GeoData.Territories.Get(LanguageTag.Parse(languageTag), region.Code);
    }

    // A calling code shared by several regions names a country only when the number is valid in exactly one of them.
    private string CountryOfNumber(PhoneNumber number, LanguageTag tag)
    {
        IReadOnlyList<Region> regions = _parser.Metadata.GetRegions(number.CountryCode);
        Region chosen = Region.Unknown;
        if (regions.Count == 1)
        {
            chosen = regions[0];
        }
        else
        {
            foreach (Region candidate in regions)
            {
                if (_parser.IsValidForRegion(number, candidate))
                {
                    if (!chosen.IsUnknown)
                    {
                        return string.Empty;
                    }

                    chosen = candidate;
                }
            }
        }

        return chosen.IsUnknown ? string.Empty : GeoData.Territories.Get(tag, chosen.Code);
    }
}
