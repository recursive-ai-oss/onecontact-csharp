using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace OneContact.Data.Names;

/// <summary>
/// Name and company training data renderer.
///   render --data DIR --elf CSV --out DIR [--seed N] [--persons N] [--multiple N] [--unknown N] [--ch-percent P] [--synth N] [--registers skip]
///   report --out DIR [--sample N]
/// </summary>
internal static class Program
{
    internal const string ToolVersion = "names-1";

    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: render --data DIR --elf CSV --out DIR [--seed N] [--persons N] [--multiple N] [--unknown N] [--ch-percent P] [--synth N] | render-tier2 --data DIR --out DIR [--seed N] [--persons N] [--multiple N] [--unknown N] | report --out DIR [--sample N]");
            return 2;
        }

        Dictionary<string, string> opts = [];
        for (int i = 1; i + 1 < args.Length; i += 2)
        {
            opts[args[i].TrimStart('-')] = args[i + 1];
        }

        string Opt(string key, string? fallback = null) =>
            opts.TryGetValue(key, out string? v) ? v : fallback ?? throw new ArgumentException($"missing --{key}");

        switch (args[0])
        {
            case "render":
                Render(
                    Opt("data"),
                    Opt("elf"),
                    Opt("out"),
                    ulong.Parse(Opt("seed", "20261105"), System.Globalization.CultureInfo.InvariantCulture),
                    int.Parse(Opt("persons", "1250000"), System.Globalization.CultureInfo.InvariantCulture),
                    int.Parse(Opt("multiple", "160000"), System.Globalization.CultureInfo.InvariantCulture),
                    int.Parse(Opt("unknown", "160000"), System.Globalization.CultureInfo.InvariantCulture),
                    int.Parse(Opt("ch-percent", "8"), System.Globalization.CultureInfo.InvariantCulture),
                    int.Parse(Opt("synth", "170000"), System.Globalization.CultureInfo.InvariantCulture),
                    Opt("registers", "all") == "skip");
                return 0;
            case "render-tier2":
                Tier2Gen.Render(
                    Opt("data"),
                    Opt("out"),
                    ulong.Parse(Opt("seed", "20261107"), System.Globalization.CultureInfo.InvariantCulture),
                    int.Parse(Opt("persons", "1500000"), System.Globalization.CultureInfo.InvariantCulture),
                    int.Parse(Opt("multiple", "160000"), System.Globalization.CultureInfo.InvariantCulture),
                    int.Parse(Opt("unknown", "100000"), System.Globalization.CultureInfo.InvariantCulture));
                return 0;
            case "report":
                RuleReport.Run(Opt("out"), int.Parse(Opt("sample", "5000"), System.Globalization.CultureInfo.InvariantCulture));
                return 0;
            default:
                Console.Error.WriteLine("unknown command " + args[0]);
                return 2;
        }
    }

    private static void Render(string dataDir, string elfPath, string outDir, ulong seed, int persons, int multiples, int unknowns, int chPercent, int synth, bool skipRegisters)
    {
        string baseDir = AppContext.BaseDirectory;
        string templateDir = Path.Combine(baseDir, "templates");
        Directory.CreateDirectory(outDir);
        Console.WriteLine("loading pools and templates");
        NamePools pools = NamePools.Load(dataDir, templateDir);
        Templates templates = Templates.Load(templateDir);
        ElfList elf = ElfList.Load(elfPath);
        PersonGen personGen = new(pools, templates);
        CompanyGen companyGen = new(elf, templates, personGen);

        SortedDictionary<string, SortedDictionary<string, long>> registerStats = new(StringComparer.Ordinal);
        HashSet<string> seen = new(StringComparer.Ordinal);
        List<CompanyRecord> records = [];
        if (!skipRegisters)
        {
            Console.WriteLine("reading GLEIF");
            records = Registers.LoadGleif(Path.Combine(dataDir, "gleif", "20261105-0800-gleif-goldencopy-lei2-golden-copy.csv.zip"), seen, out SortedDictionary<string, long> gleifStats);
            registerStats["gleif"] = gleifStats;
            Console.WriteLine("reading Companies House");
            records.AddRange(Registers.LoadCompaniesHouse(Path.Combine(dataDir, "ch", "BasicCompanyDataAsOneFile-2026-11-01.zip"), seen, chPercent, out SortedDictionary<string, long> chStats));
            registerStats["companies-house"] = chStats;
            Console.WriteLine("reading EDGAR");
            records.AddRange(Registers.LoadEdgar(Path.Combine(dataDir, "sec", "cik-lookup-data.txt"), seen, elf, out SortedDictionary<string, long> edgarStats));
            registerStats["edgar"] = edgarStats;
        }

        Console.WriteLine($"{records.Count} company records");

        // Trade names for "dba" and "c/o" parts: base names of every 16th record, fixed before rendering starts.
        for (int i = 0; i < records.Count; i += 16)
        {
            if (companyGen.TryDecompose(records[i], out _, out string b, out _, out _, out _, out _))
            {
                companyGen.RememberTradeName(b);
            }
        }

        SortedDictionary<string, long> skipped = new(StringComparer.Ordinal);
        long reserved = 0;
        long duplicates = 0;
        HashSet<ulong> texts = [];
        using (ExampleWriter writer = new(outDir))
        {
            void Emit(Example e, string kind)
            {
                if (e.Split == "reserved")
                {
                    reserved++;
                    return;
                }

                writer.Write(e);
                _ = kind;
            }

            void Run(string prefix, int attempts, Func<Prng, string, Example> make, bool dedupe = true)
            {
                for (int i = 0; i < attempts; i++)
                {
                    string id = prefix + i.ToString("D8", System.Globalization.CultureInfo.InvariantCulture);
                    Example? e = null;
                    for (int k = 0; k < 4 && e == null; k++)
                    {
                        Example c = make(Prng.For(seed, k == 0 ? id : id + "#" + k.ToString(System.Globalization.CultureInfo.InvariantCulture)), id);
                        if (c.Split == "reserved" || !dedupe || texts.Add(Txt.Hash64(Txt.Fold(c.Text))))
                        {
                            e = c;
                        }
                        else
                        {
                            duplicates++;
                        }
                    }

                    if (e != null)
                    {
                        Emit(e, prefix);
                    }
                }
            }

            Console.WriteLine("rendering persons");
            Run("p", persons, personGen.Single);
            Console.WriteLine("rendering several-people strings");
            Run("m", multiples, personGen.Multiple);
            Console.WriteLine("rendering non-names");
            Run("u", unknowns, personGen.Unknown, dedupe: false);

            Console.WriteLine("rendering register companies");
            for (int i = 0; i < records.Count; i++)
            {
                string id = "c" + i.ToString("D8", System.Globalization.CultureInfo.InvariantCulture);
                Example? e = companyGen.FromRecord(records[i], Prng.For(seed, id), id, out string reason);
                if (e == null)
                {
                    skipped[reason] = skipped.GetValueOrDefault(reason) + 1;
                    continue;
                }

                Emit(e, "c");
            }

            Console.WriteLine("rendering synthesized companies");
            Run("s", synth, companyGen.Synthesize);

            WriteOutputs(outDir, writer, seed, persons, multiples, unknowns, chPercent, synth, pools, templates, elfPath, registerStats, skipped, reserved, duplicates, records.Count);
        }
    }

    private static void WriteOutputs(
        string outDir,
        ExampleWriter writer,
        ulong seed,
        int persons,
        int multiples,
        int unknowns,
        int chPercent,
        int synth,
        NamePools pools,
        Templates templates,
        string elfPath,
        SortedDictionary<string, SortedDictionary<string, long>> registerStats,
        SortedDictionary<string, long> skipped,
        long reserved,
        long duplicates,
        int recordCount)
    {
        writer.Dispose();

        // The hashes of every train and dev text, sorted, as little-endian 64-bit numbers: an evaluation builder drops any candidate whose hash is here.
        writer.TextHashes.Sort();
        string hashFile = Path.Combine(outDir, "text-hashes.u64");
        using (FileStream fs = new(hashFile, FileMode.Create, FileAccess.Write))
        {
            byte[] buf = new byte[8];
            foreach (ulong h in writer.TextHashes)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf, h);
                fs.Write(buf);
            }
        }

        using FileStream manifest = new(Path.Combine(outDir, "manifest.json"), FileMode.Create, FileAccess.Write);
        using Utf8JsonWriter w = new(manifest, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        w.WriteStartObject();
        w.WriteString("tool", ToolVersion);
        w.WriteNumber("seed", seed);
        w.WriteStartObject("parameters");
        w.WriteNumber("personAttempts", persons);
        w.WriteNumber("multipleAttempts", multiples);
        w.WriteNumber("unknownAttempts", unknowns);
        w.WriteNumber("companiesHousePercent", chPercent);
        w.WriteNumber("synthesizedCompanyAttempts", synth);
        w.WriteNumber("firstBirthYear", NamePools.FirstBirthYear);
        w.WriteEndObject();

        w.WriteStartObject("splitRule");
        w.WriteString("key", "lower-case letters and digits of the base name (companies) or of the whole string before case changes (persons)");
        w.WriteString("hash", "FNV-1a 64-bit over UTF-8 with salt 0x73706C6974303031, then a SplitMix64 round; bucket = hash mod 100");
        w.WriteString("train", "bucket 0-89 (role: train)");
        w.WriteString("dev", "bucket 90-94 (role: dev)");
        w.WriteString("reserved", "bucket 95-99: never rendered, never trained or tuned on; the pool evaluation sets are drawn from");
        w.WriteEndObject();

        w.WriteStartArray("inputs");
        foreach (string line in File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "pins", "names.pin")))
        {
            if (line.StartsWith("file ", StringComparison.Ordinal))
            {
                string[] f = line.Split(' ');
                w.WriteStartObject();
                w.WriteString("file", f[1]);
                w.WriteNumber("bytes", long.Parse(f[2], System.Globalization.CultureInfo.InvariantCulture));
                w.WriteString("sha256", f[3]);
                w.WriteString("url", f[4]);
                w.WriteString("role", "source");
                w.WriteEndObject();
            }
        }

        w.WriteStartObject();
        w.WriteString("file", Path.GetFileName(elfPath));
        w.WriteString("sha256", Hex(SHA256.HashData(File.ReadAllBytes(elfPath))));
        w.WriteString("role", "source (ISO 20275 legal form list)");
        w.WriteEndObject();
        w.WriteEndArray();

        w.WriteStartObject("templates");
        foreach ((string name, string hash) in templates.Hashes)
        {
            w.WriteString(name, hash);
        }

        w.WriteEndObject();

        w.WriteStartObject("poolSizes");
        foreach ((string name, int size) in pools.Sizes)
        {
            w.WriteNumber(name, size);
        }

        w.WriteEndObject();

        w.WriteStartObject("registers");
        foreach ((string name, SortedDictionary<string, long> stats) in registerStats)
        {
            w.WriteStartObject(name);
            foreach ((string k, long v) in stats)
            {
                w.WriteNumber(k, v);
            }

            w.WriteEndObject();
        }

        w.WriteEndObject();

        w.WriteNumber("companyRecords", recordCount);
        w.WriteStartObject("skippedRecords");
        foreach ((string k, long v) in skipped)
        {
            w.WriteNumber(k, v);
        }

        w.WriteEndObject();
        w.WriteNumber("reservedNotRendered", reserved);
        w.WriteNumber("duplicateTextsRejected", duplicates);

        w.WriteStartObject("counts");
        long totalTrain = 0;
        long totalDev = 0;
        foreach ((string key, long v) in writer.Counts)
        {
            w.WriteNumber(key, v);
            string[] p = key.Split('|');
            if (p[1] != "noise")
            {
                if (p[0] == "train")
                {
                    totalTrain += v;
                }
                else if (p[0] == "dev")
                {
                    totalDev += v;
                }
            }
        }

        w.WriteEndObject();
        w.WriteNumber("totalTrain", totalTrain);
        w.WriteNumber("totalDev", totalDev);

        w.WriteStartArray("files");
        foreach (string file in Directory.GetFiles(outDir, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            string rel = Path.GetRelativePath(outDir, file).Replace('\\', '/');
            if (rel == "manifest.json")
            {
                continue;
            }

            w.WriteStartObject();
            w.WriteString("path", rel);
            w.WriteNumber("bytes", new FileInfo(file).Length);
            w.WriteString("sha256", Hex(SHA256.HashData(File.ReadAllBytes(file))));
            w.WriteString("role", rel.StartsWith("train/", StringComparison.Ordinal) ? "train" : rel.StartsWith("dev/", StringComparison.Ordinal) ? "dev" : "index");
            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteEndObject();
    }

    private static string Hex(byte[] hash) => Convert.ToHexString(hash).ToLowerInvariant();
}
