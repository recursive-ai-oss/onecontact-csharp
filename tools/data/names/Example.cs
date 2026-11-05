using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace OneContact.Data.Names;

/// <summary>One labelled span: a label, UTF-16 offsets into the text, and for several-people strings the people (1-based) it belongs to.</summary>
internal sealed record Part(string Label, int Start, int End, int[]? Persons);

/// <summary>One training example: a string, its class, and the spans of its parts.</summary>
internal sealed class Example
{
    internal string Id = "";
    internal string Text = "";
    internal string Class = "";
    internal string Pattern = "";
    internal string Noise = "";
    internal string Lang = "";
    internal string Region = "";
    internal string Source = "";
    internal string? Elf;
    internal string Split = "train";
    internal List<Part> Parts = [];
}

/// <summary>Builds a string and its part spans together, so the offsets are right by construction.</summary>
internal sealed class Builder
{
    private readonly StringBuilder _text = new();
    private readonly List<Part> _parts = [];

    internal int Position => _text.Length;

    /// <summary>Appends unlabelled text (separators, connectors, markers).</summary>
    internal Builder Raw(string s)
    {
        _text.Append(s);
        return this;
    }

    /// <summary>Appends text and labels it.</summary>
    internal Builder Add(string label, string s, params int[] persons)
    {
        int start = _text.Length;
        _text.Append(s);
        _parts.Add(new Part(label, start, _text.Length, persons.Length == 0 ? null : persons));
        return this;
    }

    // Words that open a family name and are labelled as a particle apart from the rest ("Le Gall" gives Particle "Le", Family "Gall"), the way
    // the rule parser reads them. Longest first.
    private static readonly string[] LeadingParticles =
        ["de la", "de los", "de las", "van der", "van den", "le", "la", "les", "de", "du", "des", "van", "von", "der", "den", "del", "della", "di", "da", "das", "dos"];

    /// <summary>The length of the particle that opens <paramref name="family"/> (followed by a space), or 0.</summary>
    internal static int ParticleLength(string family)
    {
        foreach (string p in LeadingParticles)
        {
            if (family.Length > p.Length + 1 && family[p.Length] == ' ' && family.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            {
                return p.Length;
            }
        }

        return 0;
    }

    /// <summary>Appends a family name, labelling a leading particle apart from the rest.</summary>
    internal Builder Family(string family, params int[] persons)
    {
        int cut = ParticleLength(family);
        if (cut == 0)
        {
            return Add("Family", family, persons);
        }

        Add("Particle", family[..cut], persons);
        Raw(" ");
        return Add("Family", family[(cut + 1)..], persons);
    }

    /// <summary>Labels the text appended since <paramref name="start"/>.</summary>
    internal Builder Label(string label, int start, params int[] persons)
    {
        _parts.Add(new Part(label, start, _text.Length, persons.Length == 0 ? null : persons));
        return this;
    }

    internal string Text => _text.ToString();

    internal List<Part> Parts => _parts;
}

/// <summary>Writes examples as JSON Lines into numbered shards per split, counting what it writes.</summary>
internal sealed class ExampleWriter : IDisposable
{
    private const int ShardLines = 250_000;

    private static readonly JsonWriterOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, SkipValidation = true };

    private readonly string _root;
    private readonly Dictionary<string, (FileStream Stream, int Lines, int Shard)> _open = new(StringComparer.Ordinal);
    private readonly ArrayBufferWriter<byte> _buffer = new(1024);
    private readonly Utf8JsonWriter _json;

    internal ExampleWriter(string root)
    {
        _root = root;
        _json = new Utf8JsonWriter(_buffer, Options);
    }

    /// <summary>Counts per split, class and pattern.</summary>
    internal SortedDictionary<string, long> Counts { get; } = new(StringComparer.Ordinal);

    /// <summary>Hashes of the folded text of every train and dev example: lets evaluation builders drop strings that training has seen.</summary>
    internal List<ulong> TextHashes { get; } = [];

    internal void Write(Example e)
    {
        _buffer.Clear();
        _json.Reset(_buffer);
        _json.WriteStartObject();
        _json.WriteString("id", e.Id);
        _json.WriteString("text", e.Text);
        _json.WriteString("class", e.Class);
        _json.WriteString("pattern", e.Pattern);
        if (e.Noise.Length > 0)
        {
            _json.WriteString("noise", e.Noise);
        }

        _json.WriteString("lang", e.Lang);
        _json.WriteString("region", e.Region);
        _json.WriteString("src", e.Source);
        if (e.Elf != null)
        {
            _json.WriteString("elf", e.Elf);
        }

        _json.WriteStartArray("parts");
        foreach (Part p in e.Parts)
        {
            _json.WriteStartArray();
            _json.WriteStringValue(p.Label);
            _json.WriteNumberValue(p.Start);
            _json.WriteNumberValue(p.End);
            if (p.Persons != null)
            {
                _json.WriteStartArray();
                foreach (int who in p.Persons)
                {
                    _json.WriteNumberValue(who);
                }

                _json.WriteEndArray();
            }

            _json.WriteEndArray();
        }

        _json.WriteEndArray();
        _json.WriteEndObject();
        _json.Flush();

        if (!_open.TryGetValue(e.Split, out (FileStream Stream, int Lines, int Shard) cur) || cur.Lines >= ShardLines)
        {
            int shard = cur.Stream == null ? 0 : cur.Shard + 1;
            cur.Stream?.Dispose();
            string dir = Path.Combine(_root, e.Split);
            Directory.CreateDirectory(dir);
            cur = (new FileStream(Path.Combine(dir, $"part-{shard:D5}.jsonl"), FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20), 0, shard);
        }

        cur.Stream.Write(_buffer.WrittenSpan);
        cur.Stream.WriteByte((byte)'\n');
        _open[e.Split] = (cur.Stream, cur.Lines + 1, cur.Shard);

        string key = $"{e.Split}|{e.Class}|{e.Pattern}";
        string noiseKey = $"{e.Split}|noise|{(e.Noise.Length == 0 ? "none" : e.Noise)}";
        Counts[noiseKey] = Counts.GetValueOrDefault(noiseKey) + 1;
        Counts[key] = Counts.GetValueOrDefault(key) + 1;
        TextHashes.Add(Txt.Hash64(Txt.Fold(e.Text)));
    }

    public void Dispose()
    {
        foreach ((FileStream stream, int _, int _) in _open.Values)
        {
            stream.Dispose();
        }

        _json.Dispose();
    }
}
