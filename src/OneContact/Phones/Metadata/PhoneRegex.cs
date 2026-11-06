using System.Globalization;
using System.Text;

namespace RecursiveAI.OneContact.Phones.Metadata;

/// <summary>
/// A small backtracking matcher for the regular-expression subset that phone metadata and phone-number scanning are written in:
/// literals, <c>.</c>, character classes (ranges, negation, <c>\d \D \s \S \w \W</c>, <c>\p{Nd}</c>, <c>\p{N}</c>, <c>\p{L}</c> and their
/// negations outside classes), groups (capturing and <c>(?:)</c>), alternation, the quantifiers <c>? * + {n} {n,} {n,m}</c> (greedy,
/// lazy with a trailing <c>?</c>, or possessive with a trailing <c>+</c>, which is treated as greedy) and the anchors <c>^ $</c>.
/// Anything else is rejected when the pattern is compiled. Text is read as Unicode code points. Matching is leftmost with the
/// first alternative that succeeds winning, which is what the metadata's authors rely on when a pattern captures groups for formatting.
/// A step budget bounds the work on hostile patterns: when it runs out the match fails.
/// </summary>
internal sealed class PhoneRegex
{
    /// <summary>How letters compare: exactly, ignoring the case of ASCII letters, or ignoring case with simple Unicode folding.</summary>
    internal enum CaseMode
    {
        Exact,
        Ascii,
        Unicode,
    }

    private const int MaxInstructions = 1 << 16;
    private const int MaxPatternLength = 1 << 16;
    private const int StepBudget = 400_000;
    private const int MaxStack = 1 << 20;
    private const int MaxGroups = 32;

    private enum Op : byte
    {
        Char,
        Set,
        Any,
        Split,
        Jump,
        Save,
        AssertStart,
        AssertEnd,
        Match,
    }

    private readonly Inst[] _program;
    private readonly CharSet[] _sets;
    private readonly CaseMode _case;

    // The characters a match can begin with, so a search skips start positions that cannot match. Unused when the pattern can match
    // the empty string. Over-approximate: a position the table allows may still fail.
    private readonly ulong _firstLow;
    private readonly ulong _firstHigh;
    private readonly bool _firstNonAscii;
    private readonly bool _canStartEmpty;

    private const int DfaAfterCalls = 4;

    private readonly bool _dfaEligible;
    private Dfa? _dfa;
    private int _isMatchCalls;

    [ThreadStatic]
    private static int[]? s_stack;

    private PhoneRegex(Inst[] program, CharSet[] sets, int groupCount, CaseMode caseMode)
    {
        if (caseMode == CaseMode.Exact)
        {
            // Exact patterns never fold case, so the other-case letter of the ASCII fast path is dropped.
            for (int i = 0; i < program.Length; i++)
            {
                if (program[i].Op == Op.Char)
                {
                    program[i] = new Inst(Op.Char, program[i].Cp, 0, 0, fold: false);
                }
            }
        }

        _program = program;
        _sets = sets;
        GroupCount = groupCount;
        _case = caseMode;
        foreach (CharSet set in sets)
        {
            set.Freeze(caseMode);
        }

        ComputeFirst(out _firstLow, out _firstHigh, out _firstNonAscii, out _canStartEmpty);
        _dfaEligible = true;
        foreach (Inst inst in program)
        {
            if (inst.Op is Op.AssertStart or Op.AssertEnd)
            {
                _dfaEligible = false;
            }
        }
    }

    /// <summary>Gets the number of capturing groups.</summary>
    internal int GroupCount { get; }

    /// <summary>Compiles a pattern.</summary>
    /// <param name="pattern">The pattern text.</param>
    /// <param name="caseMode">How letters compare.</param>
    /// <returns>The compiled pattern.</returns>
    /// <exception cref="InvalidDataException">The pattern is outside the supported subset or too large.</exception>
    internal static PhoneRegex Compile(string pattern, CaseMode caseMode = CaseMode.Exact)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        if (pattern.Length > MaxPatternLength)
        {
            throw new InvalidDataException("A phone pattern is implausibly long.");
        }

        Parser parser = new(pattern);
        Node root = parser.ParseAll();
        Compiler compiler = new();
        compiler.Emit(root);
        compiler.Add(new Inst(Op.Match, 0, 0, 0));
        return new PhoneRegex(compiler.Program(), [.. compiler.Sets], parser.Groups, caseMode);
    }

    /// <summary>Tests whether the whole of <paramref name="input"/> matches.</summary>
    /// <param name="input">The text.</param>
    /// <returns><see langword="true"/> when the pattern matches all of it.</returns>
    internal bool IsMatch(ReadOnlySpan<char> input)
    {
        // A pattern asked a few times is worth a DFA: the answer is the same, but each character costs one table step.
        Dfa? dfa = _dfa;
        if (dfa is null && _dfaEligible && Interlocked.Increment(ref _isMatchCalls) == DfaAfterCalls)
        {
            _dfa = dfa = new Dfa(this);
        }

        if (dfa is not null && dfa.TryIsMatch(input, out bool result))
        {
            return result;
        }

        return Run(input, 0, true, default, out _);
    }

    /// <summary>Tests whether the pattern matches at the start of <paramref name="input"/>, taking the first match in priority order.</summary>
    /// <param name="input">The text.</param>
    /// <param name="length">The length of the match in UTF-16 units.</param>
    /// <returns><see langword="true"/> when there is a match at the start.</returns>
    internal bool LookingAt(ReadOnlySpan<char> input, out int length) => Run(input, 0, false, default, out length);

    /// <summary>Matches all of <paramref name="input"/> and reports the capture groups.</summary>
    /// <param name="input">The text.</param>
    /// <param name="groups">Room for <c>2 * (GroupCount + 1)</c> integers: start and end of the whole match, then of each group (-1 when unset).</param>
    /// <returns><see langword="true"/> when the pattern matches all of it.</returns>
    internal bool Match(ReadOnlySpan<char> input, Span<int> groups) => Run(input, 0, true, groups, out _);

    /// <summary>Matches at the start of <paramref name="input"/> and reports the capture groups.</summary>
    /// <param name="input">The text.</param>
    /// <param name="groups">As for <see cref="Match"/>.</param>
    /// <returns><see langword="true"/> when there is a match at the start.</returns>
    internal bool MatchPrefix(ReadOnlySpan<char> input, Span<int> groups) => Run(input, 0, false, groups, out _);

    // Whether the instruction at pc is a character instruction that accepts this ASCII character. The same tests the matcher applies.
    private bool Consumes(int pc, char c)
    {
        Inst inst = _program[pc];
        switch (inst.Op)
        {
            case Op.Char:
                return c == inst.X || (_case != CaseMode.Exact && FoldEqual(c, inst.X, _case));
            case Op.Set:
                return _sets[inst.X].AsciiHas(c);
            case Op.Any:
                return c is not ('\n' or '\r');
            default:
                return false;
        }
    }

    private void ComputeFirst(out ulong low, out ulong high, out bool nonAscii, out bool canStartEmpty)
    {
        low = 0;
        high = 0;
        nonAscii = false;
        canStartEmpty = false;
        bool[] seen = new bool[_program.Length];
        Stack<int> todo = new();
        todo.Push(0);
        while (todo.Count > 0)
        {
            int pc = todo.Pop();
            if (seen[pc])
            {
                continue;
            }

            seen[pc] = true;
            Inst inst = _program[pc];
            switch (inst.Op)
            {
                case Op.Char:
                    for (int a = 0; a < 128; a++)
                    {
                        if (a == inst.X || (_case != CaseMode.Exact && FoldEqual(a, inst.X, _case)))
                        {
                            AddAscii(ref low, ref high, a);
                        }
                    }

                    nonAscii |= inst.X >= 128 || _case == CaseMode.Unicode;
                    break;
                case Op.Set:
                    CharSet set = _sets[inst.X];
                    for (int a = 0; a < 128; a++)
                    {
                        if (set.AsciiHas(a))
                        {
                            AddAscii(ref low, ref high, a);
                        }
                    }

                    nonAscii = true;
                    break;
                case Op.Any:
                    low = ulong.MaxValue;
                    high = ulong.MaxValue;
                    nonAscii = true;
                    break;
                case Op.Split:
                    todo.Push(inst.X);
                    todo.Push(inst.Y);
                    break;
                case Op.Jump:
                    todo.Push(inst.X);
                    break;
                case Op.Match:
                    canStartEmpty = true;
                    break;
                default: // Save and the anchors consume nothing
                    todo.Push(pc + 1);
                    break;
            }
        }
    }

    private static void AddAscii(ref ulong low, ref ulong high, int c)
    {
        if (c < 64)
        {
            low |= 1UL << c;
        }
        else
        {
            high |= 1UL << (c - 64);
        }
    }

    // Whether a match could begin with this character.
    private bool CanStartWith(char c)
    {
        if (c >= 128)
        {
            return _firstNonAscii;
        }

        return c < 64 ? (_firstLow & (1UL << c)) != 0 : (_firstHigh & (1UL << (c - 64))) != 0;
    }

    /// <summary>Finds the leftmost match.</summary>
    /// <param name="input">The text.</param>
    /// <param name="from">Where to start looking, in UTF-16 units.</param>
    /// <param name="groups">As for <see cref="Match"/>; may be empty when only the span of the whole match is wanted via <paramref name="start"/> and <paramref name="end"/>.</param>
    /// <param name="start">The start of the match.</param>
    /// <param name="end">The end of the match.</param>
    /// <returns><see langword="true"/> when found.</returns>
    internal bool Find(ReadOnlySpan<char> input, int from, Span<int> groups, out int start, out int end)
    {
        int pos = from;
        while (pos <= input.Length)
        {
            if (!_canStartEmpty && (pos == input.Length || !CanStartWith(input[pos])))
            {
                if (pos == input.Length)
                {
                    break;
                }

                pos += char.IsHighSurrogate(input[pos]) && pos + 1 < input.Length && char.IsLowSurrogate(input[pos + 1]) ? 2 : 1;
                continue;
            }

            if (Run(input, pos, false, groups, out end))
            {
                start = pos;
                return true;
            }

            if (pos == input.Length)
            {
                break;
            }

            ReadCodePoint(input, pos, out int len);
            pos += len;
        }

        start = -1;
        end = -1;
        return false;
    }

    private static int ReadCodePoint(ReadOnlySpan<char> s, int pos, out int length)
    {
        char c = s[pos];
        if (char.IsHighSurrogate(c) && pos + 1 < s.Length && char.IsLowSurrogate(s[pos + 1]))
        {
            length = 2;
            return char.ConvertToUtf32(c, s[pos + 1]);
        }

        length = 1;
        return c;
    }

    // Case mapping for the few characters whose mapping into ASCII letters differs between platforms: dotless i and long s map up to
    // I and S, the Turkish dotted capital I and the Kelvin sign map down to i and k.
    internal static int ToUpper(int cp)
    {
        if (cp == 0x131)
        {
            return 'I';
        }

        if (cp == 0x17F)
        {
            return 'S';
        }

        if (cp < 128)
        {
            return cp is >= 'a' and <= 'z' ? cp - 32 : cp;
        }

        return Rune.IsValid(cp) ? Rune.ToUpperInvariant(new Rune(cp)).Value : cp;
    }

    internal static int ToLower(int cp)
    {
        if (cp == 0x130)
        {
            return 'i';
        }

        if (cp == 0x212A)
        {
            return 'k';
        }

        if (cp < 128)
        {
            return cp is >= 'A' and <= 'Z' ? cp + 32 : cp;
        }

        return Rune.IsValid(cp) ? Rune.ToLowerInvariant(new Rune(cp)).Value : cp;
    }

    private static bool FoldEqual(int a, int b, CaseMode mode)
    {
        if (mode == CaseMode.Unicode)
        {
            return ToLower(ToUpper(a)) == ToLower(ToUpper(b));
        }

        return a < 128 && b < 128 && char.ToLowerInvariant((char)a) == char.ToLowerInvariant((char)b);
    }

    private bool Run(ReadOnlySpan<char> input, int startPos, bool full, Span<int> groups, out int end)
    {
        end = 0;
        if (!_canStartEmpty && (startPos >= input.Length || !CanStartWith(input[startPos])))
        {
            return false;
        }

        int slots = 2 * (GroupCount + 1);
        if (!groups.IsEmpty)
        {
            groups[..Math.Min(groups.Length, slots)].Fill(-1);
        }

        // Bounds: slots is at most 2 * (MaxGroups + 1) = 66 integers, so the fallback buffer is small and fixed.
        Span<int> caps = groups.Length >= slots ? groups[..slots] : stackalloc int[slots];
        if (groups.Length < slots)
        {
            caps.Fill(-1);
        }

        int[] stack = s_stack ??= new int[1024];
        int sp = 0;
        int pc = 0;
        int pos = startPos;
        int steps = 0;
        ReadOnlySpan<Inst> program = _program;
        while (true)
        {
            if (++steps > StepBudget)
            {
                return false;
            }

            bool fail = false;
            Inst inst = program[pc];
            switch (inst.Op)
            {
                case Op.Char:
                    if (pos < input.Length)
                    {
                        // ASCII fast path: Y holds the other-case letter, or -1.
                        char ch = input[pos];
                        if (ch < 128 && inst.X < 128)
                        {
                            if (ch == inst.X || ch == inst.Y)
                            {
                                pos++;
                                pc++;
                                break;
                            }

                            fail = true;
                            break;
                        }

                        int cp = ReadCodePoint(input, pos, out int len);
                        if (cp == inst.X || (_case != CaseMode.Exact && FoldEqual(cp, inst.X, _case)))
                        {
                            pos += len;
                            pc++;
                            break;
                        }
                    }

                    fail = true;
                    break;
                case Op.Set:
                    if (pos < input.Length)
                    {
                        char ch = input[pos];
                        if (ch < 128)
                        {
                            if (_sets[inst.X].AsciiHas(ch))
                            {
                                pos++;
                                pc++;
                                break;
                            }

                            fail = true;
                            break;
                        }

                        int cp = ReadCodePoint(input, pos, out int len);
                        if (_sets[inst.X].Contains(cp, _case))
                        {
                            pos += len;
                            pc++;
                            break;
                        }
                    }

                    fail = true;
                    break;
                case Op.Any:
                    if (pos < input.Length)
                    {
                        int cp = ReadCodePoint(input, pos, out int len);
                        if (cp is not ('\n' or '\r' or '\u0085' or '\u2028' or '\u2029'))
                        {
                            pos += len;
                            pc++;
                            break;
                        }
                    }

                    fail = true;
                    break;
                case Op.Split:
                    if (sp + 3 > MaxStack)
                    {
                        return false;
                    }

                    if (sp + 3 > stack.Length)
                    {
                        Array.Resize(ref stack, stack.Length * 2);
                        s_stack = stack;
                    }

                    stack[sp++] = 0;
                    stack[sp++] = inst.Y;
                    stack[sp++] = pos;
                    pc = inst.X;
                    break;
                case Op.Jump:
                    pc = inst.X;
                    break;
                case Op.Save:
                    if (sp + 3 > MaxStack)
                    {
                        return false;
                    }

                    if (sp + 3 > stack.Length)
                    {
                        Array.Resize(ref stack, stack.Length * 2);
                        s_stack = stack;
                    }

                    stack[sp++] = 1;
                    stack[sp++] = inst.X;
                    stack[sp++] = caps[inst.X];
                    caps[inst.X] = pos;
                    pc++;
                    break;
                case Op.AssertStart:
                    if (pos == 0)
                    {
                        pc++;
                    }
                    else
                    {
                        fail = true;
                    }

                    break;
                case Op.AssertEnd:
                    if (AtEnd(input, pos))
                    {
                        pc++;
                    }
                    else
                    {
                        fail = true;
                    }

                    break;
                default: // Match
                    if (!full || pos == input.Length)
                    {
                        end = pos;
                        caps[0] = startPos;
                        caps[1] = pos;
                        return true;
                    }

                    fail = true;
                    break;
            }

            if (!fail)
            {
                continue;
            }

            // Backtrack: undo capture writes until a pending alternative is found.
            bool resumed = false;
            while (sp > 0)
            {
                int c = stack[sp - 1];
                int b = stack[sp - 2];
                int kind = stack[sp - 3];
                sp -= 3;
                if (kind == 1)
                {
                    caps[b] = c;
                    continue;
                }

                pc = b;
                pos = c;
                resumed = true;
                break;
            }

            if (!resumed)
            {
                return false;
            }
        }
    }

    // "$" matches at the end of the input and also just before a final line terminator.
    private static bool AtEnd(ReadOnlySpan<char> input, int pos)
    {
        int rest = input.Length - pos;
        if (rest == 0)
        {
            return true;
        }

        if (rest == 1)
        {
            return input[pos] is '\n' or '\r' or '\u0085' or '\u2028' or '\u2029';
        }

        return rest == 2 && input[pos] == '\r' && input[pos + 1] == '\n';
    }

    /// <summary>
    /// A lazily built deterministic automaton for whole-input yes/no matching of ASCII text. States are sets of program positions; the
    /// characters 0 to 127 are grouped into classes that no instruction tells apart, and each (state, class) step is computed once, on
    /// first use. Text with a character beyond ASCII, or a pattern whose automaton grows past a fixed size, is left to the backtracking
    /// matcher, which gives the same answer.
    /// </summary>
    private sealed class Dfa
    {
        private const int MaxStates = 512;
        private const int Unknown = -1;
        private const int Dead = -2;

        private readonly PhoneRegex _re;
        private readonly byte[] _classOf = new byte[128];
        private readonly char[] _representative;
        private readonly int _classCount;
        private readonly int[] _next;
        private readonly bool[] _accept = new bool[MaxStates];
        private readonly int[][] _members = new int[MaxStates][];
        private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);
        private readonly object _gate = new();
        private readonly int _start;
        private int _count;
        private volatile bool _overflow;

        internal Dfa(PhoneRegex re)
        {
            _re = re;
            // Characters with the same answer from every consuming instruction behave alike.
            Dictionary<string, int> classes = new(StringComparer.Ordinal);
            List<char> reps = [];
            char[] signature = new char[re._program.Length];
            for (int a = 0; a < 128; a++)
            {
                for (int pc = 0; pc < signature.Length; pc++)
                {
                    signature[pc] = re.Consumes(pc, (char)a) ? '1' : '0';
                }

                string key = new(signature);
                if (!classes.TryGetValue(key, out int cls))
                {
                    cls = reps.Count;
                    classes[key] = cls;
                    reps.Add((char)a);
                }

                _classOf[a] = (byte)Math.Min(cls, 255);
                if (cls > 254)
                {
                    _overflow = true;
                }
            }

            _representative = [.. reps];
            _classCount = reps.Count;
            _next = new int[MaxStates * Math.Max(_classCount, 1)];
            Array.Fill(_next, Unknown);
            _start = _overflow ? 0 : Intern(Closure(new List<int> { 0 }));
        }

        /// <summary>Tells whether the whole input matches, when the automaton can say.</summary>
        /// <param name="input">The text.</param>
        /// <param name="result">The answer when this returns true.</param>
        /// <returns>false when the input has a character beyond ASCII or the automaton is too large: ask the backtracking matcher.</returns>
        internal bool TryIsMatch(ReadOnlySpan<char> input, out bool result)
        {
            result = false;
            if (_overflow)
            {
                return false;
            }

            int state = _start;
            foreach (char c in input)
            {
                if (c >= 128)
                {
                    return false;
                }

                int slot = (state * _classCount) + _classOf[c];
                int next = Volatile.Read(ref _next[slot]);
                if (next == Unknown)
                {
                    next = Step(state, slot, _classOf[c]);
                    if (_overflow)
                    {
                        return false;
                    }
                }

                if (next == Dead)
                {
                    return true;
                }

                state = next;
            }

            result = _accept[state];
            return true;
        }

        private int Step(int state, int slot, int cls)
        {
            lock (_gate)
            {
                int known = _next[slot];
                if (known != Unknown)
                {
                    return known;
                }

                char rep = _representative[cls];
                List<int> moved = [];
                foreach (int pc in _members[state])
                {
                    if (_re.Consumes(pc, rep))
                    {
                        moved.Add(pc + 1);
                    }
                }

                int[] set = Closure(moved);
                int target = set.Length == 0 ? Dead : Intern(set);
                if (!_overflow)
                {
                    Volatile.Write(ref _next[slot], target);
                }

                return target;
            }
        }

        // The consuming instructions (and the final Match) reachable from the given positions without reading a character.
        private int[] Closure(List<int> from)
        {
            bool[] seen = new bool[_re._program.Length];
            List<int> found = [];
            Stack<int> todo = new();
            for (int i = from.Count - 1; i >= 0; i--)
            {
                todo.Push(from[i]);
            }

            while (todo.Count > 0)
            {
                int pc = todo.Pop();
                if (seen[pc])
                {
                    continue;
                }

                seen[pc] = true;
                Inst inst = _re._program[pc];
                switch (inst.Op)
                {
                    case Op.Split:
                        todo.Push(inst.Y);
                        todo.Push(inst.X);
                        break;
                    case Op.Jump:
                        todo.Push(inst.X);
                        break;
                    case Op.Save:
                        todo.Push(pc + 1);
                        break;
                    default: // Char, Set, Any, Match
                        found.Add(pc);
                        break;
                }
            }

            found.Sort();
            return [.. found];
        }

        private int Intern(int[] set)
        {
            char[] chars = new char[set.Length];
            for (int i = 0; i < set.Length; i++)
            {
                chars[i] = (char)set[i];
            }

            string key = new(chars);
            if (_index.TryGetValue(key, out int existing))
            {
                return existing;
            }

            if (_count >= MaxStates)
            {
                _overflow = true;
                return 0;
            }

            int id = _count++;
            _members[id] = set;
            _accept[id] = Array.Exists(set, pc => _re._program[pc].Op == Op.Match);
            _index[key] = id;
            return id;
        }
    }

    private readonly struct Inst
    {
        internal Inst(Op op, int cp, int x, int y, bool fold = true)
        {
            Op = op;
            Cp = cp;
            Y = op == Op.Char ? (fold ? OtherCase(cp) : -1) : y;
            X = op == Op.Char ? cp : x;
        }

        // The other-case form of an ASCII letter; -1 for everything else. Whether letters fold at all is decided by the mode at match
        // time through the same lookup, so Exact patterns get the letter too and the fast path checks the mode once at construction.
        private static int OtherCase(int cp) => cp is >= 'a' and <= 'z' ? cp - 32 : cp is >= 'A' and <= 'Z' ? cp + 32 : -1;

        internal Op Op { get; }

        internal int Cp { get; }

        internal int X { get; }

        internal int Y { get; }
    }

    /// <summary>A set of code points: ASCII bitmap, ranges and Unicode categories, optionally negated.</summary>
    private sealed class CharSet
    {
        private readonly ulong[] _bits = new ulong[2];
        private readonly List<(int Lo, int Hi)> _ranges = [];
        private bool _decimalDigit;
        private bool _number;
        private bool _letter;
        private bool _separator;

        private ulong _asciiLow;
        private ulong _asciiHigh;

        internal bool Negated { get; set; }

        // Computes the answer for every ASCII character once, with the regex's case mode applied, so matching ASCII text is a bit test.
        internal void Freeze(CaseMode mode)
        {
            _asciiLow = 0;
            _asciiHigh = 0;
            for (int c = 0; c < 128; c++)
            {
                if (Contains(c, mode))
                {
                    AddAscii(ref _asciiLow, ref _asciiHigh, c);
                }
            }
        }

        internal bool AsciiHas(int c) => c < 64 ? (_asciiLow & (1UL << c)) != 0 : (_asciiHigh & (1UL << (c - 64))) != 0;

        internal bool Contains(int cp, CaseMode mode)
        {
            bool inSet = Raw(cp);
            if (!inSet && mode == CaseMode.Unicode)
            {
                int up = ToUpper(cp);
                inSet = Raw(up) || Raw(ToLower(up));
            }
            else if (!inSet && mode == CaseMode.Ascii && cp < 128 && char.IsAsciiLetter((char)cp))
            {
                inSet = Raw(cp ^ 0x20);
            }

            return inSet != Negated;
        }

        internal void Add(int lo, int hi)
        {
            for (int c = lo; c <= hi && c < 128; c++)
            {
                _bits[c >> 6] |= 1UL << (c & 63);
            }

            if (hi >= 128)
            {
                _ranges.Add((Math.Max(lo, 128), hi));
            }
        }

        internal void AddCategory(char which)
        {
            switch (which)
            {
                case 'd':
                    _decimalDigit = true;
                    break;
                case 'n':
                    _number = true;
                    break;
                case 'z':
                    _separator = true;
                    break;
                default:
                    _letter = true;
                    break;
            }
        }

        internal void AddSet(CharSet other, bool negate)
        {
            // Only used for the ASCII-only shorthand classes, which are plain bitmaps.
            for (int c = 0; c < 128; c++)
            {
                if (other.Raw(c) != negate)
                {
                    _bits[c >> 6] |= 1UL << (c & 63);
                }
            }

            if (negate)
            {
                _ranges.Add((128, 0x10FFFF));
            }
        }

        private bool Raw(int cp)
        {
            if (cp < 128 && (_bits[cp >> 6] & (1UL << (cp & 63))) != 0)
            {
                return true;
            }

            foreach ((int lo, int hi) in _ranges)
            {
                if (cp >= lo && cp <= hi)
                {
                    return true;
                }
            }

            if (_decimalDigit || _number || _letter || _separator)
            {
                if (cp > 0x10FFFF)
                {
                    return false;
                }

                UnicodeCategory cat = CharUnicodeInfo.GetUnicodeCategory(cp);
                if (_decimalDigit && cat == UnicodeCategory.DecimalDigitNumber)
                {
                    return true;
                }

                if (_number && cat is UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber)
                {
                    return true;
                }

                // \p{Z}: space, line and paragraph separators.
                if (_separator && cat is UnicodeCategory.SpaceSeparator or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
                {
                    return true;
                }

                if (_letter && cat is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter
                    or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private enum NodeKind
    {
        Char,
        Set,
        Any,
        Cat,
        Alt,
        Group,
        Repeat,
        Start,
        End,
        Empty,
    }

    private sealed class Node
    {
        internal NodeKind Kind;
        internal int Cp;
        internal CharSet? Set;
        internal List<Node> Kids = [];
        internal int Min;
        internal int Max;
        internal bool Lazy;
        internal int Group;
    }

    private sealed class Parser
    {
        private readonly string _p;
        private int _i;
        private int _depth;

        internal Parser(string pattern)
        {
            _p = pattern;
        }

        internal int Groups { get; private set; }

        internal Node ParseAll()
        {
            Node n = ParseAlt();
            if (_i != _p.Length)
            {
                throw Bad("unbalanced parenthesis");
            }

            return n;
        }

        private Node ParseAlt()
        {
            if (++_depth > 64)
            {
                throw Bad("nesting is too deep");
            }

            Node first = ParseCat();
            if (_i < _p.Length && _p[_i] == '|')
            {
                Node alt = new() { Kind = NodeKind.Alt };
                alt.Kids.Add(first);
                while (_i < _p.Length && _p[_i] == '|')
                {
                    _i++;
                    alt.Kids.Add(ParseCat());
                }

                _depth--;
                return alt;
            }

            _depth--;
            return first;
        }

        private Node ParseCat()
        {
            Node cat = new() { Kind = NodeKind.Cat };
            while (_i < _p.Length && _p[_i] is not ('|' or ')'))
            {
                cat.Kids.Add(ParseRepeat());
            }

            return cat.Kids.Count == 0 ? new Node { Kind = NodeKind.Empty } : cat;
        }

        private Node ParseRepeat()
        {
            Node atom = ParseAtom();
            while (_i < _p.Length)
            {
                int min;
                int max;
                char q = _p[_i];
                if (q == '?')
                {
                    min = 0;
                    max = 1;
                    _i++;
                }
                else if (q == '*')
                {
                    min = 0;
                    max = -1;
                    _i++;
                }
                else if (q == '+')
                {
                    min = 1;
                    max = -1;
                    _i++;
                }
                else if (q == '{' && IsBound())
                {
                    _i++;
                    min = Number();
                    max = min;
                    if (_p[_i] == ',')
                    {
                        _i++;
                        max = _p[_i] == '}' ? -1 : Number();
                    }

                    _i++; // '}'
                    if (max != -1 && max < min)
                    {
                        throw Bad("bad repeat bounds");
                    }
                }
                else
                {
                    break;
                }

                bool lazy = false;
                if (_i < _p.Length && _p[_i] == '?')
                {
                    lazy = true;
                    _i++;
                }
                else if (_i < _p.Length && _p[_i] == '+')
                {
                    _i++; // possessive: treated as greedy
                }

                if (min > 1000 || max > 1000)
                {
                    throw Bad("repeat count is too large");
                }

                atom = new Node { Kind = NodeKind.Repeat, Min = min, Max = max, Lazy = lazy, Kids = { atom } };
            }

            return atom;
        }

        private bool IsBound()
        {
            int j = _i + 1;
            int digits = 0;
            while (j < _p.Length && char.IsAsciiDigit(_p[j]))
            {
                j++;
                digits++;
            }

            if (digits == 0)
            {
                return false;
            }

            if (j < _p.Length && _p[j] == ',')
            {
                j++;
                while (j < _p.Length && char.IsAsciiDigit(_p[j]))
                {
                    j++;
                }
            }

            return j < _p.Length && _p[j] == '}';
        }

        private int Number()
        {
            int start = _i;
            long v = 0;
            while (_i < _p.Length && char.IsAsciiDigit(_p[_i]))
            {
                v = (v * 10) + (_p[_i] - '0');
                if (v > 100000)
                {
                    throw Bad("number is too large");
                }

                _i++;
            }

            if (_i == start)
            {
                throw Bad("missing number");
            }

            return (int)v;
        }

        private int ReadLiteral()
        {
            char c = _p[_i];
            if (char.IsHighSurrogate(c) && _i + 1 < _p.Length && char.IsLowSurrogate(_p[_i + 1]))
            {
                int cp = char.ConvertToUtf32(c, _p[_i + 1]);
                _i += 2;
                return cp;
            }

            _i++;
            return c;
        }

        private Node ParseAtom()
        {
            char c = _p[_i];
            switch (c)
            {
                case '(':
                {
                    _i++;
                    int group = 0;
                    if (_i + 1 < _p.Length && _p[_i] == '?' && _p[_i + 1] == ':')
                    {
                        _i += 2;
                    }
                    else if (_i < _p.Length && _p[_i] == '?')
                    {
                        throw Bad("group extensions are not supported");
                    }
                    else
                    {
                        group = ++Groups;
                        if (Groups > MaxGroups)
                        {
                            throw Bad("too many groups");
                        }
                    }

                    Node inner = ParseAlt();
                    if (_i >= _p.Length || _p[_i] != ')')
                    {
                        throw Bad("missing )");
                    }

                    _i++;
                    return new Node { Kind = NodeKind.Group, Group = group, Kids = { inner } };
                }

                case '[':
                    return ParseClass();
                case '.':
                    _i++;
                    return new Node { Kind = NodeKind.Any };
                case '^':
                    _i++;
                    return new Node { Kind = NodeKind.Start };
                case '$':
                    _i++;
                    return new Node { Kind = NodeKind.End };
                case '\\':
                    return ParseEscape();
                case '*' or '+' or '?':
                    throw Bad("nothing to repeat");
                default:
                    return new Node { Kind = NodeKind.Char, Cp = ReadLiteral() };
            }
        }

        private Node ParseEscape()
        {
            _i++;
            if (_i >= _p.Length)
            {
                throw Bad("dangling backslash");
            }

            char e = _p[_i++];
            switch (e)
            {
                case 'd' or 'D' or 's' or 'S' or 'w' or 'W':
                {
                    CharSet set = new();
                    AddShorthand(set, e);
                    return new Node { Kind = NodeKind.Set, Set = set };
                }

                case 'p' or 'P':
                {
                    CharSet set = new() { Negated = e == 'P' };
                    set.AddCategory(ReadCategory());
                    return new Node { Kind = NodeKind.Set, Set = set };
                }

                case 'n':
                    return new Node { Kind = NodeKind.Char, Cp = '\n' };
                case 't':
                    return new Node { Kind = NodeKind.Char, Cp = '\t' };
                case 'r':
                    return new Node { Kind = NodeKind.Char, Cp = '\r' };
                default:
                    if (char.IsAsciiLetterOrDigit(e))
                    {
                        throw Bad("unsupported escape");
                    }

                    _i--;
                    return new Node { Kind = NodeKind.Char, Cp = ReadLiteral() };
            }
        }

        // Reads {Nd}, {N}, {L} or {Z} after \p or \P and returns d, n or l.
        private char ReadCategory()
        {
            if (_i < _p.Length && _p[_i] == '{')
            {
                int close = _p.IndexOf('}', _i);
                if (close > 0)
                {
                    string name = _p[(_i + 1)..close];
                    _i = close + 1;
                    switch (name)
                    {
                        case "Nd": return 'd';
                        case "N": return 'n';
                        case "L": return 'l';
                        case "Z": return 'z';
                        default: break;
                    }
                }
            }

            throw Bad("unsupported category");
        }

        private static void AddShorthand(CharSet set, char e)
        {
            CharSet basis = new();
            switch (char.ToLowerInvariant(e))
            {
                case 'd':
                    basis.Add('0', '9');
                    break;
                case 's':
                    basis.Add(' ', ' ');
                    basis.Add('\t', '\r');
                    break;
                default: // w
                    basis.Add('0', '9');
                    basis.Add('a', 'z');
                    basis.Add('A', 'Z');
                    basis.Add('_', '_');
                    break;
            }

            set.AddSet(basis, char.IsAsciiLetterUpper(e));
        }

        private Node ParseClass()
        {
            _i++; // [
            CharSet set = new();
            if (_i < _p.Length && _p[_i] == '^')
            {
                set.Negated = true;
                _i++;
            }

            bool first = true;
            while (true)
            {
                if (_i >= _p.Length)
                {
                    throw Bad("missing ]");
                }

                char c = _p[_i];
                if (c == ']' && !first)
                {
                    _i++;
                    break;
                }

                first = false;
                if (c == '[')
                {
                    throw Bad("nested classes are not supported");
                }

                int lo;
                if (c == '\\')
                {
                    _i++;
                    if (_i >= _p.Length)
                    {
                        throw Bad("dangling backslash");
                    }

                    char e = _p[_i++];
                    if (e is 'd' or 'D' or 's' or 'S' or 'w' or 'W')
                    {
                        AddShorthand(set, e);
                        continue;
                    }

                    if (e == 'p')
                    {
                        set.AddCategory(ReadCategory());
                        continue;
                    }

                    if (e == 'P')
                    {
                        throw Bad("negated categories inside classes are not supported");
                    }

                    if (char.IsAsciiLetterOrDigit(e) && e is not ('n' or 't' or 'r'))
                    {
                        throw Bad("unsupported escape");
                    }

                    if (e is 'n' or 't' or 'r')
                    {
                        lo = e switch { 'n' => '\n', 't' => '\t', _ => '\r' };
                    }
                    else
                    {
                        _i--;
                        lo = ReadLiteral();
                    }
                }
                else
                {
                    lo = ReadLiteral();
                }

                int hi = lo;
                if (_i + 1 < _p.Length && _p[_i] == '-' && _p[_i + 1] != ']')
                {
                    _i++;
                    if (_p[_i] == '\\')
                    {
                        _i++;
                        if (_i >= _p.Length)
                        {
                            throw Bad("dangling backslash");
                        }
                    }

                    hi = ReadLiteral();
                    if (hi < lo)
                    {
                        throw Bad("bad range");
                    }
                }

                set.Add(lo, hi);
            }

            return new Node { Kind = NodeKind.Set, Set = set };
        }

        private InvalidDataException Bad(string why)
        {
            StringBuilder sb = new("Unsupported phone pattern (");
            sb.Append(why).Append(") at position ").Append(_i);
            return new InvalidDataException(sb.ToString());
        }
    }

    private sealed class Compiler
    {
        private readonly List<Inst> _code = [];
        private readonly List<CharSet> _sets = [];

        internal List<CharSet> Sets => _sets;

        internal Inst[] Program() => [.. _code];

        internal int Add(Inst inst)
        {
            if (_code.Count >= MaxInstructions)
            {
                throw new InvalidDataException("A phone pattern is too large.");
            }

            _code.Add(inst);
            return _code.Count - 1;
        }

        internal void Emit(Node n)
        {
            switch (n.Kind)
            {
                case NodeKind.Empty:
                    break;
                case NodeKind.Char:
                    Add(new Inst(Op.Char, n.Cp, 0, 0));
                    break;
                case NodeKind.Set:
                    _sets.Add(n.Set!);
                    Add(new Inst(Op.Set, 0, _sets.Count - 1, 0));
                    break;
                case NodeKind.Any:
                    Add(new Inst(Op.Any, 0, 0, 0));
                    break;
                case NodeKind.Start:
                    Add(new Inst(Op.AssertStart, 0, 0, 0));
                    break;
                case NodeKind.End:
                    Add(new Inst(Op.AssertEnd, 0, 0, 0));
                    break;
                case NodeKind.Cat:
                    foreach (Node k in n.Kids)
                    {
                        Emit(k);
                    }

                    break;
                case NodeKind.Alt:
                    EmitAlt(n);
                    break;
                case NodeKind.Group:
                    if (n.Group > 0)
                    {
                        Add(new Inst(Op.Save, 0, 2 * n.Group, 0));
                        Emit(n.Kids[0]);
                        Add(new Inst(Op.Save, 0, (2 * n.Group) + 1, 0));
                    }
                    else
                    {
                        Emit(n.Kids[0]);
                    }

                    break;
                default:
                    EmitRepeat(n);
                    break;
            }
        }

        private void EmitAlt(Node n)
        {
            List<int> jumps = [];
            for (int i = 0; i < n.Kids.Count; i++)
            {
                if (i < n.Kids.Count - 1)
                {
                    int split = Add(new Inst(Op.Split, 0, 0, 0));
                    int bodyStart = _code.Count;
                    Emit(n.Kids[i]);
                    jumps.Add(Add(new Inst(Op.Jump, 0, 0, 0)));
                    _code[split] = new Inst(Op.Split, 0, bodyStart, _code.Count);
                }
                else
                {
                    Emit(n.Kids[i]);
                }
            }

            foreach (int j in jumps)
            {
                _code[j] = new Inst(Op.Jump, 0, _code.Count, 0);
            }
        }

        private void EmitRepeat(Node n)
        {
            Node body = n.Kids[0];
            for (int i = 0; i < n.Min; i++)
            {
                Emit(body);
            }

            if (n.Max == -1)
            {
                int split = Add(new Inst(Op.Split, 0, 0, 0));
                int bodyStart = _code.Count;
                Emit(body);
                Add(new Inst(Op.Jump, 0, split, 0));
                _code[split] = Split(n.Lazy, bodyStart, _code.Count);
                return;
            }

            List<int> splits = [];
            for (int i = n.Min; i < n.Max; i++)
            {
                splits.Add(Add(new Inst(Op.Split, 0, 0, 0)));
                Emit(body);
            }

            for (int s = 0; s < splits.Count; s++)
            {
                _code[splits[s]] = Split(n.Lazy, splits[s] + 1, _code.Count);
            }
        }

        private static Inst Split(bool lazy, int body, int exit) =>
            lazy ? new Inst(Op.Split, 0, exit, body) : new Inst(Op.Split, 0, body, exit);
    }
}
