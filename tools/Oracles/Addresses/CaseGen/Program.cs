// Generates the validation and layout cases for the structural parity test.
//
//   dotnet run -- <countryinfo.txt> <oracle formats.tsv> <output cases.tsv>
//
// The cases come from the pinned address data (read here with a small reader of its own, not through the library under test)
// and from the format table the reference implementation itself reports. For every region: a valid address built from the data's
// own examples, every subdivision (by key, by name and by Latin name, in upper and lower case), every example postal code,
// subdivision/postal-code mismatches, each field dropped in turn, unexpected fields, near-miss and malformed postal codes,
// the deeper levels (locality, dependent locality) where the data has them, the language variants the data has, Latin-script
// layouts, and every subset of the fields of each format. The output is deterministic.
//
// Case line (tab separated, "\N" for an absent value, street lines joined with U+0001):
//   id, country, language, admin area, locality, dependent locality, postal code, sorting code, organization, recipient, lines
using System.Text;
using System.Text.Json;

internal static class Program
{
    private const string Absent = "\\N";

    private static readonly Dictionary<string, JsonElement> Data = new(StringComparer.Ordinal);
    private static readonly List<string> Output = [];

    private static int Main(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("usage: CaseGen <countryinfo.txt> <oracle formats.tsv> <output cases.tsv>");
            return 64;
        }

        foreach (string line in File.ReadAllLines(args[0], Encoding.UTF8))
        {
            int eq = line.IndexOf('=', StringComparison.Ordinal);
            if (eq < 0)
            {
                continue;
            }

            string key = line[..eq];
            if (key == "data" || key.StartsWith("data/", StringComparison.Ordinal))
            {
                Data[key] = JsonDocument.Parse(line[(eq + 1)..]).RootElement.Clone();
            }
        }

        string[] countries = Prop("data", "countries").Split('~');
        foreach (string cc in countries)
        {
            Validation(cc, null);
            foreach (string key in Data.Keys.Where(k => k.StartsWith($"data/{cc}--", StringComparison.Ordinal)).OrderBy(k => k, StringComparer.Ordinal))
            {
                Validation(cc, key[(key.IndexOf("--", StringComparison.Ordinal) + 2)..]);
            }
        }

        foreach (string cc in countries)
        {
            Extended(cc);
        }

        NonCountries();
        Layouts(args[1]);
        File.WriteAllLines(args[2], Output, new UTF8Encoding(false));
        Console.WriteLine($"{Output.Count} cases");
        return 0;
    }

    private sealed class Case(string id)
    {
        public string Id = id;
        public string Country = Absent;
        public string Language = Absent;
        public string Admin = Absent;
        public string Locality = Absent;
        public string Dependent = Absent;
        public string Postal = Absent;
        public string Sorting = Absent;
        public string Org = Absent;
        public string Recipient = Absent;
        public string Lines = Absent;

        public Case Clone(string id)
        {
            Case copy = (Case)MemberwiseClone();
            copy.Id = id;
            return copy;
        }

        public string ToLine() => string.Join('\t', Id, Country, Language, Admin, Locality, Dependent, Postal, Sorting, Org, Recipient, Lines);
    }

    private static string? Raw(string key, string property) =>
        Data.TryGetValue(key, out JsonElement e) && e.TryGetProperty(property, out JsonElement v) ? v.GetString() : null;

    private static string Prop(string key, string property) => Raw(key, property) ?? string.Empty;

    private static string[] List(string key, string property) =>
        Raw(key, property) is { Length: > 0 } text ? text.Split('~') : [];

    private static string Lines(params string[] lines) => string.Join('\u0001', lines);

    private static void Add(Case c) => Output.Add(c.ToLine());

    private static string First(string? examples) => string.IsNullOrEmpty(examples) ? string.Empty : examples.Split(',')[0].Split(':')[0];

    private static string NodeKey(string cc, string? language, params string[] path)
    {
        string key = "data/" + cc;
        foreach (string p in path)
        {
            key += "/" + p;
        }

        return language is null ? key : key + "--" + language;
    }

    private static void Validation(string cc, string? language)
    {
        string countryKey = NodeKey(cc, language);
        string tag = language is null ? "-" : language;
        string fmt = Raw(countryKey, "fmt") ?? Prop("data/ZZ", "fmt");
        string zipPattern = Prop(countryKey, "zip");
        string[] zipex = Prop(countryKey, "zipex").Split(',', StringSplitOptions.RemoveEmptyEntries);
        string[] subKeys = List(countryKey, "sub_keys");
        string[] subNames = List(countryKey, "sub_names");
        string[] subLnames = List(countryKey, "sub_lnames");
        string[] subZipex = List(countryKey, "sub_zipexs");
        bool hasS = fmt.Contains("%S", StringComparison.Ordinal);
        string id(string what) => $"{cc}/{tag}/{what}";

        Case Base() => new Case(string.Empty)
        {
            Country = cc,
            Language = language ?? Absent,
            Locality = fmt.Contains("%C", StringComparison.Ordinal) ? "Testville" : Absent,
            Postal = zipex.Length > 0 ? zipex[0] : Absent,
            Lines = Lines("1 Main Street"),
            Org = "Acme",
            Recipient = "Pat Jones",
        };

        Case valid = Base();
        valid.Id = id("valid");
        Add(valid);
        Case withS = valid;
        if (hasS && subKeys.Length > 0)
        {
            withS = valid.Clone(id("valid-sub0"));
            withS.Admin = subKeys[0];
            withS.Postal = subZipex.Length > 0 && First(subZipex[0]).Length > 0 ? First(subZipex[0]) : valid.Postal;
            Add(withS);
        }

        // Every example postal code of the country, with and without a subdivision.
        for (int i = 0; i < zipex.Length; i++)
        {
            Case c = valid.Clone(id($"zipex{i}"));
            c.Postal = zipex[i];
            Add(c);
        }

        // Every subdivision: by key, by name, by Latin name, with its own example postal code; upper and lower case; padded.
        for (int i = 0; i < subKeys.Length; i++)
        {
            string subZip = i < subZipex.Length ? First(subZipex[i]) : string.Empty;
            string[] names = [subKeys[i], i < subNames.Length ? subNames[i] : string.Empty, i < subLnames.Length ? subLnames[i] : string.Empty];
            for (int n = 0; n < names.Length; n++)
            {
                if (names[n].Length == 0 || (n > 0 && names[n] == subKeys[i]))
                {
                    continue;
                }

                Case c = valid.Clone(id($"sub{i}/name{n}"));
                c.Admin = names[n];
                c.Postal = subZip.Length > 0 ? subZip : valid.Postal;
                Add(c);
                {
                    Case lower = c.Clone(id($"sub{i}/name{n}/lower"));
                    lower.Admin = names[n].ToLowerInvariant();
                    Case upper = c.Clone(id($"sub{i}/name{n}/upper"));
                    upper.Admin = names[n].ToUpperInvariant();
                    Case padded = c.Clone(id($"sub{i}/name{n}/padded"));
                    padded.Admin = "  " + names[n] + " ";
                    if (lower.Admin != c.Admin)
                    {
                        Add(lower);
                    }

                    if (upper.Admin != c.Admin)
                    {
                        Add(upper);
                    }

                    Add(padded);
                }
            }

            // A postal code that belongs to a different subdivision.
            if (subKeys.Length > 1)
            {
                int other = (i + 1) % subKeys.Length;
                string otherZip = other < subZipex.Length ? First(subZipex[other]) : string.Empty;
                if (otherZip.Length > 0 && otherZip != subZip)
                {
                    Case c = valid.Clone(id($"sub{i}/mismatch"));
                    c.Admin = subKeys[i];
                    c.Postal = otherZip;
                    Add(c);
                }
            }

            // The deeper levels where the data has them.
            Levels(cc, language, tag, valid, subKeys[i], subZip, i);
        }

        if (hasS)
        {
            foreach (string bad in new[] { "Nowhere", "X", "12345" })
            {
                Case c = valid.Clone(id("admin-unknown/" + bad));
                c.Admin = bad;
                Add(c);
            }

            if (subKeys.Length > 0 && subKeys[0].Length > 2)
            {
                Case c = valid.Clone(id("admin-prefix"));
                c.Admin = subKeys[0][..^1];
                Add(c);
            }
        }

        // The fully filled address, then each field dropped in turn.
        Case full = withS.Clone(id("full"));
        full.Locality = "Testville";
        full.Dependent = "Northside";
        full.Sorting = "99";
        if (full.Admin == Absent)
        {
            full.Admin = "Somewhere";
        }

        if (full.Postal == Absent)
        {
            full.Postal = "12345";
        }

        Add(full);
        foreach (string drop in new[] { "admin", "locality", "dependent", "postal", "sorting", "org", "recipient", "lines" })
        {
            Case c = full.Clone(id("drop-" + drop));
            switch (drop)
            {
                case "admin": c.Admin = Absent; break;
                case "locality": c.Locality = Absent; break;
                case "dependent": c.Dependent = Absent; break;
                case "postal": c.Postal = Absent; break;
                case "sorting": c.Sorting = Absent; break;
                case "org": c.Org = Absent; break;
                case "recipient": c.Recipient = Absent; break;
                default: c.Lines = Absent; break;
            }

            Add(c);
            Case blank = full.Clone(id("blank-" + drop));
            switch (drop)
            {
                case "admin": blank.Admin = "  "; break;
                case "locality": blank.Locality = string.Empty; break;
                case "dependent": blank.Dependent = " "; break;
                case "postal": blank.Postal = "   "; break;
                case "sorting": blank.Sorting = string.Empty; break;
                case "org": blank.Org = " "; break;
                case "recipient": blank.Recipient = string.Empty; break;
                default: blank.Lines = Lines(string.Empty, "  "); break;
            }

            Add(blank);
        }

        // A single field only, to see what each country requires on its own.
        Case empty = new(id("empty")) { Country = cc, Language = language ?? Absent };
        Add(empty);

        // Malformed and near-miss postal codes.
        if (zipex.Length > 0)
        {
            string z = zipex[0];
            foreach ((string label, string value) in new[]
            {
                ("short", z.Length > 1 ? z[..^1] : string.Empty),
                ("long", z + "1"),
                ("long-letter", z + "A"),
                ("letters", new string('A', Math.Max(1, z.Length))),
                ("digits", new string('1', Math.Max(1, z.Length))),
                ("lower", z.ToLowerInvariant()),
                ("padded", "  " + z + "  "),
                ("junk", "!!!"),
                ("huge", new string('9', 40)),
                ("zero", "0"),
                ("tilde", z + "~" + z),
                ("space-in", z.Length > 2 ? z.Insert(2, " ") : z),
                ("dash-in", z.Length > 2 ? z.Insert(2, "-") : z),
            })
            {
                if (value.Length == 0)
                {
                    continue;
                }

                Case c = withS.Clone(id("zip-" + label));
                c.Postal = value;
                Add(c);
            }
        }

        // Latin-script layout of the country's own address.
        Case latin = full.Clone(id("latin"));
        latin.Language = "xx-Latn";
        Add(latin);
        if (language is null)
        {
            string lang = Prop(countryKey, "lang");
            if (lang.Length > 0)
            {
                foreach (string tagVariant in new[] { lang, lang.ToUpperInvariant(), lang + "-Latn", lang + "-latn", lang + "_Latn" })
                {
                    Case c = valid.Clone(id("lang/" + tagVariant));
                    c.Language = tagVariant;
                    Add(c);
                }
            }
        }
    }

    private static void Levels(string cc, string? language, string tag, Case valid, string adminKey, string adminZip, int adminIndex)
    {
        // Locality and dependent locality lists below this subdivision.
        string adminNode = NodeKey(cc, language, adminKey);
        string[] localities = List(adminNode, "sub_keys");
        if (localities.Length == 0 || adminIndex > 40)
        {
            return;
        }

        string[] localityNames = List(adminNode, "sub_names");
        string[] localityLnames = List(adminNode, "sub_lnames");
        int limit = Math.Min(localities.Length, 6);
        for (int j = 0; j < limit; j++)
        {
            Case c = valid.Clone($"{cc}/{tag}/sub{adminIndex}/loc{j}");
            c.Admin = adminKey;
            c.Postal = adminZip.Length > 0 ? adminZip : valid.Postal;
            c.Locality = localities[j];
            Add(c);
            if (j < localityNames.Length && localityNames[j] != localities[j])
            {
                Case n = c.Clone($"{cc}/{tag}/sub{adminIndex}/loc{j}/name");
                n.Locality = localityNames[j];
                Add(n);
            }

            if (j < localityLnames.Length && localityLnames[j] != localities[j])
            {
                Case n = c.Clone($"{cc}/{tag}/sub{adminIndex}/loc{j}/lname");
                n.Locality = localityLnames[j];
                Add(n);
            }

            Case lower = c.Clone($"{cc}/{tag}/sub{adminIndex}/loc{j}/lower");
            lower.Locality = localities[j].ToLowerInvariant();
            if (lower.Locality != c.Locality)
            {
                Add(lower);
            }

            string localityNode = NodeKey(cc, language, adminKey, localities[j]);
            string[] deps = List(localityNode, "sub_keys");
            for (int k = 0; k < Math.Min(deps.Length, 4); k++)
            {
                Case d = c.Clone($"{cc}/{tag}/sub{adminIndex}/loc{j}/dep{k}");
                d.Dependent = deps[k];
                Add(d);
                Case dl = d.Clone($"{cc}/{tag}/sub{adminIndex}/loc{j}/dep{k}/lower");
                dl.Dependent = deps[k].ToLowerInvariant();
                if (dl.Dependent != d.Dependent)
                {
                    Add(dl);
                }
            }

            if (deps.Length > 0)
            {
                Case bad = c.Clone($"{cc}/{tag}/sub{adminIndex}/loc{j}/dep-unknown");
                bad.Dependent = "Nowhere Quarter";
                Add(bad);
            }
        }

        Case wrong = valid.Clone($"{cc}/{tag}/sub{adminIndex}/loc-unknown");
        wrong.Admin = adminKey;
        wrong.Locality = "Nowhere City";
        Add(wrong);
    }

    // Edge cases around the structure of the data itself: extended postal code patterns, prefix patterns at different depths,
    // language variants crossed with the other language's names, skipped levels and unusual white space.
    private static void Extended(string cc)
    {
        string countryKey = NodeKey(cc, null);
        string[] zipex = Prop(countryKey, "zipex").Split(',', StringSplitOptions.RemoveEmptyEntries);
        string[] subKeys = List(countryKey, "sub_keys");
        string[] subZipex = List(countryKey, "sub_zipexs");
        string[] variants = [.. Data.Keys.Where(k => k.StartsWith($"data/{cc}--", StringComparison.Ordinal)).Select(k => k[(k.IndexOf("--", StringComparison.Ordinal) + 2)..])];
        Case Seed(string what)
        {
            Case c = new($"{cc}/ext/{what}") { Country = cc, Locality = "Testville", Lines = Lines("1 Main Street") };
            c.Postal = zipex.Length > 0 ? zipex[0] : Absent;
            return c;
        }

        for (int i = 0; i < subKeys.Length && i < 60; i++)
        {
            string subNode = NodeKey(cc, null, subKeys[i]);
            string[] ownZip = Prop(subNode, "xzip").Length > 0 ? ["800", "80001", "999077", "999078", "12345", "123456", ""] : [];
            foreach (string zip in ownZip)
            {
                Case c = Seed($"xzip{i}/{zip}");
                c.Admin = subKeys[i];
                c.Postal = zip.Length == 0 ? Absent : zip;
                Add(c);
            }

            // A postal code of another subdivision, asked at each depth below this one.
            string otherZip = subZipex.Length > 1 ? First(subZipex[(i + 1) % subZipex.Length]) : string.Empty;
            string[] localities = List(subNode, "sub_keys");
            for (int j = 0; j < Math.Min(localities.Length, 3); j++)
            {
                string[] deps = List(NodeKey(cc, null, subKeys[i], localities[j]), "sub_keys");
                Case c = Seed($"deepzip{i}/{j}");
                c.Admin = subKeys[i];
                c.Locality = localities[j];
                if (otherZip.Length > 0)
                {
                    c.Postal = otherZip;
                    Add(c);
                }

                if (deps.Length > 0)
                {
                    Case d = c.Clone($"{cc}/ext/deepzip{i}/{j}/dep");
                    d.Dependent = deps[0];
                    Add(d);
                    Case ownLocalityZip = c.Clone($"{cc}/ext/deepzip{i}/{j}/dep-ownzip");
                    ownLocalityZip.Dependent = deps[0];
                    ownLocalityZip.Postal = zipex.Length > 0 ? zipex[0] : Absent;
                    Add(ownLocalityZip);
                }
            }

            // The level skipped: no administrative area, a locality that belongs to one.
            if (localities.Length > 0 && i < 4)
            {
                Case skip = Seed($"skip-admin{i}");
                skip.Locality = localities[0];
                Add(skip);
                Case padded = Seed($"skip-admin-blank{i}");
                padded.Admin = "  ";
                padded.Locality = localities[0];
                Add(padded);
            }
        }

        // Other languages: names of one language under the tag of the other.
        foreach (string variant in variants)
        {
            string variantNode = NodeKey(cc, variant);
            string[] variantKeys = List(variantNode, "sub_keys");
            for (int i = 0; i < Math.Min(variantKeys.Length, 4); i++)
            {
                Case a = Seed($"variant-keys-under-default{variant}/{i}");
                a.Admin = variantKeys[i];
                Add(a);
            }

            for (int i = 0; i < Math.Min(subKeys.Length, 4); i++)
            {
                Case b = Seed($"default-keys-under-{variant}/{i}");
                b.Language = variant;
                b.Admin = subKeys[i];
                Add(b);
                string[] localities = List(NodeKey(cc, null, subKeys[i]), "sub_keys");
                if (localities.Length > 0)
                {
                    Case l = b.Clone($"{cc}/ext/default-locality-under-{variant}/{i}");
                    l.Locality = localities[0];
                    Add(l);
                }
            }

            foreach (string tag in new[] { variant.ToUpperInvariant(), variant + "-XX", variant + "_XX", " " + variant + " " })
            {
                Case t = Seed($"tag/{tag}");
                t.Language = tag;
                if (subKeys.Length > 0)
                {
                    t.Admin = subKeys[0];
                }

                Add(t);
            }
        }

        // White space the platform's trim and the reference's might treat differently.
        foreach ((string label, string space) in new[] { ("nbsp", "\u00a0"), ("ideographic", "\u3000"), ("emspace", "\u2003"), ("zwsp", "\u200b"), ("ctrl", "\u0001"), ("vt", "\u000b"), ("nel", "\u0085") })
        {
            Case c = Seed($"space-{label}");
            c.Admin = space;
            c.Org = space;
            c.Recipient = space + "x" + space;
            c.Dependent = space;
            c.Sorting = space;
            c.Lines = Lines(space);
            Add(c);
            Case z = Seed($"space-zip-{label}");
            z.Postal = space + (zipex.Length > 0 ? zipex[0] : "1") + space;
            if (subKeys.Length > 0)
            {
                z.Admin = space + subKeys[0] + space;
            }

            Add(z);
        }
    }

    private static void NonCountries()
    {
        foreach (string cc in new[] { "ZZ", "XX", "QQ" })
        {
            Add(new Case($"{cc}/valid") { Country = cc, Locality = "Testville", Postal = "12345", Lines = Lines("1 Main"), Admin = "CA" });
            Add(new Case($"{cc}/lines") { Country = cc, Lines = Lines("1 Main") });
            Add(new Case($"{cc}/empty") { Country = cc });
            Add(new Case($"{cc}/latin") { Country = cc, Language = "xx-Latn", Locality = "Testville", Lines = Lines("1 Main") });
        }

        Add(new Case("none/empty"));
        Add(new Case("none/fields") { Locality = "Testville", Postal = "12345", Lines = Lines("1 Main") });
    }

    private static void Layouts(string formatsPath)
    {
        // Every subset of the fields each format places, for the format as the reference reports it. The values are single
        // letters so the layout is easy to read in a mismatch; the street lines also come in twos and with blanks.
        Dictionary<string, JsonElement> formats = new(StringComparer.Ordinal);
        foreach (string line in File.ReadAllLines(formatsPath, Encoding.UTF8))
        {
            int tab = line.IndexOf('\t', StringComparison.Ordinal);
            formats[line[..tab]] = JsonDocument.Parse(line[(tab + 1)..]).RootElement.Clone();
        }

        foreach ((string cc, JsonElement format) in formats.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            foreach ((string property, string language) in new[] { ("fmt", Absent), ("lfmt", "xx-Latn") })
            {
                string? text = format.TryGetProperty(property, out JsonElement e) ? e.GetString() : null;
                if (text is null && property == "lfmt")
                {
                    continue;
                }

                text ??= formats["ZZ"].GetProperty("fmt").GetString()!;
                if (text.Contains("%T", StringComparison.Ordinal) || text.Contains("%F", StringComparison.Ordinal) || text.Contains("%L", StringComparison.Ordinal))
                {
                    continue;
                }

                List<char> present = [];
                foreach (char letter in "NOADCSZX")
                {
                    if (text.Contains("%" + letter, StringComparison.Ordinal))
                    {
                        present.Add(letter);
                    }
                }

                for (int mask = 0; mask < 1 << present.Count; mask++)
                {
                    HashSet<char> on = [.. present.Where((_, i) => (mask >> i & 1) == 1)];
                    string name = new([.. present.Where(on.Contains)]);
                    Case c = new($"layout/{cc}/{property}/{name}")
                    {
                        Country = cc,
                        Language = language,
                        Admin = on.Contains('S') ? "s" : Absent,
                        Locality = on.Contains('C') ? "c" : Absent,
                        Dependent = on.Contains('D') ? "d" : Absent,
                        Postal = on.Contains('Z') ? "z" : Absent,
                        Sorting = on.Contains('X') ? "x" : Absent,
                        Org = on.Contains('O') ? "o" : Absent,
                        Recipient = on.Contains('N') ? "n" : Absent,
                        Lines = on.Contains('A') ? Lines("a") : Absent,
                    };
                    Add(c);
                }

                if (present.Contains('A'))
                {
                    foreach ((string label, string lines) in new[]
                    {
                        ("two", Lines("a1", "a2")),
                        ("three-blank", Lines("a1", string.Empty, "  ", "a3")),
                        ("padded", Lines("  a1 ", " a2")),
                    })
                    {
                        Add(new Case($"layout/{cc}/{property}/lines-{label}")
                        {
                            Country = cc,
                            Language = language,
                            Admin = "s",
                            Locality = " c ",
                            Dependent = "d",
                            Postal = "z",
                            Sorting = "x",
                            Org = " o",
                            Recipient = "n ",
                            Lines = lines,
                        });
                    }
                }
            }
        }
    }
}
