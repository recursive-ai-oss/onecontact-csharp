using System.Globalization;
using System.Text;
using RecursiveAI.OneContact.Phones.Metadata;

namespace RecursiveAI.OneContact.Phones;

/// <summary>
/// The text-level rules of phone number parsing: which characters can start or end a number, what counts as a viable number, how
/// letters and other scripts' digits are normalized, and how an extension is recognised. The patterns are written out here in the
/// same regular-expression subset the metadata uses, so they read like the rules they implement.
/// </summary>
internal static class PhoneText
{
    internal const int MinLengthForNsn = 2;
    internal const int MaxLengthForNsn = 17;
    internal const int MaxLengthCountryCode = 3;
    internal const int MaxInputLength = 250;

    // Characters that may punctuate a number: dashes, spaces, brackets, slashes, dots, tildes, the letter x and their wide forms.
    private const string ValidPunctuationClass = "-x\u2010-\u2015\u2212\u30FC\uFF0D-\uFF0F \u00A0\u00AD\u200B\u2060\u3000()\uFF08\uFF09\uFF3B\uFF3D.\\[\\]/~\u2053\u223C\uFF5E";
    private const string PlusChars = "+\uFF0B";
    private const string Digit = "\\p{Nd}";

    private const string PossibleSeparatorsBetweenNumberAndExtLabel = "[ \u00A0\\t,]*";
    private const string PossibleSeparatorsNumberExtLabelNoComma = "[ \u00A0\\t]*";
    private const string PossibleCharsAfterExtLabel = "[:\\.\uFF0E]?[ \u00A0\\t,-]*";
    private const string OptionalExtnSuffix = "#?";
    private const string ExplicitExtLabels = "(?:e?xt(?:ensi(?:o\u0301?|\u00F3))?n?|\uFF45?\uFF58\uFF54\uFF4E?|\u0434\u043E\u0431|anexo)";
    private const string AmbiguousExtLabels = "(?:[x\uFF58#\uFF03~\uFF5E]|int|\uFF49\uFF4E\uFF54)";
    private const string AmbiguousSeparator = "[- ]+";
    private const string AutoDiallingAndExtLabelsFound = "(?:,{2}|;)";

    private static readonly string ExtensionPatternsForParsing = BuildExtensionPatterns();

    private static PhoneRegex? s_validStart;
    private static PhoneRegex? s_unwantedEnd;
    private static PhoneRegex? s_secondNumberStart;
    private static PhoneRegex? s_plusChars;
    private static PhoneRegex? s_validAlpha;
    private static PhoneRegex? s_validPhoneNumber;
    private static PhoneRegex? s_extension;
    private static PhoneRegex? s_globalNumberDigits;
    private static PhoneRegex? s_domainName;
    private static PhoneRegex? s_uniqueInternationalPrefix;
    private static PhoneRegex? s_separator;
    private static PhoneRegex? s_capturingDigit;

    private static string ExtnDigits(int max) => "(" + Digit + "{1," + max.ToString(CultureInfo.InvariantCulture) + "})";

    private static string BuildExtensionPatterns()
    {
        string rfcExtn = ";ext=" + ExtnDigits(20);
        string explicitExtn = PossibleSeparatorsBetweenNumberAndExtLabel + ExplicitExtLabels + PossibleCharsAfterExtLabel + ExtnDigits(20) + OptionalExtnSuffix;
        string ambiguousExtn = PossibleSeparatorsBetweenNumberAndExtLabel + AmbiguousExtLabels + PossibleCharsAfterExtLabel + ExtnDigits(9) + OptionalExtnSuffix;
        string americanStyleExtnWithSuffix = AmbiguousSeparator + ExtnDigits(6) + "#";
        string autoDiallingExtn = PossibleSeparatorsNumberExtLabelNoComma + AutoDiallingAndExtLabelsFound + PossibleCharsAfterExtLabel + ExtnDigits(15) + OptionalExtnSuffix;
        string onlyCommasExtn = PossibleSeparatorsNumberExtLabelNoComma + "(?:,)+" + PossibleCharsAfterExtLabel + ExtnDigits(9) + OptionalExtnSuffix;
        return rfcExtn + "|" + explicitExtn + "|" + ambiguousExtn + "|" + americanStyleExtnWithSuffix + "|" + autoDiallingExtn + "|" + onlyCommasExtn;
    }

    internal static PhoneRegex ValidStart => s_validStart ??= PhoneRegex.Compile("[" + PlusChars + Digit + "]");

    // Trailing characters that are neither numbers nor letters (nor '#').
    internal static PhoneRegex UnwantedEnd => s_unwantedEnd ??= PhoneRegex.Compile("[^\\p{N}\\p{L}#]+$");

    // A second number after a slash and an x.
    internal static PhoneRegex SecondNumberStart => s_secondNumberStart ??= PhoneRegex.Compile("[\\\\/] *x");

    internal static PhoneRegex PlusCharsPattern => s_plusChars ??= PhoneRegex.Compile("[" + PlusChars + "]+");

    // Three or more letters anywhere makes a vanity number.
    internal static PhoneRegex ValidAlpha => s_validAlpha ??= PhoneRegex.Compile("(?:.*?[A-Za-z]){3}.*", PhoneRegex.CaseMode.Ascii);

    internal static PhoneRegex ValidPhoneNumber => s_validPhoneNumber ??= PhoneRegex.Compile(
        Digit + "{2}|[" + PlusChars + "]*(?:[" + ValidPunctuationClass + "*]*" + Digit + "){3,}[" + ValidPunctuationClass + "*A-Za-z" + Digit + "]*(?:" + ExtensionPatternsForParsing + ")?",
        PhoneRegex.CaseMode.Unicode);

    internal static PhoneRegex Extension => s_extension ??= PhoneRegex.Compile("(?:" + ExtensionPatternsForParsing + ")$", PhoneRegex.CaseMode.Unicode);

    internal static PhoneRegex GlobalNumberDigits => s_globalNumberDigits ??= PhoneRegex.Compile(
        "^\\+(?:[\\-\\.\\(\\)]|" + Digit + ")*" + Digit + "(?:[\\-\\.\\(\\)]|" + Digit + ")*$");

    internal static PhoneRegex DomainName => s_domainName ??= PhoneRegex.Compile(
        "^([a-zA-Z0-9][a-zA-Z0-9\\-]*\\.)*[a-zA-Z][a-zA-Z0-9\\-]*\\.?$");

    internal static PhoneRegex UniqueInternationalPrefix => s_uniqueInternationalPrefix ??= PhoneRegex.Compile("[\\d]+(?:[~\u2053\u223C\uFF5E][\\d]+)?");

    internal static PhoneRegex Separator => s_separator ??= PhoneRegex.Compile("[" + ValidPunctuationClass + "]+");

    internal static PhoneRegex CapturingDigit => s_capturingDigit ??= PhoneRegex.Compile("(" + Digit + ")");

    /// <summary>The digit value of a character, or -1 when it is not a decimal digit.</summary>
    internal static int DigitValue(char c)
    {
        if (c < 128)
        {
            return c is >= '0' and <= '9' ? c - '0' : -1;
        }

        return CharUnicodeInfo.GetDecimalDigitValue(c);
    }

    /// <summary>Keeps only the digits of a text, turning every script's decimal digits into ASCII digits.</summary>
    internal static string NormalizeDigitsOnly(ReadOnlySpan<char> text)
    {
        StringBuilder sb = new(text.Length);
        foreach (char c in text)
        {
            int v = DigitValue(c);
            if (v >= 0)
            {
                sb.Append((char)('0' + v));
            }
        }

        return sb.ToString();
    }

    /// <summary>Keeps the characters you could dial: ASCII digits, <c>+</c>, <c>*</c> and <c>#</c>.</summary>
    internal static string NormalizeDiallableCharsOnly(ReadOnlySpan<char> text)
    {
        StringBuilder sb = new(text.Length);
        foreach (char c in text)
        {
            int v = MappedDigit(c);
            if (v >= 0)
            {
                sb.Append((char)('0' + v));
            }
            else if (c is '+' or '*' or '#')
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    // Only ASCII digits pass through the letter-to-digit and diallable-character mappings; digits of other scripts are dropped there
    // (they are converted only by NormalizeDigitsOnly).
    private static int MappedDigit(char c) => c is >= '0' and <= '9' ? c - '0' : -1;

    /// <summary>Maps a vanity letter to its keypad digit (ITU E.161), or -1.</summary>
    internal static int LetterDigit(char c)
    {
        // Letters are looked up by their upper-case form, so the dotless i and the long s count as I and S.
        int u = PhoneRegex.ToUpper(c);
        return u switch
        {
            'A' or 'B' or 'C' => 2,
            'D' or 'E' or 'F' => 3,
            'G' or 'H' or 'I' => 4,
            'J' or 'K' or 'L' => 5,
            'M' or 'N' or 'O' => 6,
            'P' or 'Q' or 'R' or 'S' => 7,
            'T' or 'U' or 'V' => 8,
            'W' or 'X' or 'Y' or 'Z' => 9,
            _ => -1,
        };
    }

    /// <summary>Normalizes a number for parsing: a number with three or more letters is a vanity number and its letters become digits; otherwise only digits are kept.</summary>
    internal static string Normalize(ReadOnlySpan<char> text)
    {
        if (ValidAlpha.IsMatch(text))
        {
            StringBuilder sb = new(text.Length);
            foreach (char c in text)
            {
                int v = MappedDigit(c);
                if (v < 0)
                {
                    v = LetterDigit(c);
                }

                if (v >= 0)
                {
                    sb.Append((char)('0' + v));
                }
            }

            return sb.ToString();
        }

        return NormalizeDigitsOnly(text);
    }

    /// <summary>Whether the text has the shape of a phone number at all: two digits, or at least three digits with only punctuation, letters and an extension around them.</summary>
    internal static bool IsViable(ReadOnlySpan<char> number) =>
        number.Length >= MinLengthForNsn && ValidPhoneNumber.IsMatch(number);

    /// <summary>
    /// Finds the part of a text that can be a phone number: it starts at the first plus sign or digit, loses trailing characters that
    /// are neither letters nor numbers (except <c>#</c>), and is cut at a second number introduced by a slash and an x.
    /// </summary>
    internal static ReadOnlySpan<char> ExtractPossibleNumber(ReadOnlySpan<char> text, out int start)
    {
        if (!ValidStart.Find(text, 0, default, out int s, out _))
        {
            start = 0;
            return default;
        }

        start = s;
        ReadOnlySpan<char> number = text[s..];
        if (UnwantedEnd.Find(number, 0, default, out int trailing, out _))
        {
            number = number[..trailing];
        }

        if (SecondNumberStart.Find(number, 0, default, out int second, out _))
        {
            number = number[..second];
        }

        return number;
    }

    /// <summary>
    /// Removes a trailing extension from <paramref name="number"/> when the text before it is still a viable number.
    /// </summary>
    /// <param name="number">The number text.</param>
    /// <param name="cut">Where the extension started, or the length of <paramref name="number"/> when there is none.</param>
    /// <returns>The extension digits as written, or an empty string.</returns>
    internal static string StripExtension(ReadOnlySpan<char> number, out int cut)
    {
        cut = number.Length;
        // Bounds: 2 * (GroupCount + 1) with at most 6 extension groups.
        Span<int> groups = stackalloc int[2 * (Extension.GroupCount + 1)];
        if (Extension.Find(number, 0, groups, out int start, out _) && IsViable(number[..start]))
        {
            for (int g = 1; g <= Extension.GroupCount; g++)
            {
                if (groups[2 * g] >= 0)
                {
                    cut = start;
                    return number[groups[2 * g]..groups[(2 * g) + 1]].ToString();
                }
            }
        }

        return "";
    }
}
