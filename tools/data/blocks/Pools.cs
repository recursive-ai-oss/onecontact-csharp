using System.Text;
using System.Text.Json;

namespace OneContact.Data.Blocks;

/// <summary>The three locales blocks are rendered for. Tier 2 languages are added by adding a locale, its pools and its lexicon entries.</summary>
internal enum Loc
{
    UsEn = 0,
    CaEn = 1,
    CaFr = 2,
}

internal sealed record PersonRec(string Text, string Given, string Family, string Lang, bool Noisy);

internal sealed record CompanyRec(string Text, string Base, string? Form, string Lang, bool Noisy);

/// <summary>An address example from a register rendering. <see cref="Text"/> is the whole example; the labelled address is the slice Start..End (recipient lines are never used).</summary>
internal sealed record AddressRec(string Text, int Start, int End, string Lang)
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

    private void ReadNames(string path, string split)
    {
        foreach (string line in File.ReadLines(path, Encoding.UTF8))
        {
            using JsonDocument doc = JsonDocument.Parse(line);
            JsonElement e = doc.RootElement;
            string cls = e.GetProperty("class").GetString()!;
            string region = e.GetProperty("region").GetString()!;
            if (region is not ("US" or "CA") || cls is not ("person" or "company"))
            {
                continue;
            }

            string lang = e.GetProperty("lang").GetString()!;
            string text = e.GetProperty("text").GetString()!;
            bool noisy = e.TryGetProperty("noise", out _);
            Loc loc = LocOf(region, lang);
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

    private void ReadAddresses(string path, string split)
    {
        foreach (string line in File.ReadLines(path, Encoding.UTF8))
        {
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
            Add(Addresses, split, LocOf(country, lang), new AddressRec(text, start, end, lang));
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
