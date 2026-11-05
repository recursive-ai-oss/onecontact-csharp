using System.Globalization;
using System.Text;
using RecursiveAI.OneContact.Phones;

namespace RecursiveAI.OneContact.Oracles.PhonesGeo;

/// <summary>
/// Generates parity cases from the data files themselves: for every calling code, numbers that start with prefixes the geocoding,
/// carrier and time-zone data name (sampled when a code has very many), completed to numbers the plan accepts, asked in every language
/// the data has for the code, in English, French and an unknown language, and in a rotating pick of awkward language tags; for every
/// region, its example numbers asked in every ISO 639-1 language; plus invalid numbers. Deterministic in the seed.
/// </summary>
internal static class Generator
{
    // Tags that exercise parsing and the script/region rules: Chinese variants, regional Portuguese, Spanish, German, French, Serbian,
    // legacy codes, case, underscores (not a language tag), extensions, an empty tag and one that is not a language at all.
    private static readonly string[] Variants =
    [
        "zh-TW", "zh-HK", "zh-MO", "zh-CN", "zh-SG", "zh-Hant", "zh-Hans", "zh-Hant-TW", "zh-Hans-TW", "zh-Hant-CN", "zh-Hans-CN", "zh-Hant-HK",
        "pt-BR", "pt-PT", "en-GB", "en-US", "en-001", "de-AT", "de-CH", "es-MX", "es-419", "es-ES", "fr-CA", "fr-CH", "sr-Latn", "sr-Cyrl", "sr-Latn-RS",
        "he-IL", "iw-IL", "in-ID", "nb-NO", "no-NO", "nn-NO", "ru-UA", "uk-UA", "ar-EG", "ar-SA", "ko-KR", "ja-JP", "it-CH", "nl-BE", "sv-FI",
        "EN", "ZH-tw", "Zh-hant-hk", "zh_TW", "en_US", "en-US-u-ca-gregory", "und", "", "x", "english", "xx", "qq-ZZ",
    ];

    internal static int Run(string path, int seed, int perCode, string upstream)
    {
        PhoneNumberParser parser = PhoneNumberParser.Default;
        PhoneMetadata metadata = parser.Metadata;
        string[] isoLanguages = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "languages.txt")).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Dictionary<int, SortedSet<string>> prefixes = [];
        Dictionary<int, SortedSet<string>> languages = [];
        foreach (int code in metadata.CallingCodes)
        {
            prefixes[code] = [string.Empty];
            languages[code] = [];
        }

        foreach (string folder in new[] { "geocoding", "carrier" })
        {
            string root = Path.Combine(upstream, "libphonenumber-" + folder, folder);
            foreach (string file in Directory.EnumerateFiles(root, "*.txt", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                string code = Path.GetFileNameWithoutExtension(file);
                string language = Path.GetFileName(Path.GetDirectoryName(file))!;
                int cc = int.Parse(code, CultureInfo.InvariantCulture);
                if (!prefixes.TryGetValue(cc, out SortedSet<string>? set))
                {
                    continue;
                }

                // zh_Hant is asked for by naming a traditional-script region.
                languages[cc].Add(language == "zh_Hant" ? "zh-TW" : language);
                foreach (string raw in File.ReadLines(file))
                {
                    string l = raw.TrimStart();
                    int bar = l.IndexOf('|', StringComparison.Ordinal);
                    if (l.Length > 0 && l[0] != '#' && bar > code.Length && l.StartsWith(code, StringComparison.Ordinal))
                    {
                        set.Add(l[code.Length..bar]);
                    }
                }
            }
        }

        foreach (string raw in File.ReadLines(Path.Combine(upstream, "libphonenumber-timezones", "timezones", "map_data.txt")))
        {
            int bar = raw.IndexOf('|', StringComparison.Ordinal);
            if (raw.Length == 0 || raw[0] == '#' || bar <= 0)
            {
                continue;
            }

            foreach (int cc in prefixes.Keys)
            {
                string code = cc.ToString(CultureInfo.InvariantCulture);
                if (raw.StartsWith(code, StringComparison.Ordinal) && bar >= code.Length)
                {
                    prefixes[cc].Add(raw[code.Length..bar]);
                }
            }
        }

        using StreamWriter w = new(path, false, new UTF8Encoding(false));
        w.NewLine = "\n";
        int id = 0;
        void Emit(int cc, string nsn, string tag, string user)
        {
            w.Write(id++);
            w.Write('\t');
            w.Write(cc);
            w.Write('\t');
            w.Write(nsn);
            w.Write('\t');
            w.Write(Escape.Write(tag));
            w.Write('\t');
            w.Write(user);
            w.WriteLine();
        }

        string[] users = ["own", "US", "DE", "ZZ", "own", "GB", "CN", "own"];
        int variantCursor = 0;
        int userCursor = 0;
        foreach (int cc in metadata.CallingCodes)
        {
            Random rng = new(unchecked((cc * 7919) + seed));
            List<string> chosen = Sample([.. prefixes[cc]], perCode, rng);
            List<string> tags = [.. languages[cc], "en", "fr", "xx"];
            foreach (string prefix in chosen)
            {
                List<string> numbers = ValidNumbers(parser, cc, prefix, rng, 2);
                if (numbers.Count == 0 || rng.Next(5) == 0)
                {
                    numbers.Add(RandomNumber(cc, prefix, rng, rng.Next(Math.Max(prefix.Length, 3), 13)));
                }

                foreach (string nsn in numbers)
                {
                    PhoneNumber n = PhoneNumber.Create(cc, nsn);
                    string own = n.Region.IsUnknown ? "-" : n.Region.Code;
                    foreach (string tag in tags.Concat([Variants[variantCursor++ % Variants.Length], Variants[variantCursor++ % Variants.Length]]))
                    {
                        string user = users[userCursor++ % users.Length];
                        Emit(cc, nsn, tag, user == "own" ? own : user);
                    }
                }
            }
        }

        // Every region's example numbers (and a mobile and a fixed line where the plan has them) in every language and variant.
        foreach (Region region in metadata.Regions)
        {
            HashSet<string> done = [];
            foreach (PhoneNumberType type in new[] { PhoneNumberType.FixedLine, PhoneNumberType.Mobile, PhoneNumberType.TollFree, PhoneNumberType.Pager })
            {
                if (parser.GetExample(region, type) is not { } example || !done.Add(example.NationalSignificantNumber))
                {
                    continue;
                }

                foreach (string tag in isoLanguages.Concat(Variants))
                {
                    string user = users[userCursor++ % users.Length];
                    Emit(example.CountryCode, example.NationalSignificantNumber, tag, user == "own" ? region.Code : user);
                }
            }
        }

        // Country names in every locale CLDR has names for: a mobile and a toll-free number of every region (their description is the
        // country name where the calling code names one).
        string[] locales = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "locales.txt")).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (Region region in metadata.Regions)
        {
            HashSet<string> done = [];
            foreach (PhoneNumberType type in new[] { PhoneNumberType.Mobile, PhoneNumberType.TollFree })
            {
                if (parser.GetExample(region, type) is not { } example || !done.Add(example.NationalSignificantNumber))
                {
                    continue;
                }

                foreach (string tag in locales)
                {
                    Emit(example.CountryCode, example.NationalSignificantNumber, tag, "-");
                }
            }
        }

        // Non-geographic calling codes.
        foreach (int cc in metadata.CallingCodes.Where(c => metadata.GetRegions(c).Count == 0))
        {
            if (parser.GetNonGeographicExample(cc) is { } example)
            {
                foreach (string tag in new[] { "en", "de", "zh", "xx" })
                {
                    Emit(cc, example.NationalSignificantNumber, tag, "US");
                }
            }
        }

        return id;
    }

    private static List<string> Sample(List<string> all, int count, Random rng)
    {
        if (all.Count <= count)
        {
            return all;
        }

        // Keep the shortest keys (they decide the fallbacks), sample the rest.
        List<string> byLength = [.. all.OrderBy(p => p.Length).ThenBy(p => p, StringComparer.Ordinal)];
        List<string> chosen = [.. byLength.Take(count / 4)];
        List<string> rest = [.. byLength.Skip(count / 4)];
        while (chosen.Count < count && rest.Count > 0)
        {
            int i = rng.Next(rest.Count);
            chosen.Add(rest[i]);
            rest[i] = rest[^1];
            rest.RemoveAt(rest.Count - 1);
        }

        return chosen;
    }

    private static List<string> ValidNumbers(PhoneNumberParser parser, int cc, string prefix, Random rng, int want)
    {
        List<string> found = [];
        HashSet<int> lengths = [];
        for (int attempt = 0; attempt < 80 && found.Count < want; attempt++)
        {
            int length = rng.Next(Math.Max(prefix.Length, 4), 14);
            string nsn = RandomNumber(cc, prefix, rng, length);
            if (nsn.Length >= 2 && lengths.Add(nsn.Length) && parser.IsValid(PhoneNumber.Create(cc, nsn)))
            {
                found.Add(nsn);
            }
        }

        return found;
    }

    private static string RandomNumber(int cc, string prefix, Random rng, int length)
    {
        StringBuilder sb = new(prefix);
        while (sb.Length < length)
        {
            sb.Append((char)('0' + rng.Next(10)));
        }

        // A national number is at most 17 digits; the calling code and number together at most 15.
        string nsn = sb.ToString();
        int max = Math.Min(17, 15 - cc.ToString(CultureInfo.InvariantCulture).Length);
        if (nsn.Length > max)
        {
            nsn = nsn[..max];
        }

        return nsn.Length < 2 ? nsn.PadRight(2, '0') : nsn;
    }
}
