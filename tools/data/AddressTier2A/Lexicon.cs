using System.Globalization;
using System.Text;

namespace OneContact.Data.AddressTier2A;

/// <summary>Street-type words, abbreviations and country names the templates spell from, per language.</summary>
internal static class Lexicon
{
    // Type words that stand in front of the name in French and Italian street names ("Rue de la Paix", "Via Nassa"): the type is its own
    // label, as in the US and Canadian French forms. Dutch and German types are written into the name ("Kerkstraat", "Hauptstraße").
    private static readonly HashSet<string> FrenchTypes = new(StringComparer.Ordinal)
    {
        "rue", "avenue", "boulevard", "chemin", "route", "place", "impasse", "allée", "allee", "quai", "passage", "square", "cours", "chaussée", "chaussee", "rond-point",
        "ruelle", "sentier", "esplanade", "villa", "cité", "cite", "domaine", "hameau", "lotissement", "parvis", "promenade", "faubourg", "traverse", "montée", "montee", "mail",
        "clos", "voie", "sente", "venelle", "rampe", "côte", "cote", "carrefour", "résidence", "residence", "zone", "parc", "port", "allées", "avenues", "boulevards", "chemins",
        "routes", "places", "rues", "bd", "bvd", "av", "ave", "imp", "all", "rte", "ch", "chem", "pl", "pass", "sq", "boul", "r", "bld",
    };

    private static readonly HashSet<string> ItalianTypes = new(StringComparer.Ordinal)
    {
        "via", "viale", "corso", "piazza", "piazzale", "strada", "vicolo", "largo", "salita", "contrada", "riva", "sentiero", "passaggio", "ponte",
    };

    private static readonly Dictionary<string, string[]> FrenchAbbreviations = new(StringComparer.Ordinal)
    {
        ["rue"] = ["r", "rue"],
        ["avenue"] = ["av", "ave"],
        ["boulevard"] = ["bd", "bvd", "boul"],
        ["chemin"] = ["ch", "chem"],
        ["route"] = ["rte"],
        ["place"] = ["pl"],
        ["impasse"] = ["imp"],
        ["allée"] = ["all"],
        ["passage"] = ["pass"],
        ["square"] = ["sq"],
    };

    /// <summary>Splits "Rue de la Gare" into the type word and the rest; null when the first word is no type or nothing follows it.</summary>
    internal static (string Type, string Name)? SplitTypeFirst(string street, string language)
    {
        HashSet<string>? types = language switch { "fr" or "lb" => FrenchTypes, "it" => ItalianTypes, _ => null };
        if (types is null)
        {
            return null;
        }

        int space = street.IndexOf(' ', StringComparison.Ordinal);
        if (space <= 0 || space == street.Length - 1)
        {
            return null;
        }

        string first = street[..space];
        string key = first.TrimEnd('.').ToLowerInvariant();
        // A single letter "R" is only a type before a longer name ("R du Parc"); "R 4" is not.
        return types.Contains(key) && (key.Length > 1 || char.IsLetter(street[space + 1])) ? (first, street[(space + 1)..].TrimStart()) : null;
    }

    /// <summary>The abbreviations a writer uses for a French type word, empty when it has none.</summary>
    internal static string[] Abbreviations(string type) =>
        FrenchAbbreviations.TryGetValue(type.TrimEnd('.').ToLowerInvariant(), out string[]? a) ? a : [];

    /// <summary>Language of a Swiss street: from its type word, else from its canton's main language.</summary>
    internal static string GuessSwissLanguage(string street, string canton)
    {
        string firstWord = street.Split(' ', 2)[0].ToLowerInvariant();
        if (SplitTypeFirst(street, "fr") is not null && firstWord is not ("r" or "ch" or "all"))
        {
            return "fr";
        }

        if (SplitTypeFirst(street, "it") is not null)
        {
            return "it";
        }

        return canton switch
        {
            "GE" or "VD" or "NE" or "JU" => "fr",
            "TI" => "it",
            _ => "de",
        };
    }

    /// <summary>NRW writes "Ückendorfer Str." and "Hauptstr."; the long forms are what a person writes in full.</summary>
    internal static string ExpandGermanStreet(string street)
    {
        if (street.EndsWith(" Str.", StringComparison.Ordinal))
        {
            return street[..^5] + " Straße";
        }

        if (street.EndsWith("str.", StringComparison.Ordinal))
        {
            return street[..^4] + "straße";
        }

        if (street.EndsWith("Str.", StringComparison.Ordinal))
        {
            return street[..^4] + "Straße";
        }

        return street;
    }

    // The two leading digits of postcodes inside North Rhine-Westphalia: 32-33 (Ostwestfalen), 40-48, 50-53, 57-59.
    private static readonly string[] NrwPrefixes =
        ["32", "33", "40", "41", "42", "44", "45", "46", "47", "48", "50", "51", "52", "53", "57", "58", "59"];

    /// <summary>
    /// A synthetic postcode for an NRW address: the register has none. The first two digits come from the municipality, the rest from the
    /// street, both through a fixed hash, so a re-render gives the same codes. They are labelled synthetic in the manifest.
    /// </summary>
    internal static string NrwPostalCode(string municipalityKey, string place)
    {
        ulong h = OneContact.Data.AddressUsCa.Prng.Hash(municipalityKey + "|" + place, 0x4E525750);
        string prefix = NrwPrefixes[(int)(h % (ulong)NrwPrefixes.Length)];
        ulong g = OneContact.Data.AddressUsCa.Prng.Hash(place + "|" + municipalityKey, 0x504C5A31);
        return prefix + (g % 1000).ToString("D3", CultureInfo.InvariantCulture);
    }

    /// <summary>Upper-case without diacritics, the form La Poste and PostNL use for the locality line.</summary>
    internal static string PostalAscii(string text)
    {
        string d = text.Normalize(NormalizationForm.FormD);
        StringBuilder sb = new(d.Length);
        foreach (char c in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        string upper = sb.ToString().Normalize(NormalizationForm.FormC).Replace("œ", "oe", StringComparison.Ordinal).Replace("Œ", "OE", StringComparison.Ordinal)
            .Replace("ß", "ss", StringComparison.Ordinal).Replace('-', ' ').ToUpperInvariant();
        // A name that ended in a hyphen (a truncated register name) must not leave a trailing blank inside a span.
        return string.Join(' ', upper.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Country names as written in a given language (the first is the usual form), then the ISO code.</summary>
    internal static string[] CountryNames(string country, string language) => (country, language) switch
    {
        ("FR", _) => ["France", "FRANCE", "France métropolitaine"],
        ("BE", "nl") => ["België", "Belgium", "BELGIË"],
        ("BE", "de") => ["Belgien", "Belgium"],
        ("BE", _) => ["Belgique", "BELGIQUE", "Belgium"],
        ("NL", _) => ["Nederland", "Netherlands", "The Netherlands", "NEDERLAND", "Holland"],
        ("LU", "de" or "lb") => ["Luxemburg", "Luxembourg"],
        ("LU", _) => ["Luxembourg", "LUXEMBOURG", "Grand-Duché de Luxembourg"],
        ("DE", _) => ["Deutschland", "Germany", "DEUTSCHLAND", "Bundesrepublik Deutschland"],
        ("AT", _) => ["Österreich", "Austria", "ÖSTERREICH"],
        ("CH", "fr") => ["Suisse", "SUISSE", "Switzerland"],
        ("CH", "it") => ["Svizzera", "SVIZZERA", "Switzerland"],
        ("CH", _) => ["Schweiz", "Switzerland", "SCHWEIZ", "Suisse"],
        ("LI", _) => ["Liechtenstein", "Fürstentum Liechtenstein", "LIECHTENSTEIN"],
        ("MC", _) => ["Monaco", "MONACO", "Principauté de Monaco"],
        _ => [country],
    };

    /// <summary>The code a postal service puts in front of the postcode ("F-75008", "CH-8001"); empty when the country has none.</summary>
    internal static string PostalPrefix(string country) => country switch
    {
        "FR" => "F",
        "BE" => "B",
        "NL" => "NL",
        "LU" => "L",
        "DE" => "D",
        "AT" => "A",
        "CH" => "CH",
        "LI" => "FL",
        "MC" => "MC",
        _ => string.Empty,
    };
}
