using System.Buffers.Binary;
using System.Text;

namespace RecursiveAI.OneContact.Phones.Geo.Data;

/// <summary>
/// A read-only map from digit-string prefixes to descriptions, in the compact layout written by the ingest tool (documented in
/// <c>docs/phones-geo.md</c>): a table of distinct descriptions and a front-coded run of sorted keys cut into blocks of
/// <see cref="BlockSize"/> entries that each start with a full key, so a lookup decodes one block and nothing else.
/// </summary>
/// <remarks>
/// Opening a map checks the header and the offset arrays against the table length. Entries are decoded on lookup and a damaged entry
/// stream throws <see cref="InvalidDataException"/> there; <see cref="Validate"/> decodes everything up front.
/// </remarks>
internal readonly struct PrefixMap
{
    /// <summary>The number of entries in a block; also the writer's block size.</summary>
    internal const int BlockSize = 64;

    /// <summary>The longest key the layout allows, in digits.</summary>
    internal const int MaxKeyLength = 32;

    private const int HeaderLength = 16;

    private readonly ReadOnlyMemory<byte> _data;
    private readonly int _entryCount;
    private readonly int _descriptionCount;
    private readonly int _blockCount;
    private readonly int _maxKey;
    private readonly int _descOffsetsAt;
    private readonly int _descBytesAt;
    private readonly int _blockOffsetsAt;
    private readonly int _streamAt;

    /// <summary>Opens a map over a table image.</summary>
    /// <param name="data">The table bytes.</param>
    /// <exception cref="InvalidDataException">The header or an offset array does not fit the table.</exception>
    internal PrefixMap(ReadOnlyMemory<byte> data)
    {
        ReadOnlySpan<byte> span = data.Span;
        if (span.Length < HeaderLength)
        {
            throw new InvalidDataException("The prefix table is truncated.");
        }

        uint entries = BinaryPrimitives.ReadUInt32LittleEndian(span);
        uint descriptions = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
        uint blocks = BinaryPrimitives.ReadUInt32LittleEndian(span[8..]);
        uint maxKey = BinaryPrimitives.ReadUInt32LittleEndian(span[12..]);
        if (maxKey > MaxKeyLength || entries > span.Length || descriptions > span.Length || blocks != (entries + BlockSize - 1) / BlockSize)
        {
            throw new InvalidDataException("The prefix table header is implausible.");
        }

        long descOffsetsAt = HeaderLength;
        long descBytesAt = descOffsetsAt + ((long)(descriptions + 1) * 4);
        if (descBytesAt > span.Length)
        {
            throw new InvalidDataException("The prefix table is truncated.");
        }

        // The description offsets are relative to the description bytes; the last one is their total length.
        uint total = BinaryPrimitives.ReadUInt32LittleEndian(span[(int)(descBytesAt - 4)..]);
        long blockOffsetsAt = descBytesAt + total;
        long streamAt = blockOffsetsAt + ((long)blocks * 4);
        if (streamAt > span.Length)
        {
            throw new InvalidDataException("The prefix table is truncated.");
        }

        uint streamLength = (uint)(span.Length - streamAt);
        uint previous = 0;
        for (uint i = 0; i < descriptions + 1; i++)
        {
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(span[(int)(descOffsetsAt + (i * 4))..]);
            if (offset < previous || offset > total || (i == 0 && offset != 0))
            {
                throw new InvalidDataException("The description offsets are not increasing.");
            }

            previous = offset;
        }

        previous = 0;
        for (uint i = 0; i < blocks; i++)
        {
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(span[(int)(blockOffsetsAt + (i * 4))..]);
            if ((i == 0 && offset != 0) || offset < previous || offset >= streamLength)
            {
                throw new InvalidDataException("The block offsets are not increasing.");
            }

            previous = offset;
        }

        _data = data;
        _entryCount = (int)entries;
        _descriptionCount = (int)descriptions;
        _blockCount = (int)blocks;
        _maxKey = (int)maxKey;
        _descOffsetsAt = (int)descOffsetsAt;
        _descBytesAt = (int)descBytesAt;
        _blockOffsetsAt = (int)blockOffsetsAt;
        _streamAt = (int)streamAt;
    }

    /// <summary>Gets the number of entries.</summary>
    internal int Count => _entryCount;

    /// <summary>Gets the length of the longest key.</summary>
    internal int LongestKey => _maxKey;

    /// <summary>Gets one description.</summary>
    /// <param name="index">The description number.</param>
    /// <returns>The text.</returns>
    /// <exception cref="InvalidDataException">The index lies outside the table.</exception>
    internal string GetDescription(int index)
    {
        if ((uint)index >= (uint)_descriptionCount)
        {
            throw new InvalidDataException("A description number lies outside the table.");
        }

        ReadOnlySpan<byte> span = _data.Span;
        int start = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[(_descOffsetsAt + (index * 4))..]);
        int end = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[(_descOffsetsAt + (index * 4) + 4)..]);
        return Encoding.UTF8.GetString(span[(_descBytesAt + start)..(_descBytesAt + end)]);
    }

    /// <summary>Finds the description of the longest key that is a prefix of the digits.</summary>
    /// <param name="digits">ASCII digits.</param>
    /// <param name="description">The description number of the longest matching key.</param>
    /// <returns><see langword="true"/> when some key is a prefix of the digits (the empty key counts).</returns>
    internal bool TryFindLongest(ReadOnlySpan<char> digits, out int description)
    {
        for (int length = Math.Min(digits.Length, _maxKey); length >= 0; length--)
        {
            if (TryFindExact(digits[..length], out description))
            {
                return true;
            }
        }

        description = -1;
        return false;
    }

    /// <summary>Finds the description of a key.</summary>
    /// <param name="key">ASCII digits.</param>
    /// <param name="description">The description number.</param>
    /// <returns><see langword="true"/> when the key is in the map.</returns>
    internal bool TryFindExact(ReadOnlySpan<char> key, out int description)
    {
        description = -1;
        if (_entryCount == 0 || key.Length > _maxKey)
        {
            return false;
        }

        // The last block whose first key does not sort after the target.
        int low = 0;
        int high = _blockCount - 1;
        int found = -1;
        while (low <= high)
        {
            int mid = (low + high) >>> 1;
            int order = CompareFirstKey(mid, key);
            if (order <= 0)
            {
                found = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return found >= 0 && ScanBlock(found, key, out description);
    }

    /// <summary>Decodes every entry, checking the layout end to end.</summary>
    /// <exception cref="InvalidDataException">An entry is malformed, keys are not strictly increasing, or a description number is out of range.</exception>
    internal void Validate()
    {
        // Bounds: a key is at most MaxKeyLength digits (a small fixed buffer).
        Span<byte> previous = stackalloc byte[MaxKeyLength];
        Span<byte> key = stackalloc byte[MaxKeyLength];
        int previousLength = 0;
        for (int block = 0; block < _blockCount; block++)
        {
            int pos = BlockStart(block);
            int end = block + 1 < _blockCount ? BlockStart(block + 1) : _data.Length;
            int inBlock = block + 1 < _blockCount ? BlockSize : _entryCount - (block * BlockSize);
            int keyLength = 0;
            int description = 0;
            for (int i = 0; i < inBlock; i++)
            {
                DecodeEntry(ref pos, end, key, ref keyLength, ref description, i == 0);
                if (i == 0 && block > 0 && key[..keyLength].SequenceCompareTo(previous[..previousLength]) <= 0)
                {
                    throw new InvalidDataException("The prefix keys are not in increasing order.");
                }

                key[..keyLength].CopyTo(previous);
                previousLength = keyLength;
            }

            if (pos != end)
            {
                throw new InvalidDataException("A block of the prefix table has trailing bytes.");
            }
        }
    }

    private int BlockStart(int block) =>
        _streamAt + (int)BinaryPrimitives.ReadUInt32LittleEndian(_data.Span[(_blockOffsetsAt + (block * 4))..]);

    private int CompareFirstKey(int block, ReadOnlySpan<char> target)
    {
        // Bounds: a key is at most MaxKeyLength digits (a small fixed buffer).
        Span<byte> key = stackalloc byte[MaxKeyLength];
        int keyLength = 0;
        int description = 0;
        int pos = BlockStart(block);
        DecodeEntry(ref pos, _data.Length, key, ref keyLength, ref description, first: true);
        return CompareKey(key[..keyLength], target);
    }

    private bool ScanBlock(int block, ReadOnlySpan<char> target, out int description)
    {
        // Bounds: a key is at most MaxKeyLength digits (a small fixed buffer).
        Span<byte> key = stackalloc byte[MaxKeyLength];
        int keyLength = 0;
        int current = 0;
        int pos = BlockStart(block);
        int end = block + 1 < _blockCount ? BlockStart(block + 1) : _data.Length;
        int inBlock = block + 1 < _blockCount ? BlockSize : _entryCount - (block * BlockSize);
        for (int i = 0; i < inBlock; i++)
        {
            DecodeEntry(ref pos, end, key, ref keyLength, ref current, i == 0);
            int order = CompareKey(key[..keyLength], target);
            if (order == 0)
            {
                description = current;
                return true;
            }

            if (order > 0)
            {
                break;
            }
        }

        description = -1;
        return false;
    }

    // Keys compare as ordinal strings of digits: the shorter of two keys with a common start sorts first.
    private static int CompareKey(ReadOnlySpan<byte> key, ReadOnlySpan<char> target)
    {
        int n = Math.Min(key.Length, target.Length);
        for (int i = 0; i < n; i++)
        {
            int d = key[i] - target[i];
            if (d != 0)
            {
                return d;
            }
        }

        return key.Length - target.Length;
    }

    // Entry: varint shared (digits kept from the previous key, 0 for a block's first entry), varint suffix length, the suffix digits
    // packed two to a byte (high nibble first), then the description number as a zigzag varint delta from the previous entry's.
    private void DecodeEntry(ref int pos, int end, Span<byte> key, ref int keyLength, ref int description, bool first)
    {
        ReadOnlySpan<byte> span = _data.Span[..end];
        int shared = (int)ReadVarint(span, ref pos);
        int suffix = (int)ReadVarint(span, ref pos);
        if ((first && shared != 0) || shared > keyLength || suffix > MaxKeyLength || shared + suffix > _maxKey)
        {
            throw new InvalidDataException("A prefix entry is malformed.");
        }

        int packed = (suffix + 1) / 2;
        if (pos + packed > span.Length)
        {
            throw new InvalidDataException("The prefix table is truncated.");
        }

        for (int i = 0; i < suffix; i++)
        {
            int b = span[pos + (i / 2)];
            int digit = (i % 2 == 0) ? b >> 4 : b & 15;
            if (digit > 9)
            {
                throw new InvalidDataException("A prefix key holds a non-digit.");
            }

            key[shared + i] = (byte)('0' + digit);
        }

        pos += packed;
        keyLength = shared + suffix;
        long delta = ReadVarint(span, ref pos);
        long value = description + ((delta >>> 1) ^ -(delta & 1));
        if (value < 0 || value >= _descriptionCount)
        {
            throw new InvalidDataException("A description number lies outside the table.");
        }

        description = (int)value;
    }

    private static long ReadVarint(ReadOnlySpan<byte> span, ref int pos)
    {
        long result = 0;
        for (int shift = 0; shift < 35; shift += 7)
        {
            if (pos >= span.Length)
            {
                throw new InvalidDataException("The prefix table is truncated.");
            }

            byte b = span[pos++];
            result |= (long)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return result;
            }
        }

        throw new InvalidDataException("A varint in the prefix table is too long.");
    }
}
