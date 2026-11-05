using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace OneContact.Data.AddressUsCa;

// usage:
//   AddressUsCa count  --nad <NAD_TXT.zip> --oda <dir with ODA_*_v1.zip> --out <dir> [--us-target N] [--ca-target N]
//   AddressUsCa render --nad <zip> --oda <dir> --out <dir> [--seed N] [--dev-per-mille N] [--selection <file>]
//   AddressUsCa sample --nad <zip> | --oda <zip> | --tiger <dir> [--n 12] [--seed N]
//   AddressUsCa count  --tiger <dir with tl_*_addrfeat.zip, tl_*_edges.zip and zcta-place.txt> --out <dir> --caps "PR=80000,*=27000" [--per-range "*=3,AS=12"]
//   render with --tiger takes the same options, plus --countries us --dataset address-us-tiger
//   AddressUsCa check  --out <dir> [--sample N]
internal static class Program
{
    internal const string TemplateVersionNote = "layouts.tsv and names.tsv under templates/";

    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: AddressUsCa count|render|sample|check ...");
            return 2;
        }

        Dictionary<string, string> o = ParseOptions(args.Skip(1).ToArray());
        string baseDir = AppContext.BaseDirectory;
        Rules rules = new(Path.Combine(baseDir, "rules"));
        Templates templates = new(Path.Combine(baseDir, "templates"));
        OdaReader.ExcludedProviders = File.ReadLines(Path.Combine(baseDir, "templates", "oda-excluded-providers.tsv"))
            .Where(l => l.Length > 0 && l[0] != '#').Select(l => l.Split('\t')[0]).ToHashSet(StringComparer.Ordinal);
        switch (args[0])
        {
            case "count": return Counter.Run(o, rules);
            case "render": return Generator.Run(o, rules, templates);
            case "sample": return Generator.Sample(o, rules, templates);
            case "check": return Checker.Run(o);
            default:
                Console.Error.WriteLine("unknown command " + args[0]);
                return 2;
        }
    }

    internal static Dictionary<string, string> ParseOptions(string[] args)
    {
        Dictionary<string, string> o = new(StringComparer.Ordinal);
        for (int i = 0; i + 1 < args.Length; i += 2)
        {
            o[args[i].TrimStart('-')] = args[i + 1];
        }

        return o;
    }

    /// <summary>The <c>--per-range</c> option: how many house numbers are drawn from each address range, <c>*=3,AS=12</c>.</summary>
    internal static Dictionary<string, int> PerRange(Dictionary<string, string> o)
    {
        Dictionary<string, int> d = new(StringComparer.Ordinal) { ["*"] = 3 };
        foreach (string part in o.GetValueOrDefault("per-range", string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] kv = part.Split('=');
            d[kv[0].Trim()] = int.Parse(kv[1], System.Globalization.CultureInfo.InvariantCulture);
        }

        return d;
    }

    internal static string Sha256File(string path)
    {
        using FileStream f = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant();
    }

    internal static IEnumerable<string> OdaFiles(string directory) =>
        Directory.GetFiles(directory, "ODA_*_v1.zip").OrderBy(p => p, StringComparer.Ordinal);

    /// <summary>The records of both registers in a fixed order: the one NAD file, then the ODA provinces by file name.</summary>
    internal static IEnumerable<Rec> Records(Dictionary<string, string> o, Rules rules, Rejects nadRejects, Rejects odaRejects)
    {
        if (o.TryGetValue("nad", out string? nad))
        {
            foreach (Rec r in NadReader.Read(nad, rules, nadRejects))
            {
                yield return r;
            }
        }

        if (o.TryGetValue("tiger", out string? tiger))
        {
            foreach (Rec r in TigerReader.Read(tiger, rules, nadRejects, PerRange(o)))
            {
                yield return r;
            }
        }

        if (o.TryGetValue("oda", out string? oda))
        {
            string[] files = Directory.Exists(oda) ? OdaFiles(oda).ToArray() : [oda];
            foreach (string f in files)
            {
                foreach (Rec r in OdaReader.Read(f, rules, odaRejects))
                {
                    yield return r;
                }
            }
        }
    }
}

/// <summary>Pass one: counts the usable records per region and works out the per-region sampling rates.</summary>
internal static class Counter
{
    internal static int Run(Dictionary<string, string> o, Rules rules)
    {
        long usTarget = long.Parse(o.GetValueOrDefault("us-target", "1300000"));
        long caTarget = long.Parse(o.GetValueOrDefault("ca-target", "380000"));
        string outDir = o["out"];
        Directory.CreateDirectory(outDir);
        // --caps "PR=80000,*=27000": a fixed cap per region instead of one shared cap that meets the country target.
        Dictionary<string, long>? caps = o.TryGetValue("caps", out string? capText)
            ? capText.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=')).ToDictionary(p => p[0].Trim(), p => long.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture), StringComparer.Ordinal)
            : null;
        Rejects nad = new();
        Rejects oda = new();
        SortedDictionary<string, long> counts = new(StringComparer.Ordinal);
        foreach (Rec r in Program.Records(o, rules, nad, oda))
        {
            string key = r.Country + "\t" + r.Region;
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        StringBuilder sb = new();
        sb.AppendLine("# country\tregion\tusable records\tsampling rate (selected = hash(record id, selection salt) below rate)");
        foreach (string country in new[] { "US", "CA" })
        {
            long target = country == "US" ? usTarget : caTarget;
            List<KeyValuePair<string, long>> mine = counts.Where(k => k.Key.StartsWith(country + "\t", StringComparison.Ordinal)).ToList();
            long cap = SolveCap(mine.Select(k => k.Value).ToList(), target);
            foreach (KeyValuePair<string, long> kv in mine)
            {
                if (caps is not null)
                {
                    string region = kv.Key[(kv.Key.IndexOf('\t', StringComparison.Ordinal) + 1)..];
                    cap = caps.TryGetValue(region, out long own) ? own : caps.GetValueOrDefault("*", kv.Value);
                }

                double rate = Math.Min(1.0, (double)cap / kv.Value);
                sb.Append(kv.Key).Append('\t').Append(kv.Value).Append('\t').Append(rate.ToString("0.000000000", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
            }
        }

        File.WriteAllText(Path.Combine(outDir, "selection.tsv"), sb.ToString(), new UTF8Encoding(false));
        WriteRejects(Path.Combine(outDir, "rejects.tsv"), nad, oda);
        Console.WriteLine($"selection written; usable US {counts.Where(k => k.Key.StartsWith("US", StringComparison.Ordinal)).Sum(k => k.Value)}, CA {counts.Where(k => k.Key.StartsWith("CA", StringComparison.Ordinal)).Sum(k => k.Value)}");
        return 0;
    }

    /// <summary>The largest per-region cap whose total selection (each region contributes min(count, cap)) stays within the target: even coverage of the regions, small ones taken whole.</summary>
    private static long SolveCap(List<long> counts, long target)
    {
        long lo = 0;
        long hi = counts.Count == 0 ? 0 : counts.Max();
        while (lo < hi)
        {
            long mid = (lo + hi + 1) / 2;
            long total = counts.Sum(c => Math.Min(c, mid));
            if (total <= target)
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

    internal static void WriteRejects(string path, Rejects nad, Rejects oda)
    {
        StringBuilder sb = new();
        sb.AppendLine("# register\treason\trows");
        foreach (KeyValuePair<string, long> kv in nad.Counts)
        {
            sb.Append("nad\t").Append(kv.Key).Append('\t').Append(kv.Value).Append('\n');
        }

        foreach (KeyValuePair<string, long> kv in oda.Counts)
        {
            sb.Append("oda\t").Append(kv.Key).Append('\t').Append(kv.Value).Append('\n');
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }
}

/// <summary>Pass two: selects, renders, splits and writes the examples, and the manifest that describes them.</summary>
internal static class Generator
{
    internal const ulong SelectSalt = 0x53454C45435431UL;
    internal const ulong SplitSalt = 0x53504C49543031UL;
    private static readonly JavaScriptEncoder Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    internal static int Run(Dictionary<string, string> o, Rules rules, Templates templates)
    {
        ulong seed = ulong.Parse(o.GetValueOrDefault("seed", "20261105"));
        int devPerMille = int.Parse(o.GetValueOrDefault("dev-per-mille", "20"));
        string outDir = o["out"];
        string selectionPath = o.GetValueOrDefault("selection", Path.Combine(outDir, "selection.tsv"));
        Directory.CreateDirectory(outDir);
        Dictionary<string, double> rate = new(StringComparer.Ordinal);
        foreach (string line in File.ReadLines(selectionPath))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] c = line.Split('\t');
            rate[c[0] + "\t" + c[1]] = double.Parse(c[3], System.Globalization.CultureInfo.InvariantCulture);
        }

        Rates rates = new();
        Renderer renderer = new(rules, templates, rates, seed);
        Stats stats = new();
        Dictionary<string, StreamWriter> writers = new(StringComparer.Ordinal);
        foreach (string country in o.GetValueOrDefault("countries", "us,ca").Split(','))
        {
            foreach (string role in new[] { "train", "dev" })
            {
                string name = $"{country}.{role}.jsonl";
                writers[name] = new StreamWriter(Path.Combine(outDir, name), false, new UTF8Encoding(false), 1 << 20) { NewLine = "\n" };
            }
        }

        HashSet<ulong> seen = [];
        Rejects nadRejects = new();
        Rejects odaRejects = new();
        long duplicates = 0;
        ArrayBufferWriter<byte> buffer = new();
        foreach (Rec rec in Program.Records(o, rules, nadRejects, odaRejects))
        {
            if (!rate.TryGetValue(rec.Country + "\t" + rec.Region, out double p) || Unit(Prng.Hash(rec.Id, SelectSalt)) >= p)
            {
                continue;
            }

            ulong key = Prng.Hash(string.Join('|', rec.Country, rec.Region, rec.HouseNumber, rec.Fraction, rec.PreDirectional, rec.StreetName, rec.StreetType, rec.PostDirectional, rec.Unit, rec.City, rec.PostalCode), 0);
            if (!seen.Add(key))
            {
                duplicates++;
                continue;
            }

            bool dev = Prng.Hash(rec.Id, SplitSalt) % 1000 < (ulong)devPerMille;
            Example ex = renderer.Render(rec);
            string file = $"{rec.Country.ToLowerInvariant()}.{(dev ? "dev" : "train")}.jsonl";
            buffer.ResetWrittenCount();
            WriteJson(buffer, ex, rec);
            writers[file].Write(Encoding.UTF8.GetString(buffer.WrittenSpan));
            writers[file].Write('\n');
            stats.Add(file, rec, ex);
        }

        foreach (StreamWriter w in writers.Values)
        {
            w.Dispose();
        }

        Counter.WriteRejects(Path.Combine(outDir, "rejects.tsv"), nadRejects, odaRejects);
        WriteManifest(Path.Combine(outDir, "manifest.json"), o, seed, devPerMille, rates, templates, stats, outDir, selectionPath, duplicates);
        foreach (KeyValuePair<string, long> kv in stats.Lines.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"{kv.Key}\t{kv.Value}");
        }

        return 0;
    }

    internal static int Sample(Dictionary<string, string> o, Rules rules, Templates templates)
    {
        int n = int.Parse(o.GetValueOrDefault("n", "12"));
        ulong seed = ulong.Parse(o.GetValueOrDefault("seed", "1"));
        Renderer renderer = new(rules, templates, new Rates(), seed);
        Rejects a = new();
        Rejects b = new();
        int shown = 0;
        int skip = int.Parse(o.GetValueOrDefault("skip", "0"));
        foreach (Rec rec in Program.Records(o, rules, a, b))
        {
            if (skip-- > 0)
            {
                continue;
            }

            Example ex = renderer.Render(rec);
            Console.WriteLine($"--- {ex.Layout} [{string.Join(',', ex.Events)}]");
            Console.WriteLine(ex.Text);
            Console.WriteLine(string.Join("  ", ex.Entities.Select(e => $"{ex.Text[e.Start..e.End]}={e.Label}")));
            if (++shown >= n)
            {
                break;
            }
        }

        foreach (KeyValuePair<string, long> kv in a.Counts.Concat(b.Counts))
        {
            Console.WriteLine($"reject {kv.Key} {kv.Value}");
        }

        return 0;
    }

    private static double Unit(ulong h) => (h >> 11) * (1.0 / 9007199254740992.0);

    private static void WriteJson(ArrayBufferWriter<byte> buffer, Example ex, Rec rec)
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
        w.WriteString("region", rec.Region);
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

    private static void WriteManifest(string path, Dictionary<string, string> o, ulong seed, int devPerMille, Rates rates, Templates templates, Stats stats, string outDir, string selectionPath, long duplicates)
    {
        using FileStream fs = File.Create(path);
        using Utf8JsonWriter w = new(fs, new JsonWriterOptions { Indented = true, Encoder = Encoder });
        w.WriteStartObject();
        w.WriteString("dataset", o.GetValueOrDefault("dataset", "address-us-ca"));
        w.WriteString("generator", "OneContact.Data.AddressUsCa");
        w.WriteNumber("seed", seed);
        w.WriteString("templateVersion", templates.Version);
        if (templates.PrVersion.Length > 0)
        {
            w.WriteString("templateVersionPuertoRico", templates.PrVersion);
        }

        w.WriteString("rates", rates.ToString());
        w.WriteString("split", $"dev when hash(record id, split salt) mod 1000 < {devPerMille}; the rest is train. Fixed before rendering, from the register record id only.");
        w.WriteString("selectionFile", Path.GetFileName(selectionPath));
        w.WriteString("selectionSha256", Program.Sha256File(selectionPath));
        w.WriteNumber("duplicateAddressesSkipped", duplicates);
        w.WriteString("excludedProviders", string.Join("; ", OdaReader.ExcludedProviders.OrderBy(x => x, StringComparer.Ordinal)));
        w.WriteString("excludedProvidersFileSha256", Program.Sha256File(Path.Combine(AppContext.BaseDirectory, "templates", "oda-excluded-providers.tsv")));
        w.WritePropertyName("registers");
        w.WriteStartArray();
        if (o.TryGetValue("nad", out string? nad))
        {
            WriteRegister(w, nad, "US DOT National Address Database (NAD), text file, compiled 2026-09-30", "public domain (17 U.S.C. 105)");
        }

        if (o.TryGetValue("tiger", out string? tiger))
        {
            foreach (string f in Directory.GetFiles(tiger).Where(f => f.EndsWith(".zip", StringComparison.Ordinal) || f.EndsWith(".txt", StringComparison.Ordinal)).OrderBy(f => f, StringComparer.Ordinal))
            {
                WriteRegister(w, f, Path.GetFileName(f) == "zcta-place.txt" ? "Census 2020 ZCTA to place relationship file (ZIP Code to place names)" : "Census TIGER/Line 2025 county shapefile, address ranges or edges (attribute table only)", "public domain (17 U.S.C. 105)");
            }
        }

        if (o.TryGetValue("oda", out string? oda))
        {
            foreach (string f in Directory.Exists(oda) ? Program.OdaFiles(oda) : [oda])
            {
                WriteRegister(w, f, "Statistics Canada Open Database of Addresses v1.0 (46-26-0001, 2021001)", "Open Government Licence - Canada");
            }
        }

        w.WriteEndArray();
        w.WritePropertyName("files");
        w.WriteStartArray();
        foreach (string name in stats.Lines.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            w.WriteStartObject();
            w.WriteString("file", name);
            w.WriteString("role", name.Contains(".dev.", StringComparison.Ordinal) ? "dev" : "train");
            w.WriteNumber("examples", stats.Lines[name]);
            w.WriteString("sha256", Program.Sha256File(Path.Combine(outDir, name)));
            w.WriteEndObject();
        }

        w.WriteEndArray();
        Group(w, "perLayout", stats.Layouts);
        Group(w, "perRegion", stats.Regions);
        Group(w, "perLanguage", stats.Languages);
        Group(w, "entitiesPerLabel", stats.Labels);
        Group(w, "generatorEvents", stats.Events);
        w.WriteEndObject();
    }

    private static void WriteRegister(Utf8JsonWriter w, string path, string description, string licence)
    {
        w.WriteStartObject();
        w.WriteString("file", Path.GetFileName(path));
        w.WriteString("description", description);
        w.WriteString("licence", licence);
        w.WriteNumber("bytes", new FileInfo(path).Length);
        w.WriteString("sha256", Program.Sha256File(path));
        w.WriteEndObject();
    }

    private static void Group(Utf8JsonWriter w, string name, SortedDictionary<string, long> counts)
    {
        w.WritePropertyName(name);
        w.WriteStartObject();
        foreach (KeyValuePair<string, long> kv in counts)
        {
            w.WriteNumber(kv.Key, kv.Value);
        }

        w.WriteEndObject();
    }

    internal sealed class Stats
    {
        internal SortedDictionary<string, long> Lines { get; } = new(StringComparer.Ordinal);

        internal SortedDictionary<string, long> Layouts { get; } = new(StringComparer.Ordinal);

        internal SortedDictionary<string, long> Regions { get; } = new(StringComparer.Ordinal);

        internal SortedDictionary<string, long> Languages { get; } = new(StringComparer.Ordinal);

        internal SortedDictionary<string, long> Labels { get; } = new(StringComparer.Ordinal);

        internal SortedDictionary<string, long> Events { get; } = new(StringComparer.Ordinal);

        internal void Add(string file, Rec rec, Example ex)
        {
            Bump(Lines, file);
            string role = file.Contains(".dev.", StringComparison.Ordinal) ? "dev" : "train";
            Bump(Layouts, $"{rec.Country}.{role}.{ex.Layout}");
            Bump(Regions, $"{rec.Country}.{role}.{rec.Region}");
            Bump(Languages, $"{rec.Country}.{role}.{ex.Language}");
            foreach ((int _, int _, string label) in ex.Entities)
            {
                Bump(Labels, $"{rec.Country}.{label}");
            }

            foreach (string e in ex.Events)
            {
                Bump(Events, $"{rec.Country}.{e}");
            }

            // where the record came from (nad, oda, tiger, tigeredge): tigeredge records have a random house number.
            Bump(Events, $"{rec.Country}.source-{rec.Id[..rec.Id.IndexOf(':', StringComparison.Ordinal)]}");
        }

        private static void Bump(SortedDictionary<string, long> d, string k) => d[k] = d.GetValueOrDefault(k) + 1;
    }
}
