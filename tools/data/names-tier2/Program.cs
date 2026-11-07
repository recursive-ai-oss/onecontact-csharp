using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OneContact.Data.Names;

// Tier 2 name and company examples for the shared model's name heads.
//
//   render --lists lists.tsv --gleif GOLDEN.csv.zip --elf ELF.csv --out DIR [--seed N] [--persons N] [--multiple N] [--companies N]
//
// Persons are assembled from the language lists (our own, in lists.tsv); companies are real legal names from the GLEIF golden copy (CC0) of the Tier 2
// countries whose legal form is found at the end of the name through the ISO 20275 list. Output is the same JSON Lines as tools/data/names.
// The split of an example is fixed by a hash of its folded text before anything else (0-89 train, 90-94 dev, 95-99 reserved and never written).
internal static class Program
{
    private const string Version = "names-tier2-1";

    private static readonly string[] CompanyCountries =
    [
        "AT", "BE", "CH", "DE", "LI", "LU", "MC", "NL", "AD", "ES", "IT", "JE", "GG", "IM", "MT", "PT", "SM", "VA", "CZ", "EE", "LV", "NO", "LT", "MX",
        "PL", "SI", "SK", "DK", "FI", "IS", "SE", "HR", "HU", "RO", "AU",
    ];

    private static readonly Dictionary<string, string> LanguageOf = new(StringComparer.Ordinal)
    {
        ["AT"] = "de", ["CH"] = "de", ["DE"] = "de", ["LI"] = "de", ["LU"] = "de", ["BE"] = "nl", ["NL"] = "nl", ["MC"] = "fr", ["AD"] = "ca", ["ES"] = "es",
        ["IT"] = "it", ["SM"] = "it", ["VA"] = "it", ["JE"] = "en", ["GG"] = "en", ["IM"] = "en", ["MT"] = "mt", ["PT"] = "pt", ["CZ"] = "cs", ["EE"] = "et",
        ["LV"] = "lv", ["NO"] = "nb", ["LT"] = "lt", ["MX"] = "es", ["PL"] = "pl", ["SI"] = "sl", ["SK"] = "sk", ["DK"] = "da", ["FI"] = "fi", ["IS"] = "is",
        ["SE"] = "sv", ["HR"] = "hr", ["HU"] = "hu", ["RO"] = "ro", ["AU"] = "en",
    };

    private static int Main(string[] args)
    {
        Dictionary<string, string> o = [];
        for (int i = 1; i + 1 < args.Length; i += 2)
        {
            o[args[i].TrimStart('-')] = args[i + 1];
        }

        if (args.Length == 0 || args[0] != "render")
        {
            Console.Error.WriteLine("usage: render --lists F --gleif F --elf F --out DIR [--seed N] [--persons N] [--multiple N] [--companies N]");
            return 2;
        }

        ulong seed = ulong.Parse(o.GetValueOrDefault("seed", "20261107"), CultureInfo.InvariantCulture);
        int persons = int.Parse(o.GetValueOrDefault("persons", "7000"), CultureInfo.InvariantCulture);
        int multiple = int.Parse(o.GetValueOrDefault("multiple", "900"), CultureInfo.InvariantCulture);
        int companies = int.Parse(o.GetValueOrDefault("companies", "7000"), CultureInfo.InvariantCulture);
        string outDir = o["out"];
        Directory.CreateDirectory(outDir);

        Dictionary<string, Lists> lists = ReadLists(o["lists"]);
        List<Example> all = [];
        foreach ((string lang, Lists l) in lists.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (l.GivenM.Count > 0)
            {
                for (int i = 0; i < persons; i++)
                {
                    all.Add(Person(lang, l, Prng.For(seed, $"p|{lang}|{i}"), $"t2p-{lang}-{i}"));
                }

                for (int i = 0; i < multiple; i++)
                {
                    all.Add(Multiple(lang, l, Prng.For(seed, $"m|{lang}|{i}"), $"t2m-{lang}-{i}"));
                }
            }

            int u = 0;
            foreach (string item in l.Unknown)
            {
                foreach (string variant in new[] { item, item.ToUpperInvariant(), item.ToLowerInvariant() })
                {
                    all.Add(Unknown(lang, variant, $"t2u-{lang}-{u++}"));
                }
            }
        }

        ElfList elf = ElfList.Load(o["elf"]);
        Registers.GleifCountries.UnionWith(CompanyCountries);
        List<CompanyRecord> records = Registers.LoadGleif(o["gleif"], [], out SortedDictionary<string, long> gleifStats);
        Dictionary<string, int> perCountry = new(StringComparer.Ordinal);
        foreach (CompanyRecord r in records.Where(r => r.Source == "gleif" && CompanyCountries.Contains(r.Jurisdiction.Length >= 2 ? r.Jurisdiction[..2] : r.Jurisdiction)).OrderBy(r => Prng.Hash(r.Name, seed)))
        {
            string cc = r.Jurisdiction.Length >= 2 ? r.Jurisdiction[..2] : r.Jurisdiction;
            if (perCountry.GetValueOrDefault(cc) >= companies)
            {
                continue;
            }

            Example? e = Company(r, cc, elf, Prng.For(seed, "c|" + r.Name));
            if (e is not null)
            {
                all.Add(e);
                perCountry[cc] = perCountry.GetValueOrDefault(cc) + 1;
            }
        }

        // The exact bytes depend on the order, so it is fixed: by id.
        all.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        SortedDictionary<string, long> counts;
        using (ExampleWriter w = new(outDir))
        {
            foreach (Example e in all)
            {
                if (e.Split != "reserved")
                {
                    w.Write(e);
                }
            }

            counts = w.Counts;
        }

        WriteManifest(outDir, o, seed, version: Version, counts, perCountry, gleifStats);

        Console.WriteLine("done " + Version + " examples=" + all.Count(e => e.Split != "reserved"));
        return 0;
    }

    private sealed class Lists
    {
        internal List<string> GivenM = [];
        internal List<string> GivenF = [];
        internal List<string> Family = [];
        internal List<string> Title = [];
        internal List<string> Particle = [];
        internal List<string> Prof = [];
        internal List<string> Unknown = [];
        internal string Conn = "and";
        internal bool FamilyFirst;
    }

    private static Dictionary<string, Lists> ReadLists(string path)
    {
        Dictionary<string, Lists> result = new(StringComparer.Ordinal);
        foreach (string line in File.ReadLines(path, Encoding.UTF8))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] f = line.Split('\t');
            if (!result.TryGetValue(f[0], out Lists? l))
            {
                result[f[0]] = l = new Lists();
            }

            List<string> items = [.. f[2].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
            switch (f[1])
            {
                case "givenM": l.GivenM = items; break;
                case "givenF": l.GivenF = items; break;
                case "family": l.Family = items; break;
                case "title": l.Title = items; break;
                case "particle": l.Particle = items; break;
                case "prof": l.Prof = items; break;
                case "unknown": l.Unknown = items; break;
                case "conn": l.Conn = f[2]; break;
                case "order": l.FamilyFirst = f[2] == "family-first"; break;
                default: throw new InvalidDataException("unknown list kind " + f[1]);
            }
        }

        return result;
    }

    private static string Given(Lists l, Prng r, out bool female)
    {
        female = r.Chance(0.5);
        List<string> list = female ? l.GivenF : l.GivenM;
        return list[r.Int(list.Count)];
    }

    private static Example Person(string lang, Lists l, Prng r, string id)
    {
        string given = Given(l, r, out bool female);
        string family = l.Family[r.Int(l.Family.Count)];
        // Lithuanian, Latvian and Icelandic family names have a feminine form; the lists carry both and the draw is independent, which is acceptable for structure.
        double x = r.Unit();
        Builder b = new();
        string pattern;
        string sp = " ";
        bool familyFirst = l.FamilyFirst;
        if (x < 0.50)
        {
            pattern = familyFirst ? "fg" : "gf";
            if (familyFirst)
            {
                b.Family(family).Raw(sp).Add("Given", given);
            }
            else
            {
                b.Add("Given", given).Raw(sp).Family(family);
            }
        }
        else if (x < 0.58)
        {
            pattern = "gmf";
            string middle = (female ? l.GivenF : l.GivenM)[r.Int((female ? l.GivenF : l.GivenM).Count)];
            if (familyFirst)
            {
                b.Family(family).Raw(sp).Add("Given", given).Raw(sp).Add("Middle", middle);
            }
            else
            {
                b.Add("Given", given).Raw(sp).Add("Middle", middle).Raw(sp).Family(family);
            }
        }
        else if (x < 0.65)
        {
            pattern = "initial";
            b.Add("Given", given[0] + ".").Raw(sp).Family(family);
        }
        else if (x < 0.74)
        {
            pattern = "comma";
            b.Family(family).Raw(", ").Add("Given", given);
        }
        else if (x < 0.86 && l.Title.Count > 0)
        {
            pattern = "title-gf";
            b.Add("Prefix", l.Title[r.Int(l.Title.Count)]).Raw(sp);
            if (familyFirst)
            {
                b.Family(family).Raw(sp).Add("Given", given);
            }
            else
            {
                b.Add("Given", given).Raw(sp).Family(family);
            }
        }
        else if (x < 0.91 && l.Title.Count > 0)
        {
            pattern = "title-f";
            b.Add("Prefix", l.Title[r.Int(l.Title.Count)]).Raw(sp).Family(family);
        }
        else if (x < 0.95 && l.Prof.Count > 0)
        {
            pattern = "gf-prof";
            b.Add("Given", given).Raw(sp).Family(family).Raw(", ").Add("ProfessionalSuffix", l.Prof[r.Int(l.Prof.Count)]);
        }
        else if (l.Particle.Count > 0)
        {
            pattern = "particle";
            string pf = l.Particle[r.Int(l.Particle.Count)];
            string plain = l.Family[r.Int(l.Family.Count)];
            int cut = Builder.ParticleLength(plain);
            if (cut > 0)
            {
                plain = plain[(cut + 1)..];
            }

            b.Add("Given", given).Raw(sp).Add("Particle", pf).Raw(sp).Add("Family", plain);
        }
        else
        {
            pattern = "gf";
            b.Add("Given", given).Raw(sp).Family(family);
        }

        return Finish(id, b, "person", pattern, lang, lang.ToUpperInvariant(), "name-lists", r);
    }

    private static Example Multiple(string lang, Lists l, Prng r, string id)
    {
        string g1 = Given(l, r, out _);
        string g2 = Given(l, r, out _);
        string f1 = l.Family[r.Int(l.Family.Count)];
        Builder b = new();
        string pattern;
        if (r.Chance(0.6))
        {
            pattern = "shared";
            b.Add("Given", g1, 1).Raw(" " + l.Conn + " ").Add("Given", g2, 2).Raw(" ").Family(f1, 1, 2);
        }
        else
        {
            pattern = "two";
            b.Add("Given", g1, 1).Raw(" ").Family(f1, 1).Raw(r.Chance(0.5) ? " " + l.Conn + " " : "; ").Add("Given", g2, 2).Raw(" ").Family(l.Family[r.Int(l.Family.Count)], 2);
        }

        return Finish(id, b, "multiple", pattern, lang, lang.ToUpperInvariant(), "name-lists", r);
    }

    private static Example Unknown(string lang, string text, string id)
    {
        int bucket = Txt.Bucket(Txt.Fold(text));
        return new Example
        {
            Id = id,
            Text = text,
            Class = "unknown",
            Pattern = "list",
            Lang = lang,
            Region = lang.ToUpperInvariant(),
            Source = "templates",
            Split = bucket < 90 ? "train" : bucket < 95 ? "dev" : "reserved",
            Parts = [],
        };
    }

    private static Example? Company(CompanyRecord rec, string cc, ElfList elf, Prng r)
    {
        string name = rec.Name.Trim();
        List<Form> forms = [.. elf.FormsOf(rec.Code ?? string.Empty)];
        if (rec.Code is null || forms.Count == 0)
        {
            return null;
        }

        forms.Sort(static (a, b) => b.Text.Length != a.Text.Length ? b.Text.Length.CompareTo(a.Text.Length) : string.CompareOrdinal(a.Text, b.Text));
        FormMatch? m = ElfList.FindTail(name, forms, skipGeneric: false);
        if (m is null)
        {
            return null;
        }

        string baseName = name[..m.Value.Start].TrimEnd(' ', ',');
        string form = name[m.Value.Start..m.Value.End];
        if (baseName.Length < 2)
        {
            return null;
        }

        double x = r.Unit();
        Builder b = new();
        string pattern;
        if (x < 0.15)
        {
            pattern = "no-form";
            b.Add("Base", baseName);
        }
        else
        {
            pattern = "form";
            b.Add("Base", baseName).Raw(" ").Add("LegalForm", form);
        }

        string text = b.Text;
        string noise = string.Empty;
        double z = r.Unit();
        if (z < 0.08)
        {
            text = Txt.MapChars(text, upper: true);
            noise = "upper";
        }
        else if (z < 0.14)
        {
            text = Txt.MapChars(text, upper: false);
            noise = "lower";
        }

        int bucket = Txt.Bucket(Txt.Fold(baseName));
        return new Example
        {
            Id = "t2c-" + cc + "-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..12].ToLowerInvariant(),
            Text = text,
            Class = "company",
            Pattern = pattern,
            Noise = noise,
            Lang = LanguageOf.GetValueOrDefault(cc, "en"),
            Region = cc,
            Source = "gleif",
            Elf = pattern == "form" ? rec.Code : null,
            Split = bucket < 90 ? "train" : bucket < 95 ? "dev" : "reserved",
            Parts = b.Parts,
        };
    }

    private static Example Finish(string id, Builder b, string cls, string pattern, string lang, string region, string source, Prng r)
    {
        string plain = b.Text;
        string text = plain;
        string noise = string.Empty;
        double z = r.Unit();
        if (z < 0.07)
        {
            text = Txt.MapChars(text, upper: true);
            noise = "upper";
        }
        else if (z < 0.13)
        {
            text = Txt.MapChars(text, upper: false);
            noise = "lower";
        }

        int bucket = Txt.Bucket(Txt.Fold(plain));
        return new Example
        {
            Id = id,
            Text = text,
            Class = cls,
            Pattern = pattern,
            Noise = noise,
            Lang = lang,
            Region = region,
            Source = source,
            Split = bucket < 90 ? "train" : bucket < 95 ? "dev" : "reserved",
            Parts = b.Parts,
        };
    }

    private static void WriteManifest(string outDir, Dictionary<string, string> o, ulong seed, string version, SortedDictionary<string, long> counts, Dictionary<string, int> perCountry, SortedDictionary<string, long> gleifStats)
    {
        static string Hash(string path)
        {
            using FileStream s = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
        }

        Dictionary<string, string> files = [];
        foreach (string f in Directory.GetFiles(outDir, "*.jsonl", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
        {
            files[Path.GetRelativePath(outDir, f)] = Hash(f);
        }

        var manifest = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["version"] = version,
            ["seed"] = seed,
            ["inputs"] = new SortedDictionary<string, string> { ["lists"] = Hash(o["lists"]), ["gleif"] = Hash(o["gleif"]), ["elf"] = Hash(o["elf"]) },
            ["companies_per_country"] = new SortedDictionary<string, int>(perCountry, StringComparer.Ordinal),
            ["gleif_stats"] = gleifStats,
            ["counts"] = counts,
            ["files"] = new SortedDictionary<string, string>(files, StringComparer.Ordinal),
        };
        File.WriteAllText(Path.Combine(outDir, "manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }
}
