namespace RecursiveAI.OneContact.Extraction.Text;

/// <summary>A stretch of text that may be a web address.</summary>
/// <param name="Start">Index of the first character.</param>
/// <param name="End">Index past the last character.</param>
internal readonly record struct UrlHit(int Start, int End);

/// <summary>
/// Finds web addresses in a line: anything with an <c>http://</c> or <c>https://</c> scheme, anything that starts with <c>www.</c>, and
/// bare domain names whose last label is a top-level domain of the Public Suffix List's ICANN section. Trailing punctuation that belongs
/// to the sentence (a full stop, a comma, an unbalanced bracket) is left out. A token with an at sign is an email address, not a URL.
/// </summary>
internal static class UrlCandidates
{
    // Top-level domains that are also the extensions of files and source code: "readme.md" is a file, not a site. They are accepted
    // only with a scheme or www.
    private static readonly HashSet<string> FileLike = new(StringComparer.Ordinal)
    {
        "zip", "mov", "md", "sh", "py", "pl", "rs", "so", "ps", "pm", "cc", "cs", "rb", "gz", "mp",
    };

    public static void Find(string text, int start, int end, List<UrlHit> sink)
    {
        int i = start;
        while (i < end)
        {
            while (i < end && IsSpace(text[i]))
            {
                i++;
            }

            int tokenStart = i;
            while (i < end && !IsSpace(text[i]))
            {
                i++;
            }

            int s = tokenStart;
            int e = i;
            while (s < e && text[s] is '(' or '<' or '[' or '{' or '"' or '\'' or '\u201C' or '\u2018')
            {
                s++;
            }

            while (e > s)
            {
                char last = text[e - 1];
                if (last is '.' or ',' or ';' or ':' or '!' or '?' or '"' or '\'' or '\u201D' or '\u2019' or '>' or ']' or '}')
                {
                    e--;
                }
                else if (last == ')' && Count(text, s, e, ')') > Count(text, s, e, '('))
                {
                    e--;
                }
                else
                {
                    break;
                }
            }

            if (e - s < 4 || text.AsSpan(s, e - s).Contains('@'))
            {
                continue;
            }

            if (IsUrl(text.AsSpan(s, e - s)))
            {
                sink.Add(new UrlHit(s, e));
            }
        }
    }

    private static bool IsSpace(char c) => c is ' ' or '\t' or '\u00A0' or '\r' or '\n';

    private static int Count(string text, int s, int e, char c)
    {
        int n = 0;
        for (int i = s; i < e; i++)
        {
            if (text[i] == c)
            {
                n++;
            }
        }

        return n;
    }

    private static bool IsUrl(ReadOnlySpan<char> token)
    {
        if (token.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || token.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            int hostAt = token.IndexOf("://", StringComparison.Ordinal) + 3;
            return hostAt < token.Length && char.IsLetterOrDigit(token[hostAt]);
        }

        bool www = token.StartsWith("www.", StringComparison.OrdinalIgnoreCase);
        int hostEnd = token.IndexOfAny("/?#:");
        ReadOnlySpan<char> host = hostEnd < 0 ? token : token[..hostEnd];
        if (hostEnd >= 0 && token[hostEnd] == ':' && !(hostEnd + 1 < token.Length && char.IsAsciiDigit(token[hostEnd + 1])))
        {
            return false;
        }

        int dot = host.LastIndexOf('.');
        if (dot < 1 || dot == host.Length - 1 || host.Length > 253)
        {
            return false;
        }

        foreach (char c in host)
        {
            if (!(char.IsLetterOrDigit(c) || c is '-' or '.'))
            {
                return false;
            }
        }

        if (host.Contains("..", StringComparison.Ordinal) || host[0] is '-' or '.')
        {
            return false;
        }

        string tld = host[(dot + 1)..].ToString();
        if (www)
        {
            return tld.Length >= 2;
        }

        if (!TopLevelDomains.IsKnown(tld) || FileLike.Contains(tld.ToLowerInvariant()))
        {
            return false;
        }

        // A single label in front of the suffix at least: "com" alone is not a site.
        return host[..dot].Length > 0 && !char.IsAsciiDigit(tld[0]);
    }
}
