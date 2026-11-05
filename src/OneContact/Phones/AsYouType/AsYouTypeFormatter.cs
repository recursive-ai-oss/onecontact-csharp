using System.Globalization;
using System.Text;
using RecursiveAI.OneContact.Phones.Metadata;

namespace RecursiveAI.OneContact.Phones;

/// <summary>
/// Formats a phone number while it is being typed: after every key press it gives the text to show so far, grouped the way the
/// region writes numbers, as far as the digits entered allow a guess.
/// </summary>
/// <remarks>
/// <para>
/// One instance follows one number. Call <see cref="InputDigit"/> for each character the user types (digits, a leading plus sign,
/// and anything else, which stops formatting), show what it returns, and call <see cref="Clear"/> to start another number. An
/// instance is not safe for use from several threads at once.
/// </para>
/// <para>
/// The output depends only on the characters typed and the numbering-plan data. Typing a national number formats it for the
/// default region; typing a plus sign or the region's international prefix switches to international grouping once the calling
/// code is known. Characters other than digits make the formatter give up and echo the input untouched until
/// <see cref="Clear"/>. The formatter never validates: it groups digits, and a number may be shown formatted although it is not valid.
/// </para>
/// </remarks>
public sealed class AsYouTypeFormatter
{
    // The placeholder written into a template where a digit has not been typed yet.
    private const char DigitPlaceholder = '\u2008';
    private const char SeparatorBeforeNationalNumber = ' ';
    private const int MinLeadingDigitsLength = 3;

    // Formats may only be used for typing when they consist of digit groups, $1 to $9, with punctuation between them.
    private static readonly PhoneRegex EligibleFormat = PhoneRegex.Compile("[-x\u2010-\u2015\u2212\u30FC\uFF0D-\uFF0F \u00A0\u00AD\u200B\u2060\u3000()\uFF08\uFF09\uFF3B\uFF3D.\\[\\]/~\u2053\u223C\uFF5E]*(\\$\\d[-x\u2010-\u2015\u2212\u30FC\uFF0D-\uFF0F \u00A0\u00AD\u200B\u2060\u3000()\uFF08\uFF09\uFF3B\uFF3D.\\[\\]/~\u2053\u223C\uFF5E]*)+");
    private static readonly PhoneRegex FirstGroupOnly = PhoneRegex.Compile("\\(?\\$1\\)?");
    private static readonly PhoneRegex NationalPrefixSeparators = PhoneRegex.Compile("[- ]");

    private readonly PhoneNumberParser _parser;
    private readonly Region _defaultRegion;
    private readonly RegionMetadata? _defaultMetadata;
    private readonly StringBuilder _accrued = new();
    private readonly StringBuilder _accruedDigits = new();
    private readonly StringBuilder _template = new();
    private readonly StringBuilder _prefixBeforeNationalNumber = new();
    private readonly StringBuilder _nationalNumber = new();
    private readonly List<NumberFormatRule> _possibleFormats = [];
    private RegionMetadata? _currentMetadata;
    private string _currentOutput = "";
    private string _extractedNationalPrefix = "";
    private string? _currentFormattingPattern;
    private bool _ableToFormat = true;
    private bool _inputHasFormatting;
    private bool _isCompleteNumber;
    private bool _isExpectingCountryCode;
    private bool _shouldAddSpaceAfterNationalPrefix;
    private bool _nationalPrefixTaken;
    private int _lastMatchPosition;
    private int _originalPosition;
    private int _positionToRemember;

    /// <summary>Creates a formatter that treats typed national numbers as belonging to a region, using the library's own numbering-plan data.</summary>
    /// <param name="defaultRegion">The region national numbers are typed in. A region with no data formats nothing but a number that starts with a plus sign.</param>
    public AsYouTypeFormatter(Region defaultRegion)
        : this(PhoneNumberParser.Default, defaultRegion)
    {
    }

    /// <summary>Creates a formatter over the data of a given parser.</summary>
    /// <param name="parser">The engine whose <see cref="PhoneNumberParser.Metadata"/> supplies the numbering plans.</param>
    /// <param name="defaultRegion">The region national numbers are typed in.</param>
    /// <exception cref="ArgumentNullException"><paramref name="parser"/> is <see langword="null"/>.</exception>
    public AsYouTypeFormatter(PhoneNumberParser parser, Region defaultRegion)
    {
        ArgumentNullException.ThrowIfNull(parser);
        _parser = parser;
        _defaultRegion = defaultRegion;
        _currentMetadata = MetadataFor(defaultRegion);
        _defaultMetadata = _currentMetadata;
    }

    /// <summary>
    /// Gets the position in the last returned text that corresponds to the character given to
    /// <see cref="InputDigitAndRememberPosition"/>. A digit typed at the cursor can be found again in the formatted text this way.
    /// </summary>
    /// <value>An index into the last output, counted in UTF-16 units; 0 before anything is remembered. When formatting has stopped, the position of the character in the echoed input.</value>
    public int RememberedPosition
    {
        get
        {
            if (!_ableToFormat)
            {
                return _originalPosition;
            }

            int inputIndex = 0;
            int outputIndex = 0;
            while (inputIndex < _positionToRemember && outputIndex < _currentOutput.Length)
            {
                if (_accruedDigits[inputIndex] == _currentOutput[outputIndex])
                {
                    inputIndex++;
                }

                outputIndex++;
            }

            return outputIndex;
        }
    }

    /// <summary>Forgets everything typed so far and starts a new number for the same default region.</summary>
    public void Clear()
    {
        _currentOutput = "";
        _accrued.Clear();
        _accruedDigits.Clear();
        _template.Clear();
        _lastMatchPosition = 0;
        _currentFormattingPattern = null;
        _prefixBeforeNationalNumber.Clear();
        _extractedNationalPrefix = "";
        _nationalPrefixTaken = false;
        _nationalNumber.Clear();
        _ableToFormat = true;
        _inputHasFormatting = false;
        _positionToRemember = 0;
        _originalPosition = 0;
        _isCompleteNumber = false;
        _isExpectingCountryCode = false;
        _possibleFormats.Clear();
        _shouldAddSpaceAfterNationalPrefix = false;
        _currentMetadata = _defaultMetadata;
    }

    /// <summary>Adds a typed character and returns the number formatted so far.</summary>
    /// <param name="next">The character typed. Digits of any script count as digits; a plus sign counts only as the first character.</param>
    /// <returns>The text to show. Once a character that is not part of a number has been typed, the whole input exactly as typed.</returns>
    public string InputDigit(char next)
    {
        _currentOutput = Input(next, false);
        return _currentOutput;
    }

    /// <summary>Adds a typed character, as <see cref="InputDigit"/> does, and remembers where it ends up in the formatted text.</summary>
    /// <param name="next">The character typed.</param>
    /// <returns>The text to show. Read <see cref="RememberedPosition"/> afterwards for the position of <paramref name="next"/> in it.</returns>
    public string InputDigitAndRememberPosition(char next)
    {
        _currentOutput = Input(next, true);
        return _currentOutput;
    }

    private RegionMetadata? MetadataFor(Region region)
    {
        // A region shares its numbering plan with the main region of its calling code, so formatting follows the main region.
        PhoneMetadata data = _parser.Metadata;
        int cc = data.GetCallingCode(region);
        if (cc == 0)
        {
            return null;
        }

        return data.GetByCallingCode(cc)?[0];
    }

    private string Input(char next, bool rememberPosition)
    {
        _accrued.Append(next);
        if (rememberPosition)
        {
            _originalPosition = _accrued.Length;
        }

        if (!IsDigitOrLeadingPlus(next))
        {
            _ableToFormat = false;
            _inputHasFormatting = true;
        }
        else
        {
            next = NormalizeAndAccrue(next, rememberPosition);
        }

        if (!_ableToFormat)
        {
            if (_inputHasFormatting)
            {
                return _accrued.ToString();
            }

            if (AttemptToExtractIdd())
            {
                if (AttemptToExtractCountryCode())
                {
                    return AttemptToChoosePatternWithPrefixExtracted();
                }
            }
            else if (AbleToExtractLongerNationalPrefix())
            {
                _prefixBeforeNationalNumber.Append(SeparatorBeforeNationalNumber);
                return AttemptToChoosePatternWithPrefixExtracted();
            }

            return _accrued.ToString();
        }

        switch (_accruedDigits.Length)
        {
            case 0:
            case 1:
            case 2:
                return _accrued.ToString();
            case 3:
                if (AttemptToExtractIdd())
                {
                    _isExpectingCountryCode = true;
                }
                else
                {
                    // No international prefix: the user may be typing a national number.
                    _extractedNationalPrefix = RemoveNationalPrefixFromNationalNumber();
                    return AttemptToChooseFormattingPattern();
                }

                goto default;
            default:
                if (_isExpectingCountryCode)
                {
                    if (AttemptToExtractCountryCode())
                    {
                        _isExpectingCountryCode = false;
                    }

                    return _prefixBeforeNationalNumber.ToString() + _nationalNumber;
                }

                if (_possibleFormats.Count > 0)
                {
                    string temp = InputDigitHelper(next);
                    string formatted = AttemptToFormatAccruedDigits();
                    if (formatted.Length > 0)
                    {
                        return formatted;
                    }

                    NarrowDownPossibleFormats(_nationalNumber.ToString());
                    if (MaybeCreateNewTemplate())
                    {
                        return InputAccruedNationalNumber();
                    }

                    return _ableToFormat ? AppendNationalNumber(temp) : _accrued.ToString();
                }

                return AttemptToChooseFormattingPattern();
        }
    }

    private bool IsDigitOrLeadingPlus(char c) =>
        char.IsDigit(c) || (_accrued.Length == 1 && (c == '+' || c == '\uFF0B'));

    private char NormalizeAndAccrue(char c, bool rememberPosition)
    {
        char normalized;
        if (c == '+')
        {
            normalized = c;
            _accruedDigits.Append(c);
        }
        else
        {
            // A full-width plus sign is not a digit; it is carried as a NUL, which no format can use.
            int digit = CharUnicodeInfo.GetDecimalDigitValue(c);
            normalized = digit < 0 ? '\0' : (char)('0' + digit);
            _accruedDigits.Append(normalized);
            _nationalNumber.Append(normalized);
        }

        if (rememberPosition)
        {
            _positionToRemember = _accruedDigits.Length;
        }

        return normalized;
    }

    private void GetAvailableFormats(string leadingDigits)
    {
        RegionMetadata? md = _currentMetadata;
        if (md is null)
        {
            NarrowDownPossibleFormats(leadingDigits);
            return;
        }

        NumberFormatRule[] list = _isCompleteNumber && md.IntlNumberFormats.Length > 0 ? md.IntlNumberFormats : md.NumberFormats;
        // Gabon has a national prefix pattern for parsing but no prefix of its own, and the reference treats it as a region with one.
        bool nationalPrefixUsed = !string.IsNullOrEmpty(md.NationalPrefix) || md.NationalPrefixForParsing is not null;
        bool prefixTyped = _nationalPrefixTaken;
        foreach (NumberFormatRule format in list)
        {
            if (EligibleFormat.IsMatch(format.Format) && IsUsable(format, nationalPrefixUsed, prefixTyped))
            {
                _possibleFormats.Add(format);
            }
        }

        NarrowDownPossibleFormats(leadingDigits);
    }

    // Observed against the reference. In a region with a national prefix, a number typed without the prefix may use formats that
    // need none (the rule is empty or only places the first group) or that make it optional; a number typed with the prefix may use
    // formats that make it optional or that spell a prefix rule out (for the national number or for a carrier code). A number with an international prefix may use any format.
    private bool IsUsable(NumberFormatRule format, bool nationalPrefixUsed, bool prefixTyped)
    {
        if (!nationalPrefixUsed || _isCompleteNumber || format.NationalPrefixOptionalWhenFormatting)
        {
            return true;
        }

        return prefixTyped
            ? !string.IsNullOrEmpty(format.NationalPrefixFormattingRule) || !string.IsNullOrEmpty(format.CarrierCodeFormattingRule)
            : FormattingRuleHasFirstGroupOnly(format.NationalPrefixFormattingRule);
    }

    private static bool FormattingRuleHasFirstGroupOnly(string? rule) =>
        string.IsNullOrEmpty(rule) || FirstGroupOnly.IsMatch(rule);

    private void NarrowDownPossibleFormats(string leadingDigits)
    {
        int index = leadingDigits.Length - MinLeadingDigitsLength;
        _possibleFormats.RemoveAll(format =>
        {
            if (format.LeadingDigits.Length == 0)
            {
                return false;
            }

            int last = Math.Min(index, format.LeadingDigits.Length - 1);
            return !format.LeadingDigits[last].Get().LookingAt(leadingDigits, out _);
        });
    }

    private bool MaybeCreateNewTemplate()
    {
        for (int i = 0; i < _possibleFormats.Count; i++)
        {
            NumberFormatRule format = _possibleFormats[i];
            string pattern = format.Pattern.Source;
            if (_currentFormattingPattern == pattern)
            {
                return false;
            }

            if (CreateFormattingTemplate(format))
            {
                _currentFormattingPattern = pattern;
                _shouldAddSpaceAfterNationalPrefix = format.NationalPrefixFormattingRule is { } r && NationalPrefixSeparators.Find(r, 0, default, out _, out _);
                _lastMatchPosition = 0;
                return true;
            }

            _possibleFormats.RemoveAt(i);
            i--;
        }

        _ableToFormat = false;
        return false;
    }

    private bool CreateFormattingTemplate(NumberFormatRule format)
    {
        _template.Clear();
        string template = GetFormattingTemplate(format);
        if (template.Length > 0)
        {
            _template.Append(template);
            return true;
        }

        return false;
    }

    private string GetFormattingTemplate(NumberFormatRule format)
    {
        // Run the pattern over the longest number there is, to find how many digits the format can take, then spell the format
        // out with a placeholder for each of them.
        const string Longest = "999999999999999";
        PhoneRegex pattern = format.Pattern.Get();
        // Bounds: 24 integers, room for 11 capture groups plus the whole match; callers refuse patterns with more.
        Span<int> groups = stackalloc int[2 * 12];
        if (pattern.GroupCount >= 12 || !pattern.Find(Longest, 0, groups, out int start, out int end))
        {
            return "";
        }

        string sample = Longest[start..end];
        if (sample.Length < _nationalNumber.Length)
        {
            return "";
        }

        string spelled = ApplyFormat(pattern, format.Format, sample);
        return spelled.Replace('9', DigitPlaceholder);
    }

    private static string ApplyFormat(PhoneRegex pattern, string format, string input)
    {
        // Bounds: 24 integers, room for 11 capture groups plus the whole match; callers refuse patterns with more.
        Span<int> groups = stackalloc int[2 * 12];
        if (pattern.GroupCount >= 12 || !pattern.Find(input, 0, groups, out int start, out int end))
        {
            return "";
        }

        StringBuilder sb = new();
        for (int i = 0; i < format.Length; i++)
        {
            char c = format[i];
            if (c == '$' && i + 1 < format.Length && char.IsAsciiDigit(format[i + 1]))
            {
                int g = format[++i] - '0';
                if (g <= pattern.GroupCount && groups[2 * g] >= 0)
                {
                    sb.Append(input, groups[2 * g], groups[(2 * g) + 1] - groups[2 * g]);
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        // Text outside the match stays as it was (a number format matches whole numbers, so there is normally none).
        return string.Concat(input.AsSpan(0, start), sb.ToString(), input.AsSpan(end));
    }

    private string InputAccruedNationalNumber()
    {
        int length = _nationalNumber.Length;
        if (length > 0)
        {
            string temp = "";
            for (int i = 0; i < length; i++)
            {
                temp = InputDigitHelper(_nationalNumber[i]);
            }

            return _ableToFormat ? AppendNationalNumber(temp) : _accrued.ToString();
        }

        return _prefixBeforeNationalNumber.ToString();
    }

    private string AppendNationalNumber(string nationalNumber)
    {
        int prefixLength = _prefixBeforeNationalNumber.Length;
        if (_shouldAddSpaceAfterNationalPrefix && prefixLength > 0 && _prefixBeforeNationalNumber[prefixLength - 1] != SeparatorBeforeNationalNumber)
        {
            return _prefixBeforeNationalNumber.ToString() + SeparatorBeforeNationalNumber + nationalNumber;
        }

        return _prefixBeforeNationalNumber.ToString() + nationalNumber;
    }

    private string InputDigitHelper(char next)
    {
        int at = -1;
        for (int i = _lastMatchPosition; i < _template.Length; i++)
        {
            if (_template[i] == DigitPlaceholder)
            {
                at = i;
                break;
            }
        }

        if (at >= 0)
        {
            _template[at] = next;
            _lastMatchPosition = at;
            return _template.ToString(0, at + 1);
        }

        // More digits than the template can take: with no other format to try, formatting stops.
        if (_possibleFormats.Count == 1)
        {
            _ableToFormat = false;
        }

        _currentFormattingPattern = null;
        return _accrued.ToString();
    }

    private string AttemptToFormatAccruedDigits()
    {
        string national = _nationalNumber.ToString();
        // Bounds: 24 integers, room for 11 capture groups plus the whole match; callers refuse patterns with more.
        Span<int> groups = stackalloc int[2 * 12];
        foreach (NumberFormatRule format in _possibleFormats)
        {
            PhoneRegex pattern = format.Pattern.Get();
            if (pattern.GroupCount < 12 && pattern.Match(national, groups))
            {
                _shouldAddSpaceAfterNationalPrefix = format.NationalPrefixFormattingRule is { } r && NationalPrefixSeparators.Find(r, 0, default, out _, out _);
                return AppendNationalNumber(ApplyFormat(pattern, format.Format, national));
            }
        }

        return "";
    }

    private string AttemptToChooseFormattingPattern()
    {
        if (_nationalNumber.Length >= MinLeadingDigitsLength)
        {
            GetAvailableFormats(_nationalNumber.ToString(0, MinLeadingDigitsLength));
            if (MaybeCreateNewTemplate())
            {
                return InputAccruedNationalNumber();
            }

            return _ableToFormat ? AppendNationalNumber(_nationalNumber.ToString()) : _accrued.ToString();
        }

        return AppendNationalNumber(_nationalNumber.ToString());
    }

    private string AttemptToChoosePatternWithPrefixExtracted()
    {
        _ableToFormat = true;
        _isExpectingCountryCode = false;
        _possibleFormats.Clear();
        _lastMatchPosition = 0;
        _template.Clear();
        _currentFormattingPattern = null;
        return AttemptToChooseFormattingPattern();
    }

    private bool AbleToExtractLongerNationalPrefix()
    {
        if (_extractedNationalPrefix.Length > 0)
        {
            // Put the prefix back to try again, and take it out of the text before the number; it may follow the calling code, as in +44 (0)20.
            _nationalNumber.Insert(0, _extractedNationalPrefix);
            string before = _prefixBeforeNationalNumber.ToString();
            int previous = before.LastIndexOf(_extractedNationalPrefix, StringComparison.Ordinal);
            _prefixBeforeNationalNumber.Length = previous;
        }

        return !string.Equals(_extractedNationalPrefix, RemoveNationalPrefixFromNationalNumber(), StringComparison.Ordinal);
    }

    private bool IsNanpaNumberWithNationalPrefix()
    {
        if (_currentMetadata is not { CountryCode: 1 } || _nationalNumber.Length < 2)
        {
            return false;
        }

        return _nationalNumber[0] == '1' && _nationalNumber[1] != '0' && _nationalNumber[1] != '1';
    }

    private string RemoveNationalPrefixFromNationalNumber()
    {
        int start = 0;
        if (IsNanpaNumberWithNationalPrefix())
        {
            start = 1;
            _prefixBeforeNationalNumber.Append('1').Append(SeparatorBeforeNationalNumber);
        }
        else if (_currentMetadata?.NationalPrefixForParsing is { } forParsing)
        {
            // Some national prefix patterns are entirely optional, so check that a prefix was actually found.
            if (forParsing.Get().LookingAt(_nationalNumber.ToString(), out int length) && length > 0)
            {
                start = length;
                _prefixBeforeNationalNumber.Append(_nationalNumber.ToString(0, start));
            }
        }

        string prefix = _nationalNumber.ToString(0, start);
        _nationalPrefixTaken = start > 0;
        _nationalNumber.Remove(0, start);
        return prefix;
    }

    private bool AttemptToExtractIdd()
    {
        // A plus sign always counts as the international prefix, besides the region's own.
        string digits = _accruedDigits.ToString();
        int end;
        if (digits.StartsWith('+'))
        {
            end = 1;
        }
        else if (_currentMetadata?.InternationalPrefixPattern is { } idd && idd.Get().LookingAt(digits, out int length))
        {
            end = length;
        }
        else
        {
            return false;
        }

        // Alternation order matters in the reference: a region prefix that matches before "+" cannot, since "+" only starts the input.
        _isCompleteNumber = true;
        _nationalNumber.Clear();
        _nationalNumber.Append(digits, end, digits.Length - end);
        _prefixBeforeNationalNumber.Clear();
        _prefixBeforeNationalNumber.Append(digits, 0, end);
        if (digits[0] != '+')
        {
            _prefixBeforeNationalNumber.Append(SeparatorBeforeNationalNumber);
        }

        return true;
    }

    private bool AttemptToExtractCountryCode()
    {
        if (_nationalNumber.Length == 0)
        {
            return false;
        }

        string national = _nationalNumber.ToString();
        int countryCode = ExtractCountryCode(national, out string rest);
        if (countryCode == 0)
        {
            return false;
        }

        _nationalNumber.Clear();
        _nationalNumber.Append(rest);
        PhoneMetadata data = _parser.Metadata;
        RegionMetadata[]? regions = data.GetByCallingCode(countryCode);
        RegionMetadata? target = regions?[0];
        if (target is not null && (target.Id == RegionMetadata.NonGeographicId || !string.Equals(target.Id, MainRegionId(), StringComparison.Ordinal)))
        {
            _currentMetadata = target;
        }

        _prefixBeforeNationalNumber.Append(countryCode.ToString(CultureInfo.InvariantCulture)).Append(SeparatorBeforeNationalNumber);
        // A national prefix found before the calling code no longer applies.
        _extractedNationalPrefix = "";
        _nationalPrefixTaken = false;
        return true;
    }

    private string MainRegionId() => _defaultRegion.IsUnknown ? "" : _defaultRegion.Code;

    private int ExtractCountryCode(string number, out string rest)
    {
        rest = "";
        if (number.Length == 0 || number[0] == '0')
        {
            return 0;
        }

        for (int i = 1; i <= PhoneText.MaxLengthCountryCode && i <= number.Length; i++)
        {
            int code = int.Parse(number.AsSpan(0, i), NumberStyles.None, CultureInfo.InvariantCulture);
            if (_parser.Metadata.HasCallingCode(code))
            {
                rest = number[i..];
                return code;
            }
        }

        return 0;
    }
}
