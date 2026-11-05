using System.Globalization;
using System.Text;
using RecursiveAI.OneContact.Phones;
using RecursiveAI.OneContact.Phones.Metadata;

namespace RecursiveAI.OneContact.Oracles.Phones;

internal static partial class ExtraGen
{
    private static readonly string[] AytAwkward =
    [
        "+", "++1", "+1+2", "1a2", "abc", "0", "00", "000", "011", "0011", "+0", "+00", "+1", "+12", "+123", "+1234", "+8", "+80", "+800", "+800123",
        "\uFF0B1234", "\uFF11\uFF12\uFF13\uFF14\uFF15", "\u0661\u0662\u0663\u0664\u0665\u0666", "1 2 3 4 5 6 7", "1-800-FLOWERS", "(202) 456", "202-456-1111",
        "2024561111", "12024561111", "+442079460958", "+44 (0)20 7946 0958", "+4407946", "07946095", "1#2", "12*34", "1234567890123456789", "911", "112",
    ];

    private static int AsYouTypeCases(StreamWriter w, Dictionary<string, TerritoryData> main, int seed)
    {
        PhoneNumberParser parser = PhoneNumberParser.Default;
        PhoneMetadata md = parser.Metadata;
        int total = 0;
        // The default regions for the fixed odd inputs: a few with different prefix habits, plus a region with no data.
        string[] oddRegions = ["US", "GB", "DE", "JP", "BR", "MX", "AR", "RU", "KZ", "IT", "CA", "ZZ", "IN", "FR", "AU", "NZ", "KR", "CN", "CO", "HU"];
        foreach (string r in oddRegions)
        {
            int k = 0;
            foreach (string t in AytAwkward)
            {
                w.WriteLine($"{r}-o{k++:D3}\tA\t{r}\t{Escape.Encode(t)}");
                total++;
            }
        }

        foreach (Region region in md.Regions)
        {
            Random rng = new(HashCode(region.Code, seed) ^ 0x5151);
            int cc = md.GetCallingCode(region);
            string natPrefix = parser.GetNationalPrefix(region, true) ?? "";
            string? idd = md.GetInternationalPrefix(region);
            if (idd is not null && !idd.All(char.IsAsciiDigit))
            {
                idd = null;
            }

            List<string> nsns = [];
            foreach (PhoneNumberType t in Enum.GetValues<PhoneNumberType>())
            {
                if (parser.GetExample(region, t) is { } e)
                {
                    nsns.Add(e.NationalSignificantNumber);
                }
            }

            TerritoryData td = main[region.Code];
            foreach (DescKind kind in new[] { DescKind.FixedLine, DescKind.Mobile, DescKind.TollFree, DescKind.PremiumRate, DescKind.General })
            {
                if (td.Descs[(int)kind]?.Pattern is { } p)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        if (PatternSampler.Sample(p, rng) is { Length: > 1 } s)
                        {
                            nsns.Add(s);
                        }
                    }
                }
            }

            List<string> typed = [];
            foreach (string nsn in nsns.Distinct())
            {
                typed.Add(nsn);
                typed.Add("+" + cc + nsn);
                if (natPrefix.Length > 0)
                {
                    typed.Add(natPrefix + nsn);
                    typed.Add("+" + cc + natPrefix + nsn);
                }

                if (idd is { Length: > 0 })
                {
                    typed.Add(idd + cc + nsn);
                }

                if (nsn.Length > 4)
                {
                    typed.Add(nsn[..3] + "-" + nsn[3..]);
                    typed.Add("(" + nsn[..2] + ") " + nsn[2..]);
                }

                if (rng.Next(3) == 0)
                {
                    typed.Add("+" + (cc == 1 ? 44 : 1) + nsn);
                    typed.Add("+" + cc + new string('0', 1) + nsn);
                }
            }

            // Random key presses: mostly digits, some separators, a plus sign, a prefix of the region and one odd character.
            for (int i = 0; i < 14; i++)
            {
                StringBuilder sb = new();
                if (rng.Next(4) == 0)
                {
                    sb.Append('+');
                }
                else if (rng.Next(3) == 0)
                {
                    sb.Append(rng.Next(2) == 0 ? natPrefix : idd ?? "00");
                }

                if (rng.Next(2) == 0)
                {
                    sb.Append(cc);
                }

                int len = 1 + rng.Next(13);
                for (int j = 0; j < len; j++)
                {
                    int r = rng.Next(40);
                    sb.Append(r < 34 ? (char)('0' + (r % 10)) : r == 34 ? ' ' : r == 35 ? '-' : r == 36 ? '(' : r == 37 ? ')' : r == 38 ? '.' : 'x');
                }

                typed.Add(sb.ToString());
            }

            int n = 0;
            foreach (string t in typed.Distinct())
            {
                w.WriteLine($"{region.Code}-a{n++:D3}\tA\t{region.Code}\t{Escape.Encode(t)}");
                total++;
            }
        }

        return total;
    }
}
