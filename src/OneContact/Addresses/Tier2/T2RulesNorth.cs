using System.Globalization;
using RecursiveAI.OneContact.Addresses.Standardization;

namespace RecursiveAI.OneContact.Addresses.Tier2;

/// <summary>
/// The rule tables of the northern Tier 2 group (Denmark, Sweden, Finland, Norway, Iceland, Estonia, Latvia, Lithuania). They are rows in files of their own
/// (<c>t2-*-north.tsv</c>) that are added to the shared tables when those are loaded, so the first groups' files stay as they were.
/// </summary>
internal static partial class T2Rules
{
    private static HashSet<string> _northUnitWords = new(StringComparer.Ordinal);

    /// <summary>Words that follow a house number in the northern group and belong to it (floor, door, apartment, building).</summary>
    internal static IReadOnlySet<string> NorthUnitWords => _northUnitWords;

    private static void LoadNorth(Tables t)
    {
        foreach (string[] r in RuleFile.Read("t2-countries-north.tsv"))
        {
            t.Countries[r[0]] = new T2Country(
                r[0],
                r[1],
                false,
                r[3].Split(','),
                r[4] switch
                {
                    "5d" => PostcodeForm.FiveDigits,
                    "4d" => PostcodeForm.FourDigits,
                    "3d" => PostcodeForm.ThreeDigits,
                    "se" => PostcodeForm.Swedish,
                    _ => PostcodeForm.Latvian,
                },
                r[5].Split(','),
                r[6],
                r[7] is "first" or "both",
                r[7] is "last" or "both",
                int.Parse(r[8], CultureInfo.InvariantCulture),
                int.Parse(r[9], CultureInfo.InvariantCulture),
                r[10].Split(','))
            {
                Profile = r[11],
            };
        }

        foreach (string[] r in RuleFile.Read("t2-country-names-north.tsv"))
        {
            t.CountryNames[r[0]] = r[1];
            t.MaxCountryWords = Math.Max(t.MaxCountryWords, r[0].Count(c => c == ' ') + 1);
        }

        foreach (string[] r in RuleFile.Read("t2-delivery-words-north.tsv"))
        {
            foreach (string country in r[0].Split(','))
            {
                t.Delivery[(country, r[2])] = new T2Delivery(r[1], r[3]);
            }
        }

        HashSet<string> units = new(StringComparer.Ordinal);
        foreach (string[] r in RuleFile.Read("t2-north-words.tsv"))
        {
            if (r[0] == "unit")
            {
                units.Add(r[1]);
            }
            else
            {
                t.SpatialWords.Add(r[1]);
            }
        }

        _northUnitWords = units;
    }
}
