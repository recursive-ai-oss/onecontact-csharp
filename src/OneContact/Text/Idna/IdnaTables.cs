using System.Buffers.Binary;
using RecursiveAI.OneContact.Data;

namespace RecursiveAI.OneContact.Text.Idna;

/// <summary>The IDNA mapping status of a code point (UTS #46 section 5).</summary>
internal enum IdnaStatus : byte
{
    Valid = 0,
    Ignored = 1,
    Mapped = 2,
    Deviation = 3,
    Disallowed = 4,
}

/// <summary>Bidi_Class values the label rules of RFC 5893 look at. The numbering is the table format (docs/idna.md).</summary>
internal enum BidiClass : byte
{
    L = 0, R = 1, AL = 2, EN = 3, ES = 4, ET = 5, AN = 6, CS = 7, NSM = 8, BN = 9, B = 10, S = 11, WS = 12, ON = 13,
    LRE = 14, LRO = 15, RLE = 16, RLO = 17, PDF = 18, LRI = 19, RLI = 20, FSI = 21, PDI = 22,
}

/// <summary>Joining_Type values of RFC 5892 appendix A.1. The numbering is the table format.</summary>
internal enum JoiningType : byte
{
    U = 0, L = 1, R = 2, D = 3, C = 4, T = 5,
}

/// <summary>
/// The Unicode data UTS #46 processing needs, read once from the compiled <c>unicode-idna</c> tables: the mapping table, canonical
/// combining classes, canonical decompositions and composition exclusions, general category Mark, Bidi_Class and Joining_Type.
/// </summary>
internal static class IdnaTables
{
    private static readonly EmbeddedTables Tables = new(typeof(IdnaTables).Assembly, "OneContact.Data.unicode-idna.ocd");
    private static readonly Lazy<Loaded> Data = new(() => new Loaded(Tables), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Code points below this value (U+0000 to U+02FF) have combining class 0 and are unchanged by NFC.</summary>
    internal const int NfcStableLimit = Loaded.AsciiCount;

    internal static IdnaStatus GetStatus(int codePoint, out ReadOnlySpan<char> mapping)
    {
        Loaded d = Data.Value;
        if ((uint)codePoint < Loaded.AsciiCount)
        {
            // Domain names are mostly ASCII or Latin: answer from the table filled with this same search.
            mapping = d.AsciiMappingLength[codePoint] == 0 ? default : d.MappingChars.AsSpan(d.AsciiMappingOffset[codePoint], d.AsciiMappingLength[codePoint]);
            return (IdnaStatus)d.AsciiStatus[codePoint];
        }

        return Search(d, codePoint, out mapping);
    }

    internal static IdnaStatus SearchStatus(int codePoint, out ReadOnlySpan<char> mapping) => Search(Data.Value, codePoint, out mapping);

    private static IdnaStatus Search(Loaded d, int codePoint, out ReadOnlySpan<char> mapping)
    {
        ReadOnlySpan<byte> ranges = d.IdnaRanges.Span;
        int lo = 0;
        int hi = (ranges.Length / 12) - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            ReadOnlySpan<byte> e = ranges.Slice(mid * 12, 12);
            uint first = BinaryPrimitives.ReadUInt32LittleEndian(e);
            uint last = BinaryPrimitives.ReadUInt32LittleEndian(e[4..]);
            if (codePoint < first)
            {
                hi = mid - 1;
            }
            else if (codePoint > last)
            {
                lo = mid + 1;
            }
            else
            {
                uint info = BinaryPrimitives.ReadUInt32LittleEndian(e[8..]);
                int length = (int)((info >> 3) & 0xFF);
                int offset = (int)(info >> 11);
                mapping = length == 0 ? default : d.MappingChars.AsSpan(offset, length);
                return (IdnaStatus)(info & 7);
            }
        }

        // Code points the table does not list are unassigned.
        mapping = default;
        return IdnaStatus.Disallowed;
    }

    // Where the mapping of a code point starts in MappingChars (the table entry's offset field).
    private static int MappingOffset(Loaded d, int codePoint)
    {
        ReadOnlySpan<byte> ranges = d.IdnaRanges.Span;
        int lo = 0;
        int hi = (ranges.Length / 12) - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            ReadOnlySpan<byte> e = ranges.Slice(mid * 12, 12);
            uint first = BinaryPrimitives.ReadUInt32LittleEndian(e);
            uint last = BinaryPrimitives.ReadUInt32LittleEndian(e[4..]);
            if (codePoint < first)
            {
                hi = mid - 1;
            }
            else if (codePoint > last)
            {
                lo = mid + 1;
            }
            else
            {
                return (int)(BinaryPrimitives.ReadUInt32LittleEndian(e[8..]) >> 11);
            }
        }

        return 0;
    }

    internal static byte CombiningClass(int codePoint) =>
        (uint)codePoint < Loaded.AsciiCount ? Data.Value.AsciiCcc[codePoint] : Lookup(Data.Value.Ccc.Span, codePoint, 0);

    internal static bool IsMark(int codePoint) =>
        (uint)codePoint < Loaded.AsciiCount ? Data.Value.AsciiMark[codePoint] != 0 : Lookup(Data.Value.Mark.Span, codePoint, 0) != 0;

    internal static bool IsCompositionExcluded(int codePoint) => Lookup(Data.Value.CompositionExclusion.Span, codePoint, 0) != 0;

    // Code points the file does not list default to L (unassigned code points are disallowed before this matters).
    internal static BidiClass GetBidiClass(int codePoint) =>
        (BidiClass)((uint)codePoint < Loaded.AsciiCount ? Data.Value.AsciiBidi[codePoint] : Lookup(Data.Value.Bidi.Span, codePoint, (byte)BidiClass.L));

    internal static JoiningType GetJoiningType(int codePoint) =>
        (JoiningType)((uint)codePoint < Loaded.AsciiCount ? Data.Value.AsciiJoining[codePoint] : Lookup(Data.Value.Joining.Span, codePoint, (byte)JoiningType.U));

    /// <summary>Gets the canonical decomposition of a code point (one or two code points), or <see langword="false"/> when it has none.</summary>
    internal static bool TryGetDecomposition(int codePoint, out int first, out int second)
    {
        ReadOnlySpan<byte> t = Data.Value.Decomposition.Span;
        int lo = 0;
        int hi = (t.Length / 12) - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            ReadOnlySpan<byte> e = t.Slice(mid * 12, 12);
            int cp = (int)BinaryPrimitives.ReadUInt32LittleEndian(e);
            if (codePoint < cp)
            {
                hi = mid - 1;
            }
            else if (codePoint > cp)
            {
                lo = mid + 1;
            }
            else
            {
                first = (int)BinaryPrimitives.ReadUInt32LittleEndian(e[4..]);
                second = (int)BinaryPrimitives.ReadUInt32LittleEndian(e[8..]);
                return true;
            }
        }

        first = 0;
        second = 0;
        return false;
    }

    /// <summary>Gets the primary composite of a starter and a following code point (UAX #15), or 0.</summary>
    internal static int Compose(int first, int second) =>
        Data.Value.Composition.TryGetValue(((long)first << 21) | (uint)second, out int composite) ? composite : 0;

    // 8-byte entries: first u32, then last | value << 24; sorted, non-overlapping.
    private static byte Lookup(ReadOnlySpan<byte> table, int codePoint, byte missing)
    {
        int lo = 0;
        int hi = (table.Length / 8) - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            ReadOnlySpan<byte> e = table.Slice(mid * 8, 8);
            uint first = BinaryPrimitives.ReadUInt32LittleEndian(e);
            uint tail = BinaryPrimitives.ReadUInt32LittleEndian(e[4..]);
            uint last = tail & 0xFFFFFF;
            if (codePoint < first)
            {
                hi = mid - 1;
            }
            else if (codePoint > last)
            {
                lo = mid + 1;
            }
            else
            {
                return (byte)(tail >> 24);
            }
        }

        return missing;
    }

    private sealed class Loaded
    {
        internal Loaded(EmbeddedTables tables)
        {
            IdnaRanges = tables.GetTable("idna.ranges");
            ReadOnlySpan<byte> map = tables.GetTable("idna.map").Span;
            MappingChars = new char[map.Length / 2];
            for (int i = 0; i < MappingChars.Length; i++)
            {
                MappingChars[i] = (char)BinaryPrimitives.ReadUInt16LittleEndian(map[(i * 2)..]);
            }

            Ccc = tables.GetTable("ccc");
            Mark = tables.GetTable("mark");
            CompositionExclusion = tables.GetTable("compexcl");
            Decomposition = tables.GetTable("decomp");
            Bidi = tables.GetTable("bidi");
            Joining = tables.GetTable("joining");

            // The answers for the low code points, from the same searches, so the common case needs none.
            for (int c = 0; c < AsciiCount; c++)
            {
                IdnaStatus status = Search(this, c, out ReadOnlySpan<char> mapping);
                AsciiStatus[c] = (byte)status;
                if (!mapping.IsEmpty)
                {
                    // The mapping is a slice of MappingChars; find where it starts by the table entry again.
                    AsciiMappingLength[c] = (byte)mapping.Length;
                    AsciiMappingOffset[c] = MappingOffset(this, c);
                }

                AsciiCcc[c] = Lookup(Ccc.Span, c, 0);
                AsciiMark[c] = Lookup(Mark.Span, c, 0);
                AsciiBidi[c] = Lookup(Bidi.Span, c, (byte)BidiClass.L);
                AsciiJoining[c] = Lookup(Joining.Span, c, (byte)JoiningType.U);
            }

            // UAX #15 section 3.11: the primary composites are the canonical decompositions into two code points that are not
            // excluded from composition (Full_Composition_Exclusion also covers singletons and non-starter decompositions).
            ReadOnlySpan<byte> d = Decomposition.Span;
            for (int i = 0; i + 12 <= d.Length; i += 12)
            {
                int cp = (int)BinaryPrimitives.ReadUInt32LittleEndian(d[i..]);
                int a = (int)BinaryPrimitives.ReadUInt32LittleEndian(d[(i + 4)..]);
                int b = (int)BinaryPrimitives.ReadUInt32LittleEndian(d[(i + 8)..]);
                if (b != 0 && Lookup(CompositionExclusion.Span, cp, 0) == 0)
                {
                    Composition[((long)a << 21) | (uint)b] = cp;
                }
            }
        }

        // U+0000 to U+02FF: ASCII, Latin-1, Latin Extended and IPA, the scripts domains in Western languages use. None of them has a
        // combining class, and none changes under NFC, which NormalizationC relies on.
        internal const int AsciiCount = 0x300;

        internal byte[] AsciiStatus { get; } = new byte[AsciiCount];

        internal byte[] AsciiMappingLength { get; } = new byte[AsciiCount];

        internal int[] AsciiMappingOffset { get; } = new int[AsciiCount];

        internal byte[] AsciiCcc { get; } = new byte[AsciiCount];

        internal byte[] AsciiMark { get; } = new byte[AsciiCount];

        internal byte[] AsciiBidi { get; } = new byte[AsciiCount];

        internal byte[] AsciiJoining { get; } = new byte[AsciiCount];

        internal ReadOnlyMemory<byte> IdnaRanges { get; }

        internal char[] MappingChars { get; }

        internal ReadOnlyMemory<byte> Ccc { get; }

        internal ReadOnlyMemory<byte> Mark { get; }

        internal ReadOnlyMemory<byte> CompositionExclusion { get; }

        internal ReadOnlyMemory<byte> Decomposition { get; }

        internal ReadOnlyMemory<byte> Bidi { get; }

        internal ReadOnlyMemory<byte> Joining { get; }

        internal Dictionary<long, int> Composition { get; } = [];
    }
}
