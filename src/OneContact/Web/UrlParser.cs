using System.Text;

namespace RecursiveAI.OneContact.Web;

/// <summary>The states of the basic URL parser (WHATWG URL Standard, section 4.4).</summary>
internal enum UrlState
{
    SchemeStart,
    Scheme,
    NoScheme,
    SpecialRelativeOrAuthority,
    PathOrAuthority,
    Relative,
    RelativeSlash,
    SpecialAuthoritySlashes,
    SpecialAuthorityIgnoreSlashes,
    Authority,
    Host,
    Hostname,
    Port,
    File,
    FileSlash,
    FileHost,
    PathStart,
    Path,
    OpaquePath,
    Query,
    Fragment,
}

/// <summary>Why the basic URL parser failed, for callers that report a reason.</summary>
internal enum UrlFailure
{
    None,
    MissingScheme,
    HostMissing,
    InvalidHost,
    InvalidPort,
    Other,
}

/// <summary>
/// Where the components of a parsed URL sit in the original input, as UTF-16 offsets; -1 when the component is absent. Only filled for
/// a parse without a base URL or state override.
/// </summary>
internal struct UrlSpans
{
    internal int SchemeEnd;
    internal int HostStart;
    internal int HostEnd;
    internal int PortStart;
    internal int PortEnd;
    internal int PathStart;
    internal int QueryStart;
    internal int FragmentStart;
    internal int End;

    internal static UrlSpans Empty => new() { SchemeEnd = -1, HostStart = -1, HostEnd = -1, PortStart = -1, PortEnd = -1, PathStart = -1, QueryStart = -1, FragmentStart = -1, End = 0 };
}

/// <summary>
/// The basic URL parser of the WHATWG URL Standard (section 4.4), written state by state from the Standard's text. Validation errors
/// that do not fail the parse are not recorded; the outcome is a URL record or failure.
/// </summary>
internal static class UrlParser
{
    private const int Eof = -1;

    /// <summary>Parses an input into a new URL record (the URL parser of section 4.4, without the blob lookup).</summary>
    /// <param name="input">The text.</param>
    /// <param name="baseUrl">The base URL, or <see langword="null"/>.</param>
    /// <param name="url">The record.</param>
    /// <param name="failure">The reason, on failure.</param>
    /// <returns><see langword="false"/> for failure.</returns>
    internal static bool TryParse(string input, UrlRecord? baseUrl, out UrlRecord url, out UrlFailure failure)
    {
        UrlSpans spans = UrlSpans.Empty;
        return TryParse(input, baseUrl, out url, out failure, ref spans);
    }

    /// <summary>Parses an input into a new URL record, also reporting where the components are.</summary>
    /// <param name="input">The text.</param>
    /// <param name="baseUrl">The base URL, or <see langword="null"/>.</param>
    /// <param name="url">The record.</param>
    /// <param name="failure">The reason, on failure.</param>
    /// <param name="spans">The component offsets into <paramref name="input"/>.</param>
    /// <returns><see langword="false"/> for failure.</returns>
    internal static bool TryParse(string input, UrlRecord? baseUrl, out UrlRecord url, out UrlFailure failure, ref UrlSpans spans)
    {
        url = new UrlRecord();

        // Remove leading and trailing C0 controls and spaces, then all ASCII tabs and newlines.
        int first = 0;
        int last = input.Length;
        while (first < last && input[first] <= 0x20)
        {
            first++;
        }

        while (last > first && input[last - 1] <= 0x20)
        {
            last--;
        }

        if (baseUrl is null && TryParsePlainWebUrl(input, first, last, url, ref spans))
        {
            failure = UrlFailure.None;
            return true;
        }

        Prepare(input, first, last, out int[] cps, out int[] origin);
        bool ok = Run(url, cps, origin, baseUrl, null, out failure, ref spans);
        return ok;
    }

    /// <summary>The same parse without the shortcut for plain web addresses, so a test can hold the shortcut to the state machine.</summary>
    /// <param name="input">The text.</param>
    /// <param name="url">The record.</param>
    /// <param name="failure">The reason, on failure.</param>
    /// <param name="spans">The component offsets.</param>
    /// <returns><see langword="false"/> for failure.</returns>
    internal static bool TryParseByStateMachine(string input, out UrlRecord url, out UrlFailure failure, ref UrlSpans spans)
    {
        url = new UrlRecord();
        int first = 0;
        int last = input.Length;
        while (first < last && input[first] <= 0x20)
        {
            first++;
        }

        while (last > first && input[last - 1] <= 0x20)
        {
            last--;
        }

        Prepare(input, first, last, out int[] cps, out int[] origin);
        return Run(url, cps, origin, null, null, out failure, ref spans);
    }

    /// <summary>Runs only the shortcut for plain web addresses.</summary>
    /// <param name="input">The text.</param>
    /// <param name="url">The record, filled when the shortcut applies.</param>
    /// <param name="spans">The component offsets.</param>
    /// <returns><see langword="true"/> when the shortcut applied.</returns>
    internal static bool TryParsePlain(string input, out UrlRecord url, ref UrlSpans spans)
    {
        url = new UrlRecord();
        int first = 0;
        int last = input.Length;
        while (first < last && input[first] <= 0x20)
        {
            first++;
        }

        while (last > first && input[last - 1] <= 0x20)
        {
            last--;
        }

        return TryParsePlainWebUrl(input, first, last, url, ref spans);
    }

    // The common shape of a web address: a special scheme other than file, "//", a host without credentials or brackets, an optional
    // port, and a path, query and fragment. The state machine's work on these is splitting the text and percent-encoding it, which is
    // done here the same way (the percent-encode sets, backslash as a slash, dot segments); the host goes to the same host parser. The
    // record is filled the way the machine would fill it (the offsets in <paramref name="spans"/> included). Anything else (credentials,
    // IPv6 addresses, tabs and newlines inside the text, a host or port that does not parse) returns false and takes the full parser,
    // which also decides every failure.
    private static bool TryParsePlainWebUrl(string input, int first, int last, UrlRecord url, ref UrlSpans spans)
    {
        ReadOnlySpan<char> s = input.AsSpan(first, last - first);
        if (!TryReadSpecialScheme(s, out string scheme, out int schemeLength, out int defaultPort))
        {
            return false;
        }

        if (s.Length < schemeLength + 4 || s[schemeLength] != ':' || s[schemeLength + 1] != '/' || s[schemeLength + 2] != '/')
        {
            return false;
        }

        // The machine drops tabs and newlines before it reads anything.
        if (s.IndexOfAny('\t', '\n', '\r') >= 0)
        {
            return false;
        }

        int hostStart = schemeLength + 3;
        int authorityLength = s[hostStart..].IndexOfAny(AuthorityEnd);
        int authorityEnd = authorityLength < 0 ? s.Length : hostStart + authorityLength;
        ReadOnlySpan<char> authority = s[hostStart..authorityEnd];
        if (authority.IndexOfAny('@', '[', ']') >= 0)
        {
            return false;
        }

        int colon = authority.IndexOf(':');
        int hostEnd = colon < 0 ? authorityEnd : hostStart + colon;
        if (hostEnd == hostStart)
        {
            return false;
        }

        int port = -1;
        int portStart = -1;
        int portEnd = -1;
        if (colon >= 0)
        {
            portStart = hostEnd + 1;
            portEnd = authorityEnd;
            if (portEnd == portStart)
            {
                return false;
            }

            long value = 0;
            for (int p = portStart; p < portEnd; p++)
            {
                if (!char.IsAsciiDigit(s[p]))
                {
                    return false;
                }

                value = (value * 10) + (s[p] - '0');
                if (value > 65535)
                {
                    return false;
                }
            }

            port = value == defaultPort ? -1 : (int)value;
        }

        ReadOnlySpan<char> host = s[hostStart..hostEnd];
        string hostText;
        HostKind hostKind;
        if (IsPlainDomain(host))
        {
            hostText = host.ToString().ToLowerInvariant();
            hostKind = HostKind.Domain;
        }
        else
        {
            // Credentials and brackets are out; what is left is for the host parser, as the machine would call it.
            foreach (char c in host)
            {
                if (char.IsSurrogate(c))
                {
                    return false;
                }
            }

            if (!HostParser.TryParse(host.ToString(), false, out hostText, out hostKind))
            {
                return false;
            }
        }

        int pathStart = authorityEnd;
        int pathEnd = pathStart;
        if (pathStart < s.Length && s[pathStart] is '/' or '\\')
        {
            int q = s[pathStart..].IndexOfAny('?', '#');
            pathEnd = q < 0 ? s.Length : pathStart + q;
        }

        int queryStart = -1;
        int queryEnd = pathEnd;
        if (pathEnd < s.Length && s[pathEnd] == '?')
        {
            queryStart = pathEnd + 1;
            int h = s[queryStart..].IndexOf('#');
            queryEnd = h < 0 ? s.Length : queryStart + h;
        }

        int fragmentStart = queryEnd < s.Length ? queryEnd + 1 : -1;

        url.Scheme = scheme;
        url.Host = hostText;
        url.HostKind = hostKind;
        url.Port = port;
        if (pathEnd == pathStart)
        {
            url.Path.Add(string.Empty);
        }
        else
        {
            // Segments are cut at / or \ (special schemes); the first separator belongs to the path-start state. "/a/b" has the segments a
            // and b; a trailing slash leaves an empty last segment. "." segments vanish and ".." removes the one before (section 4.4, path state).
            int segmentStart = pathStart + 1;
            for (int k = segmentStart; k <= pathEnd; k++)
            {
                if (k == pathEnd || s[k] is '/' or '\\')
                {
                    bool slash = k < pathEnd;
                    string segment = EncodeComponent(s[segmentStart..k], PercentEncodeSet.Path);
                    if (segment.Length is >= 1 and <= 6 && (segment[0] is '.' or '%'))
                    {
                        if (IsDoubleDot(segment))
                        {
                            ShortenPath(url);
                            if (!slash)
                            {
                                url.Path.Add(string.Empty);
                            }

                            segmentStart = k + 1;
                            continue;
                        }

                        if (IsSingleDot(segment))
                        {
                            if (!slash)
                            {
                                url.Path.Add(string.Empty);
                            }

                            segmentStart = k + 1;
                            continue;
                        }
                    }

                    url.Path.Add(segment);
                    segmentStart = k + 1;
                }
            }
        }

        if (queryStart >= 0)
        {
            url.Query = EncodeComponent(s[queryStart..queryEnd], PercentEncodeSet.SpecialQuery);
        }

        if (fragmentStart >= 0)
        {
            url.Fragment = EncodeComponent(s[fragmentStart..], PercentEncodeSet.Fragment);
        }

        spans.End = last;
        spans.SchemeEnd = first + schemeLength;
        spans.HostStart = first + hostStart;
        spans.HostEnd = first + hostEnd;
        if (portStart >= 0)
        {
            spans.PortStart = first + portStart;
            spans.PortEnd = first + portEnd;
        }

        spans.PathStart = first + pathStart;
        if (queryStart >= 0)
        {
            spans.QueryStart = first + queryStart;
        }

        if (fragmentStart >= 0)
        {
            spans.FragmentStart = first + fragmentStart;
        }

        return true;
    }

    // http, https, ftp, ws and wss, in any letter case, with the scheme's default port.
    private static bool TryReadSpecialScheme(ReadOnlySpan<char> s, out string scheme, out int length, out int defaultPort)
    {
        scheme = string.Empty;
        length = 0;
        defaultPort = 0;
        if (s.Length < 7)
        {
            return false;
        }

        char c0 = (char)(s[0] | 0x20);
        char c1 = (char)(s[1] | 0x20);
        char c2 = (char)(s[2] | 0x20);
        if (c0 == 'h' && c1 == 't' && c2 == 't' && (s[3] | 0x20) == 'p')
        {
            if ((s[4] | 0x20) == 's')
            {
                scheme = "https";
                length = 5;
                defaultPort = 443;
            }
            else
            {
                scheme = "http";
                length = 4;
                defaultPort = 80;
            }

            return true;
        }

        if (c0 == 'f' && c1 == 't' && c2 == 'p')
        {
            scheme = "ftp";
            length = 3;
            defaultPort = 21;
            return true;
        }

        if (c0 == 'w' && c1 == 's')
        {
            bool secure = c2 == 's';
            scheme = secure ? "wss" : "ws";
            length = secure ? 3 : 2;
            defaultPort = secure ? 443 : 80;
            return secure || s[2] == ':';
        }

        return false;
    }

    // Letters, digits, hyphens and dots only, no empty label, no label that starts with xn--, and a last label that is not a number
    // (which would make the host an IPv4 address): for these the host parser only lowercases.
    private static bool IsPlainDomain(ReadOnlySpan<char> host)
    {
        int lastDot = -1;
        for (int i = 0; i < host.Length; i++)
        {
            char c = host[i];
            if (char.IsAsciiLetterOrDigit(c) || c == '-')
            {
                continue;
            }

            if (c != '.' || i == 0 || lastDot == i - 1)
            {
                return false;
            }

            lastDot = i;
        }

        if (lastDot == host.Length - 1 || host.Contains("xn--", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !IsNumberLabel(lastDot < 0 ? host : host[(lastDot + 1)..]);
    }

    // Percent-encodes a component as the machine does, one scalar value at a time (a lone surrogate reads as U+FFFD).
    private static string EncodeComponent(ReadOnlySpan<char> raw, PercentEncodeSet set)
    {
        int i = 0;
        while (i < raw.Length && !PercentEncoding.InSet(raw[i], set))
        {
            i++;
        }

        if (i == raw.Length)
        {
            return raw.ToString();
        }

        var sb = new StringBuilder(raw.Length + 8);
        sb.Append(raw[..i]);
        for (; i < raw.Length; i++)
        {
            char c = raw[i];
            int cp = c;
            if (char.IsHighSurrogate(c) && i + 1 < raw.Length && char.IsLowSurrogate(raw[i + 1]))
            {
                cp = char.ConvertToUtf32(c, raw[i + 1]);
                i++;
            }
            else if (char.IsSurrogate(c))
            {
                cp = 0xFFFD;
            }

            PercentEncoding.AppendEncoded(sb, cp, set);
        }

        return sb.ToString();
    }

    private static readonly System.Buffers.SearchValues<char> AuthorityEnd = System.Buffers.SearchValues.Create("/\\?#");

    private static readonly System.Buffers.SearchValues<char> HexDigits = System.Buffers.SearchValues.Create("0123456789abcdefABCDEF");

    private static bool IsNumberLabel(ReadOnlySpan<char> label)
    {
        if (label.IsEmpty)
        {
            return false;
        }

        bool digits = true;
        foreach (char c in label)
        {
            digits &= char.IsAsciiDigit(c);
        }

        if (digits)
        {
            return true;
        }

        return label.Length >= 2 && label[0] == '0' && (label[1] | 0x20) == 'x' && label[2..].IndexOfAnyExcept(HexDigits) < 0;
    }

    /// <summary>Runs the basic URL parser on an existing URL with a state override (the URL API's setters).</summary>
    /// <param name="url">The record to change.</param>
    /// <param name="input">The new value.</param>
    /// <param name="stateOverride">The state to start in.</param>
    /// <returns><see langword="false"/> for failure; a parse that ends without a change returns <see langword="true"/>.</returns>
    internal static bool Override(UrlRecord url, string input, UrlState stateOverride)
    {
        Prepare(input, 0, input.Length, out int[] cps, out int[] origin);
        UrlSpans spans = UrlSpans.Empty;
        return Run(url, cps, origin, null, stateOverride, out _, ref spans);
    }

    // Turns the text into code points (a lone surrogate becomes U+FFFD, as for a scalar value string), dropping ASCII tab and newline,
    // and remembers each code point's UTF-16 offset in the input; the entry after the last is the end offset.
    private static void Prepare(string input, int first, int last, out int[] cps, out int[] origin)
    {
        // Count first so both arrays are allocated once, at their final size.
        int count = 0;
        for (int i = first; i < last; i++)
        {
            char c = input[i];
            if (c is '\t' or '\n' or '\r')
            {
                continue;
            }

            if (char.IsHighSurrogate(c) && i + 1 < last && char.IsLowSurrogate(input[i + 1]))
            {
                i++;
            }

            count++;
        }

        cps = new int[count];
        origin = new int[count + 1];
        int n = 0;
        for (int i = first; i < last; i++)
        {
            char c = input[i];
            if (c is '\t' or '\n' or '\r')
            {
                continue;
            }

            int cp = c;
            int start = i;
            if (char.IsHighSurrogate(c) && i + 1 < last && char.IsLowSurrogate(input[i + 1]))
            {
                cp = char.ConvertToUtf32(c, input[i + 1]);
                i++;
            }
            else if (char.IsSurrogate(c))
            {
                cp = 0xFFFD;
            }

            cps[n] = cp;
            origin[n] = start;
            n++;
        }

        origin[n] = last;
    }

    private static bool IsAsciiAlpha(int c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z';

    private static bool IsAsciiDigit(int c) => c is >= '0' and <= '9';

    private static bool IsWindowsDriveLetter(string s) => s.Length == 2 && IsAsciiAlpha(s[0]) && (s[1] == ':' || s[1] == '|');

    private static bool IsNormalizedWindowsDriveLetter(string s) => s.Length == 2 && IsAsciiAlpha(s[0]) && s[1] == ':';

    // Section 4.2: a string starts with a Windows drive letter when its first two code points are one and the string ends there or
    // continues with / \ ? #.
    private static bool StartsWithWindowsDriveLetter(int[] cps, int from)
    {
        int remaining = cps.Length - from;
        if (remaining < 2 || !IsAsciiAlpha(cps[from]) || (cps[from + 1] != ':' && cps[from + 1] != '|'))
        {
            return false;
        }

        return remaining == 2 || cps[from + 2] is '/' or '\\' or '?' or '#';
    }

    private static bool IsSingleDot(string s) => s == "." || s.Equals("%2e", StringComparison.OrdinalIgnoreCase);

    private static bool IsDoubleDot(string s) =>
        s == ".." || s.Equals(".%2e", StringComparison.OrdinalIgnoreCase) || s.Equals("%2e.", StringComparison.OrdinalIgnoreCase) || s.Equals("%2e%2e", StringComparison.OrdinalIgnoreCase);

    // Section 4.2, "shorten a URL's path".
    private static void ShortenPath(UrlRecord url)
    {
        List<string> path = url.Path;
        if (url.Scheme == "file" && path.Count == 1 && IsNormalizedWindowsDriveLetter(path[0]))
        {
            return;
        }

        if (path.Count > 0)
        {
            path.RemoveAt(path.Count - 1);
        }
    }

    private static bool Fail(out UrlFailure failure, UrlFailure reason)
    {
        failure = reason;
        return false;
    }

    private static bool Run(UrlRecord url, int[] cps, int[] origin, UrlRecord? baseUrl, UrlState? stateOverride, out UrlFailure failure, ref UrlSpans spans)
    {
        failure = UrlFailure.None;
        bool track = stateOverride is null && baseUrl is null;
        UrlState state = stateOverride ?? UrlState.SchemeStart;
        var buffer = new StringBuilder();
        StringBuilder? opaquePath = null; // created when the opaque path state first writes; appended to url.OpaquePath when that state ends
        StringBuilder? fragment = null; // created when the fragment state first writes; appended to url.Fragment when the parse ends
        int bufferLength = 0; // code points in buffer, for the authority state's pointer rewind
        bool atSignSeen = false;

        // The credentials grow by one chunk per @ sign, so they are collected here and stored once when the authority ends; copying them at every @ would be quadratic.
        StringBuilder? usernameBuilder = null;
        StringBuilder? passwordBuilder = null;
        bool insideBrackets = false;
        bool passwordTokenSeen = false;
        int pointer = 0;
        int length = cps.Length;
        spans.End = origin[length];

        int Off(int index) => origin[Math.Clamp(index, 0, length)];

        for (; ; pointer++)
        {
            int c = (uint)pointer < (uint)length ? cps[pointer] : Eof;
            bool RemainingStartsWith(int a) => pointer + 1 < length && cps[pointer + 1] == a;

            switch (state)
            {
                case UrlState.SchemeStart:
                    if (IsAsciiAlpha(c))
                    {
                        buffer.Append((char)(c | 0x20));
                        state = UrlState.Scheme;
                    }
                    else if (stateOverride is null)
                    {
                        state = UrlState.NoScheme;
                        pointer--;
                    }
                    else
                    {
                        return Fail(out failure, UrlFailure.Other);
                    }

                    break;

                case UrlState.Scheme:
                    if (IsAsciiAlpha(c) || IsAsciiDigit(c) || c is '+' or '-' or '.')
                    {
                        buffer.Append(char.ToLowerInvariant((char)c));
                    }
                    else if (c == ':')
                    {
                        string scheme = buffer.ToString();
                        if (stateOverride is not null)
                        {
                            if (url.IsSpecial != SpecialSchemes.IsSpecial(scheme))
                            {
                                return true;
                            }

                            if ((url.IncludesCredentials || url.Port >= 0) && scheme == "file")
                            {
                                return true;
                            }

                            if (url.Scheme == "file" && url.Host is { Length: 0 })
                            {
                                return true;
                            }
                        }

                        url.Scheme = scheme;
                        if (stateOverride is not null)
                        {
                            if (url.Port == SpecialSchemes.DefaultPort(url.Scheme))
                            {
                                url.Port = -1;
                            }

                            return true;
                        }

                        if (track)
                        {
                            spans.SchemeEnd = Off(pointer);
                        }

                        buffer.Clear();
                        bufferLength = 0;
                        if (url.Scheme == "file")
                        {
                            state = UrlState.File;
                        }
                        else if (url.IsSpecial && baseUrl is not null && baseUrl.Scheme == url.Scheme)
                        {
                            state = UrlState.SpecialRelativeOrAuthority;
                        }
                        else if (url.IsSpecial)
                        {
                            state = UrlState.SpecialAuthoritySlashes;
                        }
                        else if (RemainingStartsWith('/'))
                        {
                            state = UrlState.PathOrAuthority;
                            pointer++;
                        }
                        else
                        {
                            url.OpaquePath = string.Empty;
                            if (track)
                            {
                                spans.PathStart = Off(pointer + 1);
                            }

                            state = UrlState.OpaquePath;
                        }
                    }
                    else if (stateOverride is null)
                    {
                        buffer.Clear();
                        bufferLength = 0;
                        state = UrlState.NoScheme;
                        pointer = -1;
                    }
                    else
                    {
                        return Fail(out failure, UrlFailure.Other);
                    }

                    break;

                case UrlState.NoScheme:
                    if (baseUrl is null || (baseUrl.HasOpaquePath && c != '#'))
                    {
                        return Fail(out failure, UrlFailure.MissingScheme);
                    }

                    if (baseUrl.HasOpaquePath && c == '#')
                    {
                        url.Scheme = baseUrl.Scheme;
                        url.OpaquePath = baseUrl.OpaquePath;
                        url.Query = baseUrl.Query;
                        url.Fragment = string.Empty;
                        state = UrlState.Fragment;
                    }
                    else if (baseUrl.Scheme != "file")
                    {
                        state = UrlState.Relative;
                        pointer--;
                    }
                    else
                    {
                        state = UrlState.File;
                        pointer--;
                    }

                    break;

                case UrlState.SpecialRelativeOrAuthority:
                    if (c == '/' && RemainingStartsWith('/'))
                    {
                        state = UrlState.SpecialAuthorityIgnoreSlashes;
                        pointer++;
                    }
                    else
                    {
                        state = UrlState.Relative;
                        pointer--;
                    }

                    break;

                case UrlState.PathOrAuthority:
                    if (c == '/')
                    {
                        state = UrlState.Authority;
                    }
                    else
                    {
                        state = UrlState.Path;
                        pointer--;
                    }

                    break;

                case UrlState.Relative:
                    url.Scheme = baseUrl!.Scheme;
                    if (c == '/')
                    {
                        state = UrlState.RelativeSlash;
                    }
                    else if (url.IsSpecial && c == '\\')
                    {
                        state = UrlState.RelativeSlash;
                    }
                    else
                    {
                        CopyAuthority(url, baseUrl);
                        url.SetPath([.. baseUrl.Path]);
                        url.Query = baseUrl.Query;
                        if (c == '?')
                        {
                            url.Query = string.Empty;
                            state = UrlState.Query;
                        }
                        else if (c == '#')
                        {
                            url.Fragment = string.Empty;
                            state = UrlState.Fragment;
                        }
                        else if (c != Eof)
                        {
                            url.Query = null;
                            ShortenPath(url);
                            state = UrlState.Path;
                            pointer--;
                        }
                    }

                    break;

                case UrlState.RelativeSlash:
                    if (url.IsSpecial && (c == '/' || c == '\\'))
                    {
                        state = UrlState.SpecialAuthorityIgnoreSlashes;
                    }
                    else if (c == '/')
                    {
                        state = UrlState.Authority;
                    }
                    else
                    {
                        CopyAuthority(url, baseUrl!);
                        state = UrlState.Path;
                        pointer--;
                    }

                    break;

                case UrlState.SpecialAuthoritySlashes:
                    if (c == '/' && RemainingStartsWith('/'))
                    {
                        state = UrlState.SpecialAuthorityIgnoreSlashes;
                        pointer++;
                    }
                    else
                    {
                        state = UrlState.SpecialAuthorityIgnoreSlashes;
                        pointer--;
                    }

                    break;

                case UrlState.SpecialAuthorityIgnoreSlashes:
                    if (c != '/' && c != '\\')
                    {
                        state = UrlState.Authority;
                        pointer--;
                    }

                    break;

                case UrlState.Authority:
                    if (c == '@')
                    {
                        if (atSignSeen)
                        {
                            buffer.Insert(0, "%40");
                        }

                        atSignSeen = true;
                        StringBuilder username = usernameBuilder ??= new StringBuilder(url.Username);
                        StringBuilder password = passwordBuilder ??= new StringBuilder(url.Password);
                        foreach (Rune r in buffer.ToString().EnumerateRunes())
                        {
                            if (r.Value == ':' && !passwordTokenSeen)
                            {
                                passwordTokenSeen = true;
                                continue;
                            }

                            PercentEncoding.AppendEncoded(passwordTokenSeen ? password : username, r.Value, PercentEncodeSet.Userinfo);
                        }

                        buffer.Clear();
                        bufferLength = 0;
                    }
                    else if (c is Eof or '/' or '?' or '#' || (url.IsSpecial && c == '\\'))
                    {
                        if (atSignSeen && buffer.Length == 0)
                        {
                            return Fail(out failure, UrlFailure.HostMissing);
                        }

                        if (usernameBuilder is not null && passwordBuilder is not null)
                        {
                            url.Username = usernameBuilder.ToString();
                            url.Password = passwordBuilder.ToString();
                        }

                        pointer -= bufferLength + 1;
                        buffer.Clear();
                        bufferLength = 0;
                        state = UrlState.Host;
                    }
                    else
                    {
                        AppendCodePoint(buffer, c);
                        bufferLength++;
                    }

                    break;

                case UrlState.Host:
                case UrlState.Hostname:
                    if (stateOverride is not null && url.Scheme == "file")
                    {
                        pointer--;
                        state = UrlState.FileHost;
                    }
                    else if (c == ':' && !insideBrackets)
                    {
                        if (buffer.Length == 0)
                        {
                            return Fail(out failure, UrlFailure.HostMissing);
                        }

                        if (stateOverride == UrlState.Hostname)
                        {
                            return Fail(out failure, UrlFailure.Other);
                        }

                        if (!HostParser.TryParse(buffer.ToString(), !url.IsSpecial, out string host, out HostKind kind))
                        {
                            return Fail(out failure, UrlFailure.InvalidHost);
                        }

                        if (track)
                        {
                            spans.HostStart = Off(pointer - bufferLength);
                            spans.HostEnd = Off(pointer);
                            spans.PortStart = Off(pointer + 1);
                        }

                        url.Host = host;
                        url.HostKind = kind;
                        buffer.Clear();
                        bufferLength = 0;
                        state = UrlState.Port;
                    }
                    else if (c is Eof or '/' or '?' or '#' || (url.IsSpecial && c == '\\'))
                    {
                        int hostEnd = pointer;
                        pointer--;
                        if (url.IsSpecial && buffer.Length == 0)
                        {
                            return Fail(out failure, UrlFailure.HostMissing);
                        }

                        if (stateOverride is not null && buffer.Length == 0 && (url.IncludesCredentials || url.Port >= 0))
                        {
                            return Fail(out failure, UrlFailure.Other);
                        }

                        if (!HostParser.TryParse(buffer.ToString(), !url.IsSpecial, out string host, out HostKind kind))
                        {
                            return Fail(out failure, UrlFailure.InvalidHost);
                        }

                        if (track)
                        {
                            spans.HostStart = Off(hostEnd - bufferLength);
                            spans.HostEnd = Off(hostEnd);
                        }

                        url.Host = host;
                        url.HostKind = kind;
                        buffer.Clear();
                        bufferLength = 0;
                        state = UrlState.PathStart;
                        if (stateOverride is not null)
                        {
                            return true;
                        }
                    }
                    else
                    {
                        if (c == '[')
                        {
                            insideBrackets = true;
                        }

                        if (c == ']')
                        {
                            insideBrackets = false;
                        }

                        AppendCodePoint(buffer, c);
                        bufferLength++;
                    }

                    break;

                case UrlState.Port:
                    if (IsAsciiDigit(c))
                    {
                        buffer.Append((char)c);
                    }
                    else if (c is Eof or '/' or '?' or '#' || (url.IsSpecial && c == '\\') || stateOverride is not null)
                    {
                        if (buffer.Length != 0)
                        {
                            int port = 0;
                            foreach (char d in buffer.ToString())
                            {
                                port = (port * 10) + (d - '0');
                                if (port > 65535)
                                {
                                    return Fail(out failure, UrlFailure.InvalidPort);
                                }
                            }

                            if (track)
                            {
                                spans.PortEnd = Off(pointer);
                            }

                            url.Port = port == SpecialSchemes.DefaultPort(url.Scheme) ? -1 : port;
                            buffer.Clear();
                            if (stateOverride is not null)
                            {
                                return true;
                            }
                        }

                        if (stateOverride is not null)
                        {
                            return Fail(out failure, UrlFailure.Other);
                        }

                        state = UrlState.PathStart;
                        pointer--;
                    }
                    else
                    {
                        return Fail(out failure, UrlFailure.InvalidPort);
                    }

                    break;

                case UrlState.File:
                    url.Scheme = "file";
                    url.Host = string.Empty;
                    url.HostKind = HostKind.Empty;
                    if (c == '/' || c == '\\')
                    {
                        state = UrlState.FileSlash;
                    }
                    else if (baseUrl is not null && baseUrl.Scheme == "file")
                    {
                        url.Host = baseUrl.Host;
                        url.HostKind = baseUrl.HostKind;
                        url.SetPath([.. baseUrl.Path]);
                        url.Query = baseUrl.Query;
                        if (c == '?')
                        {
                            url.Query = string.Empty;
                            state = UrlState.Query;
                        }
                        else if (c == '#')
                        {
                            url.Fragment = string.Empty;
                            state = UrlState.Fragment;
                        }
                        else if (c != Eof)
                        {
                            url.Query = null;
                            if (!StartsWithWindowsDriveLetter(cps, pointer))
                            {
                                ShortenPath(url);
                            }
                            else
                            {
                                url.Path.Clear();
                            }

                            state = UrlState.Path;
                            pointer--;
                        }
                    }
                    else
                    {
                        state = UrlState.Path;
                        pointer--;
                    }

                    break;

                case UrlState.FileSlash:
                    if (c == '/' || c == '\\')
                    {
                        state = UrlState.FileHost;
                    }
                    else
                    {
                        if (baseUrl is not null && baseUrl.Scheme == "file")
                        {
                            url.Host = baseUrl.Host;
                            url.HostKind = baseUrl.HostKind;
                            if (!StartsWithWindowsDriveLetter(cps, pointer) && baseUrl.Path.Count > 0 && IsNormalizedWindowsDriveLetter(baseUrl.Path[0]))
                            {
                                url.Path.Add(baseUrl.Path[0]);
                            }
                        }

                        state = UrlState.Path;
                        pointer--;
                    }

                    break;

                case UrlState.FileHost:
                    if (c is Eof or '/' or '\\' or '?' or '#')
                    {
                        int hostEnd = pointer;
                        pointer--;
                        if (stateOverride is null && IsWindowsDriveLetter(buffer.ToString()))
                        {
                            // The buffer is kept: the path state reads the drive letter from it.
                            state = UrlState.Path;
                        }
                        else if (buffer.Length == 0)
                        {
                            url.Host = string.Empty;
                            url.HostKind = HostKind.Empty;
                            if (stateOverride is not null)
                            {
                                return true;
                            }

                            state = UrlState.PathStart;
                        }
                        else
                        {
                            if (!HostParser.TryParse(buffer.ToString(), !url.IsSpecial, out string host, out HostKind kind))
                            {
                                return Fail(out failure, UrlFailure.InvalidHost);
                            }

                            if (host == "localhost")
                            {
                                host = string.Empty;
                                kind = HostKind.Empty;
                            }

                            if (track)
                            {
                                spans.HostStart = Off(hostEnd - bufferLength);
                                spans.HostEnd = Off(hostEnd);
                            }

                            url.Host = host;
                            url.HostKind = kind;
                            if (stateOverride is not null)
                            {
                                return true;
                            }

                            buffer.Clear();
                            bufferLength = 0;
                            state = UrlState.PathStart;
                        }
                    }
                    else
                    {
                        AppendCodePoint(buffer, c);
                        bufferLength++;
                    }

                    break;

                case UrlState.PathStart:
                    if (track && spans.PathStart < 0)
                    {
                        spans.PathStart = Off(pointer);
                    }

                    if (url.IsSpecial)
                    {
                        state = UrlState.Path;
                        if (c != '/' && c != '\\')
                        {
                            pointer--;
                        }
                    }
                    else if (stateOverride is null && c == '?')
                    {
                        url.Query = string.Empty;
                        if (track)
                        {
                            spans.QueryStart = Off(pointer + 1);
                        }

                        state = UrlState.Query;
                    }
                    else if (stateOverride is null && c == '#')
                    {
                        url.Fragment = string.Empty;
                        if (track)
                        {
                            spans.FragmentStart = Off(pointer + 1);
                        }

                        state = UrlState.Fragment;
                    }
                    else if (c != Eof)
                    {
                        state = UrlState.Path;
                        if (c != '/')
                        {
                            pointer--;
                        }
                    }
                    else if (stateOverride is not null && url.Host is null)
                    {
                        url.Path.Add(string.Empty);
                    }

                    break;

                case UrlState.Path:
                    if (c is Eof or '/' || (url.IsSpecial && c == '\\') || (stateOverride is null && c is '?' or '#'))
                    {
                        string segment = buffer.ToString();
                        bool slash = c == '/' || (url.IsSpecial && c == '\\');
                        if (IsDoubleDot(segment))
                        {
                            ShortenPath(url);
                            if (!slash)
                            {
                                url.Path.Add(string.Empty);
                            }
                        }
                        else if (IsSingleDot(segment))
                        {
                            if (!slash)
                            {
                                url.Path.Add(string.Empty);
                            }
                        }
                        else
                        {
                            if (url.Scheme == "file" && url.Path.Count == 0 && IsWindowsDriveLetter(segment))
                            {
                                segment = string.Concat(segment[0].ToString(), ":");
                            }

                            url.Path.Add(segment);
                        }

                        buffer.Clear();
                        bufferLength = 0;
                        if (c == '?')
                        {
                            url.Query = string.Empty;
                            if (track)
                            {
                                spans.QueryStart = Off(pointer + 1);
                            }

                            state = UrlState.Query;
                        }

                        if (c == '#')
                        {
                            url.Fragment = string.Empty;
                            if (track)
                            {
                                spans.FragmentStart = Off(pointer + 1);
                            }

                            state = UrlState.Fragment;
                        }
                    }
                    else
                    {
                        PercentEncoding.AppendEncoded(buffer, c, PercentEncodeSet.Path);
                    }

                    break;

                case UrlState.OpaquePath:
                    if (c == '?')
                    {
                        url.OpaquePath += opaquePath?.ToString();
                        opaquePath?.Clear();
                        url.Query = string.Empty;
                        if (track)
                        {
                            spans.QueryStart = Off(pointer + 1);
                        }

                        state = UrlState.Query;
                    }
                    else if (c == '#')
                    {
                        url.OpaquePath += opaquePath?.ToString();
                        opaquePath?.Clear();
                        url.Fragment = string.Empty;
                        if (track)
                        {
                            spans.FragmentStart = Off(pointer + 1);
                        }

                        state = UrlState.Fragment;
                    }
                    else if (c == ' ')
                    {
                        (opaquePath ??= new StringBuilder()).Append(RemainingStartsWith('?') || RemainingStartsWith('#') ? "%20" : " ");
                    }
                    else if (c != Eof)
                    {
                        PercentEncoding.AppendEncoded(opaquePath ??= new StringBuilder(), c, PercentEncodeSet.C0Control);
                    }

                    break;

                case UrlState.Query:
                    if ((stateOverride is null && c == '#') || c == Eof)
                    {
                        PercentEncodeSet set = url.IsSpecial ? PercentEncodeSet.SpecialQuery : PercentEncodeSet.Query;
                        url.Query = (url.Query ?? string.Empty) + PercentEncoding.Encode(buffer.ToString(), set);
                        buffer.Clear();
                        if (c == '#')
                        {
                            url.Fragment = string.Empty;
                            if (track)
                            {
                                spans.FragmentStart = Off(pointer + 1);
                            }

                            state = UrlState.Fragment;
                        }
                    }
                    else
                    {
                        AppendCodePoint(buffer, c);
                    }

                    break;

                case UrlState.Fragment:
                    if (c != Eof)
                    {
                        PercentEncoding.AppendEncoded(fragment ??= new StringBuilder(), c, PercentEncodeSet.Fragment);
                    }

                    break;
            }

            if (pointer >= length)
            {
                break;
            }
        }

        if (opaquePath is { Length: > 0 })
        {
            url.OpaquePath += opaquePath.ToString();
        }

        if (fragment is { Length: > 0 })
        {
            url.Fragment = (url.Fragment ?? string.Empty) + fragment.ToString();
        }

        return true;
    }

    private static void CopyAuthority(UrlRecord url, UrlRecord from)
    {
        url.Username = from.Username;
        url.Password = from.Password;
        url.Host = from.Host;
        url.HostKind = from.HostKind;
        url.Port = from.Port;
    }

    private static void AppendCodePoint(StringBuilder sb, int codePoint)
    {
        if (codePoint > 0xFFFF)
        {
            sb.Append(char.ConvertFromUtf32(codePoint));
        }
        else
        {
            sb.Append((char)codePoint);
        }
    }
}
