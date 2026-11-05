using System.Globalization;
using System.Security.Cryptography;

namespace OneContact.Data.Names;

internal sealed record Title(string Text, char Sex, bool DotOptional);

internal sealed record Suffix(string Text);

/// <summary>The hand-written template lists (forms of address, suffixes, particles, placeholder strings, trade words) and their hashes for the manifest.</summary>
internal sealed class Templates
{
    internal static readonly string[] Languages = ["en", "fr", "es"];

    internal Dictionary<string, WeightedList<Title>> Titles { get; } = new(StringComparer.Ordinal);

    internal Dictionary<string, WeightedList<Suffix>> Generational { get; } = new(StringComparer.Ordinal);

    internal Dictionary<string, WeightedList<Suffix>> Professional { get; } = new(StringComparer.Ordinal);

    internal Dictionary<string, WeightedList<string>> Particles { get; } = new(StringComparer.Ordinal);

    internal Dictionary<string, WeightedList<string>> Forms { get; } = new(StringComparer.Ordinal);

    /// <summary>Plain lists by kind and language: <c>Lists["trade|en"]</c>.</summary>
    internal Dictionary<string, List<string>> Lists { get; } = new(StringComparer.Ordinal);

    internal SortedDictionary<string, string> Hashes { get; } = new(StringComparer.Ordinal);

    internal static Templates Load(string dir)
    {
        Templates t = new();
        foreach (string file in Directory.GetFiles(dir, "*.tsv").OrderBy(f => f, StringComparer.Ordinal))
        {
            t.Hashes[Path.GetFileName(file)] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
        }

        Dictionary<string, List<(Title, double)>> titles = [];
        foreach (string[] f in Rows(Path.Combine(dir, "titles.tsv")))
        {
            Add(titles, f[0], (new Title(f[1], f[2][0], f[3] == "1"), D(f[4])));
        }

        foreach ((string lang, List<(Title, double)> list) in titles)
        {
            t.Titles[lang] = new WeightedList<Title>(list);
        }

        Dictionary<string, List<(Suffix, double)>> gen = [];
        Dictionary<string, List<(Suffix, double)>> pro = [];
        foreach (string[] f in Rows(Path.Combine(dir, "suffixes.tsv")))
        {
            double w = D(f[3]);
            if (w <= 0)
            {
                continue;
            }

            foreach (string lang in f[2] == "*" ? Languages : new[] { f[2] })
            {
                Add(f[0] == "gen" ? gen : pro, lang, (new Suffix(f[1]), w));
            }
        }

        foreach ((string lang, List<(Suffix, double)> list) in gen)
        {
            t.Generational[lang] = new WeightedList<Suffix>(list);
        }

        foreach ((string lang, List<(Suffix, double)> list) in pro)
        {
            t.Professional[lang] = new WeightedList<Suffix>(list);
        }

        Dictionary<string, List<(string, double)>> particles = [];
        foreach (string[] f in Rows(Path.Combine(dir, "particles.tsv")))
        {
            double w = D(f[2]);
            if (w > 0)
            {
                Add(particles, f[0], (f[1], w));
            }
        }

        foreach ((string lang, List<(string, double)> list) in particles)
        {
            t.Particles[lang] = new WeightedList<string>(list);
        }

        Dictionary<string, List<(string, double)>> forms = [];
        foreach (string[] f in Rows(Path.Combine(dir, "forms.tsv")))
        {
            Add(forms, f[0], (f[1], D(f[2])));
        }

        foreach ((string country, List<(string, double)> list) in forms)
        {
            t.Forms[country] = new WeightedList<string>(list);
        }

        foreach (string[] f in Rows(Path.Combine(dir, "negatives.tsv")))
        {
            string key = f[0] + "|" + f[1];
            if (!t.Lists.TryGetValue(key, out List<string>? list))
            {
                t.Lists[key] = list = [];
            }

            list.Add(f[2]);
        }

        return t;
    }

    internal List<string> List(string kind, string lang) => Lists.TryGetValue(kind + "|" + lang, out List<string>? l) ? l : Lists[kind + "|en"];

    private static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    private static void Add<T>(Dictionary<string, List<T>> map, string key, T item)
    {
        if (!map.TryGetValue(key, out List<T>? list))
        {
            map[key] = list = [];
        }

        list.Add(item);
    }

    private static IEnumerable<string[]> Rows(string path)
    {
        foreach (string line in File.ReadAllLines(path))
        {
            if (line.Length > 0 && line[0] != '#')
            {
                yield return line.Split('\t');
            }
        }
    }
}
