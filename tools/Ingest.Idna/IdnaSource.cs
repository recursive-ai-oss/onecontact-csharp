using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace RecursiveAI.OneContact.Ingest.Idna;

/// <summary>
/// Compiles the Unicode files behind UTS #46 (the IDNA mapping table, and the properties normalization and the label validity
/// criteria need) into the tables <c>Text/Idna</c> reads. The layouts are written down in docs/idna.md.
/// </summary>
public sealed class IdnaSource : IIngestSource
{
    // Order is part of the table format: the runtime enums use the same numbers.
    internal static readonly string[] BidiClasses = ["L", "R", "AL", "EN", "ES", "ET", "AN", "CS", "NSM", "BN", "B", "S", "WS", "ON", "LRE", "LRO", "RLE", "RLO", "PDF", "LRI", "RLI", "FSI", "PDI"];
    internal static readonly string[] JoiningTypes = ["U", "L", "R", "D", "C", "T"];
    internal static readonly string[] Statuses = ["valid", "ignored", "mapped", "deviation", "disallowed"];

    /// <inheritdoc/>
    public string Name => "unicode-idna";

    /// <inheritdoc/>
    public string ToolVersion => "1";

    /// <inheritdoc/>
    public string NoticeText =>
        "Unicode Character Database 18.0.0 data files (IdnaMappingTable.txt, UnicodeData.txt, DerivedNormalizationProps.txt, DerivedBidiClass.txt, DerivedJoiningType.txt).\n" +
        "Copyright © 1991-2026 Unicode, Inc. Distributed under the Unicode License v3 (https://www.unicode.org/license.txt); the licence text is in upstream/unicode-idna/LICENSE.\n";

    /// <inheritdoc/>
    public void Convert(IngestInput input, TableSetWriter tables)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(tables);
        BuildMapping(Text(input, "idna/IdnaMappingTable.txt"), tables);
        BuildUnicodeData(Text(input, "ucd/UnicodeData.txt"), Text(input, "ucd/DerivedNormalizationProps.txt"), tables);
        tables.Add("bidi", Ranges(ReadPropertyFile(Text(input, "ucd/extracted/DerivedBidiClass.txt"), BidiClasses, 255)));
        tables.Add("joining", Ranges(ReadPropertyFile(Text(input, "ucd/extracted/DerivedJoiningType.txt"), JoiningTypes, 0), 0));
    }

    private static string Text(IngestInput input, string path) => Encoding.UTF8.GetString(input.ReadAllBytes(path));

    private static string StripComment(string line)
    {
        int hash = line.IndexOf('#', StringComparison.Ordinal);
        return (hash >= 0 ? line[..hash] : line).Trim();
    }

    private static (int First, int Last) CodeRange(string field)
    {
        int dots = field.IndexOf("..", StringComparison.Ordinal);
        if (dots < 0)
        {
            int one = int.Parse(field.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return (one, one);
        }

        return (
            int.Parse(field[..dots].Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            int.Parse(field[(dots + 2)..].Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    // idna.ranges: 12-byte entries (first u32, last u32, info u32) sorted by first code point, where
    // info = status | length << 3 | offset << 11, and the mapping is `length` UTF-16 units at `offset` in idna.map.
    private static void BuildMapping(string text, TableSetWriter tables)
    {
        var map = new List<char>();
        var offsets = new Dictionary<string, int>(StringComparer.Ordinal);
        var entries = new List<(int First, int Last, uint Info)>();
        foreach (string raw in text.Split('\n'))
        {
            string line = StripComment(raw);
            if (line.Length == 0)
            {
                continue;
            }

            string[] fields = line.Split(';');
            (int first, int last) = CodeRange(fields[0]);
            int status = Array.IndexOf(Statuses, fields[1].Trim());
            if (status < 0)
            {
                throw new InvalidDataException("Unknown IDNA status in: " + raw);
            }

            string mapped = "";
            if (fields.Length > 2 && fields[2].Trim().Length > 0)
            {
                var sb = new StringBuilder();
                foreach (string cp in fields[2].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    sb.Append(char.ConvertFromUtf32(int.Parse(cp, NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
                }

                mapped = sb.ToString();
            }

            int offset = 0;
            if (mapped.Length > 0)
            {
                if (!offsets.TryGetValue(mapped, out offset))
                {
                    offset = map.Count;
                    offsets[mapped] = offset;
                    map.AddRange(mapped);
                }
            }

            if (mapped.Length > 255 || offset >= (1 << 21))
            {
                throw new InvalidDataException("IDNA mapping does not fit the table layout.");
            }

            entries.Add((first, last, (uint)status | ((uint)mapped.Length << 3) | ((uint)offset << 11)));
        }

        entries.Sort((a, b) => a.First.CompareTo(b.First));
        byte[] ranges = new byte[entries.Count * 12];
        for (int i = 0; i < entries.Count; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(ranges.AsSpan(i * 12), (uint)entries[i].First);
            BinaryPrimitives.WriteUInt32LittleEndian(ranges.AsSpan((i * 12) + 4), (uint)entries[i].Last);
            BinaryPrimitives.WriteUInt32LittleEndian(ranges.AsSpan((i * 12) + 8), entries[i].Info);
        }

        byte[] mapBytes = new byte[map.Count * 2];
        for (int i = 0; i < map.Count; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(mapBytes.AsSpan(i * 2), map[i]);
        }

        tables.Add("idna.ranges", ranges);
        tables.Add("idna.map", mapBytes);
    }

    // ccc, mark and compexcl: 8-byte entries (first u32, last u32 | value << 24); decomp: 12-byte entries (code point, first, second)
    // of canonical decompositions of one or two code points (second is 0 for a singleton), sorted by code point.
    private static void BuildUnicodeData(string unicodeData, string normProps, TableSetWriter tables)
    {
        byte[] ccc = new byte[0x110000];
        byte[] mark = new byte[0x110000];
        var decomp = new List<(int Cp, int A, int B)>();
        int rangeStart = -1;
        foreach (string line in unicodeData.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] f = line.Split(';');
            int cp = int.Parse(f[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (f[1].EndsWith(", First>", StringComparison.Ordinal))
            {
                rangeStart = cp;
                continue;
            }

            int first = cp;
            if (f[1].EndsWith(", Last>", StringComparison.Ordinal))
            {
                first = rangeStart;
            }

            byte combining = byte.Parse(f[3], NumberStyles.None, CultureInfo.InvariantCulture);
            bool isMark = f[2].StartsWith('M');
            for (int c = first; c <= cp; c++)
            {
                ccc[c] = combining;
                mark[c] = isMark ? (byte)1 : (byte)0;
            }

            // A decomposition without a <tag> is canonical (UAX #44, UnicodeData field 5).
            if (f[5].Length > 0 && f[5][0] != '<')
            {
                string[] parts = f[5].Split(' ');
                if (parts.Length is < 1 or > 2)
                {
                    throw new InvalidDataException("Unexpected canonical decomposition length for " + f[0]);
                }

                decomp.Add((cp, int.Parse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture), parts.Length == 2 ? int.Parse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture) : 0));
            }
        }

        byte[] excl = new byte[0x110000];
        foreach (string raw in normProps.Split('\n'))
        {
            string line = StripComment(raw);
            string[] f = line.Split(';');
            if (f.Length == 2 && f[1].Trim() == "Full_Composition_Exclusion")
            {
                (int a, int b) = CodeRange(f[0]);
                for (int c = a; c <= b; c++)
                {
                    excl[c] = 1;
                }
            }
        }

        decomp.Sort((x, y) => x.Cp.CompareTo(y.Cp));
        byte[] d = new byte[decomp.Count * 12];
        for (int i = 0; i < decomp.Count; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(i * 12), (uint)decomp[i].Cp);
            BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan((i * 12) + 4), (uint)decomp[i].A);
            BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan((i * 12) + 8), (uint)decomp[i].B);
        }

        tables.Add("ccc", Ranges(ccc, 0));
        tables.Add("mark", Ranges(mark, 0));
        tables.Add("compexcl", Ranges(excl, 0));
        tables.Add("decomp", d);
    }

    // A file of "range ; value # comment" lines, with the value names in `names`. Code points not listed get `missing`.
    private static byte[] ReadPropertyFile(string text, string[] names, byte missing)
    {
        byte[] values = new byte[0x110000];
        Array.Fill(values, missing);
        foreach (string raw in text.Split('\n'))
        {
            string line = StripComment(raw);
            if (line.Length == 0)
            {
                continue;
            }

            string[] f = line.Split(';');
            int value = Array.IndexOf(names, f[1].Trim());
            if (value < 0)
            {
                throw new InvalidDataException("Unknown property value in: " + raw);
            }

            (int a, int b) = CodeRange(f[0]);
            for (int c = a; c <= b; c++)
            {
                values[c] = (byte)value;
            }
        }

        return values;
    }

    // Run-length encodes a per-code-point value array into 8-byte entries, leaving out the `skip` value.
    private static byte[] Ranges(byte[] values) => Ranges(values, 255);

    private static byte[] Ranges(byte[] values, byte skip)
    {
        var output = new List<byte>();
        byte[] entry = new byte[8];
        int i = 0;
        while (i < values.Length)
        {
            byte v = values[i];
            int j = i;
            while (j + 1 < values.Length && values[j + 1] == v)
            {
                j++;
            }

            if (v != skip)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(entry, (uint)i);
                BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(4), (uint)j | ((uint)v << 24));
                output.AddRange(entry);
            }

            i = j + 1;
        }

        return [.. output];
    }
}
