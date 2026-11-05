using System.Buffers.Binary;
using System.Text;

namespace RecursiveAI.OneContact.Data;

/// <summary>
/// A set of named byte tables produced at build time by an ingest tool (format "OCDT", version 1, described in
/// <c>docs/architecture.md</c>). Reading validates the whole directory up front, so a damaged or hostile file fails with
/// <see cref="InvalidDataException"/> before any table is handed out; tables are slices of the one buffer, not copies.
/// </summary>
internal sealed class CompiledTableFile
{
    internal const int FormatVersion = 1;
    private const int HeaderLength = 12;
    private const int MaxTables = 4096;

    private readonly Dictionary<string, (int Offset, int Length)> _tables;
    private readonly ReadOnlyMemory<byte> _data;

    private CompiledTableFile(ReadOnlyMemory<byte> data, Dictionary<string, (int Offset, int Length)> tables)
    {
        _data = data;
        _tables = tables;
    }

    /// <summary>Gets the table names, in directory order.</summary>
    internal IEnumerable<string> Names => _tables.Keys;

    /// <summary>Reads and validates a file image.</summary>
    /// <param name="data">The whole file.</param>
    /// <returns>The table set.</returns>
    /// <exception cref="InvalidDataException">The image is not a version-1 table file, or its directory points outside it.</exception>
    internal static CompiledTableFile Read(ReadOnlyMemory<byte> data)
    {
        ReadOnlySpan<byte> span = data.Span;
        if (span.Length < HeaderLength || !span[..4].SequenceEqual("OCDT"u8))
        {
            throw new InvalidDataException("Not a compiled table file.");
        }

        int version = BinaryPrimitives.ReadUInt16LittleEndian(span[4..]);
        if (version != FormatVersion)
        {
            throw new InvalidDataException($"Compiled table file version {version} is not supported.");
        }

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(span[8..]);
        if (count > MaxTables)
        {
            throw new InvalidDataException("The table count is implausible.");
        }

        Dictionary<string, (int Offset, int Length)> tables = new((int)count, StringComparer.Ordinal);
        int pos = HeaderLength;
        for (uint i = 0; i < count; i++)
        {
            if (pos >= span.Length)
            {
                throw new InvalidDataException("The table directory is truncated.");
            }

            int nameLength = span[pos++];
            if (nameLength == 0 || pos + nameLength + 8 > span.Length)
            {
                throw new InvalidDataException("The table directory is truncated.");
            }

            string name = Encoding.UTF8.GetString(span.Slice(pos, nameLength));
            pos += nameLength;
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(span[pos..]);
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(span[(pos + 4)..]);
            pos += 8;
            if (offset > span.Length || length > span.Length - offset)
            {
                throw new InvalidDataException($"Table '{name}' lies outside the file.");
            }

            if (!tables.TryAdd(name, ((int)offset, (int)length)))
            {
                throw new InvalidDataException($"Table '{name}' appears twice.");
            }
        }

        return new CompiledTableFile(data, tables);
    }

    /// <summary>Finds a table by name.</summary>
    /// <param name="name">The table name (ordinal comparison).</param>
    /// <param name="table">The table bytes, a slice of the file image.</param>
    /// <returns><see langword="true"/> when the table exists.</returns>
    internal bool TryGetTable(string name, out ReadOnlyMemory<byte> table)
    {
        if (_tables.TryGetValue(name, out (int Offset, int Length) entry))
        {
            table = _data.Slice(entry.Offset, entry.Length);
            return true;
        }

        table = default;
        return false;
    }
}
