using System.Text;
using RecursiveAI.OneContact.Addresses.Parsing;
using RecursiveAI.OneContact.Addresses.Streets;

namespace RecursiveAI.OneContact.Addresses.Standardization;

/// <summary>How an address is written.</summary>
public enum AddressStyle
{
    /// <summary>The postal authority's own style: upper case, its abbreviations, no punctuation beyond what the guide keeps (USPS Publication 28 section 212, Canada Post Addressing Guidelines section 2).</summary>
    Postal = 0,

    /// <summary>The same tokens as <see cref="Postal"/> in natural title case, with the authority's abbreviations and no periods.</summary>
    Display = 1,
}

/// <summary>Whether an address is written on several lines or on one.</summary>
public enum AddressLayout
{
    /// <summary>One line per address line, separated by a line feed, as on a mailing label.</summary>
    Label = 0,

    /// <summary>All lines on one line, separated by a comma and a space.</summary>
    SingleLine = 1,
}

/// <summary>Casing rules of the <see cref="AddressStyle.Display"/> rendering. Casing is invariant: no culture is consulted.</summary>
internal static class DisplayCasing
{
    // Words the authorities print in capitals in any style: directionals, state and province codes, box and route designators, military words.
    private static readonly HashSet<string> Capitals = new(StringComparer.Ordinal)
    {
        "N", "S", "E", "W", "NE", "NW", "SE", "SW", "NO", "SO", "PO", "RR", "HC", "GD", "PR", "CP", "STN", "RPO", "SUCC", "COP", "LCD", "PMB", "APO", "FPO", "DPO",
        "AE", "AP", "AA", "CPR", "OPC", "PSC", "UPR", "US", "USA", "II", "III", "IV", "DC", "URB", "COND", "EDIF", "RES", "NUM", "BLQ",
    };

    // A number after one of these names a unit, so its letters are part of the identifier (APT 4E), never an ordinal ending (5E, French for fifth).
    private static readonly HashSet<string> UnitWords = new(StringComparer.Ordinal)
    {
        "APT", "APARTMENT", "UNIT", "STE", "SUITE", "BLDG", "BUILDING", "RM", "ROOM", "FL", "FLOOR", "LOT", "SPC", "SPACE", "TRLR", "SLIP", "DEPT", "HNGR", "KEY", "LBBY", "OFC", "PH", "PIER", "SIDE", "STOP", "UPPR", "LOWR", "REAR", "FRNT", "BSMT", "APP", "BUREAU", "BOX", "UNITE", "#",
    };

    private static readonly HashSet<string> Lowercase = new(StringComparer.Ordinal) { "DE", "DES", "DU", "LA", "LE", "LES", "DEL", "LAS", "LOS", "ET", "AND", "OF", "THE", "SUR", "SOUS", "EN" };

    /// <summary>Turns a standardized (upper case) line into natural title case.</summary>
    /// <param name="postal">The postal-style text.</param>
    /// <param name="keepCapitals">True when words of <see cref="Capitals"/> stay in capitals (street lines); false for plain names such as a city.</param>
    /// <returns>The display text.</returns>
    internal static string Title(string postal, bool keepCapitals)
    {
        StringBuilder sb = new(postal.Length);
        string[] words = postal.Split(' ');
        for (int w = 0; w < words.Length; w++)
        {
            // Display writes the # designator tight against its number (#72); Postal keeps the space Publication 28 section 213.2 asks for.
            if (w > 0 && words[w - 1] != "#")
            {
                sb.Append(' ');
            }

            string word = words[w];
            if (word.Length == 0)
            {
                continue;
            }

            if (keepCapitals && Capitals.Contains(word))
            {
                sb.Append(word);
                continue;
            }

            if (w > 0 && Lowercase.Contains(word) && !keepCapitals)
            {
                sb.Append(word.ToLowerInvariant());
                continue;
            }

            AppendTitleWord(sb, word, afterUnitWord: w > 0 && UnitWords.Contains(words[w - 1]));
        }

        return sb.ToString();
    }

    private static void AppendTitleWord(StringBuilder sb, string word, bool afterUnitWord)
    {
        // A word with a digit that starts with a letter is an identifier (N6W23001, K1A, R2, B12): its letters stay as the postal style has them.
        if (!char.IsAsciiDigit(word[0]) && word.AsSpan().ContainsAnyInRange('0', '9'))
        {
            sb.Append(word);
            return;
        }

        // A word that starts with digits keeps them: an ordinal ending is lower case (5TH is 5th), any other letters stay as they are (a letter after a house number is part of the number: 123A).
        if (char.IsAsciiDigit(word[0]))
        {
            int d = 0;
            while (d < word.Length && char.IsAsciiDigit(word[d]))
            {
                d++;
            }

            string tail = word[d..];
            sb.Append(word, 0, d);
            sb.Append(!afterUnitWord && IsOrdinalEnd(tail, 0) ? tail.ToLowerInvariant() : tail);
            return;
        }

        bool boundary = true;
        for (int i = 0; i < word.Length; i++)
        {
            char c = word[i];
            if (char.IsLetter(c))
            {
                sb.Append(boundary ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
                boundary = false;
            }
            else
            {
                sb.Append(c);

                // A letter after a hyphen or slash starts a new capital; after an apostrophe only in O'BRIEN (the input does not say where other capitals go).
                boundary = c is '-' or '/' || (c == '\'' && i == 1 && char.ToUpperInvariant(word[0]) == 'O');
            }
        }
    }

    private static bool IsOrdinalEnd(string word, int i)
    {
        string tail = word[i..];
        return tail is "ST" or "ND" or "RD" or "TH" or "RE" or "ERE" or "ÈRE" or "E" or "ER" or "EME" or "ÈME";
    }
}

/// <summary>Writes a <see cref="ParsedAddress"/> in a style and layout.</summary>
internal static class AddressRenderer
{
    /// <summary>Renders the address.</summary>
    /// <param name="a">The address.</param>
    /// <param name="style">Postal or display.</param>
    /// <param name="layout">Label or single line.</param>
    /// <param name="includeCountry">True to add a country line (<c>USA</c>, <c>CANADA</c>); the authorities leave it out of domestic addresses.</param>
    /// <returns>The text.</returns>
    internal static string Render(ParsedAddress a, AddressStyle style, AddressLayout layout, bool includeCountry)
    {
        if (a.Tier2 is not null)
        {
            return a.Tier2.Render(style, layout, includeCountry);
        }

        List<string> lines = [];
        foreach (ParsedAddressText lead in a.LeadingLines)
        {
            lines.Add(style == AddressStyle.Postal ? lead.Standard : LeadingDisplay(lead.Text));
        }

        foreach (StreetLine line in a.DeliveryLines)
        {
            string postal = line.ToPostalString();
            lines.Add(style == AddressStyle.Postal ? postal : DisplayCasing.Title(postal, keepCapitals: true));
        }

        string? last = LastLine(a, style, layout);
        if (last is not null)
        {
            lines.Add(last);
        }

        if (includeCountry && a.Country.IsUnknown is false)
        {
            string country = a.Country == Region.CA ? "CANADA" : "USA";
            lines.Add(style == AddressStyle.Postal ? country : DisplayCasing.Title(country, keepCapitals: true));
        }

        return string.Join(layout == AddressLayout.Label ? "\n" : ", ", lines);
    }

    // Names and organizations are not ours to rewrite: a line the caller wrote in mixed case stays as written; one in capitals becomes title case.
    private static string LeadingDisplay(string text) =>
        text.Any(char.IsLower) ? text : DisplayCasing.Title(text.ToUpperInvariant(), keepCapitals: false);

    private static string? LastLine(ParsedAddress a, AddressStyle style, AddressLayout layout)
    {
        string city = a.Locality?.Standard ?? string.Empty;
        string admin = a.AdminArea?.Standard ?? string.Empty;
        string postal = a.PostalCode?.Standard ?? string.Empty;
        if (city.Length == 0 && admin.Length == 0 && postal.Length == 0)
        {
            return null;
        }

        if (style == AddressStyle.Postal)
        {
            // Publication 28 section 224 (two spaces preferred between state and ZIP) and Canada Post section 2 (two spaces before the postal code): on a label the gap is two spaces.
            string gap = layout == AddressLayout.Label ? "  " : " ";
            StringBuilder sb = new();
            Append(sb, city, " ");
            Append(sb, admin, " ");
            if (postal.Length > 0)
            {
                if (sb.Length > 0)
                {
                    sb.Append(admin.Length > 0 ? gap : " ");
                }

                sb.Append(postal);
            }

            return sb.ToString();
        }

        StringBuilder d = new();
        Append(d, city.Length == 0 ? string.Empty : city is "APO" or "FPO" or "DPO" ? city : DisplayCasing.Title(city, keepCapitals: false), string.Empty);
        if (admin.Length > 0)
        {
            if (d.Length > 0)
            {
                d.Append(", ");
            }

            d.Append(admin);
        }

        if (postal.Length > 0)
        {
            if (d.Length > 0)
            {
                d.Append(' ');
            }

            d.Append(postal);
        }

        return d.ToString();
    }

    private static void Append(StringBuilder sb, string part, string separator)
    {
        if (part.Length == 0)
        {
            return;
        }

        if (sb.Length > 0)
        {
            sb.Append(separator);
        }

        sb.Append(part);
    }
}
