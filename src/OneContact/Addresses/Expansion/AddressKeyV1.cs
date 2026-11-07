using System.Text;
using RecursiveAI.OneContact.Addresses.Parsing;
using RecursiveAI.OneContact.Addresses.Streets;

namespace RecursiveAI.OneContact.Addresses.Expansion;

/// <summary>
/// Key version 1 of a postal address (docs/address-keys.md). The key joins, with <c>|</c>: the country, one segment per delivery line, the locality, the
/// state or province code and the postal code. Words are folded (case, diacritics), abbreviations are replaced by the dictionaries' full words, ordinals lose
/// their ending, a Canadian unit before the civic number is the same as a unit after the street, and numbered unit designators (apartment, suite, unit, room,
/// <c>#</c>) all read as <c>unit</c>. Recipient lines never enter the key. A published version's output never changes.
/// </summary>
internal static class AddressKeyV1
{
    internal static string Compute(ParsedAddress a)
    {
        if (a.Tier2 is not null)
        {
            return Tier2.T2Key.Compute(a);
        }

        // No country: the key is made from the words of the street fields, the locality, the admin area and the postal code, under the unknown-country prefix.
        if (a.Facts.HasFlag(AddressFacts.CountryUndetermined))
        {
            string street = string.Join(' ', a.Fields.Where(f => f.Name.StartsWith("street.", StringComparison.Ordinal)).Select(f => f.Text));
            string postalCode = string.Concat(AddressExpander.Fold(a.PostalCode?.Text ?? string.Empty).Where(char.IsLetterOrDigit));
            return string.Join('|', "zz", Words(street), Words(a.Locality?.Text ?? string.Empty), AddressExpander.Fold(a.AdminArea?.Text ?? string.Empty), postalCode);
        }

        string lang = a.Country == Region.CA ? "en" : "en";
        List<string> segments = [a.Country.IsUnknown ? "zz" : a.Country.ToString().ToLowerInvariant()];
        foreach (StreetLine line in a.DeliveryLines)
        {
            segments.Add(Line(line, a.Country == Region.CA, lang));
        }

        segments.Add(Words(a.Locality?.Standard ?? string.Empty));
        segments.Add((a.AdminArea?.Standard ?? string.Empty).ToLowerInvariant());
        segments.Add(Postal(a.PostalCode?.Standard ?? string.Empty, a.Country == Region.CA));
        return string.Join('|', segments);
    }

    private static string Postal(string standard, bool canada)
    {
        // A ZIP+4 is the same delivery area as its ZIP for matching: the key keeps the five digits. A Canadian code is written without its space.
        if (canada)
        {
            return standard.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        }

        return standard.Length >= 5 ? standard[..5] : standard;
    }

    private static string Line(StreetLine line, bool canada, string lang)
    {
        List<string> w = [];
        string unitPrefix = string.Empty;
        bool frenchLine = false;
        foreach (StreetPart p in line.Parts)
        {
            if (p.Role == StreetPartRole.StreetType && canada && UsCaRulesFrench(p.Standard))
            {
                frenchLine = true;
            }
        }

        string l = frenchLine ? "fr" : lang;
        for (int i = 0; i < line.Parts.Count; i++)
        {
            StreetPart p = line.Parts[i];
            string lower = AddressExpander.Fold(p.Standard);
            switch (p.Role)
            {
                case StreetPartRole.UnitPrefix:
                    unitPrefix = "unit " + lower.TrimEnd('-');
                    break;
                case StreetPartRole.HouseNumber:
                case StreetPartRole.Fraction:
                case StreetPartRole.BoxNumber:
                case StreetPartRole.RouteNumber:
                case StreetPartRole.MilitaryNumber:
                case StreetPartRole.AdditionalNumber:
                    w.Add(lower);
                    break;
                case StreetPartRole.PreDirectional:
                case StreetPartRole.PostDirectional:
                    w.Add(AddressExpander.CanonicalPhrase(l, lower, PhraseKind.Directional));
                    break;
                case StreetPartRole.StreetName:
                    w.Add(Words(p.Standard));
                    break;
                case StreetPartRole.StreetType:
                    w.Add(AddressExpander.CanonicalPhrase(l, lower, PhraseKind.StreetType));
                    break;
                case StreetPartRole.UnitDesignator:
                    w.Add(Unit(lower, l));
                    break;
                case StreetPartRole.BoxDesignator:
                    w.Add(lower is "po box" or "cp" ? lower.Replace(" ", string.Empty, StringComparison.Ordinal) : lower);
                    break;
                default:
                    w.Add(lower);
                    break;
            }
        }

        if (unitPrefix.Length > 0)
        {
            w.Add(unitPrefix);
        }

        return string.Join(' ', w.Where(x => x.Length > 0));
    }

    private static bool UsCaRulesFrench(string standard) =>
        Standardization.UsCaRules.CaStreetTypesFrench.TryGetValue(standard, out Standardization.UsCaRules.CaStreetType t) && t.French && !Standardization.UsCaRules.CaStreetTypesEnglish.ContainsKey(standard);

    // Numbered unit designators all read as "unit"; floors, buildings and the rest keep their word.
    private static string Unit(string lower, string lang) => lower switch
    {
        "apt" or "ste" or "suite" or "unit" or "rm" or "dept" or "#" or "app" or "bureau" or "unité" or "unite" => "unit",
        _ => AddressExpander.CanonicalPhrase(lang, lower, PhraseKind.NumberedUnit),
    };

    /// <summary>Folds words of a name (diacritics, case, punctuation, ordinal endings) and replaces the dictionaries' abbreviations by their full words.</summary>
    internal static string Words(string text)
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

            if (d > 0 && d < w.Length && w[d..] is "st" or "nd" or "rd" or "th" or "e" or "er" or "re" or "ere" or "eme" or "ieme")
            {
                w = w[..d];
            }
            else if (w.Length > 1)
            {
                w = AddressExpander.CanonicalNameWord(w);
            }

            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append(w);
        }

        return sb.ToString();
    }
}
