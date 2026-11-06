using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using OneContact.Data.AddressTier2A;
using OneContact.Data.AddressUsCa;

namespace OneContact.Data.AddressTier2C;

// AddressTier2C: register addresses of Norway, the Czech Republic, Latvia and Estonia rendered as labelled free-text training examples.
//
//   AddressTier2C count  --data <train dir> --out <dir> [--countries no,cz,...] [--targets NO=250000,...]
//   AddressTier2C render --data <train dir> --out <dir> [--countries no,cz,...] [--seed 20261105] [--dev-per-mille 20]
//   AddressTier2C check  --out <dir>
internal static class Program
{
    private static readonly string[] AllCountries = ["NO", "CZ", "LV", "EE"];

    private static readonly Dictionary<string, long> DefaultTargets = new(StringComparer.Ordinal)
    {
        ["NO"] = 250_000, ["CZ"] = 250_000, ["LV"] = 150_000, ["EE"] = 150_000,
    };

    internal const ulong SelectSalt = 0x53454C45435433UL;
    internal const ulong SplitSalt = 0x53504C49543033UL;

    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: AddressTier2C count|render|check --out <dir> ... (see Program.cs)");
            return 2;
        }

        Dictionary<string, string> o = ParseOptions(args[1..]);
        return args[0] switch
        {
            "count" => Count(o),
            "render" => Render(o),
            "check" => Check(o),
            _ => 2,
        };
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        Dictionary<string, string> o = new(StringComparer.Ordinal);
        for (int i = 0; i + 1 < args.Length; i += 2)
        {
            o[args[i].TrimStart('-')] = args[i + 1];
        }

        return o;
    }

    private static string[] Countries(Dictionary<string, string> o) =>
        o.TryGetValue("countries", out string? c) ? c.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim().ToUpperInvariant()).ToArray() : AllCountries;

    private static double Unit(ulong h) => (h >> 11) * (1.0 / 9007199254740992.0);

    private static int Count(Dictionary<string, string> o)
    {
        string data = o["data"];
        string outDir = o["out"];
        Directory.CreateDirectory(outDir);
        Dictionary<string, long> targets = new(DefaultTargets, StringComparer.Ordinal);
        foreach (KeyValuePair<string, long> kv in Registers.TargetsC3)
        {
            targets.TryAdd(kv.Key, kv.Value);
        }

        if (o.TryGetValue("targets", out string? t))
        {
            foreach (string kv in t.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] p = kv.Split('=');
                targets[p[0].Trim().ToUpperInvariant()] = long.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        Rejects rejects = new();
        string selectionFile = Path.Combine(outDir, "selection.tsv");
        // Counting can run country by country; rows of countries not counted now are kept.
        List<string> kept = [];
        string[] counted = Countries(o);
        if (File.Exists(selectionFile))
        {
            kept = File.ReadLines(selectionFile).Where(l => l.Length > 0 && l[0] != '#' && !counted.Contains(l[..l.IndexOf('\t', StringComparison.Ordinal)], StringComparer.Ordinal)).ToList();
        }

        StringBuilder sb = new();
        sb.AppendLine("# country\tregion\tusable records\tsampling rate (selected = hash(record id, selection salt) below rate)");
        foreach (string line in kept)
        {
            sb.Append(line).Append('\n');
        }

        foreach (string country in counted)
        {
            SortedDictionary<string, long> counts = new(StringComparer.Ordinal);
            foreach (Addr a in Registers.Read(country, data, rejects))
            {
                counts[a.Region] = counts.GetValueOrDefault(a.Region) + 1;
            }

            long cap = SolveCap(counts.Values.ToList(), targets[country]);
            foreach (KeyValuePair<string, long> kv in counts)
            {
                double rate = Math.Min(1.0, (double)cap / kv.Value);
                sb.Append(country).Append('\t').Append(kv.Key).Append('\t').Append(kv.Value).Append('\t').Append(rate.ToString("0.000000000", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
            }

            Console.WriteLine($"{country}: {counts.Values.Sum()} usable records in {counts.Count} regions, cap {cap} per region");
        }

        File.WriteAllText(selectionFile, SortRows(sb.ToString()), new UTF8Encoding(false));
        WriteRejects(Path.Combine(outDir, "rejects-count-" + string.Join("-", counted) + ".tsv"), rejects);
        return 0;
    }

    private static string SortRows(string text)
    {
        List<string> rows = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(l => l[0] != '#').OrderBy(l => l, StringComparer.Ordinal).ToList();
        return "# country\tregion\tusable records\tsampling rate (selected = hash(record id, selection salt) below rate)\n" + string.Concat(rows.Select(r => r + "\n"));
    }

    /// <summary>The largest per-region cap whose total selection stays within the target: even coverage of the regions, small ones taken whole.</summary>
    private static long SolveCap(List<long> counts, long target)
    {
        long lo = 0;
        long hi = counts.Count == 0 ? 0 : counts.Max();
        while (lo < hi)
        {
            long mid = (lo + hi + 1) / 2;
            if (counts.Sum(c => Math.Min(c, mid)) <= target)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return lo;
    }

    private static void WriteRejects(string path, Rejects rejects)
    {
        File.WriteAllText(path, string.Concat(rejects.Counts.Select(k => $"{k.Key}\t{k.Value}\n")), new UTF8Encoding(false));
    }

    private static int Render(Dictionary<string, string> o)
    {
        ulong seed = ulong.Parse(o.GetValueOrDefault("seed", "20261105"), System.Globalization.CultureInfo.InvariantCulture);
        int devPerMille = int.Parse(o.GetValueOrDefault("dev-per-mille", "20"), System.Globalization.CultureInfo.InvariantCulture);
        string outDir = o["out"];
        string data = o["data"];
        string selectionPath = o.GetValueOrDefault("selection", Path.Combine(outDir, "selection.tsv"));
        Dictionary<string, double> rate = new(StringComparer.Ordinal);
        foreach (string line in File.ReadLines(selectionPath))
        {
            if (line.Length > 0 && line[0] != '#')
            {
                string[] c = line.Split('\t');
                rate[c[0] + "\t" + c[1]] = double.Parse(c[3], System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        Renderer renderer = new(Path.Combine(AppContext.BaseDirectory, "templates"), new Rates(), seed);
        RendererC3 rendererC3 = new(Path.Combine(AppContext.BaseDirectory, "templates"), new Rates(), seed);
        Stats stats = new();
        Rejects rejects = new();
        long duplicates = 0;
        ArrayBufferWriter<byte> buffer = new();
        foreach (string country in Countries(o))
        {
            Dictionary<string, StreamWriter> writers = new(StringComparer.Ordinal);
            foreach (string role in new[] { "train", "dev" })
            {
                string name = $"{country.ToLowerInvariant()}.{role}.jsonl";
                writers[role] = new StreamWriter(Path.Combine(outDir, name), false, new UTF8Encoding(false), 1 << 20) { NewLine = "\n" };
            }

            HashSet<ulong> seen = [];
            foreach (Addr a in Registers.Read(country, data, rejects))
            {
                if (!rate.TryGetValue(country + "\t" + a.Region, out double p) || Unit(Prng.Hash(a.Id, SelectSalt)) >= p)
                {
                    continue;
                }

                ulong key = Prng.Hash(string.Join('|', a.Country, a.Region, a.Street, a.HouseNumber, a.Suffix, a.Unit, a.Locality, a.PostalCode), 0);
                if (!seen.Add(key))
                {
                    duplicates++;
                    continue;
                }

                bool dev = Prng.Hash(a.Id, SplitSalt) % 1000 < (ulong)devPerMille;
                Example ex = RendererC3.Handles(country) ? rendererC3.Render(a) : renderer.Render(a);
                string role = dev ? "dev" : "train";
                buffer.ResetWrittenCount();
                WriteJson(buffer, ex, a);
                writers[role].Write(Encoding.UTF8.GetString(buffer.WrittenSpan));
                writers[role].Write('\n');
                stats.Add(country, role, a, ex);
            }

            foreach (StreamWriter w in writers.Values)
            {
                w.Dispose();
            }
        }

        WriteRejects(Path.Combine(outDir, "rejects-render.tsv"), rejects);
        Manifest.Write(Path.Combine(outDir, "manifest.json"), o, seed, devPerMille, renderer, stats, outDir, selectionPath, duplicates, data);
        foreach (KeyValuePair<string, long> kv in stats.Lines.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"{kv.Key}\t{kv.Value}");
        }

        return 0;
    }

    private static int Check(Dictionary<string, string> o)
    {
        long lines = 0;
        long failures = 0;
        foreach (string file in Directory.GetFiles(o["out"], "*.jsonl").OrderBy(f => f, StringComparer.Ordinal))
        {
            long n = 0;
            long bad = 0;
            foreach (string line in File.ReadLines(file))
            {
                n++;
                string? problem = SpanCheck.Validate(line);
                if (problem is not null && bad++ < 3)
                {
                    Console.WriteLine($"{Path.GetFileName(file)}:{n}: {problem}: {line}");
                }
            }

            lines += n;
            failures += bad;
            Console.WriteLine($"{Path.GetFileName(file)}\t{n}\t{bad} failures");
        }

        Console.WriteLine($"{lines} examples, {failures} span-check failures");
        return failures == 0 ? 0 : 1;
    }

    private static readonly JavaScriptEncoder Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    private static void WriteJson(ArrayBufferWriter<byte> buffer, Example ex, Addr a)
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
        w.WriteString("region", a.Region);
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
}

/// <summary>Counts of what was written, per country: examples by role, labels, layouts, languages, regions and the variants drawn.</summary>
internal sealed class Stats
{
    internal SortedDictionary<string, long> Lines { get; } = new(StringComparer.Ordinal);

    internal SortedDictionary<string, SortedDictionary<string, long>> Groups { get; } = new(StringComparer.Ordinal);

    internal void Add(string country, string role, Addr a, Example ex)
    {
        Bump(Lines, $"{country}.{role}");
        Bump(G($"{country}.region"), a.Region);
        Bump(G($"{country}.layout"), ex.Layout);
        Bump(G($"{country}.language"), ex.Language);
        foreach ((int _, int _, string label) in ex.Entities)
        {
            Bump(G($"{country}.label"), label);
        }

        foreach (string e in ex.Events)
        {
            Bump(G($"{country}.variant"), e);
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
