using System.Text;

namespace RecursiveAI.OneContact.Addresses.Structural;

/// <summary>
/// A matcher for the small regular-expression dialect the address data uses for postal codes: alternation, groups
/// (<c>(...)</c> and <c>(?:...)</c>), character classes with ranges and negation, <c>\d</c>, <c>\w</c>, <c>\s</c> and escaped
/// punctuation, <c>.</c>, and the quantifiers <c>?</c>, <c>*</c>, <c>+</c>, <c>{n}</c>, <c>{n,}</c> and <c>{n,m}</c>. It is a
/// backtracking matcher over a compiled program, with a step budget so a hostile pattern cannot run for long. It is used instead
/// of <see cref="System.Text.RegularExpressions.Regex"/> so nothing is built from text at run time, which keeps the library
/// trimmable and NativeAOT safe. Anything outside the dialect is refused when the pattern is compiled.
/// </summary>
internal sealed class PostalPattern
{
    private const int StepBudget = 200_000;
    private const int MaxDepth = 500;
    private const int MaxProgram = 20_000;
    private const int MaxRepeat = 100;

    private readonly Instruction[] _program;
    private readonly CharClass[] _classes;

    private PostalPattern(Instruction[] program, CharClass[] classes)
    {
        _program = program;
        _classes = classes;
    }

    private enum Op : byte
    {
        Char,
        Any,
        Class,
        Split,
        Jump,
        Match,
    }

    /// <summary>Compiles a pattern.</summary>
    /// <param name="pattern">The pattern text.</param>
    /// <returns>The matcher.</returns>
    /// <exception cref="FormatException">The pattern is outside the supported dialect.</exception>
    internal static PostalPattern Compile(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        Parser parser = new(pattern);
        Node root = parser.ParseAll();
        Compiler compiler = new();
        compiler.Emit(root);
        compiler.Add(new Instruction(Op.Match, 0, 0));
        return new PostalPattern([.. compiler.Program], [.. parser.Classes]);
    }

    /// <summary>Tests whether the whole text matches (Java <c>matches</c>).</summary>
    /// <param name="text">The text.</param>
    /// <param name="ignoreCase">Compare letters ignoring case (invariant folding).</param>
    /// <returns><see langword="true"/> on a match.</returns>
    internal bool IsMatch(ReadOnlySpan<char> text, bool ignoreCase) => Run(text, ignoreCase, wholeText: true);

    /// <summary>Tests whether the text starts with a match (Java <c>lookingAt</c>).</summary>
    /// <param name="text">The text.</param>
    /// <param name="ignoreCase">Compare letters ignoring case (invariant folding).</param>
    /// <returns><see langword="true"/> when some prefix matches.</returns>
    internal bool IsPrefixMatch(ReadOnlySpan<char> text, bool ignoreCase) => Run(text, ignoreCase, wholeText: false);

    private bool Run(ReadOnlySpan<char> text, bool ignoreCase, bool wholeText)
    {
        int steps = 0;
        return Step(0, 0, 0, text, ignoreCase, wholeText, ref steps);
    }

    private bool Step(int pc, int pos, int depth, ReadOnlySpan<char> text, bool ignoreCase, bool wholeText, ref int steps)
    {
        while (true)
        {
            // The depth limit keeps the recursion on the stack small whatever the pattern; the step budget bounds the time.
            if (++steps > StepBudget || depth > MaxDepth)
            {
                return false;
            }

            Instruction i = _program[pc];
            switch (i.Op)
            {
                case Op.Match:
                    return !wholeText || pos == text.Length;
                case Op.Char:
                    if (pos >= text.Length || !Same(text[pos], (char)i.X, ignoreCase))
                    {
                        return false;
                    }

                    pos++;
                    pc++;
                    break;
                case Op.Any:
                    // Java's "." does not match a line terminator.
                    if (pos >= text.Length || text[pos] is '\n' or '\r' or '\u0085' or '\u2028' or '\u2029')
                    {
                        return false;
                    }

                    pos++;
                    pc++;
                    break;
                case Op.Class:
                    if (pos >= text.Length || !_classes[i.X].Contains(text[pos], ignoreCase))
                    {
                        return false;
                    }

                    pos++;
                    pc++;
                    break;
                case Op.Jump:
                    pc = i.X;
                    break;
                case Op.Split:
                    if (Step(i.X, pos, depth + 1, text, ignoreCase, wholeText, ref steps))
                    {
                        return true;
                    }

                    pc = i.Y;
                    break;
                default:
                    return false;
            }
        }
    }

    private static bool Same(char a, char b, bool ignoreCase) =>
        a == b || (ignoreCase && char.ToUpperInvariant(a) == char.ToUpperInvariant(b));

    private readonly record struct Instruction(Op Op, int X, int Y);

    private sealed class CharClass
    {
        private readonly List<(char Low, char High)> _ranges = [];

        internal bool Negated { get; set; }

        internal void Add(char low, char high) => _ranges.Add((low, high));

        internal void Merge(CharClass other) => _ranges.AddRange(other._ranges);

        internal bool Contains(char c, bool ignoreCase)
        {
            bool inside = Has(c);
            if (!inside && ignoreCase)
            {
                char upper = char.ToUpperInvariant(c);
                char lower = char.ToLowerInvariant(c);
                inside = (upper != c && Has(upper)) || (lower != c && Has(lower));
            }

            return inside != Negated;
        }

        private bool Has(char c)
        {
            foreach ((char low, char high) in _ranges)
            {
                if (c >= low && c <= high)
                {
                    return true;
                }
            }

            return false;
        }
    }

    // --- the syntax tree ---

    private abstract record Node;

    private sealed record Literal(char Value) : Node;

    private sealed record AnyChar : Node;

    private sealed record ClassNode(int Index) : Node;

    private sealed record Sequence(List<Node> Items) : Node;

    private sealed record Alternation(List<Node> Choices) : Node;

    private sealed record Repeat(Node Body, int Min, int Max) : Node;

    private sealed class Parser
    {
        private readonly string _text;
        private int _pos;

        internal Parser(string text) => _text = text;

        internal List<CharClass> Classes { get; } = [];

        internal Node ParseAll()
        {
            Node node = ParseAlternation();
            if (_pos != _text.Length)
            {
                throw Error("unbalanced parenthesis");
            }

            return node;
        }

        private FormatException Error(string why) =>
            new($"Unsupported postal code pattern '{_text}': {why} at {_pos}.");

        private Node ParseAlternation()
        {
            List<Node> choices = [ParseSequence()];
            while (_pos < _text.Length && _text[_pos] == '|')
            {
                _pos++;
                choices.Add(ParseSequence());
            }

            return choices.Count == 1 ? choices[0] : new Alternation(choices);
        }

        private Sequence ParseSequence()
        {
            List<Node> items = [];
            while (_pos < _text.Length && _text[_pos] is not ('|' or ')'))
            {
                items.Add(ParseQuantified());
            }

            return new Sequence(items);
        }

        private Node ParseQuantified()
        {
            Node atom = ParseAtom();
            while (_pos < _text.Length)
            {
                char c = _text[_pos];
                int min;
                int max;
                if (c == '?')
                {
                    min = 0;
                    max = 1;
                    _pos++;
                }
                else if (c == '*')
                {
                    min = 0;
                    max = -1;
                    _pos++;
                }
                else if (c == '+')
                {
                    min = 1;
                    max = -1;
                    _pos++;
                }
                else if (c == '{')
                {
                    (min, max) = ParseBraces();
                }
                else
                {
                    break;
                }

                if (_pos < _text.Length && _text[_pos] is '?' or '+')
                {
                    throw Error("lazy and possessive quantifiers are not supported");
                }

                atom = new Repeat(atom, min, max);
            }

            return atom;
        }

        private (int Min, int Max) ParseBraces()
        {
            int close = _text.IndexOf('}', _pos);
            if (close < 0)
            {
                throw Error("unterminated {");
            }

            string body = _text[(_pos + 1)..close];
            _pos = close + 1;
            string[] parts = body.Split(',');
            if (parts.Length > 2 || !TryCount(parts[0], out int min))
            {
                throw Error("bad repeat count");
            }

            if (parts.Length == 1)
            {
                return (min, min);
            }

            if (parts[1].Length == 0)
            {
                return (min, -1);
            }

            if (!TryCount(parts[1], out int max) || max < min)
            {
                throw Error("bad repeat count");
            }

            return (min, max);
        }

        private static bool TryCount(string text, out int value) =>
            int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out value) && value <= MaxRepeat;

        private Node ParseAtom()
        {
            char c = _text[_pos++];
            switch (c)
            {
                case '(':
                    if (_pos < _text.Length && _text[_pos] == '?')
                    {
                        if (_pos + 1 < _text.Length && _text[_pos + 1] == ':')
                        {
                            _pos += 2;
                        }
                        else
                        {
                            throw Error("only (?:...) groups are supported");
                        }
                    }

                    Node inner = ParseAlternation();
                    if (_pos >= _text.Length || _text[_pos] != ')')
                    {
                        throw Error("unbalanced parenthesis");
                    }

                    _pos++;
                    return inner;
                case '[':
                    return ParseClass();
                case '.':
                    return new AnyChar();
                case '\\':
                    return ParseEscape(inClass: null);
                case '*' or '+' or '?' or '{':
                    throw Error("nothing to repeat");
                case '^' or '$':
                    throw Error("anchors are not supported");
                default:
                    return new Literal(c);
            }
        }

        private Node ParseEscape(CharClass? inClass)
        {
            if (_pos >= _text.Length)
            {
                throw Error("dangling backslash");
            }

            char e = _text[_pos++];
            CharClass? shorthand = e switch
            {
                'd' => Make(false, ('0', '9')),
                'D' => Make(true, ('0', '9')),
                'w' => Make(false, ('a', 'z'), ('A', 'Z'), ('0', '9'), ('_', '_')),
                'W' => Make(true, ('a', 'z'), ('A', 'Z'), ('0', '9'), ('_', '_')),
                's' => Make(false, (' ', ' '), ('\t', '\r')),
                'S' => Make(true, (' ', ' '), ('\t', '\r')),
                _ => null,
            };

            if (shorthand is not null)
            {
                if (inClass is not null)
                {
                    if (shorthand.Negated)
                    {
                        throw Error("negated shorthand inside a class is not supported");
                    }

                    inClass.Merge(shorthand);
                    return new AnyChar();
                }

                Classes.Add(shorthand);
                return new ClassNode(Classes.Count - 1);
            }

            if (char.IsAsciiLetterOrDigit(e))
            {
                throw Error($"unsupported escape \\{e}");
            }

            return new Literal(e);
        }

        private static CharClass Make(bool negated, params (char Low, char High)[] ranges)
        {
            CharClass c = new() { Negated = negated };
            foreach ((char low, char high) in ranges)
            {
                c.Add(low, high);
            }

            return c;
        }

        private ClassNode ParseClass()
        {
            CharClass cls = new();
            if (_pos < _text.Length && _text[_pos] == '^')
            {
                cls.Negated = true;
                _pos++;
            }

            bool first = true;
            while (true)
            {
                if (_pos >= _text.Length)
                {
                    throw Error("unterminated character class");
                }

                char c = _text[_pos++];
                if (c == ']' && !first)
                {
                    break;
                }

                first = false;
                char low;
                if (c == '\\')
                {
                    int before = _pos;
                    if (_pos < _text.Length && _text[_pos] is 'd' or 'w' or 's' or 'D' or 'W' or 'S')
                    {
                        ParseEscape(cls);
                        continue;
                    }

                    _pos = before;
                    if (_pos >= _text.Length)
                    {
                        throw Error("dangling backslash");
                    }

                    low = _text[_pos++];
                    if (char.IsAsciiLetterOrDigit(low))
                    {
                        throw Error($"unsupported escape \\{low}");
                    }
                }
                else if (c is '[' or '&')
                {
                    throw Error("nested classes and intersections are not supported");
                }
                else
                {
                    low = c;
                }

                if (_pos + 1 < _text.Length && _text[_pos] == '-' && _text[_pos + 1] != ']')
                {
                    _pos++;
                    char high = _text[_pos++];
                    if (high == '\\')
                    {
                        if (_pos >= _text.Length)
                        {
                            throw Error("dangling backslash");
                        }

                        high = _text[_pos++];
                    }

                    if (high < low)
                    {
                        throw Error("reversed range");
                    }

                    cls.Add(low, high);
                }
                else
                {
                    cls.Add(low, low);
                }
            }

            Classes.Add(cls);
            return new ClassNode(Classes.Count - 1);
        }
    }

    private sealed class Compiler
    {
        internal List<Instruction> Program { get; } = [];

        internal int Add(Instruction instruction)
        {
            if (Program.Count >= MaxProgram)
            {
                throw new FormatException("The postal code pattern is too large.");
            }

            Program.Add(instruction);
            return Program.Count - 1;
        }

        internal void Emit(Node node)
        {
            switch (node)
            {
                case Literal l:
                    Add(new Instruction(Op.Char, l.Value, 0));
                    break;
                case AnyChar:
                    Add(new Instruction(Op.Any, 0, 0));
                    break;
                case ClassNode c:
                    Add(new Instruction(Op.Class, c.Index, 0));
                    break;
                case Sequence s:
                    foreach (Node item in s.Items)
                    {
                        Emit(item);
                    }

                    break;
                case Alternation a:
                    EmitAlternation(a);
                    break;
                case Repeat r:
                    EmitRepeat(r);
                    break;
                default:
                    throw new InvalidOperationException();
            }
        }

        private void EmitAlternation(Alternation a)
        {
            List<int> jumps = [];
            for (int i = 0; i < a.Choices.Count; i++)
            {
                if (i < a.Choices.Count - 1)
                {
                    int split = Add(new Instruction(Op.Split, 0, 0));
                    Emit(a.Choices[i]);
                    jumps.Add(Add(new Instruction(Op.Jump, 0, 0)));
                    Program[split] = new Instruction(Op.Split, split + 1, Program.Count);
                }
                else
                {
                    Emit(a.Choices[i]);
                }
            }

            foreach (int j in jumps)
            {
                Program[j] = new Instruction(Op.Jump, Program.Count, 0);
            }
        }

        private void EmitRepeat(Repeat r)
        {
            for (int i = 0; i < r.Min; i++)
            {
                Emit(r.Body);
            }

            if (r.Max < 0)
            {
                // body*: split(L1, L3); L1: body; jump L0
                int split = Add(new Instruction(Op.Split, 0, 0));
                Emit(r.Body);
                Add(new Instruction(Op.Jump, split, 0));
                Program[split] = new Instruction(Op.Split, split + 1, Program.Count);
                return;
            }

            // Optional copies nest: (body(body(body)?)?)? keeps the greedy order and needs one exit target.
            List<int> splits = [];
            for (int i = r.Min; i < r.Max; i++)
            {
                splits.Add(Add(new Instruction(Op.Split, 0, 0)));
                Emit(r.Body);
            }

            foreach (int s in splits)
            {
                Program[s] = new Instruction(Op.Split, s + 1, Program.Count);
            }
        }
    }
}
