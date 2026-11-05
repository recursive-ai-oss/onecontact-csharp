using System.Text;

namespace RecursiveAI.OneContact.Oracles.Phones;

/// <summary>
/// Draws random strings that match a metadata pattern (digits, classes, groups, alternation and the usual quantifiers). It only needs to
/// be plausible: the oracle decides whether a drawn number is really valid, so a sample that misses is a harmless extra case.
/// </summary>
internal sealed class PatternSampler
{
    private readonly string _p;
    private int _i;

    private PatternSampler(string pattern)
    {
        _p = pattern;
    }

    internal static string? Sample(string pattern, Random rng)
    {
        try
        {
            PatternSampler s = new(pattern);
            Node root = s.ParseAlt();
            StringBuilder sb = new();
            root.Emit(rng, sb);
            return sb.Length > 40 ? null : sb.ToString();
        }
        catch (Exception e) when (e is InvalidOperationException or IndexOutOfRangeException or ArgumentOutOfRangeException or FormatException)
        {
            return null;
        }
    }

    /// <summary>Draws several samples and keeps one per distinct length, preferring the wanted lengths.</summary>
    internal static List<string> SampleByLength(string pattern, Random rng, ISet<int> wanted, int max)
    {
        Dictionary<int, string> byLength = [];
        for (int attempt = 0; attempt < 60 && byLength.Count < 8; attempt++)
        {
            string? s = Sample(pattern, rng);
            if (s is { Length: > 0 } && !byLength.ContainsKey(s.Length))
            {
                byLength[s.Length] = s;
            }
        }

        return [.. byLength.OrderBy(kv => wanted.Contains(kv.Key) ? 0 : 1).ThenBy(kv => kv.Key).Select(kv => kv.Value).Take(max)];
    }

    private abstract class Node
    {
        internal abstract void Emit(Random rng, StringBuilder sb);
    }

    private sealed class Lit(char c) : Node
    {
        internal override void Emit(Random rng, StringBuilder sb) => sb.Append(c);
    }

    private sealed class Cls(List<char> options) : Node
    {
        internal override void Emit(Random rng, StringBuilder sb) => sb.Append(options[rng.Next(options.Count)]);
    }

    private sealed class Seq(List<Node> kids) : Node
    {
        internal override void Emit(Random rng, StringBuilder sb)
        {
            foreach (Node k in kids)
            {
                k.Emit(rng, sb);
            }
        }
    }

    private sealed class Alt(List<Node> kids) : Node
    {
        internal override void Emit(Random rng, StringBuilder sb) => kids[rng.Next(kids.Count)].Emit(rng, sb);
    }

    private sealed class Rep(Node kid, int min, int max) : Node
    {
        internal override void Emit(Random rng, StringBuilder sb)
        {
            int n = min + rng.Next(max - min + 1);
            for (int i = 0; i < n; i++)
            {
                kid.Emit(rng, sb);
            }
        }
    }

    private Node ParseAlt()
    {
        List<Node> alts = [ParseSeq()];
        while (_i < _p.Length && _p[_i] == '|')
        {
            _i++;
            alts.Add(ParseSeq());
        }

        return alts.Count == 1 ? alts[0] : new Alt(alts);
    }

    private Seq ParseSeq()
    {
        List<Node> items = [];
        while (_i < _p.Length && _p[_i] is not ('|' or ')'))
        {
            items.Add(ParseRepeat());
        }

        return new Seq(items);
    }

    private Node ParseRepeat()
    {
        Node atom = ParseAtom();
        while (_i < _p.Length)
        {
            char q = _p[_i];
            if (q == '?')
            {
                atom = new Rep(atom, 0, 1);
                _i++;
            }
            else if (q == '*')
            {
                atom = new Rep(atom, 0, 3);
                _i++;
            }
            else if (q == '+')
            {
                atom = new Rep(atom, 1, 4);
                _i++;
            }
            else if (q == '{')
            {
                int close = _p.IndexOf('}', _i);
                string[] parts = _p[(_i + 1)..close].Split(',');
                int min = int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
                int max = parts.Length == 1 ? min : parts[1].Length == 0 ? min + 3 : int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                atom = new Rep(atom, min, max);
                _i = close + 1;
            }
            else
            {
                break;
            }
        }

        return atom;
    }

    private Node ParseAtom()
    {
        char c = _p[_i++];
        switch (c)
        {
            case '(':
                if (_p[_i] == '?' && _p[_i + 1] == ':')
                {
                    _i += 2;
                }

                Node inner = ParseAlt();
                _i++; // )
                return inner;
            case '[':
                return ParseClass();
            case '\\':
                char e = _p[_i++];
                if (e == 'd')
                {
                    return new Cls([.. "0123456789"]);
                }

                return new Lit(e);
            case '^' or '$':
                return new Seq([]);
            case '.':
                return new Cls([.. "0123456789"]);
            default:
                return new Lit(c);
        }
    }

    private Cls ParseClass()
    {
        bool negated = false;
        if (_p[_i] == '^')
        {
            negated = true;
            _i++;
        }

        List<char> chars = [];
        while (_p[_i] != ']')
        {
            char c = _p[_i++];
            if (c == '\\')
            {
                char e = _p[_i++];
                if (e == 'd')
                {
                    chars.AddRange("0123456789");
                    continue;
                }

                c = e;
            }

            if (_p[_i] == '-' && _p[_i + 1] != ']')
            {
                char hi = _p[_i + 1];
                _i += 2;
                for (char x = c; x <= hi; x++)
                {
                    chars.Add(x);
                }
            }
            else
            {
                chars.Add(c);
            }
        }

        _i++;
        if (negated)
        {
            chars = [.. "0123456789".Where(d => !chars.Contains(d))];
        }

        if (chars.Count == 0)
        {
            throw new InvalidOperationException("empty class");
        }

        return new Cls(chars);
    }
}
