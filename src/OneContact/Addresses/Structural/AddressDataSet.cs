using System.Buffers.Binary;
using System.Text;
using RecursiveAI.OneContact.Data;

namespace RecursiveAI.OneContact.Addresses.Structural;

/// <summary>
/// The compiled address data of every region: the tree of nodes (<c>data</c>, <c>data/US</c>, <c>data/US/CA</c>, <c>data/CA--fr</c>
/// and so on) with their properties. The three tables are described in docs/address-structural.md. Reading checks every offset
/// and count, so a truncated or hostile table fails with <see cref="InvalidDataException"/> and never reads out of range.
/// </summary>
internal sealed class AddressDataSet
{
    private const int MaxVarintBytes = 5;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly Lazy<AddressDataSet> EmbeddedSet = new(
        () => Load(new EmbeddedTables(typeof(AddressDataSet).Assembly, "OneContact.Data.libaddressinput.ocd")),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly ReadOnlyMemory<byte> _strings;
    private readonly ReadOnlyMemory<byte> _nodes;
    private readonly int _stringCount;
    private readonly int _nodeCount;
    private readonly int[] _propertySlot;
    private readonly int _propertyCount;
    private readonly string?[] _stringCache;
    private readonly AddressNode?[] _nodeCache;
    private readonly Lazy<HashSet<string>> _countries;

    private AddressDataSet(ReadOnlyMemory<byte> strings, ReadOnlyMemory<byte> nodes, int stringCount, int nodeCount, string[] propertyNames)
    {
        _strings = strings;
        _nodes = nodes;
        _stringCount = stringCount;
        _nodeCount = nodeCount;
        _propertyCount = propertyNames.Length;
        _propertySlot = new int[AddressNode.PropertyNames.Length];
        Array.Fill(_propertySlot, -1);
        for (int i = 0; i < propertyNames.Length; i++)
        {
            int known = Array.IndexOf(AddressNode.PropertyNames, propertyNames[i]);
            if (known >= 0)
            {
                _propertySlot[known] = i;
            }
        }

        _countries = new Lazy<HashSet<string>>(
            () => Find("data")?[NodeProperty.Countries] is { } list ? new HashSet<string>(list.Split('~'), StringComparer.Ordinal) : [],
            LazyThreadSafetyMode.ExecutionAndPublication);
        _stringCache = new string?[stringCount];
        _nodeCache = new AddressNode?[nodeCount];
    }

    /// <summary>Gets the data compiled into this assembly.</summary>
    internal static AddressDataSet Embedded => EmbeddedSet.Value;

    /// <summary>Gets the codes of the countries the data describes (the <c>countries</c> list of the root node), in upper case.</summary>
    internal HashSet<string> Countries => _countries.Value;

    /// <summary>Gets the number of nodes.</summary>
    internal int NodeCount => _nodeCount;

    /// <summary>Reads the tables of the embedded data file or any other table source.</summary>
    /// <param name="tables">The source.</param>
    /// <returns>The data set.</returns>
    /// <exception cref="InvalidDataException">A table is missing or damaged.</exception>
    internal static AddressDataSet Load(EmbeddedTables tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        return Load(tables.GetTable("props"), tables.GetTable("strings"), tables.GetTable("nodes"));
    }

    /// <summary>Reads and validates the three tables.</summary>
    /// <param name="props">The property names, joined by a line feed.</param>
    /// <param name="strings">The string pool.</param>
    /// <param name="nodes">The node records.</param>
    /// <returns>The data set.</returns>
    /// <exception cref="InvalidDataException">A table is damaged.</exception>
    internal static AddressDataSet Load(ReadOnlyMemory<byte> props, ReadOnlyMemory<byte> strings, ReadOnlyMemory<byte> nodes)
    {
        string[] names;
        try
        {
            string text = Utf8.GetString(props.Span);
            names = text.Length == 0 ? [] : text.Split('\n');
        }
        catch (ArgumentException e)
        {
            throw new InvalidDataException("The address property table is not valid UTF-8.", e);
        }

        if (names.Length > 1024)
        {
            throw new InvalidDataException("The address property table is implausibly long.");
        }

        ReadOnlySpan<byte> pool = strings.Span;
        if (pool.Length < 8)
        {
            throw new InvalidDataException("The address string table is truncated.");
        }

        uint stringCount = BinaryPrimitives.ReadUInt32LittleEndian(pool);
        long headerEnd = 4L + ((stringCount + 1L) * 4L);
        if (headerEnd > pool.Length)
        {
            throw new InvalidDataException("The address string table is truncated.");
        }

        long dataLength = pool.Length - headerEnd;
        uint previous = 0;
        for (long i = 0; i <= stringCount; i++)
        {
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(pool[(int)(4 + (i * 4))..]);
            if (offset < previous || offset > dataLength || (i == 0 && offset != 0))
            {
                throw new InvalidDataException("The address string offsets are not increasing and inside the table.");
            }

            previous = offset;
        }

        if (previous != dataLength)
        {
            throw new InvalidDataException("The address string table has trailing bytes.");
        }

        ReadOnlySpan<byte> nodeSpan = nodes.Span;
        if (nodeSpan.Length < 4)
        {
            throw new InvalidDataException("The address node table is truncated.");
        }

        uint nodeCount = BinaryPrimitives.ReadUInt32LittleEndian(nodeSpan);
        long nodeHeaderEnd = 4L + (nodeCount * 4L);
        if (nodeHeaderEnd > nodeSpan.Length)
        {
            throw new InvalidDataException("The address node table is truncated.");
        }

        long recordBytes = nodeSpan.Length - nodeHeaderEnd;
        uint lastOffset = 0;
        for (long i = 0; i < nodeCount; i++)
        {
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(nodeSpan[(int)(4 + (i * 4))..]);
            if ((i == 0 && offset != 0) || offset < lastOffset || offset >= recordBytes)
            {
                throw new InvalidDataException("The address node offsets are not increasing and inside the table.");
            }

            lastOffset = offset;
        }

        // Cheap to do once and it makes every later lookup safe: every string is valid UTF-8, every record parses and refers to
        // existing strings and properties, and the keys are in the order the binary search needs.
        AddressDataSet set = new(strings, nodes, (int)stringCount, (int)nodeCount, names);
        try
        {
            for (int i = 0; i < set._stringCount; i++)
            {
                _ = Utf8.GetCharCount(set.GetString(i).Span);
            }
        }
        catch (ArgumentException e)
        {
            throw new InvalidDataException("An address string is not valid UTF-8.", e);
        }

        for (int i = 0; i < set._nodeCount; i++)
        {
            set.CheckRecord(i);
            if (i > 0 && set.GetString(set.KeyStringIndex(i - 1)).Span.SequenceCompareTo(set.GetString(set.KeyStringIndex(i)).Span) >= 0)
            {
                throw new InvalidDataException("The address node keys are not in order.");
            }
        }

        return set;
    }

    /// <summary>Finds the node with this exact key.</summary>
    /// <param name="key">The key such as <c>data/US/CA</c> or <c>data/CA--fr</c>.</param>
    /// <returns>The node, or <see langword="null"/> when there is none.</returns>
    internal AddressNode? Find(string key)
    {
        // Every stored key is well-formed UTF-8, so a key with a lone surrogate matches none and cannot be encoded.
        if (!RecursiveAI.OneContact.Text.Utf16Text.IsWellFormed(key))
        {
            return null;
        }

        byte[] wanted = Utf8.GetBytes(key);
        int low = 0;
        int high = _nodeCount - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            int order = GetString(KeyStringIndex(middle)).Span.SequenceCompareTo(wanted);
            if (order == 0)
            {
                return GetNode(middle);
            }

            if (order < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return null;
    }

    /// <summary>Enumerates every node key in table order.</summary>
    /// <returns>The keys.</returns>
    internal IEnumerable<string> Keys()
    {
        for (int i = 0; i < _nodeCount; i++)
        {
            yield return GetStringText(KeyStringIndex(i));
        }
    }

    private AddressNode GetNode(int index)
    {
        AddressNode? cached = _nodeCache[index];
        if (cached is not null)
        {
            return cached;
        }

        ReadOnlySpan<byte> records = _nodes.Span;
        int pos = RecordStart(index);
        string key = GetStringText(ReadVarint(records, ref pos));
        int count = ReadVarint(records, ref pos);
        string?[] values = new string?[AddressNode.PropertyNames.Length];
        string?[] bySlot = new string?[_propertyCount];
        for (int i = 0; i < count; i++)
        {
            int property = ReadVarint(records, ref pos);
            int value = ReadVarint(records, ref pos);
            bySlot[property] = GetStringText(value);
        }

        for (int known = 0; known < values.Length; known++)
        {
            int slot = _propertySlot[known];
            values[known] = slot >= 0 ? bySlot[slot] : null;
        }

        AddressNode node = new(key, values);
        _nodeCache[index] = node;
        return node;
    }

    private void CheckRecord(int index)
    {
        ReadOnlySpan<byte> records = _nodes.Span;
        int pos = RecordStart(index);
        int end = index + 1 < _nodeCount ? RecordStart(index + 1) : records.Length;
        int keyIndex = ReadVarint(records, ref pos);
        if (keyIndex >= _stringCount)
        {
            throw new InvalidDataException("An address node names a string that does not exist.");
        }

        int count = ReadVarint(records, ref pos);
        if (count > _propertyCount)
        {
            throw new InvalidDataException("An address node has more properties than exist.");
        }

        for (int i = 0; i < count; i++)
        {
            int property = ReadVarint(records, ref pos);
            int value = ReadVarint(records, ref pos);
            if (property >= _propertyCount || value >= _stringCount)
            {
                throw new InvalidDataException("An address node refers to a property or string that does not exist.");
            }
        }

        if (pos != end)
        {
            throw new InvalidDataException("An address node record has the wrong length.");
        }
    }

    private int RecordStart(int index)
    {
        ReadOnlySpan<byte> nodes = _nodes.Span;
        uint offset = BinaryPrimitives.ReadUInt32LittleEndian(nodes[(4 + (index * 4))..]);
        return 4 + (_nodeCount * 4) + (int)offset;
    }

    private int KeyStringIndex(int node)
    {
        ReadOnlySpan<byte> records = _nodes.Span;
        int pos = RecordStart(node);
        int index = ReadVarint(records, ref pos);
        if (index >= _stringCount)
        {
            throw new InvalidDataException("An address node names a string that does not exist.");
        }

        return index;
    }

    private static int ReadVarint(ReadOnlySpan<byte> data, ref int pos)
    {
        uint value = 0;
        for (int shift = 0, n = 0; n < MaxVarintBytes; shift += 7, n++)
        {
            if (pos >= data.Length)
            {
                throw new InvalidDataException("An address record is truncated.");
            }

            byte b = data[pos++];
            value |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                if (value > int.MaxValue)
                {
                    throw new InvalidDataException("An address record holds a number that is too large.");
                }

                return (int)value;
            }
        }

        throw new InvalidDataException("An address record holds a number that is too long.");
    }

    private ReadOnlyMemory<byte> GetString(int index)
    {
        ReadOnlySpan<byte> pool = _strings.Span;
        int offsetsAt = 4 + (index * 4);
        int start = (int)BinaryPrimitives.ReadUInt32LittleEndian(pool[offsetsAt..]);
        int end = (int)BinaryPrimitives.ReadUInt32LittleEndian(pool[(offsetsAt + 4)..]);
        int dataStart = 4 + ((_stringCount + 1) * 4);
        return _strings.Slice(dataStart + start, end - start);
    }

    private string GetStringText(int index)
    {
        string? cached = _stringCache[index];
        if (cached is not null)
        {
            return cached;
        }

        string text;
        try
        {
            text = Utf8.GetString(GetString(index).Span);
        }
        catch (ArgumentException e)
        {
            throw new InvalidDataException("An address string is not valid UTF-8.", e);
        }

        _stringCache[index] = text;
        return text;
    }
}
