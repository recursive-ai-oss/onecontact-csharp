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

        // A script's few letters come up again and again, so the answer for a code point without a mapping is remembered (see Recent below).
        int known = RecentStatus[codePoint & RecentMask];
        if (known >> 3 == codePoint)
        {
            mapping = default;
            return (IdnaStatus)(known & 7);
        }

        IdnaStatus status = Search(d, codePoint, out mapping);
        if (mapping.IsEmpty)
        {
            RecentStatus[codePoint & RecentMask] = (codePoint << 3) | (int)status;
        }

        return status;
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

    internal static bool IsMark(int codePoint)
    {
        if ((uint)codePoint < Loaded.AsciiCount)
        {
            return Data.Value.AsciiMark[codePoint] != 0;
        }

        int slot = Recent[codePoint & RecentMask];
        if (slot >> 10 != codePoint || (slot & MarkKnown) == 0)
        {
            slot = Remember(codePoint, slot, MarkKnown | (Lookup(Data.Value.Mark.Span, codePoint, 0) != 0 ? MarkBit : 0));
        }

        return (slot & MarkBit) != 0;
    }

    // The answers for the code points a domain uses over and over (a script's few letters) are kept in a small table indexed by the code
    // point, so the binary searches over the Unicode tables are not repeated. An entry holds the code point (bits 10 and up), which of
    // the answers it knows, and the answers. A writer replaces a whole entry with one aligned store, so a reader sees an entry whole; two
    // threads can only overwrite each other, which costs a repeated search and never a wrong answer.
    private const int RecentMask = 1023;
    private const int MarkBit = 1;
    private const int NfcBit = 2;
    private const int BidiShift = 2;
    private const int BidiMask = 31 << BidiShift;
    private const int MarkKnown = 1 << 7;
    private const int NfcKnown = 1 << 8;
    private const int BidiKnown = 1 << 9;
    private static readonly int[] Recent = new int[RecentMask + 1];

    // The same idea for the mapping status: an entry holds the code point (bits 3 and up) and the status (bits 0 to 2). Code points below
    // AsciiCount never reach it, so an empty entry (code point 0) never matches.
    private static readonly int[] RecentStatus = new int[RecentMask + 1];

    private static int Remember(int codePoint, int slot, int answer)
    {
        int kept = slot >> 10 == codePoint ? slot & 1023 : 0;
        int entry = (codePoint << 10) | kept | answer;
        Recent[codePoint & RecentMask] = entry;
        return entry;
    }

    internal static bool IsCompositionExcluded(int codePoint) => Lookup(Data.Value.CompositionExclusion.Span, codePoint, 0) != 0;

    // Code points the file does not list default to L (unassigned code points are disallowed before this matters).
    internal static BidiClass GetBidiClass(int codePoint)
    {
        if ((uint)codePoint < Loaded.AsciiCount)
        {
            return (BidiClass)Data.Value.AsciiBidi[codePoint];
        }

        int slot = Recent[codePoint & RecentMask];
        if (slot >> 10 != codePoint || (slot & BidiKnown) == 0)
        {
            slot = Remember(codePoint, slot, BidiKnown | (Lookup(Data.Value.Bidi.Span, codePoint, (byte)BidiClass.L) << BidiShift));
        }

        return (BidiClass)((slot & BidiMask) >> BidiShift);
    }

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

    /// <summary>
    /// True when a code point can never change under NFC, whatever stands beside it: it is a starter, has no decomposition, cannot be
    /// composed onto a code point before it (it is not the second code point of any primary composite) and is not Hangul.
    /// A text made only of such code points is in NFC as it stands.
    /// </summary>
    internal static bool IsNfcStable(int codePoint)
    {
        if ((uint)codePoint < Loaded.AsciiCount)
        {
            return true;
        }

        // Hangul: syllables decompose, and the vowel and trailing-consonant jamo compose onto what comes before them.
        if (codePoint is >= 0x1161 and <= 0x1175 or >= 0x11A8 and <= 0x11C2 or >= 0xAC00 and <= 0xD7A3)
        {
            return false;
        }

        int slot = Recent[codePoint & RecentMask];
        if (slot >> 10 != codePoint || (slot & NfcKnown) == 0)
        {
            bool stable = CombiningClass(codePoint) == 0 && !TryGetDecomposition(codePoint, out _, out _) && !Data.Value.ComposedOnto.Contains(codePoint);
            slot = Remember(codePoint, slot, NfcKnown | (stable ? NfcBit : 0));
        }

        return (slot & NfcBit) != 0;
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
                    ComposedOnto.Add(b);
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

        // The second code point of every primary composite.
        internal HashSet<int> ComposedOnto { get; } = [];
    }
}
