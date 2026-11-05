using System.Buffers;

namespace RecursiveAI.OneContact.Web;

/// <summary>
/// The parsing seam for <see cref="WebAddress"/>: WHATWG URL parsing, then the canonical form (host lowercased and IDNA-processed,
/// default port, fragment and tracking parameters removed, <c>https</c> assumed when no scheme is written) and the facts.
/// </summary>
internal static class WebAddressParser
{
    // Schemes that are recognized without "//" after the colon: the ones written like "mailto:a@b" or "tel:+1555...".
    private static readonly string[] OpaqueSchemes = ["mailto", "tel", "sms", "urn", "geo", "callto", "skype", "magnet", "news", "data", "javascript", "about", "blob"];

    internal static OperationStatus TryParse(ReadOnlySpan<char> input, out WebAddress value, out ParseDetail detail)
    {
        int start = 0;
        int end = input.Length;
        while (start < end && char.IsWhiteSpace(input[start]))
        {
            start++;
        }

        while (end > start && char.IsWhiteSpace(input[end - 1]))
        {
            end--;
        }

        if (start == end)
        {
            value = default;
            detail = ParseDetail.Failure(ReasonCode.Empty, new SourceSpan(0, input.Length));
            return OperationStatus.InvalidData;
        }

        string text = input[start..end].ToString();
        bool assumed = !HasScheme(text);
        const string Prefix = "https://";
        string toParse = assumed ? Prefix + text : text;
        int shift = assumed ? -Prefix.Length : 0;

        UrlSpans spans = UrlSpans.Empty;
        if (!UrlParser.TryParse(toParse, null, out UrlRecord url, out UrlFailure failure, ref spans))
        {
            value = default;
            detail = ParseDetail.Failure(Reason(failure), new SourceSpan(start, end - start));
            return OperationStatus.InvalidData;
        }

        WebFacts facts = assumed ? WebFacts.SchemeAssumed : WebFacts.None;
        if (url.Fragment is { Length: > 0 })
        {
            facts |= WebFacts.FragmentRemoved;
        }

        if (url.IncludesCredentials)
        {
            facts |= WebFacts.HasCredentials;
        }

        if (url.Scheme is not ("http" or "https"))
        {
            facts |= WebFacts.NonWebScheme;
        }

        // Canonical form: the serialization without the fragment, and without tracking parameters in the query.
        if (url.Query is { Length: > 0 } query)
        {
            string? kept = TrackingParameters.Remove(query, out bool removed);
            if (removed)
            {
                url.Query = kept;
                facts |= WebFacts.TrackingParametersRemoved;
            }
        }

        string? publicSuffix = null;
        string? registrable = null;
        if (url.HostKind == HostKind.Domain && url.Host is { } host)
        {
            string name = host.EndsWith('.') ? host[..^1] : host;
            publicSuffix = PublicSuffixList.GetPublicSuffix(name);
            registrable = PublicSuffixList.GetRegistrableDomain(name);
            if (HasPunycodeLabel(host))
            {
                facts |= WebFacts.InternationalizedDomain;
            }
        }
        else if (url.HostKind is HostKind.Ipv4 or HostKind.Ipv6)
        {
            facts |= WebFacts.IpHost;
        }

        string pathText = url.SerializePath();
        facts |= SocialProfiles.Detect(url.Scheme, url.Host ?? string.Empty, pathText);

        string canonical = url.Serialize(excludeFragment: true);
        var parts = new WebParts(url.Scheme, url.Host ?? string.Empty, url.HostKind, url.Port, pathText, url.OpaquePath, url.Query, publicSuffix, registrable);
        value = new WebAddress(canonical, new SourceSpan(start, end - start), facts, parts);
        detail = new ParseDetail([], Fields(spans, shift, start, end, assumed));
        return OperationStatus.Done;
    }

    /// <summary>Tells whether text that can only be a web address by its shape would be read as one: a scheme, or a host name with a dot.</summary>
    /// <param name="text">Trimmed text.</param>
    /// <returns><see langword="true"/> for text that starts with a scheme and <c>//</c>, or begins with <c>www.</c>, or is a bare domain whose last label is a public suffix on the list.</returns>
    internal static bool LooksLikeWebAddress(ReadOnlySpan<char> text)
    {
        if (text.Contains("://", StringComparison.Ordinal) || text.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // A bare domain, optionally with a port, path, query or fragment: no spaces, no @ and a dot.
        int hostEnd = text.IndexOfAny('/', '?', '#');
        ReadOnlySpan<char> authority = hostEnd < 0 ? text : text[..hostEnd];
        int colon = authority.LastIndexOf(':');
        if (colon >= 0)
        {
            ReadOnlySpan<char> port = authority[(colon + 1)..];
            if (port.IsEmpty || port.Length > 5 || port.ToString().Any(c => !char.IsAsciiDigit(c)))
            {
                return false;
            }

            authority = authority[..colon];
        }

        if (authority.IsEmpty || authority.IndexOf('.') <= 0 || authority[^1] == '.')
        {
            return false;
        }

        foreach (char c in authority)
        {
            if (char.IsWhiteSpace(c) || c is '@' or '<' or '>' or '"' or ',' or ';')
            {
                return false;
            }
        }

        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                return false;
            }
        }

        // The last label must be a top-level domain the list knows ("example.com" yes, "notes.txt" no, "1.5" no).
        string name = authority.ToString();
        int lastDot = name.LastIndexOf('.');
        string tld = name[(lastDot + 1)..];
        if (tld.Length < 2 || !char.IsLetter(tld[0]))
        {
            return false;
        }

        return PublicSuffixList.IsKnownTopLevel(tld);
    }

    // The input has a scheme when it starts with scheme ":" and the colon is followed by "//", or the scheme is one that is written
    // without slashes. "host:8080/path" has no scheme: what follows the colon is a port.
    private static bool HasScheme(string text)
    {
        if (text.Length == 0 || !char.IsAsciiLetter(text[0]))
        {
            return false;
        }

        int i = 1;
        while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] is '+' or '-' or '.'))
        {
            i++;
        }

        if (i >= text.Length || text[i] != ':')
        {
            return false;
        }

        ReadOnlySpan<char> rest = text.AsSpan(i + 1);
        if (rest.StartsWith("//", StringComparison.Ordinal))
        {
            return true;
        }

        string scheme = text[..i].ToLowerInvariant();
        return Array.IndexOf(OpaqueSchemes, scheme) >= 0 || scheme is "http" or "https" or "ftp" or "ws" or "wss" or "file";
    }

    private static bool HasPunycodeLabel(string host)
    {
        foreach (string label in host.Split('.'))
        {
            if (label.StartsWith("xn--", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static ReasonCode Reason(UrlFailure failure) => failure switch
    {
        UrlFailure.HostMissing => ReasonCode.WebHostMissing,
        UrlFailure.InvalidHost => ReasonCode.WebInvalidHost,
        UrlFailure.InvalidPort => ReasonCode.WebInvalidPort,
        _ => ReasonCode.WebInvalidUrl,
    };

    // The component spans of the parse, moved from the text that was parsed back to the caller's input.
    private static List<ParsedField> Fields(UrlSpans s, int shift, int start, int end, bool assumed)
    {
        var fields = new List<ParsedField>();
        int Back(int offset) => Math.Clamp(offset + shift + start, start, end);

        void Add(string name, int from, int to)
        {
            if (from >= 0 && to >= from)
            {
                fields.Add(new ParsedField(name, new SourceSpan(Back(from), Back(to) - Back(from)), 1f, DecisionSource.Rule));
            }
        }

        if (!assumed && s.SchemeEnd >= 0)
        {
            Add("scheme", 0, s.SchemeEnd);
        }

        Add("host", s.HostStart, s.HostEnd);
        Add("port", s.PortStart, s.PortEnd);
        if (s.PathStart >= 0)
        {
            int pathEnd = s.QueryStart >= 0 ? s.QueryStart - 1 : s.FragmentStart >= 0 ? s.FragmentStart - 1 : s.End;
            Add("path", s.PathStart, pathEnd);
        }

        if (s.QueryStart >= 0)
        {
            Add("query", s.QueryStart, s.FragmentStart >= 0 ? s.FragmentStart - 1 : s.End);
        }

        if (s.FragmentStart >= 0)
        {
            Add("fragment", s.FragmentStart, s.End);
        }

        return fields;
    }
}
