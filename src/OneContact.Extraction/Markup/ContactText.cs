using System.Text;
using RecursiveAI.OneContact.Addresses;

namespace RecursiveAI.OneContact.Extraction.Markup;

/// <summary>Cuts the value out of the URI forms links carry (<c>mailto:</c>, <c>tel:</c>), the way the URI specifications write them.</summary>
internal static class ContactText
{
    /// <summary>RFC 6068 §2: the addr-spec of a <c>mailto:</c> URI, before any <c>?</c> header fields, percent-decoded. Only the first of a comma-separated list.</summary>
    public static string EmailFrom(string text)
    {
        string t = text.Trim();
        if (t.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            t = t[7..];
            int query = t.IndexOf('?', StringComparison.Ordinal);
            if (query >= 0)
            {
                t = t[..query];
            }

            int comma = t.IndexOf(',', StringComparison.Ordinal);
            if (comma >= 0)
            {
                t = t[..comma];
            }

            t = PercentDecode(t);
        }

        return t.Trim();
    }

    /// <summary>RFC 3966 §3: the telephone-subscriber of a <c>tel:</c> URI, percent-decoded, with its parameters (<c>;ext=</c>) kept for the phone parser.</summary>
    public static string PhoneFrom(string text)
    {
        string t = text.Trim();
        if (t.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
        {
            t = PercentDecode(t[4..]);
        }

        return t.Trim();
    }

    /// <summary>The recipients of a <c>mailto:</c> URI (RFC 6068 §2: a comma-separated list before the first <c>?</c>), percent-decoded.</summary>
    public static List<string> MailtoRecipients(string href)
    {
        string t = href.Trim()[7..];
        int query = t.IndexOf('?', StringComparison.Ordinal);
        if (query >= 0)
        {
            t = t[..query];
        }

        var list = new List<string>();
        foreach (string part in t.Split(','))
        {
            string r = PercentDecode(part).Trim();
            if (r.Length > 0)
            {
                list.Add(r);
            }
        }

        return list;
    }

    // RFC 3986 §2.1: %HH is one byte; a run of them is UTF-8. A malformed escape stays as written.
    internal static string PercentDecode(string s)
    {
        if (!s.Contains('%', StringComparison.Ordinal))
        {
            return s;
        }

        var bytes = new List<byte>(s.Length);
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '%' && i + 2 < s.Length && char.IsAsciiHexDigit(s[i + 1]) && char.IsAsciiHexDigit(s[i + 2]))
            {
                bytes.Add(Convert.ToByte(s.Substring(i + 1, 2), 16));
                i += 2;
                continue;
            }

            Flush(bytes, sb);
            sb.Append(s[i]);
        }

        Flush(bytes, sb);
        return sb.ToString();
    }

    private static void Flush(List<byte> bytes, StringBuilder sb)
    {
        if (bytes.Count > 0)
        {
            sb.Append(Encoding.UTF8.GetString(bytes.ToArray()));
            bytes.Clear();
        }
    }
}

/// <summary>
/// Reads the country a page gives for an address: a two-letter code, or a name (the core library's country data first, then the
/// aliases pages commonly use). An unrecognized name is not guessed at; the address keeps no country and says so.
/// </summary>
internal static class CountryNames
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["USA"] = "US", ["UK"] = "GB", ["GREATBRITAIN"] = "GB",
        ["DEUTSCHLAND"] = "DE", ["ESPANA"] = "ES", ["ITALIA"] = "IT", ["HOLLAND"] = "NL", ["THENETHERLANDS"] = "NL",
        ["SCHWEIZ"] = "CH", ["SUISSE"] = "CH", ["SVIZZERA"] = "CH", ["CZECHREPUBLIC"] = "CZ", ["CZECHIA"] = "CZ", ["BRASIL"] = "BR",
    };

    public static bool TryResolve(string text, out Region region)
    {
        string t = text.Trim();
        var key = new StringBuilder(t.Length);
        foreach (char c in t)
        {
            if (char.IsAsciiLetter(c))
            {
                key.Append(char.ToUpperInvariant(c));
            }
            else if (!char.IsWhiteSpace(c) && c != '.' && c != '-')
            {
                key.Clear();
                break;
            }
        }

        // UK is the usual spelling of the ISO 3166 code GB; the other aliases are spellings pages use for countries the data names differently.
        if (key.Length > 0 && Aliases.TryGetValue(key.ToString(), out string? code))
        {
            region = Region.Parse(code);
            return true;
        }

        if (AddressRegions.TryFindByName(t, out region))
        {
            return true;
        }

        return Region.TryParse(t, out region);
    }
}
