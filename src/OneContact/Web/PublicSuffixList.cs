using System.Text;
using RecursiveAI.OneContact.Data;
using RecursiveAI.OneContact.Text.Idna;

namespace RecursiveAI.OneContact.Web;

/// <summary>
/// The Public Suffix List (https://publicsuffix.org/): which parts of a domain name are public suffixes ("com", "co.uk",
/// "github.io") and which name is the registrable domain, the public suffix plus one label ("example.co.uk"). The list shipped is
/// the unmodified upstream file; rules of ours, if any, come from a separate file and are treated as private-section rules.
/// </summary>
/// <remarks>
/// Both methods follow the algorithm published with the list: the longest matching rule prevails, an exception rule wins over any
/// other, and a name no rule matches has its last label as public suffix. Input may be written in Unicode or as <c>xn--</c> labels and
/// in any letter case; the answer is lowercase and keeps the script of the input labels. Nothing here looks at the network or the
/// operating system's culture.
/// </remarks>
public static class PublicSuffixList
{
    private const byte NormalIcann = 1;
    private const byte NormalPrivate = 2;
    private const byte WildcardIcann = 4;
    private const byte WildcardPrivate = 8;
    private const byte ExceptionIcann = 16;
    private const byte ExceptionPrivate = 32;

    private static readonly EmbeddedTables Tables = new(typeof(PublicSuffixList).Assembly, "OneContact.Data.psl.ocd");
    private static readonly Lazy<Dictionary<string, byte>> Rules = new(LoadRules, LazyThreadSafetyMode.ExecutionAndPublication);

    // The most labels any rule has (a wildcard rule matches one more): longer suffixes than that cannot match, so a host with
    // thousands of labels costs no more to look up than one with a few.
    private static readonly Lazy<int> MaxRuleLabels = new(
        () => Rules.Value.Keys.Max(key => key.Count(c => c == '.') + 2),
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Gets the public suffix of a domain name.</summary>
    /// <param name="domain">A domain name such as <c>www.example.co.uk</c>, in Unicode or ASCII form.</param>
    /// <param name="includePrivateSection">Whether the list's private section (such as <c>github.io</c>) counts; <see langword="true"/> by default.</param>
    /// <returns>The public suffix, lowercase, or <see langword="null"/> when the input is not a usable domain name (it is empty, starts or ends with a dot, has an empty label, or holds white space or a character of a URL such as <c>/</c> or <c>:</c>).</returns>
    public static string? GetPublicSuffix(string? domain, bool includePrivateSection = true)
    {
        if (!TryAnalyze(domain, includePrivateSection, out string[]? labels, out int suffixLabels))
        {
            return null;
        }

        return string.Join('.', labels.AsSpan(labels.Length - suffixLabels).ToArray());
    }

    /// <summary>Gets the registrable domain of a domain name: its public suffix and the one label in front of it.</summary>
    /// <param name="domain">A domain name such as <c>www.example.co.uk</c>, in Unicode or ASCII form.</param>
    /// <param name="includePrivateSection">Whether the list's private section counts; <see langword="true"/> by default.</param>
    /// <returns>The registrable domain, lowercase, or <see langword="null"/> when the input is not a usable domain name or is itself a public suffix.</returns>
    public static string? GetRegistrableDomain(string? domain, bool includePrivateSection = true)
    {
        if (!TryAnalyze(domain, includePrivateSection, out string[]? labels, out int suffixLabels) || labels.Length <= suffixLabels)
        {
            return null;
        }

        return string.Join('.', labels.AsSpan(labels.Length - suffixLabels - 1).ToArray());
    }

    /// <summary>
    /// Finds the public suffix and the registrable domain of a host in one pass, as <see cref="GetPublicSuffix"/> and
    /// <see cref="GetRegistrableDomain"/> would. A host that is plain lowercase ASCII (what URL parsing produces) is read in place and the
    /// answers are slices of it; anything else takes the general path.
    /// </summary>
    /// <param name="host">The host, without a trailing dot.</param>
    /// <param name="suffix">The public suffix, or null.</param>
    /// <param name="registrable">The registrable domain, or null.</param>
    internal static void Analyze(string host, out string? suffix, out string? registrable)
    {
        if (host.Length == 0 || host[0] == '.' || host[^1] == '.')
        {
            Slow(host, out suffix, out registrable);
            return;
        }

        // Label starts from the right: only as many as a rule can use matter, and one more tells whether labels remain on the left.
        int most = MaxRuleLabels.Value + 2;
        Span<int> starts = stackalloc int[most + 1]; // bounds: MaxRuleLabels is the longest rule in the list plus two, a few dozen at most
        int found = 0;
        int labelEnd = host.Length;
        bool plain = true;
        for (int i = host.Length - 1; i >= 0 && plain; i--)
        {
            char c = host[i];
            if (c >= 0x80 || c is >= 'A' and <= 'Z')
            {
                plain = false;
            }
            else if (c == '.')
            {
                if (i + 1 == labelEnd)
                {
                    plain = false; // an empty label
                }
                else
                {
                    if (found < most)
                    {
                        starts[found++] = i + 1;
                    }

                    labelEnd = i;
                }
            }
        }

        if (!plain)
        {
            Slow(host, out suffix, out registrable);
            return;
        }

        // The leftmost label starts at 0 when the scan reached it with room to spare.
        bool moreLeft = found >= most;
        int labels = found;
        if (!moreLeft)
        {
            starts[labels++] = 0;
        }

        // labelStart(k) is where the k-th label from the right begins; labels available to the right of the cut: min(labels, most).
        Dictionary<string, byte>.AlternateLookup<ReadOnlySpan<char>> rules = Rules.Value.GetAlternateLookup<ReadOnlySpan<char>>();
        int maxRule = MaxRuleLabels.Value;
        int limit = Math.Min(labels, maxRule);
        int best = 1;
        int exceptionLabels = 0;
        for (int k = 1; k <= limit; k++)
        {
            if (rules.TryGetValue(host.AsSpan(StartOf(starts, k - 1)), out byte mask))
            {
                if ((mask & (NormalIcann | NormalPrivate)) != 0)
                {
                    best = Math.Max(best, k);
                }

                if ((mask & (WildcardIcann | WildcardPrivate)) != 0 && (k < labels || moreLeft))
                {
                    best = Math.Max(best, k + 1);
                }

                if ((mask & (ExceptionIcann | ExceptionPrivate)) != 0)
                {
                    exceptionLabels = Math.Max(exceptionLabels, k);
                }
            }
        }

        int suffixLabels = exceptionLabels > 0 ? exceptionLabels - 1 : best;
        bool hasLabelBefore = suffixLabels < labels || moreLeft;
        suffixLabels = Math.Min(suffixLabels, moreLeft ? int.MaxValue : labels);
        suffix = host[StartOf(starts, suffixLabels - 1)..];
        registrable = hasLabelBefore && suffixLabels + 1 <= labels ? host[StartOf(starts, suffixLabels)..] : null;
    }

    private static int StartOf(Span<int> starts, int fromRight) => starts[fromRight];

    private static void Slow(string host, out string? suffix, out string? registrable)
    {
        suffix = GetPublicSuffix(host);
        registrable = GetRegistrableDomain(host);
    }

    /// <summary>Tells whether a label is a top-level domain with a rule of its own in the ICANN section of the list.</summary>
    /// <param name="label">The label, in Unicode or ASCII form.</param>
    /// <returns><see langword="true"/> when the ICANN section lists it.</returns>
    internal static bool IsKnownTopLevel(string label) =>
        TryNormalizeLabel(label, out _, out string ascii)
        && Rules.Value.TryGetValue(ascii, out byte mask)
        && (mask & (NormalIcann | WildcardIcann | ExceptionIcann)) != 0;

    /// <summary>Counts the labels of the public suffix of an ASCII, lowercase host.</summary>
    /// <param name="asciiLabels">The host's labels.</param>
    /// <param name="includePrivateSection">Whether the private section counts.</param>
    /// <returns>The number of labels in the public suffix (at least 1).</returns>
    internal static int CountSuffixLabels(string[] asciiLabels, bool includePrivateSection)
    {
        Dictionary<string, byte> rules = Rules.Value;
        byte normal = (byte)(NormalIcann | (includePrivateSection ? NormalPrivate : 0));
        byte wildcard = (byte)(WildcardIcann | (includePrivateSection ? WildcardPrivate : 0));
        byte exception = (byte)(ExceptionIcann | (includePrivateSection ? ExceptionPrivate : 0));

        int best = 1; // the default rule "*"
        int exceptionLabels = 0;
        for (int k = 1; k <= Math.Min(asciiLabels.Length, MaxRuleLabels.Value); k++)
        {
            string suffix = string.Join('.', asciiLabels.AsSpan(asciiLabels.Length - k).ToArray());
            if (rules.TryGetValue(suffix, out byte mask))
            {
                if ((mask & normal) != 0)
                {
                    best = Math.Max(best, k);
                }

                if ((mask & wildcard) != 0 && k < asciiLabels.Length)
                {
                    // "*.suffix" matches one more label than the suffix has.
                    best = Math.Max(best, k + 1);
                }

                if ((mask & exception) != 0)
                {
                    exceptionLabels = Math.Max(exceptionLabels, k);
                }
            }
        }

        // An exception rule prevails over every other rule; its public suffix drops the exception's leftmost label.
        return exceptionLabels > 0 ? exceptionLabels - 1 : best;
    }

    // Normalizes the labels (lowercase, UTS #46 mapped), finds the suffix length in ASCII form and returns the labels in the
    // form the caller wrote them (Unicode stays Unicode, xn-- stays xn--).
    private static bool TryAnalyze(string? domain, bool includePrivateSection, out string[] labels, out int suffixLabels)
    {
        labels = [];
        suffixLabels = 0;
        if (string.IsNullOrEmpty(domain))
        {
            return false;
        }

        // A domain name has no white space and none of the characters that make a URL, an address or a host with a port: those are not names.
        foreach (char c in domain)
        {
            if (c <= ' ' || c is '/' or '\\' or '?' or '#' or '@' or ':' or '[' or ']' or '%')
            {
                return false;
            }
        }

        string[] raw = domain.Split('.');
        var shown = new string[raw.Length];
        var ascii = new string[raw.Length];
        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i].Length == 0)
            {
                return false;
            }

            if (!TryNormalizeLabel(raw[i], out shown[i], out ascii[i]))
            {
                return false;
            }
        }

        labels = shown;
        suffixLabels = Math.Min(CountSuffixLabels(ascii, includePrivateSection), ascii.Length);
        return true;
    }

    private static bool TryNormalizeLabel(string label, out string shown, out string ascii)
    {
        bool isAscii = true;
        foreach (char c in label)
        {
            isAscii &= c < 0x80;
        }

        if (isAscii)
        {
            shown = ascii = label.ToLowerInvariant();
            return true;
        }

        // A Unicode label: map and normalize it like a host name would be, then look it up in its xn-- form.
        if (!Uts46.TryToUnicode(label, Uts46Options.None, out shown) || !Uts46.TryToAscii(label, Uts46Options.None, out ascii))
        {
            shown = ascii = label.ToLowerInvariant();
            return true;
        }

        return true;
    }

    private static Dictionary<string, byte> LoadRules()
    {
        var rules = new Dictionary<string, byte>(StringComparer.Ordinal);
        string compiled = Encoding.UTF8.GetString(Tables.GetTable("rules").Span);
        foreach (string line in compiled.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            Add(rules, line[0], line[1], line[2..]);
        }

        // Rules of ours, in the list's own syntax, in a separate file; they count as private-section rules.
        foreach (string raw in ReadAdditions().Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            char type = 'N';
            if (line.StartsWith('!'))
            {
                type = 'E';
                line = line[1..];
            }
            else if (line.StartsWith("*.", StringComparison.Ordinal))
            {
                type = 'W';
                line = line[2..];
            }

            Add(rules, type, 'P', line.ToLowerInvariant());
        }

        return rules;
    }

    private static void Add(Dictionary<string, byte> rules, char type, char section, string rule)
    {
        byte bit = (type, section) switch
        {
            ('N', 'I') => NormalIcann,
            ('N', _) => NormalPrivate,
            ('W', 'I') => WildcardIcann,
            ('W', _) => WildcardPrivate,
            (_, 'I') => ExceptionIcann,
            _ => ExceptionPrivate,
        };
        rules[rule] = (byte)(rules.GetValueOrDefault(rule) | bit);
    }

    private static string ReadAdditions()
    {
        using Stream? stream = typeof(PublicSuffixList).Assembly.GetManifestResourceStream("OneContact.Data.psl-additions.txt");
        if (stream is null)
        {
            return string.Empty;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
