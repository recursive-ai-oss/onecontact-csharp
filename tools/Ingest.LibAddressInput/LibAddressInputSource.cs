using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace RecursiveAI.OneContact.Ingest.LibAddressInput;

/// <summary>
/// Compiles libaddressinput's per-region address data (<c>countryinfo.txt</c>: one <c>key={json}</c> line per node of the
/// <c>data/COUNTRY/ADMIN_AREA/LOCALITY/DEPENDENT_LOCALITY[--language]</c> tree) into three tables. The format is documented in
/// docs/address-structural.md; in short:
/// <list type="bullet">
/// <item><c>props</c>: the property names (ordinal order) joined by a line feed; a property is referred to by its position.</item>
/// <item><c>strings</c>: every distinct value and node key once, in ordinal UTF-8 order: <c>u32 count</c>, <c>u32 offsets[count + 1]</c>, bytes.</item>
/// <item><c>nodes</c>: <c>u32 count</c>, <c>u32 offsets[count]</c> (from the end of the offset array), then one record per node in ordinal
/// key order: <c>varint key string, varint property count, (varint property, varint value string) *</c>.</item>
/// </list>
/// Only <c>data</c> and <c>data/...</c> lines are compiled. The <c>examples/...</c> lines (sample addresses for a few countries) are skipped.
/// </summary>
public sealed class LibAddressInputSource : IIngestSource
{
    private const string DataFile = "testdata/countryinfo.txt";
    private static readonly UTF8Encoding Utf8 = new(false, true);

    /// <inheritdoc/>
    public string Name => "libaddressinput";

    /// <inheritdoc/>
    public string ToolVersion => "1";

    /// <inheritdoc/>
    public string NoticeText =>
        """
        Address metadata from libaddressinput (https://github.com/google/libaddressinput), per-region address data
        (testdata/countryinfo.txt), © Google LLC and the libaddressinput contributors, used under the Apache License,
        Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0). The project's README also states that its data is
        available under the Creative Commons Attribution 4.0 International licence (https://creativecommons.org/licenses/by/4.0/);
        the data is credited here to satisfy both. The data is compiled into compact tables without changing its meaning.
        The upstream repository ships no separate NOTICE file.
        """;

    /// <inheritdoc/>
    public void Convert(IngestInput input, TableSetWriter tables)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(tables);
        string text = Utf8.GetString(input.ReadAllBytes(DataFile));

        SortedDictionary<string, SortedDictionary<string, string>> nodes = new(KeyComparer.Instance);
        SortedSet<string> propertyNames = new(StringComparer.Ordinal);
        int lineNumber = 0;
        foreach (string rawLine in text.Split('\n'))
        {
            lineNumber++;
            string line = rawLine.TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            int equals = line.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0)
            {
                throw new FormatException($"{DataFile} line {lineNumber}: no key.");
            }

            string key = line[..equals];
            if (key != "data" && !key.StartsWith("data/", StringComparison.Ordinal))
            {
                continue;
            }

            SortedDictionary<string, string> values = new(StringComparer.Ordinal);
            using JsonDocument document = JsonDocument.Parse(line[(equals + 1)..]);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException($"{DataFile} line {lineNumber}: the value is not an object.");
            }

            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                // "id" repeats the key; every other property of a data node is a string.
                if (property.Name == "id")
                {
                    if (property.Value.GetString() != key)
                    {
                        throw new FormatException($"{DataFile} line {lineNumber}: id differs from the key.");
                    }

                    continue;
                }

                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    throw new FormatException($"{DataFile} line {lineNumber}: property '{property.Name}' is not a string.");
                }

                values.Add(property.Name, property.Value.GetString()!);
                propertyNames.Add(property.Name);
            }

            if (!nodes.TryAdd(key, values))
            {
                throw new FormatException($"{DataFile} line {lineNumber}: duplicate key '{key}'.");
            }
        }

        if (nodes.Count == 0 || !nodes.ContainsKey("data"))
        {
            throw new FormatException($"{DataFile} has no data root.");
        }

        List<string> properties = [.. propertyNames];
        Dictionary<string, int> propertyIndex = new(StringComparer.Ordinal);
        for (int i = 0; i < properties.Count; i++)
        {
            propertyIndex[properties[i]] = i;
        }

        SortedSet<string> pool = new(KeyComparer.Instance);
        foreach ((string key, SortedDictionary<string, string> values) in nodes)
        {
            pool.Add(key);
            foreach (string value in values.Values)
            {
                pool.Add(value);
            }
        }

        List<string> strings = [.. pool];
        Dictionary<string, int> stringIndex = new(StringComparer.Ordinal);
        for (int i = 0; i < strings.Count; i++)
        {
            stringIndex[strings[i]] = i;
        }

        tables.Add("props", Utf8.GetBytes(string.Join('\n', properties)));
        tables.Add("strings", WriteStrings(strings));
        tables.Add("nodes", WriteNodes(nodes, propertyIndex, stringIndex));
    }

    private static byte[] WriteStrings(List<string> strings)
    {
        List<byte[]> encoded = [.. strings.Select(s => Utf8.GetBytes(s))];
        int total = 4 + ((encoded.Count + 1) * 4) + encoded.Sum(e => e.Length);
        byte[] table = new byte[total];
        BinaryPrimitives.WriteUInt32LittleEndian(table, (uint)encoded.Count);
        uint offset = 0;
        int pos = 4;
        int dataStart = 4 + ((encoded.Count + 1) * 4);
        for (int i = 0; i < encoded.Count; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(pos), offset);
            pos += 4;
            encoded[i].CopyTo(table.AsSpan(dataStart + (int)offset));
            offset += (uint)encoded[i].Length;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(pos), offset);
        return table;
    }

    private static byte[] WriteNodes(
        SortedDictionary<string, SortedDictionary<string, string>> nodes,
        Dictionary<string, int> propertyIndex,
        Dictionary<string, int> stringIndex)
    {
        using MemoryStream records = new();
        List<uint> offsets = [];
        foreach ((string key, SortedDictionary<string, string> values) in nodes)
        {
            offsets.Add((uint)records.Length);
            WriteVarint(records, stringIndex[key]);
            WriteVarint(records, values.Count);
            foreach ((string name, string value) in values.OrderBy(p => propertyIndex[p.Key]))
            {
                WriteVarint(records, propertyIndex[name]);
                WriteVarint(records, stringIndex[value]);
            }
        }

        byte[] table = new byte[4 + (offsets.Count * 4) + (int)records.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(table, (uint)offsets.Count);
        for (int i = 0; i < offsets.Count; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(4 + (i * 4)), offsets[i]);
        }

        records.ToArray().CopyTo(table, 4 + (offsets.Count * 4));
        return table;
    }

    private static void WriteVarint(Stream stream, int value)
    {
        uint v = (uint)value;
        while (v >= 0x80)
        {
            stream.WriteByte((byte)(v | 0x80));
            v >>= 7;
        }

        stream.WriteByte((byte)v);
    }

    /// <summary>Orders strings by their UTF-8 bytes, which is the order the runtime's binary search uses.</summary>
    private sealed class KeyComparer : IComparer<string>
    {
        public static readonly KeyComparer Instance = new();

        public int Compare(string? x, string? y) =>
            Utf8.GetBytes(x!).AsSpan().SequenceCompareTo(Utf8.GetBytes(y!));
    }
}
