using System.Text;

namespace RecursiveAI.OneContact.Names;

/// <summary>
/// Case and diacritic folding for matching: lower case by invariant simple case mapping, Latin letters with marks reduced to the
/// base letter, and the few letters that have no base (sharp s, ligatures, eth, thorn) spelled out. Nothing depends on the culture.
/// </summary>
internal static class NameFolding
{
    // Each group is a base letter followed by the letters that fold to it.
    private static readonly string[] Groups =
    [
        "aàáâãäåāăąǎ", "cçćĉċč", "dďđ", "eèéêëēĕėęě", "gĝğġģ", "hĥħ", "iìíîïĩīĭįıǐ", "jĵ", "kķ", "lĺļľŀł",
        "nñńņňŉ", "oòóôõöøōŏőǒ", "rŕŗř", "sśŝşšș", "tţťŧț", "uùúûüũūŭůűųǔ", "wŵ", "yýÿŷ", "zźżž",
    ];

    private static readonly Dictionary<char, char> Base = BuildBase();

    private static Dictionary<char, char> BuildBase()
    {
        Dictionary<char, char> map = [];
        foreach (string group in Groups)
        {
            for (int i = 1; i < group.Length; i++)
            {
                map[group[i]] = group[0];
            }
        }

        return map;
    }

    /// <summary>Appends the folded form of one character (lower case, mark-free) to <paramref name="sb"/>; a letter may become several.</summary>
    internal static void AppendFolded(StringBuilder sb, char c)
    {
        char lower = char.ToLowerInvariant(c);
        switch (lower)
        {
            case 'ß':
                sb.Append("ss");
                return;
            case 'æ':
                sb.Append("ae");
                return;
            case 'œ':
                sb.Append("oe");
                return;
            case 'þ':
                sb.Append("th");
                return;
            case 'ð':
                sb.Append('d');
                return;
            default:
                break;
        }

        // Combining marks (U+0300 to U+036F) vanish, so text that arrives decomposed folds like precomposed text.
        if (lower is >= '\u0300' and <= '\u036F')
        {
            return;
        }

        sb.Append(Base.TryGetValue(lower, out char b) ? b : lower);
    }

    /// <summary>Folds <paramref name="s"/> and keeps only letters and digits: <c>S.A. de C.V.</c> becomes <c>sadecv</c>.</summary>
    internal static string LettersAndDigits(ReadOnlySpan<char> s)
    {
        // ASCII input (most words) needs no mark or letter tables: keep letters and digits, lower the capitals, in one allocation.
        int kept = 0;
        bool ascii = true;
        foreach (char c in s)
        {
            if (c >= '')
            {
                ascii = false;
                break;
            }

            if (char.IsAsciiLetterOrDigit(c))
            {
                kept++;
            }
        }

        if (ascii)
        {
            return kept == 0 ? string.Empty : string.Create(kept, s, static (dest, src) =>
            {
                int n = 0;
                foreach (char c in src)
                {
                    if (char.IsAsciiLetterOrDigit(c))
                    {
                        dest[n++] = char.IsAsciiLetterUpper(c) ? (char)(c | 0x20) : c;
                    }
                }
            });
        }

        StringBuilder sb = new(s.Length);
        foreach (char c in s)
        {
            if (char.IsLetterOrDigit(c) || c is >= '\u0300' and <= '\u036F')
            {
                AppendFolded(sb, c);
            }
        }

        return sb.ToString();
    }

    /// <summary>Folds <paramref name="s"/> into lower-case words separated by single spaces; every character that is not a letter or digit separates words.</summary>
    internal static string Words(ReadOnlySpan<char> s)
    {
        StringBuilder sb = new(s.Length);
        bool pending = false;
        foreach (char c in s)
        {
            if (char.IsLetterOrDigit(c) || c is >= '\u0300' and <= '\u036F')
            {
                if (pending && sb.Length > 0)
                {
                    sb.Append(' ');
                }

                pending = false;
                AppendFolded(sb, c);
            }
            else
            {
                pending = true;
            }
        }

        return sb.ToString();
    }

    /// <summary>Returns whether the span has at least one letter and every letter is upper case.</summary>
    internal static bool IsAllUpper(ReadOnlySpan<char> s)
    {
        bool any = false;
        foreach (char c in s)
        {
            if (char.IsLower(c))
            {
                return false;
            }

            any |= char.IsUpper(c);
        }

        return any;
    }

    /// <summary>Returns whether the span has at least one letter and every letter is lower case.</summary>
    internal static bool IsAllLower(ReadOnlySpan<char> s)
    {
        bool any = false;
        foreach (char c in s)
        {
            if (char.IsUpper(c))
            {
                return false;
            }

            any |= char.IsLower(c);
        }

        return any;
    }
}
