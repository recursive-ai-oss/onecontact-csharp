using System.Text;
using RecursiveAI.OneContact.Phones;
using RecursiveAI.OneContact.Phones.Metadata;

namespace RecursiveAI.OneContact.Oracles.Phones;

internal static partial class ExtraGen
{
    private static readonly string[] Templates =
    [
        "Call {0} now", "Phone: {0}.", "tel {0}, fax {1}", "({0})", "{0}/{1}", "{0} - {1}", "{0}-{1}", "1.{0}", "ref:{0}ab", "ab{0}", "{0}{1}",
        "Dial {0} or {1} or {2}.", "Born 1980-12-12, call {0}", "ID 123456789012345678 phone {0}", "at 10:30:15 {0}", "pp. 2012-2013 {0}",
        "{0}, {1};{2}", "Mixed: {0} (ext 5)", "The number {0}x{1}", "<{0}>", "\"{0}\"", "{0}\n{1}", "x{0}", "{0} ext.", "{0}#12", "{0} 12/03/2021",
        "{0}. {1}. {2}.", "{0}\u2013{1}", "[{0}]", "{0} e {1}", "{0} oder {1}", "\u260E {0}", "{0}:{1}", "{0}\t{1}", "  {0}  ", "{0}{0}", "+{0}",
    ];

    private static readonly string[] Noise =
    [
        "The date was 2012-12-22 and the time 12:30:15.", "See pages 1-2 / 3-4 and 12/03/2021 at 10.30.", "v1.2.3.4.5 build 20201231 123456789",
        "ISBN 978-3-16-148410-0 and ISSN 0378-5955.", "1234 5678 9012 3456 card", "Totals: 1,234,567.89 EUR; 5 000 000 USD", "(2) 11-45-67 and 3-4-5",
        "zip 90210-1234 and 1-800-FLOWERS or 1-800-GOT-MILK", "\u0661\u0662\u0663 \u0664\u0665\u0666 \u0667\u0668\u0669\u0660", "abc 123 def 4567 ghi 89012",
        "+ + + 1 2 3 + 4 5 6", "0044 20 7946 0958 or 011 44 20 7946 0958", "00 1 202 456 1111 and 001 202 456 1111", "", " ", "1", "+", "x", "ext 12",
    ];

    private static string Group(string digits, Random rng, string sep)
    {
        StringBuilder sb = new();
        int i = 0;
        while (i < digits.Length)
        {
            int len = Math.Min(digits.Length - i, 2 + rng.Next(3));
            if (sb.Length > 0)
            {
                sb.Append(sep);
            }

            sb.Append(digits, i, len);
            i += len;
        }

        return sb.ToString();
    }

    private static List<string> NumberForms(PhoneNumberParser parser, Region region, Random rng)
    {
        List<string> forms = [];
        int taken = 0;
        foreach (PhoneNumberType t in Enum.GetValues<PhoneNumberType>())
        {
            if (t == PhoneNumberType.Unknown || parser.GetExample(region, t) is not { } e || taken >= 5)
            {
                continue;
            }

            taken++;
            forms.Add(e.Format(PhoneFormat.National));
            forms.Add(e.Format(PhoneFormat.International));
            forms.Add(e.Format(PhoneFormat.E164));
            if (rng.Next(2) == 0)
            {
                forms.Add(e.Format(PhoneFormat.Rfc3966));
                forms.Add(e.Format(PhoneFormat.National) + " ext. 123");
                forms.Add(e.Format(PhoneFormat.International) + " x45");
            }

            string nsn = e.NationalSignificantNumber;
            forms.Add(Group(nsn, rng, " "));
            forms.Add(Group(nsn, rng, "-"));
            forms.Add("+" + e.CountryCode + " " + Group(nsn, rng, "."));
            forms.Add(e.Format(PhoneFormat.International).Replace(' ', '-'));
            forms.Add(e.Format(PhoneFormat.National).Replace(" ", "", StringComparison.Ordinal));
            forms.Add(nsn.Length > 3 ? nsn[..3] + " " + nsn[3..] : nsn);
            forms.Add("(" + e.CountryCode + ") " + nsn);
            if (e.Format(PhoneFormat.National) is { Length: > 3 } nat)
            {
                forms.Add(nat[..^1] + (char)('0' + ((nat[^1] - '0' + 3) % 10 + 10) % 10));
                forms.Add(nat + "0");
            }
        }

        return forms;
    }

    private static int MatcherCases(StreamWriter w, Dictionary<string, TerritoryData> main, int seed)
    {
        _ = main;
        PhoneNumberParser parser = PhoneNumberParser.Default;
        int total = 0;
        foreach (Region region in parser.Metadata.Regions)
        {
            Random rng = new(HashCode(region.Code, seed) ^ 0x3131);
            List<string> forms = NumberForms(parser, region, rng);
            if (forms.Count == 0)
            {
                continue;
            }

            int n = 0;
            int count = 36;
            for (int i = 0; i < count; i++)
            {
                string template = Templates[rng.Next(Templates.Length)];
                string text = template
                    .Replace("{0}", forms[rng.Next(forms.Count)], StringComparison.Ordinal)
                    .Replace("{1}", forms[rng.Next(forms.Count)], StringComparison.Ordinal)
                    .Replace("{2}", forms[rng.Next(forms.Count)], StringComparison.Ordinal);
                if (rng.Next(4) == 0)
                {
                    text = Noise[rng.Next(Noise.Length)] + " " + text + " " + Noise[rng.Next(Noise.Length)];
                }

                w.WriteLine($"{region.Code}-m{n++:D3}\tM\t{region.Code}\t{Escape.Encode(text)}");
                total++;
            }

            // Longer texts stitched from number forms, noise and loose punctuation, as running prose and scraped pages are.
            string[] glue = [" ", ", ", ". ", "; ", " - ", "/", "\n", " (", ") ", " x", "–", " and ", " ref ", "#", ":", "  "];
            for (int i = 0; i < 10; i++)
            {
                StringBuilder sb = new();
                int parts = 2 + rng.Next(8);
                for (int j = 0; j < parts; j++)
                {
                    sb.Append(rng.Next(3) == 0 ? Noise[rng.Next(Noise.Length)] : forms[rng.Next(forms.Count)]);
                    sb.Append(glue[rng.Next(glue.Length)]);
                }

                w.WriteLine($"{region.Code}-m{n++:D3}\tM\t{region.Code}\t{Escape.Encode(sb.ToString())}");
                total++;
            }

            foreach (string noise in Noise.Take(3 + rng.Next(Noise.Length - 3)))
            {
                w.WriteLine($"{region.Code}-m{n++:D3}\tM\t{region.Code}\t{Escape.Encode(noise)}");
                total++;
            }
        }

        return total;
    }
}
