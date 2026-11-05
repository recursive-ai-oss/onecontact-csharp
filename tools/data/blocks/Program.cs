using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace OneContact.Data.Blocks;

/// <summary>
/// Synthetic contact-block generator.
///   render --names DIR --addr DIR --tiger DIR --out DIR [--seed N] [--count N]
///   report --out DIR [--sample N]
/// </summary>
internal static class Program
{
    internal const string ToolVersion = "blocks-1";
    private const ulong SplitSalt = 0x626C6F636B303031UL; // "block001"
    private const int ChunkSize = 1000;
    private const int ShardLines = 100_000;

    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: render --names DIR --addr DIR --tiger DIR --out DIR [--seed N] [--count N] | report --out DIR [--sample N]");
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
                return Render(Opt("names"), Opt("addr"), Opt("tiger"), Opt("out"),
                    ulong.Parse(Opt("seed", "20261106"), CultureInfo.InvariantCulture),
                    int.Parse(Opt("count", "560000"), CultureInfo.InvariantCulture));
            case "report":
                RuleReport.Run(Opt("out"), int.Parse(Opt("sample", "5000"), CultureInfo.InvariantCulture));
                return 0;
            default:
                Console.Error.WriteLine("unknown command " + args[0]);
                return 2;
        }
    }

    /// <summary>The split of a block: a stable hash of its id before anything is rendered. 0-92 train, 93-94 dev, 95-99 reserved (never rendered, so evaluation sets can be drawn from that space).</summary>
    internal static string? SplitOf(string id)
    {
        ulong bucket = Prng.Hash(id, SplitSalt) % 100;
        return bucket switch { < 93 => "train", < 95 => "dev", _ => null };
    }

    private sealed class Rendered
    {
        internal string Split = "";
        internal string Line = "";
    }

    private static int Render(string namesDir, string addrDir, string tigerDir, string outDir, ulong seed, int count)
    {
        Directory.CreateDirectory(outDir);
        Console.WriteLine("loading pools");
        Pools pools = Pools.Load(namesDir, addrDir, tigerDir);
        Console.WriteLine(pools.Summary());
        FieldGen fg = new();
        Renderer renderer = new(pools, fg);
        Stats total = new();

        Dictionary<string, ShardWriter> writers = new() { ["train"] = new ShardWriter(Path.Combine(outDir, "train")), ["dev"] = new ShardWriter(Path.Combine(outDir, "dev")) };
        int chunks = (count + ChunkSize - 1) / ChunkSize;
        const int Batch = 64;
        for (int b0 = 0; b0 < chunks; b0 += Batch)
        {
            int n = Math.Min(Batch, chunks - b0);
            (List<Rendered> Lines, Stats Stats)[] results = new (List<Rendered>, Stats)[n];
            Parallel.For(0, n, k =>
            {
                int first = (b0 + k) * ChunkSize;
                int last = Math.Min(count, first + ChunkSize);
                results[k] = RenderChunk(renderer, seed, first, last);
            });
            foreach ((List<Rendered> lines, Stats st) in results)
            {
                foreach (Rendered r in lines)
                {
                    writers[r.Split].Write(r.Line);
                }

                total.Merge(st);
            }

            Console.WriteLine($"{Math.Min(count, (b0 + n) * ChunkSize)} ids");
        }

        List<(string File, long Lines, string Sha256)> files = [];
        foreach ((string name, ShardWriter w) in writers)
        {
            foreach ((string f, long lines) in w.Close())
            {
                files.Add((Path.GetRelativePath(outDir, f).Replace('\\', '/'), lines, Sha(f)));
            }
        }

        WriteManifest(outDir, seed, count, pools, total, files);
        Console.WriteLine($"span failures: {total.SpanFailures}");
        return total.SpanFailures == 0 ? 0 : 1;
    }

    private static (List<Rendered>, Stats) RenderChunk(Renderer renderer, ulong seed, int first, int last)
    {
        List<Rendered> lines = [];
        Stats st = new();
        for (int i = first; i < last; i++)
        {
            string id = "b" + i.ToString("D8", CultureInfo.InvariantCulture);
            string? split = SplitOf(id);
            if (split is null)
            {
                st.Reserved++;
                continue;
            }

            Prng r = Prng.For(seed, id);
            Loc loc = (Loc)r.Weighted([60, 22, 18]);
            int ki = r.Weighted(Kind.All.Select(k => k.Weight).ToArray());
            (string kind, _, bool page, bool crm) = Kind.All[ki];
            Logical l = renderer.Make(r, split, loc, kind);
            st.Blocks.Bump(split + "/" + kind);
            for (int view = 0; view < 2; view++)
            {
                if (view == 0 && !page || view == 1 && !crm)
                {
                    continue;
                }

                string viewName = view == 0 ? "page" : "crm";
                Doc d = new() { Page = view == 0, R = Prng.For(seed, id + "/" + viewName) };
                d.Upper = d.R.Chance(0.05);
                d.Sep = view == 0 ? "\n" : d.R.Pick(new[] { "\n", "\n", "\n", "; ", " | ", ", ", " - ", "  ", " / " });
                switch (kind)
                {
                    case Kind.Footer: renderer.Footer(d, l); break;
                    case Kind.Contact: renderer.ContactUs(d, l); break;
                    case Kind.Staff: renderer.Staff(d, l); break;
                    case Kind.Signature: renderer.Signature(d, l); break;
                    case Kind.Card: renderer.Card(d, l); break;
                    case Kind.Note: renderer.Note(d, l, false); break;
                    case Kind.Ugc: renderer.Note(d, l, true); break;
                    case Kind.Multi: renderer.Multi(d, l); break;
                    case Kind.NegNumeric: renderer.NegNumeric(d, l); break;
                    case Kind.NegProse: renderer.NegProse(d, l); break;
                    default: throw new InvalidOperationException(kind);
                }

                int bad = d.Check(out string? problem);
                if (bad > 0)
                {
                    st.SpanFailures += bad;
                    st.FirstProblem ??= $"{id}/{viewName} {problem}";
                    st.Failures.Bump(kind + "/" + problem!.Split(' ')[0] + "/" + problem.Split(' ')[3].Trim('\''));
                }

                string line = ToJson(id + "." + viewName, id, kind, loc, viewName, d);
                lines.Add(new Rendered { Split = split, Line = line });
                st.Count(split, kind, loc, viewName, d);
            }
        }

        return (lines, st);
    }

    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string ToJson(string id, string block, string kind, Loc loc, string view, Doc d)
    {
        using MemoryStream ms = new();
        using (Utf8JsonWriter w = new(ms, WriterOptions))
        {
            w.WriteStartObject();
            w.WriteString("id", id);
            w.WriteString("block", block);
            w.WriteString("kind", kind);
            w.WriteString("loc", LocName(loc));
            w.WriteString("view", view);
            w.WriteBoolean("neg", Kind.IsNegative(kind));
            w.WriteString("text", d.Text);
            w.WriteStartArray("spans");
            foreach (SpanRec s in d.Spans)
            {
                w.WriteStartArray();
                w.WriteNumberValue(s.Start);
                w.WriteNumberValue(s.End);
                w.WriteStringValue(s.Label);
                w.WriteNumberValue(s.Record);
                if (s.Mode.Length > 0)
                {
                    w.WriteStringValue(s.Mode);
                }

                w.WriteEndArray();
            }

            w.WriteEndArray();
            w.WriteStartArray("bounds");
            foreach (int b in d.Bounds)
            {
                w.WriteNumberValue(b);
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    internal static string LocName(Loc loc) => loc switch { Loc.UsEn => "us-en", Loc.CaEn => "ca-en", _ => "ca-fr" };

    internal static string Sha(string path)
    {
        using FileStream f = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant();
    }

    private static void WriteManifest(string outDir, ulong seed, int count, Pools pools, Stats st, List<(string File, long Lines, string Sha256)> files)
    {
        string exeDir = AppContext.BaseDirectory;
        using FileStream fs = File.Create(Path.Combine(outDir, "manifest.json"));
        using Utf8JsonWriter w = new(fs, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        w.WriteStartObject();
        w.WriteString("tool", ToolVersion);
        w.WriteNumber("seed", seed);
        w.WriteNumber("idsAttempted", count);
        w.WriteNumber("idsReserved", st.Reserved);
        w.WriteStartObject("splitRule");
        w.WriteString("key", "block id, e.g. b00000042");
        w.WriteString("hash", "FNV-1a 64-bit over UTF-8 with salt 0x626C6F636B303031, then a SplitMix64 round; bucket = hash mod 100");
        w.WriteString("train", "bucket 0-92 (role: train); train blocks draw people, companies and addresses only from the train files of their sources");
        w.WriteString("dev", "bucket 93-94 (role: dev); dev blocks draw only from the dev files of their sources");
        w.WriteString("reserved", "bucket 95-99: never rendered, never trained or tuned on");
        w.WriteEndObject();
        w.WriteNumber("spanFailures", st.SpanFailures);
        st.WriteTo(w);
        w.WriteStartArray("inputs");
        foreach (string m in pools.InputManifests)
        {
            w.WriteStartObject();
            w.WriteString("manifest", m.Replace('\\', '/').Split('/')[^2] + "/manifest.json");
            w.WriteString("sha256", Sha(m));
            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteStartArray("lexicon");
        foreach (string f in new[] { "Lexicon.cs", "Kinds.cs", "Fields.cs" })
        {
            string src = Path.Combine(exeDir, "sources", f);
            if (File.Exists(src))
            {
                w.WriteStartObject();
                w.WriteString("file", f);
                w.WriteString("sha256", Sha(src));
                w.WriteEndObject();
            }
        }

        w.WriteEndArray();
        w.WriteStartArray("files");
        foreach ((string file, long lines, string sha) in files.OrderBy(f => f.File, StringComparer.Ordinal))
        {
            w.WriteStartObject();
            w.WriteString("file", file);
            w.WriteString("role", file.StartsWith("train/", StringComparison.Ordinal) ? "train" : "dev");
            w.WriteNumber("lines", lines);
            w.WriteString("sha256", sha);
            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteEndObject();
    }
}

internal sealed class ShardWriter : IDisposable
{
    private readonly string _dir;
    private StreamWriter? _w;
    private long _lines;
    private int _shard;
    private readonly List<(string, long)> _done = [];
    private string _current = "";

    internal ShardWriter(string dir)
    {
        _dir = dir;
        Directory.CreateDirectory(dir);
    }

    internal void Write(string line)
    {
        if (_w is null || _lines == 100_000)
        {
            Roll();
        }

        _w!.Write(line);
        _w.Write('\n');
        _lines++;
    }

    private void Roll()
    {
        if (_w is not null)
        {
            _w.Dispose();
            _done.Add((_current, _lines));
        }

        _current = Path.Combine(_dir, $"part-{_shard++:D5}.jsonl");
        _w = new StreamWriter(_current, false, new UTF8Encoding(false));
        _lines = 0;
    }

    public void Dispose() => _w?.Dispose();

    internal List<(string, long)> Close()
    {
        if (_w is not null)
        {
            _w.Dispose();
            _done.Add((_current, _lines));
            _w = null;
        }

        return _done;
    }
}

internal sealed class Stats
{
    internal long Reserved;
    internal long SpanFailures;
    internal string? FirstProblem;
    internal readonly SortedDictionary<string, long> Failures = new(StringComparer.Ordinal);
    internal readonly SortedDictionary<string, long> Blocks = new(StringComparer.Ordinal);
    internal readonly SortedDictionary<string, long> Examples = new(StringComparer.Ordinal);
    internal readonly SortedDictionary<string, long> Spans = new(StringComparer.Ordinal);

    internal void Count(string split, string kind, Loc loc, string view, Doc d)
    {
        Examples.Bump(split + "/all");
        Examples.Bump(split + "/kind/" + kind);
        Examples.Bump(split + "/loc/" + Program.LocName(loc));
        Examples.Bump(split + "/view/" + view);
        if (Kind.IsNegative(kind))
        {
            Examples.Bump(split + "/negative");
        }

        foreach (SpanRec s in d.Spans)
        {
            Spans.Bump(split + "/field/" + s.Label);
            Spans.Bump(split + "/field-by-loc/" + Program.LocName(loc) + "/" + s.Label);
            Spans.Bump(split + "/kind-field/" + kind + "/" + s.Label);
            if (s.Mode.Length > 0)
            {
                Spans.Bump(split + "/mode/" + s.Mode + "/" + s.Label);
            }
        }
    }

    internal void Merge(Stats o)
    {
        Reserved += o.Reserved;
        SpanFailures += o.SpanFailures;
        FirstProblem ??= o.FirstProblem;
        foreach (var kv in o.Failures) { Failures[kv.Key] = Failures.GetValueOrDefault(kv.Key) + kv.Value; }
        foreach (var kv in o.Blocks) { Blocks[kv.Key] = Blocks.GetValueOrDefault(kv.Key) + kv.Value; }
        foreach (var kv in o.Examples) { Examples[kv.Key] = Examples.GetValueOrDefault(kv.Key) + kv.Value; }
        foreach (var kv in o.Spans) { Spans[kv.Key] = Spans.GetValueOrDefault(kv.Key) + kv.Value; }
    }

    internal void WriteTo(Utf8JsonWriter w)
    {
        if (FirstProblem is not null)
        {
            w.WriteString("firstSpanProblem", FirstProblem);
        }

        foreach ((string name, SortedDictionary<string, long> d) in new[] { ("blocksByKind", Blocks), ("spanFailuresBy", Failures), ("examples", Examples), ("spans", Spans) })
        {
            w.WriteStartObject(name);
            foreach (var kv in d)
            {
                w.WriteNumber(kv.Key, kv.Value);
            }

            w.WriteEndObject();
        }
    }
}

internal static class DictExtensions
{
    internal static void Bump(this SortedDictionary<string, long> d, string key) => d[key] = d.GetValueOrDefault(key) + 1;
}
