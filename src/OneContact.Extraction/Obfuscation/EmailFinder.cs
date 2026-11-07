using System.Text;
using RecursiveAI.OneContact.Extraction.Text;

namespace RecursiveAI.OneContact.Extraction.Obfuscation;

/// <summary>One email address found in text: where it was written and what it says.</summary>
/// <param name="Start">Index of the first character written.</param>
/// <param name="End">Index past the last character written.</param>
/// <param name="Address">The address, with every obfuscation replaced: <c>local@domain</c>.</param>
/// <param name="Decoded">True when the written form used a replacement for <c>@</c> or <c>.</c>.</param>
internal readonly record struct EmailHit(int Start, int End, string Address, bool Decoded);

/// <summary>
/// Finds email addresses in a line of text: written plainly (<c>john@example.com</c>), or with the tricks people use against
/// harvesters: <c>john [at] example [dot] com</c>, <c>(at)</c>, <c>{at}</c>, <c>-at-</c>, a spaced <c>at</c> with <c>dot</c> words,
/// full-width and small commercial-at signs, and <c>%40</c>. A bare "at" is trusted only when the domain is also written with a
/// dot word, so that "meet me at example.com" is not an address. Every address must end in a top-level domain of the Public Suffix
/// List's ICANN section and must be accepted by the core parser.
/// </summary>
internal static class EmailFinder
{
    private const int MaxLocal = 64;
    private const int MaxDomain = 255;

    /// <summary>Finds the addresses in <c>text[start..end]</c>; the line must not contain a line break.</summary>
    public static void Find(string text, int start, int end, List<EmailHit> sink)
    {
        int firstNew = sink.Count;
        FindPlain(text, start, end, sink);
        FindObfuscated(text, start, end, sink, firstNew);
        sink.Sort(firstNew, sink.Count - firstNew, Comparer<EmailHit>.Create((a, b) => a.Start.CompareTo(b.Start)));
    }

    private static bool IsLocalChar(char c) =>
        char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or '+' or '\'' or '&' || (c > 127 && (char.IsLetterOrDigit(c) || char.IsSurrogate(c)));

    private static bool IsLabelChar(char c) => char.IsAsciiLetterOrDigit(c) || c == '-' || (c > 127 && (char.IsLetterOrDigit(c) || char.IsSurrogate(c)));

    private static void FindPlain(string text, int start, int end, List<EmailHit> sink)
    {
        for (int at = text.IndexOf('@', start, end - start); at >= 0; at = at + 1 < end ? text.IndexOf('@', at + 1, end - at - 1) : -1)
        {
            int ls = at;
            while (ls > start && at - ls < MaxLocal + 1 && IsLocalChar(text[ls - 1]))
            {
                ls--;
            }

            while (ls < at && text[ls] is '.' or '\'' or '-' or '&')
            {
                ls++;
            }

            int de = at + 1;
            while (de < end && de - at < MaxDomain + 1 && (IsLabelChar(text[de]) || text[de] == '.'))
            {
                de++;
            }

            while (de > at + 1 && text[de - 1] is '.' or '-')
            {
                de--;
            }

            if (ls >= at || de <= at + 1 || IsUrlUserInfo(text, ls, start))
            {
                continue;
            }

            string address = text.Substring(ls, de - ls);
            if (Accept(address, requireKnownTopLevel: false))
            {
                sink.Add(new EmailHit(ls, de, address, false));
            }
        }
    }

    // "https://user@host/": the part before the at sign is a URL's user information, not a mailbox.
    private static bool IsUrlUserInfo(string text, int localStart, int lineStart) =>
        localStart - 2 >= lineStart && text[localStart - 1] == '/' && text[localStart - 2] == '/';

    // A written address with a real at sign needs only a plausible top-level label (letters); one rebuilt from words needs a real top-level domain.
    private static bool Accept(string address, bool requireKnownTopLevel)
    {
        int at = address.IndexOf('@', StringComparison.Ordinal);
        if (at < 1 || at > MaxLocal)
        {
            return false;
        }

        string domain = address[(at + 1)..];
        int dot = domain.LastIndexOf('.');
        if (dot < 1)
        {
            return false;
        }

        string top = domain[(dot + 1)..];
        if (requireKnownTopLevel ? !TopLevelDomains.IsKnown(top) : !IsPlausibleTopLevel(top))
        {
            return false;
        }

        return EmailAddress.TryParse(address, out EmailAddress parsed) && !parsed.Flags.HasFlag(EmailFacts.SingleLabelDomain);
    }

    private static readonly System.Collections.Frozen.FrozenSet<string> FileExtensions = System.Collections.Frozen.FrozenSet.ToFrozenSet(
        ["png", "jpg", "jpeg", "gif", "svg", "webp", "bmp", "ico", "css", "js", "json", "xml", "pdf", "html", "htm", "php", "asp", "aspx", "jsp", "woff", "woff2", "ttf", "eot", "otf", "map", "txt", "gz", "tgz", "avif", "tif", "tiff", "mp3", "mp4", "webm", "wav", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "csv"],
        StringComparer.Ordinal);

    private static bool IsPlausibleTopLevel(string label)
    {
        if (label.StartsWith("xn--", StringComparison.OrdinalIgnoreCase))
        {
            return label.Length > 4;
        }

        if (label.Length < 2)
        {
            return false;
        }

        // "logo@2x.png" is a file name for an image density, not a mailbox: a file extension that is no top-level domain ends no address.
        if (FileExtensions.Contains(label.ToLowerInvariant()) && !TopLevelDomains.IsKnown(label))
        {
            return false;
        }

        foreach (char c in label)
        {
            if (!char.IsLetter(c))
            {
                return false;
            }
        }

        return true;
    }

    // ---- obfuscated forms --------------------------------------------------------------------------------------------------

    private readonly record struct Token(int Start, int End, bool Bracketed);

    private static void FindObfuscated(string text, int start, int end, List<EmailHit> sink, int firstPlain)
    {
        List<Token> ats = [];
        var dotsByStart = new Dictionary<int, Token>();
        var dotEnds = new HashSet<int>();
        for (int p = start; p < end; p++)
        {
            if (TryAtToken(text, p, end, out Token a))
            {
                ats.Add(a);
            }

            if (TryDotToken(text, p, end, out Token d))
            {
                dotsByStart[p] = d;
                dotEnds.Add(d.End);
            }
        }

        foreach (Token at in ats)
        {
            if (Overlaps(sink, firstPlain, at.Start, at.End))
            {
                continue;
            }

            if (TryBuild(text, start, end, at, dotsByStart, dotEnds, out EmailHit hit) && !Overlaps(sink, firstPlain, hit.Start, hit.End))
            {
                sink.Add(hit);
            }
        }
    }

    private static bool Overlaps(List<EmailHit> sink, int from, int s, int e)
    {
        for (int i = from; i < sink.Count; i++)
        {
            if (sink[i].Start < e && sink[i].End > s)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryBuild(string text, int lineStart, int lineEnd, Token at, Dictionary<int, Token> dotsByStart, HashSet<int> dotEnds, out EmailHit hit)
    {
        hit = default;

        // The domain: labels joined by real dots or dot words.
        var domain = new StringBuilder();
        int p = at.End;
        int substitutedDots = 0;
        int realDots = 0;
        int domainEnd = p;
        while (true)
        {
            int labelStart = p;
            while (p < lineEnd && IsLabelChar(text[p]) && !dotsByStart.ContainsKey(p) && p - at.End < MaxDomain)
            {
                p++;
            }

            if (p == labelStart)
            {
                break;
            }

            domain.Append(text, labelStart, p - labelStart);
            domainEnd = p;
            if (p < lineEnd && text[p] == '.' && p + 1 < lineEnd && IsLabelChar(text[p + 1]) && !dotsByStart.ContainsKey(p + 1))
            {
                domain.Append('.');
                realDots++;
                p++;
            }
            else if (dotsByStart.TryGetValue(p, out Token dot) && dot.End < lineEnd && IsLabelChar(text[dot.End]))
            {
                domain.Append('.');
                substitutedDots++;
                p = dot.End;
            }
            else
            {
                break;
            }
        }

        if (domain.Length == 0 || substitutedDots + realDots == 0)
        {
            return false;
        }

        // A real at sign with real dots is the plain case, which the first pass has already judged (and may have refused).
        if (text[at.Start] == '@' && at.End - at.Start == 1 && substitutedDots == 0)
        {
            return false;
        }

        // "john at example.com" is ordinary prose; with a dot word as well it is an address.
        if (!at.Bracketed && substitutedDots == 0)
        {
            return false;
        }

        // The local part: runs of local characters joined by dot words, read from the at sign leftwards.
        var local = new StringBuilder();
        int q = at.Start;
        int localStart = q;
        int guard = 0;
        while (guard++ < 40)
        {
            int runEnd = q;
            int runStart = q;
            while (runStart > lineStart && q - runStart < MaxLocal && IsLocalChar(text[runStart - 1]) && !dotEnds.Contains(runStart))
            {
                runStart--;
            }

            if (runStart == runEnd)
            {
                break;
            }

            local.Insert(0, text.AsSpan(runStart, runEnd - runStart));
            localStart = runStart;
            // A dot word before this run joins it to the run before.
            Token? joiner = null;
            foreach ((int s, Token t) in dotsByStart)
            {
                if (t.End == runStart)
                {
                    joiner = t;
                    break;
                }
            }

            if (joiner is { } j && j.Start > lineStart)
            {
                local.Insert(0, '.');
                q = j.Start;
                continue;
            }

            break;
        }

        string localText = local.ToString().Trim('.', '\'', '-', '&');
        if (localText.Length == 0 || localText.Length > MaxLocal || IsUrlUserInfo(text, localStart, lineStart))
        {
            return false;
        }

        string address = localText + "@" + domain;
        if (!Accept(address, requireKnownTopLevel: true))
        {
            return false;
        }

        hit = new EmailHit(localStart, domainEnd, address, true);
        return true;
    }

    private static bool IsSpace(char c) => c is ' ' or '\t' or '\u00A0';

    private static int SkipSpaces(string text, int i, int end)
    {
        while (i < end && IsSpace(text[i]))
        {
            i++;
        }

        return i;
    }

    // "[at]", "(at)", "{at}", "<at>" (or "[@]" ...) and, for a dot, "[dot]", "[.]" ...; spaces inside the brackets are allowed.
    private static bool TryBracketed(string text, int p, int end, bool dot, out int after)
    {
        after = p;
        char open = text[p];
        if (open is not ('[' or '(' or '{' or '<'))
        {
            return false;
        }

        char close = open switch { '[' => ']', '(' => ')', '{' => '}', _ => '>' };
        int i = SkipSpaces(text, p + 1, end);
        int word = i;
        if (dot)
        {
            if (i < end && text[i] == '.')
            {
                i++;
            }
            else if (i + 3 <= end && string.Compare(text, i, "dot", 0, 3, StringComparison.OrdinalIgnoreCase) == 0)
            {
                i += 3;
            }
        }
        else if (i < end && text[i] == '@')
        {
            i++;
        }
        else if (i + 2 <= end && (text[i] is 'a' or 'A') && (text[i + 1] is 't' or 'T'))
        {
            i += 2;
        }

        if (i == word)
        {
            return false;
        }

        i = SkipSpaces(text, i, end);
        if (i < end && text[i] == close)
        {
            after = i + 1;
            return true;
        }

        return false;
    }

    private static bool TryAtToken(string text, int p, int end, out Token token)
    {
        token = default;
        char c = text[p];
        switch (c)
        {
            case '[' or '(' or '{' or '<':
                // After a space, the white-space token below takes it in.
                if ((p > 0 && IsSpace(text[p - 1])) || !TryBracketed(text, p, end, false, out int after))
                {
                    return false;
                }

                token = new Token(p, SkipSpaces(text, after, end), true);
                return true;
            case '@' or '\uFF20' or '\uFE6B':
                // A real at sign is a token too: "john@example[.]com" has its dot written as a word.
                token = new Token(p, p + 1, true);
                return true;
            case '%' when p + 2 < end && text[p + 1] == '4' && text[p + 2] == '0':
                token = new Token(p, p + 3, true);
                return true;
            case '-' or '_' when p + 3 < end && (text[p + 1] is 'a' or 'A') && (text[p + 2] is 't' or 'T') && text[p + 3] == c:
                token = new Token(p, p + 4, true);
                return true;
            case ' ' or '\t' or '\u00A0':
                {
                    // One token per run of white space: it starts at the first space.
                    if (p > 0 && IsSpace(text[p - 1]))
                    {
                        return false;
                    }

                    int i = SkipSpaces(text, p, end);
                    if (i < end && TryBracketed(text, i, end, false, out int bracketEnd))
                    {
                        token = new Token(p, SkipSpaces(text, bracketEnd, end), true);
                        return true;
                    }

                    if (i < end && text[i] == '@')
                    {
                        int j = SkipSpaces(text, i + 1, end);
                        if (j > i + 1)
                        {
                            token = new Token(p, j, true);
                            return true;
                        }

                        return false;
                    }

                    if (i + 2 < end && (text[i] is 'a' or 'A') && (text[i + 1] is 't' or 'T') && IsSpace(text[i + 2]))
                    {
                        token = new Token(p, SkipSpaces(text, i + 2, end), false);
                        return true;
                    }

                    return false;
                }

            default:
                return false;
        }
    }

    private static bool TryDotToken(string text, int p, int end, out Token token)
    {
        token = default;
        char c = text[p];
        switch (c)
        {
            case '[' or '(' or '{' or '<':
                if ((p > 0 && IsSpace(text[p - 1])) || !TryBracketed(text, p, end, true, out int after))
                {
                    return false;
                }

                token = new Token(p, SkipSpaces(text, after, end), true);
                return true;
            case '\uFF0E' or '\uFE52' or '\u2024':
                token = new Token(p, p + 1, true);
                return true;
            case '%' when p + 2 < end && text[p + 1] == '2' && (text[p + 2] is 'E' or 'e'):
                token = new Token(p, p + 3, true);
                return true;
            case '-' or '_' when p + 4 < end && string.Compare(text, p + 1, "dot", 0, 3, StringComparison.OrdinalIgnoreCase) == 0 && text[p + 4] == c:
                token = new Token(p, p + 5, true);
                return true;
            case ' ' or '\t' or '\u00A0':
                {
                    if (p > 0 && IsSpace(text[p - 1]))
                    {
                        return false;
                    }

                    int i = SkipSpaces(text, p, end);
                    if (i < end && TryBracketed(text, i, end, true, out int bracketEnd))
                    {
                        token = new Token(p, SkipSpaces(text, bracketEnd, end), true);
                        return true;
                    }

                    if (i + 3 < end && string.Compare(text, i, "dot", 0, 3, StringComparison.OrdinalIgnoreCase) == 0 && IsSpace(text[i + 3]))
                    {
                        token = new Token(p, SkipSpaces(text, i + 3, end), false);
                        return true;
                    }

                    return false;
                }

            default:
                return false;
        }
    }
}
