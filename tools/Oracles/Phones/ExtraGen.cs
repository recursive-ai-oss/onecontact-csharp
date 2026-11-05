using System.Globalization;
using System.Text;
using RecursiveAI.OneContact.Phones.Metadata;

namespace RecursiveAI.OneContact.Oracles.Phones;

/// <summary>Generates the cases for short numbers, as-you-type formatting and the matcher (see ExtraDriver.java for the case kinds).</summary>
internal static partial class ExtraGen
{
    private static string Inv(int v) => v.ToString(CultureInfo.InvariantCulture);

    internal static int Run(string path, int seed, string mainPath, string shortPath)
    {
        Dictionary<string, TerritoryData> main = [];
        using (FileStream xml = File.OpenRead(mainPath))
        {
            foreach (TerritoryData t in PhoneMetadataXml.Read(xml))
            {
                main[t.Id == "001" ? "001-" + Inv(t.CountryCode) : t.Id] = t;
            }
        }

        List<TerritoryData> shorts;
        using (FileStream xml = File.OpenRead(shortPath))
        {
            shorts = PhoneMetadataXml.Read(xml);
        }

        using StreamWriter w = new(path, false, new UTF8Encoding(false));
        w.NewLine = "\n";
        int total = 0;
        total += ShortCases(w, main, shorts, seed);
        total += AsYouTypeCases(w, main, seed);
        total += MatcherCases(w, main, seed);
        return total;
    }

    private static int HashCode(string s, int seed)
    {
        unchecked
        {
            int h = seed * 31 + 17;
            foreach (char c in s)
            {
                h = (h * 131) + c;
            }

            return h;
        }
    }

    private static int ShortCases(StreamWriter w, Dictionary<string, TerritoryData> main, List<TerritoryData> shorts, int seed)
    {
        int total = 0;
        List<string> regionIds = [.. shorts.Select(s => s.Id).Where(id => main.ContainsKey(id))];
        foreach (TerritoryData t in shorts)
        {
            if (!main.TryGetValue(t.Id, out TerritoryData? m))
            {
                continue;
            }

            Random rng = new(HashCode(t.Id, seed));
            int cc = m.CountryCode;
            List<string> numbers = [];
            List<string> emergency = [];
            for (int k = 0; k < t.Descs.Length; k++)
            {
                DescData? d = t.Descs[k];
                if (d is null)
                {
                    continue;
                }

                List<string> got = [];
                if (d.Example is { Length: > 0 })
                {
                    got.Add(d.Example);
                }

                if (d.Pattern is not null)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        string? s = PatternSampler.Sample(d.Pattern, rng);
                        if (s is { Length: > 0 })
                        {
                            got.Add(s);
                        }
                    }
                }

                numbers.AddRange(got);
                if ((DescKind)k is DescKind.Emergency or DescKind.ExpandedEmergency or DescKind.TollFree)
                {
                    emergency.AddRange(got);
                }
            }

            List<string> all = [.. numbers.Distinct()];
            foreach (string n in numbers.Take(14).ToList())
            {
                all.Add(n + "0");
                all.Add(n + "9");
                all.Add(n + "12");
                if (n.Length > 1)
                {
                    all.Add(n[..^1]);
                    all.Add(n[..^1] + (char)('0' + (((n[^1] - '0') + 1) % 10)));
                }

                all.Add("1" + n);
                all.Add("0" + n);
            }

            all.AddRange(["1", "12", "123", "1234", "12345", "123456", "1234567", "00", "000", "911", "112", "999", "100", "0112"]);
            all = [.. all.Distinct()];
            string? sibling = shorts.Select(s => s.Id).FirstOrDefault(id => id != t.Id && main.TryGetValue(id, out TerritoryData? o) && o.CountryCode == cc);
            int i2 = 0;
            foreach (string n in all)
            {
                w.WriteLine($"{t.Id}-s{i2:D3}\tS\t{t.Id}\t{n}");
                total++;
                if (sibling is not null && i2 % 2 == 0)
                {
                    w.WriteLine($"{sibling}-s{i2:D3}x{t.Id}\tS\t{sibling}\t{n}");
                    total++;
                }

                i2++;
            }

            string other = regionIds[rng.Next(regionIds.Count)];
            foreach (string n in numbers.Take(3))
            {
                w.WriteLine($"{other}-s{i2++:D3}y{t.Id}\tS\t{other}\t{n}");
                total++;
            }

            int j = 0;
            foreach (string e in emergency.Distinct().Concat(["112", "911", "999", "000", "110", "15", "17", "18", "190", "192"]).Distinct())
            {
                string[] variants = [e, e + "0", e + "123", " " + e + " ", "+" + e, string.Join(" ", e.ToCharArray()), e + ".", e + "#", e + "x5", "-" + e, e[..Math.Max(1, e.Length - 1)], "0" + e, "\u0661\u0661\u0662" + e];
                foreach (string v in variants)
                {
                    w.WriteLine($"{t.Id}-e{j++:D3}\tE\t{t.Id}\t{Escape.Encode(v)}");
                    total++;
                }
            }
        }

        return total;
    }
}
