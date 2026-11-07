using System.Globalization;
using System.Text;
using RecursiveAI.OneContact.Phones.Metadata;

namespace RecursiveAI.OneContact.Phones;

/// <summary>How sure the matcher must be that a piece of text is a phone number before it reports it. Each level includes the one before it.</summary>
public enum PhoneMatchLeniency
{
    /// <summary>The text parses as a number whose length is plausible for its region. Finds the most, including strings of digits that only look like numbers.</summary>
    Possible = 0,

    /// <summary>The number is valid for its region, is not glued to letters or currency signs, and carries its national prefix where the region's format needs one.</summary>
    Valid = 1,

    /// <summary>Valid, and the digits are grouped the way the region groups them, or an alternate way it is known to be written: a group may not be split, but separators may be dropped between groups.</summary>
    StrictGrouping = 2,

    /// <summary>Valid, and every group of digits is exactly as one of the region's formats writes it.</summary>
    ExactGrouping = 3,
}

/// <summary>A phone number found in a text.</summary>
/// <param name="Start">The index of the first character of the match in the text searched.</param>
/// <param name="RawString">The matched characters, exactly as they stand in the text.</param>
/// <param name="Number">The number parsed from them, with the raw text kept (see <see cref="PhoneNumber.RawInput"/>).</param>
public readonly record struct PhoneNumberMatch(int Start, string RawString, PhoneNumber Number)
{
    /// <summary>Gets the index just after the last character of the match.</summary>
    public int End => Start + RawString.Length;
}

/// <summary>
/// Finds phone numbers in running text. A candidate is a run of digits and the punctuation that can sit between them, which is then
/// parsed with the default region and kept when it meets the chosen <see cref="PhoneMatchLeniency"/>.
/// </summary>
/// <remarks>
/// Candidates that look like dates, times, page ranges or unbalanced brackets are skipped. Nothing here reads the current culture.
/// Instances are immutable and safe to share between threads; the search state lives in the caller's arguments.
/// </remarks>
public sealed class PhoneNumberMatcher
{
    private const string OpeningParens = "(\\[\uFF08\uFF3B";
    private const string ClosingParens = ")\\]\uFF09\uFF3D";
    private const string NonParens = "[^" + OpeningParens + ClosingParens + "]";
    private const string LeadClassBody = OpeningParens + "+\uFF0B";
    private const string Punctuation = "[-x\u2010-\u2015\u2212\u30FC\uFF0D-\uFF0F \u00A0\u00AD\u200B\u2060\u3000()\uFF08\uFF09\uFF3B\uFF3D.\\[\\]/~\u2053\u223C\uFF5E]{0,4}";
    private const string DigitSequence = "\\p{Nd}{1,20}";

    internal static readonly PhoneRegex Candidate = PhoneRegex.Compile(
        "(?:[" + LeadClassBody + "]" + Punctuation + "){0,2}" + DigitSequence + "(?:" + Punctuation + DigitSequence + "){0,20}(?:" + PhoneText.ExtensionForMatching + ")?",
        PhoneRegex.CaseMode.Unicode).WithVectorSkip();

    internal static readonly PhoneRegex LeadClass = PhoneRegex.Compile("[" + LeadClassBody + "]");

    internal static readonly PhoneRegex SlashSeparatedDates = PhoneRegex.Compile("(?:(?:[0-3]?\\d/[01]?\\d)|(?:[01]?\\d/[0-3]?\\d))/(?:[12]\\d)?\\d{2}");
    internal static readonly PhoneRegex TimeStamps = PhoneRegex.Compile("[12]\\d{3}[-/]?[01]\\d[-/]?[0-3]\\d +[0-2]\\d$");
    internal static readonly PhoneRegex TimeStampsSuffix = PhoneRegex.Compile(":[0-5]\\d");
    internal static readonly PhoneRegex MatchingBrackets = PhoneRegex.Compile(
        "(?:[" + OpeningParens + "])?(?:" + NonParens + "+[" + ClosingParens + "])?" + NonParens + "+(?:[" + OpeningParens + "]" + NonParens + "+[" + ClosingParens + "]){0,3}" + NonParens + "*");
    internal static readonly PhoneRegex PubPages = PhoneRegex.Compile("\\d{1,5}-+\\d{1,5}\\s{0,4}\\(\\d{1,4}");

    // When a whole candidate fails, the pieces between these separators are tried on their own, in this order.
    private static readonly PhoneRegex[] InnerMatches =
    [
        PhoneRegex.Compile("/+(.*)"),
        PhoneRegex.Compile("(\\([^(]*)"),
        PhoneRegex.Compile("(?:\\p{Z}-|-\\p{Z})\\p{Z}*(.+)"),
        PhoneRegex.Compile("[\u2012-\u2015\uFF0D]\\p{Z}*(.+)"),
        PhoneRegex.Compile("\\.+\\p{Z}*([^.]+)"),
        PhoneRegex.Compile("\\p{Z}+(\\P{Z}+)"),
    ];

    private static readonly Lazy<PhoneNumberMatcher> DefaultInstance = new(
        () => new PhoneNumberMatcher(PhoneNumberParser.Default),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<Dictionary<int, NumberFormatRule[]>> AlternateFormats = new(LoadAlternateFormats, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly PhoneNumberParser _parser;

    /// <summary>Creates a matcher over the data of a parser.</summary>
    /// <param name="parser">The engine that parses and validates candidates.</param>
    /// <exception cref="ArgumentNullException"><paramref name="parser"/> is <see langword="null"/>.</exception>
    public PhoneNumberMatcher(PhoneNumberParser parser)
    {
        ArgumentNullException.ThrowIfNull(parser);
        _parser = parser;
    }

    /// <summary>Gets the matcher over <see cref="PhoneNumberParser.Default"/>.</summary>
    public static PhoneNumberMatcher Default => DefaultInstance.Value;

    /// <summary>Finds every phone number in a text, left to right, without overlaps.</summary>
    /// <param name="text">The text.</param>
    /// <param name="defaultRegion">The region that numbers without a country code belong to; <see cref="Region.Unknown"/> finds only numbers with a country code.</param>
    /// <param name="leniency">How sure a candidate must be.</param>
    /// <param name="maxTries">The number of candidates to examine before giving up; a rejected candidate and each piece tried inside it cost one try. The default has no bound.</param>
    /// <returns>The matches, produced lazily.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxTries"/> is negative.</exception>
    public IEnumerable<PhoneNumberMatch> FindAll(string text, Region defaultRegion, PhoneMatchLeniency leniency, long maxTries = long.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(maxTries);
        return Iterate(text, defaultRegion, leniency, maxTries);
    }

    /// <summary>The low-level search: finds the next match at or after a position.</summary>
    /// <param name="text">The text.</param>
    /// <param name="defaultRegion">The region that numbers without a country code belong to.</param>
    /// <param name="leniency">How sure a candidate must be.</param>
    /// <param name="index">Where to start; on success moved to the end of the match, so the next call continues after it.</param>
    /// <param name="triesLeft">The number of candidates that may still be examined; decreased as candidates are rejected.</param>
    /// <param name="match">The match, when one is found.</param>
    /// <returns><see langword="true"/> when a match was found; <see langword="false"/> when the text or the tries are used up.</returns>
    public bool TryFindNext(ReadOnlySpan<char> text, Region defaultRegion, PhoneMatchLeniency leniency, ref int index, ref long triesLeft, out PhoneNumberMatch match)
    {
        match = default;
        if (index < 0 || index > text.Length)
        {
            return false;
        }

        while (triesLeft > 0 && Candidate.Find(text, index, default, out int start, out int end))
        {
            // A second number may follow after a slash and an x; only the first belongs here.
            ReadOnlySpan<char> candidate = text[start..end];
            if (PhoneText.SecondNumberStart.Find(candidate, 0, default, out int cut, out _))
            {
                candidate = candidate[..cut];
            }

            if (ExtractMatch(text, candidate, start, defaultRegion, leniency, ref triesLeft) is { } found)
            {
                match = found;
                index = found.End;
                return true;
            }

            index = start + candidate.Length;
            triesLeft--;
        }

        return false;
    }

    private IEnumerable<PhoneNumberMatch> Iterate(string text, Region region, PhoneMatchLeniency leniency, long tries)
    {
        int index = 0;
        while (TryFindNext(text, region, leniency, ref index, ref tries, out PhoneNumberMatch m))
        {
            yield return m;
        }
    }

    private PhoneNumberMatch? ExtractMatch(ReadOnlySpan<char> text, ReadOnlySpan<char> candidate, int offset, Region region, PhoneMatchLeniency leniency, ref long triesLeft)
    {
        // A candidate that is more likely a date or a time stamp is not a number.
        // Each pattern needs a literal character (a slash, a space, a parenthesis); a candidate without it cannot match, so skip the search.
        if (candidate.Contains('/') && SlashSeparatedDates.Find(candidate, 0, default, out _, out _))
        {
            return null;
        }

        if (candidate.Contains(' ') && TimeStamps.Find(candidate, 0, default, out _, out _) && TimeStampsSuffix.LookingAt(text[(offset + candidate.Length)..], out _))
        {
            return null;
        }

        string candidateText = candidate.ToString();
        if (ParseAndVerify(text, candidateText, offset, region, leniency) is { } whole)
        {
            return whole;
        }

        return ExtractInnerMatch(text, candidateText, offset, region, leniency, ref triesLeft);
    }

    private PhoneNumberMatch? ExtractInnerMatch(ReadOnlySpan<char> text, string candidate, int offset, Region region, PhoneMatchLeniency leniency, ref long triesLeft)
    {
        // Bounds: 2 * (1 + 1) integers, every inner pattern has one capture group.
        Span<int> groups = stackalloc int[4];
        foreach (PhoneRegex inner in InnerMatches)
        {
            bool isFirst = true;
            int from = 0;
            while (triesLeft > 0 && from <= candidate.Length && inner.Find(candidate, from, groups, out int matchStart, out int matchEnd))
            {
                int groupStart = groups[2];
                int groupEnd = groups[3];
                if (isFirst)
                {
                    // Whatever comes before the first separator is a candidate too.
                    string before = TrimAfterUnwantedEnd(candidate[..matchStart]);
                    if (ParseAndVerify(text, before, offset, region, leniency) is { } head)
                    {
                        return head;
                    }

                    triesLeft--;
                    isFirst = false;
                }

                string group = TrimAfterUnwantedEnd(candidate[groupStart..groupEnd]);
                if (ParseAndVerify(text, group, offset + groupStart, region, leniency) is { } found)
                {
                    return found;
                }

                triesLeft--;
                // A find that follows an empty match must move on by one position, as a regular-expression engine does.
                from = matchEnd > matchStart ? matchEnd : matchEnd + 1;
            }
        }

        return null;
    }

    private static string TrimAfterUnwantedEnd(string s) =>
        PhoneText.UnwantedEnd.Find(s, 0, default, out int at, out _) ? s[..at] : s;

    private PhoneNumberMatch? ParseAndVerify(ReadOnlySpan<char> text, string candidate, int offset, Region region, PhoneMatchLeniency leniency)
    {
        // Text with formatting that cannot belong to a number is not one.
        if (!MatchingBrackets.IsMatch(candidate) || (candidate.Contains('(') && PubPages.Find(candidate, 0, default, out _, out _)))
        {
            return null;
        }

        if (leniency >= PhoneMatchLeniency.Valid)
        {
            // Digits glued to letters or currency signs ("abc8005001234", "8005001234def") are part of something else.
            if (offset > 0 && !LeadClass.LookingAt(candidate, out _))
            {
                char previous = text[offset - 1];
                if (IsInvalidPunctuationSymbol(previous) || IsLatinLetter(previous))
                {
                    return null;
                }
            }

            int last = offset + candidate.Length;
            if (last < text.Length)
            {
                char next = text[last];
                if (IsInvalidPunctuationSymbol(next) || IsLatinLetter(next))
                {
                    return null;
                }
            }
        }

        if (!_parser.TryParseKeepingRaw(candidate, region, out PhoneNumber number))
        {
            return null;
        }

        return Verify(number, candidate, leniency) ? new PhoneNumberMatch(offset, candidate, number) : null;
    }

    private bool Verify(PhoneNumber number, string candidate, PhoneMatchLeniency leniency)
    {
        switch (leniency)
        {
            case PhoneMatchLeniency.Possible:
                return _parser.IsPossible(number);
            case PhoneMatchLeniency.Valid:
                return _parser.IsValid(number) && ContainsOnlyValidXChars(number, candidate) && IsNationalPrefixPresentIfRequired(number);
            default:
                if (!_parser.IsValid(number) || !ContainsOnlyValidXChars(number, candidate)
                    || ContainsMoreThanOneSlashInNationalNumber(number, candidate) || !IsNationalPrefixPresentIfRequired(number))
                {
                    return false;
                }

                return CheckNumberGroupingIsValid(number, candidate, leniency == PhoneMatchLeniency.ExactGrouping);
        }
    }

    private static bool IsInvalidPunctuationSymbol(char c) =>
        c == '%' || char.GetUnicodeCategory(c) == UnicodeCategory.CurrencySymbol;

    private static bool IsLatinLetter(char c)
    {
        UnicodeCategory cat = char.GetUnicodeCategory(c);
        bool letter = cat is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter
            or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter;
        if (!letter && cat != UnicodeCategory.NonSpacingMark)
        {
            return false;
        }

        // Basic Latin, Latin-1 Supplement, Latin Extended-A and -B, Latin Extended Additional, Combining Diacritical Marks.
        return c <= '\u024F' || (c >= '\u0300' && c <= '\u036F') || (c >= '\u1E00' && c <= '\u1EFF');
    }

    // An x in the candidate is either a carrier-code marker (two or more in a row, before the national number) or an extension
    // marker (exactly one, before the extension digits). Anything else means the text is not the number it parsed to.
    private bool ContainsOnlyValidXChars(PhoneNumber number, string candidate)
    {
        for (int index = 0; index < candidate.Length - 1; index++)
        {
            char c = candidate[index];
            if (c is 'x' or 'X')
            {
                char next = candidate[index + 1];
                if (next is 'x' or 'X')
                {
                    index++;
                    if (!IsNsnMatch(number, candidate[index..]))
                    {
                        return false;
                    }
                }
                else if (!string.Equals(PhoneText.NormalizeDigitsOnly(candidate.AsSpan(index)), number.Extension ?? "", StringComparison.Ordinal))
                {
                    return false;
                }
            }
        }

        return true;
    }

    // Whether the text, read as a number without a country code of its own, is the same national number as the given one.
    private bool IsNsnMatch(PhoneNumber first, string text)
    {
        // A text with its own plus sign is an exact comparison, which is not what a carrier code is; only the fallback to the
        // first number's region can give a national-number match.
        if (_parser.TryParse(text, Region.Unknown, false, out _, out ParseDetail detail) == System.Buffers.OperationStatus.Done)
        {
            return false;
        }

        if (detail.PrimaryReason != ReasonCode.PhoneRegionRequired && detail.PrimaryReason != ReasonCode.PhoneInvalidCountryCode)
        {
            return false;
        }

        IReadOnlyList<Region> regions = _parser.Metadata.GetRegions(first.CountryCode);
        if (regions.Count == 0 || _parser.TryParse(text, regions[0], false, out PhoneNumber second, out _) != System.Buffers.OperationStatus.Done)
        {
            return false;
        }

        return first.CountryCode == second.CountryCode && first.NationalNumber == second.NationalNumber
            && first.LeadingZeroCount == second.LeadingZeroCount && string.Equals(first.Extension, second.Extension, StringComparison.Ordinal);
    }

    private static bool ContainsMoreThanOneSlashInNationalNumber(PhoneNumber number, string candidate)
    {
        int first = candidate.IndexOf('/', StringComparison.Ordinal);
        if (first < 0)
        {
            return false;
        }

        int second = candidate.IndexOf('/', first + 1);
        if (second < 0)
        {
            return false;
        }

        // A slash straight after the country code is allowed.
        bool hasCountryCode = number.CountryCodeSource is CountryCodeSource.FromNumberWithPlusSign or CountryCodeSource.FromNumberWithoutPlusSign;
        if (hasCountryCode && string.Equals(PhoneText.NormalizeDigitsOnly(candidate.AsSpan(0, first)), number.CountryCode.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            return candidate.AsSpan(second + 1).Contains('/');
        }

        return true;
    }

    // A number typed without its country code must carry the national prefix when the region's format for it asks for one.
    private bool IsNationalPrefixPresentIfRequired(PhoneNumber number)
    {
        if (number.CountryCodeSource != CountryCodeSource.FromDefaultCountry)
        {
            return true;
        }

        RegionMetadata[]? regions = _parser.Metadata.GetByCallingCode(number.CountryCode);
        if (regions is null || regions[0].Id == RegionMetadata.NonGeographicId)
        {
            return true;
        }

        RegionMetadata md = regions[0];
        NumberFormatRule? rule = PhoneNumberParser.ChooseFormattingRule(md.NumberFormats, number.NationalSignificantNumber);
        if (rule is null || string.IsNullOrEmpty(rule.NationalPrefixFormattingRule))
        {
            return true;
        }

        if (rule.NationalPrefixOptionalWhenFormatting || FormattingRuleHasFirstGroupOnly(rule.NationalPrefixFormattingRule))
        {
            return true;
        }

        string raw = PhoneText.NormalizeDigitsOnly(number.RawInput ?? "");
        return _parser.MaybeStripNationalPrefixAndCarrierCode(ref raw, md, out _);
    }

    private static bool FormattingRuleHasFirstGroupOnly(string rule) =>
        rule is "$1" or "($1)" or "$1)" or "($1";

    private bool CheckNumberGroupingIsValid(PhoneNumber number, string candidate, bool exact)
    {
        string normalizedCandidate = NormalizeDigitsKeepingOthers(candidate);
        string[] groups = NationalNumberGroups(number);
        if (CheckGroups(number, normalizedCandidate, groups, exact))
        {
            return true;
        }

        // Some regions write numbers in more than one grouping; try the alternates the data lists.
        string nsn = number.NationalSignificantNumber;
        if (AlternateFormats.Value.TryGetValue(number.CountryCode, out NumberFormatRule[]? alternates))
        {
            foreach (NumberFormatRule alt in alternates)
            {
                if (alt.LeadingDigits.Length > 0 && !alt.LeadingDigits[0].Get().LookingAt(nsn, out _))
                {
                    continue;
                }

                groups = SplitOnNonDigits(ReplaceAll(alt, nsn));
                if (CheckGroups(number, normalizedCandidate, groups, exact))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool CheckGroups(PhoneNumber number, string normalizedCandidate, string[] formattedGroups, bool exact) =>
        exact ? AllGroupsAreExactlyPresent(number, normalizedCandidate, formattedGroups) : AllGroupsRemainGrouped(number, normalizedCandidate, formattedGroups);

    private string[] NationalNumberGroups(PhoneNumber number)
    {
        // RFC 3966 gives "+CC-group-group;ext=EXT": drop the extension, then the calling code (it ends at the first dash), and split.
        string rfc = _parser.Format(number, PhoneFormat.Rfc3966);
        int end = rfc.IndexOf(';', StringComparison.Ordinal);
        if (end < 0)
        {
            end = rfc.Length;
        }

        int start = rfc.IndexOf('-', StringComparison.Ordinal) + 1;
        return SplitOnNonDigits(rfc[start..end]);
    }

    private bool AllGroupsRemainGrouped(PhoneNumber number, string candidate, string[] groups)
    {
        int from = 0;
        if (number.CountryCodeSource != CountryCodeSource.FromDefaultCountry)
        {
            // Skip the country code, if the candidate has it.
            string cc = number.CountryCode.ToString(CultureInfo.InvariantCulture);
            from = candidate.IndexOf(cc, StringComparison.Ordinal) + cc.Length;
        }

        for (int i = 0; i < groups.Length; i++)
        {
            from = from < 0 ? -1 : candidate.IndexOf(groups[i], from, StringComparison.Ordinal);
            if (from < 0)
            {
                return false;
            }

            from += groups[i].Length;
            if (i == 0 && from < candidate.Length)
            {
                // Right after the area code: with a national prefix in the region, a digit here means no separator at all, which is
                // accepted only when the whole national number stands unseparated.
                IReadOnlyList<Region> regions = _parser.Metadata.GetRegions(number.CountryCode);
                string? ndd = regions.Count > 0 ? _parser.GetNationalPrefix(regions[0], true) : null;
                if (ndd is not null && char.IsDigit(candidate[from]))
                {
                    return candidate[(from - groups[i].Length)..].StartsWith(number.NationalSignificantNumber, StringComparison.Ordinal);
                }
            }
        }

        // The extension must not have been used up as part of the last group.
        return candidate[from..].Contains(number.Extension ?? "", StringComparison.Ordinal);
    }

    private static bool AllGroupsAreExactlyPresent(PhoneNumber number, string candidate, string[] formattedGroups)
    {
        string[] candidateGroups = SplitOnNonDigits(candidate);
        int index = number.Extension is not null ? candidateGroups.Length - 2 : candidateGroups.Length - 1;
        // The national number may stand as one block, possibly with a prefix such as the national prefix or the country code.
        if (candidateGroups.Length == 1 || (index >= 0 && candidateGroups[index].Contains(number.NationalSignificantNumber, StringComparison.Ordinal)))
        {
            return true;
        }

        // From the end, every group but the first must equal the formatted one.
        for (int g = formattedGroups.Length - 1; g > 0 && index >= 0; g--, index--)
        {
            if (!string.Equals(candidateGroups[index], formattedGroups[g], StringComparison.Ordinal))
            {
                return false;
            }
        }

        // The first group may follow a national prefix, so the candidate group only has to end with it.
        return index >= 0 && candidateGroups[index].EndsWith(formattedGroups[0], StringComparison.Ordinal);
    }

    // Digits of any script become ASCII digits; everything else stays.
    private static string NormalizeDigitsKeepingOthers(string text)
    {
        StringBuilder sb = new(text.Length);
        foreach (char c in text)
        {
            int d = CharUnicodeInfo.GetDecimalDigitValue(c);
            sb.Append(d >= 0 ? (char)('0' + d) : c);
        }

        return sb.ToString();
    }

    // Splits on runs of characters other than ASCII digits, dropping trailing empty pieces, as a regular-expression split does.
    private static string[] SplitOnNonDigits(string s)
    {
        List<string> parts = [];
        int i = 0;
        int pieceStart = 0;
        while (i < s.Length)
        {
            if (char.IsAsciiDigit(s[i]))
            {
                i++;
                continue;
            }

            int runStart = i;
            while (i < s.Length && !char.IsAsciiDigit(s[i]))
            {
                i++;
            }

            // A separator at the very start gives a leading empty piece; a separator elsewhere ends the piece before it.
            parts.Add(s[pieceStart..runStart]);
            pieceStart = i;
        }

        parts.Add(s[pieceStart..]);
        int count = parts.Count;
        while (count > 1 && parts[count - 1].Length == 0)
        {
            count--;
        }

        if (count == 1 && parts[0].Length == 0 && s.Length == 0)
        {
            return [""];
        }

        return [.. parts.Take(count)];
    }

    // An alternate format is applied wherever its pattern matches inside the national number, not only when it matches all of it, so a
    // pattern that covers the first nine digits of a ten-digit number still groups those and leaves the rest as it is.
    private static string ReplaceAll(NumberFormatRule rule, string nsn)
    {
        PhoneRegex pattern = rule.Pattern.Get();
        // Bounds: 2 * (32 + 1) integers, the most capture groups a pattern may have.
        Span<int> groups = stackalloc int[2 * (pattern.GroupCount + 1)];
        StringBuilder sb = new();
        int position = 0;
        while (position <= nsn.Length && pattern.Find(nsn, position, groups, out int start, out int end))
        {
            sb.Append(nsn, position, start - position);
            PhoneNumberParser.ExpandReplacement(rule.Format, nsn, groups, pattern.GroupCount, sb);
            if (end == start)
            {
                if (start < nsn.Length)
                {
                    sb.Append(nsn[start]);
                }

                position = start + 1;
            }
            else
            {
                position = end;
            }
        }

        if (position < nsn.Length)
        {
            sb.Append(nsn, position, nsn.Length - position);
        }

        return sb.ToString();
    }

    private static Dictionary<int, NumberFormatRule[]> LoadAlternateFormats()
    {
        Dictionary<int, NumberFormatRule[]> map = [];
        foreach (TerritoryData t in PhoneMetadata.ReadEmbedded("alt"))
        {
            List<NumberFormatRule> rules = [];
            foreach (FormatData f in t.Formats)
            {
                rules.Add(new NumberFormatRule(f.Pattern, f.Format, f.LeadingDigits, null, null, false));
            }

            map[t.CountryCode] = [.. rules];
        }

        return map;
    }
}
