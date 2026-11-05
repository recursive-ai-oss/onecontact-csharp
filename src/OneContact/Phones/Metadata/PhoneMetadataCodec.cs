using System.Text;

namespace RecursiveAI.OneContact.Phones.Metadata;

/// <summary>
/// The compact binary form of a territory list, used inside the compiled data file and by callers that supply compiled metadata.
/// Layout (all integers are unsigned LEB128 unless noted): a string pool (count, then length-prefixed UTF-8), then the territory count and
/// the territories. Strings are referenced by pool index plus one; zero means "absent". The reader checks every count against the bytes
/// that remain, so a hostile file cannot make it allocate more than the file's own size allows.
/// </summary>
internal static class PhoneMetadataCodec
{
    private const byte Magic0 = (byte)'P';
    private const byte Magic1 = (byte)'N';
    private const byte Magic2 = (byte)'M';
    private const byte Version = 1;

    internal static byte[] Write(IReadOnlyList<TerritoryData> territories)
    {
        Dictionary<string, int> pool = new(StringComparer.Ordinal);
        List<string> strings = [];
        MemoryStream body = new();

        int Ref(string? s)
        {
            if (s is null)
            {
                return 0;
            }

            if (!pool.TryGetValue(s, out int index))
            {
                index = strings.Count;
                strings.Add(s);
                pool.Add(s, index);
            }

            return index + 1;
        }

        void Put(uint v)
        {
            while (v >= 0x80)
            {
                body.WriteByte((byte)(v | 0x80));
                v >>= 7;
            }

            body.WriteByte((byte)v);
        }

        void PutInts(int[] values)
        {
            Put((uint)values.Length);
            foreach (int v in values)
            {
                Put((uint)(v + 1)); // -1 (no numbers of this kind) is stored as 0
            }
        }

        Put((uint)territories.Count);
        foreach (TerritoryData t in territories)
        {
            Put((uint)Ref(t.Id));
            Put((uint)t.CountryCode);
            body.WriteByte((byte)((t.MainCountryForCode ? 1 : 0) | (t.NationalPrefixOptionalWhenFormatting ? 2 : 0) | (t.MobileNumberPortableRegion ? 4 : 0)));
            Put((uint)Ref(t.LeadingDigits));
            Put((uint)Ref(t.InternationalPrefix));
            Put((uint)Ref(t.PreferredInternationalPrefix));
            Put((uint)Ref(t.NationalPrefix));
            Put((uint)Ref(t.NationalPrefixForParsing));
            Put((uint)Ref(t.NationalPrefixTransformRule));
            Put((uint)Ref(t.PreferredExtnPrefix));
            Put((uint)Ref(t.NationalPrefixFormattingRule));
            Put((uint)Ref(t.CarrierCodeFormattingRule));

            int descCount = 0;
            foreach (DescData? d in t.Descs)
            {
                if (d is not null)
                {
                    descCount++;
                }
            }

            Put((uint)descCount);
            for (int k = 0; k < t.Descs.Length; k++)
            {
                DescData? d = t.Descs[k];
                if (d is null)
                {
                    continue;
                }

                body.WriteByte((byte)k);
                body.WriteByte((byte)((d.Pattern is not null ? 1 : 0) | (d.HasPossibleLengths ? 2 : 0) | (d.Example is not null ? 4 : 0)));
                Put((uint)Ref(d.Pattern));
                PutInts(d.PossibleLengths);
                PutInts(d.LocalOnlyLengths);
                Put((uint)Ref(d.Example));
            }

            Put((uint)t.Formats.Count);
            foreach (FormatData f in t.Formats)
            {
                Put((uint)Ref(f.Pattern));
                Put((uint)Ref(f.Format));
                Put((uint)f.LeadingDigits.Length);
                foreach (string s in f.LeadingDigits)
                {
                    Put((uint)Ref(s));
                }

                Put((uint)f.IntlFormats.Length);
                foreach (string s in f.IntlFormats)
                {
                    Put((uint)Ref(s));
                }

                Put((uint)Ref(f.NationalPrefixFormattingRule));
                Put((uint)Ref(f.CarrierCodeFormattingRule));
                body.WriteByte((byte)((f.HasNationalPrefixOptionalWhenFormatting ? 1 : 0) | (f.NationalPrefixOptionalWhenFormatting ? 2 : 0)));
            }
        }

        MemoryStream output = new();
        output.WriteByte(Magic0);
        output.WriteByte(Magic1);
        output.WriteByte(Magic2);
        output.WriteByte(Version);
        WriteVarint(output, (uint)strings.Count);
        foreach (string s in strings)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(s);
            WriteVarint(output, (uint)bytes.Length);
            output.Write(bytes);
        }

        body.Position = 0;
        body.CopyTo(output);
        return output.ToArray();
    }

    private static void WriteVarint(Stream s, uint v)
    {
        while (v >= 0x80)
        {
            s.WriteByte((byte)(v | 0x80));
            v >>= 7;
        }

        s.WriteByte((byte)v);
    }

    internal static List<TerritoryData> Read(ReadOnlyMemory<byte> data)
    {
        Reader r = new(data);
        return r.ReadAll();
    }

    private ref struct Reader
    {
        private readonly ReadOnlySpan<byte> _span;
        private int _pos;
        private string[] _strings;

        internal Reader(ReadOnlyMemory<byte> data)
        {
            _span = data.Span;
            _pos = 0;
            _strings = [];
        }

        internal List<TerritoryData> ReadAll()
        {
            if (_span.Length < 4 || _span[0] != Magic0 || _span[1] != Magic1 || _span[2] != Magic2)
            {
                throw new InvalidDataException("Not compiled phone metadata.");
            }

            if (_span[3] != Version)
            {
                throw new InvalidDataException("This compiled phone metadata version is not supported.");
            }

            _pos = 4;
            uint stringCount = Count(1);
            _strings = new string[stringCount];
            for (int i = 0; i < _strings.Length; i++)
            {
                uint len = Count(1);
                if (len > _span.Length - _pos)
                {
                    throw Truncated();
                }

                _strings[i] = Encoding.UTF8.GetString(_span.Slice(_pos, (int)len));
                _pos += (int)len;
            }

            uint territoryCount = Count(10);
            if (territoryCount > PhoneMetadataXml.MaxTerritories)
            {
                throw new InvalidDataException("The compiled phone metadata has too many territories.");
            }

            List<TerritoryData> result = new((int)territoryCount);
            for (uint i = 0; i < territoryCount; i++)
            {
                result.Add(ReadTerritory());
            }

            if (_pos != _span.Length)
            {
                throw new InvalidDataException("The compiled phone metadata has trailing bytes.");
            }

            return result;
        }

        private TerritoryData ReadTerritory()
        {
            TerritoryData t = new()
            {
                Id = Str() ?? "",
                CountryCode = (int)Varint(),
            };
            int flags = Byte();
            t.MainCountryForCode = (flags & 1) != 0;
            t.NationalPrefixOptionalWhenFormatting = (flags & 2) != 0;
            t.MobileNumberPortableRegion = (flags & 4) != 0;
            t.LeadingDigits = Str();
            t.InternationalPrefix = Str();
            t.PreferredInternationalPrefix = Str();
            t.NationalPrefix = Str();
            t.NationalPrefixForParsing = Str();
            t.NationalPrefixTransformRule = Str();
            t.PreferredExtnPrefix = Str();
            t.NationalPrefixFormattingRule = Str();
            t.CarrierCodeFormattingRule = Str();

            uint descCount = Count(2);
            for (uint i = 0; i < descCount; i++)
            {
                int kind = Byte();
                if (kind >= DescKinds.Count)
                {
                    throw new InvalidDataException("The compiled phone metadata has an unknown description kind.");
                }

                int dflags = Byte();
                DescData d = new()
                {
                    Pattern = Str(),
                    HasPossibleLengths = (dflags & 2) != 0,
                    PossibleLengths = Ints(),
                    LocalOnlyLengths = Ints(),
                    Example = Str(),
                };
                t.Descs[kind] = d;
            }

            uint formatCount = Count(6);
            for (uint i = 0; i < formatCount; i++)
            {
                FormatData f = new()
                {
                    Pattern = Str() ?? "",
                    Format = Str() ?? "",
                };
                uint leading = Count(1);
                string[] ld = new string[leading];
                for (int j = 0; j < ld.Length; j++)
                {
                    ld[j] = Str() ?? "";
                }

                f.LeadingDigits = ld;
                uint intl = Count(1);
                string[] il = new string[intl];
                for (int j = 0; j < il.Length; j++)
                {
                    il[j] = Str() ?? "";
                }

                f.IntlFormats = il;
                f.NationalPrefixFormattingRule = Str();
                f.CarrierCodeFormattingRule = Str();
                int fflags = Byte();
                f.HasNationalPrefixOptionalWhenFormatting = (fflags & 1) != 0;
                f.NationalPrefixOptionalWhenFormatting = (fflags & 2) != 0;
                t.Formats.Add(f);
            }

            return t;
        }

        private int[] Ints()
        {
            uint n = Count(1);
            int[] values = new int[n];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = (int)Varint() - 1;
            }

            return values;
        }

        private string? Str()
        {
            uint index = Varint();
            if (index == 0)
            {
                return null;
            }

            if (index > _strings.Length)
            {
                throw new InvalidDataException("The compiled phone metadata refers to a missing string.");
            }

            return _strings[index - 1];
        }

        private int Byte()
        {
            if (_pos >= _span.Length)
            {
                throw Truncated();
            }

            return _span[_pos++];
        }

        /// <summary>Reads a count and checks that at least <paramref name="minBytesEach"/> bytes per item remain.</summary>
        private uint Count(int minBytesEach)
        {
            uint n = Varint();
            if ((long)n * minBytesEach > _span.Length - _pos)
            {
                throw Truncated();
            }

            return n;
        }

        private uint Varint()
        {
            uint result = 0;
            for (int shift = 0; shift < 35; shift += 7)
            {
                int b = Byte();
                result |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                {
                    return result;
                }
            }

            throw new InvalidDataException("The compiled phone metadata has a malformed integer.");
        }

        private static InvalidDataException Truncated() => new("The compiled phone metadata is truncated or its counts are implausible.");
    }
}
