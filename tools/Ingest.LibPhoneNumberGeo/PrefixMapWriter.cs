using System.Buffers.Binary;
using System.Text;

namespace RecursiveAI.OneContact.Ingest.LibPhoneNumberGeo;

/// <summary>
/// Writes the prefix-map layout read by the library (see <c>docs/phones-geo.md</c>): header (entry, description, block counts and the
/// longest key, little-endian 32-bit), description offsets, description bytes, block offsets, then the front-coded entry stream.
/// Descriptions are numbered in order of first use along the sorted keys, so a run of equal descriptions costs one byte per entry.
/// </summary>
internal static class PrefixMapWriter
{
    internal const int BlockSize = 64;

    /// <summary>Builds a map.</summary>
    /// <param name="entries">Digit keys and their descriptions; keys must be distinct.</param>
    /// <returns>The table bytes.</returns>
    internal static byte[] Build(IEnumerable<(string Key, string Value)> entries)
    {
        List<(string Key, string Value)> sorted = [.. entries];
        sorted.Sort(static (a, b) => string.CompareOrdinal(a.Key, b.Key));

        Dictionary<string, int> descriptionNumbers = new(StringComparer.Ordinal);
        List<byte[]> descriptions = [];
        List<int> numbers = [];
        foreach ((_, string value) in sorted)
        {
            if (!descriptionNumbers.TryGetValue(value, out int number))
            {
                number = descriptions.Count;
                descriptionNumbers.Add(value, number);
                descriptions.Add(Encoding.UTF8.GetBytes(value));
            }

            numbers.Add(number);
        }

        int longest = 0;
        using MemoryStream stream = new();
        List<int> blockOffsets = [];
        string previousKey = string.Empty;
        int previousNumber = 0;
        for (int i = 0; i < sorted.Count; i++)
        {
            string key = sorted[i].Key;
            if (key.Length > 32)
            {
                throw new FormatException($"Key {key} is longer than 32 digits.");
            }

            longest = Math.Max(longest, key.Length);
            bool first = i % BlockSize == 0;
            if (first)
            {
                blockOffsets.Add((int)stream.Length);
                previousKey = string.Empty;
                previousNumber = 0;
            }

            int shared = 0;
            while (shared < previousKey.Length && shared < key.Length && previousKey[shared] == key[shared])
            {
                shared++;
            }

            string suffix = key[shared..];
            WriteVarint(stream, (uint)shared);
            WriteVarint(stream, (uint)suffix.Length);
            for (int d = 0; d < suffix.Length; d += 2)
            {
                int hi = suffix[d] - '0';
                int lo = d + 1 < suffix.Length ? suffix[d + 1] - '0' : 0;
                stream.WriteByte((byte)((hi << 4) | lo));
            }

            int delta = numbers[i] - previousNumber;
            WriteVarint(stream, (uint)((delta << 1) ^ (delta >> 31)));
            previousKey = key;
            previousNumber = numbers[i];
        }

        using MemoryStream output = new();
        byte[] word = new byte[4];
        void WriteWord(uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(word, value);
            output.Write(word);
        }

        WriteWord((uint)sorted.Count);
        WriteWord((uint)descriptions.Count);
        WriteWord((uint)blockOffsets.Count);
        WriteWord((uint)longest);
        uint offset = 0;
        foreach (byte[] d in descriptions)
        {
            WriteWord(offset);
            offset += (uint)d.Length;
        }

        WriteWord(offset);
        foreach (byte[] d in descriptions)
        {
            output.Write(d);
        }

        foreach (int b in blockOffsets)
        {
            WriteWord((uint)b);
        }

        stream.WriteTo(output);
        return output.ToArray();
    }

    private static void WriteVarint(Stream stream, uint value)
    {
        while (value >= 0x80)
        {
            stream.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }

        stream.WriteByte((byte)value);
    }
}
