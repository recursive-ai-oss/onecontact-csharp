namespace RecursiveAI.OneContact.Phones.Geo.Data;

/// <summary>The embedded tables of the package, each read on first use.</summary>
internal static class GeoData
{
    internal static readonly GeoTables Geocoding = new("libphonenumber-geocoding");
    internal static readonly GeoTables Carriers = new("libphonenumber-carrier");
    internal static readonly GeoTables TimeZones = new("libphonenumber-timezones");
    internal static readonly TerritoryNames Territories = new(new GeoTables("cldr-territories"));

    /// <summary>
    /// Orders the languages whose tables can answer for a language tag: Chinese uses its traditional-script tables for Taiwan, Hong
    /// Kong and Macao (whatever script the tag names) and its simplified ones otherwise; every other language has its own.
    /// </summary>
    /// <param name="tag">The requested language.</param>
    /// <returns>Table language names, best first.</returns>
    internal static string[] TableLanguages(LanguageTag tag) => tag.Language switch
    {
        "zh" when tag.Region is "TW" or "HK" or "MO" => ["zh_Hant", "zh"],
        "" => ["en"],
        _ => [tag.Language],
    };

    /// <summary>
    /// Looks a number up in a prefix family. The language's own table (the first of its order that exists for the calling code) answers
    /// with the description of the longest key that starts the national number. When it has no such key, or the entry is empty (the
    /// compiled data empties an entry that says what English says), the answer is the English table's, found the same way; Chinese,
    /// Japanese and Korean never borrow from English.
    /// </summary>
    /// <param name="tables">The table file.</param>
    /// <param name="family">The table family.</param>
    /// <param name="tag">The requested language.</param>
    /// <param name="number">The number.</param>
    /// <returns>The description, or <see langword="null"/> when nothing is found.</returns>
    internal static string? Lookup(GeoTables tables, string family, LanguageTag tag, PhoneNumber number)
    {
        string national = StripMobileToken(number);
        if (number.CountryCode == 1 && national.Length != 10)
        {
            // The North American listings are keyed by area code and exchange, and the reference looks nothing up for a national number
            // of any other length.
            return null;
        }

        string code = number.CountryCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (string language in TableLanguages(tag))
        {
            if (tables.TryGetMap(family + "/" + language + "/" + code, out PrefixMap own))
            {
                if (own.TryFindLongest(national, out int found) && own.GetDescription(found) is { Length: > 0 } text)
                {
                    return text;
                }

                break;
            }
        }

        if (tag.Language is "zh" or "ja" or "ko" || tag.Language == "en")
        {
            return null;
        }

        return tables.TryGetMap(family + "/en/" + code, out PrefixMap english) && english.TryFindLongest(national, out int inEnglish)
            ? english.GetDescription(inEnglish)
            : null;
    }

    // Argentine mobile numbers carry a 9 after the calling code that the plan's listings leave out, so the lookup skips it.
    private static string StripMobileToken(PhoneNumber number)
    {
        string national = number.NationalSignificantNumber;
        return number.CountryCode == 54 && national.StartsWith('9') ? national[1..] : national;
    }
}
