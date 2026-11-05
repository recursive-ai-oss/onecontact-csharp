using System.Text;
using RecursiveAI.OneContact.Addresses.Expansion;
using RecursiveAI.OneContact.Addresses.Parsing;
using RecursiveAI.OneContact.Addresses.Streets;

namespace RecursiveAI.OneContact.Addresses.Tier2;

/// <summary>
/// Key version 1 for the first Tier 2 group of countries. The segments are those of the United States and Canada (country, one segment per delivery line, locality,
/// admin area, postal code), the words are folded and the abbreviations of the country's languages are replaced by their full words: street types and endings from the
/// libpostal dictionaries (<c>Hauptstr</c> is <c>hauptstrasse</c>, <c>av</c> is <c>avenue</c>), the authority's box words (<c>BP</c>, <c>Boite postale</c>), and the Belgian
/// box literals <c>bus</c>, <c>bte</c> and <c>box</c>, which all mean the same.
/// </summary>
internal static class T2Key
{
    private static readonly Dictionary<string, string> BoxWords = new(StringComparer.Ordinal)
    {
        ["boite postale"] = "bp",
        ["pf"] = "postfach",
        ["pb"] = "postbus",
        ["bus"] = "box",
        ["bte"] = "box",
    };

    internal static string Compute(ParsedAddress a)
    {
        T2Country country = a.Tier2!.Country;
        List<string> codes = [.. country.Languages];
        List<string> segments = [a.Country.ToString().ToLowerInvariant()];
        foreach (StreetLine line in a.DeliveryLines)
        {
            segments.Add(Line(line, country, codes));
        }

        segments.Add(Words(a.Locality?.Standard ?? string.Empty, codes));
        segments.Add((a.AdminArea?.Standard ?? string.Empty).ToLowerInvariant());
        segments.Add((a.PostalCode?.Standard ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant());
        return string.Join('|', segments);
    }

    private static string Line(StreetLine line, T2Country country, List<string> codes)
    {
        List<string> w = [];
        foreach (StreetPart p in line.Parts)
        {
            string lower = AddressExpander.Fold(p.Standard);
            switch (p.Role)
            {
                case StreetPartRole.HouseNumber:
                    w.Add(lower.Replace(" ", string.Empty, StringComparison.Ordinal));
                    break;
                case StreetPartRole.BoxNumber:
                    w.Add(lower.Replace(" ", string.Empty, StringComparison.Ordinal));
                    break;
                case StreetPartRole.StreetType:
                    // A type is the same word whether it is written in full or abbreviated: the table's full form.
                    w.Add(T2Rules.FindType(country, T2Rules.Key(p.Standard)) is { } t ? AddressExpander.Fold(t.Full) : Words(p.Standard, codes));
                    break;
                case StreetPartRole.BoxDesignator:
                case StreetPartRole.UnitDesignator:
                    w.Add(p.Standard == "//" ? string.Empty : BoxWords.TryGetValue(lower, out string? mapped) ? mapped : Words(p.Standard, codes));
                    break;
                default:
                    w.Add(Words(p.Standard, codes));
                    break;
            }
        }

        return string.Join(' ', w.Where(x => x.Length > 0));
    }

    private static string Words(string text, List<string> codes)
    {
        StringBuilder sb = new();
        foreach (string word in AddressExpander.Normalize(AddressExpander.Fold(text)))
        {
            string w = word;
            int d = 0;
            while (d < w.Length && char.IsAsciiDigit(w[d]))
            {
                d++;
            }

            if (d > 0 && d < w.Length && w[d..] is "e" or "er" or "re" or "ere" or "eme" or "ieme" or "ste" or "de")
            {
                w = w[..d];
            }
            else if (w.Length > 1)
            {
                w = CanonicalWord(w, codes);
            }

            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append(w);
        }

        return sb.ToString();
    }

    private static string CanonicalWord(string w, List<string> codes)
    {
        foreach (string code in codes)
        {
            foreach (PhraseEntry entry in ExpansionDictionaries.Find(code, w))
            {
                if (entry.Kind is PhraseKind.StreetName or PhraseKind.Synonym or PhraseKind.StreetType or PhraseKind.Directional)
                {
                    return AddressExpander.Fold(entry.Phrases[0]);
                }
            }
        }

        return AddressExpander.ExpandEnding(w, codes) ?? w;
    }
}
