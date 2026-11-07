using System.Buffers;
using System.Globalization;
using System.Text;
using RecursiveAI.OneContact.Phones.Metadata;

namespace RecursiveAI.OneContact.Phones;

/// <summary>
/// Finds the stretches of a text that may be phone numbers: the same language as <see cref="PhoneNumberMatcher.Candidate"/>, written as a
/// scan because the matcher asks for it on every digit run of a long text.
/// </summary>
/// <remarks>
/// A candidate is up to two leading characters (an opening bracket or a plus sign, each followed by up to four punctuation characters), a
/// run of decimal digits, then up to twenty groups of up to four punctuation characters and more digits, and last an extension if one
/// follows. Digits and punctuation never overlap, so a greedy scan reaches the same end as the pattern's backtracking would; the pattern
/// stays as the reference and a test compares the two on random text.
/// </remarks>
internal static class PhoneCandidateScanner
{
    // Every ASCII character that cannot begin a candidate: all but the opening bracket, the plus sign and the digits. Anything outside
    // ASCII may begin one (a full-width bracket or plus sign, any decimal digit), so the vectorized skip stops there.
    private static readonly SearchValues<char> NonStart = CreateNonStart();

    private static readonly PhoneRegex Extension = PhoneRegex.Compile(PhoneText.ExtensionForMatching, PhoneRegex.CaseMode.Unicode);

    /// <summary>Finds the leftmost candidate at or after a position.</summary>
    /// <param name="text">The text.</param>
    /// <param name="from">Where to start looking.</param>
    /// <param name="start">The start of the candidate.</param>
    /// <param name="end">The end of the candidate.</param>
    /// <returns><see langword="true"/> when there is one.</returns>
    internal static bool Find(ReadOnlySpan<char> text, int from, out int start, out int end)
    {
        int pos = from;
        while (pos < text.Length)
        {
            if (text[pos] < 128)
            {
                int next = text[pos..].IndexOfAnyExcept(NonStart);
                if (next < 0)
                {
                    break;
                }

                pos += next;
            }

            int mainEnd = MatchMain(text, pos);
            if (mainEnd >= 0)
            {
                start = pos;
                end = Extension.LookingAt(text[mainEnd..], out int extensionLength) ? mainEnd + extensionLength : mainEnd;
                return true;
            }

            pos += char.IsHighSurrogate(text[pos]) && pos + 1 < text.Length && char.IsLowSurrogate(text[pos + 1]) ? 2 : 1;
        }

        start = -1;
        end = -1;
        return false;
    }

    // The end of the number part (leading characters, digits and digit groups) that begins at pos, or -1 when none does.
    private static int MatchMain(ReadOnlySpan<char> text, int pos)
    {
        int at = Lead(text, pos, 2);
        if (at < 0)
        {
            return -1;
        }

        at = SkipDigits(text, at, 20);
        for (int group = 0; group < 20; group++)
        {
            int q = at;
            int run = 0;
            while (run < 5 && q < text.Length && IsPunctuation(text[q]))
            {
                q++;
                run++;
            }

            if (run > 4 || DigitLength(text, q) == 0)
            {
                break;
            }

            at = SkipDigits(text, q, 20);
        }

        return at;
    }

    // Where the digits begin after up to `remaining` leading characters, trying more leading characters first and a longer punctuation run
    // after each first, as the pattern's greedy repeats do; -1 when no digit is reached.
    private static int Lead(ReadOnlySpan<char> text, int pos, int remaining)
    {
        if (remaining > 0 && pos < text.Length && IsLead(text[pos]))
        {
            int q = pos + 1;
            int run = 0;
            while (run < 4 && q + run < text.Length && IsPunctuation(text[q + run]))
            {
                run++;
            }

            for (int k = run; k >= 0; k--)
            {
                int found = Lead(text, q + k, remaining - 1);
                if (found >= 0)
                {
                    return found;
                }
            }
        }

        return DigitLength(text, pos) > 0 ? pos : -1;
    }

    private static int SkipDigits(ReadOnlySpan<char> text, int at, int max)
    {
        for (int i = 0; i < max; i++)
        {
            int length = DigitLength(text, at);
            if (length == 0)
            {
                break;
            }

            at += length;
        }

        return at;
    }

    // The length in UTF-16 units of the decimal digit (Unicode category Nd) at the position, 0 when it is not one.
    private static int DigitLength(ReadOnlySpan<char> text, int i)
    {
        if (i >= text.Length)
        {
            return 0;
        }

        char c = text[i];
        if (c < 128)
        {
            return c is >= '0' and <= '9' ? 1 : 0;
        }

        if (char.IsHighSurrogate(c))
        {
            return i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])
                && Rune.GetUnicodeCategory(new Rune(c, text[i + 1])) == UnicodeCategory.DecimalDigitNumber ? 2 : 0;
        }

        return char.GetUnicodeCategory(c) == UnicodeCategory.DecimalDigitNumber ? 1 : 0;
    }

    private static bool IsLead(char c) => c is '(' or '[' or '+' or '\uFF08' or '\uFF3B' or '\uFF0B';

    // The characters that may sit between digits (the pattern's punctuation class, which matches x in either case).
    private static bool IsPunctuation(char c) =>
        c is '-' or 'x' or 'X' or (>= '\u2010' and <= '\u2015') or '\u2212' or '\u30FC' or (>= '\uFF0D' and <= '\uFF0F') or ' ' or '\u00A0' or '\u00AD'
            or '\u200B' or '\u2060' or '\u3000' or '(' or ')' or '\uFF08' or '\uFF09' or '\uFF3B' or '\uFF3D' or '.' or '[' or ']' or '/' or '~'
            or '\u2053' or '\u223C' or '\uFF5E';

    private static SearchValues<char> CreateNonStart()
    {
        var chars = new List<char>();
        for (char c = '\0'; c < 128; c++)
        {
            if (c is not ('(' or '[' or '+') && c is not (>= '0' and <= '9'))
            {
                chars.Add(c);
            }
        }

        return SearchValues.Create(chars.ToArray());
    }
}
