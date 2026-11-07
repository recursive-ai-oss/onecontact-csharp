using RecursiveAI.OneContact.Phones;

namespace RecursiveAI.OneContact.Extraction.Text;

/// <summary>A stretch of text that may be a telephone number.</summary>
/// <param name="Start">Index of the first character.</param>
/// <param name="End">Index past the last character (an extension is included).</param>
/// <param name="Label">The word such as "Tel" or "Fax" that stands in front of it, as written; null when there is none.</param>
internal readonly record struct PhoneHit(int Start, int End, string? Label)
{
    /// <summary>True when a label stands in front of the number.</summary>
    public bool Labelled => Label is not null;
}

/// <summary>
/// Finds the stretches of a line that may be phone numbers, for the core parser to accept or refuse. This is the seam where a
/// matcher with libphonenumber's leniency levels can replace the shape rules below.
/// </summary>
internal interface IPhoneCandidateFinder
{
    /// <summary>Appends the candidates of <c>text[start..end]</c>, a line without line breaks, in order and without overlap.</summary>
    void Find(string text, int start, int end, List<PhoneHit> sink);
}

/// <summary>
/// A candidate is a run of digits with the separators people write between groups (space, hyphen, dot, slash, parentheses), 7 to 15
/// digits long, optionally followed by an extension. It is kept when it begins with a plus sign, is written in groups, or has a label
/// before it; dates, IP addresses, amounts and longer digit runs are not candidates. Whether the run is a number is for the core
/// parser to say: unlabelled candidates that it does not accept as valid are dropped by the caller.
/// </summary>
internal sealed class ShapePhoneFinder : IPhoneCandidateFinder
{
    private static readonly string[] Labels =
    [
        "tel", "tel.", "telephone", "phone", "ph", "mobile", "mob", "cell", "fax", "call", "whatsapp", "direct", "office", "main",
        "t", "m", "p", "f", "o", "c", "w", "h",
        "t\u00e9l", "t\u00e9l.", "t\u00e9l\u00e9phone", "telefon", "telefono", "tel\u00e9fono", "telefone", "tlf", "tlf.", "puh", "puh.", "mobil", "handy", "portable",
        "telefoon", "gsm", "fon", "numero", "n\u00famero", "\u260e", "\u2706", "\U0001F4DE",
    ];

    /// <summary>The single instance: the finder keeps no state.</summary>
    public static readonly ShapePhoneFinder Instance = new();

    public void Find(string text, int start, int end, List<PhoneHit> sink)
    {
        int p = start;
        while (p < end)
        {
            char c = text[p];
            bool opens = char.IsAsciiDigit(c) || c == '+' || (c == '(' && p + 1 < end && char.IsAsciiDigit(text[p + 1]));
            if (!opens || (p > start && IsGlued(text[p - 1])))
            {
                p++;
                continue;
            }

            if (TryRun(text, p, end, out int runEnd, out bool grouped, out bool plus, out int digits, out DigitGroups groups))
            {
                string? label = FindLabel(text, start, p);
                bool labelled = label is not null;
                bool dateLike = groups.IsDateLike();
                if (digits is >= 7 and <= 15 && !dateLike && !IsMoneyBefore(text, start, p)
                    && (plus || grouped || labelled))
                {
                    sink.Add(new PhoneHit(p, runEnd, label));
                    p = runEnd;
                    continue;
                }

                p = Math.Max(runEnd, p + 1);
                continue;
            }

            p++;
        }
    }

    // A digit that follows a letter, a digit or an underscore is the middle of something else (a version, an identifier).
    private static bool IsGlued(char before) => char.IsLetterOrDigit(before) || before == '_';

    private static bool IsMoneyBefore(string text, int start, int p)
    {
        int i = p - 1;
        while (i >= start && text[i] == ' ')
        {
            i--;
        }

        return i >= start && text[i] is '$' or '\u20AC' or '\u00A3' or '\u00A5' or '#';
    }

    private static bool IsSeparator(char c) => c is ' ' or '-' or '.' or '/' or '(' or ')' or '\u00A0' or '\u2010' or '\u2011' or '\u2012' or '\u2013' or '\u2212';

    private static bool TryRun(string text, int p, int end, out int runEnd, out bool grouped, out bool plus, out int digits, out DigitGroups groups)
    {
        plus = text[p] == '+';
        int i = p + (plus ? 1 : 0);
        digits = 0;
        groups = default;
        int separators = 0;
        bool parens = false;
        int lastDigit = p;
        int group = 0;
        while (i < end && digits <= 17)
        {
            char c = text[i];
            if (char.IsAsciiDigit(c))
            {
                digits++;
                group++;
                lastDigit = i;
                i++;
                continue;
            }

            if (!IsSeparator(c))
            {
                break;
            }

            // Up to two separator characters sit between groups (") ", " - ", ". "), and a digit must follow them.
            int j = i;
            bool paren = false;
            while (j < end && IsSeparator(text[j]) && j - i < 3)
            {
                paren |= text[j] is '(' or ')';
                j++;
            }

            if (j >= end || !char.IsAsciiDigit(text[j]) || j - i > 2)
            {
                break;
            }

            if (group > 0)
            {
                groups.Add(group);
                group = 0;
            }

            parens |= paren;
            separators++;
            i = j;
        }

        if (group > 0)
        {
            groups.Add(group);
        }

        // The number ends at its last digit; a closing parenthesis right after it belongs to it only when one was opened.
        runEnd = lastDigit + 1;
        if (runEnd < end && text[runEnd] == ')' && text.AsSpan(p, runEnd - p).Contains('('))
        {
            runEnd++;
        }

        // An extension: "ext. 123", "x123", "extension 4".
        int e = runEnd;
        while (e < end && text[e] == ' ')
        {
            e++;
        }

        if (e < end && (text[e] is 'x' or 'X' || (e + 2 < end && string.Compare(text, e, "ext", 0, 3, StringComparison.OrdinalIgnoreCase) == 0)))
        {
            e += text[e] is 'x' or 'X' ? 1 : 3;
            while (e < end && text[e] is '.' or ':' or ' ' or '#')
            {
                e++;
            }

            int ds = e;
            while (e < end && char.IsAsciiDigit(text[e]) && e - ds < 6)
            {
                e++;
            }

            if (e > ds && (e >= end || !char.IsLetterOrDigit(text[e])))
            {
                runEnd = e;
            }
        }

        grouped = parens || separators >= 2;
        return digits > 0;
    }

    // The sizes of the first four digit groups of a run and how many there were: enough to tell dates and IP addresses, without a list.
    private struct DigitGroups
    {
        private int _a;
        private int _b;
        private int _c;
        private int _d;

        public int Count { get; private set; }

        public void Add(int size)
        {
            switch (Count)
            {
                case 0: _a = size; break;
                case 1: _b = size; break;
                case 2: _c = size; break;
                case 3: _d = size; break;
            }

            Count++;
        }

        // Digit groups 4-2-2, 2-2-4 and 1or2-1or2-4 are dates; four groups of up to three digits are an IPv4 address.
        public readonly bool IsDateLike()
        {
            if (Count == 3)
            {
                bool yearFirst = _a == 4 && _b <= 2 && _c <= 2;
                bool yearLast = _c == 4 && _a <= 2 && _b <= 2;
                return yearFirst || yearLast;
            }

            return Count == 4 && _a <= 3 && _b <= 3 && _c <= 3 && _d <= 3;
        }
    }

    internal static string? FindLabel(string text, int start, int p)
    {
        int i = p - 1;
        while (i >= start && text[i] is ' ' or ':' or '\u00A0' or '-' or '\u2013')
        {
            i--;
        }

        // "(Tel)" or "[Tel]": step over a closing bracket.
        if (i >= start && text[i] is ')' or ']')
        {
            i--;
        }

        int endWord = i + 1;
        while (i >= start && (char.IsLetter(text[i]) || text[i] == '.' || char.GetUnicodeCategory(text[i]) is System.Globalization.UnicodeCategory.OtherSymbol or System.Globalization.UnicodeCategory.Surrogate))
        {
            i--;
        }

        int wordStart = i + 1;
        if (endWord <= wordStart)
        {
            return null;
        }

        ReadOnlySpan<char> word = text.AsSpan(wordStart, endWord - wordStart);
        bool colon = p - endWord > 0 && text.AsSpan(endWord, p - endWord).Contains(':');
        foreach (string label in Labels)
        {
            if (word.Equals(label, StringComparison.OrdinalIgnoreCase))
            {
                // A single letter is a label only with a colon: "T: 020..." and not "a 020...".
                return label.Length > 2 || colon ? word.ToString() : null;
            }
        }

        return null;
    }
}

/// <summary>
/// Finds phone candidates with the core matcher when the region of the page is known: the matcher accepts only numbers that are
/// valid for that region (or carry a country code), so grouping and date-like runs need no rules of ours. Runs that the matcher
/// refuses but the shape rules keep (labelled numbers of another region, for example) are added after it, so nothing found by the
/// shape rules alone is lost. At most 1000 matches are taken from a line.
/// </summary>
internal sealed class MatcherPhoneFinder : IPhoneCandidateFinder
{
    private const int MaxMatchesPerLine = 1_000;

    private readonly Region _region;

    // One finder serves one extraction on one thread, so its work lists are kept between lines.
    private readonly List<PhoneHit> _shape = [];
    private readonly List<PhoneHit> _merged = [];

    /// <summary>Creates a finder for numbers written without a country code in <paramref name="region"/>.</summary>
    /// <param name="region">The region; it must be known.</param>
    public MatcherPhoneFinder(Region region) => _region = region;

    // A number needs a digit: ASCII, another script's decimal digit, or a surrogate (digits outside the Basic Multilingual Plane).
    private static bool MayHoldNumber(ReadOnlySpan<char> line)
    {
        foreach (char c in line)
        {
            if (c is >= '0' and <= '9' || (c > 0x7F && (char.IsDigit(c) || char.IsSurrogate(c))))
            {
                return true;
            }
        }

        return false;
    }

    public void Find(string text, int start, int end, List<PhoneHit> sink)
    {
        ReadOnlySpan<char> line = text.AsSpan(start, end - start);
        if (!MayHoldNumber(line))
        {
            return;
        }

        List<PhoneHit> shape = _shape;
        List<PhoneHit> merged = _merged;
        shape.Clear();
        merged.Clear();
        ShapePhoneFinder.Instance.Find(text, start, end, shape);

        int index = 0;
        long tries = long.MaxValue;
        while (PhoneNumberMatcher.Default.TryFindNext(line, _region, PhoneMatchLeniency.Valid, ref index, ref tries, out PhoneNumberMatch m))
        {
            int s = start + m.Start;
            merged.Add(new PhoneHit(s, s + m.RawString.Length, ShapePhoneFinder.FindLabel(text, start, s)));
            if (merged.Count >= MaxMatchesPerLine)
            {
                break;
            }
        }

        foreach (PhoneHit h in shape)
        {
            bool overlaps = false;
            foreach (PhoneHit m in merged)
            {
                if (h.Start < m.End && m.Start < h.End)
                {
                    overlaps = true;
                    break;
                }
            }

            if (!overlaps)
            {
                merged.Add(h);
            }
        }

        if (merged.Count > 1)
        {
            merged.Sort((a, b) => a.Start.CompareTo(b.Start));
        }

        sink.AddRange(merged);
    }
}
