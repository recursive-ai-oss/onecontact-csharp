using System.Text;

namespace RecursiveAI.OneContact.Email;

/// <summary>RFC 2047 encoded words as they may appear in a display name (RFC 5322 section 3.2.5 phrase; RFC 2047 section 5, rule 3).</summary>
internal static class EncodedWords
{
    // Windows-1252 maps 0x80..0x9F to these characters (U+FFFD where the code page leaves a hole).
    private const string Windows1252High =
        "\u20AC\uFFFD\u201A\u0192\u201E\u2026\u2020\u2021\u02C6\u2030\u0160\u2039\u0152\uFFFD\u017D\uFFFD" +
        "\uFFFD\u2018\u2019\u201C\u201D\u2022\u2013\u2014\u02DC\u2122\u0161\u203A\u0153\uFFFD\u017E\u0178";

    /// <summary>Decodes <paramref name="word"/> when it is one whole encoded word with a charset this library reads (UTF-8, US-ASCII, ISO-8859-1, ISO-8859-15, windows-1252).</summary>
    /// <param name="word">An atom such as <c>=?utf-8?q?Jos=C3=A9?=</c>.</param>
    /// <param name="decoded">The text.</param>
    /// <returns><see langword="false"/> when it is not an encoded word or cannot be decoded; the caller then keeps it as written.</returns>
    internal static bool TryDecode(ReadOnlySpan<char> word, out string decoded)
    {
        decoded = string.Empty;
        if (word.Length < 8 || !word.StartsWith("=?", StringComparison.Ordinal) || !word.EndsWith("?=", StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> inner = word[2..^2];
        int first = inner.IndexOf('?');
        if (first <= 0)
        {
            return false;
        }

        ReadOnlySpan<char> rest = inner[(first + 1)..];
        int second = rest.IndexOf('?');
        if (second != 1)
        {
            return false;
        }

        char encoding = rest[0];
        ReadOnlySpan<char> text = rest[2..];
        if (text.Contains('?'))
        {
            return false;
        }

        ReadOnlySpan<char> charset = inner[..first];
        int star = charset.IndexOf('*'); // RFC 2231 section 5: a language tag may follow the charset
        if (star >= 0)
        {
            charset = charset[..star];
        }

        byte[]? bytes = encoding switch
        {
            'B' or 'b' => DecodeBase64(text),
            'Q' or 'q' => DecodeQ(text),
            _ => null,
        };
        if (bytes is null)
        {
            return false;
        }

        string? s = DecodeCharset(charset, bytes);
        if (s is null)
        {
            return false;
        }

        decoded = s;
        return true;
    }

    private static byte[]? DecodeBase64(ReadOnlySpan<char> text)
    {
        byte[] buffer = new byte[(text.Length / 4 * 3) + 3];
        return Convert.TryFromBase64Chars(text, buffer, out int written) ? buffer[..written] : null;
    }

    // RFC 2047 section 4.2: Q encoding; underscore is a space, =XX is a byte.
    private static byte[]? DecodeQ(ReadOnlySpan<char> text)
    {
        List<byte> bytes = new(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '_')
            {
                bytes.Add((byte)' ');
            }
            else if (c == '=')
            {
                if (i + 2 >= text.Length)
                {
                    return null;
                }

                if (!byte.TryParse(text.Slice(i + 1, 2), System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out byte b))
                {
                    return null;
                }

                bytes.Add(b);
                i += 2;
            }
            else if (c < 0x80)
            {
                bytes.Add((byte)c);
            }
            else
            {
                return null;
            }
        }

        return [.. bytes];
    }

    private static string? DecodeCharset(ReadOnlySpan<char> charset, byte[] bytes)
    {
        if (charset.Equals("utf-8", StringComparison.OrdinalIgnoreCase) || charset.Equals("utf8", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return null;
            }
        }

        if (charset.Equals("us-ascii", StringComparison.OrdinalIgnoreCase))
        {
            foreach (byte b in bytes)
            {
                if (b >= 0x80)
                {
                    return null;
                }
            }

            return Encoding.ASCII.GetString(bytes);
        }

        bool latin1 = charset.Equals("iso-8859-1", StringComparison.OrdinalIgnoreCase) || charset.Equals("latin1", StringComparison.OrdinalIgnoreCase);
        bool latin9 = charset.Equals("iso-8859-15", StringComparison.OrdinalIgnoreCase);
        bool cp1252 = charset.Equals("windows-1252", StringComparison.OrdinalIgnoreCase);
        if (!latin1 && !latin9 && !cp1252)
        {
            return null;
        }

        StringBuilder sb = new(bytes.Length);
        foreach (byte b in bytes)
        {
            char c = (char)b;
            if (cp1252 && b is >= 0x80 and <= 0x9F)
            {
                c = Windows1252High[b - 0x80];
            }
            else if (latin9)
            {
                c = b switch
                {
                    0xA4 => '\u20AC',
                    0xA6 => '\u0160',
                    0xA8 => '\u0161',
                    0xB4 => '\u017D',
                    0xB8 => '\u017E',
                    0xBC => '\u0152',
                    0xBD => '\u0153',
                    0xBE => '\u0178',
                    _ => c,
                };
            }

            sb.Append(c);
        }

        return sb.ToString();
    }
}
