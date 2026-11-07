using System.Buffers;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using OneContact.Data.AddressUsCa;

namespace OneContact.Data.AddressChallenger;

// AddressChallenger: the OpenStreetMap address file of the published libpostal parser training set (ODbL), converted to our labelled
// training format for a challenger address model.
//
//   AddressChallenger count  --input <formatted_addresses_tagged.random.tsv.gz> --out <dir> [--target 250000] [--countries us,ca,...]
//   AddressChallenger render --input <same file> --out <dir> [--seed 20261106] [--dev-per-mille 20]
//   AddressChallenger check  --out <dir>
//
// The output belongs to the challenger lineage only: it is never read by the default models' recipes (see the manifest's "set" and "lineage").
internal static class Program
{
    internal const string SetName = "challenger-osm";
    internal const ulong SelectSalt = 0x4348414C4C534CUL;
    internal const ulong SplitSalt = 0x4348414C535031UL;

    // The tier countries (docs: coverage) plus the territories their posts serve; Greece, Bulgaria and Cyprus are outside the Latin-script tiers.
    internal static readonly string[] AllCountries =
    [
        "US", "PR", "GU", "VI", "AS", "MP", "CA",
        "MX", "AU", "GB", "JE", "GG", "IM",
        "AT", "BE", "HR", "CZ", "DK", "EE", "FI", "FR", "GP", "MQ", "GF", "RE", "YT", "DE", "HU", "IE", "IT", "LV", "LT", "LU", "MT", "NL", "PL", "PT", "RO", "SK", "SI", "ES", "SE",
        "CH", "NO", "IS", "LI", "AD", "MC", "SM", "VA",
    ];

    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: AddressChallenger count|render|check --out <dir> ... (see Program.cs)");
            return 2;
        }

        Dictionary<string, string> o = new(StringComparer.Ordinal);
        for (int i = 1; i + 1 < args.Length; i += 2)
        {
            o[args[i].TrimStart('-')] = args[i + 1];
        }

        return args[0] switch
        {
            "count" => Count(o),
            "render" => Render(o),
            "check" => Check(o),
            _ => 2,
        };
    }

    private static string[] Countries(Dictionary<string, string> o) =>
        o.TryGetValue("countries", out string? c) ? c.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim().ToUpperInvariant()).ToArray() : AllCountries;

    private static double Unit(ulong h) => (h >> 11) * (1.0 / 9007199254740992.0);

    private static string IdOf(string country, long line) => $"osm:{country}:{line}";

    /// <summary>Reads the gzip'd file line by line; the callback gets the line number (1-based), language, upper-case country and the tagged tokens of lines whose country is wanted.</summary>
    private static void Scan(string path, HashSet<string> wanted, Action<long, string, string, string> visit, out long total)
    {
        using FileStream fs = File.OpenRead(path);
        using GZipStream gz = new(fs, CompressionMode.Decompress);
        using StreamReader reader = new(gz, new UTF8Encoding(false), false, 1 << 20);
        Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> lookup = wanted.ToDictionary(c => c.ToLowerInvariant(), c => c, StringComparer.Ordinal).GetAlternateLookup<ReadOnlySpan<char>>();
        long n = 0;
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            n++;
            ReadOnlySpan<char> s = line;
            int t1 = s.IndexOf('\t');
            if (t1 < 0)
            {
                continue;
            }

            int t2 = s[(t1 + 1)..].IndexOf('\t');
            if (t2 < 0)
            {
                continue;
            }

            t2 += t1 + 1;
            if (!lookup.TryGetValue(s[(t1 + 1)..t2], out string? country))
            {
                continue;
            }

            visit(n, line[..t1], country, line[(t2 + 1)..]);
        }

        total = n;
    }

    private static int Count(Dictionary<string, string> o)
    {
        string outDir = o["out"];
        Directory.CreateDirectory(outDir);
        long target = long.Parse(o.GetValueOrDefault("target", "250000"), CultureInfo.InvariantCulture);
        string[] countries = Countries(o);
        Rejects rejects = new();
        SortedDictionary<string, long> lines = new(StringComparer.Ordinal);
        SortedDictionary<string, long> usable = new(StringComparer.Ordinal);
        Scan(o["input"], countries.ToHashSet(StringComparer.Ordinal), (n, lang, country, tags) =>
        {
            lines[country] = lines.GetValueOrDefault(country) + 1;
            if (Converter.Convert(tags, lang, country, IdOf(country, n), 1, rejects) != null)
            {
                usable[country] = usable.GetValueOrDefault(country) + 1;
            }
        }, out long total);

        StringBuilder sb = new();
        sb.Append("# country\tlines in the file\tusable lines\tsampling rate (selected = hash(line id, selection salt) below rate)\n");
        foreach (string c in countries.Order(StringComparer.Ordinal))
        {
            long u = usable.GetValueOrDefault(c);
            double rate = u == 0 ? 0 : Math.Min(1.0, (double)target / u);
            sb.Append(CultureInfo.InvariantCulture, $"{c}\t{lines.GetValueOrDefault(c)}\t{u}\t{rate:R}\n");
        }

        File.WriteAllText(Path.Combine(outDir, "selection.tsv"), sb.ToString(), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(outDir, "rejects-count.tsv"), $"# lines in the file\t{total}\n" + string.Concat(rejects.Counts.Select(k => $"{k.Key}\t{k.Value}\n")), new UTF8Encoding(false));
        Console.WriteLine($"counted {total} lines");
        return 0;
    }

    private static readonly JavaScriptEncoder Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    private static int Render(Dictionary<string, string> o)
    {
        ulong seed = ulong.Parse(o.GetValueOrDefault("seed", "20261106"), CultureInfo.InvariantCulture);
        int devPerMille = int.Parse(o.GetValueOrDefault("dev-per-mille", "20"), CultureInfo.InvariantCulture);
        string outDir = o["out"];
        string selectionPath = o.GetValueOrDefault("selection", Path.Combine(outDir, "selection.tsv"));
        Dictionary<string, double> rate = new(StringComparer.Ordinal);
        foreach (string line in File.ReadLines(selectionPath))
        {
            if (line.Length > 0 && line[0] != '#')
            {
                string[] c = line.Split('\t');
                rate[c[0]] = double.Parse(c[3], CultureInfo.InvariantCulture);
            }
        }

        string[] countries = Countries(o).Where(rate.ContainsKey).ToArray();
        Dictionary<string, StreamWriter> writers = new(StringComparer.Ordinal);
        foreach (string c in countries)
        {
            foreach (string role in new[] { "train", "dev" })
            {
                writers[c + "." + role] = new StreamWriter(Path.Combine(outDir, $"{c.ToLowerInvariant()}.{role}.jsonl"), false, new UTF8Encoding(false), 1 << 20) { NewLine = "\n" };
            }
        }

        Stats stats = new();
        Rejects rejects = new();
        long duplicates = 0;
        Dictionary<string, HashSet<ulong>> seen = countries.ToDictionary(c => c, _ => new HashSet<ulong>(), StringComparer.Ordinal);
        ArrayBufferWriter<byte> buffer = new();
        Scan(o["input"], countries.ToHashSet(StringComparer.Ordinal), (n, lang, country, tags) =>
        {
            string id = IdOf(country, n);
            if (Unit(Prng.Hash(id, SelectSalt)) >= rate[country])
            {
                return;
            }

            // Duplicates: the same tagged text twice in one country keeps the first.
            if (!seen[country].Add(Prng.Hash(tags, 0)))
            {
                duplicates++;
                return;
            }

            Example? ex = Converter.Convert(tags, lang, country, id, seed, rejects);
            if (ex == null)
            {
                return;
            }

            string role = Prng.Hash(id, SplitSalt) % 1000 < (ulong)devPerMille ? "dev" : "train";
            buffer.ResetWrittenCount();
            WriteJson(buffer, ex);
            StreamWriter w = writers[country + "." + role];
            w.Write(Encoding.UTF8.GetString(buffer.WrittenSpan));
            w.Write('\n');
            stats.Add(country, role, ex);
        }, out long total);

        foreach (StreamWriter w in writers.Values)
        {
            w.Dispose();
        }

        File.WriteAllText(Path.Combine(outDir, "rejects-render.tsv"), $"# duplicates dropped\t{duplicates}\n" + string.Concat(rejects.Counts.Select(k => $"{k.Key}\t{k.Value}\n")), new UTF8Encoding(false));
        Manifest.Write(Path.Combine(outDir, "manifest.json"), o, seed, devPerMille, stats, outDir, selectionPath, duplicates, total, countries);
        Console.WriteLine($"rendered from {total} lines");
        return 0;
    }

    private static void WriteJson(ArrayBufferWriter<byte> buffer, Example ex)
    {
        using Utf8JsonWriter w = new(buffer, new JsonWriterOptions { Encoder = Encoder });
        w.WriteStartObject();
        w.WriteString("id", ex.Id);
        w.WriteString("text", ex.Text);
        w.WritePropertyName("tokens");
        w.WriteStartArray();
        foreach ((int s, int e) in ex.Tokens)
        {
            w.WriteStartArray();
            w.WriteNumberValue(s);
            w.WriteNumberValue(e);
            w.WriteEndArray();
        }

        w.WriteEndArray();
        w.WritePropertyName("entities");
        WriteSpans(w, ex.Entities);
        w.WritePropertyName("spans");
        w.WriteStartObject();
        w.WritePropertyName("fields");
        WriteSpans(w, ex.Fields);
        w.WriteEndObject();
        w.WriteString("country", ex.Country);
        w.WriteString("lang", ex.Language);
        w.WriteString("layout", ex.Layout);
        w.WriteString("region", string.Empty);
        w.WriteString("set", SetName);
        w.WriteEndObject();
    }

    private static void WriteSpans(Utf8JsonWriter w, List<(int Start, int End, string Label)> spans)
    {
        w.WriteStartArray();
        foreach ((int s, int e, string l) in spans)
        {
            w.WriteStartArray();
            w.WriteNumberValue(s);
            w.WriteNumberValue(e);
            w.WriteStringValue(l);
            w.WriteEndArray();
        }

        w.WriteEndArray();
    }

    /// <summary>Span rules on every line of every output file, and the split rule: a train line's id must hash to train, a dev line's to dev.</summary>
    private static int Check(Dictionary<string, string> o)
    {
        string outDir = o["out"];
        int devPerMille = int.Parse(o.GetValueOrDefault("dev-per-mille", "20"), CultureInfo.InvariantCulture);
        long lines = 0;
        long failures = 0;
        long split = 0;
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (string file in Directory.GetFiles(outDir, "*.jsonl").Order(StringComparer.Ordinal))
        {
            bool dev = file.EndsWith(".dev.jsonl", StringComparison.Ordinal);
            foreach (string line in File.ReadLines(file))
            {
                lines++;
                string? problem = SpanCheck.Validate(line);
                using JsonDocument doc = JsonDocument.Parse(line);
                string id = doc.RootElement.GetProperty("id").GetString()!;
                if (doc.RootElement.GetProperty("set").GetString() != SetName)
                {
                    problem ??= "set is not " + SetName;
                }

                if ((Prng.Hash(id, SplitSalt) % 1000 < (ulong)devPerMille) != dev)
                {
                    split++;
                    problem ??= "split rule";
                }

                if (!ids.Add(id))
                {
                    problem ??= "repeated id";
                }

                if (problem != null)
                {
                    failures++;
                    if (failures <= 20)
                    {
                        Console.WriteLine($"{Path.GetFileName(file)} {id}: {problem}");
                    }
                }
            }
        }

        Console.WriteLine($"span check: {lines} lines, {failures} failures ({split} against the split rule)");
        return failures == 0 ? 0 : 1;
    }

    internal static string Sha256OfFile(string path)
    {
        using FileStream fs = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(fs));
    }
}

/// <summary>Counts of what was written, per country: examples by role, labels, layouts and languages.</summary>
internal sealed class Stats
{
    internal SortedDictionary<string, long> Lines { get; } = new(StringComparer.Ordinal);

    internal SortedDictionary<string, SortedDictionary<string, long>> Groups { get; } = new(StringComparer.Ordinal);

    internal void Add(string country, string role, Example ex)
    {
        Bump(Lines, $"{country}.{role}");
        Bump(G($"{country}.layout"), ex.Layout);
        Bump(G($"{country}.language"), ex.Language);
        foreach ((int _, int _, string label) in ex.Entities)
        {
            Bump(G($"{country}.label"), label);
        }
    }

    private SortedDictionary<string, long> G(string key)
    {
        if (!Groups.TryGetValue(key, out SortedDictionary<string, long>? g))
        {
            Groups[key] = g = new SortedDictionary<string, long>(StringComparer.Ordinal);
        }

        return g;
    }

    private static void Bump(SortedDictionary<string, long> d, string k) => d[k] = d.GetValueOrDefault(k) + 1;
}
