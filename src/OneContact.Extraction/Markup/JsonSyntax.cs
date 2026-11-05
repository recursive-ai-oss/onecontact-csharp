using System.Globalization;
using System.Text;

namespace RecursiveAI.OneContact.Extraction.Markup;

internal abstract class JsonNode
{
    /// <summary>Offset of the first character of the value in the script text.</summary>
    public int Start { get; init; }

    /// <summary>Offset after the last character of the value.</summary>
    public int End { get; init; }
}

internal sealed class JsonObject : JsonNode
{
    public List<KeyValuePair<string, JsonNode>> Members { get; } = [];
}

internal sealed class JsonArray : JsonNode
{
    public List<JsonNode> Items { get; } = [];
}

internal sealed class JsonString : JsonNode
{
    public required string Value { get; init; }
}

/// <summary>A number, <c>true</c>, <c>false</c> or <c>null</c>, kept as written.</summary>
internal sealed class JsonScalar : JsonNode
{
    public required string Literal { get; init; }

    public bool IsNumber => Literal.Length > 0 && (Literal[0] == '-' || char.IsAsciiDigit(Literal[0]));
}

/// <summary>
/// A strict JSON reader (RFC 8259) that keeps the offset of every value, which <c>System.Text.Json</c> cannot give for text that was
/// decoded from a page. Anything outside the grammar (a trailing comma, a comment, a raw control character in a string, text after
/// the value) makes the whole block unreadable: a half-read block would yield half-true records.
/// </summary>
internal sealed class JsonSyntax
{
    /// <summary>Nesting deeper than this is refused, so hostile input cannot exhaust the stack.</summary>
    internal const int MaxDepth = 64;

    private readonly string _s;
    private int _i;

    private JsonSyntax(string s)
    {
        _s = s;
    }

    /// <summary>Where reading stopped when it failed.</summary>
    public int ErrorOffset { get; private set; }

    public static JsonNode? TryParse(string text, out int errorOffset)
    {
        var p = new JsonSyntax(text);
        JsonNode? node = null;
        try
        {
            p.SkipWhitespace();
            node = p.ReadValue(0);
            p.SkipWhitespace();
            if (node is not null && p._i != text.Length)
            {
                p.ErrorOffset = p._i;
                node = null;
            }
        }
        catch (FormatException)
        {
            node = null;
        }

        errorOffset = p.ErrorOffset;
        return node;
    }

    private static void Fail() => throw new FormatException();

    private void SkipWhitespace()
    {
        while (_i < _s.Length && _s[_i] is ' ' or '\t' or '\n' or '\r')
        {
            _i++;
        }
    }

    private JsonNode ReadValue(int depth)
    {
        if (_i >= _s.Length)
        {
            ErrorOffset = _i;
            Fail();
        }

        char c = _s[_i];
        switch (c)
        {
            case '{':
                return ReadObject(depth);
            case '[':
                return ReadArray(depth);
            case '"':
                return ReadString();
            default:
                return ReadScalar();
        }
    }

    private JsonObject ReadObject(int depth)
    {
        if (depth >= MaxDepth)
        {
            ErrorOffset = _i;
            Fail();
        }

        int start = _i++;
        var members = new List<KeyValuePair<string, JsonNode>>();
        SkipWhitespace();
        if (_i < _s.Length && _s[_i] == '}')
        {
            _i++;
            return Build(start, members);
        }

        while (true)
        {
            SkipWhitespace();
            if (_i >= _s.Length || _s[_i] != '"')
            {
                ErrorOffset = _i;
                Fail();
            }

            JsonString key = ReadString();
            SkipWhitespace();
            if (_i >= _s.Length || _s[_i] != ':')
            {
                ErrorOffset = _i;
                Fail();
            }

            _i++;
            SkipWhitespace();
            JsonNode value = ReadValue(depth + 1);
            members.Add(new KeyValuePair<string, JsonNode>(key.Value, value));
            SkipWhitespace();
            if (_i >= _s.Length)
            {
                ErrorOffset = _i;
                Fail();
            }

            if (_s[_i] == ',')
            {
                _i++;
                continue;
            }

            if (_s[_i] == '}')
            {
                _i++;
                return Build(start, members);
            }

            ErrorOffset = _i;
            Fail();
        }
    }

    private JsonObject Build(int start, List<KeyValuePair<string, JsonNode>> members)
    {
        var o = new JsonObject { Start = start, End = _i };
        o.Members.AddRange(members);
        return o;
    }

    private JsonArray ReadArray(int depth)
    {
        if (depth >= MaxDepth)
        {
            ErrorOffset = _i;
            Fail();
        }

        int start = _i++;
        var items = new List<JsonNode>();
        SkipWhitespace();
        if (_i < _s.Length && _s[_i] == ']')
        {
            _i++;
            var empty = new JsonArray { Start = start, End = _i };
            return empty;
        }

        while (true)
        {
            SkipWhitespace();
            items.Add(ReadValue(depth + 1));
            SkipWhitespace();
            if (_i >= _s.Length)
            {
                ErrorOffset = _i;
                Fail();
            }

            if (_s[_i] == ',')
            {
                _i++;
                continue;
            }

            if (_s[_i] == ']')
            {
                _i++;
                var a = new JsonArray { Start = start, End = _i };
                a.Items.AddRange(items);
                return a;
            }

            ErrorOffset = _i;
            Fail();
        }
    }

    private JsonString ReadString()
    {
        int start = _i++;
        StringBuilder? sb = null;
        int runStart = _i;
        while (true)
        {
            if (_i >= _s.Length)
            {
                ErrorOffset = _i;
                Fail();
            }

            char c = _s[_i];
            if (c == '"')
            {
                string value;
                if (sb is null)
                {
                    value = _s.Substring(runStart, _i - runStart);
                }
                else
                {
                    sb.Append(_s, runStart, _i - runStart);
                    value = sb.ToString();
                }

                _i++;
                return new JsonString { Start = start, End = _i, Value = value };
            }

            if (c < 0x20)
            {
                ErrorOffset = _i;
                Fail();
            }

            if (c != '\\')
            {
                _i++;
                continue;
            }

            sb ??= new StringBuilder();
            sb.Append(_s, runStart, _i - runStart);
            _i++;
            if (_i >= _s.Length)
            {
                ErrorOffset = _i;
                Fail();
            }

            char e = _s[_i++];
            switch (e)
            {
                case '"':
                case '\\':
                case '/':
                    sb.Append(e);
                    break;
                case 'b':
                    sb.Append('\b');
                    break;
                case 'f':
                    sb.Append('\f');
                    break;
                case 'n':
                    sb.Append('\n');
                    break;
                case 'r':
                    sb.Append('\r');
                    break;
                case 't':
                    sb.Append('\t');
                    break;
                case 'u':
                    if (_i + 4 > _s.Length || !IsFourHex(_s.AsSpan(_i, 4)))
                    {
                        ErrorOffset = _i;
                        Fail();
                    }

                    sb.Append((char)ushort.Parse(_s.AsSpan(_i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
                    _i += 4;
                    break;
                default:
                    ErrorOffset = _i - 1;
                    Fail();
                    break;
            }

            runStart = _i;
        }
    }

    // RFC 8259 §7: exactly four hexadecimal digits follow \u.
    private static bool IsFourHex(ReadOnlySpan<char> s)
    {
        foreach (char c in s)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    private JsonScalar ReadScalar()
    {
        int start = _i;
        if (Match("true") || Match("false") || Match("null"))
        {
            return new JsonScalar { Start = start, End = _i, Literal = _s.Substring(start, _i - start) };
        }

        // RFC 8259 §6: -? int frac? exp?, with no leading zeros and no bare dot.
        if (_i < _s.Length && _s[_i] == '-')
        {
            _i++;
        }

        if (_i >= _s.Length || !char.IsAsciiDigit(_s[_i]))
        {
            ErrorOffset = _i;
            Fail();
        }

        if (_s[_i] == '0')
        {
            _i++;
        }
        else
        {
            while (_i < _s.Length && char.IsAsciiDigit(_s[_i]))
            {
                _i++;
            }
        }

        if (_i < _s.Length && _s[_i] == '.')
        {
            _i++;
            int digits = _i;
            while (_i < _s.Length && char.IsAsciiDigit(_s[_i]))
            {
                _i++;
            }

            if (_i == digits)
            {
                ErrorOffset = _i;
                Fail();
            }
        }

        if (_i < _s.Length && _s[_i] is 'e' or 'E')
        {
            _i++;
            if (_i < _s.Length && _s[_i] is '+' or '-')
            {
                _i++;
            }

            int digits = _i;
            while (_i < _s.Length && char.IsAsciiDigit(_s[_i]))
            {
                _i++;
            }

            if (_i == digits)
            {
                ErrorOffset = _i;
                Fail();
            }
        }

        return new JsonScalar { Start = start, End = _i, Literal = _s.Substring(start, _i - start) };
    }

    private bool Match(string word)
    {
        if (string.CompareOrdinal(_s, _i, word, 0, word.Length) == 0)
        {
            _i += word.Length;
            return true;
        }

        return false;
    }
}
