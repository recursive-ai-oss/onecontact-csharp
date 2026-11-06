using System.Globalization;
using System.Text;

namespace OneContact.Data.Blocks;

/// <summary>A North American number: area code, exchange and line.</summary>
internal sealed record Phone(string Area, string Exchange, string Line, string? Ext)
{
    /// <summary>For numbers outside North America: the digits as shown (national significant number, with the country code when shown), and the shown text.</summary>
    internal string? ShownDigits { get; init; }

    internal string? ShownText { get; init; }

    internal string Digits => ShownDigits ?? Area + Exchange + Line;
}

/// <summary>Generates the structured field values blocks are made of. Phone numbers are valid for their region by the library's own metadata and drawn at random,
/// so they are not taken from any directory; email addresses and web addresses are built from the person and company strings and generated or reserved domains.</summary>
internal sealed class FieldGen
{
    private readonly string[][] _areas = new string[3][];

    private static readonly string[] QuebecAreas = ["418", "438", "450", "514", "579", "581", "819", "873", "367"];
    private static readonly string[] FreeMail = ["gmail.com", "yahoo.com", "outlook.com", "hotmail.com", "icloud.com", "aol.com", "protonmail.com", "live.com"];
    private static readonly string[] FreeMailCa = ["videotron.ca", "bell.net", "sympatico.ca", "rogers.com", "shaw.ca", "telus.net", "gmail.com", "hotmail.com", "yahoo.ca"];
    private static readonly string[] Stop = ["the", "of", "and", "inc", "llc", "ltd", "corp", "co", "company", "les", "la", "le", "de", "du", "des", "et", "l", "d", "lp", "llp", "pllc", "pc", "a", "an", "at", "in", "for"];
    private static readonly string[] Syll = ["bel", "mar", "ton", "ver", "lan", "dor", "mer", "fin", "gar", "ham", "kel", "lor", "nor", "sol", "tay", "wes", "ash", "bri", "cal", "dun", "elm", "fair", "glen", "hill", "oak", "pine", "river", "stone", "view", "wood"];

    internal FieldGen()
    {
        // Area codes that the phone metadata accepts for each region, found by trying every NANP area code once.
        List<string> us = [];
        List<string> ca = [];
        for (int npa = 200; npa <= 999; npa++)
        {
            string a = npa.ToString(CultureInfo.InvariantCulture);
            if (a[1] == '9' || a[1] == '1' && a[2] == '1')
            {
                continue;
            }

            if (Valid(a, "234", "5678", Region.US))
            {
                us.Add(a);
            }

            if (Valid(a, "234", "5678", Region.CA))
            {
                ca.Add(a);
            }
        }

        _areas[(int)Loc.UsEn] = us.ToArray();
        _areas[(int)Loc.CaEn] = ca.ToArray();
        _areas[(int)Loc.CaFr] = ca.Where(a => QuebecAreas.Contains(a, StringComparer.Ordinal)).ToArray();
    }

    private static bool Valid(string area, string exchange, string line, Region region)
    {
        PhoneNumber n = PhoneNumber.Create(1, area + exchange + line);
        return n.IsValid && n.IsValidForRegion(region);
    }

    /// <summary>A number of a Tier 2 country: valid for its region by the library's metadata, made by changing the last digits of the region's example number, written the national or the international way.</summary>
    internal Phone PhoneT2(Prng r, string country, bool ext)
    {
        Region region = Region.Parse(country);
        foreach (PhoneNumberType type in new[] { r.Chance(0.3) ? PhoneNumberType.Mobile : PhoneNumberType.FixedLine, PhoneNumberType.FixedLine })
        {
            PhoneNumber? example = PhoneNumber.GetExample(region, type);
            if (example is null)
            {
                continue;
            }

            string nsn = example.Value.NationalSignificantNumber;
            int keep = Math.Max(2, nsn.Length - 4 - r.Int(3));
            for (int tries = 0; tries < 60; tries++)
            {
                char[] digits = nsn.ToCharArray();
                for (int i = keep; i < digits.Length; i++)
                {
                    digits[i] = (char)('0' + r.Int(10));
                }

                PhoneNumber candidate = PhoneNumber.Create(example.Value.CountryCode, new string(digits));
                if (!candidate.IsValid || !candidate.IsValidForRegion(region))
                {
                    continue;
                }

                bool international = r.Chance(0.45);
                string shown = candidate.Format(international ? PhoneFormat.International : PhoneFormat.National);
                string only = new(shown.Where(char.IsAsciiDigit).ToArray());
                string? extension = ext ? (1 + r.Int(r.Chance(0.5) ? 99 : 999)).ToString(CultureInfo.InvariantCulture) : null;
                return new Phone(string.Empty, string.Empty, only, extension) { ShownDigits = only, ShownText = shown };
            }
        }

        PhoneNumber fallback = PhoneNumber.GetExample(region)!.Value;
        string fb = fallback.Format(PhoneFormat.International);
        string fbDigits = new(fb.Where(char.IsAsciiDigit).ToArray());
        return new Phone(string.Empty, string.Empty, fbDigits, null) { ShownDigits = fbDigits, ShownText = fb };
    }

    internal Phone Phone(Prng r, Loc loc, bool ext)
    {
        string[] areas = _areas[(int)loc];
        Region region = loc == Loc.UsEn ? Region.US : Region.CA;
        for (int tries = 0; tries < 50; tries++)
        {
            string area = areas[r.Int(areas.Length)];
            string exchange = ((char)('2' + r.Int(8))).ToString() + r.Int(10).ToString(CultureInfo.InvariantCulture) + r.Int(10).ToString(CultureInfo.InvariantCulture);
            if (exchange[1] == '1' && exchange[2] == '1')
            {
                continue;
            }

            string line = r.Int(10000).ToString("D4", CultureInfo.InvariantCulture);
            if (Valid(area, exchange, line, region))
            {
                return new Phone(area, exchange, line, ext ? (1 + r.Int(r.Chance(0.5) ? 99 : 9999)).ToString(CultureInfo.InvariantCulture) : null);
            }
        }

        return new Phone(areas[0], "234", "5678", null);
    }

    /// <summary>Writes a number the way people write it. Returns the shown text; <paramref name="obfuscated"/> is true for spaced-out digits.</summary>
    internal string FormatPhone(Phone p, Prng r, Loc loc, LexSet lex, bool allowObfuscation, out bool obfuscated)
    {
        obfuscated = false;
        if (p.ShownText is not null)
        {
            return p.Ext is not null ? p.ShownText + lex.ExtLabels[r.Int(lex.ExtLabels.Length)] + p.Ext : p.ShownText;
        }

        string a = p.Area;
        string x = p.Exchange;
        string l = p.Line;
        string core;
        if (allowObfuscation && r.Chance(0.03))
        {
            obfuscated = true;
            return string.Join(r.Chance(0.7) ? " " : "-", (a + x + l).Select(c => c.ToString()));
        }

        switch (r.Weighted([22, 20, 12, 8, 9, 6, 6, 4, 4, 3, 3, 3]))
        {
            case 0: core = $"({a}) {x}-{l}"; break;
            case 1: core = $"{a}-{x}-{l}"; break;
            case 2: core = $"{a}.{x}.{l}"; break;
            case 3: core = $"{a} {x} {l}"; break;
            case 4: core = $"+1 {a} {x} {l}"; break;
            case 5: core = $"+1 ({a}) {x}-{l}"; break;
            case 6: core = $"1-{a}-{x}-{l}"; break;
            case 7: core = $"({a}){x}-{l}"; break;
            case 8: core = $"{a}{x}{l}"; break;
            case 9: core = $"1 ({a}) {x}-{l}"; break;
            case 10: core = $"+1-{a}-{x}-{l}"; break;
            default: core = loc == Loc.CaFr ? $"{a} {x}-{l}" : $"{a}/{x}-{l}"; break;
        }

        if (p.Ext is not null)
        {
            core += lex.ExtLabels[r.Int(lex.ExtLabels.Length)] + p.Ext;
        }

        return core;
    }

    internal static string Fold(string s)
    {
        StringBuilder b = new();
        foreach (char c in s.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            char lc = char.ToLowerInvariant(c);
            if (lc is >= 'a' and <= 'z' || lc is >= '0' and <= '9')
            {
                b.Append(lc);
            }
        }

        return b.ToString();
    }

    /// <summary>A domain name for a company: from its name when the name has usable words, otherwise generated.</summary>
    internal string Domain(Prng r, Loc loc, string companyBase)
    {
        if (r.Chance(0.1))
        {
            return r.Pick(new[] { "example.com", "example.org", "example.net" });
        }

        List<string> words = [];
        foreach (string w in companyBase.Split([' ', '-', '&', ',', '.', '\'', '(', ')', '/'], StringSplitOptions.RemoveEmptyEntries))
        {
            string f = Fold(w);
            if (f.Length > 0 && !Stop.Contains(f, StringComparer.Ordinal))
            {
                words.Add(f);
            }
        }

        string name;
        if (words.Count == 0 || r.Chance(0.12))
        {
            name = Syll[r.Int(Syll.Length)] + Syll[r.Int(Syll.Length)];
            if (r.Chance(0.3))
            {
                name += Syll[r.Int(Syll.Length)];
            }
        }
        else
        {
            int k = Math.Min(words.Count, r.Chance(0.5) ? 1 : 2 + r.Int(2));
            name = string.Concat(words.Take(k));
            for (int more = k; name.Length < 4 && more < words.Count; more++)
            {
                name += words[more];
            }
            if (name.Length > 24)
            {
                name = name[..24];
            }
        }

        string tld = loc == Loc.T2
            ? r.Pick(new[] { "com", "com", "de", "de", "fr", "fr", "nl", "it", "es", "pl", "cz", "se", "no", "dk", "fi", "at", "ch", "be", "pt", "hu", "ro", "eu", "net", "org", "si", "sk", "hr", "lt", "lv", "ee", "ie", "co.uk", "mt", "mx", "com.au" })
            : loc == Loc.UsEn
            ? r.Pick(new[] { "com", "com", "com", "com", "com", "com", "com", "net", "org", "us", "biz", "co", "io" })
            : r.Pick(new[] { "ca", "ca", "ca", "ca", "ca", "com", "com", "com", "org", "net", loc == Loc.CaFr ? "qc.ca" : "ca" });
        return name + "." + tld;
    }

    /// <summary>A person's mailbox name. Mailboxes at free providers are used for CRM-like text more than for pages.</summary>
    internal string Mailbox(Prng r, PersonRec p, bool free)
    {
        string g = Fold(p.Given.Split(' ', '-')[0]);
        string f = Fold(p.Family.Split(' ')[^1]);
        if (g.Length == 0 || f.Length == 0)
        {
            return "contact";
        }

        if (free)
        {
            return r.Weighted([40, 25, 20, 15]) switch
            {
                0 => $"{g}.{f}",
                1 => $"{g}{f}",
                2 => $"{g}{f}{r.Int(100)}",
                _ => $"{g[0]}{f}{(r.Chance(0.4) ? r.Int(1990, 2010) : r.Int(100))}",
            };
        }

        return r.Weighted([40, 15, 10, 8, 5, 4]) switch
        {
            0 => $"{g}.{f}",
            1 => $"{g[0]}{f}",
            2 => g,
            3 => $"{g}_{f}",
            4 => $"{g}{f[0]}",
            _ => $"{f}.{g}",
        };
    }

    private static readonly string[] FreeMailT2 = ["gmail.com", "outlook.com", "hotmail.com", "yahoo.com", "icloud.com", "web.de", "gmx.de", "gmx.net", "t-online.de", "orange.fr", "free.fr", "laposte.net", "libero.it", "wp.pl", "o2.pl", "seznam.cz", "centrum.cz", "telenet.be", "ziggo.nl", "live.nl", "bluewin.ch", "sapo.pt", "terra.es", "proton.me", "post.sk"];

    internal string FreeDomain(Prng r, Loc loc) => loc == Loc.T2 ? r.Pick(FreeMailT2) : loc == Loc.UsEn ? r.Pick(FreeMail) : r.Pick(FreeMailCa);

    /// <summary>The shown spelling of an email address; <paramref name="mode"/> is "o" when it is obfuscated.</summary>
    internal static string ShowEmail(string email, Prng r, bool allowObfuscation, out string mode)
    {
        mode = "";
        if (allowObfuscation && r.Chance(0.06))
        {
            mode = "o";
            string at = r.Pick(new[] { "[at]", "(at)", " at ", "{at}", " [at] ", " (at) ", " AT " });
            string dot = r.Pick(new[] { "[dot]", "(dot)", " dot ", "{dot}", " [dot] ", " (dot) ", " DOT " });
            int i = email.IndexOf('@', StringComparison.Ordinal);
            string host = email[(i + 1)..].Replace(".", dot, StringComparison.Ordinal);
            string local = email[..i];
            return (r.Chance(0.15) ? local.Replace(".", dot, StringComparison.Ordinal) : local) + at + host;
        }

        if (r.Chance(0.04))
        {
            mode = "c";
            return char.ToUpperInvariant(email[0]) + email[1..];
        }

        return email;
    }

    /// <summary>A web address as written on a page or in a note, built on a domain. Social profile pages are included.</summary>
    internal static string Web(Prng r, string domain, string slug, string? personSlug = null)
    {
        switch (r.Weighted([30, 22, 14, 10, 6, 5, 4, 4, 3, 2]))
        {
            case 0: return "www." + domain;
            case 1: return "https://www." + domain;
            case 2: return domain;
            case 3: return "https://" + domain;
            case 4: return "http://www." + domain;
            case 5: return "www." + domain + r.Pick(new[] { "/contact", "/about", "/contact-us", "/locations", "/team" });
            case 6: return "https://" + domain + "/" + r.Pick(new[] { "contact", "about-us", "our-team", "en/contact" });
            case 7: return "linkedin.com/company/" + slug;
            case 8: return "facebook.com/" + slug;
            default:
                return personSlug is null
                    ? "https://www.linkedin.com/company/" + slug
                    : "https://www.linkedin.com/in/" + personSlug + "-" + (r.Int(900) + 100).ToString("x", CultureInfo.InvariantCulture);
        }
    }
}

internal static class PrngExtensions
{
    internal static int Int(this Prng r, int lo, int hi) => lo + r.Int(hi - lo);
}
