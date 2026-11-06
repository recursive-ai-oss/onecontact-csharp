using System.Text;
using RecursiveAI.OneContact.Addresses.Expansion;
using RecursiveAI.OneContact.Addresses.Standardization;

namespace RecursiveAI.OneContact.Addresses.Tier2;

/// <summary>How the postcode of a country is written.</summary>
internal enum PostcodeForm
{
    FiveDigits,
    FourDigits,
    FourDigitsTwoLetters,

    /// <summary>Outward and inward code of the United Kingdom, Jersey, Guernsey and the Isle of Man (<c>EC1Y 8SY</c>).</summary>
    Uk,

    /// <summary>Eircode: a routing key and a unique identifier (<c>T37 F8HK</c>).</summary>
    Eircode,

    /// <summary>Four digits, a dash and three digits (<c>2725-079</c>).</summary>
    Portuguese,

    /// <summary>Three letters and four digits (<c>HMR 1428</c>).</summary>
    Maltese,

    /// <summary><c>AD</c> and three digits (<c>AD700</c>).</summary>
    Andorran,

    /// <summary>Two digits, a dash and three digits (<c>00-950</c>), left of the locality: Poland.</summary>
    Polish,

    /// <summary>Three digits and two digits with a space between (<c>110 00</c>): the Czech Republic and Slovakia.</summary>
    ThreeTwoDigits,

    /// <summary>Six digits (<c>060274</c>): Romania.</summary>
    SixDigits,

    /// <summary>Four digits after the state abbreviation on the last line (<c>NORTH SYDNEY NSW 2060</c>): Australia.</summary>
    Australian,

    /// <summary>Three digits (Iceland, <c>220</c>).</summary>
    ThreeDigits,

    /// <summary>Five digits with a space after the third (Sweden, <c>123 45</c>).</summary>
    Swedish,

    /// <summary><c>LV-</c> and four digits, after the locality and a comma (Latvia, <c>RIGA, LV-1050</c>).</summary>
    Latvian,
}

/// <summary>The rules of one country of the first Tier 2 group, read from <c>t2-countries.tsv</c>.</summary>
internal sealed record T2Country(
    string Code,
    string CountryLine,
    bool Afnor,
    string[] Languages,
    PostcodeForm Postcode,
    string[] Prefixes,
    string PrintedPrefix,
    bool NumberFirst,
    bool NumberLast,
    int MaxLine,
    int LocalitySpaces,
    string[] TypeSets)
{
    /// <summary>How the country writes a line (<c>uk</c>, <c>ie</c>, <c>es</c>, <c>pt</c>, <c>it</c>, <c>sm</c>, <c>va</c>, <c>mt</c>, <c>ad</c>, and for the last group <c>pl</c>, <c>cz</c>, <c>sk</c>, <c>si</c>, <c>hr</c>, <c>hu</c>, <c>ro</c>, <c>mx</c>, <c>au</c>); empty for the first group.</summary>
    internal string Profile { get; init; } = string.Empty;

    /// <summary>Gets whether the country writes its lines with the profiles of the last group (Poland, the Czech Republic, Slovakia, Slovenia, Croatia, Hungary, Romania, Mexico, Australia).</summary>
    internal bool IsCentral => Profile is "pl" or "cz" or "sk" or "si" or "hr" or "hu" or "ro" or "mx" or "au";

    /// <summary>Gets whether the postcode follows the locality (the United Kingdom, Ireland, Malta).</summary>
    internal bool PostcodeLast => Postcode is PostcodeForm.Uk or PostcodeForm.Eircode or PostcodeForm.Maltese or PostcodeForm.Latvian;

    /// <summary>Gets whether the country is one of the northern group (Denmark, Sweden, Finland, Norway, Iceland, Estonia, Latvia, Lithuania): street name first, then the number.</summary>
    internal bool IsNorth => Profile is "dk" or "se" or "fi" or "no" or "is" or "ee" or "lv" or "lt";
}

/// <summary>A street type or its abbreviation as an authority lists it.</summary>
internal sealed record T2Type(string Set, string Full, string Abbreviation);

/// <summary>A box, poste restante or parcel locker word.</summary>
internal sealed record T2Delivery(string Role, string Printed);

/// <summary>
/// The rule tables of the first Tier 2 group (France with its overseas departments, Monaco, Belgium, the Netherlands, Luxembourg, Germany, Austria,
/// Switzerland, Liechtenstein), loaded once from the embedded data files under <c>Addresses/Data/T2</c>. Keys are upper case without accents.
/// </summary>
internal static partial class T2Rules
{
    private static readonly Lazy<Tables> Loaded = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The countries by code.</summary>
    internal static IReadOnlyDictionary<string, T2Country> Countries => Loaded.Value.Countries;

    /// <summary>Country names as they end an address, to the country code.</summary>
    internal static IReadOnlyDictionary<string, string> CountryNames => Loaded.Value.CountryNames;

    /// <summary>The most words of a country name.</summary>
    internal static int MaxCountryWords => Loaded.Value.MaxCountryWords;

    /// <summary>Words that start a line which is never a street line.</summary>
    internal static IReadOnlySet<string> SpatialWords => Loaded.Value.SpatialWords;

    /// <summary>Postcode areas of the United Kingdom and the Crown dependencies, to the country.</summary>
    internal static IReadOnlyDictionary<string, string> UkAreas => Loaded.Value.UkAreas;

    /// <summary>Spanish province names (upper case, no accents) to the postcode prefix.</summary>
    internal static IReadOnlyDictionary<string, string> EsProvinces => Loaded.Value.EsProvinces;

    /// <summary>Italian province codes.</summary>
    internal static IReadOnlySet<string> ItProvinces => Loaded.Value.ItProvinces;

    /// <summary>Words that follow a house number and belong to it (staircase, floor, door).</summary>
    internal static IReadOnlySet<string> UnitWords => Loaded.Value.UnitWords;

    /// <summary>Swiss canton codes.</summary>
    internal static IReadOnlySet<string> Cantons => Loaded.Value.Cantons;

    /// <summary>State abbreviations (Mexico, Australia): the key is the country and the state's name or abbreviation as <see cref="Key"/> writes it; the value is the printed abbreviation.</summary>
    internal static IReadOnlyDictionary<(string Country, string Name), string> States => Loaded.Value.States;

    /// <summary>Gets a value indicating whether the country belongs to the group.</summary>
    internal static bool Handles(Region country) => !country.IsUnknown && Countries.ContainsKey(country.Code);

    /// <summary>Finds the type a stretch of words names, in the sets of a country.</summary>
    internal static T2Type? FindType(T2Country country, string words)
    {
        foreach (string set in country.TypeSets)
        {
            if (Loaded.Value.TypesBySetAndVariant.TryGetValue((set, words), out T2Type? type))
            {
                return type;
            }
        }

        return null;
    }

    /// <summary>Finds the type whose full form ends a name word (Dendermondestraat ends in STRAAT), with the length of the ending.</summary>
    internal static T2Type? FindSuffixType(T2Country country, string word)
    {
        T2Type? best = null;
        foreach (string set in country.TypeSets)
        {
            if (set == "afnor")
            {
                continue;
            }

            foreach (T2Type type in Loaded.Value.TypesBySet[set])
            {
                if (word.Length > type.Full.Length && word.EndsWith(type.Full, StringComparison.Ordinal) && (best is null || type.Full.Length > best.Full.Length))
                {
                    best = type;
                }
            }
        }

        return best;
    }

    /// <summary>Finds a delivery word (the longest one starting at the first of the words).</summary>
    internal static T2Delivery? FindDelivery(string country, string words) =>
        Loaded.Value.Delivery.TryGetValue((country, words), out T2Delivery? d) ? d : null;

    /// <summary>The most words of a delivery word.</summary>
    internal static int MaxDeliveryWords => 2;

    /// <summary>Upper case without accents, hyphens and apostrophes read as spaces, periods dropped: the form every table is matched in.</summary>
    internal static string Key(string text)
    {
        string folded = AddressExpander.Fold(text);
        StringBuilder sb = new(folded.Length);
        foreach (char c in folded)
        {
            if (c is '-' or '\'' or '\u2019' or '\u2013' or '\u2014')
            {
                sb.Append(' ');
            }
            else if (c != '.')
            {
                sb.Append(char.ToUpperInvariant(c));
            }
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    // The tables of each group of countries are files of their own, so groups are added without touching each other's rows: the first group's file has no suffix.
    private static readonly string[] Parts = [string.Empty, "-c-south"];

    private static IEnumerable<string[]> ReadParts(string name)
    {
        foreach (string part in Parts)
        {
            foreach (string[] row in RuleFile.Read(name + part + ".tsv"))
            {
                yield return row;
            }
        }
    }

    private static Tables Load()
    {
        Tables t = new();
        foreach (string[] r in ReadParts("t2-countries"))
        {
            t.Countries[r[0]] = new T2Country(
                r[0],
                r[1],
                r[2] == "afnor",
                r[3].Split(','),
                r[4] switch
                {
                    "5d" => PostcodeForm.FiveDigits,
                    "4d" => PostcodeForm.FourDigits,
                    "uk" => PostcodeForm.Uk,
                    "eircode" => PostcodeForm.Eircode,
                    "pt" => PostcodeForm.Portuguese,
                    "mt" => PostcodeForm.Maltese,
                    "ad" => PostcodeForm.Andorran,
                    "pl" => PostcodeForm.Polish,
                    "cz-sk" => PostcodeForm.ThreeTwoDigits,
                    "6d" => PostcodeForm.SixDigits,
                    "au" => PostcodeForm.Australian,
                    _ => PostcodeForm.FourDigitsTwoLetters,
                },
                r[5].Split(','),
                r[6],
                r[7] is "first" or "both",
                r[7] is "last" or "both",
                int.Parse(r[8], System.Globalization.CultureInfo.InvariantCulture),
                int.Parse(r[9], System.Globalization.CultureInfo.InvariantCulture),
                r[10].Split(','))
            {
                Profile = r.Length > 11 ? r[11] : string.Empty,
            };
        }

        foreach (string[] r in ReadParts("t2-country-names"))
        {
            t.CountryNames[r[0]] = r[1];
            t.MaxCountryWords = Math.Max(t.MaxCountryWords, r[0].Count(c => c == ' ') + 1);
        }

        foreach (string[] r in ReadParts("t2-street-types"))
        {
            T2Type type = new(r[0], r[1], r[2] == "-" ? string.Empty : r[2]);
            if (!t.TypesBySet.TryGetValue(r[0], out List<T2Type>? list))
            {
                list = [];
                t.TypesBySet[r[0]] = list;
            }

            list.Add(type);
            t.TypesBySetAndVariant[(r[0], r[1])] = type;
            if (type.Abbreviation.Length > 0)
            {
                t.TypesBySetAndVariant[(r[0], Key(type.Abbreviation))] = type;
            }

            if (r.Length > 3)
            {
                foreach (string v in r[3].Split(';'))
                {
                    t.TypesBySetAndVariant[(r[0], Key(v))] = type;
                }
            }
        }

        foreach (string[] r in ReadParts("t2-delivery-words"))
        {
            foreach (string country in r[0].Split(','))
            {
                t.Delivery[(country, r[2])] = new T2Delivery(r[1], r[3]);
            }
        }

        foreach (string[] r in ReadParts("t2-spatial-words"))
        {
            t.SpatialWords.Add(r[0]);
        }

        foreach (string[] r in RuleFile.Read("t2-uk-areas.tsv"))
        {
            t.UkAreas[r[0]] = r[1];
        }

        foreach (string[] r in RuleFile.Read("t2-es-provinces.tsv"))
        {
            foreach (string name in r[1].Split(';'))
            {
                t.EsProvinces[Key(name)] = r[0];
            }
        }

        foreach (string[] r in RuleFile.Read("t2-it-provinces.tsv"))
        {
            t.ItProvinces.Add(r[0]);
        }

        foreach (string[] r in ReadParts("t2-unit-words"))
        {
            t.UnitWords.Add(r[0]);
        }

        foreach (string[] r in RuleFile.Read("t2-states-c-south.tsv"))
        {
            t.States[(r[0], Key(r[1]))] = r[1];
            foreach (string name in r.Length > 2 ? r[2].Split(';') : [])
            {
                t.States[(r[0], Key(name))] = r[1];
            }
        }

        foreach (string[] r in RuleFile.Read("t2-cantons.tsv"))
        {
            t.Cantons.Add(r[0]);
        }

        LoadNorth(t);
        return t;
    }

    private sealed class Tables
    {
        internal Dictionary<string, T2Country> Countries { get; } = new(StringComparer.Ordinal);

        internal Dictionary<(string, string), string> States { get; } = [];

        internal Dictionary<string, string> CountryNames { get; } = new(StringComparer.Ordinal);

        internal int MaxCountryWords { get; set; } = 1;

        internal Dictionary<string, List<T2Type>> TypesBySet { get; } = new(StringComparer.Ordinal);

        internal Dictionary<(string, string), T2Type> TypesBySetAndVariant { get; } = [];

        internal Dictionary<(string, string), T2Delivery> Delivery { get; } = [];

        internal HashSet<string> SpatialWords { get; } = new(StringComparer.Ordinal);

        internal HashSet<string> Cantons { get; } = new(StringComparer.Ordinal);

        internal Dictionary<string, string> UkAreas { get; } = new(StringComparer.Ordinal);

        internal Dictionary<string, string> EsProvinces { get; } = new(StringComparer.Ordinal);

        internal HashSet<string> ItProvinces { get; } = new(StringComparer.Ordinal);

        internal HashSet<string> UnitWords { get; } = new(StringComparer.Ordinal);
    }
}
