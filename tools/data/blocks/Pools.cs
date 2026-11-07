using System.Text;
using System.Text.Json;

namespace OneContact.Data.Blocks;

/// <summary>The three locales blocks are rendered for. Tier 2 languages are added by adding a locale, its pools and its lexicon entries.</summary>
internal enum Loc
{
    UsEn = 0,
    CaEn = 1,
    CaFr = 2,

    /// <summary>Tier 2 blocks: people and companies of the Tier 2 languages, addresses of the Tier 2 registers, English page words plus the contact labels of the Tier 2 languages.</summary>
    T2 = 3,
}

internal sealed record PersonRec(string Text, string Given, string Family, string Lang, bool Noisy);

internal sealed record CompanyRec(string Text, string Base, string? Form, string Lang, bool Noisy);

/// <summary>An address example from a register rendering. <see cref="Text"/> is the whole example; the labelled address is the slice Start..End (recipient lines are never used).</summary>
internal sealed record AddressRec(string Text, int Start, int End, string Lang, string Country = "")
{
    internal string Value => Text.Substring(Start, End - Start);
}

/// <summary>The rendered person, company and address examples that blocks are assembled from, per split and locale. Train blocks draw only from train files and dev blocks only from dev files.</summary>
internal sealed class Pools
{
    internal readonly Dictionary<(string Split, Loc Loc), List<PersonRec>> Persons = [];
    internal readonly Dictionary<(string Split, Loc Loc), List<CompanyRec>> Companies = [];
    internal readonly Dictionary<(string Split, Loc Loc), List<AddressRec>> Addresses = [];
    internal readonly List<string> InputManifests = [];

    private static Loc LocOf(string region, string lang) =>
        region == "CA" ? (lang == "fr" ? Loc.CaFr : Loc.CaEn) : Loc.UsEn;

    internal static Pools Load(string namesDir, string addressDir, string tigerDir)
    {
        Pools p = new();
        foreach (string split in new[] { "train", "dev" })
        {
            foreach (string f in Directory.GetFiles(Path.Combine(namesDir, split), "*.jsonl").OrderBy(x => x, StringComparer.Ordinal))
            {
                p.ReadNames(f, split);
            }

            foreach ((string dir, string file) in new[] { (addressDir, "us"), (addressDir, "ca"), (tigerDir, "us") })
            {
                string path = Path.Combine(dir, $"{file}.{split}.jsonl");
                if (File.Exists(path))
                {
                    p.ReadAddresses(path, split);
                }
            }
        }

        foreach (string m in new[] { Path.Combine(namesDir, "manifest.json"), Path.Combine(addressDir, "manifest.json"), Path.Combine(tigerDir, "manifest.json") })
        {
            if (File.Exists(m))
            {
                p.InputManifests.Add(m);
            }
        }

        return p;
    }

    /// <summary>Loads the Tier 2 pools: persons and companies of the Tier 2 name sets and a stride-sample of each Tier 2 address file (at most <paramref name="perFile"/> train and a tenth of that dev records each).</summary>
    internal static Pools LoadTier2(string namesDir, IReadOnlyList<string> addressFiles, int perFile)
    {
        Pools p = new();
        foreach (string split in new[] { "train", "dev" })
        {
            foreach (string f in Directory.GetFiles(Path.Combine(namesDir, split), "*.jsonl").OrderBy(x => x, StringComparer.Ordinal))
            {
                p.ReadNames(f, split, tier2: true);
            }
        }

        p.InputManifests.Add(Path.Combine(namesDir, "manifest.json"));
        foreach (string file in addressFiles)
        {
            string name = Path.GetFileName(file);
            string split = name.Contains(".dev.", StringComparison.Ordinal) ? "dev" : "train";
            long lines = File.ReadLines(file).LongCount();
            long limit = split == "dev" ? Math.Max(1, perFile / 10) : perFile;
            p.ReadAddresses(file, split, tier2: true, stride: (int)Math.Max(1, lines / limit));
        }

        foreach (string dir in addressFiles.Select(f => Path.GetDirectoryName(f)!).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
        {
            string m = Path.Combine(dir, "manifest.json");
            if (File.Exists(m))
            {
                p.InputManifests.Add(m);
            }
        }

        return p;
    }

    private void ReadNames(string path, string split, bool tier2 = false)
    {
        foreach (string line in File.ReadLines(path, Encoding.UTF8))
        {
            using JsonDocument doc = JsonDocument.Parse(line);
            JsonElement e = doc.RootElement;
            string cls = e.GetProperty("class").GetString()!;
            string region = e.GetProperty("region").GetString()!;
            if (cls is not ("person" or "company") || (!tier2 && region is not ("US" or "CA")))
            {
                continue;
            }

            string lang = e.GetProperty("lang").GetString()!;
            string text = e.GetProperty("text").GetString()!;
            bool noisy = e.TryGetProperty("noise", out _);
            Loc loc = tier2 ? Loc.T2 : LocOf(region, lang);
            if (text.Length > 70 || text.Contains('\n'))
            {
                continue;
            }

            string? Part(string label)
            {
                foreach (JsonElement part in e.GetProperty("parts").EnumerateArray())
                {
                    if (part[0].GetString() == label)
                    {
                        return text.Substring(part[1].GetInt32(), part[2].GetInt32() - part[1].GetInt32());
                    }
                }

                return null;
            }

            if (cls == "person")
            {
                string? given = Part("Given");
                string? family = Part("Family");
                if (given is null || family is null)
                {
                    continue;
                }

                // "Family, Given" forms are kept (people write them) but only as noisy entries, which blocks use sparingly.
                bool comma = text.Contains(',');
                Add(Persons, split, loc, new PersonRec(text, given, family, lang, noisy || comma));
            }
            else
            {
                string? b = Part("Base");
                if (b is null)
                {
                    continue;
                }

                Add(Companies, split, loc, new CompanyRec(text, b, Part("LegalForm"), lang, noisy));
            }
        }
    }

    private void ReadAddresses(string path, string split, bool tier2 = false, int stride = 1)
    {
        long n = 0;
        foreach (string line in File.ReadLines(path, Encoding.UTF8))
        {
            if (n++ % stride != 0)
            {
                continue;
            }

            using JsonDocument doc = JsonDocument.Parse(line);
            JsonElement e = doc.RootElement;
            string text = e.GetProperty("text").GetString()!;
            if (text.Length > 160)
            {
                continue;
            }

            int start = int.MaxValue;
            int end = -1;
            int count = 0;
            bool skip = false;
            foreach (JsonElement ent in e.GetProperty("entities").EnumerateArray())
            {
                string label = ent[2].GetString()!;
                if (label is "recipient" or "organization" || label.StartsWith("country", StringComparison.Ordinal))
                {
                    skip = true;
                    break;
                }

                start = Math.Min(start, ent[0].GetInt32());
                end = Math.Max(end, ent[1].GetInt32());
                count++;
            }

            if (skip || count < 3)
            {
                continue;
            }

            string country = e.GetProperty("country").GetString()!;
            string lang = e.TryGetProperty("lang", out JsonElement l) ? l.GetString()! : "en";
            Add(Addresses, split, tier2 ? Loc.T2 : LocOf(country, lang), new AddressRec(text, start, end, lang, country));
        }
    }

    private static void Add<T>(Dictionary<(string, Loc), List<T>> d, string split, Loc loc, T item)
    {
        if (!d.TryGetValue((split, loc), out List<T>? l))
        {
            d[(split, loc)] = l = [];
        }

        l.Add(item);
    }

    internal string Summary() =>
        string.Join("; ", Persons.OrderBy(k => k.Key).Select(k => $"persons {k.Key.Split}/{k.Key.Loc}={k.Value.Count}")
            .Concat(Companies.OrderBy(k => k.Key).Select(k => $"companies {k.Key.Split}/{k.Key.Loc}={k.Value.Count}"))
            .Concat(Addresses.OrderBy(k => k.Key).Select(k => $"addresses {k.Key.Split}/{k.Key.Loc}={k.Value.Count}")));
}
