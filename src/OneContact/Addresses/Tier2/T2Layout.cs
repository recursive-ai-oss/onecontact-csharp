using System.Text;
using RecursiveAI.OneContact.Addresses.Expansion;
using RecursiveAI.OneContact.Addresses.Standardization;

namespace RecursiveAI.OneContact.Addresses.Tier2;

/// <summary>
/// The lines of an address of the first Tier 2 group as they print, in both styles. The reader builds it while it standardizes; rendering only joins the lines,
/// so the postal and display text of a line always come from the same decisions.
/// </summary>
internal sealed class T2Layout
{
    internal T2Layout(T2Country country, List<(string Postal, string Display)> leading, List<(string Postal, string Display)> delivery, (string Postal, string Display) last)
    {
        Country = country;
        Leading = leading;
        Delivery = delivery;
        Last = last;
    }

    internal T2Country Country { get; }

    internal List<(string Postal, string Display)> Leading { get; }

    internal List<(string Postal, string Display)> Delivery { get; }

    internal (string Postal, string Display) Last { get; }

    /// <summary>The locality as a line of its own, printed above the street line (Hungary); <see langword="null"/> for the countries whose locality shares the last line.</summary>
    internal (string Postal, string Display)? LocalityLine { get; init; }

    /// <summary>The number of leading lines printed above <see cref="LocalityLine"/>.</summary>
    internal int LocalityAfter { get; init; }

    /// <summary>The English name of the country as the country line prints it (domestic mail leaves it out).</summary>
    internal string CountryLine => Country.CountryLine;

    internal string Render(AddressStyle style, AddressLayout layout, bool includeCountry)
    {
        bool postal = style == AddressStyle.Postal;
        List<string> lines = [];
        for (int i = 0; i < Leading.Count; i++)
        {
            if (LocalityLine is { } before && i == LocalityAfter)
            {
                lines.Add(postal ? before.Postal : before.Display);
            }

            lines.Add(postal ? Leading[i].Postal : Leading[i].Display);
        }

        if (LocalityLine is { } locality && LocalityAfter >= Leading.Count)
        {
            lines.Add(postal ? locality.Postal : locality.Display);
        }

        foreach ((string p, string d) in Delivery)
        {
            lines.Add(postal ? p : d);
        }

        // The last line of Britain, Ireland, Malta and Spain is several lines (post town, county or province, postcode).
        string last = postal ? Last.Postal : Last.Display;
        lines.Add(layout == AddressLayout.SingleLine ? last.Replace("\n", ", ", StringComparison.Ordinal) : last);
        if (includeCountry)
        {
            lines.Add(postal ? CountryLine : T2Text.Title(CountryLine, capitalsKept: false));
        }

        return string.Join(layout == AddressLayout.SingleLine ? ", " : "\n", lines);
    }
}

/// <summary>Text rewrites shared by the reader: the La Poste line form and natural title case.</summary>
internal static class T2Text
{
    // Words that stay in capitals in the display style: acronyms the authorities print so, and Roman numerals.
    private static readonly HashSet<string> Capitals = new(StringComparer.Ordinal)
    {
        "CEDEX", "BP", "CS", "TSA", "PF", "PB", "ZI", "ZA", "ZAC", "ZAD", "CCAL", "II", "III", "IV", "VI", "VII", "VIII", "IX", "XI", "XII", "CCI", "RC", "AP", "NV", "SA", "AG", "BV", "GMBH", "SARL", "SAS", "SPRL", "CVBA", "VZW", "ASBL",
        "PO", "GPO", "RMB", "RMS", "RSD", "CMB", "CMA", "CPA", "CDMX", "BC", "DC",
    };

    // Particles that are written in lower case inside a name.
    private static readonly HashSet<string> Particles = new(StringComparer.Ordinal)
    {
        "DE", "DU", "DES", "LA", "LE", "LES", "ET", "SUR", "SOUS", "EN", "AU", "AUX", "D", "L",
        "VAN", "DER", "DEN", "TE", "TEN", "HET", "VON", "ZU", "ZUR", "ZUM", "AM", "AN", "IM", "IN", "UND", "BEI", "OB",
        "DI", "DEL", "DELLA", "DA", "AL", "NAD", "POD", "AND",
        "LAS", "LOS", "Y", "DO", "DOS", "DAS", "DEI", "DEGLI", "DELLE", "DELLO", "OF",
    };

    /// <summary>
    /// La Poste's line form (Guide pratique courrier 2026, p. 19: capitals without accents and without punctuation, the hyphen counting as punctuation):
    /// letters and digits only, words separated by one space.
    /// </summary>
    internal static string Afnor(string text)
    {
        StringBuilder sb = new(text.Length);
        foreach (char c in AddressExpander.Fold(text))
        {
            sb.Append(char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : ' ');
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>UPU France sheet: the words SAINT and SAINTE of the locality line and of the line above it are abbreviated ST and STE.</summary>
    internal static string SaintAbbreviated(string afnorText)
    {
        string[] words = afnorText.Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            if (words[i] == "SAINT")
            {
                words[i] = "ST";
            }
            else if (words[i] == "SAINTE")
            {
                words[i] = "STE";
            }
        }

        return string.Join(' ', words);
    }

    /// <summary>Natural title case for display: a word with any lower-case letter is kept as written; an all-capital word gets one capital per part.</summary>
    internal static string Title(string text, bool capitalsKept, bool firstIsInitial = true)
    {
        string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        StringBuilder sb = new(text.Length);
        for (int w = 0; w < words.Length; w++)
        {
            if (w > 0)
            {
                sb.Append(' ');
            }

            sb.Append(TitleWord(words[w], w == 0 && firstIsInitial, capitalsKept));
        }

        return sb.ToString();
    }

    internal static string TitleWord(string word, bool first, bool capitalsKept = true)
    {
        bool anyLower = false;
        bool anyLetter = false;
        bool anyDigit = false;
        foreach (char c in word)
        {
            anyLower |= char.IsLower(c);
            anyLetter |= char.IsLetter(c);
            anyDigit |= char.IsAsciiDigit(c);
        }

        // Mixed case was chosen by the writer, and an identifier with digits (12B, N6W23001) keeps its letters.
        if (anyLower || !anyLetter || anyDigit || word.Length == 1)
        {
            return word;
        }

        string key = AddressExpander.Fold(word).ToUpperInvariant();
        if (capitalsKept && Capitals.Contains(key))
        {
            return word;
        }

        // L'EGLISE: the elided article stays lower case, the word after it gets its capital.
        int apostrophe = word.IndexOf('\'', StringComparison.Ordinal);
        if (apostrophe == 1 && word.Length > 2 && char.ToUpperInvariant(word[0]) is 'L' or 'D')
        {
            return char.ToLowerInvariant(word[0]) + "'" + TitleWord(word[2..], first: false, capitalsKept);
        }

        if (!first && Particles.Contains(key))
        {
            return word.ToLowerInvariant();
        }

        StringBuilder sb = new(word.Length);
        bool boundary = true;
        foreach (char c in word)
        {
            if (char.IsLetter(c))
            {
                sb.Append(boundary ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
                boundary = false;
            }
            else
            {
                sb.Append(c);
                boundary = c is '-' or '\u2013' or '\u2014' or '/' or '.' or '\'' && c != '\'';
            }
        }

        return sb.ToString();
    }
}
