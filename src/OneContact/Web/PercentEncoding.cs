using System.Text;

namespace RecursiveAI.OneContact.Web;

/// <summary>The percent-encode sets of the WHATWG URL Standard, section 1.3.</summary>
internal enum PercentEncodeSet
{
    C0Control,
    Fragment,
    Query,
    SpecialQuery,
    Path,
    Userinfo,
}

/// <summary>Percent-encoding and decoding as the WHATWG URL Standard defines them (section 1.3).</summary>
internal static class PercentEncoding
{
    private const string Upper = "0123456789ABCDEF";

    /// <summary>Tells whether a code point is in a percent-encode set.</summary>
    /// <param name="codePoint">A scalar value.</param>
    /// <param name="set">The set.</param>
    /// <returns><see langword="true"/> when the code point must be percent-encoded.</returns>
    internal static bool InSet(int codePoint, PercentEncodeSet set)
    {
        // The C0 control percent-encode set: C0 controls and everything above U+007E.
        if (codePoint <= 0x1F || codePoint > 0x7E)
        {
            return true;
        }

        if (set == PercentEncodeSet.C0Control)
        {
            return false;
        }

        switch (set)
        {
            case PercentEncodeSet.Fragment:
                return codePoint is ' ' or '"' or '<' or '>' or '`';
            case PercentEncodeSet.Query:
                return codePoint is ' ' or '"' or '#' or '<' or '>';
            case PercentEncodeSet.SpecialQuery:
                return codePoint is ' ' or '"' or '#' or '<' or '>' or '\'';
            case PercentEncodeSet.Path:
                return codePoint is ' ' or '"' or '#' or '<' or '>' or '?' or '^' or '`' or '{' or '}';
            default:
                // The userinfo set is the path set plus / : ; = @ [ \ ] |.
                return codePoint is ' ' or '"' or '#' or '<' or '>' or '?' or '^' or '`' or '{' or '}'
                    or '/' or ':' or ';' or '=' or '@' or '[' or '\\' or ']' or '|';
        }
    }

    /// <summary>UTF-8 percent-encodes one scalar value and appends the result.</summary>
    /// <param name="sb">The output.</param>
    /// <param name="codePoint">The scalar value.</param>
    /// <param name="set">The set.</param>
    internal static void AppendEncoded(StringBuilder sb, int codePoint, PercentEncodeSet set)
    {
        if (!InSet(codePoint, set))
        {
            sb.Append((char)codePoint);
            return;
        }

        // UTF-8 encoding of a scalar value, one byte at a time (RFC 3629 section 3).
        if (codePoint < 0x80)
        {
            AppendByte(sb, codePoint);
        }
        else if (codePoint < 0x800)
        {
            AppendByte(sb, 0xC0 | (codePoint >> 6));
            AppendByte(sb, 0x80 | (codePoint & 0x3F));
        }
        else if (codePoint < 0x10000)
        {
            AppendByte(sb, 0xE0 | (codePoint >> 12));
            AppendByte(sb, 0x80 | ((codePoint >> 6) & 0x3F));
            AppendByte(sb, 0x80 | (codePoint & 0x3F));
        }
        else
        {
            AppendByte(sb, 0xF0 | (codePoint >> 18));
            AppendByte(sb, 0x80 | ((codePoint >> 12) & 0x3F));
            AppendByte(sb, 0x80 | ((codePoint >> 6) & 0x3F));
            AppendByte(sb, 0x80 | (codePoint & 0x3F));
        }
    }

    private static void AppendByte(StringBuilder sb, int value) =>
        sb.Append('%').Append(Upper[value >> 4]).Append(Upper[value & 15]);

    /// <summary>UTF-8 percent-encodes a string (a scalar value string: lone surrogates become U+FFFD).</summary>
    /// <param name="text">The text.</param>
    /// <param name="set">The set.</param>
    /// <returns>The encoded text.</returns>
    internal static string Encode(string text, PercentEncodeSet set)
    {
        var sb = new StringBuilder(text.Length);
        foreach (Rune r in text.EnumerateRunes())
        {
            AppendEncoded(sb, r.Value, set);
        }

        return sb.ToString();
    }

    /// <summary>Percent-decodes UTF-8 bytes of a string (section 1.3, "percent-decode a scalar value string").</summary>
    /// <param name="text">The text.</param>
    /// <returns>The decoded bytes.</returns>
    internal static byte[] Decode(string text)
    {
        byte[] input = Encoding.UTF8.GetBytes(text);
        var output = new List<byte>(input.Length);
        for (int i = 0; i < input.Length; i++)
        {
            if (input[i] == '%' && i + 2 < input.Length && IsHex(input[i + 1]) && IsHex(input[i + 2]))
            {
                output.Add((byte)((HexValue(input[i + 1]) << 4) | HexValue(input[i + 2])));
                i += 2;
            }
            else
            {
                output.Add(input[i]);
            }
        }

        return [.. output];
    }

    internal static bool IsHex(int c) => c is >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f';

    internal static int HexValue(int c) => c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;
}
