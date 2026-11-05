using System.Globalization;

namespace RecursiveAI.OneContact.Email;

/// <summary>Address literals of RFC 5321 section 4.1.3 (IPv4, IPv6, general).</summary>
internal static class EmailAddressLiteral
{
    /// <summary>Validates the text between the brackets and gives the canonical bracketed form.</summary>
    /// <param name="text">The content, white space removed.</param>
    /// <param name="canonical">The domain as stored, brackets included.</param>
    /// <param name="smtp"><see langword="false"/> when the content is valid RFC 5322 dtext but not an address literal SMTP can carry.</param>
    /// <returns><see langword="false"/> when the text starts like an IPv4 or IPv6 literal but is not one.</returns>
    internal static bool TryNormalize(string text, out string canonical, out bool smtp)
    {
        smtp = true;
        if (text.StartsWith("IPv6:", StringComparison.OrdinalIgnoreCase))
        {
            canonical = string.Concat("[IPv6:", text[5..].ToLowerInvariant(), "]");
            return IsIPv6(text.AsSpan(5));
        }

        canonical = string.Concat("[", text, "]");
        if (LooksLikeIPv4(text))
        {
            return IsIPv4(text);
        }

        // General-address-literal = Standardized-tag ":" 1*dcontent (RFC 5321 section 4.1.3). No tag is registered besides IPv6, so
        // anything else is carried but flagged.
        smtp = false;
        return true;
    }

    private static bool LooksLikeIPv4(string text)
    {
        if (text.Length == 0)
        {
            return false;
        }

        foreach (char c in text)
        {
            if (!char.IsAsciiDigit(c) && c != '.')
            {
                return false;
            }
        }

        return true;
    }

    // IPv4-address-literal = Snum 3("." Snum); Snum = 1*3DIGIT, 0 to 255.
    private static bool IsIPv4(ReadOnlySpan<char> text)
    {
        int parts = 0;
        foreach (Range range in text.Split('.'))
        {
            ReadOnlySpan<char> part = text[range];
            if (part.Length is 0 or > 3 || !int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n > 255)
            {
                return false;
            }

            parts++;
        }

        return parts == 4;
    }

    // IPv6-addr: hex groups of 1 to 4 digits, at most one "::", and an optional trailing IPv4 address that counts as two groups.
    // Eight groups without "::"; with it, at most seven.
    private static bool IsIPv6(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return false;
        }

        int compress = text.IndexOf("::", StringComparison.Ordinal);
        if (compress >= 0 && text[(compress + 1)..].IndexOf("::", StringComparison.Ordinal) >= 0)
        {
            return false;
        }

        if (compress < 0)
        {
            return TryCountGroups(text, allowIPv4: true, out int total) && total == 8;
        }

        ReadOnlySpan<char> head = text[..compress];
        ReadOnlySpan<char> tail = text[(compress + 2)..];
        int headGroups = 0;
        int tailGroups = 0;
        if ((!head.IsEmpty && !TryCountGroups(head, allowIPv4: false, out headGroups)) || (!tail.IsEmpty && !TryCountGroups(tail, allowIPv4: true, out tailGroups)))
        {
            return false;
        }

        return headGroups + tailGroups <= 7;
    }

    private static bool TryCountGroups(ReadOnlySpan<char> text, bool allowIPv4, out int groups)
    {
        groups = 0;
        int start = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && text[i] != ':')
            {
                continue;
            }

            ReadOnlySpan<char> group = text[start..i];
            bool last = i == text.Length;
            if (last && allowIPv4 && group.Contains('.'))
            {
                if (!IsIPv4(group))
                {
                    return false;
                }

                groups += 2;
            }
            else
            {
                if (group.Length is 0 or > 4)
                {
                    return false;
                }

                foreach (char c in group)
                {
                    if (!char.IsAsciiHexDigit(c))
                    {
                        return false;
                    }
                }

                groups++;
            }

            start = i + 1;
        }

        return true;
    }
}
