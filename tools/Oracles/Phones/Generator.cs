using System.Globalization;
using System.Text;
using RecursiveAI.OneContact.Phones;
using RecursiveAI.OneContact.Phones.Metadata;

namespace RecursiveAI.OneContact.Oracles.Phones;

/// <summary>
/// Generates parity cases. Per region: texts built from the region's own example numbers (written nationally, internationally, with the
/// national prefix, punctuated, with extensions, in other scripts, as vanity numbers, as <c>tel:</c> URIs, truncated, extended and with
/// digits changed), numbers of other regions, a fixed list of awkward inputs and random garbage. Everything is deterministic in the seed.
/// </summary>
internal static class Generator
{
    private static readonly string[] Awkward =
    [
        "", " ", "1", "12", "123", "+", "+1", "++1 202 456 1111", "tel:", "00", "+00", "0044", "(((", "abc", "1-800-FLOWERS", "+800 1234 5678",
        "+0 123 4567", "12345678901234567890", "+999 123 4567", "x5", "1234567;ext=", "1234567 ext", "#1234567", "tel:+1;phone-context=",
        "tel:123;phone-context=example.com", "tel:+12024561111;isub=123", "\u0660\u0660\u0664\u0664\u0662\u0660", "+44\uFF12\uFF10\uFF17\uFF19\uFF14\uFF16\uFF10\uFF19\uFF15\uFF18",
        "tel:2024561111;phone-context=+1", "tel:2024561111;phone-context=", "tel:2024561111;phone-context=1", "tel:+1-202-456-1111;ext=12",
        "202-456-1111 ext. 123", "202-456-1111,,123", "202-456-1111;123", "202.456.1111 x  9", "+1 (202) 456-1111 / x 12", "1 202 456 1111 \u0434\u043E\u0431. 5",
        "\u2066+1 202 456 1111\u2069", "  +1 202 456 1111  ", "\t(202)\u00A0456\u20111111", "0112024561111", "1-800-GOT-MILK", "011-44-20-7946-0958",
    ];

    private const string GarbagePool = "0123456789 +-().x#*/,;=:abcDEFXtel ext\uFF10\uFF11\u0663\u0967\u00A0\u2010\uFF0B\u200B\uD835\uDFD8\u017F\u0131\u0130\u212A\u00E9";

    internal static int Run(string path, int seed, string metadataPath)
    {
        Dictionary<string, TerritoryData> territories = [];
        using (FileStream xml = File.OpenRead(metadataPath))
        {
            foreach (TerritoryData t in PhoneMetadataXml.Read(xml))
            {
                territories[t.Id == "001" ? "001-" + t.CountryCode.ToString(CultureInfo.InvariantCulture) : t.Id] = t;
            }
        }

        PhoneNumberParser parser = PhoneNumberParser.Default;
        PhoneMetadata md = parser.Metadata;
        List<(Region Region, int Cc, string Nsn)> pool = [];
        foreach (Region r in md.Regions)
        {
            foreach (PhoneNumberType t in new[] { PhoneNumberType.Mobile, PhoneNumberType.FixedLine })
            {
                PhoneNumber? e = parser.GetExample(r, t);
                if (e is { } n)
                {
                    pool.Add((r, n.CountryCode, n.NationalSignificantNumber));
                    break;
                }
            }
        }

        using StreamWriter w = new(path, false, new UTF8Encoding(false));
        w.NewLine = "\n";
        int total = 0;
        foreach (Region region in md.Regions)
        {
            int cc = md.GetCallingCode(region);
            Random rng = new(HashCode(region.Code, seed));
            List<string> texts = RegionTexts(parser, md, region, cc, pool, rng);
            texts.InsertRange(texts.Count - Awkward.Length - 20, Sampled(territories[region.Code], rng));
            int i = 0;
            foreach (string text in texts)
            {
                w.WriteLine($"{region.Code}-{i++:D4}\tP\t{region.Code}\t{Escape.Encode(text)}");
                total++;
            }

            w.WriteLine($"{region.Code}-R\tR\t{region.Code}\t-");
            total++;
        }

        foreach (int cc in md.CallingCodes)
        {
            if (md.GetRegions(cc).Count > 0)
            {
                continue;
            }

            Random rng = new(HashCode("N" + cc.ToString(CultureInfo.InvariantCulture), seed));
            List<string> texts = NonGeoTexts(parser, cc, pool, rng);
            texts.InsertRange(texts.Count - Awkward.Length - 10, Sampled(territories["001-" + cc.ToString(CultureInfo.InvariantCulture)], rng));
            int i = 0;
            foreach (string text in texts)
            {
                w.WriteLine($"N{cc}-{i++:D4}\tP\t-\t{Escape.Encode(text)}");
                total++;
            }

            w.WriteLine($"N{cc}-N\tN\t-\t{cc}");
            total++;
        }

        return total;
    }

    // Numbers drawn from every type pattern and every format of the territory, nationally and with the calling code.
    private static List<string> Sampled(TerritoryData t, Random rng)
    {
        List<string> texts = [];
        string cc = t.CountryCode.ToString(CultureInfo.InvariantCulture);
        string np = (t.NationalPrefix ?? "").Replace("~", "", StringComparison.Ordinal);
        HashSet<int> generalLengths = [];
        foreach (DescData? d in t.Descs)
        {
            if (d is not null)
            {
                generalLengths.UnionWith(d.PossibleLengths.Where(l => l > 0));
            }
        }

        void Add(string nsn)
        {
            texts.Add(nsn);
            texts.Add("+" + cc + nsn);
            if (np.Length > 0)
            {
                texts.Add(np + nsn);
            }
        }

        for (int k = 0; k < t.Descs.Length; k++)
        {
            DescData? d = t.Descs[k];
            if (d?.Pattern is null || (DescKind)k == DescKind.NoInternationalDialling)
            {
                continue;
            }

            foreach (string s in PatternSampler.SampleByLength(d.Pattern, rng, new HashSet<int>(d.PossibleLengths), 3))
            {
                Add(s);
            }
        }

        foreach (FormatData f in t.Formats)
        {
            string? prefix = f.LeadingDigits.Length > 0 ? PatternSampler.Sample(f.LeadingDigits[^1], rng) : "";
            foreach (int length in generalLengths.Order().Take(3))
            {
                if (prefix is not null && prefix.Length <= length)
                {
                    StringBuilder sb = new(prefix);
                    while (sb.Length < length)
                    {
                        sb.Append((char)('0' + rng.Next(10)));
                    }

                    Add(sb.ToString());
                }
            }

            foreach (string s in PatternSampler.SampleByLength(f.Pattern, rng, generalLengths, 2))
            {
                Add(s);
            }
        }

        return texts;
    }

    private static int HashCode(string text, int seed)
    {
        int h = seed;
        foreach (char c in text)
        {
            h = (h * 31) + c;
        }

        return h;
    }

    private static List<string> RegionTexts(PhoneNumberParser parser, PhoneMetadata md, Region region, int cc, List<(Region Region, int Cc, string Nsn)> pool, Random rng)
    {
        List<string> texts = [];
        string ccs = cc.ToString(CultureInfo.InvariantCulture);
        string np = parser.GetNationalPrefix(region) ?? "";
        string? iddText = md.GetInternationalPrefix(region);
        string idd = LeadingDigits(iddText ?? "") is { Length: > 0 } d ? d : "00";

        List<string> examples = [];
        foreach (PhoneNumberType t in Enum.GetValues<PhoneNumberType>())
        {
            if (t == PhoneNumberType.Unknown)
            {
                continue;
            }

            if (parser.GetExample(region, t) is { } e && !examples.Contains(e.NationalSignificantNumber))
            {
                examples.Add(e.NationalSignificantNumber);
            }
        }

        if (examples.Count == 0)
        {
            examples.Add(RandomDigits(rng, 9));
        }

        // A spread of example types: the first, the last and some from between.
        List<string> chosen = [];
        int take = Math.Min(4, examples.Count);
        for (int k = 0; k < take; k++)
        {
            chosen.Add(examples[take == 1 ? 0 : k * (examples.Count - 1) / (take - 1)]);
        }

        foreach (string nsn in chosen.Distinct())
        {
            texts.Add(nsn);
            texts.Add(np + nsn);
            texts.Add(Group(nsn, 0));
            texts.Add(np + Group(nsn, 1));
            texts.Add("+" + ccs + nsn);
            texts.Add("+" + ccs + " " + Group(nsn, 2));
            texts.Add("+" + ccs + "-" + Group(nsn, 1));
            texts.Add(ccs + nsn);
            texts.Add(idd + ccs + nsn);
            texts.Add(idd + " " + ccs + " " + Group(nsn, 0));
            texts.Add("tel:+" + ccs + "-" + nsn);
            texts.Add("tel:" + nsn + ";phone-context=+" + ccs);
            texts.Add(nsn + " ext. 123");
            texts.Add("+" + ccs + nsn + ";ext=77");
            texts.Add(Group(nsn, 0) + "x45");
            texts.Add(Group(nsn, 0) + " #9");
            texts.Add(FullWidth(nsn));
            texts.Add("+" + ccs + ArabicIndic(nsn));
            texts.Add(Vanity(nsn, 4));
            texts.Add("Call +" + ccs + " " + Group(nsn, 2) + ".");
            texts.Add(np + nsn + "ext1234567890123456789012");
            for (int cut = 1; cut <= 3; cut++)
            {
                texts.Add(nsn[..Math.Max(1, nsn.Length - cut)]);
                texts.Add("+" + ccs + nsn[..Math.Max(1, nsn.Length - cut)]);
            }

            for (int more = 1; more <= 2; more++)
            {
                texts.Add(nsn + RandomDigits(rng, more));
                texts.Add("+" + ccs + nsn + RandomDigits(rng, more));
            }

            for (int change = 1; change <= 3; change++)
            {
                string changed = ChangeTail(nsn, change, rng);
                texts.Add(changed);
                texts.Add("+" + ccs + changed);
            }

            texts.Add("0" + nsn);
            texts.Add("+" + ccs + "0" + nsn);
            texts.Add(np + "12" + nsn);
            texts.Add(np + "0" + nsn);
        }

        for (int k = 0; k < 8; k++)
        {
            (Region other, int occ, string onsn) = pool[rng.Next(pool.Count)];
            texts.Add("+" + occ.ToString(CultureInfo.InvariantCulture) + onsn);
            texts.Add(onsn);
            texts.Add(idd + occ.ToString(CultureInfo.InvariantCulture) + " " + Group(onsn, 0) + (other == region ? "" : " x1"));
        }

        texts.AddRange(Awkward);
        for (int k = 0; k < 20; k++)
        {
            texts.Add(Garbage(rng));
        }

        return texts;
    }

    private static List<string> NonGeoTexts(PhoneNumberParser parser, int cc, List<(Region Region, int Cc, string Nsn)> pool, Random rng)
    {
        List<string> texts = [];
        string ccs = cc.ToString(CultureInfo.InvariantCulture);
        string example = parser.GetNonGeographicExample(cc)?.NationalSignificantNumber ?? RandomDigits(rng, 8);
        texts.Add("+" + ccs + example);
        texts.Add("+" + ccs + " " + Group(example, 0));
        texts.Add("+" + ccs + "-" + Group(example, 1));
        texts.Add("tel:+" + ccs + "-" + example);
        texts.Add("+" + ccs + example + ";ext=5");
        texts.Add("+" + ccs + example + " x5");
        texts.Add("+" + ccs + FullWidth(example));
        texts.Add("+" + ccs + "0" + example);
        texts.Add("+" + ccs + example + "9");
        for (int cut = 1; cut <= 4; cut++)
        {
            texts.Add("+" + ccs + example[..Math.Max(1, example.Length - cut)]);
        }

        for (int change = 1; change <= 6; change++)
        {
            texts.Add("+" + ccs + ChangeTail(example, change, rng));
        }

        for (int k = 0; k < 12; k++)
        {
            (_, int occ, string onsn) = pool[rng.Next(pool.Count)];
            texts.Add("+" + occ.ToString(CultureInfo.InvariantCulture) + onsn);
        }

        texts.AddRange(Awkward);
        for (int k = 0; k < 10; k++)
        {
            texts.Add(Garbage(rng));
        }

        return texts;
    }

    private static string LeadingDigits(string text)
    {
        int i = 0;
        while (i < text.Length && text[i] is >= '0' and <= '9')
        {
            i++;
        }

        return text[..i];
    }

    private static string RandomDigits(Random rng, int count)
    {
        StringBuilder sb = new(count);
        for (int i = 0; i < count; i++)
        {
            sb.Append((char)('0' + rng.Next(10)));
        }

        return sb.ToString();
    }

    private static string ChangeTail(string nsn, int count, Random rng)
    {
        char[] c = nsn.ToCharArray();
        for (int i = Math.Max(1, c.Length - count); i < c.Length; i++)
        {
            c[i] = (char)('0' + rng.Next(10));
        }

        return new string(c);
    }

    // Plausible groupings; the variant picks a punctuation style.
    private static string Group(string nsn, int variant)
    {
        if (nsn.Length < 6)
        {
            return nsn;
        }

        int a = nsn.Length > 8 ? 3 : 2;
        int b = Math.Max(2, (nsn.Length - a) / 2);
        string p1 = nsn[..a];
        string p2 = nsn.Substring(a, b);
        string p3 = nsn[(a + b)..];
        return variant switch
        {
            0 => $"({p1}) {p2}-{p3}",
            1 => $"{p1}-{p2}-{p3}",
            _ => $"{p1} {p2} {p3}",
        };
    }

    private static string FullWidth(string digits) =>
        string.Concat(digits.Select(c => c is >= '0' and <= '9' ? (char)('\uFF10' + (c - '0')) : c));

    private static string ArabicIndic(string digits) =>
        string.Concat(digits.Select(c => c is >= '0' and <= '9' ? (char)('\u0660' + (c - '0')) : c));

    private static string Vanity(string nsn, int letters)
    {
        const string Keys = "01ADGJMPTW";
        char[] c = nsn.ToCharArray();
        for (int i = Math.Max(0, c.Length - letters); i < c.Length; i++)
        {
            c[i] = Keys[c[i] - '0'];
        }

        return new string(c);
    }

    private static string Garbage(Random rng)
    {
        int len = rng.Next(3, 26);
        StringBuilder sb = new(len);
        for (int i = 0; i < len; i++)
        {
            char c = GarbagePool[rng.Next(GarbagePool.Length)];
            if (c == '\uD835')
            {
                sb.Append("\uD835\uDFD8");
                continue;
            }

            if (c == '\uDFD8')
            {
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }
}
