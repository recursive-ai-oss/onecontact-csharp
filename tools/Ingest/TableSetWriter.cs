using System.Buffers.Binary;
using System.Text;

namespace RecursiveAI.OneContact.Ingest;

/// <summary>
/// Builds a compiled table file (format "OCDT" version 1, see docs/architecture.md): a set of named byte tables. The output is
/// deterministic: tables are written in ordinal name order and nothing else varies, so the same inputs give identical bytes.
/// </summary>
public sealed class TableSetWriter
{
    private readonly SortedDictionary<string, byte[]> _tables = new(StringComparer.Ordinal);

    /// <summary>Adds a table.</summary>
    /// <param name="name">One to 255 UTF-8 bytes, unique in the file.</param>
    /// <param name="data">The table bytes.</param>
    /// <exception cref="ArgumentException">The name is empty, too long or already used.</exception>
    public void Add(string name, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(data);
        int nameBytes = Encoding.UTF8.GetByteCount(name);
        if (nameBytes is 0 or > 255)
        {
            throw new ArgumentException("A table name is 1 to 255 UTF-8 bytes.", nameof(name));
        }

        if (!_tables.TryAdd(name, data))
        {
            throw new ArgumentException($"Table '{name}' was added twice.", nameof(name));
        }
    }

    /// <summary>Produces the file image.</summary>
    /// <returns>The bytes of the file.</returns>
    public byte[] ToArray()
    {
        int directory = 12;
        foreach (string name in _tables.Keys)
        {
            directory += 1 + Encoding.UTF8.GetByteCount(name) + 8;
        }

        long total = directory;
        foreach (byte[] data in _tables.Values)
        {
            total += data.Length;
        }

        if (total > int.MaxValue)
        {
            throw new InvalidOperationException("The tables are too large for one file.");
        }

        byte[] image = new byte[total];
        Span<byte> span = image;
        "OCDT"u8.CopyTo(span);
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(span[6..], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], (uint)_tables.Count);
        int pos = 12;
        int dataPos = directory;
        foreach ((string name, byte[] data) in _tables)
        {
            int n = Encoding.UTF8.GetBytes(name, span[(pos + 1)..]);
            span[pos] = (byte)n;
            pos += 1 + n;
            BinaryPrimitives.WriteUInt32LittleEndian(span[pos..], (uint)dataPos);
            BinaryPrimitives.WriteUInt32LittleEndian(span[(pos + 4)..], (uint)data.Length);
            pos += 8;
            data.CopyTo(span[dataPos..]);
            dataPos += data.Length;
        }

        return image;
    }
}
