using System.Buffers;
using System.Text;
using RecursiveAI.OneContact.Phones.Metadata;

namespace RecursiveAI.OneContact.Phones;

/// <summary>
/// The phone engine: parses text into <see cref="PhoneNumber"/> values and answers questions about them (validity, type, region,
/// possible lengths, every format, examples) from an explicit <see cref="PhoneMetadata"/>. The <see cref="PhoneNumber"/> members use
/// <see cref="Default"/>; construct a parser over your own metadata to use newer or customised numbering plans.
/// </summary>
/// <remarks>Instances are immutable and safe to share between threads. Nothing here reads the current culture.</remarks>
public sealed partial class PhoneNumberParser
{
    private static readonly Lazy<PhoneNumberParser> DefaultInstance = new(
        () => new PhoneNumberParser(PhoneMetadata.Default),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly PhoneMetadata _metadata;

    /// <summary>Creates a parser over the given numbering-plan data.</summary>
    /// <param name="metadata">The data.</param>
    /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
    public PhoneNumberParser(PhoneMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        _metadata = metadata;
    }

    /// <summary>Gets the parser over <see cref="PhoneMetadata.Default"/>.</summary>
    public static PhoneNumberParser Default => DefaultInstance.Value;

    /// <summary>Gets the data this parser works from.</summary>
    public PhoneMetadata Metadata => _metadata;

    internal static OperationStatus TryParse(ReadOnlySpan<char> input, Region defaultRegion, out PhoneNumber value, out ParseDetail detail) =>
        Default.TryParse(input, defaultRegion, false, out value, out detail);

    internal static OperationStatus TryParse(string input, Region defaultRegion, out PhoneNumber value, out ParseDetail detail) =>
        Default.TryParseCore(input, defaultRegion, input, out value, out detail);

    /// <summary>Parses a phone number.</summary>
    /// <param name="input">The text. Surrounding white space, and any text before the first digit or plus sign, is ignored.</param>
    /// <param name="defaultRegion">The region that national numbers belong to; <see cref="Region.Unknown"/> accepts only numbers that start with a plus sign.</param>
    /// <param name="keepRawInput">Whether to keep the text on the result, which <see cref="PhoneNumber.FormatInOriginalFormat"/> and the carrier code need. This copies the text.</param>
    /// <param name="value">The number, or the default value when the status is not <see cref="OperationStatus.Done"/>.</param>
    /// <param name="detail">The reason for a failure, the extent of the number in the input and what was found.</param>
    /// <returns><see cref="OperationStatus.Done"/>, or <see cref="OperationStatus.InvalidData"/> with the reason in <paramref name="detail"/>.</returns>
    /// <remarks>
    /// Parsing checks that the text has the shape of a number and that its length is plausible; it does not check validity. Use
    /// <see cref="IsValid"/> for that. Letters are read as keypad digits when there are three or more of them.
    /// </remarks>
    public OperationStatus TryParse(ReadOnlySpan<char> input, Region defaultRegion, bool keepRawInput, out PhoneNumber value, out ParseDetail detail) =>
        TryParseCore(input.ToString(), defaultRegion, keepRawInput ? input.ToString() : null, out value, out detail);

    /// <summary>Parses a batch of texts with one default region; results are in input order.</summary>
    /// <param name="inputs">The texts.</param>
    /// <param name="defaultRegion">The region that national numbers belong to.</param>
    /// <param name="keepRawInput">Whether to keep each text on its result.</param>
    /// <returns>One result per input, lazily.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="inputs"/> is <see langword="null"/>.</exception>
    public IEnumerable<PhoneParseResult> ParseMany(IEnumerable<string> inputs, Region defaultRegion, bool keepRawInput = false)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        return ParseManyCore(inputs, defaultRegion, keepRawInput);
    }

    private IEnumerable<PhoneParseResult> ParseManyCore(IEnumerable<string> inputs, Region defaultRegion, bool keepRawInput)
    {
        foreach (string input in inputs)
        {
            OperationStatus status = TryParseCore(input ?? "", defaultRegion, keepRawInput ? input : null, out PhoneNumber value, out ParseDetail detail);
            yield return new PhoneParseResult(status, value, detail);
        }
    }

    private OperationStatus TryParseCore(string input, Region defaultRegion, string? rawInput, out PhoneNumber value, out ParseDetail detail)
    {
        ReasonCode reason = ParseNumber(input, defaultRegion, rawInput, out value, out SourceSpan span);
        if (!reason.IsNone)
        {
            value = default;
            detail = ParseDetail.Failure(reason, span);
            return OperationStatus.InvalidData;
        }

        detail = new ParseDetail([], [new ParsedField("number", value.Source, 1f, DecisionSource.Rule)]);
        return OperationStatus.Done;
    }

    private ReasonCode ParseNumber(string input, Region defaultRegion, string? rawInput, out PhoneNumber value, out SourceSpan errorSpan)
    {
        value = default;
        errorSpan = new SourceSpan(0, input.Length);
        if (input.AsSpan().Trim().IsEmpty)
        {
            return ReasonCode.Empty;
        }

        if (input.Length > PhoneText.MaxInputLength)
        {
            return ReasonCode.PhoneTooLong;
        }

        // Building the number text: an RFC 3966 phone-context contributes its digits; otherwise the possible number is cut out of the text.
        string national;
        SourceSpan numberSpan;
        string? phoneContext = ExtractPhoneContext(input);
        if (!IsPhoneContextValid(phoneContext))
        {
            return ReasonCode.PhoneNotANumber;
        }

        if (phoneContext is not null)
        {
            StringBuilder sb = new();
            if (phoneContext[0] == '+')
            {
                sb.Append(phoneContext);
            }

            int tel = input.IndexOf("tel:", StringComparison.Ordinal);
            int begin = tel >= 0 ? tel + 4 : 0;
            int contextAt = input.IndexOf(";phone-context=", StringComparison.Ordinal);
            if (contextAt >= begin)
            {
                sb.Append(input, begin, contextAt - begin);
            }

            national = sb.ToString();
            ReadOnlySpan<char> trimmed = input.AsSpan().Trim();
            numberSpan = new SourceSpan(input.Length - input.AsSpan().TrimStart().Length, trimmed.Length);
        }
        else
        {
            ReadOnlySpan<char> possible = PhoneText.ExtractPossibleNumber(input, out int possibleStart);
            national = possible.ToString();
            numberSpan = new SourceSpan(possibleStart, possible.Length);
        }

        // The ISDN sub-address and everything after it is not part of the number.
        int isub = national.IndexOf(";isub=", StringComparison.Ordinal);
        if (isub > 0)
        {
            national = national[..isub];
        }

        if (!PhoneText.IsViable(national))
        {
            errorSpan = numberSpan;
            return ReasonCode.PhoneNotANumber;
        }

        bool startsWithPlus = national.Length > 0 && national[0] is '+' or '\uFF0B';
        RegionMetadata? regionMetadata = _metadata.GetRegionMetadata(defaultRegion);
        if (regionMetadata is null && !startsWithPlus)
        {
            return defaultRegion.IsUnknown ? ReasonCode.PhoneRegionRequired : ReasonCode.PhoneUnknownRegion;
        }

        bool vanity = PhoneText.ValidAlpha.IsMatch(national);
        string extension = PhoneText.StripExtension(national, out int cut);
        national = national[..cut];

        CountryCodeSource source = CountryCodeSource.FromDefaultCountry;
        ReasonCode reason = MaybeExtractCountryCode(national, regionMetadata, out string nsn, out int countryCode, ref source);
        if (!reason.IsNone)
        {
            if (reason == ReasonCode.PhoneInvalidCountryCode && startsWithPlus)
            {
                // Strip the plus signs and try again: the digits may carry an international dialling prefix after them.
                int plusEnd = 0;
                while (plusEnd < national.Length && national[plusEnd] is '+' or '\uFF0B')
                {
                    plusEnd++;
                }

                reason = MaybeExtractCountryCode(national[plusEnd..], regionMetadata, out nsn, out countryCode, ref source);
                if (reason.IsNone && countryCode == 0)
                {
                    reason = ReasonCode.PhoneInvalidCountryCode;
                }
            }

            if (!reason.IsNone)
            {
                errorSpan = numberSpan;
                return reason;
            }
        }

        if (countryCode != 0)
        {
            RegionMetadata[]? sharing = _metadata.GetByCallingCode(countryCode);
            RegionMetadata main = sharing![0];
            if (regionMetadata is null || main.Id != regionMetadata.Id)
            {
                regionMetadata = main;
            }
        }
        else
        {
            // No calling code in the text: the number is national to the default region.
            nsn = PhoneText.Normalize(national);
            countryCode = regionMetadata!.CountryCode;
        }

        if (nsn.Length < PhoneText.MinLengthForNsn)
        {
            errorSpan = numberSpan;
            return ReasonCode.PhoneTooShort;
        }

        string? carrier = null;
        {
            string potential = nsn;
            MaybeStripNationalPrefixAndCarrierCode(ref potential, regionMetadata, out string? carrierCode);

            // The stripped form is used only when it is still a plausible length; otherwise the original may be a short number.
            PhonePossibility check = TestNumberLength(potential, regionMetadata, PhoneNumberType.Unknown);
            if (check is not (PhonePossibility.TooShort or PhonePossibility.IsPossibleLocalOnly or PhonePossibility.InvalidLength))
            {
                nsn = potential;
                if (rawInput is not null && !string.IsNullOrEmpty(carrierCode))
                {
                    carrier = carrierCode;
                }
            }
        }

        if (nsn.Length < PhoneText.MinLengthForNsn)
        {
            errorSpan = numberSpan;
            return ReasonCode.PhoneTooShort;
        }

        if (nsn.Length > PhoneText.MaxLengthForNsn)
        {
            errorSpan = numberSpan;
            return ReasonCode.PhoneTooLong;
        }

        // Leading zeros are part of the number in some plans (Italy) and must survive the integer.
        int zeros = 0;
        if (nsn.Length > 1 && nsn[0] == '0')
        {
            zeros = 1;
            while (zeros < nsn.Length - 1 && nsn[zeros] == '0')
            {
                zeros++;
            }
        }

        ulong nationalNumber = ulong.Parse(nsn.AsSpan(zeros), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture);
        PhoneFacts flags = PhoneFacts.None;
        if (extension.Length > 0)
        {
            flags |= PhoneFacts.HasExtension;
        }

        if (vanity)
        {
            flags |= PhoneFacts.Vanity;
        }

        if (source is CountryCodeSource.FromNumberWithPlusSign or CountryCodeSource.FromNumberWithIdd)
        {
            flags |= PhoneFacts.InternationalInput;
        }

        PhoneNumber number = new(
            countryCode,
            nationalNumber,
            zeros,
            extension,
            rawInput,
            rawInput is null ? CountryCodeSource.Unspecified : source,
            carrier,
            numberSpan,
            flags);
        RegionMetadata? located = RegionMetadataFor(number);
        if (located is not null && TypeOf(GetNsn(number), located) != PhoneNumberType.Unknown)
        {
            flags |= PhoneFacts.Valid;
            if (located.Id == RegionMetadata.NonGeographicId)
            {
                flags |= PhoneFacts.NonGeographic;
            }

            number = new PhoneNumber(countryCode, nationalNumber, zeros, extension, rawInput, rawInput is null ? CountryCodeSource.Unspecified : source, carrier, numberSpan, flags);
        }

        value = number;
        return default;
    }

    // RFC 3966 section 5.1.2: the phone-context parameter is either a global number (digits after a plus) or a domain name.
    private static string? ExtractPhoneContext(string input)
    {
        int at = input.IndexOf(";phone-context=", StringComparison.Ordinal);
        if (at < 0)
        {
            return null;
        }

        int start = at + ";phone-context=".Length;
        if (start >= input.Length)
        {
            return "";
        }

        int end = input.IndexOf(';', start);
        return end >= 0 ? input[start..end] : input[start..];
    }

    private static bool IsPhoneContextValid(string? context)
    {
        if (context is null)
        {
            return true;
        }

        if (context.Length == 0)
        {
            return false;
        }

        return PhoneText.GlobalNumberDigits.IsMatch(context) || PhoneText.DomainName.IsMatch(context);
    }

    private ReasonCode MaybeExtractCountryCode(string number, RegionMetadata? region, out string nationalNumber, out int countryCode, ref CountryCodeSource source)
    {
        nationalNumber = "";
        countryCode = 0;
        if (number.Length == 0)
        {
            return default;
        }

        string full = number;
        source = StripInternationalPrefixAndNormalize(ref full, region?.InternationalPrefixPattern);
        if (source != CountryCodeSource.FromDefaultCountry)
        {
            if (full.Length <= PhoneText.MinLengthForNsn)
            {
                return ReasonCode.PhoneTooShortAfterIdd;
            }

            int potential = ExtractCountryCode(full, out string rest);
            if (potential != 0)
            {
                nationalNumber = rest;
                countryCode = potential;
                return default;
            }

            return ReasonCode.PhoneInvalidCountryCode;
        }

        if (region is not null)
        {
            // The text may start with the default region's own calling code; keep it that way only when that makes a better number.
            string codeText = region.CountryCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (full.StartsWith(codeText, StringComparison.Ordinal))
            {
                string potentialNational = full[codeText.Length..];
                Desc general = region.GetDesc(DescKind.General);
                MaybeStripNationalPrefixAndCarrierCode(ref potentialNational, region, out _);
                if ((!MatchesEntirely(general, full) && MatchesEntirely(general, potentialNational))
                    || TestNumberLength(full, region, PhoneNumberType.Unknown) == PhonePossibility.TooLong)
                {
                    nationalNumber = potentialNational;
                    source = CountryCodeSource.FromNumberWithoutPlusSign;
                    countryCode = region.CountryCode;
                    return default;
                }
            }
        }

        return default;
    }

    private static CountryCodeSource StripInternationalPrefixAndNormalize(ref string number, LazyPattern? iddPattern)
    {
        if (number.Length == 0)
        {
            return CountryCodeSource.FromDefaultCountry;
        }

        if (number[0] is '+' or '\uFF0B')
        {
            int end = 0;
            while (end < number.Length && number[end] is '+' or '\uFF0B')
            {
                end++;
            }

            number = PhoneText.Normalize(number.AsSpan(end));
            return CountryCodeSource.FromNumberWithPlusSign;
        }

        number = PhoneText.Normalize(number);
        if (iddPattern is not null && iddPattern.Get().LookingAt(number, out int matchEnd))
        {
            // A calling code never starts with 0, so an IDD followed by 0 is not stripped.
            if (matchEnd < number.Length && number[matchEnd] == '0')
            {
                return CountryCodeSource.FromDefaultCountry;
            }

            number = number[matchEnd..];
            return CountryCodeSource.FromNumberWithIdd;
        }

        return CountryCodeSource.FromDefaultCountry;
    }

    // E.164: calling codes are one to three digits and never start with 0.
    private int ExtractCountryCode(string full, out string rest)
    {
        rest = "";
        if (full.Length == 0 || full[0] == '0')
        {
            return 0;
        }

        for (int i = 1; i <= PhoneText.MaxLengthCountryCode && i <= full.Length; i++)
        {
            int candidate = int.Parse(full.AsSpan(0, i), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture);
            if (_metadata.HasCallingCode(candidate))
            {
                rest = full[i..];
                return candidate;
            }
        }

        return 0;
    }

    private static bool MatchesEntirely(Desc desc, string number) =>
        desc.Pattern is not null && desc.Pattern.Get().IsMatch(number);

    private bool MaybeStripNationalPrefixAndCarrierCode(ref string number, RegionMetadata region, out string? carrierCode)
    {
        carrierCode = null;
        if (number.Length == 0 || region.NationalPrefixForParsing is null)
        {
            return false;
        }

        PhoneRegex prefix = region.NationalPrefixForParsing.Get();
        // Bounds: at most 2 * (32 + 1) integers.
        Span<int> g = stackalloc int[2 * (prefix.GroupCount + 1)];
        if (!prefix.MatchPrefix(number, g))
        {
            return false;
        }

        Desc general = region.GetDesc(DescKind.General);
        bool viableOriginal = MatchesEntirely(general, number);
        int groups = prefix.GroupCount;
        int matchEnd = g[1];
        string? transform = region.NationalPrefixTransformRule;
        bool lastGroupSet = groups > 0 && g[2 * groups] >= 0;
        if (string.IsNullOrEmpty(transform) || !lastGroupSet)
        {
            // Nothing was captured for a transformation: the prefix is simply removed.
            if (viableOriginal && !MatchesEntirely(general, number[matchEnd..]))
            {
                return false;
            }

            if (groups > 0 && lastGroupSet)
            {
                carrierCode = number[g[2]..g[3]];
            }

            number = number[matchEnd..];
            return true;
        }

        StringBuilder transformed = new();
        ExpandReplacement(transform, number, g, groups, transformed);
        transformed.Append(number, matchEnd, number.Length - matchEnd);
        string result = transformed.ToString();
        if (viableOriginal && !MatchesEntirely(general, result))
        {
            return false;
        }

        if (groups > 1)
        {
            carrierCode = g[2] >= 0 ? number[g[2]..g[3]] : null;
        }

        number = result;
        return true;
    }

    // Replacement text with $1..$9 group references and backslash escapes.
    private static void ExpandReplacement(string template, string input, ReadOnlySpan<int> groups, int groupCount, StringBuilder output)
    {
        for (int i = 0; i < template.Length; i++)
        {
            char c = template[i];
            if (c == '\\' && i + 1 < template.Length)
            {
                output.Append(template[++i]);
            }
            else if (c == '$' && i + 1 < template.Length && char.IsAsciiDigit(template[i + 1]))
            {
                int g = template[++i] - '0';
                while (i + 1 < template.Length && char.IsAsciiDigit(template[i + 1]) && (g * 10) + (template[i + 1] - '0') <= groupCount)
                {
                    g = (g * 10) + (template[++i] - '0');
                }

                if (g <= groupCount && groups[2 * g] >= 0)
                {
                    output.Append(input, groups[2 * g], groups[(2 * g) + 1] - groups[2 * g]);
                }
            }
            else
            {
                output.Append(c);
            }
        }
    }
}

/// <summary>The outcome of parsing one text in a batch.</summary>
/// <param name="Status"><see cref="OperationStatus.Done"/> or <see cref="OperationStatus.InvalidData"/>.</param>
/// <param name="Number">The number; the default value when parsing failed.</param>
/// <param name="Detail">What was found, or why parsing failed.</param>
public readonly record struct PhoneParseResult(OperationStatus Status, PhoneNumber Number, ParseDetail Detail);
